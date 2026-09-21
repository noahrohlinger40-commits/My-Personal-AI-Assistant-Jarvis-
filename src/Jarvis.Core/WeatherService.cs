using System.Globalization;
using System.Text.Json;

namespace Jarvis.Core;

public interface IWeatherService
{
    Task<string> GetCurrentWeatherAsync(string location, CancellationToken cancellationToken);
}

public sealed class OpenMeteoWeatherService : IWeatherService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<string> GetCurrentWeatherAsync(string location, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return "Usage: weather <location>. You can also set `defaultWeatherLocation` in jarvis.settings.json.";
        }

        var resolvedLocation = await ResolveLocationAsync(location.Trim(), cancellationToken);

        if (resolvedLocation is null)
        {
            return $"No weather location match found for \"{location.Trim()}\".";
        }

        var weatherUrl =
            $"https://api.open-meteo.com/v1/forecast?latitude={resolvedLocation.Latitude.ToString(CultureInfo.InvariantCulture)}" +
            $"&longitude={resolvedLocation.Longitude.ToString(CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,wind_direction_10m" +
            "&temperature_unit=fahrenheit" +
            "&wind_speed_unit=mph" +
            "&precipitation_unit=inch" +
            "&timezone=auto";

        using var weatherResponse = await HttpClient.GetAsync(weatherUrl, cancellationToken);
        var weatherBody = await weatherResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!weatherResponse.IsSuccessStatusCode)
        {
            return $"Weather lookup failed: HTTP {(int)weatherResponse.StatusCode}.";
        }

        using var weatherDocument = JsonDocument.Parse(weatherBody);
        var root = weatherDocument.RootElement;

        if (!root.TryGetProperty("current", out var current))
        {
            return $"Weather lookup failed for {resolvedLocation.DisplayName}.";
        }

        var currentUnits = root.TryGetProperty("current_units", out var units) ? units : default;
        var temperature = GetDouble(current, "temperature_2m");
        var apparentTemperature = GetDouble(current, "apparent_temperature");
        var weatherCode = GetInt32(current, "weather_code");
        var windSpeed = GetDouble(current, "wind_speed_10m");
        var windDirection = GetDouble(current, "wind_direction_10m");
        var observationTime = GetString(current, "time");
        var temperatureUnit = GetString(currentUnits, "temperature_2m") ?? "°F";
        var windUnit = GetString(currentUnits, "wind_speed_10m") ?? "mph";

        var lines = new List<string>
        {
            $"Current weather for {resolvedLocation.DisplayName}:",
            $"- Condition: {DescribeWeatherCode(weatherCode)}",
            $"- Temperature: {FormatNumber(temperature)}{temperatureUnit}",
            $"- Feels like: {FormatNumber(apparentTemperature)}{temperatureUnit}",
            $"- Wind: {FormatNumber(windSpeed)} {windUnit} {DescribeWindDirection(windDirection)}"
        };

        if (!string.IsNullOrWhiteSpace(observationTime))
        {
            lines.Add($"- Observed at: {observationTime} ({resolvedLocation.TimeZone})");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<ResolvedWeatherLocation?> ResolveLocationAsync(string location, CancellationToken cancellationToken)
    {
        var geocodeUrl =
            $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(location)}&count=1&language=en&format=json";

        using var geocodeResponse = await HttpClient.GetAsync(geocodeUrl, cancellationToken);
        var geocodeBody = await geocodeResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!geocodeResponse.IsSuccessStatusCode)
        {
            return null;
        }

        using var geocodeDocument = JsonDocument.Parse(geocodeBody);
        var root = geocodeDocument.RootElement;

        if (!root.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
        {
            return null;
        }

        var match = results[0];

        return new ResolvedWeatherLocation(
            match.GetProperty("latitude").GetDouble(),
            match.GetProperty("longitude").GetDouble(),
            BuildDisplayName(match),
            GetString(match, "timezone") ?? "local time");
    }

    private static string BuildDisplayName(JsonElement element)
    {
        var parts = new[]
        {
            GetString(element, "name"),
            GetString(element, "admin1"),
            GetString(element, "country")
        }.Where(value => !string.IsNullOrWhiteSpace(value))
         .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(", ", parts);
    }

    private static string DescribeWeatherCode(int code)
    {
        return code switch
        {
            0 => "Clear sky",
            1 => "Mainly clear",
            2 => "Partly cloudy",
            3 => "Overcast",
            45 or 48 => "Fog",
            51 or 53 or 55 => "Drizzle",
            56 or 57 => "Freezing drizzle",
            61 or 63 or 65 => "Rain",
            66 or 67 => "Freezing rain",
            71 or 73 or 75 or 77 => "Snow",
            80 or 81 or 82 => "Rain showers",
            85 or 86 => "Snow showers",
            95 => "Thunderstorm",
            96 or 99 => "Thunderstorm with hail",
            _ => $"Weather code {code}"
        };
    }

    private static string DescribeWindDirection(double degrees)
    {
        var directions = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        var normalized = ((degrees % 360) + 360) % 360;
        var index = (int)Math.Round(normalized / 45d, MidpointRounding.AwayFromZero) % directions.Length;
        return directions[index];
    }

    private static string FormatNumber(double value)
    {
        return double.IsNaN(value) ? "n/a" : value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static double GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return double.NaN;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)
            ? value
            : double.NaN;
    }

    private static int GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return -1;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : -1;
    }

    private sealed record ResolvedWeatherLocation(
        double Latitude,
        double Longitude,
        string DisplayName,
        string TimeZone);
}
