using MuxDeck.Core.Muxing;

namespace MuxDeck.Desktop.ViewModels;

internal enum DraftEntry { Prepare, Commit, Remove, SetIncluded, Import }

/// <summary>
/// 草稿结果到中文文案的唯一映射：按（入口, 操作, 错误码, 原因）区分旧文案。
/// 界面不得解析 Detail 文本；Detail 只作为异常类文案的原文附加。
/// </summary>
internal static class DraftText
{
    public static InputAcceptanceError ToError(DraftErrorKind error) => error switch
    {
        DraftErrorKind.Locked => InputAcceptanceError.Locked,
        DraftErrorKind.Stale => InputAcceptanceError.Stale,
        DraftErrorKind.ConfirmationRequired => InputAcceptanceError.ConfirmationRequired,
        DraftErrorKind.InvalidCount => InputAcceptanceError.InvalidCount,
        DraftErrorKind.InvalidSlot => InputAcceptanceError.InvalidSlot,
        DraftErrorKind.Duplicate => InputAcceptanceError.Duplicate,
        DraftErrorKind.NonLocal => InputAcceptanceError.NonLocal,
        DraftErrorKind.InvalidPath => InputAcceptanceError.InvalidPath,
        DraftErrorKind.WrongType => InputAcceptanceError.WrongType,
        DraftErrorKind.MissingPath => InputAcceptanceError.MissingPath,
        // 外加轨道缺失在旧接纳枚举中没有独立值，按缺路径处理。
        DraftErrorKind.ExternalTrackMissing => InputAcceptanceError.MissingPath,
        DraftErrorKind.None => throw new ArgumentOutOfRangeException(nameof(error), error, "成功结果没有错误码。"),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "未知的草稿错误码。")
    };

    public static string Describe(DraftEntry entry, InputOperation operation, DraftErrorKind error,
        DraftFailureReason reason, string? detail) => entry switch
    {
        DraftEntry.Prepare => Prepare(operation, error, reason, detail),
        DraftEntry.Commit => Commit(operation, error, reason, detail),
        DraftEntry.Remove => Remove(error),
        DraftEntry.SetIncluded => SetIncluded(error),
        _ => Import(error)
    };

    private static string Prepare(InputOperation operation, DraftErrorKind error, DraftFailureReason reason, string? detail) => error switch
    {
        DraftErrorKind.Locked => "当前正在运行、取消清理或关闭等待，不能接纳输入。",
        DraftErrorKind.InvalidCount => operation switch
        {
            InputOperation.Subtitles => "一次接纳 1–2 个 ASS，并明确分配到 SC/TC。",
            InputOperation.Video => "一次只能接纳一个 MKV/MP4 视频。",
            _ => "一次只能接纳一个文件夹。"
        },
        DraftErrorKind.InvalidSlot => "每个 SC/TC 槽位只能分配一次。",
        DraftErrorKind.Duplicate => "同一 ASS 路径不能同时分配到 SC 和 TC。",
        DraftErrorKind.NonLocal => "只接受系统提供的本地文件或文件夹；URL 和纯文本路径不受支持。",
        DraftErrorKind.WrongType => reason switch
        {
            DraftFailureReason.WrongLocationKindDirectory => "请选择单个本地文件夹。",
            DraftFailureReason.WrongLocationKindFile => "这里只接受本地文件。",
            DraftFailureReason.UnsupportedVideoExtension => "视频仅支持 MKV/MP4。",
            DraftFailureReason.UnsupportedSubtitleExtension => "字幕仅支持 ASS。",
            _ => operation is InputOperation.FontsDirectory or InputOperation.OutputDirectory
                ? "请选择单个本地文件夹。" : "这里只接受本地文件。"
        },
        DraftErrorKind.InvalidPath => reason switch
        {
            DraftFailureReason.InvalidPathSyntax => "路径含有无效字符或数据流标记。",
            DraftFailureReason.PathInaccessible => "路径无效或不可访问：" + detail,
            _ => "需要完整的 Windows 本地路径。"
        },
        DraftErrorKind.MissingPath => reason switch
        {
            DraftFailureReason.DirectoryMissing => "文件夹不存在或不可访问。",
            _ => "文件不存在或不可访问。"
        },
        _ => "输入未接纳。"
    };

    private static string Commit(InputOperation operation, DraftErrorKind error, DraftFailureReason reason, string? detail) => error switch
    {
        DraftErrorKind.Locked => "当前正在运行、取消清理或关闭等待，不能提交输入。",
        DraftErrorKind.Stale => "候选确认期间草稿已变化，请重新分配和确认。",
        DraftErrorKind.ConfirmationRequired => "替换已有视频或字幕需要明确确认。",
        _ => Prepare(operation, error, reason, detail)
    };

    private static string Remove(DraftErrorKind error) => error switch
    {
        DraftErrorKind.Locked => "当前正在运行、取消清理或关闭等待，不能修改输入。",
        DraftErrorKind.InvalidSlot => "字幕槽位必须是 SC 或 TC。",
        _ => "输入未接纳。"
    };

    private static string SetIncluded(DraftErrorKind error) => error switch
    {
        DraftErrorKind.Locked => "当前不能修改字幕启用状态。",
        DraftErrorKind.InvalidSlot => "字幕槽位必须是 SC 或 TC。",
        DraftErrorKind.MissingPath => "该槽位尚未添加 ASS。",
        _ => "输入未接纳。"
    };

    private static string Import(DraftErrorKind error) => error switch
    {
        DraftErrorKind.Locked or DraftErrorKind.Stale => "草稿已变化或任务已锁定，原输入保持。",
        DraftErrorKind.ConfirmationRequired => "更换已有视频需要确认，整批未接纳。",
        DraftErrorKind.Duplicate => "文件中的轨道已加入；请在属性栏更换已有文件。",
        _ => "无法接纳输入，原输入保持。"
    };
}
