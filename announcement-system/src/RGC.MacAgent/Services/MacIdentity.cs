using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using RGC.Shared;

namespace RGC.MacAgent.Services;

public static class MacIdentity
{
    /// <summary>A stable id for this Mac (per user account), created on first run.</summary>
    public static Guid GetClientId()
    {
        var file = Path.Combine(MacPaths.DataDir, "client-id");
        try
        {
            if (File.Exists(file) && Guid.TryParse(File.ReadAllText(file).Trim(), out var existing)) return existing;
        }
        catch { /* create a new one */ }

        var id = Guid.NewGuid();
        try { File.WriteAllText(file, id.ToString()); } catch (Exception ex) { AgentLog.Error("Could not save client id", ex); }
        return id;
    }

    public static string CurrentUser => Environment.UserName;

    public static AgentRegistration BuildRegistration(Guid clientId) => new(
        clientId,
        ComputerName,
        CurrentUser,
        GetLocalIpAddress(),
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
        OsVersion,
        CurrentUser);

    /// <summary>The name shown in System Settings → General → Sharing, e.g. "Ali's MacBook Pro".</summary>
    private static string ComputerName { get; } = Run("/usr/sbin/scutil", "--get ComputerName") ?? Environment.MachineName;

    /// <summary>e.g. "macOS 15.1 (24B83)".</summary>
    private static string OsVersion { get; } = Run("/usr/bin/sw_vers", "-productVersion") is { } v
        ? $"macOS {v}{(Run("/usr/bin/sw_vers", "-buildVersion") is { } b ? $" ({b})" : "")}"
        : Environment.OSVersion.VersionString;

    private static string? Run(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, UseShellExecute = false });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(3000);
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

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
