using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace McpServerApp.Tests;

public class McpServerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string CurrentProtocolVersion = "2026-07-28";
    private readonly WebApplicationFactory<Program> _factory;

    public McpServerIntegrationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task PostMcp_ServerDiscover_UsesCurrentStatelessProtocol()
    {
        using var client = _factory.CreateClient();
        using var response = await SendCurrentRequestAsync(client, "server/discover", """
            { "jsonrpc":"2.0", "id":1, "method":"server/discover", "params": { "_meta": {
                "io.modelcontextprotocol/protocolVersion":"2026-07-28",
                "io.modelcontextprotocol/clientCapabilities":{} } } }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().NotContain(header => header.Key.Equals("Mcp-Session-Id", StringComparison.OrdinalIgnoreCase));
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("event: message");
        body.Should().Contain("\"supportedVersions\":[\"2026-07-28\"]");
        body.Should().Contain("\"tools\":{}");
    }

    [Fact]
    public async Task PostMcp_ToolsList_DiscoversAttributedTools()
    {
        using var client = _factory.CreateClient();
        using var response = await SendCurrentRequestAsync(client, "tools/list", """
            { "jsonrpc":"2.0", "id":2, "method":"tools/list", "params": { "_meta": {
                "io.modelcontextprotocol/protocolVersion":"2026-07-28",
                "io.modelcontextprotocol/clientCapabilities":{} } } }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"get_system_metrics\"");
        body.Should().Contain("\"get_weather_forecast\"");
        body.Should().Contain("\"calculate_compound_interest\"");
        body.Should().Contain("\"readOnlyHint\":true");
    }

    [Fact]
    public async Task PostMcp_ToolCall_UsesRequiredCurrentRequestHeaders()
    {
        using var client = _factory.CreateClient();
        using var response = await SendCurrentRequestAsync(client, "tools/call", """
            { "jsonrpc":"2.0", "id":3, "method":"tools/call", "params": {
                "name":"get_weather_forecast", "arguments":{"city":"London"}, "_meta": {
                    "io.modelcontextprotocol/protocolVersion":"2026-07-28",
                    "io.modelcontextprotocol/clientCapabilities":{} } } }
            """, "get_weather_forecast");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("London");
        body.Should().Contain("sampleTimestampUtc");
    }

    [Fact]
    public async Task PostMcp_MalformedJson_ReturnsControlledJsonRpcError()
    {
        using var client = _factory.CreateClient();
        using var response = await SendCurrentRequestAsync(client, "server/discover", "{not-json}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"jsonrpc\":\"2.0\"");
        body.Should().Contain("\"error\"");
    }

    [Fact]
    public async Task PostMcp_OversizedPayload_IsRejectedBeforeProtocolProcessing()
    {
        using var client = _factory.CreateClient();
        using var response = await SendCurrentRequestAsync(client, "server/discover", new string('x', (64 * 1024) + 1));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task PostMcp_MissingRequiredCurrentMetadata_ReturnsControlledError()
    {
        using var client = _factory.CreateClient();
        using var response = await SendCurrentRequestAsync(client, "tools/list", """
            { "jsonrpc":"2.0", "id":4, "method":"tools/list", "params": {} }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"error\"");
    }

    [Fact]
    public async Task PostMcp_CrossOriginBrowserRequest_IsRejected()
    {
        using var client = _factory.CreateClient();
        using var request = CreateCurrentRequest("server/discover", """
            { "jsonrpc":"2.0", "id":5, "method":"server/discover", "params": { "_meta": {
                "io.modelcontextprotocol/protocolVersion":"2026-07-28",
                "io.modelcontextprotocol/clientCapabilities":{} } } }
            """);
        request.Headers.TryAddWithoutValidation("Origin", "https://attacker.example");

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LegacySseEndpoints_AreNotMapped()
    {
        using var client = _factory.CreateClient();
        using var sse = await client.GetAsync("/sse");
        using var messages = await client.PostAsync("/messages", new StringContent("{}", Encoding.UTF8, "application/json"));

        sse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        messages.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> SendCurrentRequestAsync(HttpClient client, string method, string body, string? name = null) =>
        client.SendAsync(CreateCurrentRequest(method, body, name));

    private static HttpRequestMessage CreateCurrentRequest(string method, string body, string? name = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", CurrentProtocolVersion);
        request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        if (name is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Name", name);
        }

        return request;
    }
}
