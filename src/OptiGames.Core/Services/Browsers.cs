namespace OptiGames.Core.Services;

/// <summary>
/// Which browsers are on this machine. Checked once at catalog build time so the debloat
/// tweak only writes policies for software the user actually has.
/// </summary>
public static class Browsers
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static bool BraveInstalled { get; } =
        Directory.Exists(Path.Combine(Local, "BraveSoftware"));

    public static bool ChromeInstalled { get; } =
        Directory.Exists(Path.Combine(Local, "Google", "Chrome"));
}
