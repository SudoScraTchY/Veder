namespace Infrastructure.Persistence;

/// <summary>
/// Where provider state lives. Defaults outside the repository, under the user's local application
/// data, so records and credentials are never accidentally committed.
/// </summary>
public sealed class ProviderStoreOptions
{
    public const string SectionName = "ProviderStore";

    /// <summary>Root directory for the JSON document store. Empty means the platform default.</summary>
    public string? RootPath { get; set; }

    /// <summary>How many recent call records a query may return at most.</summary>
    public int MaxQueryCount { get; set; } = 500;

    public string ResolveRootPath() => string.IsNullOrWhiteSpace(RootPath)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Veder",
            "provider-store")
        : Path.GetFullPath(RootPath!);
}
