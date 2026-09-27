using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Entities;
using Domain.Entities.Interfaces;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence.Json;

/// <summary>
/// Provider diagnostics as a single JSON document, rewritten atomically (temp file + move) so a
/// crash mid-write can never leave a half-written view of the last success/failure.
/// </summary>
public sealed class JsonProviderDiagnosticsStore : IProviderDiagnosticsStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private readonly ProviderStoreOptions _options;
    private bool _manifestReady;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JsonProviderDiagnosticsStore(IOptions<ProviderStoreOptions> options)
    {
        _options = options.Value;
        _path = Path.Combine(_options.ResolveRootPath(), "provider-diagnostics.json");
    }

    public string FilePath => _path;

    public async Task<ProviderDiagnostics?> GetAsync(string providerId, CancellationToken ct)
    {
        var all = await ReadAllInternalAsync(ct);
        return all.FirstOrDefault(d => string.Equals(d.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<ProviderDiagnostics>> GetAllAsync(CancellationToken ct) => await ReadAllInternalAsync(ct);

    public Task RecordSuccessAsync(string providerId, DateTimeOffset at, string operation, CancellationToken ct) =>
        MutateAsync(providerId, ct, current => current is null
            ? new ProviderDiagnostics
            {
                ProviderId = providerId,
                LastSuccessAt = at,
                LastSuccessOperation = operation,
                ConsecutiveFailures = 0,
                TotalSuccesses = 1
            }
            : current with
            {
                LastSuccessAt = at,
                LastSuccessOperation = operation,
                ConsecutiveFailures = 0,
                TotalSuccesses = current.TotalSuccesses + 1
            });

    public Task RecordFailureAsync(string providerId, DateTimeOffset at, string operation, string outcome, string message, CancellationToken ct) =>
        MutateAsync(providerId, ct, current => current is null
            ? new ProviderDiagnostics
            {
                ProviderId = providerId,
                LastFailureAt = at,
                LastFailureOperation = operation,
                LastFailureOutcome = outcome,
                LastFailureMessage = Truncate(message),
                ConsecutiveFailures = 1,
                TotalFailures = 1
            }
            : current with
            {
                LastFailureAt = at,
                LastFailureOperation = operation,
                LastFailureOutcome = outcome,
                LastFailureMessage = Truncate(message),
                ConsecutiveFailures = current.ConsecutiveFailures + 1,
                TotalFailures = current.TotalFailures + 1
            });

    private static string Truncate(string message) => message.Length <= 512 ? message : message[..512] + "…";

    private async Task MutateAsync(string providerId, CancellationToken ct, Func<ProviderDiagnostics?, ProviderDiagnostics> mutate)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureReadyAsync(ct);

            var all = await ReadAllInternalAsync(ct);
            var updated = mutate(all.FirstOrDefault(d => string.Equals(d.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)));
            var next = all.Where(d => !string.Equals(d.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)).ToList();
            next.Add(updated);

            await WriteAsync(next, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ProviderDiagnostics>> ReadAllInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var stream = File.Open(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var entries = await JsonSerializer.DeserializeAsync<List<ProviderDiagnostics>>(stream, Options, ct);
        return entries ?? [];
    }

    private async Task WriteAsync(List<ProviderDiagnostics> entries, CancellationToken ct)
    {
        Directory.CreateDirectory(_options.ResolveRootPath());
        var temporary = $"{_path}.tmp";

        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, entries, Options, ct);
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
