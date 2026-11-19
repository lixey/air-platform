namespace Platform.Agent.Internals;

internal static class AgentMemoryTagParser
{
  private const string StartTag = "<agent_memory>";
  private const string EndTag = "</agent_memory>";

  private static readonly JsonDocumentOptions JsonOpts = new()
  {
    AllowTrailingCommas = true,
    CommentHandling = JsonCommentHandling.Skip
  };

  public static bool ContainsTag(string? text) =>
    AgentTagBlockExtractor.ContainsTagOutsideInlineCode(text, StartTag);

  public static List<(string Kind, string Title, string Content)> ParsePersonalCandidates(string text)
  {
    var candidates = new List<(string Kind, string Title, string Content)>();

    foreach (var json in AgentTagBlockExtractor.ExtractInnerBlocks(text, StartTag, EndTag))
    {
      try
      {
        using var doc = JsonDocument.Parse(json, JsonOpts);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
          continue;
        }

        var title = ReadTrimmedString(root, "title");
        var content = ReadTrimmedString(root, "content");
        var rawKind = ReadTrimmedString(root, "kind");
        var rawCategory = ReadTrimmedString(root, "category");
        var rawScope = ReadTrimmedString(root, "scope");

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
        {
          continue;
        }

        // Inline <agent_memory> blocks may never propose tenant/global Rules or Seeds.
        if (IsForbiddenScopeOrKind(rawKind, rawCategory, rawScope, root))
        {
          continue;
        }

        var kind = ResolvePersonalKind(!string.IsNullOrWhiteSpace(rawKind) ? rawKind : rawCategory);
        if (kind == null)
        {
          continue;
        }

        title = title.Length > 200 ? title[..200] : title;
        content = content.Length > 8000 ? content[..8000] : content;
        candidates.Add((kind, title, content));
      }
      catch
      {
        // Ignore malformed memory blocks
      }
    }

    return candidates;
  }

  public static bool IsProtectedRow(AppAgentMemory row) =>
    row.IsPinned ||
    string.Equals(row.Kind, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(row.Kind, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase);

  private static string? ReadTrimmedString(JsonElement root, string name) =>
    root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString()?.Trim() : null;

  private static bool IsForbiddenScopeOrKind(string? rawKind, string? rawCategory, string? rawScope, JsonElement root) =>
    string.Equals(rawKind, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(rawKind, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(rawCategory, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(rawCategory, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(rawScope, "tenant", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(rawScope, "global", StringComparison.OrdinalIgnoreCase) ||
    root.TryGetProperty("userId", out var uProp) && uProp.ValueKind == JsonValueKind.Null;

  private static string? ResolvePersonalKind(string? effectiveKind)
  {
    if (string.Equals(effectiveKind, AgentMemoryKinds.Fact, StringComparison.OrdinalIgnoreCase))
    {
      return AgentMemoryKinds.Fact;
    }

    if (string.IsNullOrWhiteSpace(effectiveKind) ||
        string.Equals(effectiveKind, AgentMemoryKinds.Preference, StringComparison.OrdinalIgnoreCase))
    {
      return AgentMemoryKinds.Preference;
    }

    return null;
  }
}
