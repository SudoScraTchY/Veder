using Cortex.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using UseCases.Handlers.Queries;

namespace UseCases.Helpers.DI;

public static class DependencyInjection
{
    public static IServiceCollection AddUseCases(this IServiceCollection services)
    {
        services.AddCortexMediator(
            new[] { typeof(GetWeatherForecastQueryHandler) },
            options => options.AddDefaultBehaviors());

        return services;
    }
}
