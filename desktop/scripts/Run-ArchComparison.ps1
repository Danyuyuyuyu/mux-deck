param(
    [Parameter(Mandatory)][string]$BaselineRoot,
    [Parameter(Mandatory)][string]$CurrentRoot,
    [Parameter(Mandatory)][string]$CaseManifest,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$Mkvmerge,
    [Parameter(Mandatory)][string]$Mkvextract
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$BaselineRoot = [IO.Path]::GetFullPath($BaselineRoot)
$CurrentRoot = [IO.Path]::GetFullPath($CurrentRoot)
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
if ((& git -C $BaselineRoot rev-parse --short HEAD) -ne 'e005923') { throw '基准工作树必须为 e005923。' }
if (Test-Path -LiteralPath $EvidenceDirectory) { throw '本次证据目录已存在，拒绝覆盖。' }
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null

# 两份独立 host 各自引用相应工作树的 Core，同一请求与工具根目录作为公共输入。
# 临时 host 不进解决方案、不改 Smoke；公开旧入口的结果和进度均写入本地证据。
$hostSource = @'
using System.Security.Cryptography;
using System.Text.Json;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;

var request = JsonSerializer.Deserialize<MuxRequest>(await File.ReadAllTextAsync(args[0]))!
    with { OutputDirectory = args[2] };
var inputs = new[] { request.VideoPath }.Concat(request.Subtitles.Select(s => s.Path))
    .Concat(request.Audios.Select(a => a.Path))
    .Concat(string.IsNullOrWhiteSpace(request.FontsDirectory) ? [] : Directory.EnumerateFiles(request.FontsDirectory))
    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
var before = inputs.ToDictionary(path => path, Hash);
var runner = new RecordingRunner();
var progress = new List<MuxProgress>();
var result = await new SingleMuxService(new Workspace(args[1]), runner).RunAsync(request, new Sink(progress.Add));
var after = inputs.ToDictionary(path => path, Hash);
await File.WriteAllTextAsync(args[3], JsonSerializer.Serialize(new { Request = request, Result = result,
    Progress = progress, InputHashes = before, InputHashesAfter = after, ToolBinaries = runner.Tools }, new JsonSerializerOptions { WriteIndented = true }));
if (before.Any(item => after[item.Key] != item.Value)) throw new InvalidOperationException("源输入哈希变化。");
if (result.Status != MuxStatus.Succeeded) throw new InvalidOperationException("对照入口未成功：" + result.Error);
Console.WriteLine("PAIRED OUTPUT: " + result.OutputPath);
sealed class Sink(Action<MuxProgress> action) : IProgress<MuxProgress>
{ public void Report(MuxProgress value) => action(value); }
sealed class RecordingRunner : IMuxToolRunner
{
    private readonly MuxToolRunner runner = new();
    public Dictionary<string, string> Tools { get; } = new();
    public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress, CancellationToken cancellationToken)
    {
        if (!Tools.ContainsKey(request.Executable))
        { using var stream = File.OpenRead(request.Executable); Tools.Add(request.Executable, Convert.ToHexString(SHA256.HashData(stream))); }
        return runner.RunAsync(request, progress, cancellationToken);
    }
}
'@
foreach ($side in @('baseline', 'current')) {
    $root = if ($side -eq 'baseline') { $BaselineRoot } else { $CurrentRoot }
    $directory = Join-Path $EvidenceDirectory ("host-" + $side)
    New-Item -ItemType Directory -Path $directory | Out-Null
    $core = [Security.SecurityElement]::Escape((Join-Path $root 'desktop/src/MuxDeck.Core/MuxDeck.Core.csproj'))
    $project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><ProjectReference Include="' + $core + '" /></ItemGroup></Project>'
    Set-Content -LiteralPath (Join-Path $directory 'host.csproj') -Value $project -Encoding utf8
    Set-Content -LiteralPath (Join-Path $directory 'Program.cs') -Value $hostSource -Encoding utf8
    & dotnet build (Join-Path $directory 'host.csproj') -c Release -p:UseSharedCompilation=false 2>&1 |
        Tee-Object -FilePath (Join-Path $EvidenceDirectory ("host-" + $side + "-build.txt"))
    if ($LASTEXITCODE -ne 0) { throw "对照 host 构建失败：$side" }
}
$cases = @(Get-Content -LiteralPath $CaseManifest -Raw | ConvertFrom-Json)
$completed = @()
foreach ($case in $cases) {
    if ($case.Name -notmatch '^[A-Za-z0-9-]+$' -or $case.Suite -notmatch '^[A-Za-z0-9-]+$') { throw '用例与套件名必须是安全的目录名。' }
    $directory = Join-Path $EvidenceDirectory ($case.Suite + '-' + $case.Name)
    New-Item -ItemType Directory -Path $directory | Out-Null
    foreach ($side in @('baseline', 'current')) {
        $project = Join-Path $EvidenceDirectory ("host-" + $side + '/host.csproj')
        $result = Join-Path $directory ($side + '-result.json')
        $output = Join-Path $directory ($side + '-output')
        & dotnet run --project $project --no-build -c Release -- $case.RequestPath $CurrentRoot $output $result 2>&1 |
            Tee-Object -FilePath (Join-Path $directory ($side + '-run.txt'))
        if ($LASTEXITCODE -ne 0) { throw "同输入任务执行失败：$($case.Name) $side" }
    }
    $old = Get-Content -LiteralPath (Join-Path $directory 'baseline-result.json') -Raw | ConvertFrom-Json
    $new = Get-Content -LiteralPath (Join-Path $directory 'current-result.json') -Raw | ConvertFrom-Json
    $report = Join-Path $directory 'comparison.json'
    try {
        $parameters = @{
            BaselineMedia = $old.Result.OutputPath; CurrentMedia = $new.Result.OutputPath
            BaselineResult = Join-Path $directory 'baseline-result.json'; CurrentResult = Join-Path $directory 'current-result.json'
            Mkvmerge = $Mkvmerge; Mkvextract = $Mkvextract; ReportPath = $report
        }
        & (Join-Path $PSScriptRoot 'Compare-ArchArtifacts.ps1') @parameters
        $completed += [ordered]@{ Suite = $case.Suite; Name = $case.Name; Status = '一致'; Report = $report }
    } catch {
        $completed += [ordered]@{ Suite = $case.Suite; Name = $case.Name; Status = '差异'; Report = $report; Detail = $_.Exception.Message }
        $completed | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'summary.json') -Encoding utf8
        throw
    }
}
$completed | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'summary.json') -Encoding utf8
