namespace MuxDeck.Core.Components;

public static class ComponentDirectories
{
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static ComponentRemovalPlan CreatePlan(StoragePaths storage, string id, ComponentRemovalKind kind)
    {
        MuxDeck.Contracts.IManageComponents manager = new ComponentManagement();
        return ComponentManagement.Legacy(manager.PrepareUninstallAsync(id, (MuxDeck.Contracts.ComponentRemovalKind)kind,
            ComponentManagement.Configuration(storage)).GetAwaiter().GetResult());
    }

    internal static ComponentRemovalPlan CreatePlanCore(StoragePaths storage, string id, ComponentRemovalKind kind)
    {
        if (kind is not (ComponentRemovalKind.Component or ComponentRemovalKind.Dependencies) || !ComponentReleases.All.TryGetValue(id, out var release) || kind == ComponentRemovalKind.Dependencies && id != "afs")
            throw new ArgumentException("未知组件或依赖类型。");
        if (!Path.IsPathFullyQualified(storage.ToolsDirectory)) throw new IOException("组件目录必须是绝对路径。");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storage.ToolsDirectory));
        var target = Path.GetFullPath(Path.Combine(root, kind == ComponentRemovalKind.Dependencies ? "fonttools" : release.Directory));
        if (!IsInside(root, target) || !string.Equals(Path.GetDirectoryName(target), root, Comparison)) throw new IOException("拒绝卸载组件目录以外的路径。");
        EnsureNoLinks(root);
        EnsureNoLinks(target);
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException("待卸载目录不存在：" + target);
        return new(id, kind, root, target);
    }

    public static ComponentRemovalPlan? TryPlan(StoragePaths storage, string id, ComponentRemovalKind kind)
    {
        try { return CreatePlan(storage, id, kind); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    internal static ComponentRemovalPlan? TryPlanCore(StoragePaths storage, string id, ComponentRemovalKind kind)
    {
        try { return CreatePlanCore(storage, id, kind); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    public static bool IsInside(string root, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Path.EndsInDirectorySeparator(prefix)) prefix += Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, Comparison);
    }

    public static void Validate(ComponentRemovalPlan plan, StoragePaths storage)
    {
        var current = CreatePlanCore(storage, plan.ComponentId, plan.Kind);
        if (!string.Equals(current.ToolsDirectory, plan.ToolsDirectory, Comparison) || !string.Equals(current.TargetDirectory, plan.TargetDirectory, Comparison))
            throw new IOException("组件目录已变化，请重新确认卸载范围。");
    }

    public static void EnsureNoLinks(string path)
    {
        // 检查完整祖先链，不能通过用户目录中的联接绕过组件根目录边界。
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("目录包含链接，拒绝自动卸载：" + current);
    }
}
