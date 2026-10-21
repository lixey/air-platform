using System.Text.Json;
using Air.Agent.Providers;
using Air.Data.Tenancy;
using Air.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Air.Agent;

public sealed record AgentChatRequestDto
{
  public string? SessionId { get; init; }
  public string? Message { get; init; }
  public IReadOnlyList<string>? AttachmentIds { get; init; }
  public string? Locale { get; init; }
  public string? Provider { get; init; }
  public string? Model { get; init; }
}

public sealed record AgentResetRequestDto(string? SessionId);

public static class AgentEndpoints
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  public static IServiceCollection AddAirAgent(this IServiceCollection services)
  {
    services.AddAirMcp();
    services.TryAddSingleton<AgentStore>();
    services.AddSingleton<IAgentProvider, AgyCliAgentProvider>();
    services.AddSingleton<IAgentProvider, ClaudeCliAgentProvider>();
    services.AddSingleton<IAgentProvider, CodexCliAgentProvider>();
    services.AddSingleton<IAgentProvider, OpenAiCompatibleAgentProvider>();
    return services;
  }

  public static IServiceCollection AddAgentProposalCommitter<TCommitter>(this IServiceCollection services)
    where TCommitter : class, IAgentProposalCommitter
  {
    services.AddAirAgent();
    services.AddScoped<IAgentProposalCommitter, TCommitter>();
    return services;
  }

  public static void MapAirAgent(this IEndpointRouteBuilder app)
  {
    app.MapAirMcp("/api/mcp");

    var group = app.MapGroup("/api/agent");

    group.MapGet("/status", (IEnumerable<IAgentProvider> providers) =>
    {
      var list = providers.Select(p => new
      {
        handle = p.Handle,
        name = p.Name,
        isAvailable = p.IsAvailable
      }).ToList();

      var active = list.FirstOrDefault(p => p.isAvailable)?.handle ?? "Agy";
      return Results.Ok(new
      {
        activeProvider = active,
        providers = list
      });
    });

    group.MapPost("/upload", async (HttpRequest req, AgentStore store, CancellationToken ct) =>
    {
      if (!req.HasFormContentType)
      {
        return Results.BadRequest(new { title = "Expected multipart/form-data" });
      }

      var form = await req.ReadFormAsync(ct);
      var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
      if (file is null || file.Length == 0)
      {
        return Results.BadRequest(new { title = "File is empty" });
      }

      await using var stream = file.OpenReadStream();
      var saved = await store.SaveAttachmentAsync(file.FileName, file.ContentType, stream, ct);
      return Results.Ok(saved);
    });

    group.MapGet("/attachments/{id}", (string id, AgentStore store) =>
    {
      var att = store.GetAttachment(id);
      if (att is null || !File.Exists(att.LocalPath))
      {
        return Results.NotFound();
      }

      return Results.File(att.LocalPath, att.ContentType, att.FileName);
    });

    group.MapPost("/reset", (AgentResetRequestDto req, AgentStore store) =>
    {
      if (!string.IsNullOrWhiteSpace(req.SessionId))
      {
        store.ResetSession(req.SessionId);
      }
      return Results.Ok(new { ok = true });
    });

    group.MapPost("/proposals/{id}/confirm", async (
      string id,
      HttpContext ctx,
      AgentStore store,
      IEnumerable<IAgentProposalCommitter> committers,
      CancellationToken ct) =>
    {
      var proposal = store.GetProposal(id);
      if (proposal is null)
      {
        return Results.NotFound(new { title = "Пропозицію не знайдено або вона застаріла." });
      }

      if (proposal.Status == "committed" && proposal.CommittedRecordId.HasValue)
      {
        return Results.Ok(new AgentCommitResultDto
        {
          ProposalId = proposal.Id,
          RecordId = proposal.CommittedRecordId.Value,
          RecordLabel = proposal.CommittedLabel ?? $"#{proposal.CommittedRecordId.Value}",
          FormService = proposal.FormService ?? "fin-expense-form",
          RefreshServices = ["fin-transaction-grid", "fin-account-grid", "fin-budget-grid", "fin-counterparty-grid"]
        });
      }

      var committer = committers.FirstOrDefault(c => string.Equals(c.Kind, proposal.Kind, StringComparison.OrdinalIgnoreCase));
      if (committer is null)
      {
        return Results.BadRequest(new { title = $"No committer registered for kind '{proposal.Kind}'." });
      }

      try
      {
        var result = await committer.CommitAsync(proposal, ctx.RequestServices, ct);
        proposal.Status = "committed";
        proposal.CommittedRecordId = result.RecordId;
        proposal.CommittedLabel = result.RecordLabel;
        proposal.FormService = result.FormService;
        return Results.Ok(result);
      }
      catch (Exception ex)
      {
        return Results.UnprocessableEntity(new
        {
          title = ex.Message
        });
      }
    });

    group.MapPost("/chat", async (
      AgentChatRequestDto body,
      HttpContext ctx,
      AgentStore store,
      McpExecutionBridge bridge,
      ITenantContext tenantContext,
      IEnumerable<IAgentProvider> providers,
      CancellationToken ct) =>
    {
      var sessionId = string.IsNullOrWhiteSpace(body.SessionId)
        ? Guid.NewGuid().ToString("N")[..12]
        : body.SessionId;

      var providerHandle = body.Provider ?? "Agy";
      var provider = providers.FirstOrDefault(p => string.Equals(p.Handle, providerHandle, StringComparison.OrdinalIgnoreCase))
        ?? providers.First();

      var attachments = (body.AttachmentIds ?? [])
        .Select(store.GetAttachment)
        .Where(a => a is not null)
        .Cast<AgentAttachmentDto>()
        .ToList();

      var serverBaseUrl = ctx.Connection.LocalPort > 0
        ? $"http://127.0.0.1:{ctx.Connection.LocalPort}"
        : $"{ctx.Request.Scheme}://{ctx.Request.Host}";

      var turnReq = new AgentTurnRequest
      {
        SessionId = sessionId,
        Message = body.Message ?? string.Empty,
        Attachments = attachments,
        Locale = body.Locale ?? "uk",
        ServerBaseUrl = serverBaseUrl,
        ProviderHandle = provider.Handle,
        Model = body.Model,
        Services = ctx.RequestServices
      };

      ctx.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
      ctx.Response.Headers.CacheControl = "no-cache";
      ctx.Response.Headers.Connection = "keep-alive";

      using var scope = bridge.BeginTurn(tenantContext.TenantId, tenantContext.UserId, tenantContext.Rights, sessionId);

      await foreach (var evt in provider.StreamTurnAsync(turnReq, ct))
      {
        var json = JsonSerializer.Serialize(evt, JsonOptions);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
      }
    });
  }
}
