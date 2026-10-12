<#
  Prepares Group Policy deployment of the RGC Agent to every staff PC.
  Run ONCE on a domain controller (or a server with the GroupPolicy and ActiveDirectory
  PowerShell modules), as a Domain Admin, from the unzipped RGC-Staff-Installer folder:

      Set-ExecutionPolicy -Scope Process Bypass
      .\Setup-RGC-GroupPolicy.ps1

  What it does:
    1. Copies the installer files to C:\RGC-Deploy (and downloads the .NET Desktop Runtime 8
       there once, so the PCs don't each download it from the internet).
    2. Shares the folder as \\<this server>\RGC$ with read access for Domain Computers.
    3. Creates the GPO "RGC Agent", turns on "Always wait for the network at computer startup"
       in it, and links it to the OU you choose.
  The last step (adding the startup script to the GPO) is done in the Group Policy editor;
  the script prints the exact values to paste.
#>
param(
    [string]$DeployDir = "C:\RGC-Deploy",
    [string]$ShareName = "RGC$",
    [string]$GpoName = "RGC Agent"
)
$ErrorActionPreference = "Stop"
Import-Module GroupPolicy, ActiveDirectory

# 1. Files
$files = "RGC.Agent.exe", "msalruntime.dll", "Deploy-RGC-Agent.ps1"
foreach ($f in $files) {
    if (-not (Test-Path (Join-Path $PSScriptRoot $f))) { throw "$f not found next to this script - run it from the unzipped RGC-Staff-Installer folder." }
}
New-Item -ItemType Directory -Force -Path $DeployDir | Out-Null
$DeployDir = (Resolve-Path $DeployDir).Path
# Skip the copy when the script is already running from the deploy folder.
if ($DeployDir.TrimEnd('\') -ne $PSScriptRoot.TrimEnd('\')) {
    foreach ($f in $files) { Copy-Item (Join-Path $PSScriptRoot $f) $DeployDir -Force }
}
Get-ChildItem $DeployDir | Unblock-File

$runtime = Join-Path $DeployDir "windowsdesktop-runtime-8-win-x64.exe"
if (-not (Test-Path $runtime)) {
    Write-Host "Downloading .NET Desktop Runtime 8 (x64) into $DeployDir ..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe" -OutFile $runtime -UseBasicParsing
    } catch {
        Write-Host "Could not download it ($($_.Exception.Message)). PCs will download it themselves instead." -ForegroundColor Yellow
        Remove-Item $runtime -ErrorAction SilentlyContinue
    }
}
Write-Host "Files ready in $DeployDir" -ForegroundColor Green

# 2. Share (read-only for computers and users; admins keep full control locally)
$domain = Get-ADDomain
$computers = "$($domain.NetBIOSName)\Domain Computers"
if (-not (Get-SmbShare -Name $ShareName -ErrorAction SilentlyContinue)) {
    New-SmbShare -Name $ShareName -Path $DeployDir -ReadAccess $computers, "Authenticated Users" -FullAccess "BUILTIN\Administrators" | Out-Null
}
$acl = Get-Acl $DeployDir
foreach ($who in $computers, "Authenticated Users") {
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($who, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")))
}
Set-Acl $DeployDir $acl
$fqdn = "$env:COMPUTERNAME.$($domain.DNSRoot)"
$unc = "\\$fqdn\$ShareName"
Write-Host "Shared as $unc" -ForegroundColor Green

# 3. GPO
$gpo = Get-GPO -Name $GpoName -ErrorAction SilentlyContinue
if (-not $gpo) { $gpo = New-GPO -Name $GpoName -Comment "Installs and updates the RGC announcement agent at computer startup." }
# Computer startup scripts need the network to be up before they run.
Set-GPRegistryValue -Name $GpoName -Key "HKLM\Software\Policies\Microsoft\Windows NT\CurrentVersion\Winlogon" `
    -ValueName "SyncForegroundPolicy" -Type DWord -Value 1 | Out-Null

$ous = @(Get-ADOrganizationalUnit -Filter * | Sort-Object DistinguishedName)
Write-Host ""
Write-Host "Which OU holds the staff PCs? (tip: pick a small test OU first, then link more OUs later)"
for ($i = 0; $i -lt $ous.Count; $i++) { Write-Host ("  [{0}] {1}" -f ($i + 1), $ous[$i].DistinguishedName) }
$choice = Read-Host "Number (or press Enter to skip linking for now)"
if ($choice) {
    $ou = $ous[[int]$choice - 1].DistinguishedName
    if (-not ((Get-GPInheritance -Target $ou).GpoLinks.DisplayName -contains $GpoName)) {
        New-GPLink -Name $GpoName -Target $ou -LinkEnabled Yes | Out-Null
    }
    Write-Host "GPO '$GpoName' linked to $ou" -ForegroundColor Green
}

Write-Host ""
Write-Host "LAST STEP - add the startup script (2 minutes):" -ForegroundColor Cyan
Write-Host "  1. Open Group Policy Management, right-click '$GpoName' > Edit"
Write-Host "  2. Computer Configuration > Policies > Windows Settings > Scripts (Startup/Shutdown) > Startup"
Write-Host "  3. On the 'Scripts' tab (not 'PowerShell Scripts') click Add and enter:"
Write-Host "       Script Name:       powershell.exe"
Write-Host "       Script Parameters: -NoProfile -ExecutionPolicy Bypass -File `"$unc\Deploy-RGC-Agent.ps1`""
Write-Host "  4. OK, OK, close the editor."
Write-Host ""
Write-Host "Then restart a PC in that OU (twice the first time). Its log is C:\ProgramData\RGC\deploy.log"
