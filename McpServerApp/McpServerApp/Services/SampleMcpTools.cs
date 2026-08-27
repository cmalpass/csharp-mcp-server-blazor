using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace McpServerApp.Services;

[McpServerToolType]
public class SampleMcpTools
{
    [Description("Get server system metrics including OS description, processor count, and memory status.")]
    [McpServerTool(Name = "get_system_metrics", ReadOnly = true, Idempotent = true)]
    public static string GetSystemMetrics()
    {
        var metrics = new
        {
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            processors = Environment.ProcessorCount,
            workingSetBytes = Environment.WorkingSet,
            uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\.hh\:mm\:ss"),
            serverUtcTime = DateTime.UtcNow.ToString("o")
        };

        return JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Query the simulated enterprise database for customer records by region or account tier.")]
    [McpServerTool(Name = "query_customers", ReadOnly = true, Idempotent = true)]
    public static string QueryCustomers(
        [Description("Filter by customer region, e.g. 'NorthAmerica', 'Europe', 'Asia'")] string? region = null,
        [Description("Minimum account tier, e.g. 'Standard', 'Premium', 'Enterprise'")] string? tier = null,
        [Description("Maximum records to return (1-50)")] int limit = 5)
    {
        var sampleData = new[]
        {
            new { id = "CUST-101", name = "Acme Corp", region = "NorthAmerica", tier = "Enterprise", activeOrders = 14, balance = 45200.50 },
            new { id = "CUST-102", name = "Global Logistics", region = "Europe", tier = "Premium", activeOrders = 8, balance = 18900.00 },
            new { id = "CUST-103", name = "Pacific Wave", region = "Asia", tier = "Enterprise", activeOrders = 22, balance = 94300.75 },
            new { id = "CUST-104", name = "Nordic Retail", region = "Europe", tier = "Standard", activeOrders = 2, balance = 3200.00 },
            new { id = "CUST-105", name = "Lone Star Energy", region = "NorthAmerica", tier = "Enterprise", activeOrders = 31, balance = 128400.00 },
        };

        var query = sampleData.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(region))
        {
            query = query.Where(c => c.region.Equals(region, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(tier))
        {
            query = query.Where(c => c.tier.Equals(tier, StringComparison.OrdinalIgnoreCase));
        }

        var results = query.Take(Math.Clamp(limit, 1, 50)).ToList();

        return JsonSerializer.Serialize(new
        {
            totalFound = results.Count,
            customers = results
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Calculate compound investment growth using decimal arithmetic for a teaching example; this is not financial advice.")]
    [McpServerTool(Name = "calculate_compound_interest", ReadOnly = true, Idempotent = true)]
    public static string CalculateCompoundInterest(
        [Description("Initial principal amount in USD")] decimal principal,
        [Description("Annual interest rate percentage (e.g. 5.5 for 5.5%)")] decimal annualRatePercent,
        [Description("Investment duration in years")] int years,
        [Description("Compounding periods per year (1 for annual, 12 for monthly, 365 for daily)")] int compoundFrequency = 12)
    {
        if (principal <= 0 || annualRatePercent <= 0 || years <= 0 || compoundFrequency <= 0)
        {
            return JsonSerializer.Serialize(new { error = "All numerical inputs must be positive non-zero numbers." });
        }

        var rate = annualRatePercent / 100m;
        var futureValue = principal;
        var periodicRate = rate / compoundFrequency;
        var periods = checked(compoundFrequency * years);
        for (var period = 0; period < periods; period++)
        {
            futureValue *= 1m + periodicRate;
        }
        var totalInterest = futureValue - principal;

        var result = new
        {
            principal = decimal.Round(principal, 2, MidpointRounding.AwayFromZero),
            annualRate = $"{annualRatePercent}%",
            durationYears = years,
            futureValue = decimal.Round(futureValue, 2, MidpointRounding.AwayFromZero),
            totalInterestEarned = decimal.Round(totalInterest, 2, MidpointRounding.AwayFromZero),
            effectiveAnnualYield = decimal.Round((CalculateEffectiveAnnualYield(periodicRate, compoundFrequency)) * 100m, 3, MidpointRounding.AwayFromZero)
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    private static decimal CalculateEffectiveAnnualYield(decimal periodicRate, int compoundFrequency)
    {
        var result = 1m;
        for (var period = 0; period < compoundFrequency; period++)
        {
            result *= 1m + periodicRate;
        }

        return result - 1m;
    }

    [Description("Get simulated weather and forecast data for a specified city. Returns deterministic sample data for demonstration purposes, not live meteorological data.")]
    [McpServerTool(Name = "get_weather_forecast", ReadOnly = true, Idempotent = true)]
    public static string GetWeatherForecast(
        [Description("City name (e.g. 'Seattle', 'London', 'Tokyo')")] string city,
        [Description("Temperature scale: 'celsius' or 'fahrenheit'")] string unit = "celsius")
    {
        var isFahrenheit = unit.Equals("fahrenheit", StringComparison.OrdinalIgnoreCase);
        var baseTempC = (StableCityHash(city) % 30) + 5;
        var temp = isFahrenheit ? (baseTempC * 9 / 5) + 32 : baseTempC;
        var tempUnit = isFahrenheit ? "°F" : "°C";

        var forecast = new
        {
            city = city,
            currentTemperature = $"{temp}{tempUnit}",
            conditions = baseTempC > 20 ? "Sunny with scattered clouds" : (baseTempC > 10 ? "Mild and overcast" : "Cool and rainy"),
            humidity = $"{40 + (baseTempC * 2)}%",
            windSpeed = $"{10 + (baseTempC % 15)} km/h",
            sampleTimestampUtc = "2025-06-18T12:00:00.0000000Z"
        };

        return JsonSerializer.Serialize(forecast, new JsonSerializerOptions { WriteIndented = true });
    }

    // String.GetHashCode is deliberately randomized between .NET processes. A stable
    // FNV-1a hash keeps this sample fixture repeatable across machines and runs.
    private static int StableCityHash(string city)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in city.ToUpperInvariant())
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (int)(hash & 0x7fffffff);
        }
    }
}
