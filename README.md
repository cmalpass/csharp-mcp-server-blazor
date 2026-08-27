# C# MCP Server & Blazor WebAssembly Inspector

A companion code repository for the blog post: **[Building a C# MCP Server with Blazor Frontend](https://chrismalpass.com/posts/building-a-csharp-mcp-server-with-blazor-frontend/)**.

This repository provides a production-grade implementation of the **Model Context Protocol (MCP)** in C# on .NET 9, paired with an interactive **Blazor WebAssembly Tool Inspector & Protocol Monitor**.

---

## 🚀 Architecture Highlights

- **Standard MCP Protocol Engine**: Implements the official Model Context Protocol over Server-Sent Events (`GET /sse`) and JSON-RPC 2.0 (`POST /messages`), supporting external AI clients like **Claude Desktop**, **Cursor**, **VS Code**, and the **Anthropic MCP Inspector CLI**.
- **Native C# Tool Reflection**: Exposes strongly-typed C# methods as discoverable MCP tools using `Microsoft.Extensions.AI` (`AIFunctionFactory`) and `System.Text.Json` automated schema generation.
- **Interactive Blazor WASM Inspector**: A developer GUI to inspect generated JSON schemas, test tool execution with custom parameter forms, and monitor real-time JSON-RPC wire logs.
- **Zero-Config Local Dev vs Guarded Production**: Local development runs friction-free without secrets; non-Development environments enforce JWT Bearer authentication, rate limiting, and request timeouts.

---

## 📁 Solution Structure

- `McpServerApp/McpServerApp`: The ASP.NET Core server hosting the `/sse`, `/messages`, and `/api/mcp` endpoints.
- `McpServerApp/McpServerApp.Client`: The Blazor WebAssembly client providing the interactive Inspector UI.
- `McpServerApp/McpServerApp.Tests`: Full automated test suite (Unit, Integration via `WebApplicationFactory`, and bUnit UI Component tests).
- `e2e/`: Playwright browser test validating tool discovery, execution, and screenshot capture.

---

## 🛠️ Getting Started

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (or .NET 10 preview)
- [Node.js 20+](https://nodejs.org/) (for Playwright browser testing)

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

3. Open your browser and navigate to `https://localhost:7150` (or the port displayed in your terminal) to explore the **MCP Inspector**.

---

## 🔌 Connecting External AI Clients

### 1. Claude Desktop
Add this entry to your `claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "csharp-blazor-server": {
      "url": "http://localhost:5000/sse"
    }
  }
}
```

### 2. Cursor
Go to **Settings > Features > MCP**, click **+ Add New MCP Server**, select **SSE**, and provide `http://localhost:5000/sse`.

### 3. Anthropic CLI Inspector
```bash
npx @modelcontextprotocol/inspector http://localhost:5000/sse
```

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
