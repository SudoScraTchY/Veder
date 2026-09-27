using Domain.Entities;
using Domain.Entities.Interfaces;
using Domain.Entities.Enumerations;
using Shared.Contracts.Queries;
using Shared.Contracts.Responses;
using Cortex.Mediator.Queries;
using ErrorOr;

namespace UseCases.Handlers.Queries;

public sealed class GetAvailableProvidersQueryHandler : IQueryHandler<GetAvailableProvidersQuery, ErrorOr<IReadOnlyList<ProviderResponse>>>
{
    private readonly IProviderRegistry _providerRegistry;

    public GetAvailableProvidersQueryHandler(IProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry;
    }

    public async Task<ErrorOr<IReadOnlyList<ProviderResponse>>> Handle(GetAvailableProvidersQuery query, CancellationToken ct)
    {
        var providers = _providerRegistry.GetAll()
            .Select(p => new ProviderResponse(
                Id: p.Id,
                DisplayName: p.DisplayName,
                Capabilities: Enum.GetValues<ProviderCapability>().Where(f => f != ProviderCapability.None && f != ProviderCapability.All && p.Capabilities.HasFlag(f)).Select(f => f.ToString()).ToArray(),
                Priority: p.Priority,
                IsEnabled: p.IsEnabled,
                ApiKeyExpiresAt: p.ApiKeyExpiresAt,
                DailyQuota: p.DailyQuota,
                UsedQuota: p.UsedQuota,
                IsHealthy: p.IsHealthy
            ))
            .ToList();

        return providers;
    }
}