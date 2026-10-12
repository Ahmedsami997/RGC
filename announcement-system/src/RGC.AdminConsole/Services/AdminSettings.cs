using System.IO;
using System.Text.Json;

namespace RGC.AdminConsole.Services;

public sealed class AdminSettings
{
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string? LastUsername { get; set; }
    public bool LastSignInWasLocal { get; set; }

    private static string UserFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RGC", "admin-console.json");

    /// <summary>Install-folder defaults, overridden by what this user last used.</summary>
    public static AdminSettings Load()
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var settings = new AdminSettings();
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "adminsettings.json"), UserFile })
        {
            try
            {
                if (File.Exists(path))
                    settings = JsonSerializer.Deserialize<AdminSettings>(File.ReadAllText(path), opts) ?? settings;
            }
            catch { /* fall back to defaults */ }
        }
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
            File.WriteAllText(UserFile, JsonSerializer.Serialize(this));
        }
        catch { /* not critical */ }
    }
}
