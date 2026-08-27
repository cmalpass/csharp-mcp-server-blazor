import { expect, test } from "@playwright/test";

test("a developer can discover, inspect, and execute C# MCP tools in the Blazor inspector", async ({ page }, testInfo) => {
  await page.goto("/");

  await expect(page.getByRole("heading", { name: "C# MCP Server & Blazor Inspector" })).toBeVisible();

  // Wait for WebAssembly runtime hydration
  await page.waitForTimeout(750);

  // Assert tool discovery
  const toolButton = page.locator(".list-group-item").filter({ hasText: "get_system_metrics" }).first();
  await expect(toolButton).toBeVisible();
  await toolButton.click();

  // Execute the tool
  const executeButton = page.getByRole("button", { name: "▶ Execute Tool via Local Inspector API" });
  await expect(executeButton).toBeVisible();
  await executeButton.click();

  // Verify successful tool execution output
  const resultHeader = page.getByText("Execution Result");
  await expect(resultHeader).toBeVisible();
  await expect(page.locator("pre code").first()).toContainText("os");

  // Keep generated evidence in Playwright's ignored per-test output directory.
  const screenshotPath = testInfo.outputPath("mcp-inspector-execution.png");
  await page.screenshot({ path: screenshotPath, fullPage: true });

  await testInfo.attach("mcp-tool-execution", {
    path: screenshotPath,
    contentType: "image/png"
  });
});
