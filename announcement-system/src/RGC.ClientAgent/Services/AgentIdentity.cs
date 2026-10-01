using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using RGC.Shared;

namespace RGC.ClientAgent.Services;

public static class AgentIdentity
{
    /// <summary>
    /// A stable id for this PC. Stored machine-wide when possible so every user on the
    /// PC reports as the same computer; falls back to the user profile.
    /// </summary>
    public static Guid GetClientId()
    {
        foreach (var dir in new[] { AgentPaths.MachineDataDir, AgentPaths.UserDataDir })
        {
            var file = Path.Combine(dir, "client-id");
            try
            {
                if (File.Exists(file) && Guid.TryParse(File.ReadAllText(file).Trim(), out var existing))
                    return existing;
            }
            catch { /* try next location */ }
        }

        var id = Guid.NewGuid();
        foreach (var dir in new[] { AgentPaths.MachineDataDir, AgentPaths.UserDataDir })
        {
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "client-id"), id.ToString());
                return id;
            }
            catch { /* try next location */ }
        }
        return id;
    }

    public static AgentRegistration BuildRegistration(Guid clientId) => new(
        clientId,
        Environment.MachineName,
        CurrentUser,
        GetLocalIpAddress(),
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
        Environment.OSVersion.VersionString);

    public static string CurrentUser =>
        string.IsNullOrEmpty(Environment.UserDomainName)
            ? Environment.UserName
            : $@"{Environment.UserDomainName}\{Environment.UserName}";

    private static string? GetLocalIpAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
