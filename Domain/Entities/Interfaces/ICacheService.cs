namespace Domain.Entities.Interfaces;

public interface ICacheService
{
    Task<T?> TryGetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}
