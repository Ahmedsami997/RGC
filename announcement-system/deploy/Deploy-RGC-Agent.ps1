<#
  One-step install of the RGC Agent on a staff PC.
    - installs the .NET Desktop Runtime 8 (x64) if it is missing
    - copies the agent to C:\Program Files\RGC\Agent
    - points it at the RGC server and starts it at every Windows logon (all users)
  Works when double-clicked (via Install-RGC-Agent.bat), from Intune/SCCM, or as a
  Group Policy computer startup script (runs as SYSTEM; the agent then starts at user logon).
#>
param(
    [string]$ServerUrl = "https://rgc-announcements-ayakghhpdhfkbydr.uaenorth-01.azurewebsites.net",
    [string]$InstallDir = "$env:ProgramFiles\RGC\Agent"
)
$ErrorActionPreference = "Stop"
$dataDir = Join-Path $env:ProgramData "RGC"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$logFile = Join-Path $dataDir "deploy.log"
function Log([string]$m) { $line = "$(Get-Date -Format s)  $m"; Write-Host $line; Add-Content -Path $logFile -Value $line }

try {
    Log "RGC Agent deployment starting on $env:COMPUTERNAME (server $ServerUrl)"

    # 1. .NET Desktop Runtime 8 (x64)
    $runtimeDir = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.WindowsDesktop.App"
    $hasRuntime = (Test-Path $runtimeDir) -and (Get-ChildItem $runtimeDir -Directory -Filter "8.*" -ErrorAction SilentlyContinue)
    if (-not $hasRuntime) {
        Log "Installing .NET Desktop Runtime 8 (x64)..."
        $bundled = Get-ChildItem $PSScriptRoot -Filter "windowsdesktop-runtime-8*-win-x64.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($bundled) { $installer = $bundled.FullName }
        else {
            $installer = Join-Path $env:TEMP "windowsdesktop-runtime-8-win-x64.exe"
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe" -OutFile $installer -UseBasicParsing
        }
        $p = Start-Process $installer -ArgumentList "/install /quiet /norestart" -Wait -PassThru
        Log "Runtime installer exit code: $($p.ExitCode)"
    } else {
        Log ".NET Desktop Runtime 8 already installed"
    }

    # 2. Agent files
    Get-Process -Name "RGC.Agent" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    foreach ($f in "RGC.Agent.exe", "msalruntime.dll") {
        $src = Join-Path $PSScriptRoot $f
        if (Test-Path $src) { Copy-Item $src $InstallDir -Force }
    }
    Get-ChildItem $InstallDir | Unblock-File
    if (-not (Test-Path (Join-Path $InstallDir "RGC.Agent.exe"))) { throw "RGC.Agent.exe not found next to this script." }
    Log "Agent copied to $InstallDir"

    # 3. Settings (machine-wide) and a stable id for this PC
    @{ ServerUrl = $ServerUrl; AgentKey = ""; CountdownSeconds = 10; AutoStart = $true; AllowUserExit = $false } |
        ConvertTo-Json | Set-Content -Path (Join-Path $dataDir "agentsettings.json") -Encoding UTF8
    $idFile = Join-Path $dataDir "client-id"
    if (-not (Test-Path $idFile)) { [guid]::NewGuid().ToString() | Set-Content -Path $idFile -Encoding ASCII }

    # 4. Start at every logon, for every user
    $exe = Join-Path $InstallDir "RGC.Agent.exe"
    Set-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "RGC Agent" -Value "`"$exe`" --autostart"
    Log "Registered to start at logon"

    # 5. Start now if someone is logged on interactively (not when running as SYSTEM from GPO)
    $isSystem = [Security.Principal.WindowsIdentity]::GetCurrent().IsSystem
    # Started through Explorer so it runs as the signed-in user, not elevated: the Microsoft 365
    # sign-in (Windows account broker) fails with 0x80070520 in an elevated or different-user process.
    if (-not $isSystem) { Start-Process -FilePath "explorer.exe" -ArgumentList "`"$exe`""; Log "Agent started" }

    Log "Done."
    exit 0
}
catch {
    Log "FAILED: $($_.Exception.Message)"
    exit 1
}
