using System.Diagnostics;
using System.Text;
using Platform.Agent.Providers.Internals;

namespace Platform.Agent.Tests;

public sealed class ClaudeAndCodexCliProviderTests
{
  [Fact]
  public async Task ClaudeCliAgentProvider_UsesRestrictedModeAndDynamicPlatformMcpAllowedTools()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), $"air-claude-cfg-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    try
    {
      var mcpConfigPath = await CliEnvironmentHelper.WriteMcpConfigAsync(
        tempDir, "http://127.0.0.1:5191/api/mcp", "tok-claude-1", new UTF8Encoding(false), CancellationToken.None);
      Assert.NotNull(mcpConfigPath);

      var psi = new ProcessStartInfo { WorkingDirectory = tempDir };
      ClaudeCliAgentProvider.ConfigureTurnStartInfo(
        psi,
        "Show chart",
        "opus",
        "high",
        null,
        mcpConfigPath,
        tempDir,
        ["platform_records_query", "custom_analytics_tool"]);

      Assert.Contains("--restricted", psi.ArgumentList);
      Assert.DoesNotContain("--safe-mode", psi.ArgumentList);
      Assert.Contains("--strict-mcp-config", psi.ArgumentList);
      Assert.Contains("--mcp-config", psi.ArgumentList);
      Assert.Contains(mcpConfigPath, psi.ArgumentList);

      var allowedIdx = psi.ArgumentList.IndexOf("--allowedTools");
      Assert.True(allowedIdx >= 0 && allowedIdx + 1 < psi.ArgumentList.Count);
      Assert.Equal("Read,mcp__platform__platform_records_query,mcp__platform__custom_analytics_tool", psi.ArgumentList[allowedIdx + 1]);

      // Default fallback includes the four built-in platform MCP tools when no explicit list is passed.
      var defaultToolsPsi = new ProcessStartInfo { WorkingDirectory = tempDir };
      ClaudeCliAgentProvider.ConfigureTurnStartInfo(defaultToolsPsi, "Show chart", "opus", "high", null, mcpConfigPath, tempDir);
      var defaultAllowedIdx = defaultToolsPsi.ArgumentList.IndexOf("--allowedTools");
      Assert.Equal(
        "Read,mcp__platform__platform_catalog_list,mcp__platform__platform_catalog_describe,mcp__platform__platform_records_query,mcp__platform__platform_proposal_stage",
        defaultToolsPsi.ArgumentList[defaultAllowedIdx + 1]);

      // Without an MCP config, only Read is allowed and --mcp-config is omitted.
      var noMcpPsi = new ProcessStartInfo { WorkingDirectory = tempDir };
      ClaudeCliAgentProvider.ConfigureTurnStartInfo(noMcpPsi, "Hi", "opus", "high", null, null, tempDir);
      Assert.DoesNotContain("--mcp-config", noMcpPsi.ArgumentList);
      var noMcpAllowedIdx = noMcpPsi.ArgumentList.IndexOf("--allowedTools");
      Assert.Equal("Read", noMcpPsi.ArgumentList[noMcpAllowedIdx + 1]);
    }
    finally
    {
      try
      {
        Directory.Delete(tempDir, recursive: true);
      }
      catch { }
    }
  }

  [Fact]
  public async Task CodexCliAgentProvider_SetsPlatformMcpAutoApprovalOnFreshAndResumeTurns()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), $"air-codex-cfg-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    try
    {
      var mcpConfigPath = await CliEnvironmentHelper.WriteMcpConfigAsync(
        tempDir, "http://127.0.0.1:5191/api/mcp", "tok-codex-1", new UTF8Encoding(false), CancellationToken.None);
      Assert.NotNull(mcpConfigPath);

      var freshPsi = new ProcessStartInfo { WorkingDirectory = tempDir };
      CodexCliAgentProvider.ConfigureTurnStartInfo(freshPsi, "Hello", "gpt-5", "high", null, "", [], mcpConfigPath);
      Assert.Contains("mcp_servers.platform.default_tools_approval_mode=\"approve\"", freshPsi.ArgumentList);

      var resumePsi = new ProcessStartInfo { WorkingDirectory = tempDir };
      CodexCliAgentProvider.ConfigureTurnStartInfo(resumePsi, "Follow-up", "gpt-5", "high", "thread-1", "", [], mcpConfigPath);
      Assert.Contains("mcp_servers.platform.default_tools_approval_mode=\"approve\"", resumePsi.ArgumentList);
    }
    finally
    {
      try
      {
        Directory.Delete(tempDir, recursive: true);
      }
      catch { }
    }
  }
}
