using System.Text.Json;

namespace BusPing.Services;

/// <summary>Atomic JSON persistence helper: write to a temp file, then replace.</summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string AppDataPath(string fileName) =>
        Path.Combine(FileSystem.AppDataDirectory, fileName);

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";

        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
        }

        File.Move(temp, path, overwrite: true);
    }
}
