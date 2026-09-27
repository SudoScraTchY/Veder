using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Identity;

/// <summary>
/// Identity store for Veder. Accounts are deliberately optional: nothing on the weather path requires
/// a user, and this context exists so that future personalisation (saved locations, alert
/// subscriptions) has somewhere durable to live without ever forcing a sign-in.
/// </summary>
public sealed class VederIdentityDbContext(DbContextOptions<VederIdentityDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    /// <summary>Schema name keeps the identity tables clearly separated from domain data.</summary>
    public const string Schema = "identity";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);
    }
}
