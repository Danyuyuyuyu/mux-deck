<#
.SYNOPSIS
  Manually mux an MKV: pick the video, SC/TC ASS subtitles, fonts and audio tracks.

.DESCRIPTION
  Assembles ONE output MKV from explicit inputs:
    - source video file
    - simplified-Chinese ASS subtitle (optional)
    - traditional-Chinese ASS subtitle (optional)
    - font directory used by assfonts to build embedded font subsets
    - audio: keep all / keep none / pick source tracks by id or language,
      or add an external audio file; the two selections are independent
  Source video subtitle tracks are dropped whenever ASS subs are given.
  Source font attachments are rebuilt only when ASS subs are given;
  without new subs the source attachments are kept.

.PARAMETER Video
  Path to the source video file (mandatory).

.PARAMETER ScSub
  Path to the simplified Chinese ASS subtitle. Optional.

.PARAMETER TcSub
  Path to the traditional Chinese ASS subtitle. Optional.

.PARAMETER FontsDir
  Directory containing the fonts needed by the subs.
  Default: "Fonts" or "Font" folder next to the video.

.PARAMETER Audio
  External audio file (preferred) or legacy source-track selector.
  An existing file path is always appended as an independent external
  track alongside whatever -AudioTracks selects. Only when -AudioTracks
  is empty AND -Audio is not an existing file is it parsed as a legacy
  selector (default "all"):
    all            keep every audio track from the source
    none           drop all audio
    1,2,3          keep source audio tracks with these mkvmerge track ids
    jpn,eng        keep source audio tracks whose language code matches

.PARAMETER KeepSrcAudio
  Deprecated and ignored; source audio selection is done with -AudioTracks.

.PARAMETER AudioLang
  Language code (e.g. jpn) applied to the external audio file.

.PARAMETER AudioName
  Track name applied to the external audio file.

.PARAMETER ScLang / ScName / TcLang / TcName
  Language codes and track names for the added subtitle tracks.
  Defaults: zh-Hans/SC (default flag ON), zh-Hant/TC (default flag OFF).

.PARAMETER OutDir
  Where to write the result. Default: replace the source file
  (the original is moved to "__mux_tmp_manual" next to it).

.PARAMETER Force
  Proceed even if the source video already has font attachments.

.PARAMETER NoBackup
  When replacing the source file (no -OutDir), delete the original instead of
  moving it to "__mux_tmp_manual".

.PARAMETER AudioTracks
  Source audio selection, independent of -Audio: "" keeps all source
  audio tracks, "none" drops them all, "1,3" keeps only these mkvmerge
  track ids. UI: tick boxes in track probe.

.PARAMETER SubtitleTracks
  Comma-separated mkvmerge track ids of SOURCE subtitle tracks to keep.
  When given, source subs are kept alongside any new SC/TC subs.

.PARAMETER KeepAttachments
  Keep the source file attachments (fonts/cover) instead of rebuilding them.

.EXAMPLE
  .\ass_mux_manual.ps1 -Video "D:\v.mkv" -ScSub "D:\v.sc.ass" -TcSub "D:\v.tc.ass" -FontsDir "D:\Font" -Audio "jpn"

.EXAMPLE
  .\ass_mux_manual.ps1 -Video "D:\v.mkv" -ScSub "D:\v.sc.ass" -Audio "D:\track.flac" -AudioLang "jpn" -KeepSrcAudio -OutDir "D:\out"
#>
param(
  [Parameter(Mandatory=$true)][string]$Video,
  [string]$ScSub,
  [string]$TcSub,
  [string]$FontsDir,
  [string]$Audio = "all",
  [switch]$KeepSrcAudio,
  [string]$AudioLang = "",
  [string]$AudioName = "",
  [string]$ScLang = "zh-Hans",
  [string]$ScName = "SC",
  [string]$TcLang = "zh-Hant",
  [string]$TcName = "TC",
  [string]$OutDir = "",
  [switch]$Force,
  [switch]$NoBackup,
  [string]$AudioTracks = "",
  [string]$SubtitleTracks = "",
  [switch]$KeepAttachments
)

$ErrorActionPreference = "Stop"

# UTF-8 console I/O: keep mkvmerge's UTF-8 pipe output intact through PowerShell (mojibake fix)
$__utf8 = New-Object System.Text.UTF8Encoding($false)
try { [Console]::InputEncoding = $__utf8 } catch {}
try { [Console]::OutputEncoding = $__utf8 } catch {}

# script-scope temp dir; Fail() removes it on error (H4)
$tmp = ""

function Fail([string]$msg) {
  Write-Host ("FAIL: " + $msg) -ForegroundColor Red
  if ($tmp -and (Test-Path -LiteralPath $tmp)) {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
  }
  exit 1
}

# ---------- tool lookup: prefer known install locations, fall back to PATH (I5) ----------
$mkvmerge = Join-Path (Split-Path -Parent $PSScriptRoot) "bin\mkvtoolnix\mkvmerge.exe"
if (-not (Test-Path -LiteralPath $mkvmerge)) { $mkvmerge = "C:\Program Files\MKVToolNix\mkvmerge.exe" }
if (-not (Test-Path -LiteralPath $mkvmerge)) {
  $cmd = Get-Command mkvmerge -ErrorAction SilentlyContinue
  if ($cmd) { $mkvmerge = $cmd.Source }
  else { Fail "mkvmerge not found; put bin\mkvtoolnix next to this script, install MKVToolNix, or add it to PATH" }
}
$assfonts = Join-Path (Split-Path -Parent $PSScriptRoot) "bin\assfonts\assfonts.exe"
if (-not (Test-Path -LiteralPath $assfonts)) {
  $cmd = Get-Command assfonts -ErrorAction SilentlyContinue
  if ($cmd) { $assfonts = $cmd.Source }
  else { Fail "assfonts not found; expected next to this script or on PATH" }
}

# ---------- validate inputs ----------
if (-not (Test-Path -LiteralPath $Video)) { Fail "video not found: $Video" }
$videoDir = [IO.Path]::GetDirectoryName($Video)
$base = [IO.Path]::GetFileNameWithoutExtension($Video)
$ext = [IO.Path]::GetExtension($Video)

if (-not $FontsDir) {
  if (Test-Path -LiteralPath (Join-Path $videoDir "Fonts")) { $FontsDir = Join-Path $videoDir "Fonts" }
  elseif (Test-Path -LiteralPath (Join-Path $videoDir "Font")) { $FontsDir = Join-Path $videoDir "Font" }
}
if (-not $FontsDir) { Fail "no font dir given and none found next to the video; use -FontsDir" }
if (-not (Test-Path -LiteralPath $FontsDir)) { Fail "font dir not found: $FontsDir" }

$subs = @()
if ($ScSub) {
  if (-not (Test-Path -LiteralPath $ScSub)) { Fail "sc subtitle not found: $ScSub" }
  $subs += $ScSub
}
if ($TcSub) {
  if (-not (Test-Path -LiteralPath $TcSub)) { Fail "tc subtitle not found: $TcSub" }
  $subs += $TcSub
}

Write-Host ("Source : " + $Video)
Write-Host ("Fonts  : " + $FontsDir)
if ($subs.Count -eq 0) { Write-Host "Subs   : (none - source subtitles kept)" }
else { Write-Host ("Subs   : " + ($subs -join ", ")) }

# ---------- source info ----------
$j = & $mkvmerge -J $Video | Out-String | ConvertFrom-Json
$attachCount = @($j.attachments).Count
# only guard when we would rebuild attachments (new ASS subs are given)
if ($subs.Count -gt 0 -and $attachCount -gt 0 -and -not $Force -and -not $KeepAttachments) {
  Fail ("source already has " + $attachCount + " attachments; use -Force to re-mux anyway")
}

# ---------- temp work dir ----------
$tmp = Join-Path $env:TEMP ("manual_mux_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmp | Out-Null
$subsetDir = Join-Path $tmp "subs"
New-Item -ItemType Directory -Path $subsetDir | Out-Null

# ---------- assfonts subset ----------
if ($subs.Count -gt 0) {
  Write-Host "assfonts: building database and subsetting fonts..."
  $dbDir = Join-Path $tmp "db"
  New-Item -ItemType Directory -Path $dbDir | Out-Null
  Push-Location $tmp
  & $assfonts -f $FontsDir -b -d $dbDir *> "$tmp\assfonts_build.log"
  $code = $LASTEXITCODE
  Pop-Location
  if ($code -ne 0) { Fail ("assfonts database build failed; log: " + $tmp + "\assfonts_build.log") }
  $db = Join-Path $dbDir "fonts.json"
  if (-not (Test-Path -LiteralPath $db)) { Fail ("fonts database not created at " + $db) }
  $sargs = @("-f", $FontsDir, "-s", "-c", "-d", $dbDir, "-o", $subsetDir) + $subs
  & $assfonts @sargs *> "$tmp\assfonts_subset.log"
  if ($LASTEXITCODE -ne 0) { Fail ("assfonts subset failed (missing fonts?); log: " + $tmp + "\assfonts_subset.log") }
}

$fonts = @()
$sf = Join-Path $subsetDir "subsetted_fonts"
if (Test-Path -LiteralPath $sf) {
  $fonts = @(Get-ChildItem -LiteralPath $sf -File | Where-Object { $_.Extension -match '^\.(ttf|otf|ttc|otc|woff2?)$' })
}
Write-Host ("Fonts to embed: " + $fonts.Count)

# ---------- audio selection (orthogonal: source tracks vs. external file) ----------
$audioArgs = @()
$extAudio = ""
# -AudioTracks: "" = keep all source audio, "none" = drop all, "1,3" = keep these ids
if ($AudioTracks -eq "none") {
  $audioArgs += "--no-audio"
} elseif ($AudioTracks) {
  $audioArgs += "--audio-tracks", $AudioTracks
}
# -Audio: an existing file is always an independent external track;
# otherwise it is a legacy selector, used only when -AudioTracks is not given.
if ($Audio -and (Test-Path -LiteralPath $Audio -PathType Leaf)) {
  $extAudio = $Audio
} elseif (-not $AudioTracks -and $Audio) {
  if ($Audio -eq "none") {
    $audioArgs += "--no-audio"
  } elseif ($Audio -and $Audio -ne "all") {
    if ($Audio -match '^[\d,\s]+$') {
      $audioArgs += "--audio-tracks", $Audio
    } else {
      $langs = @($Audio -split ',' | ForEach-Object { $_.Trim().ToLower() } | Where-Object { $_ })
      $at = @($j.tracks | Where-Object {
        $_.type -eq "audio" -and $null -ne $_.properties.language -and
        $langs -contains $_.properties.language.ToLower()
      } | ForEach-Object { $_.id })
      if ($at.Count -eq 0) { Fail ("no source audio track matches language: " + $Audio) }
      $audioArgs += "--audio-tracks", ($at -join ",")
    }
  }
}

# ---------- build mkvmerge command ----------
$outTmp = Join-Path $tmp ($base + ".muxed" + $ext)
$margs = @("-o", $outTmp)
if ($SubtitleTracks) {
  $margs += "--subtitle-tracks", $SubtitleTracks
} elseif ($subs.Count -gt 0) {
  $margs += "--no-subtitles"
}
# no new subset fonts -> keep source attachments (mkvmerge copies them by default)
if ($subs.Count -gt 0 -and -not $KeepAttachments) { $margs += "--no-attachments" }
$margs += $audioArgs
$margs += $Video
$scDef = "0:1"
$tcDef = "0:0"
if (-not $ScSub -and $TcSub) { $tcDef = "0:1" }
if ($ScSub) {
  $margs += "--language", ("0:" + $ScLang), "--track-name", ("0:" + $ScName), "--default-track-flag", $scDef, $ScSub
}
if ($TcSub) {
  $margs += "--language", ("0:" + $TcLang), "--track-name", ("0:" + $TcName), "--default-track-flag", $tcDef, $TcSub
}
if ($extAudio) {
  if ($AudioLang) { $margs += "--language", ("0:" + $AudioLang) }
  if ($AudioName) { $margs += "--track-name", ("0:" + $AudioName) }
  $margs += $extAudio
}
foreach ($f in $fonts) {
  $mime = "application/x-truetype-font"
  if ($f.Extension -match '^\.woff2?$') { $mime = "application/font-woff" }
  $margs += "--attachment-mime-type", $mime, "--attach-file", $f.FullName
}

Write-Host "Muxing..."
& $mkvmerge @margs *>&1 | Tee-Object -FilePath "$tmp\mux.log"
if ($LASTEXITCODE -ne 0) { Fail ("mkvmerge failed; log: " + $tmp + "\mux.log") }

# ---------- verify ----------
$vo = & $mkvmerge -J $outTmp | Out-String | ConvertFrom-Json
$st = @($vo.tracks | Where-Object { $_.type -eq "subtitles" }).Count
$keptSubs = 0
if ($SubtitleTracks) {
  $keepIds = @($SubtitleTracks -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
  $keptSubs = @($j.tracks | Where-Object { $_.type -eq "subtitles" -and $keepIds -contains [string]$_.id }).Count
} elseif ($subs.Count -eq 0) {
  $keptSubs = @($j.tracks | Where-Object { $_.type -eq "subtitles" }).Count
}
$expectSubs = $subs.Count + $keptSubs
if ($st -ne $expectSubs) { Fail ("expected " + $expectSubs + " subtitle tracks, got " + $st) }
Write-Host "--- Result ---"
foreach ($tr in $vo.tracks) {
  $lang = $tr.properties.language
  if (-not $lang) { $lang = "-" }
  $name = $tr.properties.track_name
  if (-not $name) { $name = "" }
  $flag = $tr.properties.default_track
  if ($null -eq $flag) { $flag = $false }
  Write-Host ("  track {0}: {1}  lang={2}  name={3}  default={4}" -f $tr.id, $tr.type, $lang, $name, $flag)
}
Write-Host ("  attachments: " + @($vo.attachments).Count)

# ---------- install ----------
if ($OutDir) {
  New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
  $dest = Join-Path $OutDir ($base + $ext)
  Move-Item -LiteralPath $outTmp -Destination $dest -Force
  Write-Host ("OK -> " + $dest) -ForegroundColor Green
} else {
  $dest = Join-Path $videoDir ($base + $ext)
  if ($NoBackup) {
    # safe order (H2): stage original in $tmp, land the output, delete staging only on success
    $staged = Join-Path $tmp ($base + $ext)
    try {
      Move-Item -LiteralPath $Video -Destination $staged -Force
    } catch {
      Fail ("cannot stage the original: " + $_.Exception.Message)
    }
    try {
      Move-Item -LiteralPath $outTmp -Destination $dest -Force
    } catch {
      $err = $_.Exception.Message
      Move-Item -LiteralPath $staged -Destination $Video -Force -ErrorAction SilentlyContinue
      if (Test-Path -LiteralPath $staged) {
        # the staged original lives inside $tmp; Fail() would wipe it - rescue it first
        $rescue = Join-Path $videoDir ($base + ".restore_failed" + $ext)
        $rn = 1
        while (Test-Path -LiteralPath $rescue) { $rescue = Join-Path $videoDir ($base + ".restore_failed." + $rn + $ext); $rn++ }
        Move-Item -LiteralPath $staged -Destination $rescue -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $rescue) {
          Fail ("install failed; original could not return to its path, rescued to " + $rescue + ": " + $err)
        } else {
          $tmp = ""  # keep the temp dir so the staged original survives Fail()'s cleanup
          Fail ("install failed; original kept at " + $staged + " (temp dir preserved): " + $err)
        }
      } else {
        Fail ("install failed, original restored: " + $err)
      }
    }
    Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue
    Write-Host ("OK -> " + $dest + "  (original deleted, no backup)") -ForegroundColor Green
  } else {
    $bakDir = Join-Path $videoDir "__mux_tmp_manual"
    New-Item -ItemType Directory -Path $bakDir -Force | Out-Null
    # never overwrite an existing backup (H1): pick the first free name (EP01.mkv, EP01.1.mkv, ...)
    $bakDest = Join-Path $bakDir ($base + $ext)
    $bakN = 1
    while (Test-Path -LiteralPath $bakDest) {
      $bakDest = Join-Path $bakDir ($base + "." + $bakN + $ext)
      $bakN++
    }
    Move-Item -LiteralPath $Video -Destination $bakDest
    try {
      Move-Item -LiteralPath $outTmp -Destination $dest -Force
    } catch {
      $err = $_.Exception.Message
      Move-Item -LiteralPath $bakDest -Destination $Video -Force -ErrorAction SilentlyContinue
      if (Test-Path -LiteralPath $bakDest) {
        Fail ("install failed and the backup could not be moved back (left at " + $bakDest + "): " + $err)
      } else {
        Fail ("install failed, backup moved back to its original location: " + $err)
      }
    }
    Write-Host ("OK -> " + $dest + "  (original kept in " + $bakDest + ")") -ForegroundColor Green
  }
}
Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
