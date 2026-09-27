using Domain.Entities;
using Domain.Entities.ValueObjects;
using Infrastructure.Persistence.InMemory;

namespace Veder.Tests;

public sealed class SavedLocationRepositoryTests
{
    private static readonly Coordinates London = Coordinates.FromDegrees(51.5, -0.12);

    [Fact]
    public async Task Add_ThenRead_ReturnsTheStoredLocation()
    {
        var repository = new InMemorySavedLocationRepository();
        var location = new SavedLocation("loc-1", "user-1", "Home", London);

        await repository.AddAsync(location, CancellationToken.None);
        var all = await repository.GetByUserIdAsync("user-1", CancellationToken.None);
        var single = await repository.GetByIdAsync("user-1", "loc-1", CancellationToken.None);

        Assert.Single(all);
        Assert.Equal("Home", all[0].Label);
        Assert.NotNull(single);
        Assert.Equal(London, single!.Location);
    }

    [Fact]
    public async Task Locations_AreScopedToTheirUser()
    {
        var repository = new InMemorySavedLocationRepository();

        await repository.AddAsync(new SavedLocation("loc-1", "user-1", "Home", London), CancellationToken.None);
        await repository.AddAsync(new SavedLocation("loc-2", "user-2", "Work", London), CancellationToken.None);

        Assert.Single(await repository.GetByUserIdAsync("user-1", CancellationToken.None));
        Assert.Single(await repository.GetByUserIdAsync("user-2", CancellationToken.None));
        Assert.Null(await repository.GetByIdAsync("user-1", "loc-2", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheTargetedLocation()
    {
        var repository = new InMemorySavedLocationRepository();
        await repository.AddAsync(new SavedLocation("loc-1", "user-1", "Home", London), CancellationToken.None);
        await repository.AddAsync(new SavedLocation("loc-2", "user-1", "Work", London), CancellationToken.None);

        await repository.DeleteAsync("user-1", "loc-1", CancellationToken.None);

        var remaining = await repository.GetByUserIdAsync("user-1", CancellationToken.None);
        Assert.Single(remaining);
        Assert.Equal("loc-2", remaining[0].Id);
    }

    [Fact]
    public async Task UnknownUser_ReturnsAnEmptyList()
    {
        var repository = new InMemorySavedLocationRepository();

        Assert.Empty(await repository.GetByUserIdAsync("nobody", CancellationToken.None));
    }
}
