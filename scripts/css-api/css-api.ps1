#requires -Version 7
<#
.SYNOPSIS
  NoneadCSS 系统 API 通用调用器（自动登录 + 令牌缓存 + 401 自动重登）。

.DESCRIPTION
  认证流程（实测，见 src NoneadCSS.Application/Services/AuthService.cs:57-160）：
    1) GET  /api/Auth/captcha        → { captchaId, svgContent }（SVG 内文字即验证码，明文可解析）
    2) POST /api/Auth/login          → { success, token, refreshToken, user }
       body: { email, password, captchaId, captchaCode }
    3) 之后所有受保护接口：Authorization: Bearer <token>
  验证码是强制项（AuthService 在密码校验前先 ValidateAsync），不能省略。

.PARAMETER Password
  登录密码。不传时读 $env:CSS_OPS_PASSWORD。密码不落盘、不写日志。

.PARAMETER Force
  忽略令牌缓存，强制重新登录。

.EXAMPLE
  ./css-api.ps1 -Path '/api/Profile' -AsObject

.EXAMPLE
  ./css-api.ps1 -Path '/api/Customers?page=1&pageSize=5' -AsObject

.EXAMPLE
  ./css-api.ps1 -Path '/api/Macros/1' -Method PUT -Body '{"name":"x"}'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [ValidateSet('GET','POST','PUT','PATCH','DELETE')][string]$Method = 'GET',
    [string]$Body,
    [string]$Email = 'admin@example.com',
    [string]$Password = $env:CSS_OPS_PASSWORD,
    [switch]$AsObject,
    [switch]$Force,
    [int]$TimeoutSec = 60
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Base = 'http://css.example.com:5173'
$CacheFile = Join-Path $env:TEMP 'css-api-session.json'

function New-CssSession {
    param([string]$Email, [string]$Password)
    if ([string]::IsNullOrWhiteSpace($Password)) {
        throw '缺少密码：传 -Password 或设置环境变量 CSS_OPS_PASSWORD'
    }
    $captcha = (Invoke-WebRequest -Uri "$Base/api/Auth/captcha" -TimeoutSec $TimeoutSec -SkipHttpErrorCheck).Content | ConvertFrom-Json
    $code = ([regex]::Matches($captcha.data.svgContent, '<text[^>]*>([^<]+)</text>') | ForEach-Object { $_.Groups[1].Value }) -join ''
    if ($code.Length -lt 3) { throw "验证码解析失败（得到 '$code'）" }
    $payload = @{
        email       = $Email
        password    = $Password
        captchaId   = $captcha.data.captchaId
        captchaCode = $code
    } | ConvertTo-Json -Compress
    $resp = Invoke-WebRequest -Uri "$Base/api/Auth/login" -Method Post -Body $payload -ContentType 'application/json' -TimeoutSec $TimeoutSec -SkipHttpErrorCheck
    $json = $resp.Content | ConvertFrom-Json
    if ($resp.StatusCode -ne 200 -or -not $json.token) {
        $msg = if ($json.errorMessage) { $json.errorMessage } else { "HTTP $($resp.StatusCode)" }
        throw "登录失败：$msg"
    }
    $part = $json.token.Split('.')[1].Replace('-', '+').Replace('_', '/')
    while ($part.Length % 4) { $part += '=' }
    $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($part)) | ConvertFrom-Json
    $session = [pscustomobject]@{
        token   = $json.token
        exp     = [int64]$claims.exp
        user    = $json.user
        baseUrl = $Base
    }
    $session | ConvertTo-Json -Depth 6 | Set-Content -Path $CacheFile -Encoding utf8
    return $session
}

function Get-CssSession {
    if (-not $Force -and (Test-Path $CacheFile)) {
        try {
            $cached = Get-Content $CacheFile -Raw | ConvertFrom-Json
            $left = [DateTimeOffset]::FromUnixTimeSeconds([int64]$cached.exp).ToLocalTime() - (Get-Date)
            if ($left.TotalMinutes -gt 2) { return $cached }
        } catch { }
    }
    return New-CssSession -Email $Email -Password $Password
}

$session = Get-CssSession
$headers = @{ Authorization = "Bearer $($session.token)"; 'User-Agent' = 'dsh-css-api/1.0' }
$req = @{
    Uri                = $Base + $Path
    Method             = $Method
    Headers            = $headers
    TimeoutSec         = $TimeoutSec
    SkipHttpErrorCheck = $true
}
if ($Body) { $req.Body = $Body; $req.ContentType = 'application/json' }
$resp = Invoke-WebRequest @req

if ($resp.StatusCode -eq 401) {
    $session = New-CssSession -Email $Email -Password $Password
    $headers.Authorization = "Bearer $($session.token)"
    $req.Headers = $headers
    $resp = Invoke-WebRequest @req
}

if ($AsObject) { return ($resp.Content | ConvertFrom-Json) }
return [pscustomobject]@{ StatusCode = $resp.StatusCode; Content = $resp.Content }
