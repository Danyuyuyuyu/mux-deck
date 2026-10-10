namespace MuxDeck.Desktop.ViewModels;

public enum NotificationKind { Information, Busy, Success, Warning, Error }

public sealed class InteractionNotification : ObservableObject
{
    private string message = "";
    private NotificationKind kind;
    private bool visible;
    private long revision;
    public string Message => message;
    public NotificationKind Kind => kind;
    public bool IsVisible => visible;
    public long Revision => revision;
    public bool IsBusy => kind == NotificationKind.Busy;
    public bool IsSuccess => kind == NotificationKind.Success;
    public bool IsWarning => kind == NotificationKind.Warning;
    public bool IsError => kind == NotificationKind.Error;
    public bool CanDismiss => !IsBusy;
    public string Icon => kind switch { NotificationKind.Busy => "◌", NotificationKind.Success => "✓", NotificationKind.Warning => "!", NotificationKind.Error => "×", _ => "i" };
    public TimeSpan? Lifetime => kind switch { NotificationKind.Success => TimeSpan.FromSeconds(5), NotificationKind.Information => TimeSpan.FromSeconds(4), _ => null };

    public void Show(string text, NotificationKind severity)
    {
        // 相同且仍可见的反馈不重启计时，避免连续拒绝时闪烁或不断延长。
        if (visible && message == text && kind == severity) return;
        message = text; kind = severity; visible = !string.IsNullOrWhiteSpace(text); revision++;
        foreach (var name in new[] { nameof(Message), nameof(Kind), nameof(IsVisible), nameof(IsBusy), nameof(IsSuccess), nameof(IsWarning), nameof(IsError), nameof(CanDismiss), nameof(Icon), nameof(Lifetime), nameof(Revision) }) Raise(name);
    }
    public void Dismiss(long expectedRevision)
    {
        // 迟到的计时只拥有原通知，不能清掉后来产生的错误。
        if (expectedRevision != revision || !visible) return;
        visible = false; Raise(nameof(IsVisible));
    }
}

public sealed class MediaDropFeedback : ObservableObject
{
    private bool visible, accepts;
    private string reason = "";
    public bool IsVisible => visible;
    public bool CanAccept => accepts;
    public string Title => accepts ? "释放以自动识别并添加" : "当前无法添加";
    public string Icon => accepts ? "↓" : "×";
    public string Description => accepts ? "视频、音频、ASS/SRT 字幕、字体文件或目录" : reason;
    public void Show(bool canAccept, string message)
    {
        visible = true; accepts = canAccept; reason = message;
        foreach (var name in new[] { nameof(IsVisible), nameof(CanAccept), nameof(Title), nameof(Description), nameof(Icon) }) Raise(name);
    }
    public void Clear() { if (!visible) return; visible = false; Raise(nameof(IsVisible)); }
}
