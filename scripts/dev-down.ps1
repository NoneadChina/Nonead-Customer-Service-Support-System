<#
.SYNOPSIS
    Stop the detached development stack started by scripts\dev-up.ps1.

.DESCRIPTION
    Order matters: the API runner (scripts\dev-api-run.cmd) restarts dotnet 5s
    after it exits, so the runner must be killed first - otherwise it resurrects
    the API right after this script stops it.

.PARAMETER All
    Also stop the Vite dev server listening on -WebPort.

.PARAMETER ApiPort / WebPort
    Same defaults as dev-up.ps1 (5008 / 5171).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\dev-down.ps1
#>
[CmdletBinding()]
param(
    [switch]$All,
    [int]$ApiPort = 5008,
    [int]$WebPort = 5171
)

$ErrorActionPreference = 'Stop'

$logDir = Join-Path $env:TEMP 'noneadcss-dev'
$runnerPidFile = Join-Path $logDir 'api-runner.pid'
$apiPidFile = Join-Path $logDir 'api.pid'
$legacySupPidFile = Join-Path $logDir 'supervisor.pid'

function Get-PortOwner([int]$Port) {
    $conn = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $conn) { return $null }
    $proc = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $conn.OwningProcess) -ErrorAction SilentlyContinue
    [pscustomobject]@{
        ProcessId   = $conn.OwningProcess
        CommandLine = if ($proc) { $proc.CommandLine } else { '' }
    }
}

function Stop-IfAlive([int]$ProcessId, [string]$What) {
    if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
        Write-Host ("  " + $What + " pid " + $ProcessId + " already gone"); return
    }
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
    if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
        Write-Host ("  WARN: " + $What + " pid " + $ProcessId + " is still alive")
    } else {
        Write-Host ("  stopped " + $What + " (pid " + $ProcessId + ")")
    }
}

Write-Host '== stopping NoneadCSS dev stack =='

# 1. runner first (it would restart dotnet within 5s)
if (Test-Path $runnerPidFile) {
    $raw = (Get-Content $runnerPidFile -Raw).Trim()
    if ($raw -match '^\d+$') { Stop-IfAlive -ProcessId ([int]$raw) -What 'API runner' }
    Remove-Item $runnerPidFile -ErrorAction SilentlyContinue
} else {
    Write-Host '  no API runner pid file'
}
# legacy supervisor from an earlier version of dev-up.ps1
if (Test-Path $legacySupPidFile) {
    $raw = (Get-Content $legacySupPidFile -Raw).Trim()
    if ($raw -match '^\d+$') { Stop-IfAlive -ProcessId ([int]$raw) -What 'legacy supervisor' }
    Remove-Item $legacySupPidFile -ErrorAction SilentlyContinue
}

# 2. then the dotnet listener (pid file + port owner as fallback)
$stopped = $false
if (Test-Path $apiPidFile) {
    $raw = (Get-Content $apiPidFile -Raw).Trim()
    if ($raw -match '^\d+$') { Stop-IfAlive -ProcessId ([int]$raw) -What 'API'; $stopped = $true }
    Remove-Item $apiPidFile -ErrorAction SilentlyContinue
}
$owner = Get-PortOwner $ApiPort
if ($owner -and ($owner.CommandLine -match 'NoneadCSS\.Api')) {
    if (-not $stopped) { Stop-IfAlive -ProcessId $owner.ProcessId -What ('API on port ' + $ApiPort) }
} elseif ($owner) {
    Write-Host ("  port " + $ApiPort + " is held by another program (pid " + $owner.ProcessId + ") - left alone")
}

# 3. Vite only on request
if ($All) {
    $webOwner = Get-PortOwner $WebPort
    if ($webOwner) { Stop-IfAlive -ProcessId $webOwner.ProcessId -What ('Vite on port ' + $WebPort) }
    else { Write-Host ("  nothing listening on " + $WebPort) }
} else {
    Write-Host ("  Vite on " + $WebPort + " left running (use -All to stop it)")
}

Write-Host 'done.'
