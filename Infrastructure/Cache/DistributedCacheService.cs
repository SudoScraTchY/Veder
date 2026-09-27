using System.Text.Json;
using Domain.Entities.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace Infrastructure.Cache;

public sealed class DistributedCacheService(IDistributedCache cache) : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> TryGetAsync<T>(string key, CancellationToken ct)
    {
        var payload = await cache.GetAsync(key, ct);
        return payload is null || payload.Length == 0
            ? default
            : JsonSerializer.Deserialize<T>(payload, SerializerOptions);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct)
        => cache.SetAsync(
            key,
            JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            ct);

    public Task RemoveAsync(string key, CancellationToken ct) => cache.RemoveAsync(key, ct);
}
