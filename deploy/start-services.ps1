$root = Split-Path -Parent $PSScriptRoot   # 仓库根目录（本脚本位于 deploy/）
$logs = Join-Path $root "deploy\logs"
New-Item -ItemType Directory -Path $logs -Force | Out-Null

# ===== 1. Kill existing processes =====
Get-Process -Name dotnet,node,nginx -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 6

# ===== 2. Backend environment variables (inherited by child processes) =====
$env:ASPNETCORE_ENVIRONMENT                    = "Production"
$env:ASPNETCORE_FORWARDEDHEADERS_ENABLED       = "true"
$env:ConnectionStrings__SqliteConnection        = "Data Source=data/noneadcss.db"
$env:APP__FrontendUrl                           = "http://127.0.0.1:5173,http://127.0.0.1:5172,https://css.example.com,https://css.example.com"

# ===== 3. Start backend dotnet (:5009) =====
$beDll  = Join-Path $root "publish\NoneadCSS.Api.dll"
$beArgs = "`"$beDll`" --urls `"http://127.0.0.1:5009`""
$pBe = Start-Process -FilePath "dotnet.exe" `
  -ArgumentList $beArgs `
  -WorkingDirectory (Join-Path $root "NoneadCSS.Api") `
  -RedirectStandardOutput (Join-Path $logs "backend.out.log") `
  -RedirectStandardError  (Join-Path $logs "backend.err.log") `
  -NoNewWindow -PassThru
Write-Host ("[start-services] backend  dotnet pid={0,-6} :5009" -f $pBe.Id) -ForegroundColor Green

# ===== 4. Start frontend node vite preview (:5172) =====
$feBin  = Join-Path $root "frontend\node_modules\vite\bin\vite.js"
$feArgs = "`"$feBin`" preview --port 5172 --host 127.0.0.1 --strictPort"
$pFe = Start-Process -FilePath "node.exe" `
  -ArgumentList $feArgs `
  -WorkingDirectory (Join-Path $root "frontend") `
  -RedirectStandardOutput (Join-Path $logs "frontend.out.log") `
  -RedirectStandardError  (Join-Path $logs "frontend.err.log") `
  -NoNewWindow -PassThru
Write-Host ("[start-services] frontend node   pid={0,-6} :5172" -f $pFe.Id) -ForegroundColor Green

# ===== 5. Start Nginx (:5173) =====
$ngArgs = "-c `"$($root)\deploy\nginx.conf`" -p `"$env:NGINX_HOME`""
$pNg = Start-Process -FilePath "$env:NGINX_HOME\nginx.exe" `
  -ArgumentList $ngArgs `
  -WorkingDirectory "$env:NGINX_HOME" `
  -RedirectStandardOutput (Join-Path $logs "nginx.out.log") `
  -RedirectStandardError  (Join-Path $logs "nginx.err.log") `
  -NoNewWindow -PassThru
Write-Host ("[start-services] nginx           pid={0,-6} :5173" -f $pNg.Id) -ForegroundColor Green

# ===== 6. Daemon loop (keep this script alive so grandchildren outlive Job object) =====
Write-Host "[start-services] daemon loop started. will not exit while any child is alive." -ForegroundColor DarkCyan
while ($true) {
  Start-Sleep -Seconds 15
  $anyAlive = $false
  if (-not $pBe.HasExited) { $anyAlive = $true } else { Write-Warning ("backend(pid {0}) exited code={1}" -f $pBe.Id,$pBe.ExitCode) }
  if (-not $pFe.HasExited) { $anyAlive = $true } else { Write-Warning ("frontend(pid {0}) exited code={1}" -f $pFe.Id,$pFe.ExitCode) }
  if (-not $pNg.HasExited) { $anyAlive = $true } else { Write-Warning ("nginx(pid {0}) exited code={1}" -f $pNg.Id,$pNg.ExitCode) }
  if (-not $anyAlive) {
    Write-Host "[start-services] all children exited, daemon done."
    break
  }
}
