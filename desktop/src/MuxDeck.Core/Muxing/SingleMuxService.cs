using MuxDeck.Application;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;

namespace MuxDeck.Core.Muxing;

// 4a 前保留旧 API；流程只由 Application 用例实现。
public sealed class SingleMuxService
{
    private Workspace workspace;
    private readonly IMuxToolRunner runner;
    public SingleMuxService(Workspace workspace) : this(workspace, new MuxToolRunner()) { }
    public SingleMuxService(Workspace workspace, IMuxToolRunner runner)
    { this.workspace = workspace; this.runner = runner; }

    /// <summary>只改变下一次任务配置，已开始任务持有自己的不可变快照；4a 删除该入口。</summary>
    public void UpdateWorkspace(Workspace nextWorkspace)
    {
        if (string.IsNullOrWhiteSpace(nextWorkspace.Root)) throw new ArgumentException("工作区不能为空。", nameof(nextWorkspace));
        workspace = nextWorkspace;
    }

    public async Task<MediaProbe> ProbeAsync(string videoPath, CancellationToken cancellationToken = default) =>
        LegacyMuxContractConverter.ToLegacyProbe(await new MkvmergeProber(runner)
            .ProbeAsync(videoPath, (ToolConfiguration)workspace, cancellationToken).ConfigureAwait(false));

    public async Task<MuxResult> RunAsync(MuxRequest request, IProgress<MuxProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var configuration = workspace;
        var legacyWorkspace = new LegacyTaskWorkspaceFactory();
        try
        {
            // 在首次回调和等待前固定旧请求集合，后续调用方修改不影响本次任务与日志。
            request = request with { Subtitles = request.Subtitles?.ToArray()!, Audios = request.Audios?.ToArray()! };
            var spec = LegacyMuxContractConverter.ToRunSpec(request, configuration);
            var useCase = new RunSingleMuxUseCase(new MuxInputReader(), new ToolReadiness(runner),
                new MkvmergeProber(runner), new AfsSubsetter(runner), new MkvmergeMuxer(runner),
                new MkvextractExtractor(runner), new MediaReferenceBuilder(runner), new MuxEvidenceReader(),
                new NoOverwritePublisher(), legacyWorkspace);
            var result = await useCase.RunAsync(spec, new LegacyProgress(value =>
            {
                if (value.StepCode == "INPUT_VALIDATED")
                {
                    var snapshot = LegacyMuxContractConverter.NormalizeRequestForLog(request);
                    legacyWorkspace.Write("请求 " + System.Text.Json.JsonSerializer.Serialize(snapshot));
                    return;
                }
                var converted = LegacyMuxContractConverter.ToLegacyProgress(value);
                if (converted is null) return;
                legacyWorkspace.Write("阶段 " + converted.Stage + "：" + converted.Message);
                try { progress?.Report(converted); }
                catch (Exception ex) { legacyWorkspace.Write("进度订阅者异常：" + ex.Message); }
            }), cancellationToken).ConfigureAwait(false);
            return LegacyMuxContractConverter.ToLegacyResult(result);
        }
        catch (Exception ex)
        {
            return new(MuxStatus.Failed, null, ex.Message, null, []);
        }
    }

    private sealed class LegacyProgress(Action<JobProgress> action) : IProgress<JobProgress>
    { public void Report(JobProgress value) => action(value); }
}
