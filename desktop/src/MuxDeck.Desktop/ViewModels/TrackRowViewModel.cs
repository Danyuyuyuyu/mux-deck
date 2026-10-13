using MuxDeck.Domain;
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
    private string displayId = "";
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
    public string DisplayName => Name;
    public string DisplayId { get => string.IsNullOrEmpty(displayId) ? Id : displayId; internal set { if (Set(ref displayId, value)) { Raise(nameof(InspectorTitle)); Raise(nameof(AccessibleName)); Raise(nameof(IdTip)); Raise(nameof(InclusionAccessibleName)); } } }
    public string IdTip => IsEditable ? $"外加显示编号 {DisplayId}；输入 Track ID {OriginTrackId}，不是成品 Track ID" : $"源文件轨道 ID：{Id}";
    public string LanguageName => LanguageDisplay.Name(Language);
    public string? LegacyLanguage { get; init; }
    public string? LanguageIetf { get; init; }
    public string? CodecId { get; init; }
    public int OriginTrackId { get; init; }
    public string? PixelDimensions { get; init; }
    public int? AudioChannels { get; init; }
    public string LanguageTip => LanguageIetf is not null && LegacyLanguage is not null ? $"{LanguageName} · 原始 BCP 47: {LanguageIetf} · 原始 ISO 639: {LegacyLanguage}" : $"{LanguageName} · {Language}";
    public bool IsVideo => Type == "视频";
    public bool IsAudio => Type == "音频";
    public bool IsSubtitle => Type == "字幕";
    public bool IsDefault { get => isDefault; set { if (CanChange && Set(ref isDefault, value)) { Raise(nameof(Flags)); Raise(nameof(DefaultText)); Raise(nameof(DefaultMark)); Raise(nameof(DefaultTip)); } } }
    public bool IsForced { get => isForced; set { if (CanChange && Set(ref isForced, value)) { Raise(nameof(Flags)); Raise(nameof(ForcedMark)); Raise(nameof(ForcedTip)); } } }
    public string DefaultMark => HasDefaultFlag && IsDefault ? "✓" : "";
    public string ForcedMark => IsSubtitle && HasForcedFlag && IsForced ? "✓" : "";
    public string DefaultTip => !HasDefaultFlag ? "源文件未声明默认标记" : IsDefault ? "默认：开启" : "默认：关闭";
    public string ForcedTip => !IsSubtitle ? "强制标记仅适用于字幕" : !HasForcedFlag ? "源文件未声明强制标记" : IsForced ? "强制：开启" : "强制：关闭";
    public string InclusionTip => IsEditable ? "纳入本次封装" : "源视频轨道当前为只读；" + (InclusionBox ? "本次保留" : "本次不保留");
    public bool IsIncluded { get => isIncluded; set { if (CanChange && Set(ref isIncluded, value)) { Raise(nameof(InclusionText)); Raise(nameof(InclusionBox)); } } }
    public bool IsSelected { get => isSelected; internal set => Set(ref isSelected, value); }
    // 展示投影：源行按固定策略表现包含状态，不直接绑定默认为 true 的 IsIncluded。
    public bool InclusionBox { get => TrackPolicy.IsIncluded(IsEditable, isIncluded, Policy == "保留"); set { if (CanChange) IsIncluded = value; } }
    public bool InclusionBoxEnabled => IsEditable && CanChange;
    public string InclusionText => IsIncluded ? "加入" : "暂不加入";
    public string InputPath { get => inputPath; internal set => Set(ref inputPath, value); }
    public bool IsExternal => IsEditable;
    public bool CanEdit => IsEditable && CanChange;
    public string SourceLabel => IsEditable ? "外加文件" : "源视频 · 内封";
    public string InspectorTitle => (IsEditable ? "外加" + Type : "源" + Type) + " #" + DisplayId;
    // 行选择控件的可访问名称：必须辨认 SC/TC 与来源，不能退化为控件类型文本。
    public string AccessibleName => (Id.StartsWith("external-", StringComparison.Ordinal) ? $"查看外加{Type} #{DisplayId} 属性" : $"查看 {Id} {Type} 属性 · ID {DisplayId}") + (string.IsNullOrWhiteSpace(Name) ? "" : $"（{Name}）");
    public string InclusionAccessibleName => $"纳入本次封装 · {SourceLabel} {Type} #{DisplayId}";
    private bool CanChange => canEdit?.Invoke() ?? true;
    internal void SetEditGuard(Func<bool> guard) => canEdit = guard;
    internal void NotifyEditability() { Raise(nameof(CanEdit)); Raise(nameof(InclusionBoxEnabled)); }
    public string Flags => $"默认：{(!HasDefaultFlag ? "未声明" : IsDefault ? "是" : "否")} / 强制：{(!HasForcedFlag ? "未声明" : IsForced ? "是" : "否")}";
    public string DefaultText => !HasDefaultFlag ? "未声明" : IsDefault ? "是" : "否";
}
