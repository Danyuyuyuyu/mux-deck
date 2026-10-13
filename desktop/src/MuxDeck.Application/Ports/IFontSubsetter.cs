using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>执行字体子集化并返回工作文件与诊断。</summary>
public interface IFontSubsetter
{
    /// <summary>执行字体子集化并返回工作文件与诊断。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<FontSubsetResult> SubsetAsync(FontSubsetRequest request, ToolCallContext context,
        IProgress<JobProgress>? progress = null, CancellationToken cancellationToken = default);
}
