<#
.SYNOPSIS
    为离线安装包生成安全随机密钥并写入 .env（基于同目录 .env.example）。

.DESCRIPTION
    使用 System.Security.Cryptography.RandomNumberGenerator 生成密码学安全随机值。
    字符集不含 $ 与 #，避免 docker compose 变量插值破坏。
    每次运行都会产生全新的密钥。

.EXAMPLE
    .\generate-secrets.ps1 -Mode New -AdminEmail admin@example.com -AdminPassword "YourStrongP@ssw0rd!"
    .\generate-secrets.ps1 -Mode RotateOnly   # 只轮换密钥，保留现有管理员账号
#>

[CmdletBinding()]
param(
    [ValidateSet('New', 'RotateOnly')]
    [string]$Mode = 'New',
    [string]$AdminEmail = '',
    [string]$AdminPassword = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile    = Join-Path $scriptDir '.env'
$exampleFile = Join-Path $scriptDir '.env.example'

if (-not (Test-Path $exampleFile)) {
    throw ".env.example 模板文件不存在: $exampleFile"
}

function New-RandomBase64([int]$ByteCount) {
    $bytes = New-Object byte[] $ByteCount
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return [Convert]::ToBase64String($bytes)
}

function New-RandomAscii([int]$Length) {
    $allowed = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_+-=%&@!?'
    $sb = New-Object System.Text.StringBuilder
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $one = New-Object byte[] 1
        for ($i = 0; $i -lt $Length; $i++) {
            # 用模运算生成 < allowed.Length 的均匀索引（GetInt32 在 .NET Framework 不可用）
            do { $rng.GetBytes($one) } while ($one[0] -ge 256 - [math]::Floor(256 / $allowed.Length) * $allowed.Length)
            $idx = $one[0] % $allowed.Length
            [void]$sb.Append($allowed[$idx])
        }
    } finally { $rng.Dispose() }
    return $sb.ToString()
}

function New-StrongPassword([int]$Length = 24) {
    return (New-RandomAscii -Length $Length) + 'A1!'
}

$secrets = @{
    JwtKey          = New-RandomBase64 -ByteCount 64
    HasherKey       = New-RandomBase64 -ByteCount 64
    Aes256Key       = New-RandomBase64 -ByteCount 32
    AuditHmacKey    = New-RandomAscii -Length 72
    SatisfactionKey = New-RandomAscii -Length 64
}

$content = Get-Content -Path $exampleFile -Raw -Encoding UTF8

$content = $content -replace '<REPLACE_WITH_64BYTES_BASE64_RANDOM>', 'PLACEHOLDER_JWT_HASHER'
$content = $content.Replace('JWT__Key=PLACEHOLDER_JWT_HASHER', "JWT__Key=$($secrets.JwtKey)")
$content = $content.Replace('SECRET_HASHER__Key=PLACEHOLDER_JWT_HASHER', "SECRET_HASHER__Key=$($secrets.HasherKey)")
$content = $content.Replace('<REPLACE_WITH_32BYTES_BASE64_RANDOM>', $secrets.Aes256Key)
$content = $content.Replace('<REPLACE_WITH_64CHARS_ASCII_RANDOM>', $secrets.AuditHmacKey)
$content = $content.Replace('<REPLACE_WITH_AT_LEAST_32CHARS_RANDOM>', 'PLACEHOLDER_32CHARS')
$content = $content.Replace('APP__SatisfactionTokenKey=PLACEHOLDER_32CHARS', "APP__SatisfactionTokenKey=$($secrets.SatisfactionKey)")

if ($Mode -eq 'New') {
    if ([string]::IsNullOrWhiteSpace($AdminEmail)) { $AdminEmail = 'admin@example.com' }
    if ([string]::IsNullOrWhiteSpace($AdminPassword)) { $AdminPassword = New-StrongPassword -Length 28 }
    $content = $content.Replace('ADMIN_EMAIL=admin@example.com', "ADMIN_EMAIL=$AdminEmail")
    $content = $content.Replace('ADMIN_PASSWORD=<REPLACE_WITH_STRONG_PASSWORD>', "ADMIN_PASSWORD=$AdminPassword")
    Write-Host "管理员账号: $AdminEmail" -ForegroundColor Cyan
    Write-Host "管理员密码: $AdminPassword" -ForegroundColor Yellow
    Write-Warning "请立即记录以上管理员密码，本脚本不会再次显示！"
} else {
    if (Test-Path $envFile) {
        $old = Get-Content -Path $envFile -Raw -Encoding UTF8
        $m1 = [regex]::Match($old, '(?m)^ADMIN_EMAIL=(.*)$')
        $m2 = [regex]::Match($old, '(?m)^ADMIN_PASSWORD=(.*)$')
        if ($m1.Success) { $content = $content.Replace('ADMIN_EMAIL=admin@example.com', "ADMIN_EMAIL=$($m1.Groups[1].Value)") }
        if ($m2.Success) { $content = $content.Replace('ADMIN_PASSWORD=<REPLACE_WITH_STRONG_PASSWORD>', "ADMIN_PASSWORD=$($m2.Groups[1].Value)") }
    }
    Write-Host "RotateOnly 模式：仅轮换密钥，管理员账号保持不变。" -ForegroundColor Cyan
}

Set-Content -Path $envFile -Value $content -Encoding UTF8 -NoNewline

# 收紧文件 ACL：仅当前用户可读写（仅 Windows 生效）
try {
    $acl = Get-Acl -Path $envFile
    $acl.SetAccessRuleProtection($true, $false)
    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $inherit = [System.Security.AccessControl.InheritanceFlags]::None
    $propagate = [System.Security.AccessControl.PropagationFlags]::None
    $allow = [System.Security.AccessControl.AccessControlType]::Allow
    $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($currentUser, 'FullControl', $inherit, $propagate, $allow)
    $acl.AddAccessRule($rule)
    Set-Acl -Path $envFile $acl
    Write-Host "文件访问控制已收紧：仅当前用户 ($currentUser) 可读写。" -ForegroundColor Green
} catch {
    Write-Warning "无法设置 ACL：$($_.Exception.Message)"
}


Write-Host "已生成安全随机密钥写入 $envFile" -ForegroundColor Green
