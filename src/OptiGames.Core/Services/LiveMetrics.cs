using System.Runtime.InteropServices;

namespace OptiGames.Core.Services;

/// <summary>One reading of overall machine load. Percentages are 0-100.</summary>
public sealed record MetricsSample(
    double CpuPercent,
    double RamPercent,
    ulong RamUsedBytes,
    ulong RamTotalBytes);

/// <summary>
/// Whole-machine CPU and RAM load, sampled on demand.
///
/// Deliberately not System.Diagnostics.PerformanceCounter: that lives in a separate NuGet
/// package on .NET 8, and it also takes hundreds of milliseconds to spin up its first
/// category read. Two kernel32 calls give the same numbers for free.
///
/// Not thread safe — call it from one thread (the UI timer).
/// </summary>
public sealed class LiveMetrics
{
    // GetSystemTimes reports totals accumulated since boot, so a single reading tells you
    // nothing about load right now: after a week of uptime the idle counter is enormous no
    // matter what the machine is doing this second. Usage only falls out of the DIFFERENCE
    // between two readings, which is why the previous one is kept here and why the very
    // first Sample() has to report 0.
    private ulong _prevIdle;
    private ulong _prevTotal;
    private bool _hasPrevious;

    /// <summary>Last good reading, returned verbatim if the interop fails.</summary>
    private MetricsSample _last = new(0, 0, 0, 0);

    public MetricsSample Sample()
    {
        try
        {
            double cpu = SampleCpuPercent();
            var (usedBytes, totalBytes, ramPercent) = SampleMemory();
            _last = new MetricsSample(cpu, ramPercent, usedBytes, totalBytes);
        }
        catch
        {
            // A meter that freezes on its last value is fine; one that takes the app down
            // with it is not. Nothing here is important enough to surface an error for.
        }
        return _last;
    }

    // ---------------------------------------------------------------- CPU

    private double SampleCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return _last.CpuPercent;

        ulong idleTicks = ToTicks(idle);

        // Kernel time already INCLUDES idle time on Windows, so kernel + user is the whole
        // elapsed wall clock across all cores, and busy is simply what is left over.
        ulong totalTicks = ToTicks(kernel) + ToTicks(user);

        if (!_hasPrevious)
        {
            _prevIdle = idleTicks;
            _prevTotal = totalTicks;
            _hasPrevious = true;
            return 0;
        }

        // Unsigned subtraction is safe: these counters only ever increase.
        ulong idleDelta = idleTicks >= _prevIdle ? idleTicks - _prevIdle : 0;
        ulong totalDelta = totalTicks >= _prevTotal ? totalTicks - _prevTotal : 0;

        _prevIdle = idleTicks;
        _prevTotal = totalTicks;

        // Two calls inside the same 15.6 ms scheduler tick see identical totals.
        if (totalDelta == 0) return _last.CpuPercent;

        double busy = (1.0 - (double)idleDelta / totalDelta) * 100.0;
        return Math.Clamp(busy, 0, 100);
    }

    private static ulong ToTicks(FILETIME t)
        => ((ulong)(uint)t.dwHighDateTime << 32) | (uint)t.dwLowDateTime;

    // ---------------------------------------------------------------- RAM

    private (ulong Used, ulong Total, double Percent) SampleMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            return (_last.RamUsedBytes, _last.RamTotalBytes, _last.RamPercent);

        ulong total = status.ullTotalPhys;
        ulong used = total - Math.Min(status.ullAvailPhys, total);

        // dwMemoryLoad is the same figure Task Manager shows, but derive it so used bytes
        // and the percentage can never disagree on screen.
        double percent = Math.Clamp((double)used / total * 100.0, 0, 100);
        return (used, total, percent);
    }

    // ---------------------------------------------------------------- interop

    // Every field below is written by the kernel, never by C#, which the compiler cannot see.
#pragma warning disable CS0649

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

#pragma warning restore CS0649

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
