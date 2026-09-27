using Domain.Entities;
using Domain.Entities.Interfaces;
using Infrastructure.Persistence.Json;
using Infrastructure.Persistence.Credentials;
using Infrastructure.Providers;
using Microsoft.Extensions.Options;

namespace Veder.Tests;

public sealed class CredentialVaultTests
{
    private static ProtectedCredentialStore Vault(TempProviderStore store) =>
        new(Options.Create(store.Options));

    [Fact]
    public async Task StoreThenResolve_RoundTripsTheSecretAndKeepsItOutOfTheMetadata()
    {
        using var store = new TempProviderStore();
        var vault = Vault(store);

        var metadata = await vault.StoreAsync("open-meteo", "super-secret-key-1234", CancellationToken.None);

        Assert.Equal("open-meteo", metadata.ProviderId);
        Assert.Equal("…1234", metadata.Hint);
        Assert.Equal(ProtectedCredentialStore.Fingerprint("super-secret-key-1234"), metadata.Fingerprint);
        Assert.False(metadata.IsRevoked);
        Assert.Equal(0, metadata.RotationCount);
        Assert.Contains(metadata.Protection, new[] { "dpapi", "aes-gcm" });

        Assert.Equal("super-secret-key-1234", await vault.ResolveAsync("open-meteo", CancellationToken.None));

        // Nothing on disk may contain the raw secret in clear text.
        var metadataFile = await File.ReadAllTextAsync(Path.Combine(store.Root, "credentials", "credentials.json"));
        Assert.DoesNotContain("super-secret-key-1234", metadataFile);
        var credentialBytes = await File.ReadAllBytesAsync(Path.Combine(store.Root, "credentials", "open-meteo.cred"));
        Assert.DoesNotContain("super-secret-key-1234", System.Text.Encoding.UTF8.GetString(credentialBytes));
    }

    [Fact]
    public async Task Rotate_KeepsTheOriginalCreatedAtAndCountsTheRotation()
    {
        using var store = new TempProviderStore();
        var vault = Vault(store);

        var first = await vault.StoreAsync("open-meteo", "first-key-1111", CancellationToken.None);
        var rotated = await vault.StoreAsync("open-meteo", "second-key-2222", CancellationToken.None);

        Assert.Equal(first.CreatedAt, rotated.CreatedAt);
        Assert.NotNull(rotated.RotatedAt);
        Assert.Equal(1, rotated.RotationCount);
        Assert.Equal("…2222", rotated.Hint);
        Assert.Equal("second-key-2222", await vault.ResolveAsync("open-meteo", CancellationToken.None));
        Assert.NotEqual(first.Fingerprint, rotated.Fingerprint);
    }

    [Fact]
    public async Task Revoke_RemovesTheSecretAndReportsRevoked()
    {
        using var store = new TempProviderStore();
        var vault = Vault(store);
        await vault.StoreAsync("open-meteo", "key-to-revoke-9999", CancellationToken.None);

        var revoked = await vault.RevokeAsync("open-meteo", CancellationToken.None);

        Assert.True(revoked);
        Assert.Null(await vault.ResolveAsync("open-meteo", CancellationToken.None));
        Assert.False(vault.TryResolve("open-meteo", out _));

        var metadata = await vault.GetMetadataAsync("open-meteo", CancellationToken.None);
        Assert.True(metadata!.IsRevoked);
        Assert.NotNull(metadata.RevokedAt);

        // Revoking twice is a no-op rather than an error.
        Assert.False(await vault.RevokeAsync("open-meteo", CancellationToken.None));
    }

    [Fact]
    public async Task UnknownProvider_ResolvesNothing()
    {
        using var store = new TempProviderStore();
        var vault = Vault(store);

        Assert.Null(await vault.ResolveAsync("nobody", CancellationToken.None));
        Assert.Null(await vault.GetMetadataAsync("nobody", CancellationToken.None));
        Assert.False(await vault.RevokeAsync("nobody", CancellationToken.None));
    }

    [Fact]
    public async Task EmptySecret_IsRejected()
    {
        using var store = new TempProviderStore();
        var vault = Vault(store);

        await Assert.ThrowsAsync<ArgumentException>(() => vault.StoreAsync("open-meteo", "   ", CancellationToken.None));
    }
}

public sealed class ObservationStoreTests
{
    private static JsonObservationStore Observations(TempProviderStore store) => new(Options.Create(store.Options));

    private static ObservationRecord Reading(string kind, DateTimeOffset at) => new()
    {
        Id = ObservationRecord.BuildId("open-meteo", kind, 52.52, 13.41, at),
        ProviderId = "open-meteo",
        Kind = kind,
        Latitude = 52.52,
        Longitude = 13.41,
        ObservedAt = at,
        RecordedAt = DateTimeOffset.UtcNow,
        Summary = "Overcast",
        TemperatureC = 13.9
    };

    [Fact]
    public async Task Upsert_IsIdempotentForTheSameReading()
    {
        using var store = new TempProviderStore();
        var observations = Observations(store);
        var at = DateTimeOffset.FromUnixTimeSeconds(1758456000);

        await observations.UpsertAsync(Reading("weather", at), CancellationToken.None);
        await observations.UpsertAsync(Reading("weather", at), CancellationToken.None);

        Assert.Equal(1, await observations.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DifferentReadings_AndKinds_AreKeptSeparatelyAndFilterable()
    {
        using var store = new TempProviderStore();
        var observations = Observations(store);
        var at = DateTimeOffset.FromUnixTimeSeconds(1758456000);

        await observations.UpsertAsync(Reading("weather", at), CancellationToken.None);
        await observations.UpsertAsync(Reading("weather", at.AddHours(1)), CancellationToken.None);
        await observations.UpsertAsync(Reading("air-quality", at), CancellationToken.None);

        Assert.Equal(3, await observations.CountAsync(CancellationToken.None));
        Assert.Equal(2, (await observations.GetRecentAsync("weather", 10, CancellationToken.None)).Count);
        Assert.Single(await observations.GetRecentAsync("air-quality", 10, CancellationToken.None));
    }

    [Fact]
    public async Task GetById_ReturnsTheDeterministicRecord()
    {
        using var store = new TempProviderStore();
        var observations = Observations(store);
        var at = DateTimeOffset.FromUnixTimeSeconds(1758456000);
        var record = Reading("weather", at);

        await observations.UpsertAsync(record, CancellationToken.None);

        var found = await observations.GetByIdAsync(record.Id, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal("open-meteo|weather|52.52:13.41|1758456000", found!.Id);
        Assert.Equal(13.9, found.TemperatureC);
    }
}

public sealed class ConcurrencyGateTests
{
    [Fact]
    public async Task Gate_SerialisesCallersBeyondTheLimit()
    {
        using var gate = new ProviderConcurrencyGate(1);
        var order = new List<string>();

        var first = await gate.AcquireAsync(CancellationToken.None);
        order.Add("first-acquired");

        var waiting = Task.Run(async () =>
        {
            var second = await gate.AcquireAsync(CancellationToken.None);
            order.Add("second-acquired");
            second.Dispose();
        });

        await Task.Delay(120);
        Assert.Equal(["first-acquired"], order);

        first.Dispose();
        await waiting;

        Assert.Equal(["first-acquired", "second-acquired"], order);
    }

    [Fact]
    public async Task Gate_AllowsParallelWorkUpToItsLimit()
    {
        using var gate = new ProviderConcurrencyGate(3);

        var leases = new List<IDisposable>();
        for (var i = 0; i < 3; i++)
        {
            leases.Add(await gate.AcquireAsync(CancellationToken.None));
        }

        Assert.Equal(0, gate.CurrentlyWaiting);

        foreach (var lease in leases)
        {
            lease.Dispose();
        }

        Assert.Equal(3, gate.CurrentlyWaiting);
    }
}
