using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using McpServerApp.Contracts;
using Microsoft.Extensions.AI;

namespace McpServerApp.Services;

public class McpServerRegistry
{
    /// <summary>Protocol versions this server can negotiate. See the MCP specification docs/specification/.</summary>
    public static readonly string[] SupportedProtocolVersions =
    [
        "2024-11-05", // legacy SSE transport
        "2025-03-26", // Streamable HTTP (introduced)
        "2025-06-18"  // Streamable HTTP (batches removed, MCP-Protocol-Version header)
    ];

    private readonly Dictionary<string, AIFunction> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<McpLogEntry> _logs = new();
    private readonly ConcurrentDictionary<string, SseSession> _sseSessions = new();
    private const int MaxLogEntries = 100;

    public McpServerRegistry()
    {
        RegisterTool(AIFunctionFactory.Create(SampleMcpTools.GetSystemMetrics, "get_system_metrics", "Get server system metrics including OS description, processor count, and memory status."));
        RegisterTool(AIFunctionFactory.Create(SampleMcpTools.QueryCustomers, "query_customers", "Query the simulated enterprise database for customer records by region or account tier."));
        RegisterTool(AIFunctionFactory.Create(SampleMcpTools.CalculateCompoundInterest, "calculate_compound_interest", "Calculate compound growth or loan amortization for financial modeling."));
        RegisterTool(AIFunctionFactory.Create(SampleMcpTools.GetWeatherForecast, "get_weather_forecast", "Get simulated weather and forecast data for a specified city (deterministic sample data, not live meteorological data)."));
    }

    public void RegisterTool(AIFunction function)
    {
        _tools[function.Name] = function;
    }

    public IReadOnlyList<McpToolInfo> GetToolDefinitions()
    {
        var result = new List<McpToolInfo>();

        foreach (var (name, func) in _tools)
        {
            var schemaElement = func.JsonSchema;
            result.Add(new McpToolInfo
            {
                Name = name,
                Description = func.Description ?? "",
                InputSchema = schemaElement
            });
        }

        return result;
    }

    public async Task<McpCallResponse> CallToolAsync(string name, IDictionary<string, object?>? arguments, CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(name, out var tool))
        {
            return new McpCallResponse
            {
                IsError = true,
                Content = new List<McpContentItem> { new() { Text = $"Tool '{name}' not found." } }
            };
        }

        try
        {
            var args = arguments != null ? new AIFunctionArguments(arguments) : new AIFunctionArguments();
            var invokeResult = await tool.InvokeAsync(args, cancellationToken);
            
            var text = invokeResult?.ToString() ?? "null";
            return new McpCallResponse
            {
                IsError = false,
                Content = new List<McpContentItem> { new() { Text = text } }
            };
        }
        catch (Exception ex)
        {
            return new McpCallResponse
            {
                IsError = true,
                Content = new List<McpContentItem> { new() { Text = $"Error executing tool '{name}': {ex.Message}" } }
            };
        }
    }

    public async Task<JsonElement> HandleJsonRpcRequestAsync(string jsonRpcPayload, string fallbackProtocolVersion, CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(jsonRpcPayload);
        var root = doc.RootElement;

        var method = root.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
        var id = root.TryGetProperty("id", out var idProp) ? idProp.Clone() : default;

        LogEvent("inbound", method, jsonRpcPayload);

        object responseBody = method switch
        {
            "initialize" => new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    protocolVersion = NegotiateProtocolVersion(root, fallbackProtocolVersion),
                    serverInfo = new { name = "CsharpMcpServer", version = "1.0.0" },
                    capabilities = new
                    {
                        tools = new { listChanged = false },
                        resources = new { },
                        prompts = new { }
                    }
                }
            },
            "notifications/initialized" => null!, // Notification, no response
            "ping" => new
            {
                jsonrpc = "2.0",
                id = id,
                result = new { }
            },
            "tools/list" => new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    tools = GetToolDefinitions()
                }
            },
            "tools/call" => await ExecuteJsonRpcToolCallAsync(root, id, cancellationToken),
            _ => new
            {
                jsonrpc = "2.0",
                id = id,
                error = new { code = -32601, message = $"Method '{method}' not found." }
            }
        };

        if (responseBody == null)
        {
            return default;
        }

        var responseJson = JsonSerializer.Serialize(responseBody, new JsonSerializerOptions { WriteIndented = false });
        LogEvent("outbound", method, responseJson);

        using var responseDoc = JsonDocument.Parse(responseJson);
        return responseDoc.RootElement.Clone();
    }

    /// <summary>
    /// Implements the MCP initialize version negotiation: respond with the client's
    /// proposed version when supported, otherwise with the transport's default version.
    /// </summary>
    private static string NegotiateProtocolVersion(JsonElement root, string fallbackProtocolVersion)
    {
        if (root.TryGetProperty("params", out var paramsElem) &&
            paramsElem.TryGetProperty("protocolVersion", out var proposed) &&
            proposed.ValueKind == JsonValueKind.String)
        {
            var proposedVersion = proposed.GetString();
            if (proposedVersion is not null && SupportedProtocolVersions.Contains(proposedVersion))
            {
                return proposedVersion;
            }
        }

        return fallbackProtocolVersion;
    }

    private async Task<object> ExecuteJsonRpcToolCallAsync(JsonElement root, JsonElement id, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("params", out var paramsElem))
        {
            return new
            {
                jsonrpc = "2.0",
                id = id,
                error = new { code = -32602, message = "Missing params for tools/call." }
            };
        }

        var toolName = paramsElem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var argsDict = new Dictionary<string, object?>();

        if (paramsElem.TryGetProperty("arguments", out var argsElem) && argsElem.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in argsElem.EnumerateObject())
            {
                argsDict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? i : prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.GetRawText()
                };
            }
        }

        var callResult = await CallToolAsync(toolName, argsDict, cancellationToken);

        return new
        {
            jsonrpc = "2.0",
            id = id,
            result = callResult
        };
    }

    public void LogEvent(string direction, string method, string payload)
    {
        _logs.Enqueue(new McpLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Direction = direction,
            Method = method,
            Payload = payload
        });

        while (_logs.Count > MaxLogEntries)
        {
            _logs.TryDequeue(out _);
        }
    }

    public IReadOnlyList<McpLogEntry> GetRecentLogs() => _logs.Reverse().ToList();

    // --- Legacy SSE transport session management ---
    // Each open GET /sse connection registers an SseSession. POST /messages lookups the
    // session and pushes JSON-RPC response frames onto its channel, which the /sse handler
    // drains as SSE `message` events, per the 2024-11-05 transport spec.

    public SseSession CreateSseSession(string id)
    {
        var session = new SseSession(id);
        _sseSessions[id] = session;
        return session;
    }

    public SseSession? FindSseSession(string id) =>
        _sseSessions.TryGetValue(id, out var session) ? session : null;

    public void RemoveSseSession(string id)
    {
        if (_sseSessions.TryRemove(id, out var session))
        {
            session.Frames.Writer.TryComplete();
        }
    }
}

/// <summary>
/// A single connected SSE client: the channel through which JSON-RPC response frames
/// (already formatted as SSE `message` events) are delivered to its /sse stream.
/// </summary>
public sealed class SseSession
{
    public SseSession(string id)
    {
        Id = id;
        Frames = Channel.CreateUnbounded<string>();
    }

    public string Id { get; }

    public Channel<string> Frames { get; }
}
