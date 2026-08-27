using System.Text.Json;
using McpServerApp.Client.Pages;
using McpServerApp.Components;
using McpServerApp.Contracts;
using McpServerApp.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<McpServerRegistry>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("mcp", context =>
    {
        var partitionKey = context.User.Identity?.Name
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 30,
            TokensPerPeriod = 30,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            AutoReplenishment = true,
            QueueLimit = 0
        });
    });
});

builder.Services.AddRequestTimeouts(options =>
{
    options.AddPolicy("mcp", TimeSpan.FromSeconds(60));
});

var app = builder.Build();

// Configure HTTP pipeline
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api") && 
               !context.Request.Path.StartsWithSegments("/sse") && 
               !context.Request.Path.StartsWithSegments("/messages") &&
               !context.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseRequestTimeouts();
app.MapStaticAssets();

// --- MCP Standard Protocol Endpoints (for Claude Desktop, Cursor, and Inspector) ---

// The server speaks two transports, as the spec's backwards-compatibility section allows:
//   * Legacy HTTP+SSE  (MCP 2024-11-05, deprecated by SEP-2596): GET /sse + POST /messages
//   * Streamable HTTP  (MCP 2025-03-26 / 2025-06-18, current):  POST /mcp

// 1. GET /sse - Standard MCP Server-Sent Events handshake
//    Sends the `endpoint` event, then streams every JSON-RPC response pushed by
//    POST /messages as SSE `message` events, per the 2024-11-05 transport spec.
app.MapGet("/sse", async (HttpContext context, McpServerRegistry registry, CancellationToken cancellationToken) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    var sessionId = Guid.NewGuid().ToString("n");
    var session = registry.CreateSseSession(sessionId);
    var messageUri = $"/messages?sessionId={sessionId}";

    registry.LogEvent("outbound", "sse/connect", $"Client connected. SessionId: {sessionId}");

    // Send the endpoint event as per MCP SSE Transport Spec
    await context.Response.WriteAsync($"event: endpoint\ndata: {messageUri}\n\n", cancellationToken);
    await context.Response.Body.FlushAsync(cancellationToken);

    // Deliver response frames from POST /messages; ping every 15s to keep the
    // connection alive. Keep a single pending tick: PeriodicTimer throws if
    // WaitForNextTickAsync is called while a previous tick is still pending,
    // so the tick is re-armed only after it fires.
    using var pingTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
    var pingTask = pingTimer.WaitForNextTickAsync(cancellationToken).AsTask();
    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var readTask = session.Frames.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var completed = await Task.WhenAny(readTask, pingTask);

            if (completed == pingTask)
            {
                await context.Response.WriteAsync(": ping\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
                pingTask = pingTimer.WaitForNextTickAsync(cancellationToken).AsTask();
            }

            if (session.Frames.Reader.TryRead(out var frame))
            {
                await context.Response.WriteAsync(frame, cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        }
    }
    catch (OperationCanceledException)
    {
        registry.LogEvent("inbound", "sse/disconnect", $"Client disconnected. SessionId: {sessionId}");
    }
    finally
    {
        registry.RemoveSseSession(sessionId);
    }
});

// 2. POST /messages - Standard MCP JSON-RPC message endpoint
//    The SSE transport spec forbids answering in the POST response: the server
//    acknowledges with 202 Accepted and delivers JSON-RPC responses as SSE
//    `message` events on the client's open /sse stream. Unknown sessions get 404.
app.MapPost("/messages", async (HttpContext context, McpServerRegistry registry, CancellationToken cancellationToken) =>
{
    var sessionId = context.Request.Query.TryGetValue("sessionId", out var sessionIdValues) && sessionIdValues.Count > 0
        ? sessionIdValues[0]
        : null;

    if (string.IsNullOrEmpty(sessionId) || registry.FindSseSession(sessionId) is not { } session)
    {
        return Results.NotFound();
    }

    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync(cancellationToken);

    if (string.IsNullOrWhiteSpace(body))
    {
        return Results.BadRequest(new { error = "Request body cannot be empty." });
    }

    var result = await registry.HandleJsonRpcRequestAsync(body, "2024-11-05", cancellationToken);

    // Notifications (e.g. notifications/initialized) have no JSON-RPC response;
    // only actual responses are pushed to the client's stream.
    if (result.ValueKind != JsonValueKind.Undefined)
    {
        var frame = $"event: message\ndata: {result}\n\n";
        session.Frames.Writer.TryWrite(frame);
    }

    return Results.Accepted();
});

// 3. POST /mcp - MCP Streamable HTTP endpoint (current transport per SEP-2596)
//    Single JSON-RPC message per request; responses are 200 application/json,
//    or 202 Accepted for notifications and other requests with no response.
//    This demo is stateless: no Mcp-Session-Id is issued (the server MAY omit it).
app.MapPost("/mcp", async (HttpContext context, McpServerRegistry registry, CancellationToken cancellationToken) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync(cancellationToken);

    if (string.IsNullOrWhiteSpace(body))
    {
        return Results.BadRequest(new { error = "Request body cannot be empty." });
    }

    var result = await registry.HandleJsonRpcRequestAsync(body, "2025-06-18", cancellationToken);

    if (result.ValueKind == JsonValueKind.Undefined)
    {
        return Results.Accepted();
    }

    return Results.Json(result);
});

// GET /mcp - This server does not open server-initiated SSE streams, so per the
// Streamable HTTP spec it answers GET with 405 Method Not Allowed.
app.MapGet("/mcp", (HttpContext context) =>
{
    context.Response.Headers.Allow = "POST";
    return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
});

// --- Blazor Web UI Inspector Endpoints ---

var mcpApiGroup = app.MapGroup("/api/mcp");
mcpApiGroup.RequireRateLimiting("mcp").WithRequestTimeout("mcp");

if (!app.Environment.IsDevelopment())
{
    mcpApiGroup.RequireAuthorization();
}

mcpApiGroup.MapGet("/tools", (McpServerRegistry registry) =>
{
    return Results.Ok(registry.GetToolDefinitions());
});

mcpApiGroup.MapPost("/call", async (McpCallRequest request, McpServerRegistry registry, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["name"] = ["Tool name is required."]
        });
    }

    var response = await registry.CallToolAsync(request.Name, request.Arguments, cancellationToken);
    return Results.Ok(response);
});

mcpApiGroup.MapGet("/logs", (McpServerRegistry registry) =>
{
    return Results.Ok(registry.GetRecentLogs());
});

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(McpServerApp.Client._Imports).Assembly);

app.Run();

public partial class Program { }
