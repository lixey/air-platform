namespace Platform.Agent.Tools;

public sealed class RecordsQueryMcpTool : IMcpTool
{
  public string Name => "platform_records_query";

  public string Description =>
    "Queries records (bounded paginated search up to 100 rows per page) or reads a single record with its child grids by ID from an agent-enabled grid or form.";

  public object InputSchema => new
  {
    type = "object",
    required = new[] { "service" },
    properties = new
    {
      service = new { type = "string" },
      id = new { type = "integer" },
      search = new { type = "string" },
      page = new { type = "integer", minimum = 0 },
      limit = new { type = "integer", minimum = 1, maximum = 100 }
    }
  };

  public Task<object?> ExecuteAsync(JsonElement arguments, IServiceProvider services, CancellationToken ct) =>
    AgentContextQueryService.QueryRecordsAsync(arguments, services, ct);
}
