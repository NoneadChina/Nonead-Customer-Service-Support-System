#requires -Version 7
<#
.SYNOPSIS
  端点越权审计：找出「接收资源 ID 参数但没有做访问控制」的 Controller action。

.DESCRIPTION
  背景（功能全景总结 #21 / 审核报告 P2-05）：
  本系统没有 TenantId、没有行级安全（RLS），数据可见性完全依赖每个端点主动调用
  ICurrentUserAccessor（CanAccessMailboxAsync / GetAccessibleMailboxIdsAsync 等）。
  新写一个端点漏调一次，就是一个越权洞。

  本脚本做的是**静态近似检查**，不是证明有漏洞：
    1. 找 action 签名里出现 mailboxId / conversationId / customerId / threadId / attachmentId 参数的方法
    2. 在方法体范围内搜索访问控制关键字
    3. 两者都不满足 → 列为「需人工复核」

  超管专属端点（[RequireSuperAdmin]）默认豁免访问控制（超管本来就能看全部），
  但仍会标注，便于确认「是不是真的该只给超管」。

.PARAMETER ControllersPath
  Controller 目录，默认 <repo>/NoneadCSS.Api/Controllers

.PARAMETER OutputPath
  可选：把结果写成 CSV

.EXAMPLE
  pwsh -File scripts/diagnostics/audit-accessor-coverage.ps1
  pwsh -File scripts/diagnostics/audit-accessor-coverage.ps1 -OutputPath .\audit.csv
#>
[CmdletBinding()]
param(
    [string]$ControllersPath = (Join-Path $PSScriptRoot '..\..\NoneadCSS.Api\Controllers'),
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ControllersPath)) {
    throw "Controller 目录不存在：$ControllersPath"
}

# 触发「需要做访问控制」的参数名（资源 ID）
$resourceParams = @('mailboxId', 'conversationId', 'customerId', 'threadId', 'attachmentId', 'chatSessionId')
# 访问控制关键字（出现任一即视为「有做隔离」）
$accessorKeywords = @(
    'CanAccessMailboxAsync',
    'GetAccessibleMailboxIdsAsync',
    'ICurrentUserAccessor',
    '_currentUserAccessor',
    '_userAccessor',
    'CanAccessConversation',
    'EnsureCanAccess'
)

$actionPattern = '^\s*public\s+(async\s+)?(Task<[^>]+>|IActionResult|ActionResult<[^>]+>|void)\s+(\w+)\s*\('
$results = [System.Collections.Generic.List[object]]::new()

Get-ChildItem -Path $ControllersPath -Filter '*Controller.cs' -File | ForEach-Object {
    $file = $_
    $lines = Get-Content $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch $actionPattern) { continue }

        $actionName = $Matches[3]

        # 1) 收集签名（可能跨行，直到出现 ')' 后跟 '{' 或 '=>'）
        $signature = $lines[$i]
        $j = $i
        while ($j -lt $lines.Count -and $signature -notmatch '\)\s*(\{|\r?\n\s*\{|=>)') {
            $j++
            if ($j -lt $lines.Count) { $signature += ' ' + $lines[$j].Trim() }
        }

        $matchedParams = @($resourceParams | Where-Object { $signature -match "\b$_\b" })
        if ($matchedParams.Count -eq 0) { continue }

        # 2) 向上找特性（8 行内）：标注是否超管专属
        $attrs = @()
        for ($b = $i - 1; $b -ge 0 -and $b -ge $i - 10; $b--) {
            $t = $lines[$b].Trim()
            if ($t -match '^\[') { $attrs += $t; continue }
            if ($t -eq '') { continue }
            break
        }
        $isSuperAdminOnly = ($attrs -join ' ') -match 'RequireSuperAdmin'
        $hasPermission = ($attrs -join ' ') -match 'Permission\('

        # 3) 扫方法体：从签名行开始，按大括号深度收尾（上限 400 行，防跑飞）
        $depth = 0
        $started = $false
        $bodyText = ''
        $limit = [Math]::Min($lines.Count - 1, $j + 400)
        for ($k = $j; $k -le $limit; $k++) {
            $line = $lines[$k]
            $bodyText += $line + "`n"
            $depth += ([regex]::Matches($line, '\{')).Count
            $depth -= ([regex]::Matches($line, '\}')).Count
            if ($depth -gt 0) { $started = $true }
            if ($started -and $depth -le 0) { break }
        }

        $hasAccessControl = $false
        foreach ($kw in $accessorKeywords) {
            if ($bodyText.Contains($kw)) { $hasAccessControl = $true; break }
        }

        # 只报告「有资源参数 + 没有访问控制」的
        if ($hasAccessControl) { continue }

        $results.Add([pscustomobject]@{
            Controller    = $file.BaseName
            Action        = $actionName
            ResourceParams= ($matchedParams -join ', ')
            SuperAdminOnly= $isSuperAdminOnly
            HasPermission = $hasPermission
            Risk          = if ($isSuperAdminOnly) { '低（超管专属）' } elseif ($hasPermission) { '中' } else { '高' }
            Line          = ($i + 1)
        })
    }
}

if ($results.Count -eq 0) {
    Write-Host '未发现「接收资源 ID 但无访问控制」的 action。' -ForegroundColor Green
    return
}

$results | Sort-Object @{ Expression = { $_.Risk -ne '高' } }, Controller, Action |
    Format-Table Controller, Action, ResourceParams, SuperAdminOnly, HasPermission, Risk, Line -AutoSize

Write-Host ''
Write-Host ("合计 {0} 个 action 需要人工复核（高 = 非超管专属且无访问控制，优先看）。" -f $results.Count) -ForegroundColor Yellow
Write-Host '注意：这是静态近似检查，命中的不一定有洞，未命中的也不代表安全（可能通过其他方式校验）。'

if ($OutputPath) {
    $results | Sort-Object Controller, Action | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8
    Write-Host "已写出：$OutputPath"
}
