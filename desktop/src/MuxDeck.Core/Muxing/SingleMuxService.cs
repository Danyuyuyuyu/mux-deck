using MuxDeck.Application;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;

namespace MuxDeck.Core.Muxing;

// 第 5 步前保留旧 API；流程依赖由装配提供，配置只在每次调用时取快照。
public sealed class SingleMuxService
{
    private readonly Func<ITaskWorkspaceFactory, IRunSingleMux> createRun;
    public ToolConfiguration Tools { get; }
    public IProbeMedia Prober { get; }
    public IMuxInputReader InputReader { get; }
    public IPathFacts PathFacts { get; }
    // 拖入反馈的同步入口：与异步端口同一份事实实现，由装配注入（默认取 Infrastructure 的实现）。
    public Func<IReadOnlyList<InputItem>, IReadOnlyList<InputPathFacts>> CollectFacts { get; }
    public static Func<IReadOnlyList<InputItem>, IReadOnlyList<InputPathFacts>> DefaultCollectFacts { get; } = SystemPathFacts.Instance.Collect;
    public static IPathFacts DefaultPathFacts { get; } = SystemPathFacts.Instance;
    // 导入用例以契约形式交付给视图层；默认实现由基础设施端口组装，装配可覆盖。
    public IPrepareImport Import { get; }
    // 仅为 4a-3 特征测试冻结的三参旧构造保留默认端口；生产对象图必须由 Composition 显式装配。
    public SingleMuxService(ToolConfiguration tools, IProbeMedia prober,
        Func<ITaskWorkspaceFactory, IRunSingleMux> createRun,
        IMuxInputReader? inputReader = null, IPathFacts? pathFacts = null,
        Func<IReadOnlyList<InputItem>, IReadOnlyList<InputPathFacts>>? collectFacts = null, IPrepareImport? import = null)
    {
        Tools = tools;
        Prober = prober;
        this.createRun = createRun;
        InputReader = inputReader ?? new MuxInputReader();
        PathFacts = pathFacts ?? DefaultPathFacts;
        CollectFacts = collectFacts ?? DefaultCollectFacts;
        Import = import ?? new PrepareImportUseCase(prober, InputReader, PathFacts);
    }

    public async Task<MediaProbe> ProbeAsync(string videoPath, CancellationToken cancellationToken = default) =>
        LegacyMuxContractConverter.ToLegacyProbe(await Prober.ProbeAsync(videoPath, Tools, cancellationToken).ConfigureAwait(false));

    public Task<MuxResult> RunAsync(MuxRequest request, IProgress<MuxProgress>? progress = null,
        CancellationToken cancellationToken = default) => RunAsync(request, Tools, progress, cancellationToken);

    public async Task<MuxResult> RunAsync(MuxRequest request, ToolConfiguration tools, IProgress<MuxProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var configuration = tools;
        var legacyWorkspace = new LegacyTaskWorkspaceFactory();
        try
        {
            // 在首次回调和等待前固定旧请求集合，后续调用方修改不影响本次任务与日志。
            request = request with { Subtitles = request.Subtitles?.ToArray()!, Audios = request.Audios?.ToArray()! };
            var spec = LegacyMuxContractConverter.ToRunSpec(request, configuration);
            var useCase = createRun(legacyWorkspace);
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
