<#
  Fills in RGC.Server's appsettings.json for you:
    - generates a random Jwt:SigningKey and Announcements:AgentKey on this PC
    - sets the SQL Server connection
    - sets the first admin password
  Run it from the folder that contains appsettings.json (the unzipped RGC-Server folder):
    Set-ExecutionPolicy -Scope Process Bypass
    .\Configure-Server.ps1
#>
param(
    [string]$SqlServer,          # e.g. "localhost", "SQL01" or "SQL01\SQLEXPRESS"
    [string]$Database = "RGC_Announcements"
)
$ErrorActionPreference = "Stop"

$file = Join-Path $PSScriptRoot "appsettings.json"
if (-not (Test-Path $file)) { throw "appsettings.json not found next to this script ($PSScriptRoot)." }

function New-Secret([int]$bytes) {
    $buf = New-Object byte[] $bytes
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($buf)
    $rng.Dispose()
    # URL/JSON-safe characters only
    return ([Convert]::ToBase64String($buf)).Replace('+', 'A').Replace('/', 'B').TrimEnd('=')
}

if (-not $SqlServer) {
    $SqlServer = Read-Host "SQL Server name (press Enter for 'localhost' = SQL Server on this PC; SQL Express is usually 'localhost\SQLEXPRESS')"
    if (-not $SqlServer) { $SqlServer = "localhost" }
}

do {
    $p1 = Read-Host "Choose the RGC admin password (8+ characters)" -AsSecureString
    $p2 = Read-Host "Type it again" -AsSecureString
    $pw1 = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($p1))
    $pw2 = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($p2))
    if ($pw1 -ne $pw2) { Write-Host "Passwords don't match, try again." -ForegroundColor Yellow }
    elseif ($pw1.Length -lt 8) { Write-Host "Password must be at least 8 characters." -ForegroundColor Yellow }
} until ($pw1 -eq $pw2 -and $pw1.Length -ge 8)

$json = Get-Content $file -Raw | ConvertFrom-Json

$json.ConnectionStrings.RgcDatabase = "Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
$json.Jwt.SigningKey = New-Secret 48
$agentKey = New-Secret 24
$json.Announcements.AgentKey = $agentKey
$json.BootstrapAdmin.Password = $pw1

Copy-Item $file "$file.bak" -Force
$json | ConvertTo-Json -Depth 10 | Set-Content -Path $file -Encoding UTF8

$keyFile = Join-Path $PSScriptRoot "AGENT-KEY.txt"
"RGC Agent key (needed when installing the agent on every PC):`r`n$agentKey" | Set-Content -Path $keyFile -Encoding UTF8

Write-Host ""
Write-Host "appsettings.json updated (backup saved as appsettings.json.bak)." -ForegroundColor Green
Write-Host "  SQL Server : $SqlServer  (database $Database)"
Write-Host "  Admin user : admin"
Write-Host "  Agent key  : $agentKey" -ForegroundColor Cyan
Write-Host "  (also saved to $keyFile - keep it safe)"
Write-Host ""
Write-Host "Next: .\Install-Server.ps1   (run PowerShell as Administrator)"
