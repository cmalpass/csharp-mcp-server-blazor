using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace McpServerApp.Tests;

public class McpServerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public McpServerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSse_ReturnsEventStreamWithEndpoint()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/sse", HttpCompletionOption.ResponseHeadersRead);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        // Read the first chunk (should contain "event: endpoint")
        char[] buffer = new char[256];
        int read = await reader.ReadAsync(buffer, 0, buffer.Length);
        var content = new string(buffer, 0, read);

        content.Should().Contain("event: endpoint");
        content.Should().Contain("data: /messages?sessionId=");
    }

    [Fact]
    public async Task PostMessages_Initialize_ReturnsProtocolCapabilities()
    {
        // Arrange
        var client = _factory.CreateClient();
        var initPayload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                clientInfo = new { name = "IntegrationTestClient", version = "1.0.0" }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(initPayload), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/messages", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        doc.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        doc.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("CsharpMcpServer");
    }

    [Fact]
    public async Task PostMessages_ToolsList_ReturnsRegisteredTools()
    {
        // Arrange
        var client = _factory.CreateClient();
        var listPayload = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list",
            @params = new { }
        };

        var content = new StringContent(JsonSerializer.Serialize(listPayload), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/messages", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PostMessages_ToolsCall_ExecutesCsharpTool()
    {
        // Arrange
        var client = _factory.CreateClient();
        var callPayload = new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new
            {
                name = "get_system_metrics",
                arguments = new { }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(callPayload), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/messages", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var result = doc.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeFalse();
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        text.Should().Contain("os");
    }

    [Fact]
    public async Task PostMcp_Initialize_EchoesSupportedStreamableHttpVersion()
    {
        // Arrange
        var client = _factory.CreateClient();
        var initPayload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-06-18",
                clientInfo = new { name = "IntegrationTestClient", version = "1.0.0" }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(initPayload), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/mcp", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString().Should().Be("2025-06-18");
    }

    [Fact]
    public async Task PostMcp_Initialize_UnsupportedVersion_FallsBackToTransportDefault()
    {
        // Arrange
        var client = _factory.CreateClient();
        var initPayload = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2020-01-01",
                clientInfo = new { name = "IntegrationTestClient", version = "1.0.0" }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(initPayload), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/mcp", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString().Should().Be("2025-06-18");
    }

    [Fact]
    public async Task PostMcp_InitializedNotification_Returns202Accepted()
    {
        // Arrange
        var client = _factory.CreateClient();
        var notification = new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        };

        var content = new StringContent(JsonSerializer.Serialize(notification), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/mcp", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task PostMcp_ToolsCall_ExecutesTool()
    {
        // Arrange
        var client = _factory.CreateClient();
        var callPayload = new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new
            {
                name = "get_system_metrics",
                arguments = new { }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(callPayload), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/mcp", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = doc.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeFalse();
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        text.Should().Contain("os");
    }

    [Fact]
    public async Task GetMcp_ReturnsMethodNotAllowed()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/mcp");

        // Assert: server does not open server-initiated SSE streams
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}
