namespace Platform.Agent.Internals;

internal static class AgentMemoryEndpointHandlers
{
  public static async Task<IResult> HandleMemoryConfirmAsync(
    AgentMemoryConfirmRequest req,
    HttpContext ctx,
    CancellationToken ct)
  {
    var owner = AgentStore.ResolveOwner(ctx.RequestServices);
    var saved = await AgentMemoryService.ConfirmInlineMemoryAsync(req.PendingToken, owner, ctx.RequestServices, ct);
    return saved is not null ? Results.Ok(saved) : Results.NotFound();
  }

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
