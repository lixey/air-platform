namespace Platform.Agent;

public sealed class AgentOptions
{
  public HashSet<string> EnabledProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "Agy", "Claude", "Codex", "LmStudio" };
  public string DefaultProvider { get; set; } = "Agy";
  public string DefaultModel { get; set; } = AgentModelCatalog.DefaultModelId;
  public int MaxFullDictionarySize { get; set; } = 50;
  public TimeSpan SessionTtl { get; set; } = TimeSpan.FromHours(2);
  public string? DomainKnowledge { get; set; }
  public int MaxMemoryChars { get; set; } = 12_000;
}
