using System.Text.Json;
using MuxDeck.Core;

namespace MuxDeck.Desktop.Preferences;

// 用户界面偏好与封装草稿分离；只在生产启动处选择真实配置位置。
public sealed class UserPreferencesStore(string path)
{
    private readonly SemaphoreSlim writer = new(1, 1);
    public static UserPreferencesStore CreateDefault(StoragePaths? storage = null) => new(Path.Combine(
        (storage ?? StoragePaths.Default).ConfigDirectory, "preferences.json"));

    public IReadOnlyDictionary<string, double> LoadTrackWidths()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) return new Dictionary<string, double>();
            var data = JsonSerializer.Deserialize<PreferencesData>(File.ReadAllText(path));
            return data is { Version: 1, TrackColumnWidths: not null } ? data.TrackColumnWidths : new Dictionary<string, double>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return new Dictionary<string, double>(); }
    }

    public async Task SaveTrackWidthsAsync(IReadOnlyDictionary<string, double> widths)
    {
        var data = new PreferencesData(1, new Dictionary<string, double>(widths));
        await writer.WaitAsync().ConfigureAwait(false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            // 写入失败也不把半份 JSON 留作下次启动配置。
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            writer.Release();
        }
    }

    private sealed record PreferencesData(int Version, Dictionary<string, double>? TrackColumnWidths);
}
