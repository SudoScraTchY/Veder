using Domain.Entities;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;
using Shared.Contracts.Commands;
using Shared.Contracts.Responses;
using Cortex.Mediator.Commands;
using ErrorOr;

namespace UseCases.Handlers.Commands;

public sealed class SaveLocationCommandHandler : ICommandHandler<SaveLocationCommand, ErrorOr<SavedLocationResponse>>
{
    private readonly ISavedLocationRepository _repository;

    public SaveLocationCommandHandler(ISavedLocationRepository repository)
    {
        _repository = repository;
    }

    public async Task<ErrorOr<SavedLocationResponse>> Handle(SaveLocationCommand command, CancellationToken ct)
    {
        var location = new SavedLocation(
            Id: Guid.NewGuid().ToString(),
            UserId: command.UserId,
            Label: command.Label,
            Location: command.Location
        );

        await _repository.AddAsync(location, ct);

        return new SavedLocationResponse(
            Id: location.Id,
            UserId: location.UserId,
            Label: location.Label,
            Location: location.Location
        );
    }
}

public sealed class DeleteLocationCommandHandler : ICommandHandler<DeleteLocationCommand, ErrorOr<Deleted>>
{
    private readonly ISavedLocationRepository _repository;

    public DeleteLocationCommandHandler(ISavedLocationRepository repository)
    {
        _repository = repository;
    }

    public async Task<ErrorOr<Deleted>> Handle(DeleteLocationCommand command, CancellationToken ct)
    {
        await _repository.DeleteAsync(command.UserId, command.LocationId, ct);
        return Result.Deleted;
    }
}

public sealed class UpdateProviderPriorityCommandHandler : ICommandHandler<UpdateProviderPriorityCommand, ErrorOr<Updated>>
{
    private readonly IProviderRegistry _providerRegistry;

    public UpdateProviderPriorityCommandHandler(IProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry;
    }

    public async Task<ErrorOr<Updated>> Handle(UpdateProviderPriorityCommand command, CancellationToken ct)
    {
        var ok = await _providerRegistry.SetPriorityAsync(command.ProviderId, command.Priority, ct);
        return ok ? Result.Updated : Error.NotFound("Provider.NotFound", $"Provider {command.ProviderId} not found");
    }
}

public sealed class ToggleProviderCommandHandler : ICommandHandler<ToggleProviderCommand, ErrorOr<Updated>>
{
    private readonly IProviderRegistry _providerRegistry;

    public ToggleProviderCommandHandler(IProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry;
    }

    public async Task<ErrorOr<Updated>> Handle(ToggleProviderCommand command, CancellationToken ct)
    {
        var ok = await _providerRegistry.SetEnabledAsync(command.ProviderId, command.Enabled, ct);
        return ok ? Result.Updated : Error.NotFound("Provider.NotFound", $"Provider {command.ProviderId} not found");
    }
}