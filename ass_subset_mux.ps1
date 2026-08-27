<#
.SYNOPSIS
Auto-subset ASS subs and mux into MKV.
Matches MKV and subs by EPISODE NUMBER (not same filename):
  Video.S01E01.mkv        + sub E01.sc.ass / E01.tc.ass
  Name[01]....mkv         + Name[01]....sc.ass
  Kono ... 2 - 01.mkv     + [...][01]....sc.ass

.PARAMETER Root     target folder (required)
.PARAMETER FontsDir fonts dir (default Root\Fonts, then Root\Font)
.PARAMETER OutDir   output dir (default: overwrite original MKV)
.PARAMETER ScLang / ScName / TcLang / TcName  track language/name
.PARAMETER Force    reprocess MKVs that already have fonts
#>
param(
  [Parameter(Mandatory=$true)][string]$Root,
  [string]$FontsDir = '',
  [string]$OutDir = '',
  [string]$AssFonts = (Join-Path $PSScriptRoot 'bin\assfonts\assfonts.exe'),
  [string]$MkvMerge = '',
  [string]$MkvExtract = '',
  [string]$ScLang = 'zh-Hans',
  [string]$TcLang = 'zh-Hant',
  [string]$ScName = 'SC',
  [string]$TcName = 'TC',
  [switch]$Force
)
$ErrorActionPreference = 'Stop'
# 工具回落：自带便携版 -> 系统默认安装 -> PATH
if (-not $MkvMerge) {
  $cand = Join-Path $PSScriptRoot 'bin\mkvtoolnix\mkvmerge.exe'
  $MkvMerge = $(if (Test-Path -LiteralPath $cand) { $cand } else { 'C:\Program Files\MKVToolNix\mkvmerge.exe' })
}
if (-not $MkvExtract) {
  $cand = Join-Path $PSScriptRoot 'bin\mkvtoolnix\mkvextract.exe'
  $MkvExtract = $(if (Test-Path -LiteralPath $cand) { $cand } else { 'C:\Program Files\MKVToolNix\mkvextract.exe' })
}
# UTF-8 console I/O：中文提示经管道传输不乱码
$__utf8 = New-Object System.Text.UTF8Encoding($false)
try { [Console]::InputEncoding = $__utf8 } catch {}
try { [Console]::OutputEncoding = $__utf8 } catch {}
if (-not (Test-Path -LiteralPath $Root)) { Write-Error "Root not found: $Root"; exit 1 }
if (-not $FontsDir) {
  $FontsDir = Join-Path $Root 'Fonts'
  if (-not (Test-Path -LiteralPath $FontsDir)) { $FontsDir = Join-Path $Root 'Font' }
}
if (-not (Test-Path -LiteralPath $FontsDir)) { Write-Error "Fonts dir not found: $FontsDir"; exit 1 }

# Extract episode number from a filename (supports [01] / E01 / EP01 / S01E01 / "- 01" / trailing number)
function Get-EpisodeNumber([string]$Name) {
  if ($Name -match 'S(\d{1,2})E(\d{1,3})') { return [int]$matches[2] }
  if ($Name -match '\[(\d{1,3})\]') { return [int]$matches[1] }
  if ($Name -match '[-_]\s*(\d{1,3})(?![0-9])') { return [int]$matches[1] }
  if ($Name -match 'EP(\d{1,3})') { return [int]$matches[1] }
  if ($Name -match '(?<![0-9A-Za-z])E(\d{1,3})(?![0-9A-Za-z])') { return [int]$matches[1] }
  if ($Name -match '(\d{1,3})(?![0-9])$') { return [int]$matches[1] }
  return -1
}

# 轻量编码检查：UTF-8 BOM 或严格 UTF-8 解码；非 UTF-8 返回 $false（仅警告，不中断）
function Test-Utf8([string]$Path) {
  try { $bytes = [System.IO.File]::ReadAllBytes($Path) } catch { return $true }
  if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { return $true }
  try {
    $strict = New-Object System.Text.UTF8Encoding($false, $true)
    $null = $strict.GetString($bytes)
    return $true
  } catch { return $false }
}

$workBase = Join-Path $env:TEMP 'ass_subset_mux'
New-Item -ItemType Directory -Force -Path $workBase | Out-Null

Write-Host "Building fonts DB from: $FontsDir"
$buildLog = Join-Path $workBase 'assfonts_build.log'
& $AssFonts -f $FontsDir -b -d $workBase *> $buildLog
if ($LASTEXITCODE -ne 0) {
  Write-Host "FATAL: assfonts 建库失败 (exit $LASTEXITCODE)，请检查字体目录后重试"
  Write-Host "日志位置: $buildLog"
  exit 1
}

$mkvs = @(Get-ChildItem -LiteralPath $Root -Filter '*.mkv' -File | Sort-Object Name)
if ($mkvs.Count -eq 0) { Write-Host "No MKV found."; exit 0 }
Write-Host "Found $($mkvs.Count) MKV(s)."

$ok = 0; $skip = 0; $fail = 0
foreach ($mkv in $mkvs) {
  Write-Host "===== $($mkv.Name) ====="
  $info = (& $MkvMerge -J --ui-language en $mkv.FullName) | ConvertFrom-Json
  $hasFont = @($info.attachments | Where-Object { $_.mime_type -match 'font' }).Count -gt 0
  if ($hasFont -and -not $Force) { Write-Host "SKIP (already has fonts)"; $skip++; continue }

  # --- match SC/TC subs by episode number ---
  $mkvEp = Get-EpisodeNumber $mkv.BaseName
  $sc = ''; $tc = ''
  if (Test-Path -LiteralPath (Join-Path $mkv.DirectoryName ($mkv.BaseName + '.sc.ass'))) { $sc = Join-Path $mkv.DirectoryName ($mkv.BaseName + '.sc.ass') }
  if (Test-Path -LiteralPath (Join-Path $mkv.DirectoryName ($mkv.BaseName + '.tc.ass'))) { $tc = Join-Path $mkv.DirectoryName ($mkv.BaseName + '.tc.ass') }
  if (($sc -eq '' -or $tc -eq '') -and $mkvEp -ge 0) {
    $cands = @(Get-ChildItem -LiteralPath $mkv.DirectoryName -Filter '*.ass' -File)
    foreach ($c in $cands) {
      if ($c.Name -ieq ($mkv.BaseName + '.sc.ass') -or $c.Name -ieq ($mkv.BaseName + '.tc.ass')) { continue }
      if ((Get-EpisodeNumber $c.BaseName) -ne $mkvEp) { continue }
      if ($c.Name -match '\.sc\.ass$' -and $sc -ieq '') { $sc = $c.FullName; Write-Host "  matched by ep#: $($c.Name)" }
      if ($c.Name -match '\.tc\.ass$' -and $tc -ieq '') { $tc = $c.FullName; Write-Host "  matched by ep#: $($c.Name)" }
    }
  }
  if (-not $sc -and -not $tc) { Write-Host "SKIP (no matching sc/tc ass, ep#=$mkvEp)"; $skip++; continue }

  # 编码检查：CLI 路径不做编码转换，非 UTF-8 只醒目警告、不中断
  foreach ($s in @($sc, $tc)) {
    if ($s -and -not (Test-Utf8 $s)) {
      Write-Host "  WARNING: $s 该字幕不是 UTF-8，CLI 路径不做编码转换，建议先用 Web UI 的编码检查/转换"
    }
  }

  $epTag = ($mkv.BaseName -replace '[\\/:*?"<>|\[\]()]','_')
  $epWork = Join-Path $workBase $epTag
  New-Item -ItemType Directory -Force -Path $epWork | Out-Null

  $sf = Join-Path $epWork 'sf'
  New-Item -ItemType Directory -Force -Path $sf | Out-Null
  $subArgs = @()
  if ($sc) { $subArgs += $sc }
  if ($tc) { $subArgs += $tc }
  $slog = @(& $AssFonts -f $FontsDir -d $workBase -o $sf -s -c -i $subArgs 2>&1)
  $sfdir = Join-Path $sf 'subsetted_fonts'
  $fonts = @(Get-ChildItem -LiteralPath $sfdir -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -match '^\.(ttf|otf|ttc|otc|woff2?)$' })
  if ($fonts.Count -eq 0) {
    Write-Host "FAIL (subset produced no fonts)"
    $slog | Select-String 'Missing|ERROR' | ForEach-Object { Write-Host "  $($_.Line.Trim())" }
    $fail++; continue
  }
  Write-Host "  subsetted $($fonts.Count) fonts"
  $slog | Select-String 'WARN|Missing' | ForEach-Object { Write-Host "  WARN: $($_.Line.Trim())" }

  if ($OutDir) {
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $out = Join-Path $OutDir $mkv.Name
    if (Test-Path -LiteralPath $out) {
      Write-Host "FAIL (同名冲突: $out 已存在，不覆盖)"
      $fail++
      continue
    }
  } else {
    $out = Join-Path $epWork 'out.mkv'
  }
  $argList = @('-o', $out, '--ui-language', 'en', '-S', '-M', $mkv.FullName)
  $scDef = '0:1'
  $tcDef = '0:0'
  if (-not $sc -and $tc) { $tcDef = '0:1' }
  if ($sc) {
    $argList += @('--language', "0:$ScLang", '--track-name', "0:$ScName", '--default-track-flag', $scDef, '--forced-display-flag', '0:0', $sc)
  }
  if ($tc) {
    $argList += @('--language', "0:$TcLang", '--track-name', "0:$TcName", '--default-track-flag', $tcDef, '--forced-display-flag', '0:0', $tc)
  }
  foreach ($f in $fonts) { $argList += @('--attach-file', $f.FullName) }
  $muxout = @(& $MkvMerge @argList 2>&1)
  if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL (mux error):"
    $muxout | Select-Object -Last 5 | ForEach-Object { Write-Host "    $_" }
    $fail++; continue
  }

  $vid = (& $MkvMerge -J --ui-language en $out) | ConvertFrom-Json
  $newSubs = @($vid.tracks | Where-Object { $_.type -eq 'subtitles' })
  if ($newSubs.Count -eq 0 -or @($vid.attachments).Count -eq 0) { Write-Host "FAIL (verify)"; $fail++; continue }

  if (-not $OutDir) {
    # 替换语义：原片移入视频旁 __mux_tmp_manual（同名加序号，永不覆盖已有备份）；失败时把原片移回原位并计 FAIL
    $bakDir = Join-Path $mkv.DirectoryName '__mux_tmp_manual'
    New-Item -ItemType Directory -Force -Path $bakDir | Out-Null
    $bakName = $mkv.Name
    $bakDest = Join-Path $bakDir $bakName
    $n = 1
    while (Test-Path -LiteralPath $bakDest) {
      $bakDest = Join-Path $bakDir ($mkv.BaseName + '_' + $n + $mkv.Extension)
      $n++
    }
    try {
      Move-Item -LiteralPath $mkv.FullName -Destination $bakDest
      Move-Item -LiteralPath $out -Destination $mkv.FullName
      Write-Host "  original kept in: $bakDest"
    } catch {
      # 失败：把原件移回原位（若已被移走）
      if (Test-Path -LiteralPath $bakDest -and -not (Test-Path -LiteralPath $mkv.FullName)) {
        Move-Item -LiteralPath $bakDest -Destination $mkv.FullName -Force
      }
      Write-Host "FAIL (替换失败，原片已还原): $($_.Exception.Message)"
      $fail++
      continue
    }
  }
  Write-Host "OK: subs=$($newSubs.Count) fonts=$(@($vid.attachments).Count)"
  $ok++
}
Write-Host ""
Write-Host "===== DONE: OK=$ok SKIP=$skip FAIL=$fail ====="
