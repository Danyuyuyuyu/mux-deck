using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;

namespace MuxDeck.Infrastructure;

public sealed class NoOverwritePublisher : IArtifactPublisher
{
    public Task<OutputArtifact> PublishNoOverwriteAsync(WorkFile prepared, string destinationPath,
        CancellationToken cancellationToken = default) => PortBoundary.Guard(async () =>
    {
        string? staging = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath))!;
            Directory.CreateDirectory(directory);
            staging = Path.Combine(directory, ".muxdeck-" + Guid.NewGuid().ToString("N") + ".partial");
            await using (var from = new FileStream(prepared.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var to = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await from.CopyToAsync(to, cancellationToken).ConfigureAwait(false);
                await to.FlushAsync(cancellationToken).ConfigureAwait(false);
                to.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // 最后一次取消检查之后进入提交点；同盘 rename 且不覆盖期间新出现的目标。
            try { File.Move(staging, destinationPath, overwrite: false); }
            catch (IOException ex) when (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            { throw PortBoundary.Failure(ErrorCodes.TargetAlreadyExists, ex); }
            staging = null;
            return new OutputArtifact(destinationPath);
        }
        finally
        {
            if (staging is not null)
                try { File.Delete(staging); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    });
}
