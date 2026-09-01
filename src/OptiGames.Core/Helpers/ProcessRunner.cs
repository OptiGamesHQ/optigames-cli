using System.Diagnostics;

namespace OptiGames.Core.Helpers;

public sealed class ProcessResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
    public bool Success => ExitCode == 0;
}

/// <summary>Runs console tools (powercfg, cleanmgr, inspector) and echoes them to the log.</summary>
public sealed class ProcessRunner
{
    private readonly ILogSink _log;
    public ProcessRunner(ILogSink log) => _log = log;

    public ProcessResult Run(string fileName, string arguments, int timeoutMs = 120_000)
    {
        _log.Write($"> {fileName} {arguments}");
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var p = Process.Start(psi);
            if (p is null)
            {
                _log.Write($"  (failed to start {fileName})");
                return new ProcessResult { ExitCode = -1, StdErr = "process did not start" };
            }

            // Read both streams concurrently — PowerShell can emit enough on stderr to fill
            // the pipe buffer and deadlock a sequential read.
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
                _log.Write($"  (timed out after {timeoutMs / 1000}s)");
                return new ProcessResult { ExitCode = -1, StdErr = "timed out" };
            }

            string outp = outTask.GetAwaiter().GetResult();
            string err = errTask.GetAwaiter().GetResult();

            var trimmedErr = err.Trim();
            if (trimmedErr.Length > 0) _log.Write("  ERR: " + trimmedErr);

            return new ProcessResult { ExitCode = p.ExitCode, StdOut = outp, StdErr = err };
        }
        catch (Exception ex)
        {
            _log.Write($"  EXCEPTION: {ex.Message}");
            return new ProcessResult { ExitCode = -1, StdErr = ex.Message };
        }
    }

    /// <summary>Runs a PowerShell command with profile and execution policy out of the way.</summary>
    public ProcessResult PowerShell(string command, int timeoutMs = 120_000)
        => Run("powershell.exe",
               $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"",
               timeoutMs);
}
