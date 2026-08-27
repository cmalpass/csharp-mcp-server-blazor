using FluentAssertions;
using McpServerApp.Services;
using System.Text.Json;
using Xunit;

namespace McpServerApp.Tests;

public class McpToolsUnitTests
{
    [Fact]
    public void GetSystemMetrics_ReturnsValidJsonWithRequiredProperties()
    {
        // Act
        var json = SampleMcpTools.GetSystemMetrics();

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("os", out var os).Should().BeTrue();
        doc.RootElement.TryGetProperty("framework", out var fw).Should().BeTrue();
        doc.RootElement.TryGetProperty("processors", out var proc).Should().BeTrue();
        proc.GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public void QueryCustomers_WithFilter_ReturnsMatchingCustomers()
    {
        // Act
        var json = SampleMcpTools.QueryCustomers(region: "NorthAmerica", limit: 2);

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("totalFound").GetInt32().Should().BeGreaterThan(0);
        var customers = doc.RootElement.GetProperty("customers");
        customers.GetArrayLength().Should().BeLessThanOrEqualTo(2);
        customers[0].GetProperty("region").GetString().Should().Be("NorthAmerica");
    }

    [Fact]
    public void CalculateCompoundInterest_ValidParameters_ComputesCorrectYield()
    {
        // Act
        var json = SampleMcpTools.CalculateCompoundInterest(principal: 10000, annualRatePercent: 5.0, years: 2, compoundFrequency: 1);

        // Assert
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("futureValue").GetDouble().Should().Be(11025.0);
        doc.RootElement.GetProperty("totalInterestEarned").GetDouble().Should().Be(1025.0);
    }

    [Fact]
    public async Task McpServerRegistry_ToolDiscoveryAndExecution_WorksEndToEnd()
    {
        // Arrange
        var registry = new McpServerRegistry();

        // Act 1: Discover Tools
        var tools = registry.GetToolDefinitions();

        // Assert 1
        tools.Should().HaveCountGreaterThanOrEqualTo(4);
        tools.Should().Contain(t => t.Name == "get_weather_forecast");

        // Act 2: Execute Tool
        var args = new Dictionary<string, object?> { ["city"] = "London", ["unit"] = "celsius" };
        var callResult = await registry.CallToolAsync("get_weather_forecast", args);

        // Assert 2
        callResult.IsError.Should().BeFalse();
        callResult.GetTextContent().Should().Contain("London");
    }
}
