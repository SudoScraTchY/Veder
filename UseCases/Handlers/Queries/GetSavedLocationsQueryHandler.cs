using Domain.Entities;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;
using Shared.Contracts.Queries;
using Shared.Contracts.Responses;
using Cortex.Mediator.Queries;
using ErrorOr;

namespace UseCases.Handlers.Queries;

public sealed class GetSavedLocationsQueryHandler : IQueryHandler<GetSavedLocationsQuery, ErrorOr<IReadOnlyList<SavedLocationResponse>>>
{
    private readonly ISavedLocationRepository _repository;

    public GetSavedLocationsQueryHandler(ISavedLocationRepository repository)
    {
        _repository = repository;
    }

    public async Task<ErrorOr<IReadOnlyList<SavedLocationResponse>>> Handle(GetSavedLocationsQuery query, CancellationToken ct)
    {
        var locations = await _repository.GetByUserIdAsync(query.UserId, ct);

        var responses = locations
            .Select(l => new SavedLocationResponse(
                Id: l.Id,
                UserId: l.UserId,
                Label: l.Label,
                Location: l.Location
            ))
            .ToList();

        return responses;
    }
}