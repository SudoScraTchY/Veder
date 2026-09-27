using Domain.Entities.Exceptions;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Identity;

/// <summary>
/// Anonymous-first Identity wiring.
///
/// The contract this file has to guarantee: an unauthenticated request to any weather endpoint is
/// served normally, and if a caller does hit something that needs an account, the API answers 401
/// rather than redirecting to a login page. Identity here is additive - it exists for optional
/// accounts, never as a gate in front of the product.
/// </summary>
public static class IdentityRegistration
{
    public const string ConnectionStringName = "VederIdentity";

    /// <summary>Registers the Identity store, the API endpoints' backing services, and the cookie behaviour.</summary>
    /// <summary>Returns false when no connection string is configured - Identity is an optional feature.</summary>
    public static bool AddVederIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        // Identity is optional: with no connection string the host still boots and every weather
        // feature keeps working anonymously.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }
        services.AddDbContext<VederIdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__identity_migrations", VederIdentityDbContext.Schema)));

        services.AddAuthorization();

        services
            .AddIdentityApiEndpoints<IdentityUser>(options =>
            {
                // Sane defaults for an optional account rather than a security-critical gate.
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<VederIdentityDbContext>();

        services.ConfigureApplicationCookie(options =>
        {
            // The API must never answer an anonymous caller with a redirect to a login page.
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });

        return true;
    }

    /// <summary>
    /// Maps the framework's Identity endpoints (/register, /login, /refresh, /confirmEmail, /manage/*).
    /// They are entirely optional for consumers of the weather API.
    /// </summary>
    public static IEndpointRouteBuilder MapVederIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        // The framework's Identity endpoints cover register / login / refresh / confirm / manage but
        // deliberately omit sign-out, so it is provided here. Without it "logout works" is simply false.
        app.MapPost("/logout", async (SignInManager<IdentityUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Ok(new { signedOut = true });
        })
        .WithName("Logout");

        app.MapIdentityApi<IdentityUser>();
        return app;
    }
}
