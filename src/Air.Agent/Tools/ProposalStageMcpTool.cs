namespace Platform.Agent.Tools;

public sealed class ProposalStageMcpTool : IMcpTool
{
  public string Name => "air_proposal_stage";

  public string Description =>
    "Stages a create or edit confirmation proposal card in the user's chat session without mutating database records directly.";

  public object InputSchema => new
  {
    type = "object",
    required = new[] { "service", "values" },
    properties = new
    {
      sessionId = new { type = "string" },
      service = new { type = "string" },
      id = new { type = "integer" },
      key = new { type = "string" },
      values = new { type = "object" },
      lines = new
      {
        type = "array",
        items = new { type = "object" }
      },
      linesMode = new
      {
        type = "string",
        @enum = new[] { "replace", "append" }
      }
    }
  };

  public async Task<object?> ExecuteAsync(JsonElement arguments, IServiceProvider services, CancellationToken ct) =>
    await AgentProposalBuilder.StageProposalAsync(arguments, services, ct);
}
