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
    CancellationToken ct = default
  )
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

    sb.AppendLine("[SYSTEM INSTRUCTIONS FOR PLATFORM AI ASSISTANT]");
    sb.AppendLine($"SessionId: {request.SessionId}");
    sb.AppendLine($"Preferred Language: {langDisplay}");
    sb.AppendLine("You are the built-in AI assistant of the Air Platform application.");
    sb.AppendLine(
      "Your job is to help the user create new records or edit existing records using the available `forms`, `dictionaries`, `largeDictionaries`, and `recentRecords` in the Context JSON below from natural language or attached files/images."
    );
    sb.AppendLine("IMPORTANT RULES:");
    sb.AppendLine(
      "1. NEVER save records directly without user confirmation. Instead, prepare proposal card(s) by outputting `<air_proposal>` JSON block(s) in your response."
    );
    sb.AppendLine(
      "2. CRITICAL TOOL RESTRICTION: NEVER call `run_command`, `bash`, `grep_search`, `find_by_name`, `list_dir`, or any browser tools! Shell commands are blocked by the sandbox and calling `run_command` will immediately crash and abort your response. You may ONLY call `read_file` (or `view_file` / `Read`) on the exact `AbsolutePath` listed in `[ATTACHED FILES]`, `[EXTENDED WORKSPACE RECORDS]`, or `[AVAILABLE FORMS...]` below, and (when available in your tool list) the built-in `air_*` MCP tools (`air_catalog_list`, `air_catalog_describe`, `air_records_query`, `air_proposal_stage`)."
    );
    sb.AppendLine(
      "3. If the user attached image/document file(s) listed in `[ATTACHED FILES]` below, call `read_file` / `Read` on each `AbsolutePath` once (unless the image is already attached in your vision input) to read all field values and line items matching the target form schema. Cross-reference attached screenshots/documents against `recentRecords` (and `[EXTENDED WORKSPACE RECORDS]` if older rows or `largeDictionaries` entries are needed) before proposing changes."
    );
    sb.AppendLine("4. CREATING vs EDITING RECORDS:");
    sb.AppendLine("   - To CREATE a new record: omit `id` in `<air_proposal>` and populate the required fields in `values`.");
    sb.AppendLine(
      "   - To EDIT an existing record: ALWAYS check `recentRecords` (and `[EXTENDED WORKSPACE RECORDS]` when needed) first! When a record already exists (such as an imported statement row, document, or dictionary record), NEVER create a duplicate — include `\"id\": <recordId>` in `<air_proposal>` and pass ONLY the fields being modified in `values` (all other existing fields are automatically preserved and diffed as `old → new`). For polymorphic records in `recentRecords` (records that include a `\"form\"` property), use that record's `form` service."
    );
    sb.AppendLine(
      "   - When editing child `lines`, set `\"linesMode\": \"replace\"` (default) to replace all lines or `\"linesMode\": \"append\"` to add lines to existing ones. When editing multi-select fields, set `\"multiSelectMode\": \"append\"` to add items or `\"multiSelectMode\": \"replace\"` (default) to replace them."
    );
    sb.AppendLine("5. REFERENCE FIELDS (`referenceService`):");
    sb.AppendLine(
      "   - Small dictionaries (<= 50 rows) are preloaded in full in `dictionaries[referenceService]`. If an existing record matches by name (respecting `referenceFilter` if present), set the field to its numeric `id`."
    );
    sb.AppendLine(
      "   - Large dictionaries (> 50 rows, listed in `largeDictionaries`) include keyword-matched rows in `dictionaries[referenceService]`. If you know an existing entity's name and do not need custom fields on a new record, pass its numeric `id` or exact name string (the server searches the database automatically); when creating a new referenced entity with specific fields (or multiple distinct records), stage an explicit prerequisite `<air_proposal>` with `\"key\"` and link to it via `\"$ref:<key>\"`."
    );
    sb.AppendLine("6. MULTI-STEP LINKED BUNDLES (`\"key\"` & `\"$ref:<key>\"`) — 1-CLICK ATOMIC CONFIRMATION:");
    sb.AppendLine(
      "   - When a workflow requires multiple dependent creates and/or edits (e.g. creating or updating a master/reference entity → creating dependent parent/child records → EDITING existing records to link them to the newly created IDs), emit all `<air_proposal>` blocks in topological order (prerequisites first, dependents after)."
    );
    sb.AppendLine(
      "   - Assign each prerequisite `<air_proposal>` a unique `\"key\": \"<stepKey>\"` (e.g. `\"ref_1\"`, `\"doc_1\"`, `\"item_1\"`) and reference it in any downstream proposal's reference field (or `lines` reference field) via `\"$ref:<stepKey>\"`. If two distinct records have the same title or amount, give them distinct keys (`\"doc_1\"`, `\"doc_2\"`) so each dependent links to the exact intended record. You may also set `\"dependsOn\": [\"<stepKey>\"]` when an edit must run after an earlier step."
    );
    sb.AppendLine(
      "   - All `<air_proposal>` blocks emitted in a single turn are presented as a numbered step chain with a single **Confirm All** button and committed together in one atomic database transaction."
    );
    sb.AppendLine("7. ITERATIVE REVISION OF UNCONFIRMED PROPOSALS:");
    sb.AppendLine(
      "   - If `[CURRENT UNCONFIRMED PROPOSALS IN THIS SESSION]` appears below and the user asks to adjust, fix, rename, add, or remove parts of the proposed changes, output the **complete revised `<air_proposal>` bundle** (including unchanged steps from that bundle alongside your revised/added steps, keeping `\"key\"` and `\"$ref:<key>\"` links intact) so the user can still confirm everything in 1 click, while the server automatically marks the previous unconfirmed bundle as superseded."
    );
    sb.AppendLine(
      "8. When creating a new record, if a required reference field is not specified by the user, pick the most appropriate default from `dictionaries` and mention it briefly."
    );
    sb.AppendLine(
      "9. If the user asks an informational question without asking to create or edit a record, answer clearly and concisely in the Preferred Language and do NOT output `<air_proposal>`. When the user asks to create or edit record(s), respond with a clear, concise summary in the Preferred Language AND include one or more `<air_proposal>` JSON blocks at the end of your response using this universal schema:"
    );
    sb.AppendLine(
      """
      <air_proposal>
      {
        "sessionId": "<SessionId>",
        "key": "<optional unique stepKey when referenced by downstream steps>",
        "service": "<form.service from Context JSON>",
        "id": 123,
        "values": {
          "<field.path>": "<value, numeric id, or \"$ref:<stepKey>\">"
        },
        "lines": [
          { "<lineField.path>": "<value>" }
        ],
        "linesMode": "replace",
        "multiSelectMode": "replace",
        "dependsOn": ["<optional stepKey>"],
        "attachmentIds": ["<attachmentId>"]
      }
      </air_proposal>
      """
    );
    sb.AppendLine(
      "(Omit `key`, `id`, `lines`, `linesMode`, `multiSelectMode`, `dependsOn`, and `attachmentIds` when not applicable. Include `id` ONLY when editing an existing record.)"
    );
    sb.AppendLine("Do NOT wrap `<air_proposal>` in markdown code fences (` ``` `) — output `<air_proposal>{...}</air_proposal>` directly.");
    sb.AppendLine("10. RICH STRUCTURED RESPONSES (MARKDOWN, CHARTS, TABLES, KPIs, HTML):");
    sb.AppendLine("   - Always format prose with clean Markdown (`###` headings, `-` bullet lists, `**bold**` key figures).");
    sb.AppendLine("   - Use the aggregated `reports`, `dictionaries`, and `recentRecords` in the Context JSON for analytical answers.");
    sb.AppendLine(
      "   - For summary metrics, emit `<air_kpi>[{\"label\": \"...\", \"value\": 125000, \"color\": \"emerald\", \"subtitle\": \"...\"}]</air_kpi>`."
    );
    sb.AppendLine(
      "   - For charts/diagrams (when asked for a chart/graph or visual breakdown), emit `<air_chart>{...}</air_chart>` (without code fences) using:"
    );
    sb.AppendLine(
      "     * Donut/Pie: `{\"key\": \"c1\", \"title\": \"...\", \"kind\": \"donut\", \"slices\": [{\"name\": \"...\", \"value\": 1000, \"color\": \"blue\"}]}`"
    );
    sb.AppendLine(
      "     * Bar/Horizontal-Bar/Stacked-Bar/Line/Area: `{\"key\": \"c1\", \"title\": \"...\", \"kind\": \"bar\", \"categories\": [\"Jan\", \"Feb\"], \"series\": [{\"name\": \"...\", \"kind\": \"bar\", \"color\": \"emerald\", \"data\": [100, 200]}]}`"
    );
    sb.AppendLine(
      "   - For tables, either use a standard Markdown pipe table (`| Col | Value |`) OR emit `<air_table>{\"title\": \"...\", \"columns\": [{\"key\": \"date\", \"label\": \"...\", \"kind\": \"date\"}, {\"key\": \"name\", \"label\": \"...\", \"kind\": \"text\"}, {\"key\": \"sum\", \"label\": \"...\", \"kind\": \"money\", \"decimals\": 2}], \"rows\": [{\"date\": \"2026-11-01\", \"name\": \"...\", \"sum\": 1000}], \"bottomRows\": [{\"name\": \"Total\", \"sum\": 1000}]}</air_table>` (`kind`: `text`, `date`, `datetime`, `badge`, `money`, `number`, `percent`)."
    );
    sb.AppendLine(
      "   - Allowed `color` values: `slate`, `red`, `orange`, `amber`, `emerald`, `teal`, `cyan`, `sky`, `blue`, `indigo`, `violet`, `purple`, `fuchsia`, `pink`, `rose`."
    );
    sb.AppendLine("11. PERSONAL USER MEMORY (`<air_memory>`):");
    sb.AppendLine(
      "   - When the user explicitly asks you to remember a personal preference or domain fact for future sessions, confirm in your message AND emit `<air_memory>{\"kind\": \"Preference\", \"title\": \"Short title\", \"content\": \"Clear preference or fact\"}</air_memory>` (where `kind` is `\"Preference\"` or `\"Fact\"`)."
    );

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
        : request.Message
    );

    await AppendDatabaseMemoryAndDomainKnowledgeAsync(sb, request, ct);

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
        if (fullContextFile != null && File.Exists(fullContextFile))
        {
          sb.AppendLine($"[AVAILABLE FORMS, DICTIONARIES, AND RECENT RECORDS CONTEXT JSON — FULL CONTEXT SAVED IN \"{fullContextFile}\"]");
        }
        else
        {
          sb.AppendLine("[AVAILABLE FORMS, DICTIONARIES, AND RECENT RECORDS CONTEXT JSON]");
        }

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
        sb.AppendLine(
          $"[AVAILABLE FORMS, DICTIONARIES, AND RECENT RECORDS CONTEXT — FULL CONTEXT SAVED IN \"{contextPath}\" (USE read_file IF NEEDED)]"
        );
        var budgetBytes = Math.Max(4096, MaxInlinePromptBytes - currentBytes - 256);
        sb.AppendLine(TruncateUtf8(contextJson, budgetBytes));
      }
    }

    return sb.ToString();
  }

  private static async Task AppendDatabaseMemoryAndDomainKnowledgeAsync(
    StringBuilder sb,
    AgentTurnRequest request,
    CancellationToken ct
  )
  {
    var options = request.Services?.GetService<AgentOptions>();
    var maxChars = options?.MaxMemoryChars ?? 12_000;
    var db = request.Services?.GetService<PlatformDbContext>();
    var hasDbSeed = false;

    if (db is not null)
    {
      try
      {
        var ownerTenantId = request.Owner.TenantId;
        var ownerUserId = request.Owner.UserId;
        var memories = await db.AppAgentMemory
          .IgnoreQueryFilters()
          .AsNoTracking()
          .Where(m => m.IsActive &&
                      (m.TenantId == null || ownerTenantId != null && m.TenantId == ownerTenantId) &&
                      (m.UserId == null || ownerUserId != null && m.UserId == ownerUserId))
          .OrderByDescending(m => m.Kind == AgentMemoryKinds.Rule && m.UserId == null)
          .ThenByDescending(m => m.IsPinned)
          .ThenByDescending(m => m.Priority)
          .ThenBy(m => m.Id)
          .ToListAsync(ct);

        if (memories.Count > 0)
        {
          var usedChars = 0;
          var rules = new List<AppAgentMemory>();
          var knowledge = new List<AppAgentMemory>();

          foreach (var mem in memories)
          {
            var entryLen = mem.Title.Length + mem.Content.Length + 16;
            if (usedChars + entryLen > maxChars)
            {
              continue;
            }

            usedChars += entryLen;
            if (mem.UserId == null && string.Equals(mem.Kind, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase))
            {
              rules.Add(mem);
            }
            else
            {
              if (string.Equals(mem.Kind, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase))
              {
                hasDbSeed = true;
              }

              knowledge.Add(mem);
            }
          }

          if (rules.Count > 0)
          {
            sb.AppendLine();
            sb.AppendLine("[MANDATORY AI RULES & POLICIES (FROM DATABASE)]");
            foreach (var r in rules)
            {
              sb.AppendLine($"- [{r.Title}]: {r.Content}");
            }
          }

          if (knowledge.Count > 0)
          {
            sb.AppendLine();
            sb.AppendLine("[PERSISTENT AI MEMORY & DOMAIN KNOWLEDGE (FROM DATABASE)]");
            foreach (var k in knowledge)
            {
              sb.AppendLine($"### {k.Title} ({k.Kind})");
              sb.AppendLine(k.Content);
            }
          }
        }
      }
      catch
      {
        // Ignore if table is not yet migrated
      }
    }

    if (!hasDbSeed)
    {
      var registeredEntries = AgentMemoryCatalog.DomainKnowledgeEntries;
      if (registeredEntries.Count > 0)
      {
        sb.AppendLine();
        sb.AppendLine("[APPLICATION DOMAIN KNOWLEDGE]");
        foreach (var entry in registeredEntries)
        {
          sb.AppendLine($"### {entry.Title}");
          sb.AppendLine(entry.Content);
        }
      }
      else
      {
        var domainKnowledge = options?.DomainKnowledge ?? AirAgentRuntime.SystemPromptSupplement;
        if (!string.IsNullOrWhiteSpace(domainKnowledge))
        {
          sb.AppendLine();
          sb.AppendLine("[APPLICATION DOMAIN KNOWLEDGE]");
          sb.AppendLine(domainKnowledge);
        }
      }
    }
  }

  private static void AppendUnconfirmedSessionProposals(StringBuilder sb, AgentTurnRequest request)
  {
    var store = request.Services?.GetService<AgentStore>();
    if (store is null)
    {
      return;
    }

    var pending = store.GetSessionProposals(request.SessionId, request.Owner)
      .Where(p => p.Status == "pending")
      .ToList();
    if (pending.Count == 0)
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

      summaryItems.Add(
        new
        {
          step = prop.StepNumber,
          key = prop.StepKey,
          service = prop.Service,
          id = prop.RecordId,
          title = prop.Title,
          values
        }
      );
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

  public static async Task<IReadOnlyList<string>> StageInlineProposalsAsync(
    string text,
    AgentTurnRequest request,
    CancellationToken ct
  )
  {
    const string startTag = "<air_proposal>";
    const string endTag = "</air_proposal>";
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
      elements = AgentStepGraphHelper.SortInlineProposalsTopologically(elements);

      var turnId = Guid.NewGuid().ToString("N")[..10];
      var store = request.Services.GetService<AgentStore>();
      var sessionId = AgentStore.NormalizeSessionId(request.SessionId);
      var stagedInTurn = new List<AgentProposalDto>();

      foreach (var el in elements)
      {
        try
        {
          var staged = await AgentProposalBuilder.StageProposalAsync(
            el,
            request.Services,
            ct,
            fallbackAttIds,
            turnId
          );
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

        store.SupersedeOverlappingTurnProposals(
          sessionId,
          request.Owner,
          turnId,
          editedRecords,
          createdNames
        );
        store.FinalizeTurnProposals(sessionId, request.Owner, turnId);
      }
    }

    await SaveInlineMemoriesAsync(text, request, ct);
    return warnings;
  }

  public static async Task SaveInlineMemoriesAsync(
    string text,
    AgentTurnRequest request,
    CancellationToken ct
  )
  {
    const string startTag = "<air_memory>";
    const string endTag = "</air_memory>";
    if (string.IsNullOrWhiteSpace(text) || text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase) < 0)
    {
      return;
    }

    var db = request.Services?.GetService<PlatformDbContext>();
    var app = request.Services?.GetService<PlatformAppService>();
    if (db is null || app is null || app.User is not { IsAuthenticated: true, UserId: > 0 })
    {
      return;
    }

    var userId = app.User.UserId.Value;
    var tenantId = db.TenantContext.TenantId;
    if (db.SetOfBase<BaseAppTenant>() != null && tenantId is null or <= 0)
    {
      return;
    }

    const int maxPersonalMemories = 100;
    var personalCount = await db.AppAgentMemory
      .IgnoreQueryFilters()
      .CountAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);

    var jsonOpts = new JsonDocumentOptions
    {
      AllowTrailingCommas = true,
      CommentHandling = JsonCommentHandling.Skip
    };

    var idx = 0;
    var hasChanges = false;
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

      if (string.IsNullOrWhiteSpace(json))
      {
        continue;
      }

      try
      {
        using var doc = JsonDocument.Parse(json, jsonOpts);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
          continue;
        }

        var title = root.TryGetProperty("title", out var tProp) && tProp.ValueKind == JsonValueKind.String
          ? tProp.GetString()?.Trim()
          : null;
        var content = root.TryGetProperty("content", out var cProp) && cProp.ValueKind == JsonValueKind.String
          ? cProp.GetString()?.Trim()
          : null;
        var rawKind = root.TryGetProperty("kind", out var kProp) && kProp.ValueKind == JsonValueKind.String
          ? kProp.GetString()?.Trim()
          : null;

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
        {
          continue;
        }

        if (title.Length > 200)
        {
          title = title[..200];
        }

        if (content.Length > 8000)
        {
          content = content[..8000];
        }

        // Inline <air_memory> from assistant turns is strictly personal (Preference or Fact) — never tenant/global Rule or Seed.
        var kind = string.Equals(rawKind, AgentMemoryKinds.Fact, StringComparison.OrdinalIgnoreCase)
          ? AgentMemoryKinds.Fact
          : AgentMemoryKinds.Preference;

        var existing = await db.AppAgentMemory
          .IgnoreQueryFilters()
          .FirstOrDefaultAsync(
            m => m.Title == title && m.TenantId == tenantId && m.UserId == userId,
            ct
          );
        if (existing is not null)
        {
          if (existing.IsPinned ||
              string.Equals(existing.Kind, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(existing.Kind, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase))
          {
            continue;
          }

          existing.Kind = kind;
          existing.Content = content;
          existing.IsActive = true;
        }
        else
        {
          if (personalCount >= maxPersonalMemories)
          {
            continue;
          }

          personalCount++;
          db.AppAgentMemory.Add(
            new AppAgentMemory
            {
              TenantId = tenantId,
              UserId = userId,
              Kind = kind,
              Title = title,
              Content = content,
              Priority = 100,
              IsPinned = false,
              IsActive = true
            }
          );
        }

        hasChanges = true;
      }
      catch
      {
        // Ignore malformed memory blocks
      }
    }

    if (hasChanges)
    {
      try
      {
        await db.SaveChangesAsync(ct);
      }
      catch
      {
        // Ignore persistence errors if table is unavailable
      }
    }
  }
}
