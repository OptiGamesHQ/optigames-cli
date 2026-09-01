namespace OptiGames.Core.Services;

/// <summary>
/// Every file the tool writes lives under %LOCALAPPDATA%\OptiGamesTool — downloaded payloads,
/// the extracted NVIDIA inspector, and the first-run marker.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OptiGamesTool");

    public static string FirstRunMarker => Path.Combine(Root, "onboarded.marker");
    public static string NvidiaInspector => Path.Combine(Root, "inspector.exe");

    /// <summary>Creates the root if needed and returns it.</summary>
    public static string Ensure()
    {
        Directory.CreateDirectory(Root);
        return Root;
    }

    public static string File(string name) => Path.Combine(Ensure(), name);

    public static bool HasOnboarded => System.IO.File.Exists(FirstRunMarker);

    public static void MarkOnboarded()
        => System.IO.File.WriteAllText(FirstRunMarker, DateTime.Now.ToString("o"));
}
