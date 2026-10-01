<#
  Installs the RGC Client Agent on a PC (run as Administrator, or push via GPO/Intune/SCCM).
  - Copies RGC.Agent.exe to Program Files
  - Writes machine-wide settings to %ProgramData%\RGC\agentsettings.json
  - Registers it to start for every user at Windows logon (HKLM Run)
  - Starts it for the current user

  Example:
    .\Install-Agent.ps1 -ServerUrl "http://rgc-server:5080" -AgentKey "the-shared-agent-key"
#>
param(
    [Parameter(Mandatory)] [string]$ServerUrl,
    [Parameter(Mandatory)] [string]$AgentKey,
    [string]$InstallDir = "C:\Program Files\RGC\Agent",
    [int]$CountdownSeconds = 10
)
$ErrorActionPreference = "Stop"

Get-Process -Name "RGC.Agent" -ErrorAction SilentlyContinue | Stop-Process -Force

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot "*") -Destination $InstallDir -Recurse -Force -Exclude "Install-Agent.ps1"

$dataDir = Join-Path $env:ProgramData "RGC"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
# One stable id per PC, shared by every user who logs on to it.
$idFile = Join-Path $dataDir "client-id"
if (-not (Test-Path $idFile)) { [guid]::NewGuid().ToString() | Set-Content -Path $idFile -Encoding ASCII }

@{
    ServerUrl        = $ServerUrl
    AgentKey         = $AgentKey
    CountdownSeconds = $CountdownSeconds
    AutoStart        = $true
    AllowUserExit    = $false
} | ConvertTo-Json | Set-Content -Path (Join-Path $dataDir "agentsettings.json") -Encoding UTF8

$exe = Join-Path $InstallDir "RGC.Agent.exe"
Set-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "RGC Agent" -Value "`"$exe`" --autostart"

Start-Process -FilePath $exe -ArgumentList "--autostart"
Write-Host "RGC Agent installed. It starts automatically at every Windows logon." -ForegroundColor Green
