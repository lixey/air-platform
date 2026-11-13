namespace Platform.Agent.Internals;

internal static class AgentMemoryEndpointHandlers
{
  public static async Task<IResult> HandleMemoryUndoAsync(
    AgentMemoryUndoRequest body,
    HttpContext ctx,
    CancellationToken ct)
  {
    var owner = AgentStore.ResolveOwner(ctx.RequestServices);
    var deleted = await AgentMemoryService.UndoPersonalMemoryAsync(body.Id, owner, ctx.RequestServices, ct);
    return deleted ? Results.Ok(new { ok = true, id = body.Id }) : Results.NotFound(new { ok = false });
  }
}
