namespace Domain.Entities.ValueObjects;

public readonly record struct AirQualityIndex(int Value, string Category, string DominantPollutant)
{
    public static AirQualityIndex FromValue(int value)
    {
        var (category, pollutant) = value switch
        {
            <= 50 => ("Good", "PM2.5"),
            <= 100 => ("Moderate", "PM2.5"),
            <= 150 => ("Unhealthy for Sensitive Groups", "PM2.5"),
            <= 200 => ("Unhealthy", "PM2.5"),
            <= 300 => ("Very Unhealthy", "PM2.5"),
            _ => ("Hazardous", "PM2.5")
        };
        return new AirQualityIndex(value, category, pollutant);
    }

    public bool IsHealthy => Value <= 100;

    public override string ToString() => $"{Category} (AQI: {Value})";
}