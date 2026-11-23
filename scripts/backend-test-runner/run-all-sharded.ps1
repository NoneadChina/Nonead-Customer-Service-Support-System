# 分片执行 NoneadCSS 后端全量测试（一类一进程，最后逐用例补齐）
#
# 为什么需要分片：xunit 在个别测试类上会「提前返回」（start 了类却没有对应的
# finished，后续类不再执行）。一类一进程可把这种提前返回限制在单个类内，
# 再用 --one-by-one 对该类逐用例补跑，即可拿到零缺口的完整结果。
#
# 用法：
#   pwsh -File scripts/backend-test-runner/run-all-sharded.ps1

$ErrorActionPreference = 'Continue'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$testout  = Join-Path $repoRoot 'NoneadCSS.Tests\bin\Release\net10.0'
$runner   = Join-Path $testout 'BackendTestRunner.dll'
$dll      = Join-Path $testout 'NoneadCSS.Tests.dll'

if (-not (Test-Path $runner)) {
    Write-Host "找不到执行器: $runner"
    Write-Host "请先构建: dotnet build scripts/backend-test-runner/BackendTestRunner.csproj -c Release -o NoneadCSS.Tests\bin\Release\net10.0"
    exit 2
}
if (-not (Test-Path $dll)) {
    Write-Host "找不到测试程序集: $dll（请先 dotnet build -c Release）"
    exit 2
}

Set-Location $repoRoot

$list = & dotnet $runner $dll --list 2>&1
$classes = $list | Where-Object { $_ -like 'CLASS=*' } | ForEach-Object { $_.Substring(6).Trim() }
Write-Host "CLASSES=$($classes.Count)"

$totP = 0; $totF = 0; $totS = 0; $totSel = 0; $totRep = 0
$failLines  = New-Object System.Collections.Generic.List[string]
$partial    = New-Object System.Collections.Generic.List[string]
$noSummary  = New-Object System.Collections.Generic.List[string]

$i = 0
foreach ($c in $classes) {
    $i++
    $out = & dotnet $runner $dll --class $c 2>&1
    $summary = $out | Where-Object { $_ -like 'SUMMARY *' } | Select-Object -First 1
    if (-not $summary) {
        Write-Host ("[{0}/{1}] {2}  -> 无汇总" -f $i, $classes.Count, $c)
        $noSummary.Add($c)
        continue
    }
    $m = [regex]::Match($summary, 'pass=(\d+) fail=(\d+) skip=(\d+) reported=(\d+) selected=(\d+)')
    $p = [int]$m.Groups[1].Value; $f = [int]$m.Groups[2].Value; $s = [int]$m.Groups[3].Value
    $rep = [int]$m.Groups[4].Value; $sel = [int]$m.Groups[5].Value
    $totP += $p; $totF += $f; $totS += $s; $totRep += $rep; $totSel += $sel
    foreach ($l in ($out | Where-Object { $_ -like 'FAILCASE *' })) { $failLines.Add($c + ' :: ' + $l.Substring(9)) }

    $flag = ''
    if ($rep -lt $sel) { $flag = "  ⚠ 提前返回 reported=$rep selected=$sel"; $partial.Add($c) }
    Write-Host ("[{0}/{1}] {2}  pass={3} fail={4} skip={5}{6}" -f $i, $classes.Count, $c, $p, $f, $s, $flag)
}

# 对提前返回的类逐用例补跑
if ($partial.Count -gt 0) {
    Write-Host ""
    Write-Host "---- 对 $($partial.Count) 个提前返回的类做逐用例补齐 ----"
    foreach ($c in $partial) {
        $out = & dotnet $runner $dll --class $c --one-by-one 2>&1
        $summary = $out | Where-Object { $_ -like 'SUMMARY *' } | Select-Object -First 1
        $missing = $out | Where-Object { $_ -like 'MISSING_COUNT=*' } | Select-Object -First 1
        $m = [regex]::Match($summary, 'pass=(\d+) fail=(\d+) skip=(\d+) reported=(\d+) selected=(\d+)')
        Write-Host ("  {0}  pass={1} fail={2} skip={3} {4}" -f $c, $m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value, $missing)
        foreach ($l in ($out | Where-Object { $_ -like 'FAILCASE *' })) { $failLines.Add($c + ' :: ' + $l.Substring(9)) }
    }
}

Write-Host ""
Write-Host "==================== 汇总 ===================="
Write-Host "  类数        = $($classes.Count)"
Write-Host "  选中用例    = $totSel"
Write-Host "  无汇总的类  = $($noSummary.Count)"
Write-Host "  提前返回的类 = $($partial.Count)"
if ($failLines.Count -gt 0) {
    Write-Host "  ---- 失败明细（$($failLines.Count) 条）----"
    $failLines | ForEach-Object { Write-Host "  [FAIL] $_" }
} else {
    Write-Host "  失败明细    = 无"
}
Write-Host "============================================="
Write-Host "SHARDED_RUN_DONE"
