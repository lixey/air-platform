namespace Platform.Agent.Tools;

public sealed class ProposalStageMcpTool : IMcpTool
{
  public string Name => "platform_proposal_stage";

  public string Description =>
    "Stages a create, update, or status-transition confirmation proposal card in the user's chat session without mutating database records directly.";

  public object InputSchema => new
  {
    type = "object",
    required = new[] { "service" },
    properties = new
    {
      sessionId = new { type = "string" },
      service = new { type = "string" },
      operation = new
      {
        type = "string",
        @enum = new[] { "create", "update", "move-status" }
      },
      id = new { type = "integer" },
      transition = new { type = "string" },
      key = new { type = "string" },
      values = new { type = "object" },
      ask = new { type = "object" },
      children = new
      {
        type = "array",
        items = new
        {
          type = "object",
          properties = new
          {
            childKey = new { type = "string" },
            mode = new { type = "string", @enum = new[] { "replace", "append" } },
            rows = new { type = "array", items = new { type = "object" } }
          }
        }
      },
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
