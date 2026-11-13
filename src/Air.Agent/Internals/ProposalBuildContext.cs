namespace Platform.Agent.Internals;

internal sealed record ProposalBuildContext(
  string SessionId,
  string? TurnId,
  AgentOwnerScope Owner,
  AgentStore Store,
  AgentOptions Options,
  IServiceProvider Services,
  PlatformDbContext Db,
  PlatformAppService App,
  Localizer Localizer)
{
  public ProposalBuildContext WithScope(PlatformDbContext db, PlatformAppService app) =>
    this with { Db = db, App = app };

  public AgentProposalDto? FindReferencedProposal(string refKey, string? refService = null) =>
    AgentDuplicateDetector.FindReferencedProposal(Store, SessionId, Owner, TurnId, refKey, refService);
}
