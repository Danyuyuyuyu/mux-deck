namespace MuxDeck.Desktop.ViewModels;

public sealed class TrackRowViewModel : ObservableObject
{
    private string name = "";
    private bool isDefault;
    private bool isForced;
    private bool isIncluded = true;
    private bool isSelected;
    private string inputPath = "";
    private Func<bool>? canEdit;
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
    public string Name { get => name; set { if (CanChange && Set(ref name, value)) { Raise(nameof(DisplayName)); Raise(nameof(AccessibleName)); } } }
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "（未命名）" : Name;
    public bool IsDefault { get => isDefault; set { if (CanChange && Set(ref isDefault, value)) { Raise(nameof(Flags)); Raise(nameof(DefaultText)); } } }
    public bool IsForced { get => isForced; set { if (CanChange && Set(ref isForced, value)) Raise(nameof(Flags)); } }
    public bool IsIncluded { get => isIncluded; set { if (CanChange && Set(ref isIncluded, value)) { Raise(nameof(InclusionText)); Raise(nameof(InclusionBox)); } } }
    public bool IsSelected { get => isSelected; internal set => Set(ref isSelected, value); }
    // 展示投影：源行按固定策略表现包含状态，不直接绑定默认为 true 的 IsIncluded。
    public bool InclusionBox { get => IsEditable ? isIncluded : Policy == "保留"; set { if (CanChange) IsIncluded = value; } }
    public bool InclusionBoxEnabled => IsEditable && CanChange;
    public string InclusionText => IsIncluded ? "加入" : "暂不加入";
    public string InputPath { get => inputPath; internal set => Set(ref inputPath, value); }
    public bool IsExternal => IsEditable;
    public bool CanEdit => IsEditable && CanChange;
    public string SourceLabel => IsEditable ? "外加文件" : "源视频 · 内封";
    public string InspectorTitle => (IsEditable ? "外加字幕" : "源内容") + " · " + Id;
    // 行选择控件的可访问名称：必须辨认 SC/TC 与来源，不能退化为控件类型文本。
    public string AccessibleName => $"查看 {Id} {Type} 属性（{DisplayName}）";
    private bool CanChange => canEdit?.Invoke() ?? true;
    internal void SetEditGuard(Func<bool> guard) => canEdit = guard;
    internal void NotifyEditability() { Raise(nameof(CanEdit)); Raise(nameof(InclusionBoxEnabled)); }
    public string Flags => $"默认：{(!HasDefaultFlag ? "未声明" : IsDefault ? "是" : "否")} / 强制：{(!HasForcedFlag ? "未声明" : IsForced ? "是" : "否")}";
    public string DefaultText => !HasDefaultFlag ? "未声明" : IsDefault ? "是" : "否";
}
