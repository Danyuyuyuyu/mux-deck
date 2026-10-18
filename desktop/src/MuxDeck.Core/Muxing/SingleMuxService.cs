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
    private readonly IPathFacts? pathFacts;
    private readonly Func<IReadOnlyList<InputItem>, IReadOnlyList<InputPathFacts>>? collectFacts;
    private readonly IPrepareImport? import;
    private readonly ISyncPathFacts? slotPathFacts;
    private readonly SingleMuxDraft? draft;
    public ToolConfiguration Tools { get; }
    public IProbeMedia Prober { get; }
    public IMuxInputReader InputReader { get; }
    public IPathFacts PathFacts => pathFacts ?? throw new InvalidOperationException("SingleMuxService 未装配异步路径端口。");
    // 拖入反馈的同步入口：与异步端口同一份事实实现，由装配显式注入。
    public Func<IReadOnlyList<InputItem>, IReadOnlyList<InputPathFacts>> CollectFacts =>
        collectFacts ?? throw new InvalidOperationException("SingleMuxService 未装配同步批量事实入口。");
    // 固定槽位接纳使用裁剪后的完整路径语义；不能与拖入反馈的批量事实混用。
    public ISyncPathFacts SlotPathFacts =>
        slotPathFacts ?? throw new InvalidOperationException("SingleMuxService 未装配固定槽位事实端口。");
    // 导入用例以契约形式交付给视图层；默认实现只由调用方注入的端口组装。
    public IPrepareImport Import => import ?? throw new InvalidOperationException("SingleMuxService 未装配导入用例。");
    // ViewModel 的草稿门面：仍由 SlotPathFacts 收集固定槽位事实，不向视图暴露 Application 类型。
    public SingleMuxDraft Draft => draft ?? throw new InvalidOperationException("SingleMuxService 未装配固定槽位事实端口。");
    // 三参旧构造仅供只运行 RunAsync 的过渡测试；任何端口未装配时在使用处明确失败。
    public SingleMuxService(ToolConfiguration tools, IProbeMedia prober,
        Func<ITaskWorkspaceFactory, IRunSingleMux> createRun,
        IMuxInputReader? inputReader = null, IPathFacts? pathFacts = null,
        Func<IReadOnlyList<InputItem>, IReadOnlyList<InputPathFacts>>? collectFacts = null,
        IPrepareImport? import = null, ISyncPathFacts? slotPathFacts = null)
    {
        Tools = tools;
        Prober = prober;
        this.createRun = createRun;
        InputReader = inputReader ?? new MuxInputReader();
        this.pathFacts = pathFacts;
        this.collectFacts = collectFacts;
        this.slotPathFacts = slotPathFacts;
        this.draft = slotPathFacts is null ? null : new SingleMuxDraft(slotPathFacts);
        this.import = import ?? (pathFacts is null ? null : new PrepareImportUseCase(prober, InputReader, pathFacts));
    }

    public async Task<MediaProbe> ProbeAsync(string videoPath, CancellationToken cancellationToken = default) =>
        LegacyMuxContractConverter.ToLegacyProbe(await Prober.ProbeAsync(videoPath, Tools, cancellationToken).ConfigureAwait(false));

    public Task<MuxResult> RunAsync(MuxRequest request, IProgress<MuxProgress>? progress = null,
        CancellationToken cancellationToken = default) => RunAsync(request, Tools, progress, cancellationToken);

    public async Task<MuxResult> RunAsync(MuxRequest request, ToolConfiguration tools, IProgress<MuxProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 在首次回调和等待前固定旧请求集合，后续调用方修改不影响本次任务与日志。
        request = request with { Subtitles = request.Subtitles?.ToArray()!, Audios = request.Audios?.ToArray()! };
        var spec = LegacyMuxContractConverter.ToRunSpec(request, tools);
        return await ExecuteAsync(spec, tools, () => LegacyMuxContractConverter.NormalizeRequestForLog(request),
            progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>新入口：直接执行草稿规格；日志仍经旧请求转换生成，保证“请求 …”一行逐字不变。</summary>
    public Task<MuxResult> RunSpecAsync(MuxJobSpec spec, IProgress<MuxProgress>? progress = null,
        CancellationToken cancellationToken = default) => RunSpecAsync(spec, Tools, progress, cancellationToken);

    public Task<MuxResult> RunSpecAsync(MuxJobSpec spec, ToolConfiguration tools, IProgress<MuxProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(spec, tools,
            () => LegacyMuxContractConverter.NormalizeRequestForLog(LegacyMuxContractConverter.ToLegacyRequest(spec)),
            progress, cancellationToken);

    private async Task<MuxResult> ExecuteAsync(MuxJobSpec spec, ToolConfiguration configuration,
        Func<MuxRequest> createLogRequest, IProgress<MuxProgress>? progress, CancellationToken cancellationToken)
    {
        var legacyWorkspace = new LegacyTaskWorkspaceFactory();
        try
        {
            MuxRequest? logRequest = null;
            var useCase = createRun(legacyWorkspace);
            var result = await useCase.RunAsync(spec, new LegacyProgress(value =>
            {
                if (value.StepCode == "INPUT_VALIDATED")
                {
                    logRequest ??= createLogRequest();
                    legacyWorkspace.Write("请求 " + System.Text.Json.JsonSerializer.Serialize(logRequest));
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
