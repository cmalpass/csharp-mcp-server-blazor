using McpServerApp.Client.Pages;
using McpServerApp.Components;
using McpServerApp.Contracts;
using McpServerApp.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Reflection;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<McpServerRegistry>();

// Forwarded headers are honored only when an operator explicitly identifies the
// reverse proxy. Never trust client-supplied X-Forwarded-* headers by default.
var trustedProxyAddress = builder.Configuration["Mcp:TrustedProxyAddress"];
var useTrustedProxy = IPAddress.TryParse(trustedProxyAddress, out var trustedProxyIp);
if (useTrustedProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(trustedProxyIp!);
    });
}

// The official SDK owns protocol negotiation, JSON-RPC validation, error responses,
// and transport lifecycle. Stateless mode is the forward-compatible HTTP choice.
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly(Assembly.GetExecutingAssembly());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("mcp", context =>
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
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
builder.Services.AddRequestTimeouts(options => options.AddPolicy("mcp", TimeSpan.FromSeconds(60)));

var app = builder.Build();

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
               !context.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

if (useTrustedProxy)
{
    app.UseForwardedHeaders();
}

// Local development intentionally supports the HTTP profile used by the README.
// Production deployments should terminate TLS and redirect HTTP to HTTPS.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp") && !HasAllowedOrigin(context))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    if (context.Request.Path.StartsWithSegments("/mcp") && context.Request.ContentLength is > 64 * 1024)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    await next(context);
});
app.UseRateLimiter();
app.UseRequestTimeouts();
app.MapStaticAssets();

// Current MCP Streamable HTTP endpoint. Legacy /sse and /messages endpoints are
// intentionally not mapped; the SDK's default disables the obsolete SSE transport.
app.MapMcp("/mcp")
    .RequireRateLimiting("mcp")
    .WithRequestTimeout("mcp")
    .WithMetadata(new RequestSizeLimitAttribute(64 * 1024));

// The browser inspector is local demo UI, not an authentication example. It reuses
// the same tool implementations through its existing registry facade.
var mcpApiGroup = app.MapGroup("/api/mcp");
mcpApiGroup.RequireRateLimiting("mcp").WithRequestTimeout("mcp");
mcpApiGroup.MapGet("/tools", (McpServerRegistry registry) => Results.Ok(registry.GetToolDefinitions()));
mcpApiGroup.MapPost("/call", async (McpCallRequest request, McpServerRegistry registry, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Tool name is required."] });
    }

    return Results.Ok(await registry.CallToolAsync(request.Name, request.Arguments, cancellationToken));
});
mcpApiGroup.MapGet("/logs", (McpServerRegistry registry) => Results.Ok(registry.GetRecentLogs()));

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(McpServerApp.Client._Imports).Assembly);

app.Run();

static bool HasAllowedOrigin(HttpContext context)
{
    if (!context.Request.Headers.TryGetValue("Origin", out var originValues) || string.IsNullOrWhiteSpace(originValues))
    {
        // Native MCP clients normally omit Origin. Browser callers must be same-origin.
        return true;
    }

    if (!Uri.TryCreate(originValues.ToString(), UriKind.Absolute, out var origin) ||
        !string.Equals(origin.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(origin.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    // Uri omits default ports in an Origin value, while Host may include one when
    // a reverse proxy forwards an explicit :80 or :443. Compare effective ports.
    var requestPort = context.Request.Host.Port ?? (context.Request.IsHttps ? 443 : 80);
    var originPort = origin.IsDefaultPort
        ? (string.Equals(origin.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
        : origin.Port;
    return requestPort == originPort;
}

public partial class Program { }
