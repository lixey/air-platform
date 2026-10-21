using System.Text.Json;
using System.Text.Json.Serialization;

namespace Air.Agent;

public sealed record AgentAttachmentDto
{
  public required string Id { get; init; }
  public required string FileName { get; init; }
  public required string ContentType { get; init; }
  public required long Size { get; init; }
  public required string PreviewUrl { get; init; }
  [JsonIgnore]
  public string LocalPath { get; init; } = string.Empty;
}

public sealed record AgentProposalFieldDto(
  string Label,
  string Value,
  string? Badge = null,
  string? Color = null);

public sealed record AgentProposalLineDto(
  string Name,
  decimal Qty,
  decimal Price,
  decimal Amount,
  string? Category = null);

public sealed record AgentProposalDto
{
  public required string Id { get; init; }
  public required string SessionId { get; init; }
  public required string Kind { get; init; }
  public required string Title { get; init; }
  public string? Subtitle { get; init; }
  public string? Badge { get; init; }
  public string? BadgeColor { get; init; }
  public IReadOnlyList<AgentProposalFieldDto> Fields { get; init; } = [];
  public IReadOnlyList<AgentProposalLineDto> Lines { get; init; } = [];
  public IReadOnlyList<string> AttachmentIds { get; init; } = [];
  public JsonElement Payload { get; init; }
  public string Status { get; set; } = "pending";
  public long? CommittedRecordId { get; set; }
  public string? CommittedLabel { get; set; }
  public string? FormService { get; set; }
}

public sealed record AgentCommitResultDto
{
  public required string ProposalId { get; init; }
  public required long RecordId { get; init; }
  public required string RecordLabel { get; init; }
  public required string FormService { get; init; }
  public IReadOnlyList<string> RefreshServices { get; init; } = [];
}

public sealed record AgentStreamEvent
{
  public required string Type { get; init; } // "status" | "delta" | "proposal" | "done" | "error"
  public string? Text { get; init; }
  public AgentProposalDto? Proposal { get; init; }
  public string? SessionId { get; init; }
}

public sealed record AgentTurnRequest
{
  public required string SessionId { get; init; }
  public required string Message { get; init; }
  public IReadOnlyList<AgentAttachmentDto> Attachments { get; init; } = [];
  public string Locale { get; init; } = "uk";
  public string ServerBaseUrl { get; init; } = "http://localhost:5095";
  public string? ProviderHandle { get; init; }
  public IServiceProvider Services { get; init; } = null!;
}

public interface IAgentProvider
{
  string Handle { get; }
  string Name { get; }
  bool IsAvailable { get; }
  IAsyncEnumerable<AgentStreamEvent> StreamTurnAsync(AgentTurnRequest request, CancellationToken ct);
}

public interface IAgentProposalCommitter
{
  string Kind { get; }
  Task<AgentCommitResultDto> CommitAsync(AgentProposalDto proposal, IServiceProvider services, CancellationToken ct);
}
