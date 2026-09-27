using Domain.Entities.ValueObjects;
using Cortex.Mediator.Commands;
using ErrorOr;
using Shared.Contracts.Responses;

namespace Shared.Contracts.Commands;

public sealed record SaveLocationCommand(string UserId, string Label, Coordinates Location) : ICommand<ErrorOr<SavedLocationResponse>>;

public sealed record DeleteLocationCommand(string UserId, string LocationId) : ICommand<ErrorOr<Deleted>>;

public sealed record UpdateProviderPriorityCommand(string ProviderId, int Priority) : ICommand<ErrorOr<Updated>>;

public sealed record ToggleProviderCommand(string ProviderId, bool Enabled) : ICommand<ErrorOr<Updated>>;