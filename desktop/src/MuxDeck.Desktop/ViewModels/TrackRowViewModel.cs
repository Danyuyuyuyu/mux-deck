namespace MuxDeck.Desktop.ViewModels;

public sealed class TrackRowViewModel : ObservableObject
{
    private string name = "";
    private bool isDefault;
    private bool isForced;
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string Codec { get; init; } = "";
    public string Source { get; init; } = "";
    public string Language { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Policy { get; init; } = "";
    public bool IsEditable { get; init; }
    public bool HasDefaultFlag { get; init; } = true;
    public bool HasForcedFlag { get; init; } = true;
    public string Name { get => name; set { if (Set(ref name, value)) Raise(nameof(DisplayName)); } }
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "（未命名）" : Name;
    public bool IsDefault { get => isDefault; set { if (Set(ref isDefault, value)) Raise(nameof(Flags)); } }
    public bool IsForced { get => isForced; set { if (Set(ref isForced, value)) Raise(nameof(Flags)); } }
    public string Flags => $"默认：{(!HasDefaultFlag ? "未声明" : IsDefault ? "是" : "否")} / 强制：{(!HasForcedFlag ? "未声明" : IsForced ? "是" : "否")}";
}
