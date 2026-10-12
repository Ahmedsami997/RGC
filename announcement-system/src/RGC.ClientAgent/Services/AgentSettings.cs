using System.IO;
using System.Text.Json;

namespace RGC.ClientAgent.Services;

public sealed class AgentSettings
{
    /// <summary>Used when no settings file can be read, so a damaged file doesn't take the PC offline.</summary>
    public const string DefaultServerUrl = "https://rgc-announcements-ayakghhpdhfkbydr.uaenorth-01.azurewebsites.net";

    public string ServerUrl { get; set; } = DefaultServerUrl;
    public string AgentKey { get; set; } = "";
    public int CountdownSeconds { get; set; } = 10;
    public bool AutoStart { get; set; } = true;
    public bool AllowUserExit { get; set; }
    /// <summary>Cover the screen and block app switching until the announcement is acknowledged.</summary>
    public bool LockScreen { get; set; } = true;

    /// <summary>
    /// Loads agentsettings.json from the install folder, then lets
    /// %ProgramData%\RGC\agentsettings.json override it (handy for GPO/Intune deployment).
    /// </summary>
    public static AgentSettings Load()
    {
        var settings = new AgentSettings();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

        foreach (var path in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "agentsettings.json"),
                     Path.Combine(AgentPaths.MachineDataDir, "agentsettings.json")
                 })
        {
            if (!File.Exists(path)) continue;
            try
            {
                // A file cut short by a power cut or crash can be empty or full of zero bytes: skip it.
                var json = File.ReadAllText(path).Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');
                if (json.Length == 0)
                {
                    AgentLog.Error($"Settings file {path} is empty or damaged; ignoring it");
                    continue;
                }
                var loaded = JsonSerializer.Deserialize<AgentSettings>(json, options);
                if (loaded is not null) settings = loaded;
            }
            catch (Exception ex)
            {
                AgentLog.Error($"Could not read settings file {path}", ex);
            }
        }

        settings.CountdownSeconds = Math.Clamp(settings.CountdownSeconds, 0, 300);
        if (string.IsNullOrWhiteSpace(settings.ServerUrl)) settings.ServerUrl = DefaultServerUrl;
        settings.ServerUrl = settings.ServerUrl.TrimEnd('/');
        return settings;
    }
}

public static class AgentPaths
{
    public static string MachineDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RGC");

    public static string UserDataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RGC");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string LogDir
    {
        get
        {
            var dir = Path.Combine(UserDataDir, "Logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
