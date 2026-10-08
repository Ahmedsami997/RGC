<#
  Lets IT use the "Connect" button in the RGC Admin Console (Windows Remote Assistance, "offer help").
  Adds to the "RGC Agent" Group Policy, which already reaches every workstation:
    - Offer Remote Assistance: on, helpers may view and control (the user still has to click Yes)
    - the helpers allowed to offer it (default: Domain Admins)
    - firewall rules for Remote Assistance, domain network only

  Run once on the domain controller (RGCDC03), in PowerShell as Administrator:

      Set-ExecutionPolicy -Scope Process Bypass -Force
      .\Enable-RGC-RemoteAssistance.ps1
      .\Enable-RGC-RemoteAssistance.ps1 -Helpers "ROYALGOLFCLUB\IT Support"   # a different group

  PCs pick it up at their next restart or "gpupdate /force".
#>
param(
    [string]$GpoName = "RGC Agent",
    # Users or groups allowed to offer help. Use a group, not a person.
    [string[]]$Helpers = @("ROYALGOLFCLUB\Domain Admins")
)
$ErrorActionPreference = "Stop"
Import-Module GroupPolicy
Import-Module NetSecurity
Import-Module ActiveDirectory

$gpo = Get-GPO -Name $GpoName
$ts = "HKLM\Software\Policies\Microsoft\Windows NT\Terminal Services"

# Computer Configuration > Administrative Templates > System > Remote Assistance > Configure Offer Remote Assistance
Set-GPRegistryValue -Guid $gpo.Id -Key $ts -ValueName "fAllowUnsolicited" -Type DWord -Value 1 | Out-Null
Set-GPRegistryValue -Guid $gpo.Id -Key $ts -ValueName "fAllowUnsolicitedFullControl" -Type DWord -Value 1 | Out-Null
foreach ($helper in $Helpers) {
    Set-GPRegistryValue -Guid $gpo.Id -Key "$ts\RAUnsolicit" -ValueName $helper -Type String -Value $helper | Out-Null
}
Write-Host "Offer Remote Assistance enabled for: $($Helpers -join ', ')" -ForegroundColor Green

# Firewall rules inside the GPO (domain profile only).
$store = "$((Get-ADDomain).DNSRoot)\$GpoName"
$rules = @(
    @{ Name = "RGC-RA-DCOM";   DisplayName = "RGC Remote Assistance (DCOM-In)";   Protocol = "TCP"; LocalPort = "135"; Program = "%SystemRoot%\System32\svchost.exe"; Service = "RpcSs" },
    @{ Name = "RGC-RA-Server"; DisplayName = "RGC Remote Assistance (RA Server)"; Protocol = "TCP"; Program = "%SystemRoot%\System32\raserver.exe" },
    @{ Name = "RGC-RA-TCP";    DisplayName = "RGC Remote Assistance (TCP-In)";    Protocol = "TCP"; Program = "%SystemRoot%\System32\msra.exe" }
)
foreach ($r in $rules) {
    Remove-NetFirewallRule -PolicyStore $store -Name $r.Name -ErrorAction SilentlyContinue
    $rule = @{
        PolicyStore = $store; Name = $r.Name; DisplayName = $r.DisplayName; Group = "RGC Remote Assistance"
        Direction = "Inbound"; Action = "Allow"; Profile = "Domain"; Protocol = $r.Protocol; Program = $r.Program
    }
    if ($r.LocalPort) { $rule.LocalPort = $r.LocalPort }
    if ($r.Service) { $rule.Service = $r.Service }
    New-NetFirewallRule @rule | Out-Null
    Write-Host "  firewall rule: $($r.DisplayName)"
}

Write-Host ""
Write-Host "Done. PCs get it at their next restart (or run 'gpupdate /force' on a PC)." -ForegroundColor Green
Write-Host "The Admin Console must be run by a member of: $($Helpers -join ', ')"
