namespace Platform.Agent.Contracts;

public sealed record AgentTurnRequest
{
  public required string SessionId { get; init; }
  public required AgentOwnerScope Owner { get; init; }
  public required string Message { get; init; }
  public IReadOnlyList<AgentAttachmentDto> Attachments { get; init; } = [];
  public string Locale { get; init; } = "uk";
  public string? ProviderHandle { get; init; }
  public string? Model { get; init; }
  public string? ModelName { get; init; }
  public IServiceProvider Services { get; init; } = null!;
}
