using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Entities;
using Domain.Entities.Interfaces;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence.Json;

/// <summary>
/// Observations as a single atomically-rewritten JSON document, keyed by the deterministic
/// observation id. A small, auditable dataset: upserts replace in place, so replaying the same
/// reading is idempotent and the file never grows duplicates.
/// </summary>
public sealed class JsonObservationStore : IObservationStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ProviderStoreOptions _options;
    private readonly string _path;
    private bool _manifestReady;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonObservationStore(IOptions<ProviderStoreOptions> options)
    {
        _options = options.Value;
        _path = Path.Combine(_options.ResolveRootPath(), "observations.json");
    }

    public string FilePath => _path;

    public async Task<ObservationRecord> UpsertAsync(ObservationRecord record, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureReadyAsync(ct);

            var all = await ReadAllInternalAsync(ct);
            var next = all.Where(r => !string.Equals(r.Id, record.Id, StringComparison.Ordinal)).ToList();
            next.Add(record);

            await WriteAsync(next, ct);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ObservationRecord>> GetRecentAsync(string? kind, int count, CancellationToken ct)
    {
        var all = await ReadAllInternalAsync(ct);
        var filtered = string.IsNullOrWhiteSpace(kind)
            ? all
            : all.Where(r => string.Equals(r.Kind, kind, StringComparison.OrdinalIgnoreCase));

        return filtered
            .OrderByDescending(r => r.ObservedAt)
            .Take(Math.Clamp(count, 1, Math.Max(1, _options.MaxQueryCount)))
            .ToList();
    }

    public async Task<ObservationRecord?> GetByIdAsync(string id, CancellationToken ct)
    {
        var all = await ReadAllInternalAsync(ct);
        return all.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
    }

    public async Task<long> CountAsync(CancellationToken ct) => (await ReadAllInternalAsync(ct)).Count;

    private async Task<List<ObservationRecord>> ReadAllInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var stream = File.Open(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<List<ObservationRecord>>(stream, Options, ct) ?? [];
    }

    private async Task WriteAsync(List<ObservationRecord> records, CancellationToken ct)
    {
        Directory.CreateDirectory(_options.ResolveRootPath());
        var temporary = $"{_path}.tmp";

        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, records, Options, ct);
        }

        File.Move(temporary, _path, overwrite: true);
    }

    private async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_manifestReady)
        {
            return;
        }

        await ProviderStoreManifest.EnsureAsync(_options.ResolveRootPath(), ct);
        _manifestReady = true;
    }
}
