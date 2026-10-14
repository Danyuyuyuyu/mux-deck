param(
    [Parameter(Mandatory)][string]$BaselineMedia,
    [Parameter(Mandatory)][string]$CurrentMedia,
    [Parameter(Mandatory)][string]$BaselineResult,
    [Parameter(Mandatory)][string]$CurrentResult,
    [Parameter(Mandatory)][string]$Mkvmerge,
    [Parameter(Mandatory)][string]$Mkvextract,
    [Parameter(Mandatory)][string]$ReportPath,
    [switch]$RawOnly,
    [string]$Python = "python"
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# 本地对照先保留全部原始差异，再只应用用户批准的 AFS 名称及 head 字段规则。
function InvokeMediaTool([string]$Executable, [string[]]$Arguments) {
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $start.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (!$process.Start()) { throw "工具无法启动：$Executable" }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "工具失败（$($process.ExitCode)）：$Executable $errorText" }
        return $output
    } finally { $process.Dispose() }
}
function Optional($Object, [string]$Name) {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}
function ReadMediaEvidence([string]$Media, [string]$Side) {
    $directory = Join-Path (Split-Path -Parent $ReportPath) $Side
    if (Test-Path -LiteralPath $directory) { throw "证据目录已存在，拒绝覆盖：$directory" }
    New-Item -ItemType Directory -Path $directory | Out-Null
    $probeText = InvokeMediaTool $Mkvmerge @('-J', $Media)
    Set-Content -LiteralPath (Join-Path $directory 'probe.json') -Value $probeText -Encoding utf8
    $probe = $probeText | ConvertFrom-Json
    $tracks = @()
    $attachments = @()
    $ass = @()
    $audio = @()
    foreach ($track in $probe.tracks) {
        $properties = $track.properties
        $tracks += [ordered]@{
            Id = $track.id; Type = $track.type; Codec = $track.codec
            CodecId = Optional $properties 'codec_id'
            Language = Optional $properties 'language'; LanguageIetf = Optional $properties 'language_ietf'
            Name = Optional $properties 'track_name'; Default = Optional $properties 'default_track'
            Forced = Optional $properties 'forced_track'; PixelDimensions = Optional $properties 'pixel_dimensions'
            AudioChannels = Optional $properties 'audio_channels'; AudioSamplingFrequency = Optional $properties 'audio_sampling_frequency'
        }
        if ((Optional $properties 'codec_id') -eq 'S_TEXT/ASS') {
            $file = Join-Path $directory ("track-" + $track.id + '.ass')
            InvokeMediaTool $Mkvextract @($Media, 'tracks', "$($track.id):$file") | Out-Null
            $ass += [ordered]@{ Id = $track.id; Text = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8) }
        }
        if ($track.type -eq 'audio') {
            $file = Join-Path $directory ("track-" + $track.id + '.fullraw')
            InvokeMediaTool $Mkvextract @($Media, 'tracks', '--fullraw', "$($track.id):$file") | Out-Null
            $audio += [ordered]@{ Id = $track.id; Sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
        }
    }
    foreach ($attachment in $probe.attachments) {
        $file = Join-Path $directory ("attachment-" + $attachment.id + '.bin')
        InvokeMediaTool $Mkvextract @($Media, 'attachments', "$($attachment.id):$file") | Out-Null
        $attachments += [ordered]@{
            Id = $attachment.id; Name = $attachment.file_name; ContentType = $attachment.content_type
            Size = $attachment.size; Sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        }
    }
    return [ordered]@{ Tracks = $tracks; Attachments = $attachments; Ass = $ass; AudioFullRaw = $audio }
}

$ReportPath = [System.IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
$old = ReadMediaEvidence $BaselineMedia 'baseline'
$new = ReadMediaEvidence $CurrentMedia 'current'
$oldResult = Get-Content -LiteralPath $BaselineResult -Raw | ConvertFrom-Json
$newResult = Get-Content -LiteralPath $CurrentResult -Raw | ConvertFrom-Json
$old['Checks'] = $oldResult.Result.Checks; $new['Checks'] = $newResult.Result.Checks
$old['Warnings'] = $oldResult.Result.Warnings; $new['Warnings'] = $newResult.Result.Warnings
$old['Progress'] = $oldResult.Progress; $new['Progress'] = $newResult.Progress
$old['InputHashes'] = $oldResult.InputHashes; $new['InputHashes'] = $newResult.InputHashes
$old['ToolBinaries'] = $oldResult.ToolBinaries; $new['ToolBinaries'] = $newResult.ToolBinaries
$comparisons = @()
foreach ($name in $old.Keys) {
    $oldJson = ConvertTo-Json -InputObject $old[$name] -Depth 50 -Compress
    $newJson = ConvertTo-Json -InputObject $new[$name] -Depth 50 -Compress
    $comparisons += [ordered]@{
        Name = $name; Equal = [string]::Equals($oldJson, $newJson, [System.StringComparison]::Ordinal)
        Baseline = $old[$name]; Current = $new[$name]
    }
}
$different = @($comparisons | Where-Object { !$_.Equal })
$report = [ordered]@{
    Date = [DateTimeOffset]::Now.ToString('o'); BaselineMedia = $BaselineMedia; CurrentMedia = $CurrentMedia
    Mkvmerge = $Mkvmerge; Mkvextract = $Mkvextract
    BaselineResult = $BaselineResult; CurrentResult = $CurrentResult
    Status = $(if ($different.Count) { '差异' } else { '一致' })
    Comparisons = $comparisons
}
$rawPath = if ($RawOnly) { $ReportPath } else { $ReportPath + '.raw.json' }
$report | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $rawPath -Encoding utf8
if (!$RawOnly) {
    $normalized = Join-Path (Split-Path -Parent $ReportPath) 'normalized'
    & $Python -X utf8 (Join-Path $PSScriptRoot 'Normalize-AfsEvidence.py') --report $rawPath --output-directory $normalized
    $normalizationExit = $LASTEXITCODE
    $normalizationReport = Join-Path $normalized 'comparison.json'
    if (!(Test-Path -LiteralPath $normalizationReport) -or $normalizationExit -gt 1) {
        throw 'AFS 证据处理失败，不能宣称一致。'
    }
    Copy-Item -LiteralPath $normalizationReport -Destination $ReportPath
    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    $different = @($report.Comparisons | Where-Object { !$_.Equal })
}

if ($different.Count) { throw "逐成品对照发现差异，停止：$($different.Name -join ', ')；报告：$ReportPath" }
Write-Output "一致：$ReportPath"
