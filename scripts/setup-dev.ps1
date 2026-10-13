#Requires -Version 5.1
<#
.SYNOPSIS
  One-time developer setup for SalesApp. Safe to re-run.

.DESCRIPTION
  - Checks the .NET SDK, Node.js and SQL Server LocalDB are available
  - Restores repo-local .NET tools (dotnet-ef)
  - Generates a random JWT signing key into dotnet user-secrets (never committed)
  - Installs frontend packages
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root 'backend/src/SalesApp.Api'

function Step($message) { Write-Host "`n==> $message" -ForegroundColor Cyan }

Push-Location $root
try {
    Step 'Checking prerequisites'
    Write-Host ".NET SDK : $(dotnet --version)"
    Write-Host "Node.js  : $(node --version)"
    $localDb = sqllocaldb info MSSQLLocalDB 2>$null | Select-String '^Version:'
    if (-not $localDb) {
        throw 'SQL Server LocalDB instance "MSSQLLocalDB" was not found. Install SQL Server Express LocalDB 2019 or later.'
    }
    Write-Host "LocalDB  : $($localDb.Line -replace 'Version:\s*', '')"

    Step 'Restoring .NET tools (dotnet-ef)'
    dotnet tool restore

    Step 'Checking user-secrets'
    $secrets = dotnet user-secrets list --project $api 2>$null
    if ($secrets | Select-String '^Jwt:SigningKey') {
        Write-Host 'Jwt:SigningKey already set.'
    } else {
        # RandomNumberGenerator.Create() works on Windows PowerShell 5.1 (.NET Framework) too.
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        $bytes = New-Object byte[] 48
        $rng.GetBytes($bytes)
        $rng.Dispose()
        dotnet user-secrets set 'Jwt:SigningKey' ([Convert]::ToBase64String($bytes)) --project $api | Out-Null
        Write-Host 'Generated a random Jwt:SigningKey in user-secrets.'
    }
    if (-not ($secrets | Select-String '^Anthropic:ApiKey')) {
        Write-Host 'Anthropic:ApiKey is not set yet. AI features stay switched off until you add it (needed from phase P5):' -ForegroundColor Yellow
        Write-Host "  dotnet user-secrets set `"Anthropic:ApiKey`" `"<your key>`" --project backend/src/SalesApp.Api" -ForegroundColor Yellow
    }

    Step 'Installing frontend packages'
    npm ci --prefix frontend --no-fund --no-audit

    Step 'Done'
    Write-Host 'Start the API : dotnet run --project backend/src/SalesApp.Api --launch-profile http'
    Write-Host 'Start the UI  : npm run dev --prefix frontend   (then open http://localhost:5173)'
}
finally {
    Pop-Location
}
