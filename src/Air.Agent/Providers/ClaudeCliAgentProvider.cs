namespace Platform.Agent.Providers;

/// <summary>
/// Adapter for Claude Code CLI (`claude -p --output-format stream-json`).
/// </summary>
public sealed class ClaudeCliAgentProvider(AgentStore store) : IAgentProvider
{
  private static readonly JsonSerializerOptions PromptJsonOptions = new(JsonSerializerDefaults.Web)
  {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  private IReadOnlyList<AgentModelDto>? _cachedModels;
  private DateTime _cachedBinaryWriteUtc;
  private string? _cachedBinaryPath;
  private readonly SemaphoreSlim _modelsLock = new(1, 1);

  private static readonly Regex BinaryModelRegex = new(
    @"\bclaude-(?<family>opus|sonnet|haiku)-(?<major>\d{1,2})(?:-(?<minor>\d{1,2}))?(?![0-9-])\b",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  public string Handle => "Claude";
  public string Name => "Claude CLI (claude)";
  public bool IsAvailable => ResolveExecutablePath() is not null;

  public async Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false)
  {
    var claudePath = ResolveExecutablePath();
    if (claudePath is null)
    {
      return [];
    }

    var realPath = File.ResolveLinkTarget(claudePath, returnFinalTarget: true)?.FullName ?? claudePath;
    if (!File.Exists(realPath))
    {
      return [];
    }

    var writeUtc = File.GetLastWriteTimeUtc(realPath);
    if (!forceRefresh &&
        _cachedModels is { Count: > 0 } &&
        string.Equals(_cachedBinaryPath, realPath, StringComparison.Ordinal) &&
        _cachedBinaryWriteUtc == writeUtc)
    {
      return _cachedModels;
    }

    await _modelsLock.WaitAsync(ct);
    try
    {
      if (!forceRefresh &&
          _cachedModels is { Count: > 0 } &&
          string.Equals(_cachedBinaryPath, realPath, StringComparison.Ordinal) &&
          _cachedBinaryWriteUtc == writeUtc)
      {
        return _cachedModels;
      }

      var discovered = await DiscoverModelsFromBinaryStreamAsync(realPath, ct);
      if (discovered.Count > 0)
      {
        _cachedModels = discovered;
        _cachedBinaryPath = realPath;
        _cachedBinaryWriteUtc = writeUtc;
        return _cachedModels;
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Ignore binary inspection errors
    }
    finally
    {
      _modelsLock.Release();
    }

    return _cachedModels ?? [];
  }

  private static async Task<List<AgentModelDto>> DiscoverModelsFromBinaryStreamAsync(string realPath, CancellationToken ct)
  {
    const int chunkSize = 1024 * 1024;
    const int overlapSize = 64;

    var found = new HashSet<(string Family, int Major, int? Minor)>();
    var buffer = new byte[chunkSize + overlapSize];
    var carryLen = 0;

    await using var fs = new FileStream(realPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: true);
    while (true)
    {
      var read = await fs.ReadAsync(buffer.AsMemory(carryLen, chunkSize), ct);
      if (read <= 0)
      {
        break;
      }

      var totalLen = carryLen + read;
      var chunkText = Encoding.Latin1.GetString(buffer, 0, totalLen);
      foreach (Match match in BinaryModelRegex.Matches(chunkText))
      {
        var family = match.Groups["family"].Value.ToLowerInvariant();
        if (!int.TryParse(match.Groups["major"].Value, out var major) || major <= 0)
        {
          continue;
        }

        int? minor = null;
        if (match.Groups["minor"].Success && int.TryParse(match.Groups["minor"].Value, out var parsedMinor))
        {
          if (parsedMinor > 20)
          {
            continue;
          }

          minor = parsedMinor;
        }

        found.Add((family, major, minor));
      }

      if (totalLen > overlapSize)
      {
        Buffer.BlockCopy(buffer, totalLen - overlapSize, buffer, 0, overlapSize);
        carryLen = overlapSize;
      }
      else
      {
        carryLen = totalLen;
      }
    }

    if (found.Count == 0)
    {
      return [];
    }

    static int FamilyRank(string family) => family switch
    {
      "opus" => 30,
      "sonnet" => 20,
      "haiku" => 10,
      _ => 0
    };

    static string FamilyTitle(string family) => family switch
    {
      "opus" => "Opus",
      "sonnet" => "Sonnet",
      "haiku" => "Haiku",
      _ => family
    };

    // Select the latest generation (highest major + minor) per family present in the installed CLI binary
    var ordered = found
      .GroupBy(x => x.Family, StringComparer.OrdinalIgnoreCase)
      .Select(g => g
        .OrderByDescending(x => x.Major)
        .ThenByDescending(x => x.Minor ?? 0)
        .First())
      .OrderByDescending(x => x.Major)
      .ThenByDescending(x => x.Minor ?? 0)
      .ThenByDescending(x => FamilyRank(x.Family))
      .ToList();

    var result = new List<AgentModelDto>();
    foreach (var (family, major, minor) in ordered)
    {
      var baseId = minor.HasValue ? $"claude-{family}-{major}-{minor}" : $"claude-{family}-{major}";
      var verText = minor.HasValue ? $"{major}.{minor}" : $"{major}";
      var title = $"Claude {FamilyTitle(family)} {verText}";

      if (family is "opus" or "sonnet")
      {
        result.Add(new AgentModelDto { Id = $"{baseId}-high", Name = $"{title} (High)", Provider = "Claude" });
        result.Add(new AgentModelDto { Id = $"{baseId}-medium", Name = $"{title} (Medium)", Provider = "Claude" });
        result.Add(new AgentModelDto { Id = $"{baseId}-low", Name = $"{title} (Low)", Provider = "Claude" });
      }
      else
      {
        result.Add(new AgentModelDto { Id = baseId, Name = title, Provider = "Claude" });
      }
    }

    return result;
  }

  public static string? ResolveExecutablePath()
  {
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var candidates = new[]
    {
      "/opt/homebrew/bin/claude",
      "/usr/local/bin/claude",
      Path.Combine(home, ".local", "bin", "claude")
    };

    foreach (var candidate in candidates)
    {
      if (File.Exists(candidate))
      {
        return candidate;
      }
    }

    var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
      var full = Path.Combine(dir, "claude");
      if (File.Exists(full))
      {
        return full;
      }
    }

    return null;
  }

  private static (string Model, string Effort) ResolveCliModelArgs(string selectedModel)
  {
    var lower = selectedModel.Trim().ToLowerInvariant();
    var effort = "high";
    var baseModel = lower;

    string[] effortSuffixes = ["-high", "-medium", "-low"];
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

  public async IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    var L = Localizer.Instance;
    var claudePath = ResolveExecutablePath();
    if (claudePath is null)
    {
      yield return new AgentStreamEventDto
      {
        Type = "error",
        Text = L["AgentErrorCliMissing"]
      };
      yield break;
    }

    var existingConvId = store.GetConversationId(request.SessionId, request.Owner, Handle);

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
        var fullPath = Path.Combine(store.WorkspaceDirectory, $"context-{request.SessionId}.json");
        await File.WriteAllTextAsync(fullPath, contextJson, Encoding.UTF8, ct);
        var compactObj = await AirAgentRuntime.BuildContextAsync(request.Services, request.Message, ct, includeReports: false, includeRecentRecords);
        contextJson = JsonSerializer.Serialize(compactObj, PromptJsonOptions);
      }
    }
    catch
    {
      // Ignore context prefetch errors
    }

    var prompt = AgentPromptBuilder.BuildPrompt(request, contextJson, store.WorkspaceDirectory);
    var rawModel = string.IsNullOrWhiteSpace(request.Model) ? string.Empty : request.Model.Trim();
    var selectedModel = rawModel.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    var modelDisplayName = !string.IsNullOrWhiteSpace(request.ModelName)
      ? request.ModelName
      : (_cachedModels?.FirstOrDefault(m => string.Equals(m.Id, selectedModel, StringComparison.OrdinalIgnoreCase))?.Name ?? selectedModel);
    var (cliModel, effort) = ResolveCliModelArgs(selectedModel);

    var psi = new ProcessStartInfo
    {
      FileName = claudePath,
      WorkingDirectory = store.WorkspaceDirectory,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8
    };

    psi.ArgumentList.Add("--print");
    psi.ArgumentList.Add(prompt);
    psi.ArgumentList.Add("--output-format");
    psi.ArgumentList.Add("stream-json");
    psi.ArgumentList.Add("--verbose");
    psi.ArgumentList.Add("--safe-mode");
    psi.ArgumentList.Add("--strict-mcp-config");
    psi.ArgumentList.Add("--disable-slash-commands");
    psi.ArgumentList.Add("--tools");
    psi.ArgumentList.Add("Read");
    psi.ArgumentList.Add("--allowedTools");
    psi.ArgumentList.Add("Read");
    if (!string.IsNullOrWhiteSpace(cliModel))
    {
      psi.ArgumentList.Add("--model");
      psi.ArgumentList.Add(cliModel);
    }

    psi.ArgumentList.Add("--effort");
    psi.ArgumentList.Add(effort);
    psi.ArgumentList.Add("--add-dir");
    psi.ArgumentList.Add(store.WorkspaceDirectory);

    if (!string.IsNullOrWhiteSpace(existingConvId) && request.Attachments.Count == 0)
    {
      psi.ArgumentList.Add("--resume");
      psi.ArgumentList.Add(existingConvId);
    }

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

        using (doc)
        {
          var root = doc.RootElement;
          if (root.TryGetProperty("session_id", out var sidProp) &&
              sidProp.ValueKind == JsonValueKind.String &&
              sidProp.GetString() is { Length: > 0 } sid)
          {
            store.SetConversationId(request.SessionId, request.Owner, sid, Handle);
          }

          var type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() : null;
          if (type == "assistant" &&
              root.TryGetProperty("message", out var msgObj) &&
              msgObj.TryGetProperty("content", out var contentArr) &&
              contentArr.ValueKind == JsonValueKind.Array)
          {
            foreach (var item in contentArr.EnumerateArray())
            {
              var itemType = item.TryGetProperty("type", out var itProp) ? itProp.GetString() : null;
              if (itemType == "text" &&
                  item.TryGetProperty("text", out var txtProp) &&
                  txtProp.GetString() is { Length: > 0 } chunk)
              {
                fullAssistantText.Append(chunk);
                yield return new AgentStreamEventDto
                {
                  Type = "delta",
                  Text = chunk
                };
              }
              else if (itemType == "tool_use")
              {
                yield return new AgentStreamEventDto
                {
                  Type = "status",
                  Text = L["AgentStatusPreparing"]
                };
              }
            }
          }
          else if (type == "result" &&
                   fullAssistantText.Length == 0 &&
                   root.TryGetProperty("result", out var resProp) &&
                   resProp.ValueKind == JsonValueKind.String &&
                   resProp.GetString() is { Length: > 0 } resText)
          {
            if (root.TryGetProperty("is_error", out var errFlag) && errFlag.ValueKind == JsonValueKind.True)
            {
              emittedError = true;
              yield return new AgentStreamEventDto
              {
                Type = "error",
                Text = resText
              };
            }
            else
            {
              fullAssistantText.Append(resText);
              yield return new AgentStreamEventDto
              {
                Type = "delta",
                Text = resText
              };
            }
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

          if (type == "result")
          {
            break;
          }
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

    if (emittedError || fullAssistantText.Length == 0 && emittedProposals == 0)
    {
      store.RemoveConversationId(request.SessionId, request.Owner, Handle);
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
