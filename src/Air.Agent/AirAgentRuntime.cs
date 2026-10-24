namespace Platform.Agent;

public static class AirAgentRuntime
{
  public static string? SystemPromptSupplement { get; set; }

  public static IReadOnlyList<GridCreateDescriptor> DiscoverForms(PlatformDbContext db, PlatformAppService app) =>
    AgentFeatureRegistry.DiscoverForms(db, app);

  public static Task<object?> BuildContextAsync(
    IServiceProvider services,
    string? userMessage = null,
    CancellationToken ct = default,
    bool includeReports = true,
    bool includeRecentRecords = true) =>
    AgentContextQueryService.BuildContextAsync(services, userMessage, ct, includeReports, includeRecentRecords);

  public static Task<object?> QueryRecordsAsync(
    JsonElement args,
    IServiceProvider services,
    CancellationToken ct) =>
    AgentContextQueryService.QueryRecordsAsync(args, services, ct);

  public static Task<AgentProposalDto> StageProposalAsync(
    JsonElement args,
    IServiceProvider services,
    CancellationToken ct,
    IReadOnlyList<string>? fallbackAttachmentIds = null) =>
    AgentProposalBuilder.StageProposalAsync(args, services, ct, fallbackAttachmentIds);

  public static Task<AgentCommitResultDto> CommitProposalAsync(
    AgentProposalDto proposal,
    IServiceProvider services,
    CancellationToken ct) =>
    AgentProposalExecutor.CommitProposalAsync(proposal, services, ct);
}
