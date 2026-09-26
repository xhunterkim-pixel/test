using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace LevelGate.Editor;

/// <summary>
/// Item pictures, downloaded once from tarkov.dev (which keeps a picture per item id)
/// and kept in %AppData%\LevelGateEditor\icons, so they show instantly afterwards and
/// also offline. Items tarkov.dev doesn't have (modded items) are remembered for a week.
/// </summary>
public static class ItemIcons
{
    private static readonly string Folder = Path.Combine(EditorSettings.Folder, "icons");
    private static readonly Regex Name = new("^([0-9a-f]{24})-(icon|grid-image|base-image|512)$", RegexOptions.Compiled);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Gate = new(6); // a few at a time, like a browser would
    private static readonly TimeSpan MissingFor = TimeSpan.FromDays(7);

    static ItemIcons() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LevelGateEditor/1.0");

    /// <summary>The picture's bytes (webp), or null when there is none / the download failed.</summary>
    public static async Task<byte[]?> GetAsync(string name)
    {
        name = name.ToLowerInvariant();
        if (name.EndsWith(".webp")) name = name[..^5];
        if (!Name.IsMatch(name)) return null;
        Directory.CreateDirectory(Folder);
        var file = Path.Combine(Folder, name + ".webp");
        var missing = Path.Combine(Folder, name + ".missing");
        if (File.Exists(file)) return await File.ReadAllBytesAsync(file);
        if (File.Exists(missing) && DateTime.UtcNow - File.GetLastWriteTimeUtc(missing) < MissingFor) return null;

        await Gate.WaitAsync();
        try
        {
            if (File.Exists(file)) return await File.ReadAllBytesAsync(file); // fetched while waiting
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var response = await Http.GetAsync($"https://assets.tarkov.dev/{name}.webp");
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        await File.WriteAllTextAsync(missing, "no picture for this item");
                        return null;
                    }
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        var temp = file + ".tmp";
                        await File.WriteAllBytesAsync(temp, bytes);
                        File.Move(temp, file, overwrite: true);
                        return bytes;
                    }
                }
                catch
                {
                    // offline / timeout: try again shortly
                }
                await Task.Delay(700 * (attempt + 1));
            }
            return null; // not remembered as missing: next time it's tried again
        }
        finally
        {
            Gate.Release();
        }
    }
}
