namespace Platform.Agent;

public sealed class AgentOptions
{
  public HashSet<string> EnabledProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "Agy", "Claude", "Codex", "LmStudio" };
  public string DefaultProvider { get; set; } = "Agy";
  public string DefaultModel { get; set; } = string.Empty;
  public int MaxFullDictionarySize { get; set; } = 50;
  public TimeSpan SessionTtl { get; set; } = TimeSpan.FromHours(2);
  public string? DomainKnowledge { get; set; }
  public int MaxMemoryChars { get; set; } = 24_000;
  public int MaxConcurrentTurns { get; set; } = 4;
  public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromMinutes(3);
  public TimeSpan IdleOutputTimeout { get; set; } = TimeSpan.FromSeconds(90);

  public AgentOptions AddDomainKnowledge(string? module, string title, string content, int priority = 150)
  {
    AgentMemoryCatalog.RegisterDomainKnowledge(module, title, content, priority);
    return this;
  }
}
