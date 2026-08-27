<#
.SYNOPSIS
Mux Deck 便携运行时引导脚本：下载 Python / MKVToolNix / ffmpeg 到 bin\（可重复运行，已存在则跳过）。

用法（在项目目录执行）:
  powershell -ExecutionPolicy Bypass -File bootstrap.ps1
  需要代理时:
  powershell -ExecutionPolicy Bypass -File bootstrap.ps1 -Proxy http://127.0.0.1:7890
  或用环境变量: set BOOTSTRAP_PROXY=http://127.0.0.1:7890
.PARAMETER Proxy
HTTP 代理地址（可选）。
#>
param([string]$Proxy = "")
$ErrorActionPreference = "Stop"
$base = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin = Join-Path $base "bin"
$tmp = Join-Path $env:TEMP "muxdeck_bootstrap"
$py = Join-Path $bin "python\python.exe"
if (-not $Proxy) { $Proxy = $env:BOOTSTRAP_PROXY }
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

function Get-File([string]$Url, [string]$Out, [int]$MinBytes = 1000000) {
  # 优先用自带 python 的 urllib 下载（对大文件/CDN 更稳，支持代理）；python 不可用时回落到 curl
  if (Test-Path $py) {
    $dlpy = @'
import urllib.request, sys
proxy = sys.argv[3] if len(sys.argv) > 3 else ""
if proxy:
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({"http": proxy, "https": proxy}))
else:
    opener = urllib.request.build_opener()
req = urllib.request.Request(sys.argv[1], headers={"User-Agent": "Mozilla/5.0"})
with opener.open(req, timeout=600) as r, open(sys.argv[2], "wb") as f:
    while True:
        chunk = r.read(65536)
        if not chunk:
            break
        f.write(chunk)
'@
    [IO.File]::WriteAllText((Join-Path $tmp "dl.py"), $dlpy, [Text.UTF8Encoding]::new($false))
    if ($Proxy) { & $py (Join-Path $tmp "dl.py") $Url $Out $Proxy }
    else { & $py (Join-Path $tmp "dl.py") $Url $Out }
    if ($LASTEXITCODE -ne 0) { throw "下载失败: $Url" }
  } elseif ($Proxy) {
    curl.exe -s -L -f -x $Proxy --max-time 1800 -o $Out $Url
    if ($LASTEXITCODE -ne 0) { throw "下载失败: $Url" }
  } else {
    curl.exe -s -L -f --max-time 1800 -o $Out $Url
    if ($LASTEXITCODE -ne 0) { throw "下载失败: $Url" }
  }
  $sz = (Get-Item $Out).Length
  if ($sz -lt $MinBytes) { throw "下载文件过小 ($sz 字节)，疑似失败: $Url" }
}
function Test-Zip([string]$Zip) {
  if (-not (Test-Path $py)) { return $true }
  & $py -c "import zipfile,sys; z=zipfile.ZipFile(sys.argv[1]); sys.exit(0 if z.testzip() is None else 1)" $Zip
  return ($LASTEXITCODE -eq 0)
}
function Get-ZipChecked([string]$Url, [string]$Out) {
  Get-File $Url $Out
  if (-not (Test-Zip $Out)) {
    Write-Host "  zip 完整性校验失败，重试一次..."
    Get-File $Url $Out
    if (-not (Test-Zip $Out)) { throw "zip 仍损坏: $Url" }
  }
}

Write-Host "== Mux Deck 便携运行时引导 =="

# ---------- 1) Python embeddable ----------
if (Test-Path $py) {
  Write-Host "[1/4] Python 已存在，跳过。"
} else {
  Write-Host "[1/4] 下载 Python 3.12.8 embeddable ..."
  Get-ZipChecked "https://www.python.org/ftp/python/3.12.8/python-3.12.8-embed-amd64.zip" (Join-Path $tmp "py.zip")
  New-Item -ItemType Directory -Force -Path (Join-Path $bin "python") | Out-Null
  Expand-Archive (Join-Path $tmp "py.zip") (Join-Path $bin "python") -Force
  Write-Host "  -> bin\python"
}

# ---------- 2) MKVToolNix ----------
$mkm = Join-Path $bin "mkvtoolnix\mkvmerge.exe"
if (Test-Path $mkm) {
  Write-Host "[2/4] MKVToolNix 已存在，跳过。"
} else {
  Write-Host "[2/4] 下载 MKVToolNix 101.0 ..."
  Get-ZipChecked "https://mkvtoolnix.download/windows/releases/101.0/mkvtoolnix-64-bit-101.0.zip" (Join-Path $tmp "mkv.zip")
  New-Item -ItemType Directory -Force -Path (Join-Path $bin "mkvtoolnix") | Out-Null
  Expand-Archive (Join-Path $tmp "mkv.zip") (Join-Path $bin "mkvtoolnix") -Force
  $inner = Get-ChildItem (Join-Path $bin "mkvtoolnix") -Directory | Where-Object { $_.Name -like "mkvtoolnix*" } | Select-Object -First 1
  if ($inner -and -not (Test-Path $mkm)) {
    Get-ChildItem $inner.FullName -Force | Move-Item -Destination (Join-Path $bin "mkvtoolnix") -Force
    Remove-Item $inner.FullName -Recurse -Force -ErrorAction SilentlyContinue
  }
  Write-Host "  -> bin\mkvtoolnix"
}

# ---------- 3) ffmpeg ----------
$ff = Join-Path $bin "ffmpeg\bin\ffmpeg.exe"
if (Test-Path $ff) {
  Write-Host "[3/4] ffmpeg 已存在，跳过。"
} else {
  Write-Host "[3/4] 下载 ffmpeg essentials (含 libass，预览必需) ..."
  Get-ZipChecked "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip" (Join-Path $tmp "ff.zip")
  Expand-Archive (Join-Path $tmp "ff.zip") $bin -Force
  $fdir = Get-ChildItem $bin -Directory | Where-Object { $_.Name -like "ffmpeg-*essentials*" } | Select-Object -First 1
  if ($fdir) {
    if (Test-Path (Join-Path $bin "ffmpeg")) { Remove-Item (Join-Path $bin "ffmpeg") -Recurse -Force }
    Move-Item $fdir.FullName (Join-Path $bin "ffmpeg") -Force
  }
  Write-Host "  -> bin\ffmpeg"
}

# ---------- 4) assfonts ----------
$af = Join-Path $bin "assfonts\assfonts.exe"
if (Test-Path $af) {
  Write-Host "[4/4] assfonts 已存在，跳过。"
} else {
  Write-Host "[4/4] 下载 assfonts v0.7.3 ..."
  Get-ZipChecked "https://github.com/wyzdwdz/assfonts/releases/download/v0.7.3/assfonts-v0.7.3-x86_64-Windows.zip" (Join-Path $tmp "af.zip")
  New-Item -ItemType Directory -Force -Path (Join-Path $bin "assfonts") | Out-Null
  Expand-Archive (Join-Path $tmp "af.zip") (Join-Path $bin "assfonts") -Force
  Write-Host "  -> bin\assfonts"
}

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ""
Write-Host "引导完成。运行 环境自检.bat 验证，再双击 start_mux_ui.bat 启动。"
