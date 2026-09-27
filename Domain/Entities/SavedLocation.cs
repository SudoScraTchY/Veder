using Domain.Entities.ValueObjects;

namespace Domain.Entities;

public sealed record SavedLocation(string Id, string UserId, string Label, Coordinates Location);