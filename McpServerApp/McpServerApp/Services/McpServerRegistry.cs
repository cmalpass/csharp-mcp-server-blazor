using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using McpServerApp.Contracts;
using Microsoft.Extensions.AI;

namespace McpServerApp.Services;

public class McpServerRegistry
{
    private readonly Dictionary<string, AIFunction> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<McpLogEntry> _logs = new();
    private const int MaxLogEntries = 100;

    public McpServerRegistry()
    {
        RegisterTool(AIFunctionFactory.Create(SampleMcpTools.GetSystemMetrics, "get_system_metrics", "Get server system metrics including OS description, processor count, and memory status."));
        RegisterTool(AIFunctionFactory.Create(SampleMcpTools.QueryCustomers, "query_customers", "Query the simulated enterprise database for customer records by region or account tier."));
        RegisterTool(AIFunctionFactory.Create(SampleMcpTools.CalculateCompoundInterest, "calculate_compound_interest", "Calculate compound growth or loan amortization for financial modeling."));
        RegisterTool(AIFunctionFactory.Create(SampleMcpTools.GetWeatherForecast, "get_weather_forecast", "Fetch real-time weather and forecast data for a specified city."));
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

    public async Task<JsonElement> HandleJsonRpcRequestAsync(string jsonRpcPayload, CancellationToken cancellationToken = default)
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
                    protocolVersion = "2024-11-05",
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
}
