using Microsoft.Win32;

namespace RGC.ClientAgent.Services;

/// <summary>Registers the agent under HKCU\...\Run so it starts with Windows for this user.</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RGC Agent";

    public static void Ensure(bool enabled)
    {
        try
        {
            // If IT registered the agent machine-wide (HKLM Run), don't add a duplicate per-user entry.
            using (var machine = Registry.LocalMachine.OpenSubKey(RunKey))
                if (enabled && machine?.GetValue(ValueName) is not null) return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            var exe = Environment.ProcessPath;
            if (enabled && exe is not null)
            {
                var command = $"\"{exe}\" --autostart";
                if (!Equals(key.GetValue(ValueName), command))
                {
                    key.SetValue(ValueName, command);
                    AgentLog.Info($"Registered auto-start: {command}");
                }
            }
            else if (!enabled && key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                AgentLog.Info("Removed auto-start entry");
            }
        }
        catch (Exception ex)
        {
            AgentLog.Error("Could not update auto-start registry entry", ex);
        }
    }
}
