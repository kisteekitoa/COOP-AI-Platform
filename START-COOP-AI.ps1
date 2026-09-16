[CmdletBinding()]
param(
    [ValidateRange(10, 600)]
    [int] $StartupTimeoutSeconds = 120,

    [switch] $SkipBrowser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$script:SqlServiceName = 'MSSQL$COOPAI_REHEARSAL'
$script:ApiPort = 5171
$script:FrontendPort = 5173
$script:ApiHealthUrl = 'http://localhost:5171/health'
$script:FrontendUrl = 'http://localhost:5173'
$script:LoginUrl = 'http://localhost:5173/login'
$script:RehearsalConnectionString = 'Server=lpc:.\COOPAI_REHEARSAL;Database=COOPAI_PREVIEW_20260827_01;Integrated Security=True;Encrypt=False;TrustServerCertificate=False;Application Name=COOPAI Local Owner Launcher'

function Write-CoopAiStatus {
    param(
        [Parameter(Mandatory)] [string] $Component,
        [Parameter(Mandatory)] [string] $Message,
        [ConsoleColor] $Color = [ConsoleColor]::Gray
    )

    Write-Host "[$Component] $Message" -ForegroundColor $Color
}

function Test-CoopAiAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-CoopAiSqlAction {
    param(
        [Parameter(Mandatory)] [bool] $IsRunning,
        [Parameter(Mandatory)] [bool] $IsElevated
    )

    if ($IsRunning) { return 'Reuse' }
    if (-not $IsElevated) {
        throw [InvalidOperationException]::new(
            'SQL service is stopped. Right-click START-COOP-AI.ps1 and choose Run with PowerShell as Administrator.')
    }
    return 'Start'
}

function Resolve-CoopAiApiAction {
    param(
        [Parameter(Mandatory)] [bool] $IsListening,
        [Parameter(Mandatory)] [bool] $IsHealthy
    )

    if (-not $IsListening) { return 'Start' }
    if ($IsHealthy) { return 'Reuse' }
    throw [InvalidOperationException]::new('Port 5171 is occupied, but the API /health endpoint is not healthy.')
}

function Resolve-CoopAiFrontendAction {
    param(
        [Parameter(Mandatory)] [bool] $IsListening,
        [Parameter(Mandatory)] [bool] $IsReady
    )

    if (-not $IsListening) { return 'Start' }
    if ($IsReady) { return 'Reuse' }
    throw [InvalidOperationException]::new('Port 5173 is occupied, but the COOP-AI frontend is not reachable.')
}

function Get-CoopAiPortListeners {
    param([Parameter(Mandatory)] [int] $Port)

    return @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
}

function Test-CoopAiPortListening {
    param([Parameter(Mandatory)] [int] $Port)

    return @(Get-CoopAiPortListeners -Port $Port).Count -gt 0
}

function Get-CoopAiPortOwnerDescription {
    param([Parameter(Mandatory)] [int] $Port)

    $processIds = @(Get-CoopAiPortListeners -Port $Port |
        Select-Object -ExpandProperty OwningProcess -Unique)
    if ($processIds.Count -eq 0) { return 'no listener owner found' }

    $descriptions = foreach ($processId in $processIds) {
        try {
            $process = Get-Process -Id $processId -ErrorAction Stop
            "PID $processId ($($process.ProcessName))"
        }
        catch {
            "PID $processId (process details unavailable)"
        }
    }
    return $descriptions -join ', '
}

function Test-CoopAiApiHealth {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $script:ApiHealthUrl -TimeoutSec 3
        return [int]$response.StatusCode -eq 200
    }
    catch { return $false }
}

function Test-CoopAiFrontendReady {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $script:LoginUrl -TimeoutSec 3
        return [int]$response.StatusCode -eq 200 -and
            [string]$response.Content -match '<div id="root"></div>'
    }
    catch { return $false }
}

function Wait-CoopAiCondition {
    param(
        [Parameter(Mandatory)] [scriptblock] $Condition,
        [Parameter(Mandatory)] [string] $FailureMessage,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    throw [TimeoutException]::new($FailureMessage)
}

function ConvertTo-CoopAiPowerShellLiteral {
    param([Parameter(Mandatory)] [string] $Value)

    $escaped = $Value.Replace("'", "''")
    return "'" + $escaped + "'"
}

function Start-CoopAiPowerShellWindow {
    param(
        [Parameter(Mandatory)] [string] $Title,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string[]] $Commands
    )

    $titleLiteral = ConvertTo-CoopAiPowerShellLiteral -Value $Title
    $workingDirectoryLiteral = ConvertTo-CoopAiPowerShellLiteral -Value $WorkingDirectory
    $command = @(
        "try { `$Host.UI.RawUI.WindowTitle = $titleLiteral } catch { }"
        "Set-Location -LiteralPath $workingDirectoryLiteral"
    ) + $Commands
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes(($command -join '; ')))

    return Start-Process -FilePath 'powershell.exe' `
        -ArgumentList @('-NoLogo', '-NoExit', '-EncodedCommand', $encodedCommand) `
        -WorkingDirectory $WorkingDirectory `
        -WindowStyle Normal `
        -PassThru
}

function Start-CoopAiSql {
    param([Parameter(Mandatory)] [int] $TimeoutSeconds)

    try {
        $service = Get-Service -Name $script:SqlServiceName -ErrorAction Stop
    }
    catch {
        throw [InvalidOperationException]::new(
            "SQL service '$($script:SqlServiceName)' is not installed or cannot be inspected.",
            $_.Exception)
    }

    $action = Resolve-CoopAiSqlAction `
        -IsRunning ($service.Status -eq [ServiceProcess.ServiceControllerStatus]::Running) `
        -IsElevated (Test-CoopAiAdministrator)
    if ($action -eq 'Start') {
        Write-CoopAiStatus -Component 'SQL' -Message 'Starting COOPAI_REHEARSAL...' -Color Yellow
        try {
            Start-Service -Name $script:SqlServiceName
            $service.WaitForStatus(
                [ServiceProcess.ServiceControllerStatus]::Running,
                [TimeSpan]::FromSeconds($TimeoutSeconds))
            $service.Refresh()
        }
        catch {
            throw [InvalidOperationException]::new(
                "Could not start SQL service '$($script:SqlServiceName)': $($_.Exception.Message)",
                $_.Exception)
        }
        if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Running) {
            throw [InvalidOperationException]::new("SQL service '$($script:SqlServiceName)' did not reach Running state.")
        }
    }

    Write-CoopAiStatus -Component 'SQL' -Message 'Running' -Color Green
}

function Start-CoopAiApi {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    $isListening = Test-CoopAiPortListening -Port $script:ApiPort
    $isHealthy = $isListening -and (Test-CoopAiApiHealth)
    try {
        $action = Resolve-CoopAiApiAction -IsListening $isListening -IsHealthy $isHealthy
    }
    catch {
        $owner = Get-CoopAiPortOwnerDescription -Port $script:ApiPort
        throw [InvalidOperationException]::new("$($_.Exception.Message) Conflicting listener: $owner. Nothing was stopped.")
    }

    if ($action -eq 'Start') {
        if (-not (Get-Command 'dotnet.exe' -ErrorAction SilentlyContinue)) {
            throw [InvalidOperationException]::new('dotnet.exe was not found. Install the .NET SDK or add it to PATH.')
        }

        $localRuntimeRoot = Join-Path $env:LOCALAPPDATA 'COOPAI-Rehearsal'
        $keyRoot = Join-Path $localRuntimeRoot 'DataProtection-Keys'
        $importTemp = Join-Path $localRuntimeRoot 'ImportTemp'
        [void](New-Item -ItemType Directory -Path $keyRoot -Force)
        [void](New-Item -ItemType Directory -Path $importTemp -Force)

        $commands = @(
            "`$env:ASPNETCORE_ENVIRONMENT = 'Development'"
            "`$env:ASPNETCORE_URLS = 'http://localhost:5171'"
            "`$env:ConnectionStrings__DefaultConnection = $(ConvertTo-CoopAiPowerShellLiteral -Value $script:RehearsalConnectionString)"
            "`$env:PortfolioSnapshots__PublishingEnabled = 'false'"
            "`$env:ReverseProxy__Enabled = 'false'"
            "`$env:DataProtection__ApplicationName = 'COOPAI-Rehearsal-Preview'"
            "`$env:DataProtection__KeyRingPath = $(ConvertTo-CoopAiPowerShellLiteral -Value $keyRoot)"
            "`$env:DataProtection__ProtectionMode = 'DPAPI'"
            "`$env:ImportUpload__TempDirectory = $(ConvertTo-CoopAiPowerShellLiteral -Value $importTemp)"
            "dotnet run --project '.\src\COOPAI.API\COOPAI.API.csproj'"
        )
        Write-CoopAiStatus -Component 'API' -Message 'Starting in a separate window...' -Color Yellow
        [void](Start-CoopAiPowerShellWindow -Title 'COOP-AI API' -WorkingDirectory $RepositoryRoot -Commands $commands)
    }

    try {
        Wait-CoopAiCondition -Condition { Test-CoopAiApiHealth } `
            -FailureMessage "API did not return HTTP 200 from $($script:ApiHealthUrl) within $TimeoutSeconds seconds. Check the COOP-AI API window." `
            -TimeoutSeconds $TimeoutSeconds
    }
    catch {
        if (Test-CoopAiPortListening -Port $script:ApiPort) {
            $owner = Get-CoopAiPortOwnerDescription -Port $script:ApiPort
            throw [InvalidOperationException]::new("$($_.Exception.Message) Current listener: $owner. Nothing was stopped.")
        }
        throw
    }

    Write-CoopAiStatus -Component 'API' -Message 'Healthy on port 5171' -Color Green
}

function Start-CoopAiFrontend {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    $isListening = Test-CoopAiPortListening -Port $script:FrontendPort
    $isReady = $isListening -and (Test-CoopAiFrontendReady)
    try {
        $action = Resolve-CoopAiFrontendAction -IsListening $isListening -IsReady $isReady
    }
    catch {
        $owner = Get-CoopAiPortOwnerDescription -Port $script:FrontendPort
        throw [InvalidOperationException]::new("$($_.Exception.Message) Conflicting listener: $owner. Nothing was stopped.")
    }

    if ($action -eq 'Start') {
        if (-not (Get-Command 'npm.cmd' -ErrorAction SilentlyContinue)) {
            throw [InvalidOperationException]::new('npm.cmd was not found. Install Node.js or add it to PATH.')
        }

        $webRoot = Join-Path $RepositoryRoot 'src\coopai-web'
        Write-CoopAiStatus -Component 'Frontend' -Message 'Starting in a separate window...' -Color Yellow
        [void](Start-CoopAiPowerShellWindow `
            -Title 'COOP-AI Frontend' `
            -WorkingDirectory $webRoot `
            -Commands @('npm.cmd run dev -- --port 5173 --strictPort'))
    }

    try {
        Wait-CoopAiCondition -Condition {
            (Test-CoopAiPortListening -Port $script:FrontendPort) -and (Test-CoopAiFrontendReady)
        } -FailureMessage "Frontend did not become reachable on port 5173 within $TimeoutSeconds seconds. Check the COOP-AI Frontend window." `
          -TimeoutSeconds $TimeoutSeconds
    }
    catch {
        if (Test-CoopAiPortListening -Port $script:FrontendPort) {
            $owner = Get-CoopAiPortOwnerDescription -Port $script:FrontendPort
            throw [InvalidOperationException]::new("$($_.Exception.Message) Current listener: $owner. Nothing was stopped.")
        }
        throw
    }

    Write-CoopAiStatus -Component 'Frontend' -Message 'Ready on port 5173' -Color Green
}

function Invoke-CoopAiStartup {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [int] $TimeoutSeconds,
        [Parameter(Mandatory)] [bool] $OpenBrowser
    )

    Write-CoopAiStatus -Component 'COOP-AI' -Message 'Safe local startup' -Color Cyan
    Write-CoopAiStatus -Component 'Safety' -Message 'Local COOPAI_REHEARSAL only; publishing and migrations remain disabled.' -Color DarkCyan
    Start-CoopAiSql -TimeoutSeconds ([Math]::Min($TimeoutSeconds, 60))
    Start-CoopAiApi -RepositoryRoot $RepositoryRoot -TimeoutSeconds $TimeoutSeconds
    Start-CoopAiFrontend -RepositoryRoot $RepositoryRoot -TimeoutSeconds $TimeoutSeconds

    if ($OpenBrowser) {
        try {
            Start-Process -FilePath $script:LoginUrl
            Write-CoopAiStatus -Component 'Browser' -Message "Opened $($script:LoginUrl)" -Color Green
        }
        catch {
            throw [InvalidOperationException]::new(
                "COOP-AI is ready, but the default browser could not be opened: $($_.Exception.Message)",
                $_.Exception)
        }
    }

    Write-CoopAiStatus -Component 'COOP-AI' -Message 'Ready' -Color Green
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Invoke-CoopAiStartup `
            -RepositoryRoot $PSScriptRoot `
            -TimeoutSeconds $StartupTimeoutSeconds `
            -OpenBrowser (-not $SkipBrowser)
    }
    catch {
        Write-CoopAiStatus -Component 'COOP-AI' `
            -Message "FAILED: $($_.Exception.Message)" `
            -Color Red
        exit 1
    }
}
