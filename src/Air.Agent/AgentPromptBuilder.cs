using Platform.Agent.Internals;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent;

public static class AgentPromptBuilder
{
  private const int MaxInlinePromptBytes = 105_000;

  public static async Task<string> BuildPromptAsync(
    AgentTurnRequest request,
    string? contextJson,
    string? turnWorkspaceDir = null,
    CancellationToken ct = default)
  {
    var sb = new StringBuilder();
    var uiCulture = CultureInfo.CurrentUICulture;
    var langDisplay = string.Equals(uiCulture.EnglishName, uiCulture.NativeName, StringComparison.OrdinalIgnoreCase)
      ? uiCulture.EnglishName
      : $"{uiCulture.EnglishName} ({uiCulture.NativeName})";

    var recordsFile = !string.IsNullOrWhiteSpace(turnWorkspaceDir)
      ? AgentStore.GetRecordsFilePath(turnWorkspaceDir)
      : null;
    var hasRecordsFile = recordsFile != null && File.Exists(recordsFile);

    AppendSystemInstructions(sb, request.SessionId, langDisplay);

    if (request.Attachments.Count > 0)
    {
      sb.AppendLine();
      sb.AppendLine("[ATTACHED FILES — USE read_file TO INSPECT]");
      foreach (var att in request.Attachments)
      {
        sb.AppendLine($"- AttachmentId: \"{att.Id}\", FileName: \"{att.FileName}\", AbsolutePath: \"{att.LocalPath}\"");
      }
    }

    if (hasRecordsFile)
    {
      sb.AppendLine();
      sb.AppendLine("[EXTENDED WORKSPACE RECORDS — USE read_file ONLY IF OLDER RECENT RECORDS (>25, UP TO 150) ARE NEEDED]");
      sb.AppendLine($"- ExtendedRecordsPath: \"{recordsFile}\"");
    }

    AppendUnconfirmedSessionProposals(sb, request);

    if (!string.IsNullOrWhiteSpace(request.SyncContext))
    {
      var syncBytes = Encoding.UTF8.GetByteCount(request.SyncContext);
      var currentSyncPrefixBytes = Encoding.UTF8.GetByteCount(sb.ToString());
      if (currentSyncPrefixBytes + syncBytes + 8192 <= MaxInlinePromptBytes || string.IsNullOrWhiteSpace(turnWorkspaceDir))
      {
        sb.AppendLine();
        sb.AppendLine("[SYNC CONTEXT JSON]");
        sb.AppendLine(request.SyncContext);
      }
      else
      {
        var syncPath = AgentStore.GetSyncContextFilePath(turnWorkspaceDir);
        await File.WriteAllTextAsync(syncPath, request.SyncContext, Encoding.UTF8, ct);
        sb.AppendLine();
        sb.AppendLine($"[SYNC CONTEXT JSON — FULL CONTEXT SAVED IN \"{syncPath}\" (USE read_file IF NEEDED)]");
        var syncBudgetBytes = Math.Min(24_000, Math.Max(4096, MaxInlinePromptBytes - currentSyncPrefixBytes - 8192));
        sb.AppendLine(TruncateUtf8(request.SyncContext, syncBudgetBytes));
      }
    }

    sb.AppendLine();
    sb.AppendLine("[USER MESSAGE]");
    sb.AppendLine(
      string.IsNullOrWhiteSpace(request.Message)
        ? Localizer.Instance["AgentDefaultPhotoPrompt"]
        : request.Message);

    await AgentMemoryService.AppendDatabaseMemoryAndDomainKnowledgeAsync(sb, request, ct);

    if (!string.IsNullOrWhiteSpace(contextJson))
    {
      var currentBytes = Encoding.UTF8.GetByteCount(sb.ToString());
      var contextBytes = Encoding.UTF8.GetByteCount(contextJson);
      var fullContextFile = !string.IsNullOrWhiteSpace(turnWorkspaceDir)
        ? AgentStore.GetContextFilePath(turnWorkspaceDir)
        : null;

      if (currentBytes + contextBytes + 128 <= MaxInlinePromptBytes || string.IsNullOrWhiteSpace(turnWorkspaceDir))
      {
        sb.AppendLine();
        sb.AppendLine(fullContextFile != null && File.Exists(fullContextFile)
          ? $"[AVAILABLE FORMS, DICTIONARIES, AND RECENT RECORDS CONTEXT JSON — FULL CONTEXT SAVED IN \"{fullContextFile}\"]"
          : "[AVAILABLE FORMS, DICTIONARIES, AND RECENT RECORDS CONTEXT JSON]");
        sb.AppendLine(contextJson);
      }
      else
      {
        var contextPath = fullContextFile!;
        if (!File.Exists(contextPath))
        {
          await File.WriteAllTextAsync(contextPath, contextJson, Encoding.UTF8, ct);
        }

        sb.AppendLine();
        sb.AppendLine($"[AVAILABLE FORMS, DICTIONARIES, AND RECENT RECORDS CONTEXT — FULL CONTEXT SAVED IN \"{contextPath}\" (USE read_file IF NEEDED)]");
        var budgetBytes = Math.Max(4096, MaxInlinePromptBytes - currentBytes - 256);
        sb.AppendLine(TruncateUtf8(contextJson, budgetBytes));
      }
    }

    return sb.ToString();
  }

  public static async Task<IReadOnlyList<string>> StageInlineProposalsAsync(
    string text,
    AgentTurnRequest request,
    CancellationToken ct)
  {
    var (warnings, _) = await StageInlineProposalsAndMemoriesAsync(text, request, ct);
    return warnings;
  }

  public static async Task<(IReadOnlyList<string> Warnings, IReadOnlyList<AgentSavedMemoryDto> SavedMemories)> StageInlineProposalsAndMemoriesAsync(
    string text,
    AgentTurnRequest request,
    CancellationToken ct)
  {
    const string startTag = "<ui_proposal>";
    const string endTag = "</ui_proposal>";
    var warnings = new List<string>();
    var L = Localizer.Instance;
    var fallbackAttIds = request.Attachments.Select(a => a.Id).ToList();
    var jsonOpts = new JsonDocumentOptions
    {
      AllowTrailingCommas = true,
      CommentHandling = JsonCommentHandling.Skip
    };

    var elements = new List<JsonElement>();
    var idx = 0;
    while (idx < text.Length)
    {
      var start = text.IndexOf(startTag, idx, StringComparison.OrdinalIgnoreCase);
      if (start < 0)
      {
        break;
      }

      var end = text.IndexOf(endTag, start + startTag.Length, StringComparison.OrdinalIgnoreCase);
      if (end < 0)
      {
        break;
      }

      var json = text[(start + startTag.Length)..end].Trim();
      idx = end + endTag.Length;
      if (!string.IsNullOrWhiteSpace(json))
      {
        try
        {
          using var doc = JsonDocument.Parse(json, jsonOpts);
          elements.Add(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
          warnings.Add(L["AgentProposalStageWarning", L["RequestInvalid"]]);
        }
      }
    }

    if (elements.Count > 0 && request.Services is not null)
    {
      elements = AgentTopologicalSorter.SortInlineProposalsTopologically(elements);

      var turnId = Guid.NewGuid().ToString("N")[..10];
      var store = request.Services.GetService<AgentStore>();
      var sessionId = AgentStore.NormalizeSessionId(request.SessionId);
      var stagedInTurn = new List<AgentProposalDto>();

      foreach (var el in elements)
      {
        try
        {
          var staged = await AgentProposalBuilder.StageProposalAsync(el, request.Services, ct, fallbackAttIds, turnId);
          stagedInTurn.Add(staged);
        }
        catch (FormRefusedException refusedEx)
        {
          var firstMsg = refusedEx.Messages.FirstOrDefault();
          var reason = firstMsg?.Detail ?? firstMsg?.Title ?? refusedEx.Title;
          warnings.Add(L["AgentProposalStageWarning", reason]);
        }
        catch (InvalidOperationException invEx)
        {
          warnings.Add(L["AgentProposalStageWarning", invEx.Message]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          warnings.Add(L["AgentProposalStageWarning", L["AgentErrorSave"]]);
        }
      }

      if (store != null && stagedInTurn.Count > 0)
      {
        var editedRecords = new List<(string Service, long RecordId)>();
        var createdNames = new List<(string Service, string Name)>();
        foreach (var p in stagedInTurn)
        {
          var normSvc = AgentFeatureRegistry.NormalizeServiceKey(p.Service);
          if (p.RecordId is > 0)
          {
            editedRecords.Add((normSvc, p.RecordId.Value));
          }
          else if (AgentStepGraphHelper.GetProposalShortLabel(p) is { Length: > 0 } shortLabel)
          {
            createdNames.Add((normSvc, shortLabel));
          }
        }

        store.SupersedeOverlappingTurnProposals(sessionId, request.Owner, turnId, editedRecords, createdNames);
        store.FinalizeTurnProposals(sessionId, request.Owner, turnId);
      }
    }

    var savedMemories = await AgentMemoryService.SaveInlineMemoriesAsync(text, request, ct);
    return (warnings, savedMemories);
  }

  public static Task<IReadOnlyList<AgentSavedMemoryDto>> SaveInlineMemoriesAsync(
    string text,
    AgentTurnRequest request,
    CancellationToken ct) =>
    AgentMemoryService.SaveInlineMemoriesAsync(text, request, ct);

  private static void AppendSystemInstructions(StringBuilder sb, string sessionId, string langDisplay)
  {
    sb.AppendLine("[SYSTEM INSTRUCTIONS FOR PLATFORM AI ASSISTANT]");
    sb.AppendLine($"SessionId: {sessionId}");
    sb.AppendLine($"Preferred Language: {langDisplay}");
    sb.AppendLine("You are the built-in AI assistant of the platform application.");
    sb.AppendLine("Your job is to help the user query analytics/records and stage confirmation proposals using the available `forms`, `dictionaries`, `largeDictionaries`, and `recentRecords` in the Context JSON below.");
    sb.AppendLine("IMPORTANT RULES:");
    sb.AppendLine("1. NEVER save records directly without user confirmation. Stage proposal card(s) by outputting `<ui_proposal>` JSON block(s) (or calling `platform_proposal_stage`).");
    sb.AppendLine("2. CRITICAL TOOL RESTRICTION: NEVER call `run_command`, `bash`, `grep_search`, `find_by_name`, `list_dir`, or browser tools! You may ONLY call `read_file` / `Read` on exact paths listed below and the `platform_*` MCP tools (`platform_catalog_list`, `platform_catalog_describe`, `platform_records_query`, `platform_proposal_stage`).");
    sb.AppendLine("3. If files are listed in `[ATTACHED FILES]`, inspect each `AbsolutePath` via `read_file` / `Read` and cross-reference `recentRecords` before proposing changes.");
    sb.AppendLine("4. CREATING, EDITING & MOVING STATUS:");
    sb.AppendLine("   - To CREATE: omit `id` and populate `values`.");
    sb.AppendLine("   - To EDIT: check `recentRecords` first! Include `\"id\": <recordId>` and pass ONLY modified fields in `values`.");
    sb.AppendLine("   - CHILD GRIDS (`children`): For forms with one or more `children` schemas, pass `\"children\": [{ \"childKey\": \"<child.key>\", \"mode\": \"replace\", \"rows\": [{ \"<field.path>\": \"<value>\" }] }]` (`mode`: `\"replace\"` or `\"append\"`; single-child forms also accept `\"lines\"` + `\"linesMode\"`).");
    sb.AppendLine("   - STATUS TRANSITIONS (`transitions`): When a form/grid exposes `transitions`, move a record's status via `\"operation\": \"move-status\"`, `\"service\": \"<service>\"`, `\"id\": <recordId>`, `\"transition\": \"<transition.handle>\"`, and optional `\"ask\": { \"<askField.path>\": \"<value>\" }`.");
    sb.AppendLine("5. REFERENCE FIELDS (`referenceService`): Use numeric `id` from `dictionaries`, exact name for `largeDictionaries`, or stage a prerequisite `<ui_proposal>` with `\"key\": \"<stepKey>\"` and link via `\"$ref:<stepKey>\"` (in `values`, `children[].rows`, or `ask`).");
    sb.AppendLine("6. MULTI-STEP BUNDLES & REVISIONS: Emit dependent `<ui_proposal>` blocks in topological order with `\"key\"` / `\"$ref:<key>\"` (and optional `\"dependsOn\": [\"<stepKey>\"]`). When revising `[CURRENT UNCONFIRMED PROPOSALS]`, output the complete updated bundle.");
    sb.AppendLine("7. `<ui_proposal>` SCHEMA (output directly without markdown code fences):");
    sb.AppendLine(
      """
      <ui_proposal>
      {
        "sessionId": "<SessionId>",
        "key": "<optional stepKey>",
        "operation": "create",
        "service": "<form.service>",
        "id": 123,
        "transition": "<optional transition.handle when operation is move-status>",
        "values": { "<field.path>": "<value or \"$ref:<stepKey>\">" },
        "ask": { "<askField.path>": "<value>" },
        "children": [
          { "childKey": "<child.key>", "mode": "replace", "rows": [{ "<childField.path>": "<value>" }] }
        ],
        "multiSelectMode": "replace",
        "dependsOn": ["<optional stepKey>"],
        "attachmentIds": ["<attachmentId>"]
      }
      </ui_proposal>
      """);
    sb.AppendLine("8. RICH VISUAL BLOCKS (emit without code fences when appropriate):");
    sb.AppendLine("   - `<ui_kpi>[{\"label\": \"...\", \"value\": 125000, \"color\": \"emerald\", \"subtitle\": \"...\"}]</ui_kpi>`");
    sb.AppendLine("   - `<ui_chart>{\"key\": \"c1\", \"title\": \"...\", \"kind\": \"donut\", \"slices\": [{\"name\": \"...\", \"value\": 1000, \"color\": \"blue\"}]}</ui_chart>` or `bar`/`line`/`area` with `categories` + `series`.");
    sb.AppendLine("   - `<ui_table>{\"title\": \"...\", \"columns\": [{\"key\": \"name\", \"label\": \"...\", \"kind\": \"text\"}], \"rows\": [{\"name\": \"...\"}]}</ui_table>` or `<ui_html>...</ui_html>`.");
    sb.AppendLine("   - Allowed `color`: `slate`, `red`, `orange`, `amber`, `emerald`, `teal`, `cyan`, `sky`, `blue`, `indigo`, `violet`, `purple`, `fuchsia`, `pink`, `rose`.");
    sb.AppendLine("9. MEMORY & RULES (`<agent_memory>` vs `<ui_proposal>`):");
    sb.AppendLine("   - For personal user preferences or facts ONLY, emit `<agent_memory>{\"kind\": \"Preference\", \"title\": \"Short title\", \"content\": \"Clear preference or fact\"}</agent_memory>` (`kind`: `\"Preference\"` or `\"Fact\"`). This stages a confirmation pill for the user — tell them they can click Save to remember it, and do not claim it is already saved.");
    sb.AppendLine("   - NEVER emit `<agent_memory>` with `kind: \"Rule\"` or for tenant-wide rules (silent rule writes are rejected). To create or edit a tenant-wide `Rule`, stage a standard `<ui_proposal>` targeting the `AgentMemory` form service when available in `forms`.");
  }

  private static void AppendUnconfirmedSessionProposals(StringBuilder sb, AgentTurnRequest request)
  {
    var store = request.Services?.GetService<AgentStore>();
    var pending = store?.GetSessionProposals(request.SessionId, request.Owner).Where(p => p.Status == "pending").ToList();
    if (pending is not { Count: > 0 })
    {
      return;
    }

    var summaryItems = new List<object>(pending.Count);
    foreach (var prop in pending)
    {
      Dictionary<string, object?>? values = null;
      try
      {
        var payload = prop.Payload.Deserialize<StagedProposalPayload>(WebJson);
        if (payload != null)
        {
          values = payload.ExplicitFields.Count > 0
            ? payload.ExplicitFields
              .Where(k => payload.Values.ContainsKey(k))
              .ToDictionary(k => k, k => payload.Values[k], StringComparer.OrdinalIgnoreCase)
            : payload.Values;

          foreach (var nr in payload.NewReferences.Where(r => r.LineIndex == null && !string.IsNullOrEmpty(r.StepKey)))
          {
            values ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            values[nr.FieldPath] = $"$ref:{nr.StepKey}";
          }
        }
      }
      catch { }

      summaryItems.Add(new
      {
        step = prop.StepNumber,
        key = prop.StepKey,
        operation = prop.Operation,
        service = prop.Service,
        transition = prop.Transition,
        id = prop.RecordId,
        title = prop.Title,
        values
      });
    }

    sb.AppendLine();
    sb.AppendLine("[CURRENT UNCONFIRMED PROPOSALS IN THIS SESSION — IF USER ASKS TO REVISE/ADJUST THEM, EMIT THE COMPLETE UPDATED BUNDLE]");
    sb.AppendLine(JsonSerializer.Serialize(summaryItems, WebJson));
  }

  private static string TruncateUtf8(string text, int maxBytes)
  {
    if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
    {
      return text;
    }

    var bytes = Encoding.UTF8.GetBytes(text);
    var len = maxBytes;
    while (len > 0 && (bytes[len] & 0b1100_0000) == 0b1000_0000)
    {
      len--;
    }

    return Encoding.UTF8.GetString(bytes, 0, len);
  }
}
