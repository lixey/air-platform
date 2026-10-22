namespace Air.Agent;

public static class AgentPromptBuilder
{
  public static string BuildPrompt(AgentTurnRequest request, string? contextJson)
  {
    var sb = new StringBuilder();
    var uiCulture = CultureInfo.CurrentUICulture;
    var langDisplay = string.Equals(uiCulture.EnglishName, uiCulture.NativeName, StringComparison.OrdinalIgnoreCase)
      ? uiCulture.EnglishName
      : $"{uiCulture.EnglishName} ({uiCulture.NativeName})";

    sb.AppendLine("[SYSTEM INSTRUCTIONS FOR PLATFORM AI ASSISTANT]");
    sb.AppendLine($"SessionId: {request.SessionId}");
    sb.AppendLine($"Preferred Language: {langDisplay}");
    sb.AppendLine("You are the built-in AI assistant of the Air Platform application.");
    sb.AppendLine("Your job is to help the user create new records or edit existing records using the available `forms`, `dictionaries`, `largeDictionaries`, and `recentRecords` in the Context JSON below from natural language or attached photos/receipts.");
    sb.AppendLine("IMPORTANT RULES:");
    sb.AppendLine("1. NEVER save records directly without user confirmation. Instead, prepare proposal card(s) by outputting `<air_proposal>` JSON block(s) in your response.");
    sb.AppendLine("2. Do NOT call external MCP tools. Only call `view_file` if there are attached image/document files listed below.");
    sb.AppendLine("3. If the user attached image/document file(s) listed below, you MUST first call `view_file` on each `AbsolutePath` to read all details (e.g., merchant/counterparty name, date, total amount, and line items with name, qty, price, amount).");
    sb.AppendLine("4. CREATING vs EDITING RECORDS:");
    sb.AppendLine("   - To CREATE a new record: omit `id` in `<air_proposal>` and populate the required fields in `values`.");
    sb.AppendLine("   - To EDIT an existing record: find the target record's numeric `id` in `recentRecords` or `dictionaries`. Include `\"id\": <recordId>` in `<air_proposal>` and pass ONLY the fields being modified in `values` (all other existing fields are automatically preserved and diffed as `old → new`). For polymorphic records in `recentRecords` (such as transactions that include a `\"form\"` property), use that record's `form` service.");
    sb.AppendLine("   - When editing child `lines`, set `\"linesMode\": \"replace\"` (default) to replace all lines or `\"linesMode\": \"append\"` to add lines to existing ones. When editing multi-select fields, set `\"tagsMode\": \"append\"` to add items or `\"tagsMode\": \"replace\"` (default) to replace them.");
    sb.AppendLine("5. REFERENCE FIELDS (`referenceService`):");
    sb.AppendLine("   - Small dictionaries (<= 50 rows) are preloaded in full in `dictionaries[referenceService]`. If an existing record matches by name (respecting `referenceFilter` if present), set the field to its numeric `id`.");
    sb.AppendLine("   - Large dictionaries (> 50 rows, listed in `largeDictionaries`) only include keyword-matched rows in `dictionaries[referenceService]`. If you know the referenced entity's name (for example, a merchant/counterparty name read from a receipt photo or user prompt) and it is not in the preloaded list, simply pass its exact name string in `<air_proposal>` — the server will automatically search the full database for an existing match or stage a new-record proposal if it does not exist yet.");
    sb.AppendLine("6. When creating a new record, if a required reference field is not specified by the user, pick the most appropriate default from `dictionaries` and mention it briefly.");
    sb.AppendLine("7. If the user asks an informational question (e.g. how the system works, how a process or status works, or how it differs from the legacy system) without asking to create or edit a record, answer clearly and concisely in the Preferred Language and do NOT output `<air_proposal>`. When the user asks to create or edit record(s), respond with a short, friendly message (1-3 sentences) in the Preferred Language AND include one or more `<air_proposal>` JSON blocks at the end of your response using this universal schema:");
    sb.AppendLine("""
<air_proposal>
{
  "sessionId": "<SessionId>",
  "service": "<form.service from Context JSON>",
  "id": 123,
  "values": {
    "<field.path>": "<value or id or reference name>"
  },
  "lines": [
    { "name": "Item name", "qty": 1, "price": 100.00, "amount": 100.00, "categoryId": 1 }
  ],
  "linesMode": "replace",
  "tagsMode": "replace",
  "attachmentIds": ["<attachmentId>"]
}
</air_proposal>
""");
    sb.AppendLine("(Omit `id`, `lines`, `linesMode`, `tagsMode`, and `attachmentIds` when not applicable. Include `id` ONLY when editing an existing record.)");
    sb.AppendLine("Do NOT wrap `<air_proposal>` in markdown code fences (` ``` `) — output `<air_proposal>{...}</air_proposal>` directly.");

    var domainKnowledge = request.Services?.GetService<AgentOptions>()?.DomainKnowledge ?? AirAgentRuntime.SystemPromptSupplement;
    if (!string.IsNullOrWhiteSpace(domainKnowledge))
    {
      sb.AppendLine();
      sb.AppendLine("[APPLICATION DOMAIN KNOWLEDGE & LEGACY MAPPING]");
      sb.AppendLine(domainKnowledge);
    }

    if (!string.IsNullOrWhiteSpace(contextJson))
    {
      sb.AppendLine();
      sb.AppendLine("[AVAILABLE FORMS, DICTIONARIES, AND RECENT RECORDS CONTEXT JSON]");
      sb.AppendLine(contextJson);
    }

    if (request.Attachments.Count > 0)
    {
      sb.AppendLine();
      sb.AppendLine("[ATTACHED FILES — USE view_file TO INSPECT]");
      foreach (var att in request.Attachments)
      {
        sb.AppendLine($"- AttachmentId: \"{att.Id}\", FileName: \"{att.FileName}\", AbsolutePath: \"{att.LocalPath}\"");
      }
    }

    sb.AppendLine();
    sb.AppendLine("[USER MESSAGE]");
    sb.AppendLine(string.IsNullOrWhiteSpace(request.Message)
      ? Localizer.Instance["AgentDefaultPhotoPrompt"]
      : request.Message);

    return sb.ToString();
  }

  public static async Task StageInlineProposalsAsync(
    string text,
    AgentTurnRequest request,
    CancellationToken ct)
  {
    const string startTag = "<air_proposal>";
    const string endTag = "</air_proposal>";
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
        catch
        {
          // Ignore malformed JSON block
        }
      }
    }

    if (elements.Count > 1)
    {
      elements =
      [
        .. elements.OrderBy(el =>
        {
          var name = el.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object &&
                     v.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()
            : null;
          if (string.IsNullOrWhiteSpace(name)) { return 1; } var isReferencedByOther = elements.Any(other =>
            !other.Equals(el) &&
            other.TryGetProperty("values", out var ov) && ov.ValueKind == JsonValueKind.Object &&
            ov.EnumerateObject().Any(p =>
              p.Value.ValueKind == JsonValueKind.String &&
              string.Equals(p.Value.GetString()?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)));
          return isReferencedByOther ? 0 : 1; })
      ];
    }

    foreach (var el in elements)
    {
      try
      {
        await AgentProposalBuilder.StageProposalAsync(el, request.Services, ct, fallbackAttIds);
      }
      catch
      {
        // Ignore staging errors for invalid inline blocks
      }
    }
  }
}
