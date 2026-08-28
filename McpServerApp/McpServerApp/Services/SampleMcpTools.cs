using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace McpServerApp.Services;

[McpServerToolType]
public class SampleMcpTools
{
    [Description("Get server process metrics: OS and framework description, processor count, process working-set bytes, uptime, and UTC time.")]
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

    [Description("Query fixed, simulated customer fixture records by region or account tier. totalFound is the number of records matching the filters before the returned page is limited.")]
    [McpServerTool(Name = "query_customers", ReadOnly = true, Idempotent = true)]
    public static string QueryCustomers(
        [Description("Filter by customer region, e.g. 'NorthAmerica', 'Europe', 'Asia'")] string? region = null,
        [Description("Exact account tier filter: 'Standard', 'Premium', or 'Enterprise'.")] string? tier = null,
        [Description("Maximum records to return. Values are clamped to the inclusive range 1-50.")] int limit = 5)
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

        var matchingCustomers = query.ToList();
        var results = matchingCustomers.Take(Math.Clamp(limit, 1, 50)).ToList();

        return JsonSerializer.Serialize(new
        {
            totalFound = matchingCustomers.Count,
            customers = results
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Calculate compound investment growth using decimal arithmetic for a teaching example; this is not financial advice.")]
    [McpServerTool(Name = "calculate_compound_interest", ReadOnly = true, Idempotent = true)]
    public static string CalculateCompoundInterest(
        [Description("Initial principal amount in USD. Must be greater than zero.")] decimal principal,
        [Description("Annual interest rate percentage (e.g. 5.5 for 5.5%). Must be greater than zero.")] decimal annualRatePercent,
        [Description("Investment duration in whole years. Must be between 1 and 100.")] int years,
        [Description("Compounding periods per year. Must be between 1 and 365; the total number of periods must not exceed 36,500.")] int compoundFrequency = 12)
    {
        if (principal <= 0 || annualRatePercent <= 0 || years <= 0 || compoundFrequency <= 0)
        {
            throw new McpException("principal, annualRatePercent, years, and compoundFrequency must all be greater than zero.");
        }

        if (years > 100 || compoundFrequency > 365 || (long)years * compoundFrequency > 36_500)
        {
            throw new McpException("years must be at most 100, compoundFrequency at most 365, and total compounding periods at most 36,500.");
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

    [Description("Get deterministic, simulated weather fixture data for a specified city. This is not live meteorological data.")]
    [McpServerTool(Name = "get_weather_forecast", ReadOnly = true, Idempotent = true)]
    public static string GetWeatherForecast(
        [Description("Non-empty city name (e.g. 'Seattle', 'London', 'Tokyo').")] string city,
        [Description("Temperature scale. Must be exactly 'celsius' or 'fahrenheit' (case-insensitive).")] string unit = "celsius")
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            throw new McpException("city must be a non-empty string.");
        }

        if (string.IsNullOrWhiteSpace(unit) ||
            (!unit.Equals("celsius", StringComparison.OrdinalIgnoreCase) &&
             !unit.Equals("fahrenheit", StringComparison.OrdinalIgnoreCase)))
        {
            throw new McpException("unit must be either 'celsius' or 'fahrenheit'.");
        }

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
