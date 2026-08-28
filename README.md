# C# MCP Server & Blazor WebAssembly Inspector

A companion repository for the blog post: **[Building a C# MCP Server with a Blazor WebAssembly Inspector](https://chrismalpass.net/posts/building-a-csharp-mcp-server-with-blazor-frontend/)**.

This is a local, pedagogical MCP server built on .NET 10. It uses the official MCP C# SDK **v2.2.0** and its stateless Streamable HTTP transport at `POST /mcp`, alongside a Blazor WebAssembly page for inspecting the sample tools. Customer and weather responses are simulated fixtures; the system-metrics tool intentionally reports live data from the local process. No tool queries an external system.

> [!IMPORTANT]
> The Blazor page is a diagnostic UI, not an MCP client: its buttons use the application-specific `/api/mcp/*` API to inspect and invoke the sample tools. The actual MCP transport is `POST /mcp`.

## Protocol and transport

The server uses the current `2026-07-28` MCP HTTP wire format through MCP C# SDK v2.2.0. It is stateless: each `POST /mcp` request is independent, and the server does not issue or retain an `Mcp-Session-Id`. The current revision uses `server/discover` for bootstrap rather than the historical `initialize` handshake, and HTTP requests carry the protocol version in the `MCP-Protocol-Version` header.

Streamable HTTP is the recommended HTTP transport for new MCP servers. See the official [MCP C# SDK transport guidance](https://csharp.sdk.modelcontextprotocol.io/concepts/transports/transports.html) and [stateless HTTP guidance](https://csharp.sdk.modelcontextprotocol.io/concepts/stateless/stateless.html) for the protocol behaviour and production deployment options.

## Solution structure

- `McpServerApp/McpServerApp`: ASP.NET Core host for `POST /mcp` and the diagnostic inspector API.
- `McpServerApp/McpServerApp.Client`: Blazor WebAssembly diagnostic inspector UI.
- `McpServerApp/McpServerApp.Tests`: unit, integration, and bUnit component tests.
- `e2e/`: Playwright browser test for local diagnostic inspection and invocation.

## Run locally

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Install [Node.js](https://nodejs.org/) only when running the browser test.

```bash
git clone https://github.com/cmalpass/csharp-mcp-server-blazor.git
cd csharp-mcp-server-blazor
dotnet run --project McpServerApp/McpServerApp
```

Open the local address printed by ASP.NET Core (normally `http://localhost:5000`) to explore the sample tools and local diagnostic log. For a client integration, point a current MCP client at the local `POST /mcp` endpoint and follow that client’s official, up-to-date configuration documentation.

## Production considerations

This repository teaches the protocol and tool-inspection flow; it is not a complete production deployment template. Before exposing an MCP server, design authentication and authorization for the tool surface, validate allowed origins and host names, apply request/concurrency/rate limits, avoid logging secrets or raw untrusted payloads, and run behind HTTPS and an appropriate reverse proxy. The official C# SDK documentation is the source of truth for current transport and deployment behaviour.

If a reverse proxy terminates TLS, configure it to overwrite the forwarded headers and set `Mcp:TrustedProxyAddress` to that proxy's exact IP address (for example, `Mcp__TrustedProxyAddress=10.0.0.10` as an environment variable). The application ignores forwarded headers unless this exact proxy is configured; never enable trust for arbitrary client-supplied `X-Forwarded-*` headers.

## Testing

Run the .NET test suite:

```bash
dotnet test --solution McpServerApp/McpServerApp.sln --configuration Release
```

Run the local browser test:

```bash
npm ci
npm run test:e2e
```

Playwright keeps screenshots, traces, and its HTML report in ignored test-output directories (`test-results/` and `playwright-report/`). Running the test never updates tracked documentation images.

## License

This project is licensed under the [MIT License](LICENSE).
