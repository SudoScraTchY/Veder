using Domain.Entities;
using Domain.Entities.Interfaces;
using Domain.Entities.ValueObjects;
using Shared.Contracts.Queries;
using Shared.Contracts.Responses;
using Cortex.Mediator.Queries;
using ErrorOr;

namespace UseCases.Handlers.Queries;

public sealed class GetAirQualityQueryHandler : IQueryHandler<GetAirQualityQuery, ErrorOr<AirQualityResponse>>
{
    private readonly IProviderSelector _providerSelector;

    public GetAirQualityQueryHandler(IProviderSelector providerSelector)
    {
        _providerSelector = providerSelector;
    }

    public async Task<ErrorOr<AirQualityResponse>> Handle(GetAirQualityQuery query, CancellationToken ct)
    {
        var (reading, providerId, fromCache) = await _providerSelector.ResolveAirQualityAsync(query.Location, query.ProviderId, ct);

        return new AirQualityResponse(
            Location: reading.Location,
            AqiValue: reading.Index.Value,
            AqiCategory: reading.Index.Category,
            DominantPollutant: reading.Index.DominantPollutant,
            MeasuredAt: reading.MeasuredAt,
            Pollutants: reading.Pollutants,
            ProviderId: providerId
        );
    }
}