$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$launcherPath = Join-Path $repositoryRoot 'START-COOP-AI.ps1'
. $launcherPath

$passed = 0

function Assert-Equal {
    param(
        [Parameter(Mandatory)] $Expected,
        [Parameter(Mandatory)] $Actual,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($Expected -ne $Actual) {
        throw "$Name failed. Expected '$Expected'; actual '$Actual'."
    }
}

function Assert-ThrowsLike {
    param(
        [Parameter(Mandatory)] [scriptblock] $Action,
        [Parameter(Mandatory)] [string] $Pattern,
        [Parameter(Mandatory)] [string] $Name
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike $Pattern) {
            throw "$Name failed with an unexpected message: $($_.Exception.Message)"
        }
        return
    }
    throw "$Name failed because no exception was raised."
}

function Complete-Scenario {
    param([Parameter(Mandatory)] [string] $Name)
    $script:passed++
    Write-Host "[PASS] $Name" -ForegroundColor Green
}

# A. SQL/API/frontend all stopped, elevated owner.
Assert-Equal 'Start' (Resolve-CoopAiSqlAction -IsRunning $false -IsElevated $true) 'Scenario A SQL'
Assert-Equal 'Start' (Resolve-CoopAiApiAction -IsListening $false -IsHealthy $false) 'Scenario A API'
Assert-Equal 'Start' (Resolve-CoopAiFrontendAction -IsListening $false -IsReady $false) 'Scenario A frontend'
Complete-Scenario 'A - all stopped starts each component'

# B. SQL already running; API/frontend stopped.
Assert-Equal 'Reuse' (Resolve-CoopAiSqlAction -IsRunning $true -IsElevated $false) 'Scenario B SQL'
Assert-Equal 'Start' (Resolve-CoopAiApiAction -IsListening $false -IsHealthy $false) 'Scenario B API'
Assert-Equal 'Start' (Resolve-CoopAiFrontendAction -IsListening $false -IsReady $false) 'Scenario B frontend'
Complete-Scenario 'B - running SQL is reused'

# C. Healthy API already owns 5171.
Assert-Equal 'Reuse' (Resolve-CoopAiApiAction -IsListening $true -IsHealthy $true) 'Scenario C API'
Complete-Scenario 'C - healthy API is reused'

# D. Existing frontend already owns 5173 and serves COOP-AI.
Assert-Equal 'Reuse' (Resolve-CoopAiFrontendAction -IsListening $true -IsReady $true) 'Scenario D frontend'
Complete-Scenario 'D - ready frontend is reused'

# E. Wrong process owns API port.
Assert-ThrowsLike { Resolve-CoopAiApiAction -IsListening $true -IsHealthy $false } `
    '*occupied*not healthy*' 'Scenario E API conflict'
Complete-Scenario 'E - unhealthy 5171 conflict stops safely'

# F. SQL stopped in a non-elevated session.
Assert-ThrowsLike { Resolve-CoopAiSqlAction -IsRunning $false -IsElevated $false } `
    '*Administrator*' 'Scenario F elevation'
Complete-Scenario 'F - stopped SQL clearly requires Administrator'

# G. Repeated execution reuses both listeners and cannot request duplicate starts.
Assert-Equal 'Reuse' (Resolve-CoopAiApiAction -IsListening $true -IsHealthy $true) 'Scenario G API'
Assert-Equal 'Reuse' (Resolve-CoopAiFrontendAction -IsListening $true -IsReady $true) 'Scenario G frontend'
Complete-Scenario 'G - repeated execution creates no duplicate listeners'

$launcherText = Get-Content -Raw -Encoding utf8 -LiteralPath $launcherPath
foreach ($requiredText in @(
    'MSSQL$COOPAI_REHEARSAL',
    "dotnet run --project '.\src\COOPAI.API\COOPAI.API.csproj'",
    'npm.cmd run dev -- --port 5173 --strictPort',
    'COOP-AI API',
    'COOP-AI Frontend',
    'http://localhost:5173/login',
    "`$env:PortfolioSnapshots__PublishingEnabled = 'false'"
)) {
    if (-not $launcherText.Contains($requiredText)) {
        throw "Launcher contract is missing: $requiredText"
    }
}
if ($launcherText -match '\bStop-Process\b|\btaskkill\b|Database\.Migrate|dotnet ef') {
    throw 'Launcher contains a prohibited automatic stop or migration command.'
}

Write-Host "Launcher verification passed: $passed/7 scenarios; safety contract present." -ForegroundColor Cyan
