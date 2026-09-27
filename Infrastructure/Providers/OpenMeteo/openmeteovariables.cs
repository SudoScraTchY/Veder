namespace Infrastructure.Providers.OpenMeteo;

/// <summary>One documented Open-Meteo dataset, each served from its own host.</summary>
public enum OpenMeteoDataset
{
    Forecast,
    Archive,
    HistoricalForecast,
    AirQuality,
    Marine,
    Ensemble,
    Climate,
    Flood
}

/// <summary>
/// Catalogue of the variable names each dataset accepts, used to validate a request before it
/// reaches the wire. Every list mirrors the published Open-Meteo documentation.
/// </summary>
public static class OpenMeteoVariables
{
    public static IReadOnlyList<string> Current { get; } =
    [
        "temperature_2m", "relative_humidity_2m", "apparent_temperature", "is_day", "precipitation",
        "rain", "showers", "snowfall", "weather_code", "cloud_cover", "pressure_msl", "surface_pressure",
        "wind_speed_10m", "wind_direction_10m", "wind_gusts_10m"
    ];

    public static IReadOnlyList<string> Minutely15 { get; } =
    [
        "temperature_2m", "relative_humidity_2m", "dew_point_2m", "apparent_temperature", "precipitation",
        "rain", "snowfall", "weather_code", "wind_speed_10m", "wind_gusts_10m", "freezing_level_height",
        "visibility", "cape", "lightning_potential", "shortwave_radiation", "direct_radiation",
        "diffuse_radiation", "direct_normal_irradiance", "global_tilted_irradiance", "terrestrial_radiation"
    ];

    public static IReadOnlyList<string> Hourly { get; } =
    [
        "temperature_2m", "relative_humidity_2m", "dew_point_2m", "apparent_temperature",
        "precipitation_probability", "precipitation", "rain", "showers", "snowfall", "snow_depth",
        "weather_code", "pressure_msl", "surface_pressure", "cloud_cover", "cloud_cover_low",
        "cloud_cover_mid", "cloud_cover_high", "visibility", "evapotranspiration",
        "et0_fao_evapotranspiration", "vapour_pressure_deficit", "wind_speed_10m", "wind_speed_80m",
        "wind_speed_120m", "wind_speed_180m", "wind_direction_10m", "wind_direction_80m",
        "wind_direction_120m", "wind_direction_180m", "wind_gusts_10m", "temperature_80m",
        "temperature_120m", "temperature_180m", "soil_temperature_0cm", "soil_temperature_6cm",
        "soil_temperature_18cm", "soil_temperature_54cm", "soil_moisture_0_to_1cm",
        "soil_moisture_1_to_3cm", "soil_moisture_3_to_9cm", "soil_moisture_9_to_27cm",
        "soil_moisture_27_to_81cm", "uv_index", "uv_index_clear_sky", "is_day", "freezing_level_height",
        "sunshine_duration", "wet_bulb_temperature_2m", "total_column_integrated_water_vapour", "cape",
        "lifted_index", "convective_inhibition", "shortwave_radiation", "direct_radiation",
        "direct_normal_irradiance", "diffuse_radiation", "global_tilted_irradiance",
        "terrestrial_radiation", "boundary_layer_height", "albedo"
    ];

    public static IReadOnlyList<string> Daily { get; } =
    [
        "weather_code", "temperature_2m_max", "temperature_2m_min", "apparent_temperature_max",
        "apparent_temperature_min", "sunrise", "sunset", "daylight_duration", "sunshine_duration",
        "uv_index_max", "uv_index_clear_sky_max", "precipitation_sum", "rain_sum", "showers_sum",
        "snowfall_sum", "precipitation_hours", "precipitation_probability_max",
        "precipitation_probability_min", "precipitation_probability_mean", "wind_speed_10m_max",
        "wind_gusts_10m_max", "wind_direction_10m_dominant", "shortwave_radiation_sum",
        "et0_fao_evapotranspiration"
    ];

    public static IReadOnlyList<string> AirQuality { get; } =
    [
        "european_aqi", "european_aqi_pm2_5", "european_aqi_pm10", "european_aqi_nitrogen_dioxide",
        "european_aqi_ozone", "european_aqi_sulphur_dioxide", "us_aqi", "us_aqi_pm2_5", "us_aqi_pm10",
        "us_aqi_nitrogen_dioxide", "us_aqi_ozone", "us_aqi_sulphur_dioxide", "us_aqi_carbon_monoxide",
        "pm10", "pm2_5", "carbon_monoxide", "nitrogen_dioxide", "sulphur_dioxide", "ozone",
        "aerosol_optical_depth", "dust", "uv_index", "uv_index_clear_sky", "ammonia", "alder_pollen",
        "birch_pollen", "grass_pollen", "mugwort_pollen", "olive_pollen", "ragweed_pollen"
    ];

    public static IReadOnlyList<string> Marine { get; } =
    [
        "wave_height", "wave_direction", "wave_period", "wind_wave_height", "wind_wave_direction",
        "wind_wave_period", "wind_wave_peak_period", "swell_wave_height", "swell_wave_direction",
        "swell_wave_period", "swell_wave_peak_period", "ocean_current_velocity", "ocean_current_direction",
        "sea_surface_temperature", "sea_level_height_msl"
    ];

    public static IReadOnlyList<string> MarineDaily { get; } =
    [
        "wave_height_max", "wave_direction_dominant", "wave_period_max", "wind_wave_height_max",
        "wind_wave_direction_dominant", "wind_wave_period_max", "wind_wave_peak_period_max",
        "swell_wave_height_max", "swell_wave_direction_dominant", "swell_wave_period_max",
        "swell_wave_peak_period_max"
    ];

    public static IReadOnlyList<string> Ensemble { get; } =
    [
        "temperature_2m", "relative_humidity_2m", "dew_point_2m", "apparent_temperature", "precipitation",
        "rain", "snowfall", "weather_code", "pressure_msl", "surface_pressure", "cloud_cover",
        "wind_speed_10m", "wind_direction_10m", "wind_gusts_10m", "shortwave_radiation",
        "direct_radiation", "diffuse_radiation", "direct_normal_irradiance", "cape",
        "et0_fao_evapotranspiration"
    ];

    public static IReadOnlyList<string> Climate { get; } =
    [
        "temperature_2m_max", "temperature_2m_min", "temperature_2m_mean", "apparent_temperature_max",
        "apparent_temperature_min", "apparent_temperature_mean", "precipitation_sum", "rain_sum",
        "snowfall_sum", "precipitation_hours", "wind_speed_10m_max", "wind_speed_10m_mean",
        "wind_direction_10m_dominant", "shortwave_radiation_sum", "et0_fao_evapotranspiration",
        "relative_humidity_2m_max", "relative_humidity_2m_min", "relative_humidity_2m_mean",
        "cloud_cover_mean", "pressure_msl_mean", "soil_moisture_0_to_10cm_mean"
    ];

    public static IReadOnlyList<string> Flood { get; } =
    [
        "river_discharge", "river_discharge_mean", "river_discharge_median", "river_discharge_max",
        "river_discharge_min", "river_discharge_p25", "river_discharge_p75"
    ];

    /// <summary>Deterministic forecast models selectable through the <c>models</c> request parameter.</summary>
    public static IReadOnlyList<string> ForecastModels { get; } =
    [
        "best_match", "ecmwf_ifs025", "ecmwf_aifs025", "gfs_seamless", "gfs_global", "gfs_hrrr",
        "icon_seamless", "icon_global", "icon_eu", "icon_d2", "gem_seamless", "gem_global",
        "meteofrance_seamless", "meteofrance_arpege_world", "metno_nordic", "ukmo_seamless",
        "ukmo_global_deterministic_10km", "jma_seamless", "jma_gsm", "cma_grapes_global",
        "bom_access_global", "knmi_harmonie_arome_europe", "dmi_harmonie_arome_europe"
    ];

    /// <summary>CMIP6 high-resolution models selectable through the climate endpoint.</summary>
    public static IReadOnlyList<string> ClimateModels { get; } =
    [
        "CMCC_CM2_VHR4", "FGOALS_f3_H", "HiRAM_SIT_HR", "MRI_AGCM3_2_S", "EC_Earth3P_HR",
        "MPI_ESM1_2_XR", "NICAM16_8S"
    ];

    /// <summary>Groups accepted per dataset, in the wire spelling.</summary>
    public static IReadOnlyList<string> Groups(OpenMeteoDataset dataset) => dataset switch
    {
        OpenMeteoDataset.Forecast => ["current", "minutely_15", "hourly", "daily"],
        OpenMeteoDataset.Archive => ["hourly", "daily"],
        OpenMeteoDataset.HistoricalForecast => ["hourly", "daily"],
        OpenMeteoDataset.AirQuality => ["current", "hourly"],
        OpenMeteoDataset.Marine => ["current", "hourly", "daily"],
        OpenMeteoDataset.Ensemble => ["hourly", "daily"],
        OpenMeteoDataset.Climate => ["daily"],
        OpenMeteoDataset.Flood => ["daily"],
        _ => []
    };

    /// <summary>The documented variable list for a dataset/grouping pair, or null when the pair is unknown.</summary>
    public static IReadOnlyList<string>? For(OpenMeteoDataset dataset, string grouping) => (dataset, grouping) switch
    {
        (OpenMeteoDataset.Forecast, "current") => Current,
        (OpenMeteoDataset.Forecast, "minutely_15") => Minutely15,
        (OpenMeteoDataset.Forecast, "hourly") => Hourly,
        (OpenMeteoDataset.Forecast, "daily") => Daily,
        (OpenMeteoDataset.Archive, "hourly") => Hourly,
        (OpenMeteoDataset.Archive, "daily") => Daily,
        (OpenMeteoDataset.HistoricalForecast, "hourly") => Hourly,
        (OpenMeteoDataset.HistoricalForecast, "daily") => Daily,
        (OpenMeteoDataset.AirQuality, "current") => AirQuality,
        (OpenMeteoDataset.AirQuality, "hourly") => AirQuality,
        (OpenMeteoDataset.Marine, "current") => Marine,
        (OpenMeteoDataset.Marine, "hourly") => Marine,
        (OpenMeteoDataset.Marine, "daily") => MarineDaily,
        (OpenMeteoDataset.Ensemble, "hourly") => Ensemble,
        (OpenMeteoDataset.Ensemble, "daily") => Daily,
        (OpenMeteoDataset.Climate, "daily") => Climate,
        (OpenMeteoDataset.Flood, "daily") => Flood,
        _ => null
    };

    public static bool IsKnown(OpenMeteoDataset dataset, string grouping, string variable) =>
        For(dataset, grouping)?.Contains(variable, StringComparer.Ordinal) ?? false;

    /// <summary>Variables that exist on several datasets and therefore cannot be validated by name alone.</summary>
    public static bool IsUniversal(string variable) =>
        Hourly.Contains(variable, StringComparer.Ordinal) || Daily.Contains(variable, StringComparer.Ordinal);
}
