namespace Domain.Entities;

/// <summary>
/// Metadata about a stored provider credential. The secret itself never appears here — only a
/// fingerprint and a short hint, so the vault can be audited without exposing key material.
/// </summary>
public sealed record ProviderCredentialMetadata
{
    public required string ProviderId { get; init; }

    /// <summary>First 12 hex characters of the SHA-256 of the secret; identifies a key without revealing it.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Last four characters of the secret, for an operator to recognise which key is loaded.</summary>
    public required string Hint { get; init; }

    /// <summary>How the secret is protected at rest, e.g. "dpapi" or "aes-gcm".</summary>
    public required string Protection { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? RotatedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public bool IsRevoked => RevokedAt is not null;

    public int RotationCount { get; init; }
}
