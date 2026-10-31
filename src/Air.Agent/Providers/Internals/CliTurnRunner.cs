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
    [EnumeratorCancellation] CancellationToken ct)
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

    var existingConvId = store.GetConversationId(request.SessionId, request.Owner, providerHandle);
    var contextFilePath = Path.Combine(store.WorkspaceDirectory, $"context-{AgentStore.NormalizeSessionId(request.SessionId)}.json");
    try
    {
      if (File.Exists(contextFilePath))
      {
        File.Delete(contextFilePath);
      }
    }
    catch { }

    string? contextJson = null;
    try
    {
      var hasPhoto = request.Attachments.Count > 0;
      var includeReports = !hasPhoto;
      var includeRecentRecords = !hasPhoto || request.Message.Contains('#') || Regex.IsMatch(request.Message, @"\bid\s*\d+", RegexOptions.IgnoreCase);
      var ctxObj = await AirAgentRuntime.BuildContextAsync(request.Services, request.Message, ct, includeReports, includeRecentRecords);
      contextJson = JsonSerializer.Serialize(ctxObj, PromptJsonOptions);
      if (includeReports && Encoding.UTF8.GetByteCount(contextJson) > 98_000)
      {
        await File.WriteAllTextAsync(contextFilePath, contextJson, Encoding.UTF8, ct);
        var compactObj = await AirAgentRuntime.BuildContextAsync(request.Services, request.Message, ct, includeReports: false, includeRecentRecords);
        contextJson = JsonSerializer.Serialize(compactObj, PromptJsonOptions);
      }
    }
    catch
    {
      // Ignore context prefetch errors
    }

    var prompt = AgentPromptBuilder.BuildPrompt(request, contextJson, store.WorkspaceDirectory);

    var psi = new ProcessStartInfo
    {
      FileName = executablePath,
      WorkingDirectory = store.WorkspaceDirectory,
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
    process.Start();
    process.StandardInput.Close();
    var stderrTask = process.StandardError.ReadToEndAsync(ct);

    yield return new AgentStreamEventDto
    {
      Type = "status",
      Text = request.Attachments.Count > 0 ? L["AgentStatusReadingPhoto"] : L["AgentStatusAnalyzing"],
      ModelId = selectedModel,
      ModelName = modelDisplayName
    };

    var fullAssistantText = new StringBuilder();
    var emittedProposals = 0;
    var emittedError = false;
    var pendingEvents = new List<AgentStreamEventDto>();

    try
    {
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
      if (!process.HasExited && !ct.IsCancellationRequested)
      {
        try
        {
          await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch { }
      }

      if (!process.HasExited)
      {
        try
        {
          process.Kill(entireProcessTree: true);
        }
        catch { }
      }

      if (File.Exists(contextFilePath))
      {
        try
        {
          File.Delete(contextFilePath);
        }
        catch { }
      }
    }

    var completeText = fullAssistantText.ToString();
    await AgentPromptBuilder.StageInlineProposalsAsync(completeText, request, ct);

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
        stderr = (await stderrTask.WaitAsync(TimeSpan.FromSeconds(2), ct)).Trim();
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
