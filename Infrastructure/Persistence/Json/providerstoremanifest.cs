using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Persistence.Json;

/// <summary>
/// Declares and checks the on-disk schema version of a store. Bumping <see cref="CurrentVersion"/>
/// is the migration hook: <see cref="EnsureAsync"/> refuses to run against a newer layout and
/// upgrades an older one in place.
/// </summary>
public static class ProviderStoreManifest
{
    public const int CurrentVersion = 1;

    public const string FileName = "manifest.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public sealed record Manifest(int SchemaVersion, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public static async Task EnsureAsync(string rootPath, CancellationToken ct)
    {
        Directory.CreateDirectory(rootPath);
        var path = Path.Combine(rootPath, FileName);

        if (!File.Exists(path))
        {
            await WriteAsync(path, new Manifest(CurrentVersion, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ct);
            return;
        }

        Manifest? existing;
        await using (var stream = File.OpenRead(path))
        {
            existing = await JsonSerializer.DeserializeAsync<Manifest>(stream, Options, ct);
        }

        if (existing is null)
        {
            await WriteAsync(path, new Manifest(CurrentVersion, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ct);
            return;
        }

        if (existing.SchemaVersion > CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Provider store at '{rootPath}' uses schema version {existing.SchemaVersion}, " +
                $"but this build understands at most {CurrentVersion}. Upgrade the application or point ProviderStore:RootPath elsewhere.");
        }

        if (existing.SchemaVersion < CurrentVersion)
        {
            // Single-step migration point: older documents are forward-compatible today, so the
            // upgrade only has to restamp the manifest. Future versions add their rewrite here.
            await WriteAsync(path, existing with { SchemaVersion = CurrentVersion, UpdatedAt = DateTimeOffset.UtcNow }, ct);
        }
    }

    private static async Task WriteAsync(string path, Manifest manifest, CancellationToken ct)
    {
        var temporary = $"{path}.tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, Options, ct);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
