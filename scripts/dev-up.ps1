<#
.SYNOPSIS
    Start (and keep alive) the local development stack: API on 5008 + Vite on 5171.

.DESCRIPTION
    Why this script exists
    ----------------------
    The login page shows "Could not load the CAPTCHA" whenever the API behind the
    Vite proxy is not listening (the proxy then answers 502). Two things used to
    break it:

      1. Starting the API with Start-Process makes it a CHILD of whatever shell
         started it, so closing that shell (or restarting the agent host) kills it.
         Fix: launch through the WMI provider host, which is not our child.

      2. Nothing restarted it afterwards.
         Fix: scripts\dev-api-run.cmd wraps `dotnet NoneadCSS.Api.dll` in a loop
         that restarts it 5s after any exit. A .cmd loop is used instead of a
         PowerShell supervisor because on this machine a WMI-created process can
         run cmd.exe + dotnet but CANNOT run a .ps1 file at all (script rule),
         and Windows PowerShell 5.1 launched that way silently does nothing.

    Steps: build (unless -SkipBuild) -> API -> Vite -> report.

.PARAMETER SkipBuild
    Do not run `dotnet build` first (use the existing binaries).

.PARAMETER Force
    If port 5008 is held by our own API (or by a leftover dev-api-run.cmd), stop
    it and start a fresh one.

.PARAMETER ApiPort / WebPort
    Override the default ports (5008 / 5171).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\dev-up.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$Force,
    [int]$ApiPort = 5008,
    [int]$WebPort = 5171
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot 'NoneadCSS.slnx'))) {
    throw "Cannot find NoneadCSS.slnx above $PSScriptRoot - run this script from the repository's scripts folder."
}
$apiDir = Join-Path $repoRoot 'NoneadCSS.Api'
$webDir = Join-Path $repoRoot 'frontend'
$runnerCmd = Join-Path $PSScriptRoot 'dev-api-run.cmd'
$logDir = Join-Path $env:TEMP 'noneadcss-dev'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$apiPidFile = Join-Path $logDir 'api.pid'
$runnerPidFile = Join-Path $logDir 'api-runner.pid'
$healthUrl = "http://127.0.0.1:$ApiPort/api/auth/captcha"
$webUrl = "http://127.0.0.1:$WebPort/"

# --------------------------------------------------------------------------- #
# helpers
# --------------------------------------------------------------------------- #

function Get-PortOwner([int]$Port) {
    $conn = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $conn) { return $null }
    $proc = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $conn.OwningProcess) -ErrorAction SilentlyContinue
    [pscustomobject]@{
        ProcessId   = $conn.OwningProcess
        CommandLine = if ($proc) { $proc.CommandLine } else { '' }
    }
}

function Test-ApiHealthy([int]$Port, [int]$TimeoutSec = 4) {
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/api/auth/captcha" -UseBasicParsing -TimeoutSec $TimeoutSec
        return ($r.StatusCode -eq 200 -and $r.Content -match '"svgContent"')
    } catch {
        return $false
    }
}

function Wait-Api([int]$Port, [int]$TimeoutSec = 90) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-ApiHealthy -Port $Port) { return $true }
        Start-Sleep -Milliseconds 700
    }
    return $false
}

function Show-ApiLogTail([int]$Lines = 25) {
    $log = Join-Path $logDir 'api.log'
    if (-not (Test-Path $log)) { Write-Host '  (no API log found)'; return }
    Write-Host ('  --- tail of ' + $log + ' ---')
    Get-Content $log -Tail $Lines | ForEach-Object { Write-Host ('  | ' + $_) }
    Write-Host '  --- end of log ---'
}

function Stop-OurApi([int]$Port) {
    # Kill the cmd wrapper FIRST: it would otherwise relaunch dotnet 5s later.
    if (Test-Path $runnerPidFile) {
        $raw = (Get-Content $runnerPidFile -Raw).Trim()
        if ($raw -match '^\d+$') {
            Stop-Process -Id ([int]$raw) -Force -ErrorAction SilentlyContinue
            Write-Host ('  stopped API runner (pid ' + $raw + ')')
        }
        Remove-Item $runnerPidFile -ErrorAction SilentlyContinue
    }
    $owner = Get-PortOwner $Port
    if ($owner -and $owner.CommandLine -match 'NoneadCSS\.Api') {
        Stop-Process -Id $owner.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Host ('  stopped API listener (pid ' + $owner.ProcessId + ')')
        Start-Sleep -Milliseconds 800
    } elseif ($owner) {
        Write-Host ('  port ' + $Port + ' is held by another program (pid ' + $owner.ProcessId + ') - left alone')
    }
    Remove-Item $apiPidFile -ErrorAction SilentlyContinue
}

function Start-Api([int]$Port) {
    # `cmd /c "<script>" <port>` executed by the WMI provider host: the runner is
    # not our child and keeps restarting dotnet after a crash.
    $full = 'cmd.exe /c ""' + $runnerCmd + '" ' + $Port + '"'
    $res = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $full }
    if ($res.ReturnValue -ne 0) {
        throw ("Win32_Process.Create failed with ReturnValue=" + $res.ReturnValue)
    }
    Set-Content -Path $runnerPidFile -Value $res.ProcessId -Encoding ascii
    Write-Host ("  API runner started (pid " + $res.ProcessId + "), log: " + (Join-Path $logDir 'api.log'))
    return $res.ProcessId
}

function Save-ApiListenerPid([int]$Port) {
    # dotnet is a child of the runner, so record the actual listener for dev-down.
    $owner = Get-PortOwner $Port
    if ($owner) { Set-Content -Path $apiPidFile -Value $owner.ProcessId -Encoding ascii }
}

# --------------------------------------------------------------------------- #
# 1. build
# --------------------------------------------------------------------------- #

Write-Host '== NoneadCSS dev stack =='
Write-Host ("   repo: " + $repoRoot)

if ($SkipBuild) {
    Write-Host '[1/3] build skipped (-SkipBuild)'
} else {
    Write-Host '[1/3] dotnet build ...'
    Push-Location $repoRoot
    try {
        & dotnet build 'NoneadCSS.slnx' -v:q -nologo
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }
    } finally {
        Pop-Location
    }
}

# --------------------------------------------------------------------------- #
# 2. API
# --------------------------------------------------------------------------- #

Write-Host ("[2/3] API on " + $ApiPort + " ...")
$owner = Get-PortOwner $ApiPort
if ($owner -and (Test-ApiHealthy -Port $ApiPort)) {
    Write-Host ("  already healthy (pid " + $owner.ProcessId + ")")
    Save-ApiListenerPid -Port $ApiPort
} elseif ($owner) {
    $isOurs = ($owner.CommandLine -match 'NoneadCSS\.Api')
    if (-not $isOurs) {
        throw ("Port " + $ApiPort + " is held by another program (pid " + $owner.ProcessId + "): " +
               $owner.CommandLine + ". Free the port or pass -ApiPort.")
    }
    if (-not $Force) {
        throw ("Port " + $ApiPort + " is held by an unhealthy NoneadCSS.Api process (pid " + $owner.ProcessId +
               "). Re-run with -Force to restart it.")
    }
    Stop-OurApi -Port $ApiPort
    Start-Api -Port $ApiPort | Out-Null
    if (-not (Wait-Api -Port $ApiPort -TimeoutSec 90)) { Show-ApiLogTail; throw ("API not healthy - see " + $logDir) }
    Save-ApiListenerPid -Port $ApiPort
    Write-Host '  API healthy (auto-restart enabled)'
} else {
    if ($Force) { Stop-OurApi -Port $ApiPort }
    Start-Api -Port $ApiPort | Out-Null
    if (-not (Wait-Api -Port $ApiPort -TimeoutSec 90)) {
        Show-ApiLogTail
        throw ("API did not answer " + $healthUrl + " within 90s - see " + $logDir)
    }
    Save-ApiListenerPid -Port $ApiPort
    Write-Host '  API healthy (auto-restart enabled)'
}

# --------------------------------------------------------------------------- #
# 3. Vite
# --------------------------------------------------------------------------- #

Write-Host ("[3/3] Vite on " + $WebPort + " ...")
$webOwner = Get-PortOwner $WebPort
if ($webOwner) {
    Write-Host ("  already listening (pid " + $webOwner.ProcessId + ")")
} else {
    $full = 'cmd.exe /c cd /d "' + $webDir + '" && npm run dev >> "' + (Join-Path $logDir 'web.log') + '" 2>&1'
    $res = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $full }
    if ($res.ReturnValue -ne 0) { throw ("could not start Vite (ReturnValue=" + $res.ReturnValue + ")") }
    Write-Host ("  Vite starting (pid " + $res.ProcessId + "), log: " + (Join-Path $logDir 'web.log'))
    $deadline = (Get-Date).AddSeconds(90)
    $up = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri $webUrl -UseBasicParsing -TimeoutSec 3
            if ($r.StatusCode -eq 200) { $up = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 800
    }
    if ($up) { Write-Host '  Vite is up' } else { Write-Host '  WARN: Vite did not answer within 90s - see web.log' }
}

Write-Host ''
Write-Host 'Ready:'
Write-Host ("  web : " + $webUrl + "login")
Write-Host ("  api : " + $healthUrl)
Write-Host ("  logs: " + $logDir)
Write-Host 'The API is restarted automatically within 5s if it exits (scripts\dev-api-run.cmd).'
Write-Host 'Stop everything with: powershell -File scripts\dev-down.ps1'
