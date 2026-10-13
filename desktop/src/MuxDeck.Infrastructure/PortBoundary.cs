using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;

namespace MuxDeck.Infrastructure;

// 保留原异常；取消不转换，失败码只使用集中常量。
internal static class PortBoundary
{
    public static async Task<T> Guard<T>(Func<Task<T>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (ToolFailureException) { throw; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or System.Xml.XmlException or InvalidDataException or FormatException or KeyNotFoundException)
        { throw Failure(ErrorCodes.ToolOutputInvalid, ex); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        { throw Failure(ErrorCodes.WorkspaceIoFailed, ex); }
        catch (System.ComponentModel.Win32Exception ex) { throw Failure(ErrorCodes.ToolExitFailed, ex); }
    }
    public static ToolFailureException Failure(string code, Exception ex) => new(code, ex.Message, ex.ToString(), ex);
}
