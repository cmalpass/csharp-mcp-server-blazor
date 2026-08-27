# C# MCP Server & Blazor WebAssembly Inspector

A companion code repository for the blog post: **[Building a C# MCP Server with Blazor Frontend](https://chrismalpass.com/posts/building-a-csharp-mcp-server-with-blazor-frontend/)**.

This repository provides a production-grade implementation of the **Model Context Protocol (MCP)** in C# on .NET 10, paired with an interactive **Blazor WebAssembly Tool Inspector & Protocol Monitor**.

---

## 🚀 Architecture Highlights

- **Standard MCP Protocol Engine**: Implements the official Model Context Protocol over both transports — legacy HTTP+SSE (`GET /sse` + `POST /messages`, MCP `2024-11-05`, deprecated by SEP-2596) and **Streamable HTTP** (`POST /mcp`, MCP `2025-06-18`, the current transport) — with protocol version negotiation on `initialize`, supporting external clients like **Claude Desktop** (via the `mcp-remote` bridge), **Cursor**, and the official **MCP Inspector**.
- **Native C# Tool Reflection**: Exposes strongly-typed C# methods as discoverable MCP tools using `Microsoft.Extensions.AI` (`AIFunctionFactory`) and `System.Text.Json` automated schema generation.
- **Interactive Blazor WASM Inspector**: A developer GUI to inspect generated JSON schemas, test tool execution with custom parameter forms, and monitor real-time JSON-RPC wire logs.
- **Zero-Config Local Dev vs Guarded Production**: Local development runs friction-free without secrets; non-Development environments enforce JWT Bearer authentication, rate limiting, and request timeouts.

---

## 📁 Solution Structure

- `McpServerApp/McpServerApp`: The ASP.NET Core server hosting the `/sse`, `/messages`, `/mcp`, and `/api/mcp` endpoints.
- `McpServerApp/McpServerApp.Client`: The Blazor WebAssembly client providing the interactive Inspector UI.
- `McpServerApp/McpServerApp.Tests`: Full automated test suite (Unit, Integration via `WebApplicationFactory`, and bUnit UI Component tests).
- `e2e/`: Playwright browser test validating tool discovery, execution, and screenshot capture.

---

## 🛠️ Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20+](https://nodejs.org/) (for Playwright browser testing); Node.js 22+ recommended for the official MCP Inspector

### Running Locally
1. Clone the repository:
   ```bash
   git clone https://github.com/cmalpass/csharp-mcp-server-blazor.git
   cd csharp-mcp-server-blazor/McpServerApp
   ```

2. Run the application:
   ```bash
   dotnet run --project McpServerApp
   ```

3. Open your browser and navigate to `http://localhost:5000` (or the URL displayed in your terminal) to explore the **MCP Inspector**. The MCP protocol endpoints (`/sse`, `/messages`) and the inspector API (`/api/mcp/*`) are served from the same host.

---

## 🔌 Connecting External AI Clients

This server exposes two endpoints: **Streamable HTTP** at `http://localhost:5000/mcp` (the current MCP transport) and **legacy SSE** at `http://localhost:5000/sse` (deprecated by [SEP-2596](https://modelcontextprotocol.io/specification/2025-03-26/basic/transports) but still supported for compatibility).

### 1. Claude Desktop (via the mcp-remote bridge)
Claude Desktop's `claude_desktop_config.json` only supports **stdio** servers — there is no `url` key. For a local dev server, use the [`mcp-remote`](https://www.npmjs.com/package/mcp-remote) stdio bridge (requires Node.js 22+):
```json
{
  "mcpServers": {
    "csharp-blazor-server": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://localhost:5000/mcp"]
    }
  }
}
```
For a publicly hosted (HTTPS) server, use **Customize → Connectors → Add custom connector** in Claude instead of the config file.

### 2. Cursor
Add a remote MCP server in **Settings → MCP**, or in `.cursor/mcp.json` (project) / `~/.cursor/mcp.json` (user):
```json
{
  "mcpServers": {
    "csharp-tools": {
      "url": "http://localhost:5000/mcp"
    }
  }
}
```
Cursor supports stdio, SSE, and Streamable HTTP; the legacy SSE URL `http://localhost:5000/sse` works as well.

### 3. Official MCP Inspector
Run the Inspector's web UI and connect using the transport selector:
```bash
npx @modelcontextprotocol/inspector
```
It prints a URL on `localhost:6274` (with a session token) — open it in your browser, then choose **Streamable HTTP** (paste `http://localhost:5000/mcp`) or **SSE** (paste `http://localhost:5000/sse`), and connect. The UI can also export ready-to-paste `mcp.json` snippets.

---

## 🧪 Testing

### Deterministic Test Suite (.NET)
Run the unit, integration, and bUnit component tests:
```bash
dotnet test McpServerApp/McpServerApp.sln --configuration Release
```

### Browser E2E Test (Playwright)
```bash
npm ci
npm run test:e2e
```
This runs the full browser flow and generates screenshot evidence under `docs/evidence/mcp-inspector-execution.png`.

---

## 📜 License

This project is licensed under the [MIT License](LICENSE).
