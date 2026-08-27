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
    public async Task PostMessages_Initialize_ResponseDeliveredAsSseMessageEvent()
    {
        // Arrange
        var client = _factory.CreateClient();
        await using var connection = await ConnectSseAsync(client);
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

        // Act
        var response = await client.PostAsync(
            $"/messages?sessionId={connection.SessionId}",
            new StringContent(JsonSerializer.Serialize(initPayload), Encoding.UTF8, "application/json"));

        // Assert: POST is acknowledged with 202, response arrives on the SSE stream
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var data = await ReadMessageEventDataAsync(connection);
        using var doc = JsonDocument.Parse(data);

        doc.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString().Should().Be("2024-11-05");
        doc.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("CsharpMcpServer");
    }

    [Fact]
    public async Task PostMessages_ToolsList_ResponseDeliveredAsSseMessageEvent()
    {
        // Arrange
        var client = _factory.CreateClient();
        await using var connection = await ConnectSseAsync(client);
        var listPayload = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list",
            @params = new { }
        };

        // Act
        var response = await client.PostAsync(
            $"/messages?sessionId={connection.SessionId}",
            new StringContent(JsonSerializer.Serialize(listPayload), Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var data = await ReadMessageEventDataAsync(connection);
        using var doc = JsonDocument.Parse(data);

        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PostMessages_ToolsCall_ResponseDeliveredAsSseMessageEvent()
    {
        // Arrange
        var client = _factory.CreateClient();
        await using var connection = await ConnectSseAsync(client);
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

        // Act
        var response = await client.PostAsync(
            $"/messages?sessionId={connection.SessionId}",
            new StringContent(JsonSerializer.Serialize(callPayload), Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var data = await ReadMessageEventDataAsync(connection);
        using var doc = JsonDocument.Parse(data);

        var result = doc.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeFalse();
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        text.Should().Contain("os");
    }

    [Fact]
    public async Task PostMessages_UnknownSession_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();
        var initPayload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = "2024-11-05" }
        };

        // Act
        var response = await client.PostAsync(
            "/messages?sessionId=does-not-exist",
            new StringContent(JsonSerializer.Serialize(initPayload), Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostMessages_Notification_ReturnsAcceptedWithoutStreamEvent()
    {
        // Arrange
        var client = _factory.CreateClient();
        await using var connection = await ConnectSseAsync(client);
        var notification = new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        };

        // Act
        var response = await client.PostAsync(
            $"/messages?sessionId={connection.SessionId}",
            new StringContent(JsonSerializer.Serialize(notification), Encoding.UTF8, "application/json"));

        // Assert: 202 acknowledgment, but JSON-RPC forbids a response to a notification,
        // so no message event is pushed to the stream.
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var read = () => connection.Reader.ReadFrameAsync(CancellationToken.None, 3);
        await read.Should().ThrowAsync<TimeoutException>();
    }

    /// <summary>
    /// Opens GET /sse, waits for the endpoint event, and returns the live stream
    /// plus the sessionId from the endpoint URI.
    /// </summary>
    private async Task<SseConnection> ConnectSseAsync(HttpClient client)
    {
        var response = await client.GetAsync("/sse", HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var stream = await response.Content.ReadAsStreamAsync();
        var reader = new SseFrameReader(stream);
        var endpointFrame = await reader.ReadFrameAsync(CancellationToken.None, 10);
        endpointFrame.Should().Contain("event: endpoint");

        var dataLine = endpointFrame.Split('\n').First(l => l.StartsWith("data: "));
        var sessionId = dataLine["data: ".Length..].Trim().Split('=', 2)[1];
        return new SseConnection(response, reader, sessionId);
    }

    /// <summary>
    /// Reads the next SSE frame and returns the JSON payload of its `data:` line,
    /// asserting the frame is a `message` event.
    /// </summary>
    private static async Task<string> ReadMessageEventDataAsync(SseConnection connection)
    {
        var frame = await connection.Reader.ReadFrameAsync(CancellationToken.None, 10);
        frame.Should().StartWith("event: message");
        var dataLine = frame.Split('\n').First(l => l.StartsWith("data: "));
        return dataLine["data: ".Length..].Trim();
    }

    private sealed class SseConnection(HttpResponseMessage response, SseFrameReader reader, string sessionId) : IAsyncDisposable
    {
        public SseFrameReader Reader { get; } = reader;
        public string SessionId { get; } = sessionId;

        public ValueTask DisposeAsync()
        {
            response.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SseFrameReader
    {
        private readonly StreamReader _reader;
        private string _buffer = "";

        public SseFrameReader(Stream stream)
        {
            _reader = new StreamReader(stream);
        }

        /// <summary>
        /// Reads until a complete SSE frame (terminated by a blank line) is assembled.
        /// Throws TimeoutException if no frame arrives within the timeout.
        /// </summary>
        public async Task<string> ReadFrameAsync(CancellationToken cancellationToken, int timeoutSeconds = 10)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                while (_buffer.IndexOf("\n\n") == -1)
                {
                    var buffer = new char[256];
                    var read = await _reader.ReadAsync(new Memory<char>(buffer), cts.Token);
                    if (read == 0)
                    {
                        throw new TimeoutException("SSE stream closed before a complete frame arrived.");
                    }

                    _buffer += new string(buffer, 0, read);
                }

                var end = _buffer.IndexOf("\n\n") + 2;
                var frame = _buffer[..end];
                _buffer = _buffer[end..];
                return frame;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException($"No SSE frame arrived within {timeoutSeconds}s.");
            }
        }
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
