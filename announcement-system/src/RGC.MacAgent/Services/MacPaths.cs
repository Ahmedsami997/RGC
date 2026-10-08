namespace RGC.MacAgent.Services;

/// <summary>Per-user locations: ~/Library/Application Support/RGC and ~/Library/Logs/RGC.</summary>
public static class MacPaths
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string DataDir => Ensure(Path.Combine(Home, "Library", "Application Support", "RGC"));
    public static string LogDir => Ensure(Path.Combine(Home, "Library", "Logs", "RGC"));

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
