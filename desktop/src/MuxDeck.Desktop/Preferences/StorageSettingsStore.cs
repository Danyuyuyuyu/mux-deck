using System.Text.Json;
using MuxDeck.Core;

namespace MuxDeck.Desktop.Preferences;

/// <summary>
/// 固定保存路径索引，让应用在用户自定义工作目录后仍能找到真实配置位置。
/// </summary>
public sealed class StorageSettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuxDeck", "settings.json");

    public StoragePaths Load(StoragePaths fallback)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) return fallback;
            var data = JsonSerializer.Deserialize<StorageData>(File.ReadAllText(path));
            if (data is not { Version: 1 } || string.IsNullOrWhiteSpace(data.WorkDirectory) ||
                string.IsNullOrWhiteSpace(data.ToolsDirectory)) return fallback;
            return StoragePaths.ForRoot(data.WorkDirectory, data.ToolsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return fallback; }
    }

    public void Save(StoragePaths paths)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var data = new StorageData(1, paths.AppDataRoot, paths.ToolsDirectory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record StorageData(int Version, string WorkDirectory, string ToolsDirectory);
}
