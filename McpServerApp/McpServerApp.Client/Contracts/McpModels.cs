using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpServerApp.Contracts;

public class McpToolInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; set; }
}

public class McpCallRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public Dictionary<string, object?> Arguments { get; set; } = new();
}

public class McpCallResponse
{
    [JsonPropertyName("isError")]
    public bool IsError { get; set; }

    [JsonPropertyName("content")]
    public List<McpContentItem> Content { get; set; } = new();

    public string GetTextContent() => 
        string.Join("\n", Content.Where(c => c.Type == "text").Select(c => c.Text));
}

public class McpContentItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

public class McpLogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "inbound"; // "inbound" or "outbound"

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = "";
}
