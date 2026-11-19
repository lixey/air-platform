using System.Diagnostics;
using System.Text;
using Platform.Agent.Providers.Internals;

namespace Platform.Agent.Tests;

public sealed class AgyCliAgentProviderTests
{
  [Fact]
  public async Task ConfigureTurnStartInfo_WritesScopedSettingsAndEagerMcpConfig()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), $"air-agy-cfg-{Guid.NewGuid():N}");
    var workDir = Path.Combine(tempDir, "turn-1");
    var attDir = Path.Combine(tempDir, "attachments");
    Directory.CreateDirectory(workDir);
    Directory.CreateDirectory(attDir);
    try
    {
      var utf8 = new UTF8Encoding(false);
      var mcpConfigPath = await CliEnvironmentHelper.WriteMcpConfigAsync(
        tempDir, "http://127.0.0.1:5191/api/mcp", "tok-agy-1", utf8, CancellationToken.None);
      Assert.NotNull(mcpConfigPath);

      var psi = new ProcessStartInfo { WorkingDirectory = workDir };
      AgyCliAgentProvider.ConfigureTurnStartInfo(
        psi,
        "Create 3 orders",
        "gemini-4-hydrogen-xhigh",
        null,
        attDir,
        mcpConfigPath,
        ["platform_catalog_list", "platform_records_query"]);

      var settingsFlagIdx = psi.ArgumentList.IndexOf("--settings");
      Assert.True(settingsFlagIdx >= 0 && settingsFlagIdx + 1 < psi.ArgumentList.Count);
      var settingsPath = psi.ArgumentList[settingsFlagIdx + 1];
      Assert.True(File.Exists(settingsPath));

      using (var settingsDoc = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath)))
      {
        var root = settingsDoc.RootElement;
        Assert.Equal(string.Empty, root.GetProperty("agentMode").GetString());
        Assert.Equal("request-review", root.GetProperty("toolPermission").GetString());
        Assert.False(root.GetProperty("allowNonWorkspaceAccess").GetBoolean());

        var allow = root.GetProperty("permissions").GetProperty("allow").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("mcp(platform/*)", allow);
        Assert.Contains($"read_file({Path.GetFullPath(workDir)})", allow);
        Assert.Contains($"read_file({Path.GetFullPath(attDir)})", allow);
        Assert.Contains($"read_file({AgyTurnConfigWriter.ResolveCanonicalPath(workDir)})", allow);

        var deny = root.GetProperty("permissions").GetProperty("deny").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("command(*)", deny);
        Assert.Contains("write_file(*)", deny);
        Assert.Contains("read_url(*)", deny);
        Assert.Contains("execute_url(*)", deny);
        Assert.Contains("download_url(*)", deny);
      }

      var agyMcpPath = Path.Combine(workDir, ".agents", "mcp_config.json");
      Assert.True(File.Exists(agyMcpPath));
      using var mcpDoc = JsonDocument.Parse(await File.ReadAllTextAsync(agyMcpPath));
      var platform = mcpDoc.RootElement.GetProperty("mcpServers").GetProperty("platform");
      Assert.Equal("http://127.0.0.1:5191/api/mcp", platform.GetProperty("serverUrl").GetString());
      Assert.Equal("tok-agy-1", platform.GetProperty("headers").GetProperty("X-Platform-Mcp-Token").GetString());
      Assert.True(platform.GetProperty("tools").GetProperty("platform_records_query").GetProperty("eager").GetBoolean());
      Assert.True(platform.GetProperty("tools").GetProperty("platform_catalog_list").GetProperty("eager").GetBoolean());
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
  public async Task ConfigureTurnStartInfo_FailsClosedWhenMcpConfigMissingOrMalformedOrWorkingDirEmpty()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), $"air-agy-failclosed-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    try
    {
      var badMcpPath = Path.Combine(tempDir, "bad-mcp.json");
      await File.WriteAllTextAsync(badMcpPath, "{ not-json");

      var workDir = Path.Combine(tempDir, "turn-bad");
      var psiWithBadMcp = new ProcessStartInfo { WorkingDirectory = workDir };
      AgyCliAgentProvider.ConfigureTurnStartInfo(psiWithBadMcp, "Hi", "gemini-4-hydrogen-low", null, "", badMcpPath);

      Assert.False(File.Exists(Path.Combine(workDir, ".agents", "mcp_config.json")));
      var badIdx = psiWithBadMcp.ArgumentList.IndexOf("--settings");
      Assert.True(badIdx >= 0);
      using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(psiWithBadMcp.ArgumentList[badIdx + 1])))
      {
        var allow = doc.RootElement.GetProperty("permissions").GetProperty("allow").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.DoesNotContain("mcp(platform/*)", allow);
      }

      // Empty working directory still passes inline JSON --settings (fail-closed).
      var inlinePsi = new ProcessStartInfo();
      AgyCliAgentProvider.ConfigureTurnStartInfo(inlinePsi, "Hi", "gemini-4-hydrogen-low", null, "");
      var inlineIdx = inlinePsi.ArgumentList.IndexOf("--settings");
      Assert.True(inlineIdx >= 0 && inlineIdx + 1 < inlinePsi.ArgumentList.Count);
      using var inlineDoc = JsonDocument.Parse(inlinePsi.ArgumentList[inlineIdx + 1]);
      Assert.Equal("request-review", inlineDoc.RootElement.GetProperty("toolPermission").GetString());
      Assert.False(inlineDoc.RootElement.GetProperty("allowNonWorkspaceAccess").GetBoolean());
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
  public void HandleStreamEvent_ParsesInitToolStatusDeltaFallbackAndResultError()
  {
    string? capturedConvId = null;
    var text = new StringBuilder();
    var events = new List<AgentStreamEventDto>();

    using (var initDoc = JsonDocument.Parse("""{"event":"init","conversation_id":"conv-42"}"""))
    {
      var (shouldBreak, err) = AgyCliAgentProvider.HandleStreamEvent(initDoc.RootElement, text, events, cid => capturedConvId = cid, "Preparing...");
      Assert.False(shouldBreak);
      Assert.False(err);
      Assert.Equal("conv-42", capturedConvId);
    }

    using (var toolActiveDoc = JsonDocument.Parse("""{"event":"step_update","step_update":{"step_type":"tool","state":"ACTIVE","tool_name":"mcp_platform_platform_records_query"}}"""))
    {
      AgyCliAgentProvider.HandleStreamEvent(toolActiveDoc.RootElement, text, events, _ => { }, "Preparing...");
      Assert.Single(events, e => e.Type == AgentStreamEventType.Status && e.Text == "Preparing...");
    }

    events.Clear();
    using (var toolDoneDoc = JsonDocument.Parse("""{"event":"step_update","step_update":{"step_type":"tool","state":"DONE","tool_name":"mcp_platform_platform_records_query"}}"""))
    {
      AgyCliAgentProvider.HandleStreamEvent(toolDoneDoc.RootElement, text, events, _ => { }, "Preparing...");
      Assert.Empty(events);
    }

    using (var fallbackResultDoc = JsonDocument.Parse("""{"event":"result","result":{"status":"SUCCESS","response":"Done!"}}"""))
    {
      var (shouldBreak, err) = AgyCliAgentProvider.HandleStreamEvent(fallbackResultDoc.RootElement, text, events, _ => { }, "Preparing...");
      Assert.True(shouldBreak);
      Assert.False(err);
      Assert.Equal("Done!", text.ToString());
      Assert.Single(events, e => e.Type == AgentStreamEventType.Delta && e.Text == "Done!");
    }

    events.Clear();
    using (var errResultDoc = JsonDocument.Parse("""{"event":"result","result":{"status":"ERROR","error":"quota exceeded"}}"""))
    {
      var (shouldBreak, err) = AgyCliAgentProvider.HandleStreamEvent(errResultDoc.RootElement, text, events, _ => { }, "Preparing...");
      Assert.True(shouldBreak);
      Assert.True(err);
      Assert.Single(events, e => e.Type == AgentStreamEventType.Error && e.Text == "quota exceeded");
    }

    events.Clear();
    using (var msgErrDoc = JsonDocument.Parse("""{"event":"error","message":"backend unavailable"}"""))
    {
      var (shouldBreak, err) = AgyCliAgentProvider.HandleStreamEvent(msgErrDoc.RootElement, text, events, _ => { }, "Preparing...");
      Assert.False(shouldBreak);
      Assert.True(err);
      Assert.Single(events, e => e.Type == AgentStreamEventType.Error && e.Text == "backend unavailable");
    }
  }
}
