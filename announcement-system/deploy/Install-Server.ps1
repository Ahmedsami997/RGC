<#
  Installs RGC.Server as a Windows Service. Run as Administrator from the published Server folder.
  Before running: edit appsettings.json (connection string, Jwt:SigningKey, Announcements:AgentKey,
  BootstrapAdmin:Password) – or set them as environment variables, e.g. Jwt__SigningKey.
#>
param(
    [string]$InstallDir = "C:\Program Files\RGC\Server",
    [string]$ServiceAccount = "",          # e.g. "DOMAIN\svc-rgc" (leave empty for LocalSystem)
    [string]$ServiceAccountPassword = "",
    [int]$Port = 5080
)
$ErrorActionPreference = "Stop"
$name = "RGC Announcement Server"

if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
    Stop-Service -Name $name -Force
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot "*") -Destination $InstallDir -Recurse -Force -Exclude "Install-Server.ps1","Configure-Server.ps1","AGENT-KEY.txt","*.bak"

$exe = Join-Path $InstallDir "RGC.Server.exe"
if (-not (Get-Service -Name $name -ErrorAction SilentlyContinue)) {
    if ($ServiceAccount) {
        New-Service -Name $name -BinaryPathName "`"$exe`"" -DisplayName $name -StartupType Automatic `
            -Credential (New-Object PSCredential($ServiceAccount, (ConvertTo-SecureString $ServiceAccountPassword -AsPlainText -Force))) | Out-Null
    } else {
        New-Service -Name $name -BinaryPathName "`"$exe`"" -DisplayName $name -StartupType Automatic | Out-Null
    }
    sc.exe failure "$name" reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
}

if (-not (Get-NetFirewallRule -DisplayName "RGC Announcement Server" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "RGC Announcement Server" -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
}

Start-Service -Name $name
Write-Host "RGC server installed and started on port $Port." -ForegroundColor Green
