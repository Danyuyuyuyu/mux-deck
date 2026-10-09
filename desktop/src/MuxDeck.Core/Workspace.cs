namespace MuxDeck.Core;

public sealed record Workspace(string Root)
{
    public string ToolsDirectory => Path.Combine(Root, "bin");

    public static Workspace Discover(string? explicitRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            var root = Path.GetFullPath(explicitRoot);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            return new Workspace(root);
        }

        // Development uses the repo; portable deployments use the executable directory.
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "start_mux_ui.bat")))
                    return new Workspace(dir.FullName);

        return new Workspace(AppContext.BaseDirectory);
    }
}
