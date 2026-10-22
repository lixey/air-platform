global using Air.App.Contracts;

namespace Air.Agent;

public sealed class AgentOptions
{
  public HashSet<string> EnabledProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "Agy" };
  public string DefaultProvider { get; set; } = "Agy";
  public string DefaultModel { get; set; } = "gemini-3.8-flash-low";
  public int MaxFullDictionarySize { get; set; } = 50;
  public TimeSpan SessionTtl { get; set; } = TimeSpan.FromHours(2);
  public string? DomainKnowledge { get; set; }
}

public sealed record AgentOwnerScope(long TenantId, long? UserId);

public sealed record AgentTurnRequest
{
  public required string SessionId { get; init; }
  public required AgentOwnerScope Owner { get; init; }
  public required string Message { get; init; }
  public IReadOnlyList<AgentAttachmentDto> Attachments { get; init; } = [];
  public string Locale { get; init; } = "uk";
  public string? ProviderHandle { get; init; }
  public string? Model { get; init; }
  public IServiceProvider Services { get; init; } = null!;
}

public interface IAgentProvider
{
  string Handle { get; }
  string Name { get; }
  bool IsAvailable { get; }
  Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<AgentModelDto>>([]);
  IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(AgentTurnRequest request, CancellationToken ct);
}

internal sealed class StagedProposalPayload
{
  public string Service { get; set; } = string.Empty;
  public long? RecordId { get; set; }
  public Dictionary<string, object?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  public List<PendingNewReference> NewReferences { get; set; } = [];
  public string? ChildFormService { get; set; }
  public bool HasLinesUpdate { get; set; }
  public string? LinesMode { get; set; }
  public List<Dictionary<string, object?>> Lines { get; set; } = [];
}

internal sealed class PendingNewReference
{
  public string FieldPath { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string? FormService { get; set; }
  public bool IsMultiple { get; set; }
  public string? ProposalId { get; set; }
  public Dictionary<string, object?> ExtraValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
