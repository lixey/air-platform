using Platform.Agent.Internals;

namespace Platform.Agent.Providers.Internals;

internal static class CliTurnRunner
{
  internal static bool IsAllowedEnvironmentVariable(string name) =>
    CliEnvironmentHelper.IsAllowedEnvironmentVariable(name);

  internal static void ConfigureSanitizedEnvironment(
    IDictionary<string, string?> targetEnv,
    IReadOnlyDictionary<string, string?> sourceEnv,
    string? mcpTurnToken = null,
    string? sessionId = null,
    string? mcpServerUrl = null) =>
    CliEnvironmentHelper.ConfigureSanitizedEnvironment(targetEnv, sourceEnv, mcpTurnToken, sessionId, mcpServerUrl);

  internal static void ScrubStartInfoEnvironment(
    ProcessStartInfo psi,
    string? mcpTurnToken = null,
    string? sessionId = null,
    string? mcpServerUrl = null) =>
    CliEnvironmentHelper.ScrubStartInfoEnvironment(psi, mcpTurnToken, sessionId, mcpServerUrl);

  public static string? ResolveExecutablePath(string binaryName, params string[] preferredPaths) =>
    CliEnvironmentHelper.ResolveExecutablePath(binaryName, preferredPaths);

  public static (string Model, string Effort) ResolveCliModelArgs(string selectedModel, params string[] effortSuffixes) =>
    CliEnvironmentHelper.ResolveCliModelArgs(selectedModel, effortSuffixes);

  public static async IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(
    AgentStore store,
    string providerHandle,
    string? executablePath,
    AgentTurnRequest request,
    string selectedModel,
    string modelDisplayName,
    Func<ProcessStartInfo, string, string?, string?, string, AgentTurnRequest, string> configureStartInfoAndStdin,
    Func<JsonElement, StringBuilder, List<AgentStreamEventDto>, (bool ShouldBreak, bool EmittedError)> handleJsonLine,
    [EnumeratorCancellation] CancellationToken ct)
  {
    var L = Localizer.Instance;
    if (executablePath is null)
    {
      yield return new AgentStreamEventDto { Type = AgentStreamEventType.Error, Text = L["AgentErrorCliMissing"] };
      yield break;
    }

    if (!store.TryAcquireSessionTurn(request.SessionId, request.Owner, out var sessionLease) || sessionLease is null)
    {
      yield return new AgentStreamEventDto { Type = AgentStreamEventType.Error, Text = L["AgentErrorSessionBusy"] };
      yield break;
    }

    using var ownedSessionLease = sessionLease;
    using var globalLease = await store.AcquireGlobalTurnSlotAsync(ct);

    var turnWorkspaceDir = store.CreateTurnWorkspaceDirectory(request.Owner, request.SessionId);
    var sessionAttachmentsDir = store.GetSessionAttachmentsDirectory(request.Owner, request.SessionId);
    var existingConvId = store.GetConversationId(request.SessionId, request.Owner, providerHandle);

    var fullAssistantText = new StringBuilder();
    var emittedProposals = 0;
    var emittedError = false;
    var timedOut = false;
    Task<string>? stderrTask = null;
    var effectiveRequest = request;

    try
    {
      if (request.Attachments.Count > 0)
      {
        var stagedAttachments = AgentAttachmentValidator.StageSessionAttachments(request.Attachments, sessionAttachmentsDir);
        effectiveRequest = request with { Attachments = stagedAttachments };
      }

      var contextJson = await CliEnvironmentHelper.PrepareContextFilesAsync(effectiveRequest, turnWorkspaceDir, ct);
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

      var mcpTurnToken = effectiveRequest.Services?.GetService<McpExecutionBridge>()?.Active?.TurnToken;
      var mcpConfigPath = await CliEnvironmentHelper.WriteMcpConfigAsync(
        turnWorkspaceDir, request.McpServerUrl, mcpTurnToken, utf8NoBom, ct);

      ScrubStartInfoEnvironment(psi, mcpTurnToken, request.SessionId, request.McpServerUrl);
      var stdinPayload = configureStartInfoAndStdin(
        psi, prompt, existingConvId, mcpConfigPath, sessionAttachmentsDir, effectiveRequest);

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
          // Child CLI exited before draining stdin
        }

        var hasImageAttachment = effectiveRequest.Attachments.Any(a => AgentAttachmentValidator.IsInlineSafeImage(a.ContentType));
        yield return new AgentStreamEventDto
        {
          Type = AgentStreamEventType.Status,
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

          JsonDocument? doc;
          try
          {
            doc = JsonDocument.Parse(line);
          }
          catch
          {
            continue;
          }

          bool shouldBreak;
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
              Type = AgentStreamEventType.Supersede,
              SupersededProposalIds = [.. midTurnSuperseded]
            };
          }

          foreach (var staged in store.DrainPendingProposals(request.SessionId, request.Owner))
          {
            emittedProposals++;
            yield return new AgentStreamEventDto
            {
              Type = AgentStreamEventType.Proposal,
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
      yield return new AgentStreamEventDto { Type = AgentStreamEventType.Error, Text = L["AgentErrorTurnTimeout"] };
    }

    var completeText = fullAssistantText.ToString();
    var (stageWarnings, savedMemories) = await AgentPromptBuilder.StageInlineProposalsAndMemoriesAsync(completeText, effectiveRequest, ct);

    var supersededIds = store.DrainSupersededProposalIds(request.SessionId, request.Owner);
    if (supersededIds.Count > 0)
    {
      yield return new AgentStreamEventDto
      {
        Type = AgentStreamEventType.Supersede,
        SupersededProposalIds = [.. supersededIds]
      };
    }

    foreach (var staged in store.DrainPendingProposals(request.SessionId, request.Owner))
    {
      emittedProposals++;
      yield return new AgentStreamEventDto
      {
        Type = AgentStreamEventType.Proposal,
        Proposal = staged
      };
    }

    foreach (var savedMem in savedMemories)
    {
      yield return new AgentStreamEventDto
      {
        Type = AgentStreamEventType.Memory,
        Memory = savedMem
      };
    }

    if (stageWarnings.Count > 0)
    {
      var warningBlock = "\n\n" + string.Join("\n\n", stageWarnings.Select(w => $"> {w}"));
      fullAssistantText.Append(warningBlock);
      yield return new AgentStreamEventDto { Type = AgentStreamEventType.Delta, Text = warningBlock };
    }

    if (!ct.IsCancellationRequested && (emittedError || fullAssistantText.Length == 0 && emittedProposals == 0 && savedMemories.Count == 0))
    {
      store.RemoveConversationId(request.SessionId, request.Owner, providerHandle);
    }

    if (!emittedError && fullAssistantText.Length == 0 && emittedProposals == 0 && savedMemories.Count == 0 && !ct.IsCancellationRequested)
    {
      string? stderr = null;
      try
      {
        stderr = stderrTask is not null ? (await stderrTask.WaitAsync(TimeSpan.FromSeconds(2), ct)).Trim() : null;
      }
      catch { }

      yield return new AgentStreamEventDto
      {
        Type = AgentStreamEventType.Error,
        Text = !string.IsNullOrWhiteSpace(stderr) ? stderr : L["AgentErrorRequest"]
      };
    }

    yield return new AgentStreamEventDto
    {
      Type = AgentStreamEventType.Done,
      SessionId = request.SessionId,
      ModelId = selectedModel,
      ModelName = modelDisplayName
    };
  }
}
