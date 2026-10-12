<#
  Builds release packages for all three RGC components into .\publish\
    publish\Server        – RGC.Server.exe   (ASP.NET Core + SignalR, runs as a Windows Service)
    publish\AdminConsole  – RGC.Admin.exe    (WPF admin console)
    publish\Agent         – RGC.Agent.exe    (WPF tray agent for every PC)
  Each is self-contained (no .NET install needed on target PCs).
#>
param([string]$Configuration = "Release", [string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out  = Join-Path $root "publish"

$common = @("-c", $Configuration, "-r", $Runtime, "--self-contained", "true", "-p:DebugType=none")

dotnet publish "$root\src\RGC.Server\RGC.Server.csproj"             @common -o "$out\Server"
dotnet publish "$root\src\RGC.AdminConsole\RGC.AdminConsole.csproj" @common -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "$out\AdminConsole"
dotnet publish "$root\src\RGC.ClientAgent\RGC.ClientAgent.csproj"   @common -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "$out\Agent"

Copy-Item "$PSScriptRoot\Install-Agent.ps1"  "$out\Agent\"  -Force
Copy-Item "$PSScriptRoot\Install-Server.ps1" "$out\Server\" -Force
Copy-Item "$PSScriptRoot\Configure-Server.ps1" "$out\Server\" -Force
Write-Host "Done. Packages are in $out" -ForegroundColor Green
