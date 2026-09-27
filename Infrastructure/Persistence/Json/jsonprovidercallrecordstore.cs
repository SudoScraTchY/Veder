using Domain.Entities;
using Domain.Entities.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence.Json;

/// <summary>
/// Append-only JSON Lines store for provider call records. Chosen over a single JSON document
/// because call records are written far more often than they are read, and an append never
/// rewrites (or risks) the existing history.
/// </summary>
public sealed class JsonProviderCallRecordStore : IProviderCallRecordStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonRecordFile _file;
    private readonly ProviderStoreOptions _options;
    private readonly ILogger<JsonProviderCallRecordStore> _logger;
    private bool _manifestReady;

    public JsonProviderCallRecordStore(IOptions<ProviderStoreOptions> options, ILogger<JsonProviderCallRecordStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _file = new JsonRecordFile(Path.Combine(_options.ResolveRootPath(), "provider-calls.jsonl"));
    }

    public string FilePath => _file.Path;

    public async Task AppendAsync(ProviderCallRecord record, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureReadyAsync(ct);
            await _file.AppendAsync(record, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ProviderCallRecord>> GetRecentAsync(int count, CancellationToken ct)
    {
        var limit = Normalize(count);

        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(_file.Path))
            {
                return [];
            }

            var records = await _file.ReadAllAsync<ProviderCallRecord>(ct);
            return records.OrderByDescending(r => r.StartedAt).Take(limit).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ProviderCallRecord>> GetByProviderAsync(string providerId, int count, CancellationToken ct)
    {
        var limit = Normalize(count);

        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(_file.Path))
            {
                return [];
            }

            var records = await _file.ReadAllAsync<ProviderCallRecord>(ct);
            return records
                .Where(r => string.Equals(r.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.StartedAt)
                .Take(limit)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> CountAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return !File.Exists(_file.Path) ? 0 : (await _file.ReadAllAsync<ProviderCallRecord>(ct)).Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    private int Normalize(int count) => Math.Clamp(count, 1, Math.Max(1, _options.MaxQueryCount));

    private async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_manifestReady)
        {
            return;
        }

        await ProviderStoreManifest.EnsureAsync(_options.ResolveRootPath(), ct);
        _manifestReady = true;
        _logger.LogDebug("Provider call records are stored at {Path}", _file.Path);
    }
}
