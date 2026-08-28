using System.Collections.Concurrent;
using System.Text.Json;
using McpServerApp.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace McpServerApp.Services;

public class McpServerRegistry
{
    private readonly Dictionary<string, AIFunction> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<McpLogEntry> _logs = new();
    private readonly bool _capturePayloads;
    private readonly ILogger<McpServerRegistry>? _logger;
    private const int MaxLogEntries = 100;

    public McpServerRegistry(IWebHostEnvironment? environment = null, ILogger<McpServerRegistry>? logger = null)
    {
        _capturePayloads = environment?.IsDevelopment() == true;
        _logger = logger;
        RegisterTool(AIFunctionFactory.Create(SampleMcpTools.GetSystemMetrics, "get_system_metrics", "Get server process metrics: OS and framework description, processor count, process working-set bytes, uptime, and UTC time."));
        RegisterTool(AIFunctionFactory.Create(SampleMcpTools.QueryCustomers, "query_customers", "Query fixed, simulated customer fixture records by region or account tier. totalFound is the number of records matching the filters before the returned page is limited."));
        RegisterTool(AIFunctionFactory.Create(SampleMcpTools.CalculateCompoundInterest, "calculate_compound_interest", "Calculate compound investment growth using decimal arithmetic for a teaching example; this is not financial advice."));
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
            LogEvent("inbound", $"diagnostic/tools/call:{name}", JsonSerializer.Serialize(arguments ?? new Dictionary<string, object?>()));
            var invokeResult = await tool.InvokeAsync(args, cancellationToken);

            var text = invokeResult?.ToString() ?? "null";
            return new McpCallResponse
            {
                IsError = false,
                Content = new List<McpContentItem> { new() { Text = text } }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Diagnostic tool {ToolName} failed.", name);
            return new McpCallResponse
            {
                IsError = true,
                // Tool exceptions can contain connection strings, file paths, or other
                // implementation details. Keep protocol responses safe for clients.
                Content = new List<McpContentItem> { new() { Text = $"Tool '{name}' could not be completed." } }
            };
        }
    }

    public void LogEvent(string direction, string method, string payload)
    {
        _logs.Enqueue(new McpLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Direction = direction,
            Method = method,
            Payload = _capturePayloads ? payload : "[payload redacted outside Development]"
        });

        while (_logs.Count > MaxLogEntries)
        {
            _logs.TryDequeue(out _);
        }
    }

    public IReadOnlyList<McpLogEntry> GetRecentLogs() => _logs.Reverse().ToList();

}
