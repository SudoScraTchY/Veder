using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Entities;
using Domain.Entities.Interfaces;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Json;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence.Credentials;

/// <summary>
/// File-backed credential vault. Secrets are encrypted at rest — Windows DPAPI when available, and
/// AES-256-GCM with a locally generated key otherwise — while only metadata (fingerprint, hint,
/// timestamps) is kept in clear text. The vault lives outside the repository by default.
/// </summary>
public sealed class ProtectedCredentialStore : IProviderCredentialStore
{
    private const string MetadataFileName = "credentials.json";
    private const string KeyFileName = "vault.key";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ProviderStoreOptions _options;
    private bool _manifestReady;

    private static readonly JsonSerializerOptions MetadataOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProtectedCredentialStore(IOptions<ProviderStoreOptions> options) => _options = options.Value;

    private string CredentialPath(string providerId) =>
        Path.Combine(_options.ResolveRootPath(), "credentials", $"{Sanitize(providerId)}.cred");

    private string MetadataPath => Path.Combine(_options.ResolveRootPath(), "credentials", MetadataFileName);

    public async Task<ProviderCredentialMetadata?> GetMetadataAsync(string providerId, CancellationToken ct)
    {
        var all = await ReadMetadataAsync(ct);
        return all.FirstOrDefault(m => string.Equals(m.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string?> ResolveAsync(string providerId, CancellationToken ct)
    {
        if (!File.Exists(CredentialPath(providerId)))
        {
            return null;
        }

        var metadata = await GetMetadataAsync(providerId, ct);
        if (metadata is { IsRevoked: true })
        {
            return null;
        }

        var payload = await File.ReadAllBytesAsync(CredentialPath(providerId), ct);
        return Unprotect(payload);
    }

    public bool TryResolve(string providerId, out string? secret)
    {
        secret = null;

        try
        {
            if (!File.Exists(CredentialPath(providerId)))
            {
                return false;
            }

            secret = Unprotect(File.ReadAllBytes(CredentialPath(providerId)));
            return !string.IsNullOrEmpty(secret);
        }
        catch (CryptographicException)
        {
            // A value that cannot be decrypted (different machine/user, corrupted vault) is treated as absent.
            secret = null;
            return false;
        }
        catch (IOException)
        {
            secret = null;
            return false;
        }
    }

    public async Task<ProviderCredentialMetadata> StoreAsync(string providerId, string secret, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("A credential must not be empty.", nameof(secret));
        }

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureReadyAsync(ct);

            var existing = await GetMetadataAsync(providerId, ct);
            var now = DateTimeOffset.UtcNow;

            var metadata = new ProviderCredentialMetadata
            {
                ProviderId = providerId,
                Fingerprint = Fingerprint(secret),
                Hint = Hint(secret),
                Protection = ProtectionScheme(),
                CreatedAt = existing?.CreatedAt ?? now,
                RotatedAt = existing is null ? null : now,
                RevokedAt = null,
                RotationCount = (existing?.RotationCount ?? 0) + (existing is null ? 0 : 1)
            };

            await WriteAtomicAsync(CredentialPath(providerId), Protect(secret), ct);
            await WriteMetadataAsync(providerId, metadata, ct);

            return metadata;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RevokeAsync(string providerId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var existing = await GetMetadataAsync(providerId, ct);
            if (existing is null || existing.IsRevoked)
            {
                return false;
            }

            var revoked = existing with { RevokedAt = DateTimeOffset.UtcNow };

            // Overwrite the ciphertext so the key material is gone, keeping revoked metadata as the marker.
            if (File.Exists(CredentialPath(providerId)))
            {
                await WriteAtomicAsync(CredentialPath(providerId), [], ct);
            }

            await WriteMetadataAsync(providerId, revoked, ct);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>SHA-256 prefix: stable across restarts, useless for recovering the key.</summary>
    public static string Fingerprint(string secret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    public static string Hint(string secret) => secret.Length <= 4 ? "****" : $"…{secret[^4..]}";

    private static string ProtectionScheme() => OperatingSystem.IsWindows() ? "dpapi" : "aes-gcm";

    private static byte[] Protect(string secret)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);

        if (OperatingSystem.IsWindows())
        {
            // DPAPI ties the ciphertext to the current user account; no key management required.
            return ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }

        return AesGcmProtect(plaintext);
    }

    private static string Unprotect(byte[] payload)
    {
        if (payload.Length == 0)
        {
            throw new CryptographicException("The stored credential is empty (revoked).");
        }

        if (OperatingSystem.IsWindows())
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(payload, optionalEntropy: null, DataProtectionScope.CurrentUser));
        }

        return Encoding.UTF8.GetString(AesGcmUnprotect(payload));
    }

    private static byte[] AesGcmProtect(byte[] plaintext)
    {
        var key = LoadOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return [.. nonce, .. tag, .. ciphertext];
    }

    private static byte[] AesGcmUnprotect(byte[] payload)
    {
        var nonceLength = AesGcm.NonceByteSizes.MaxSize;
        var tagLength = AesGcm.TagByteSizes.MaxSize;

        if (payload.Length <= nonceLength + tagLength)
        {
            throw new CryptographicException("The stored credential is truncated.");
        }

        var nonce = payload.AsSpan(0, nonceLength);
        var tag = payload.AsSpan(nonceLength, tagLength);
        var ciphertext = payload.AsSpan(nonceLength + tagLength);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(LoadOrCreateKey(), tagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    private static byte[] LoadOrCreateKey()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Veder",
            "provider-store",
            "credentials",
            KeyFileName);

        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, key);
        return key;
    }

    private async Task<List<ProviderCredentialMetadata>> ReadMetadataAsync(CancellationToken ct)
    {
        if (!File.Exists(MetadataPath))
        {
            return [];
        }

        await using var stream = File.Open(MetadataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<List<ProviderCredentialMetadata>>(stream, MetadataOptions, ct) ?? [];
    }

    private async Task WriteMetadataAsync(string providerId, ProviderCredentialMetadata metadata, CancellationToken ct)
    {
        var all = await ReadMetadataAsync(ct);
        var next = all.Where(m => !string.Equals(m.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)).ToList();
        next.Add(metadata);

        var json = JsonSerializer.SerializeToUtf8Bytes(next, MetadataOptions);
        await WriteAtomicAsync(MetadataPath, json, ct);
    }

    private static async Task WriteAtomicAsync(string path, byte[] payload, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.tmp";

        await File.WriteAllBytesAsync(temporary, payload, ct);
        File.Move(temporary, path, overwrite: true);
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

    private static string Sanitize(string providerId) =>
        new(providerId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
}
