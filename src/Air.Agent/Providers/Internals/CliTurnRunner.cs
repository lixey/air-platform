namespace Platform.Agent.Providers.Internals;

internal static class CliTurnRunner
{
  private static readonly JsonSerializerOptions PromptJsonOptions = new(JsonSerializerDefaults.Web)
  {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  private static readonly HashSet<string> AllowedExactEnvVars = new(StringComparer.OrdinalIgnoreCase)
  {
    "PATH",
    "HOME",
    "USER",
    "LOGNAME",
    "SHELL",
    "TMPDIR",
    "TEMP",
    "TMP",
    "LANG",
    "LC_ALL",
    "LC_CTYPE",
    "TERM",
    "SSL_CERT_FILE",
    "SSL_CERT_DIR",
    "NODE_EXTRA_CA_CERTS",
    "HTTP_PROXY",
    "HTTPS_PROXY",
    "NO_PROXY",
    "ALL_PROXY",
    "APPDATA",
    "LOCALAPPDATA",
    "USERPROFILE",
    "SYSTEMROOT",
    "COMSPEC",
    "PATHEXT"
  };

  private static readonly string[] AllowedEnvPrefixes =
  [
    "XDG_",
    "AGY_",
    "ANTHROPIC_",
    "CLAUDE_",
    "OPENAI_",
    "CODEX_",
    "GEMINI_"
  ];

  internal static bool IsAllowedEnvironmentVariable(string name)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      return false;
    }

    if (AllowedExactEnvVars.Contains(name))
    {
      return true;
    }

    foreach (var prefix in AllowedEnvPrefixes)
    {
      if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  internal static void ConfigureSanitizedEnvironment(
    IDictionary<string, string?> targetEnv,
    IReadOnlyDictionary<string, string?> sourceEnv,
    string? mcpTurnToken = null,
    string? sessionId = null,
    string? mcpServerUrl = null
  )
  {
    targetEnv.Clear();
    foreach (var (key, value) in sourceEnv)
    {
      if (value is not null && IsAllowedEnvironmentVariable(key))
      {
        targetEnv[key] = value;
      }
    }

    targetEnv["NO_COLOR"] = "1";
    if (!string.IsNullOrWhiteSpace(mcpTurnToken))
    {
      targetEnv["AIR_MCP_TOKEN"] = mcpTurnToken;
    }

    if (!string.IsNullOrWhiteSpace(sessionId))
    {
      targetEnv["AIR_SESSION_ID"] = sessionId;
    }

    if (!string.IsNullOrWhiteSpace(mcpServerUrl))
    {
      targetEnv["AIR_MCP_URL"] = mcpServerUrl;
    }
  }

  internal static void ScrubStartInfoEnvironment(
    ProcessStartInfo psi,
    string? mcpTurnToken = null,
    string? sessionId = null,
    string? mcpServerUrl = null
  )
  {
    var hostVars = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
    {
      if (entry.Key is string key)
      {
        hostVars[key] = entry.Value as string;
      }
    }

    ConfigureSanitizedEnvironment(psi.Environment, hostVars, mcpTurnToken, sessionId, mcpServerUrl);
  }

  public static string? ResolveExecutablePath(string binaryName, params string[] preferredPaths)
  {
    foreach (var candidate in preferredPaths)
    {
      if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
      {
        return candidate;
      }
    }

    var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
      var full = Path.Combine(dir, binaryName);
      if (File.Exists(full))
      {
        return full;
      }
    }

    return null;
  }

  public static (string Model, string Effort) ResolveCliModelArgs(string selectedModel, params string[] effortSuffixes)
  {
    var lower = selectedModel.Trim().ToLowerInvariant();
    var effort = "high";
    var baseModel = lower;

    foreach (var suffix in effortSuffixes)
    {
      if (lower.EndsWith(suffix, StringComparison.Ordinal) && lower.Length > suffix.Length)
      {
        effort = suffix[1..];
        baseModel = lower[..^suffix.Length];
        break;
      }
    }

    return (baseModel, effort);
  }

  public static async IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(
    AgentStore store,
    string providerHandle,
    string? executablePath,
    AgentTurnRequest request,
    string selectedModel,
    string modelDisplayName,
    Func<ProcessStartInfo, string, string?, string?, string> configureStartInfoAndStdin,
    Func<JsonElement, StringBuilder, List<AgentStreamEventDto>, (bool ShouldBreak, bool EmittedError)> handleJsonLine,
    [EnumeratorCancellation] CancellationToken ct
  )
  {
    var L = Localizer.Instance;
    if (executablePath is null)
    {
      yield return new AgentStreamEventDto
      {
        Type = "error",
        Text = L["AgentErrorCliMissing"]
      };
      yield break;
    }

    if (!store.TryAcquireSessionTurn(request.SessionId, request.Owner, out var sessionLease) || sessionLease is null)
    {
      yield return new AgentStreamEventDto
      {
        Type = "error",
        Text = L["AgentErrorSessionBusy"]
      };
      yield break;
    }

    using var ownedSessionLease = sessionLease;
    using var globalLease = await store.AcquireGlobalTurnSlotAsync(ct);

    var turnWorkspaceDir = store.CreateTurnWorkspaceDirectory(request.Owner, request.SessionId);
    var existingConvId = store.GetConversationId(request.SessionId, request.Owner, providerHandle);
    var contextFilePath = AgentStore.GetContextFilePath(turnWorkspaceDir);
    var recordsFilePath = AgentStore.GetRecordsFilePath(turnWorkspaceDir);

    var fullAssistantText = new StringBuilder();
    var emittedProposals = 0;
    var emittedError = false;
    var timedOut = false;
    Task<string>? stderrTask = null;

    try
    {
      var effectiveRequest = request;
      if (request.Attachments.Count > 0)
      {
        var stagedAttachments = new List<AgentAttachmentDto>(request.Attachments.Count);
        foreach (var att in request.Attachments)
        {
          if (!string.IsNullOrWhiteSpace(att.LocalPath) && File.Exists(att.LocalPath))
          {
            var stagedPath = Path.Combine(turnWorkspaceDir, Path.GetFileName(att.LocalPath));
            if (!string.Equals(Path.GetFullPath(att.LocalPath), Path.GetFullPath(stagedPath), StringComparison.OrdinalIgnoreCase))
            {
              File.Copy(att.LocalPath, stagedPath, overwrite: true);
            }

            stagedAttachments.Add(
              new AgentAttachmentDto
              {
                Id = att.Id,
                FileName = att.FileName,
                ContentType = att.ContentType,
                Size = att.Size,
                PreviewUrl = att.PreviewUrl,
                LocalPath = stagedPath,
                Sha256 = att.Sha256
              }
            );
          }
          else
          {
            stagedAttachments.Add(att);
          }
        }

        effectiveRequest = request with
        {
          Attachments = stagedAttachments
        };
      }

      string? contextJson = null;
      if (effectiveRequest.Services is not null)
      {
        try
        {
          var hasPhoto = effectiveRequest.Attachments.Any(a =>
            string.IsNullOrEmpty(a.ContentType) || a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
          );
          var includeReports = !hasPhoto;
          var ctxObj = await AirAgentRuntime.BuildContextAsync(
            effectiveRequest.Services,
            effectiveRequest.Message,
            ct,
            includeReports,
            includeRecentRecords: true
          );
          contextJson = JsonSerializer.Serialize(ctxObj, PromptJsonOptions);
          if (Encoding.UTF8.GetByteCount(contextJson) > 98_000)
          {
            await File.WriteAllTextAsync(contextFilePath, contextJson, Encoding.UTF8, ct);
            if (ctxObj is IDictionary<string, object?> ctxDict && ctxDict.Remove("reports"))
            {
              contextJson = JsonSerializer.Serialize(ctxDict, PromptJsonOptions);
            }
          }

          var extendedRecordsObj = await AirAgentRuntime.BuildExtendedWorkspaceRecordsAsync(effectiveRequest.Services, ct);
          await File.WriteAllTextAsync(recordsFilePath, JsonSerializer.Serialize(extendedRecordsObj, PromptJsonOptions), Encoding.UTF8, ct);
        }
        catch
        {
          // Ignore context prefetch errors
        }
      }

      var prompt = await AgentPromptBuilder.BuildPromptAsync(effectiveRequest, contextJson, turnWorkspaceDir, ct);
      var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

      var psi = new ProcessStartInfo
      {
        FileName = executablePath,
        WorkingDirectory = turnWorkspaceDir,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardInputEncoding = utf8NoBom,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
      };

      var mcpBridge = effectiveRequest.Services?.GetService<McpExecutionBridge>();
      var mcpTurnToken = mcpBridge?.Active?.TurnToken;
      string? mcpConfigPath = null;
      if (!string.IsNullOrWhiteSpace(request.McpServerUrl) && !string.IsNullOrWhiteSpace(mcpTurnToken))
      {
        mcpConfigPath = Path.Combine(turnWorkspaceDir, "mcp-config.json");
        var mcpConfigJson = JsonSerializer.Serialize(
          new
          {
            mcpServers = new
            {
              air = new
              {
                type = "http",
                url = request.McpServerUrl,
                headers = new Dictionary<string, string>
                {
                  ["X-Air-Mcp-Token"] = mcpTurnToken
                }
              }
            }
          },
          PromptJsonOptions);
        await File.WriteAllTextAsync(mcpConfigPath, mcpConfigJson, utf8NoBom, ct);
      }

      ScrubStartInfoEnvironment(psi, mcpTurnToken, request.SessionId, request.McpServerUrl);
      var stdinPayload = configureStartInfoAndStdin(psi, prompt, existingConvId, mcpConfigPath);

      using var process = new Process { StartInfo = psi };
      var pendingEvents = new List<AgentStreamEventDto>();
      using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      turnCts.CancelAfter(store.Options.TurnTimeout);

      try
      {
        process.Start();
        stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
          if (!string.IsNullOrEmpty(stdinPayload))
          {
            await process.StandardInput.WriteAsync(stdinPayload.AsMemory(), turnCts.Token);
            await process.StandardInput.FlushAsync(turnCts.Token);
          }

          process.StandardInput.Close();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
          timedOut = true;
        }
        catch (IOException)
        {
          // Child CLI exited before draining stdin; let stdout/stderr reading report the actual CLI error.
        }

        var hasImageAttachment = request.Attachments.Any(a =>
          string.IsNullOrEmpty(a.ContentType) || a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        );
        yield return new AgentStreamEventDto
        {
          Type = "status",
          Text = hasImageAttachment ? L["AgentStatusReadingPhoto"] : L["AgentStatusAnalyzing"],
          ModelId = selectedModel,
          ModelName = modelDisplayName
        };

        while (!ct.IsCancellationRequested)
        {
          string? line;
          using (var idleCts = CancellationTokenSource.CreateLinkedTokenSource(turnCts.Token))
          {
            idleCts.CancelAfter(store.Options.IdleOutputTimeout);
            try
            {
              line = await process.StandardOutput.ReadLineAsync(idleCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
              timedOut = true;
              break;
            }
          }

          if (line is null)
          {
            break;
          }

          if (string.IsNullOrWhiteSpace(line) || !line.StartsWith('{'))
          {
            continue;
          }

          JsonDocument? doc = null;
          try
          {
            doc = JsonDocument.Parse(line);
          }
          catch
          {
            continue;
          }

          var shouldBreak = false;
          pendingEvents.Clear();

          using (doc)
          {
            var outcome = handleJsonLine(doc.RootElement, fullAssistantText, pendingEvents);
            shouldBreak = outcome.ShouldBreak;
            if (outcome.EmittedError)
            {
              emittedError = true;
            }
          }

          foreach (var evt in pendingEvents)
          {
            yield return evt;
          }

          var midTurnSuperseded = store.DrainSupersededProposalIds(request.SessionId, request.Owner);
          if (midTurnSuperseded.Count > 0)
          {
            yield return new AgentStreamEventDto
            {
              Type = "supersede",
              SupersededProposalIds = [.. midTurnSuperseded]
            };
          }

          foreach (var staged in store.DrainPendingProposals(request.SessionId, request.Owner))
          {
            emittedProposals++;
            yield return new AgentStreamEventDto
            {
              Type = "proposal",
              Proposal = staged
            };
          }

          if (shouldBreak)
          {
            break;
          }
        }
      }
      finally
      {
        try
        {
          if (!process.HasExited && !ct.IsCancellationRequested && !timedOut)
          {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1));
          }
        }
        catch { }

        try
        {
          if (!process.HasExited)
          {
            process.Kill(entireProcessTree: true);
          }
        }
        catch { }
      }
    }
    finally
    {
      try
      {
        if (Directory.Exists(turnWorkspaceDir))
        {
          Directory.Delete(turnWorkspaceDir, recursive: true);
        }
      }
      catch { }
    }

    if (timedOut)
    {
      emittedError = true;
      yield return new AgentStreamEventDto
      {
        Type = "error",
        Text = L["AgentErrorTurnTimeout"]
      };
    }

    var completeText = fullAssistantText.ToString();
    var stageWarnings = await AgentPromptBuilder.StageInlineProposalsAsync(completeText, request, ct);

    var supersededIds = store.DrainSupersededProposalIds(request.SessionId, request.Owner);
    if (supersededIds.Count > 0)
    {
      yield return new AgentStreamEventDto
      {
        Type = "supersede",
        SupersededProposalIds = [.. supersededIds]
      };
    }

    foreach (var staged in store.DrainPendingProposals(request.SessionId, request.Owner))
    {
      emittedProposals++;
      yield return new AgentStreamEventDto
      {
        Type = "proposal",
        Proposal = staged
      };
    }

    if (stageWarnings.Count > 0)
    {
      var warningBlock = "\n\n" + string.Join("\n\n", stageWarnings.Select(w => $"> {w}"));
      fullAssistantText.Append(warningBlock);
      yield return new AgentStreamEventDto
      {
        Type = "delta",
        Text = warningBlock
      };
    }

    if (!ct.IsCancellationRequested && (emittedError || fullAssistantText.Length == 0 && emittedProposals == 0))
    {
      store.RemoveConversationId(request.SessionId, request.Owner, providerHandle);
    }

    if (!emittedError && fullAssistantText.Length == 0 && emittedProposals == 0 && !ct.IsCancellationRequested)
    {
      string? stderr = null;
      try
      {
        stderr = stderrTask is not null
          ? (await stderrTask.WaitAsync(TimeSpan.FromSeconds(2), ct)).Trim()
          : null;
      }
      catch { }

      yield return new AgentStreamEventDto
      {
        Type = "error",
        Text = !string.IsNullOrWhiteSpace(stderr) ? stderr : L["AgentErrorRequest"]
      };
    }

    yield return new AgentStreamEventDto
    {
      Type = "done",
      SessionId = request.SessionId,
      ModelId = selectedModel,
      ModelName = modelDisplayName
    };
  }
}
