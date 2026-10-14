using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Components;

namespace MuxDeck.Core.Components;

// 兼容首轮安装入口，所有组件共用同一下载、暂存和并发守卫。
public sealed class AssfontsInstaller(HttpClient? httpClient = null, IComponentVerifier? verifier = null, ComponentPackage? package = null) : IComponentInstaller
{
    public static ComponentPackage Release => ComponentReleases.All["assfonts"].Package;
    private readonly ComponentInstaller installer = new(httpClient, verifier,
        package is null ? null : new Dictionary<string, ComponentRelease>
        {
            ["assfonts"] = ComponentReleases.All["assfonts"] with { Package = package }
        });

    public Task<ComponentInstallResult> InstallAsync(string id, StoragePaths storage, IProgress<ComponentInstallProgress> progress, CancellationToken cancellationToken)
    {
        if (id != "assfonts") throw new NotSupportedException("此入口仅安装 assfonts。");
        return installer.InstallAsync(id, storage, progress, cancellationToken);
    }
}
