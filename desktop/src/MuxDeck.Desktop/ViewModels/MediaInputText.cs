using MuxDeck.Contracts;

namespace MuxDeck.Desktop.ViewModels;

/// <summary>
/// 拖入识别错误码到中文文案的唯一映射：同步拖入反馈与导入用例结果都经过这里，界面不解析文案。
/// </summary>
internal static class MediaInputText
{
    public static string Describe(string code, string? path) => code switch
    {
        ErrorCodes.ImportEmptySelection => "拖入本地视频、音频、ASS/SRT 或字体目录。",
        ErrorCodes.ImportNonLocal => "仅接受系统本地文件或文件夹，URL / 文本路径不受支持。",
        ErrorCodes.ImportPathNotAbsolute => "需要完整的本地路径。",
        ErrorCodes.ImportPathMissing => "路径不存在或不可访问：" + path,
        ErrorCodes.ImportTypeUnsupported => "不支持此文件类型：" + Path.GetFileName(path) + "；支持视频、音频、ASS/SRT 和字体。",
        ErrorCodes.ImportDuplicate => "同一文件不能重复导入。",
        ErrorCodes.ImportFontDirectories => "一次只能设置一个字体目录。",
        ErrorCodes.ImportSourceVideoCount => "单个封装一次只能接纳一个源视频。",
        ErrorCodes.ImportAudioMissing => "文件没有可导入的音频轨：" + path,
        ErrorCodes.ImportSubtitleMismatch => "文件内容不是有效的 ASS/SRT：" + path,
        ErrorCodes.ImportTrackConflict => "文件中的轨道已加入；请在属性栏更换已有文件。",
        ErrorCodes.ImportSubtitleTimeline => "无法识别拖入文件，整批未接纳：SRT 没有有效时间轴：" + path,
        _ => "无法识别拖入文件，整批未接纳：" + path
    };

    // 端口或工具失败保留原始原因：旧文案是前缀加原始消息。
    public static string Describe(OperationFailure failure) =>
        failure.Code switch
        {
            ErrorCodes.ImportEmptySelection or ErrorCodes.ImportNonLocal or ErrorCodes.ImportPathNotAbsolute
                or ErrorCodes.ImportPathMissing or ErrorCodes.ImportTypeUnsupported or ErrorCodes.ImportDuplicate
                or ErrorCodes.ImportFontDirectories or ErrorCodes.ImportSourceVideoCount or ErrorCodes.ImportAudioMissing
                or ErrorCodes.ImportSubtitleMismatch or ErrorCodes.ImportTrackConflict or ErrorCodes.ImportSubtitleTimeline
                => Describe(failure.Code, failure.Detail),
            _ => "无法识别拖入文件，整批未接纳：" + failure.Message
        };
}
