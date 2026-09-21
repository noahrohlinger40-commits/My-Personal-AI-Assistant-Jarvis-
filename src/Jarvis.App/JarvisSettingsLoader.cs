using System.Text.Json;
using Jarvis.Core;

namespace Jarvis.App;

internal static class JarvisSettingsLoader
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<JarvisOptions> LoadAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(workspaceRoot, "jarvis.settings.json");

        if (!File.Exists(filePath))
        {
            return new JarvisOptions();
        }

        await using var stream = File.OpenRead(filePath);
        var options = await JsonSerializer.DeserializeAsync<JarvisOptions>(
            stream,
            ReadOptions,
            cancellationToken);

        return options ?? new JarvisOptions();
    }

    public static async Task SaveAsync(string workspaceRoot, JarvisOptions options, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(workspaceRoot, "jarvis.settings.json");
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, options, WriteOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
