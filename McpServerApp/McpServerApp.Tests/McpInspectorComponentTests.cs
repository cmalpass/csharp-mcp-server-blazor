using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using McpServerApp.Client.Pages;
using McpServerApp.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace McpServerApp.Tests;

public class McpInspectorComponentTests : BunitContext
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public void McpInspector_InitialRender_RendersDiscoveredToolsAndTabs()
    {
        // Arrange
        using var emptySchema = JsonDocument.Parse("{}");
        var sampleTools = new List<McpToolInfo>
        {
            new() { Name = "get_system_metrics", Description = "System metrics", InputSchema = emptySchema.RootElement.Clone() },
            new() { Name = "query_customers", Description = "Query customers", InputSchema = emptySchema.RootElement.Clone() }
        };

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri?.AbsolutePath.Contains("/api/mcp/tools") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(sampleTools), Encoding.UTF8, "application/json")
                };
            }
            if (req.RequestUri?.AbsolutePath.Contains("/api/mcp/logs") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton(httpClient);

        // Act
        var cut = Render<McpInspector>();

        // Assert
        cut.Find("h1").TextContent.Should().Contain("C# MCP Server & Blazor Inspector");
        cut.WaitForAssertion(() => cut.FindAll(".list-group-item").Count.Should().Be(2), TimeSpan.FromSeconds(5));
        var buttons = cut.FindAll(".list-group-item");
        buttons[0].TextContent.Should().Contain("get_system_metrics");
        buttons[1].TextContent.Should().Contain("query_customers");
    }

    [Fact]
    public async Task McpInspector_TabSwitching_DisplaysConfigGuide()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton(httpClient);

        var cut = Render<McpInspector>();

        // Act - Click the AI Client Setup Guide tab
        var tabs = cut.FindAll(".nav-link");
        var configTab = tabs.First(t => t.TextContent.Contains("AI Client Setup Guide"));
        await cut.InvokeAsync(() => configTab.Click());

        // Assert
        cut.Find("h5").TextContent.Should().Contain("Connecting External AI Clients");
        cut.Markup.Should().Contain("claude_desktop_config.json");
    }
}
