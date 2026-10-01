using System.IO;
using System.Text.Json;

namespace RGC.ClientAgent.Services;

public sealed class AgentSettings
{
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string AgentKey { get; set; } = "";
    public int CountdownSeconds { get; set; } = 10;
    public bool AutoStart { get; set; } = true;
    public bool AllowUserExit { get; set; }

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
                var loaded = JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(path), options);
                if (loaded is not null) settings = loaded;
            }
            catch (Exception ex)
            {
                AgentLog.Error($"Could not read settings file {path}", ex);
            }
        }

        settings.CountdownSeconds = Math.Clamp(settings.CountdownSeconds, 0, 300);
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
