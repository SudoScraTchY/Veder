using Cortex.Mediator;
using ErrorOr;
using Shared.Contracts.Queries;
using Shared.Contracts.Responses;

namespace WebApi.Endpoints;

public static class ProvidersEndpoints
{
    public static IEndpointRouteBuilder MapProvidersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/providers", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.QueryAsync(new GetAvailableProvidersQuery(), ct);
            return result.IsError ? ToProblem(result.FirstError) : Results.Ok(result.Value);
        })
        .WithName("GetAvailableProviders");

        return app;
    }

    private static IResult ToProblem(Error error) => error.Type switch
    {
        ErrorType.NotFound => Results.Problem(error.Description, statusCode: StatusCodes.Status404NotFound, title: error.Code),
        ErrorType.Validation => Results.Problem(error.Description, statusCode: StatusCodes.Status400BadRequest, title: error.Code),
        ErrorType.Unauthorized => Results.Problem(error.Description, statusCode: StatusCodes.Status401Unauthorized, title: error.Code),
        ErrorType.Forbidden => Results.Problem(error.Description, statusCode: StatusCodes.Status403Forbidden, title: error.Code),
        _ => Results.Problem(error.Description, statusCode: StatusCodes.Status503ServiceUnavailable, title: error.Code)
    };
}
