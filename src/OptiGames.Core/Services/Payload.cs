using System.Reflection;

namespace OptiGames.Core.Services;

/// <summary>
/// Files that ship inside the exe and get written to %LOCALAPPDATA%\OptiGamesTool on demand.
/// Keeping them embedded means the one-line installer stays a single download.
/// </summary>
public static class Payload
{
    private static readonly Assembly Owner = typeof(Payload).Assembly;
    private const string Prefix = "OptiGames.Core.Payloads.";

    public static bool Exists(string name) => Owner.GetManifestResourceInfo(Prefix + name) is not null;

    /// <summary>
    /// Extracts a payload to the tool directory and returns its path. Skips the write when
    /// an identically-sized copy is already there, so repeat launches are instant.
    /// </summary>
    public static string Extract(string name)
    {
        var target = AppPaths.File(name);
        using var src = Owner.GetManifestResourceStream(Prefix + name)
            ?? throw new FileNotFoundException($"Embedded payload '{name}' is missing from the build.");

        if (File.Exists(target) && new FileInfo(target).Length == src.Length)
            return target;

        using var dst = File.Create(target);
        src.CopyTo(dst);
        return target;
    }
}
