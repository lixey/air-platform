namespace Platform.Agent.Providers.Internals;

internal static class CliTurnRunner
{
  private static readonly JsonSerializerOptions PromptJsonOptions = new(JsonSerializerDefaults.Web)
  {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

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
    Action<ProcessStartInfo, string, string?> configureStartInfo,
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

    var sessionWorkspaceDir = store.GetSessionWorkspaceDirectory(request.Owner, request.SessionId);
    var existingConvId = store.GetConversationId(request.SessionId, request.Owner, providerHandle);
    var contextFilePath = AgentStore.GetContextFilePath(store.WorkspaceDirectory, request.Owner, request.SessionId);
    var recordsFilePath = AgentStore.GetRecordsFilePath(store.WorkspaceDirectory, request.Owner, request.SessionId);
    var syncContextFilePath = AgentStore.GetSyncContextFilePath(store.WorkspaceDirectory, request.Owner, request.SessionId);
    try
    {
      if (File.Exists(contextFilePath))
      {
        File.Delete(contextFilePath);
      }

      if (File.Exists(recordsFilePath))
      {
        File.Delete(recordsFilePath);
      }

      if (File.Exists(syncContextFilePath))
      {
        File.Delete(syncContextFilePath);
      }
    }
    catch { }

    var stagedAttachmentPaths = new List<string>();
    var effectiveRequest = request;
    if (request.Attachments.Count > 0)
    {
      var stagedAttachments = new List<AgentAttachmentDto>(request.Attachments.Count);
      foreach (var att in request.Attachments)
      {
        if (!string.IsNullOrWhiteSpace(att.LocalPath) && File.Exists(att.LocalPath))
        {
          var stagedPath = Path.Combine(sessionWorkspaceDir, Path.GetFileName(att.LocalPath));
          if (!string.Equals(Path.GetFullPath(att.LocalPath), Path.GetFullPath(stagedPath), StringComparison.OrdinalIgnoreCase))
          {
            File.Copy(att.LocalPath, stagedPath, overwrite: true);
            stagedAttachmentPaths.Add(stagedPath);
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

    var prompt = AgentPromptBuilder.BuildPrompt(effectiveRequest, contextJson, store.WorkspaceDirectory);

    var psi = new ProcessStartInfo
    {
      FileName = executablePath,
      WorkingDirectory = sessionWorkspaceDir,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8
    };

    configureStartInfo(psi, prompt, existingConvId);

    using var process = new Process { StartInfo = psi };
    var fullAssistantText = new StringBuilder();
    var emittedProposals = 0;
    var emittedError = false;
    var pendingEvents = new List<AgentStreamEventDto>();
    Task<string>? stderrTask = null;

    try
    {
      process.Start();
      process.StandardInput.Close();
      stderrTask = process.StandardError.ReadToEndAsync(ct);

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
        var line = await process.StandardOutput.ReadLineAsync(ct);
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
        if (!process.HasExited && !ct.IsCancellationRequested)
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

      if (File.Exists(contextFilePath))
      {
        try
        {
          File.Delete(contextFilePath);
        }
        catch { }
      }

      if (File.Exists(recordsFilePath))
      {
        try
        {
          File.Delete(recordsFilePath);
        }
        catch { }
      }

      if (File.Exists(syncContextFilePath))
      {
        try
        {
          File.Delete(syncContextFilePath);
        }
        catch { }
      }

      foreach (var stagedPath in stagedAttachmentPaths)
      {
        try
        {
          if (File.Exists(stagedPath))
          {
            File.Delete(stagedPath);
          }
        }
        catch { }
      }
    }

    var completeText = fullAssistantText.ToString();
    await AgentPromptBuilder.StageInlineProposalsAsync(completeText, request, ct);

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
