<#
  Cleans up the Group Policy share folder and puts the new RGC Agent in it.
  Run on the server, in PowerShell as Administrator:

      Set-ExecutionPolicy -Scope Process Bypass
      .\Update-RGC-Deploy.ps1

  Keeps only what the PCs need:
      Deploy-RGC-Agent.ps1                   (the startup script Group Policy runs)
      RGC.Agent.exe, msalruntime.dll         (the agent)
      windowsdesktop-runtime-8-win-x64.exe   (so PCs don't download .NET from the internet)
      Install-RGC-Agent.bat                  (manual install on PCs that aren't on the domain)
  Everything else is MOVED (not deleted) to a backup folder next to it.
#>
param(
    [string]$DeployDir = "D:\DATAS\IT\RGC-Deploy",
    # The new RGC-Agent-v1.1.zip; searched in Downloads and next to this script if not given.
    [string]$AgentZip
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path $DeployDir)) { throw "Folder not found: $DeployDir" }

if (-not $AgentZip) {
    $AgentZip = @(
        (Join-Path $env:USERPROFILE "Downloads\RGC-Agent-v1.1.zip"),
        (Join-Path $PSScriptRoot "RGC-Agent-v1.1.zip")
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $AgentZip -or -not (Test-Path $AgentZip)) {
    throw "RGC-Agent-v1.1.zip not found. Put it in Downloads or next to this script, or pass -AgentZip <path>."
}

$keep = "Deploy-RGC-Agent.ps1", "RGC.Agent.exe", "msalruntime.dll",
        "windowsdesktop-runtime-8-win-x64.exe", "Install-RGC-Agent.bat"

# 1. Move everything that isn't needed into a dated backup folder.
$backup = Join-Path (Split-Path $DeployDir) ("RGC-Deploy-backup-" + (Get-Date -Format "yyyyMMdd-HHmm"))
$extra = Get-ChildItem $DeployDir | Where-Object { $keep -notcontains $_.Name }
if ($extra) {
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    foreach ($item in $extra) {
        Move-Item $item.FullName $backup
        Write-Host "  moved to backup: $($item.Name)" -ForegroundColor DarkGray
    }
}

# 2. Back up the old agent, then put the new one in place.
$temp = Join-Path $env:TEMP ("rgc-agent-" + [guid]::NewGuid())
Expand-Archive -Path $AgentZip -DestinationPath $temp -Force
try {
    $newExe = Get-ChildItem $temp -Recurse -Filter "RGC.Agent.exe" | Select-Object -First 1
    if (-not $newExe) { throw "RGC.Agent.exe is not in $AgentZip" }

    $oldExe = Join-Path $DeployDir "RGC.Agent.exe"
    if (Test-Path $oldExe) {
        New-Item -ItemType Directory -Force -Path $backup | Out-Null
        Copy-Item $oldExe (Join-Path $backup "RGC.Agent.exe")
    }
    Copy-Item $newExe.FullName $DeployDir -Force
    $dll = Get-ChildItem $temp -Recurse -Filter "msalruntime.dll" | Select-Object -First 1
    if ($dll) { Copy-Item $dll.FullName $DeployDir -Force }
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
Get-ChildItem $DeployDir | Unblock-File

# 3. Report.
Write-Host ""
Write-Host "RGC-Deploy now contains:" -ForegroundColor Green
Get-ChildItem $DeployDir | ForEach-Object {
    $version = if ($_.Extension -eq ".exe") { " (version $($_.VersionInfo.FileVersion))" } else { "" }
    Write-Host ("  {0}{1}" -f $_.Name, $version)
}
foreach ($name in $keep) {
    if (-not (Test-Path (Join-Path $DeployDir $name))) { Write-Host "  MISSING: $name" -ForegroundColor Yellow }
}
if (Test-Path $backup) { Write-Host "`nOld files are in $backup (delete that folder once everything works)." }
Write-Host "PCs pick up the new agent at their next restart."
