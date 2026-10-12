using System.Text.Json;

namespace RGC.MacAgent.Services;

public sealed class MacSettings
{
    public string ServerUrl { get; set; } = "https://rgc-announcements-ayakghhpdhfkbydr.uaenorth-01.azurewebsites.net";
    public int CountdownSeconds { get; set; } = 10;
    public bool AllowUserExit { get; set; }

    /// <summary>Defaults, overridden by ~/Library/Application Support/RGC/agentsettings.json when present.</summary>
    public static MacSettings Load()
    {
        var settings = new MacSettings();
        var path = Path.Combine(MacPaths.DataDir, "agentsettings.json");
        if (File.Exists(path))
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
                settings = JsonSerializer.Deserialize<MacSettings>(File.ReadAllText(path), options) ?? settings;
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
