using Domain.Caching;
using Domain.Entities.Exceptions;

namespace Veder.Tests;

/// <summary>
/// The failover rules the review demanded: availability-only substitution, strict callers never
/// substituted, and request rejections excluded from provider health.
/// </summary>
public sealed class ProviderFailurePolicyTests
{
    private sealed class Malformed : Exception, IProviderMalformedResponseException;

    [Fact]
    public void ExceptionsMapToTheRightFailureKind()
    {
        Assert.Equal(ProviderFailureKind.None, ProviderFailurePolicy.Classify(null));
        Assert.Equal(ProviderFailureKind.RateLimit, ProviderFailurePolicy.Classify(new ProviderRateLimitedException("open-meteo", TimeSpan.FromSeconds(30))));
        Assert.Equal(ProviderFailureKind.Authentication, ProviderFailurePolicy.Classify(new ProviderAuthenticationException("open-meteo", "bad key")));
        Assert.Equal(ProviderFailureKind.Rejection, ProviderFailurePolicy.Classify(new ProviderRequestException("open-meteo", "unknown variable")));
        Assert.Equal(ProviderFailureKind.Availability, ProviderFailurePolicy.Classify(new ProviderUnavailableException("open-meteo")));
        Assert.Equal(ProviderFailureKind.Timeout, ProviderFailurePolicy.Classify(new TaskCanceledException()));
        Assert.Equal(ProviderFailureKind.Availability, ProviderFailurePolicy.Classify(new HttpRequestException("connection reset")));
        Assert.Equal(ProviderFailureKind.MalformedResponse, ProviderFailurePolicy.Classify(new Malformed()));
    }

    [Fact]
    public void RequestRejectionNeverCountsAgainstProviderHealth()
    {
        // The inland-marine case: a legitimate "no coverage" answer must not degrade weather for everyone.
        Assert.False(ProviderFailurePolicy.AffectsProviderHealth(ProviderFailureKind.Rejection));
        Assert.False(ProviderFailurePolicy.AffectsProviderHealth(ProviderFailureKind.Authentication));

        Assert.True(ProviderFailurePolicy.AffectsProviderHealth(ProviderFailureKind.Availability));
        Assert.True(ProviderFailurePolicy.AffectsProviderHealth(ProviderFailureKind.Timeout));
        Assert.True(ProviderFailurePolicy.AffectsProviderHealth(ProviderFailureKind.RateLimit));
        Assert.True(ProviderFailurePolicy.AffectsProviderHealth(ProviderFailureKind.MalformedResponse));
        Assert.False(ProviderFailurePolicy.AffectsProviderHealth(ProviderFailureKind.None));
    }

    [Fact]
    public void ARejectionDoesNotTriggerFailover()
    {
        Assert.False(ProviderFailurePolicy.AllowsFailover(ProviderFailureKind.Rejection));
        Assert.False(ProviderFailurePolicy.AllowsFailover(ProviderFailureKind.Authentication));
        Assert.False(ProviderFailurePolicy.AllowsFailover(ProviderFailureKind.None));

        Assert.True(ProviderFailurePolicy.AllowsFailover(ProviderFailureKind.Availability));
        Assert.True(ProviderFailurePolicy.AllowsFailover(ProviderFailureKind.RateLimit));
        Assert.True(ProviderFailurePolicy.AllowsFailover(ProviderFailureKind.Timeout));
    }

    [Fact]
    public void NoRequestedProviderMeansAnyProviderMayServe()
    {
        Assert.True(ProviderFailurePolicy.MaySubstitute(null, strictProvider: false, ProviderFailureKind.Rejection));
        Assert.True(ProviderFailurePolicy.MaySubstitute("", strictProvider: true, ProviderFailureKind.Rejection));
    }

    [Fact]
    public void APreferredProviderIsSubstitutedOnlyOnAvailabilityFailures()
    {
        Assert.True(ProviderFailurePolicy.MaySubstitute("openweathermap", strictProvider: false, ProviderFailureKind.Availability));
        Assert.True(ProviderFailurePolicy.MaySubstitute("openweathermap", strictProvider: false, ProviderFailureKind.RateLimit));

        // A wrong request stays wrong whoever we ask.
        Assert.False(ProviderFailurePolicy.MaySubstitute("openweathermap", strictProvider: false, ProviderFailureKind.Rejection));
    }

    [Fact]
    public void AStrictProviderIsNeverSubstituted()
    {
        foreach (var kind in Enum.GetValues<ProviderFailureKind>())
        {
            Assert.False(ProviderFailurePolicy.MaySubstitute("openweathermap", strictProvider: true, kind));
        }
    }

    [Fact]
    public void OutcomeNamesAreStable()
    {
        Assert.Equal("request-rejected", ProviderFailurePolicy.ToOutcomeName(ProviderFailureKind.Rejection));
        Assert.Equal("rate-limited", ProviderFailurePolicy.ToOutcomeName(ProviderFailureKind.RateLimit));
        Assert.Equal("provider-unavailable", ProviderFailurePolicy.ToOutcomeName(ProviderFailureKind.Availability));
        Assert.Equal("none", ProviderFailurePolicy.ToOutcomeName(ProviderFailureKind.None));
    }
}
