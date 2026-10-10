using MuxDeck.Desktop.Preferences;

namespace MuxDeck.Desktop.ViewModels;

public sealed record TrackColumn(string Key, string Title, double DefaultWidth, double MinimumWidth);

// 所有表头和行共享一个列模型；界面偏好不进入 DraftRevision 或封装请求。
public sealed class TrackColumnLayout : ObservableObject
{
    public static IReadOnlyList<TrackColumn> Columns { get; } = Array.AsReadOnly(new[]
    {
        new TrackColumn("included", "纳入本次封装", 36, 36),
        new TrackColumn("id", "ID", 48, 40),
        new TrackColumn("type", "类型", 104, 80),
        new TrackColumn("language", "语言", 112, 88),
        new TrackColumn("name", "名称", 190, 120),
        new TrackColumn("default", "默认", 56, 48),
        new TrackColumn("forced", "强制", 56, 48),
        new TrackColumn("codec", "编码", 180, 140)
    });
    private readonly UserPreferencesStore? preferences;
    private readonly Dictionary<string, double> overrides = new();
    private readonly double[] widths = new double[8];
    private double availableWidth;
    private Task saveTask = Task.CompletedTask;
    public TrackColumnLayout(UserPreferencesStore? preferences = null)
    {
        this.preferences = preferences;
        if (preferences is not null)
            foreach (var pair in preferences.LoadTrackWidths())
                if (Columns.FirstOrDefault(c => c.Key == pair.Key) is { } column && double.IsFinite(pair.Value))
                    overrides[pair.Key] = Math.Clamp(pair.Value, column.MinimumWidth, 2000);
        Recalculate();
    }
    public IReadOnlyList<double> Widths => widths;
    public double TotalWidth => widths.Sum();
    public double ContentWidth => Math.Max(availableWidth, TotalWidth) + 2;
    public void SetAvailableWidth(double value)
    {
        value = double.IsFinite(value) ? Math.Max(0, value - 2) : 0;
        if (Math.Abs(availableWidth - value) < 0.5) return;
        availableWidth = value;
        Recalculate();
    }
    public void Resize(int index, double value)
    {
        if (index < 0 || index >= Columns.Count || !double.IsFinite(value)) return;
        overrides[Columns[index].Key] = Math.Clamp(value, Columns[index].MinimumWidth, 2000);
        Recalculate();
    }
    public void Reset(int? index = null)
    {
        if (index is { } i && i >= 0 && i < Columns.Count) overrides.Remove(Columns[i].Key);
        else if (index is null) overrides.Clear();
        Recalculate();
    }
    private void Recalculate()
    {
        for (var i = 0; i < Columns.Count; i++) widths[i] = overrides.GetValueOrDefault(Columns[i].Key, Columns[i].DefaultWidth);
        if (!overrides.ContainsKey("name")) widths[4] = Math.Max(Columns[4].MinimumWidth, availableWidth - widths.Where((_, i) => i != 4).Sum());
        Raise(nameof(Widths)); Raise(nameof(TotalWidth)); Raise(nameof(ContentWidth));
    }
    // 快照在 UI 线程取得；后台只处理文件，按调用顺序写入。关闭窗口等待最后一次保存。
    public Task PersistAsync()
    {
        if (preferences is null) return Task.CompletedTask;
        var snapshot = new Dictionary<string, double>(overrides);
        saveTask = SaveAfterAsync(saveTask, snapshot);
        return saveTask;
    }
    private async Task SaveAfterAsync(Task previous, IReadOnlyDictionary<string, double> snapshot)
    {
        try { await previous.ConfigureAwait(false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        await preferences!.SaveTrackWidthsAsync(snapshot).ConfigureAwait(false);
    }
    public Task FlushAsync() => saveTask;
}
