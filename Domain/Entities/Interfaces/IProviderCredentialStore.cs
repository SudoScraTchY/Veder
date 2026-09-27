using Domain.Entities;

namespace Domain.Entities.Interfaces;

/// <summary>
/// Store for provider credentials. Secrets are encrypted at rest and never returned through
/// <see cref="GetMetadataAsync"/>, so an operator can rotate or revoke a key without a redeploy and
/// without the key ever touching source control or a log.
/// </summary>
public interface IProviderCredentialStore
{
    /// <summary>Metadata only — safe to log.</summary>
    Task<ProviderCredentialMetadata?> GetMetadataAsync(string providerId, CancellationToken ct);

    /// <summary>Returns the decrypted secret, or null when none is stored or it has been revoked.</summary>
    Task<string?> ResolveAsync(string providerId, CancellationToken ct);

    /// <summary>Synchronous variant for startup configuration; returns false when nothing is stored.</summary>
    bool TryResolve(string providerId, out string? secret);

    /// <summary>Stores or replaces the secret, returning the new metadata (fingerprint, hint, rotation count).</summary>
    Task<ProviderCredentialMetadata> StoreAsync(string providerId, string secret, CancellationToken ct);

    /// <summary>Revokes the stored secret; the file is retained as a revocation marker until it is overwritten.</summary>
    Task<bool> RevokeAsync(string providerId, CancellationToken ct);
}
