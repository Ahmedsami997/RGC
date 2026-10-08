using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace RGC.AdminConsole.Services;

/// <summary>
/// Starts Windows Remote Assistance ("offer help") to a domain PC. The user on that PC is asked
/// to allow it, then IT sees the screen and can request control. Needs the "Offer Remote
/// Assistance" policy and firewall rule on the PCs (deploy\Enable-RGC-RemoteAssistance.ps1).
/// </summary>
public static partial class RemoteAssistance
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-_.]{0,62}$")]
    private static partial Regex HostName();

    public static void Offer(string machineName)
    {
        if (!HostName().IsMatch(machineName))
            throw new ApiException($"'{machineName}' is not a valid computer name.");

        var msra = Path.Combine(Environment.SystemDirectory, "msra.exe");
        if (!File.Exists(msra))
            throw new ApiException("Windows Remote Assistance (msra.exe) is not available on this PC.");

        // msra needs administrator rights to offer help: start it through the shell so Windows
        // shows the UAC prompt instead of failing with "The requested operation requires elevation".
        try
        {
            Process.Start(new ProcessStartInfo(msra, $"/offerRA {machineName}") { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            throw new ApiException("Remote Assistance was cancelled (administrator permission is needed to connect).");
        }
    }
}
