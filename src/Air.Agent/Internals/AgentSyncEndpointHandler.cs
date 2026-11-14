namespace Platform.Agent.Internals;

internal static class AgentSyncEndpointHandler
{
  public static async Task HandleSyncAsync(
    AgentSyncRequest body,
    HttpContext ctx,
    AgentStore store,
    AgentOptions options,
    McpExecutionBridge bridge,
    ITenantContext tenantContext,
    IEnumerable<IAgentProvider> providers,
    IEnumerable<IAgentSyncProvider> syncProviders,
    IAgentModelSynchronizer synchronizer,
    CancellationToken ct)
  {
    AgentEndpointHandlers.ApplyTurnCulture(ctx.Request, body.Locale, out var culture);

    if (string.IsNullOrWhiteSpace(body.SourceId) ||
        !string.IsNullOrWhiteSpace(body.SessionId) && !AgentStore.IsValidSessionId(body.SessionId))
    {
      ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
      return;
    }

    IAgentSyncProvider? matchedSyncProvider = null;
    foreach (var sp in syncProviders)
    {
      try
      {
        var allowedSources = await sp.GetSourcesAsync(ctx.RequestServices, ct);
        if (allowedSources.Any(s => string.Equals(s.Id, body.SourceId, StringComparison.OrdinalIgnoreCase)))
        {
          matchedSyncProvider = sp;
          break;
        }
      }
      catch { }
    }

    if (matchedSyncProvider is null)
    {
      ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
      return;
    }

    var owner = AgentStore.ResolveOwner(ctx.RequestServices);
    var sessionId = AgentStore.NormalizeSessionId(body.SessionId);
    AgentAttachmentDto? syncFile = null;
    if (!string.IsNullOrWhiteSpace(body.SyncFileAttachmentId))
    {
      syncFile = store.GetAttachment(body.SyncFileAttachmentId, owner);
      if (syncFile is null)
      {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
      }
    }

    var attachments = (body.AttachmentIds ?? [])
      .Where(id => !string.Equals(id, syncFile?.Id, StringComparison.OrdinalIgnoreCase))
      .Select(id => store.GetAttachment(id, owner))
      .Where(a => a is not null)
      .Cast<AgentAttachmentDto>()
      .ToList();
    var userMessage = body.Message?.Trim();

    await using var sse = new AgentSseWriter(
      ctx.Response,
      options.SseKeepaliveInterval,
      ct,
      ctx.RequestServices.GetService<TimeProvider>());

    AgentSyncResult syncResult;
    try
    {
      syncResult = await matchedSyncProvider.ExecuteSyncAsync(
        new AgentSyncExecutionContext
        {
          SessionId = sessionId,
          SourceId = body.SourceId,
          SyncFile = syncFile,
          Attachments = attachments,
          Locale = culture.TwoLetterISOLanguageName,
          Services = ctx.RequestServices,
          ReportStatusAsync = status => WriteStatusAsync(sse, status, ct)
        },
        ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      await sse.WriteEventAsync(new AgentStreamEventDto { Type = AgentStreamEventType.Error, Text = ex.Message }, ct);
      return;
    }

    if (syncResult.RefreshServices.Count > 0)
    {
      await sse.WriteEventAsync(
        new AgentStreamEventDto
        {
          Type = AgentStreamEventType.Refresh,
          RefreshServices = [.. syncResult.RefreshServices]
        },
        ct);
    }

    var hasUserContext = !string.IsNullOrWhiteSpace(userMessage) || attachments.Count > 0;
    if (!syncResult.RequiresAgentTurn && !hasUserContext)
    {
      if (!string.IsNullOrWhiteSpace(syncResult.SummaryMarkdown))
      {
        await sse.WriteEventAsync(new AgentStreamEventDto { Type = AgentStreamEventType.Delta, Text = syncResult.SummaryMarkdown }, ct);
      }

      await sse.WriteEventAsync(new AgentStreamEventDto { Type = AgentStreamEventType.Done, SessionId = sessionId }, ct);
      return;
    }

    var hasImageAttachment = attachments.Any(a => AgentAttachmentValidator.IsInlineSafeImage(a.ContentType));
    var (provider, selectedModel, selectedModelName) = await AgentEndpointHandlers.ResolveTurnProviderAndModelAsync(
      ctx, store, synchronizer, options, providers, body.Provider, body.Model, preferPhotoModel: hasImageAttachment, ct);

    if (provider is null)
    {
      if (!string.IsNullOrWhiteSpace(syncResult.SummaryMarkdown))
      {
        await sse.WriteEventAsync(new AgentStreamEventDto { Type = AgentStreamEventType.Delta, Text = syncResult.SummaryMarkdown }, ct);
      }

      await sse.WriteEventAsync(new AgentStreamEventDto { Type = AgentStreamEventType.Error, Text = Localizer.Instance["AgentErrorCliMissing"] }, ct);
      return;
    }

    if (!string.IsNullOrWhiteSpace(syncResult.SummaryMarkdown))
    {
      await sse.WriteEventAsync(
        new AgentStreamEventDto
        {
          Type = AgentStreamEventType.Delta,
          Text = $"{syncResult.SummaryMarkdown.TrimEnd()}\n\n"
        },
        ct);
    }

    var promptParts = new List<string>(3);
    if (syncResult.RequiresAgentTurn)
    {
      promptParts.Add(!string.IsNullOrWhiteSpace(syncResult.AgentPrompt)
        ? syncResult.AgentPrompt
        : "Review the synchronization context.");
    }
    else if (!string.IsNullOrWhiteSpace(syncResult.SummaryMarkdown))
    {
      promptParts.Add($"[SYNCHRONIZATION SUMMARY]:\n{syncResult.SummaryMarkdown.Trim()}");
    }

    if (attachments.Count > 0)
    {
      promptParts.Add("[ATTACHED FILES]: Inspect all attached files via read_file and apply their exact details directly.");
    }

    if (!string.IsNullOrWhiteSpace(userMessage))
    {
      promptParts.Add($"[USER INSTRUCTIONS]:\n{userMessage}");
    }
    else if (!syncResult.RequiresAgentTurn)
    {
      promptParts.Add(hasImageAttachment
        ? Localizer.Instance["AgentDefaultPhotoPrompt"]
        : "Analyze the attached files in the context of this synchronization.");
    }

    var turnReq = new AgentTurnRequest
    {
      SessionId = sessionId,
      Owner = owner,
      Message = string.Join("\n\n", promptParts),
      Attachments = attachments,
      Locale = culture.TwoLetterISOLanguageName,
      ProviderHandle = provider.Handle,
      Model = selectedModel,
      ModelName = selectedModelName,
      SyncContext = syncResult.SyncContextJson,
      McpServerUrl = AgentEndpointHandlers.ResolveLoopbackMcpUrl(ctx),
      Services = ctx.RequestServices
    };

    var effectiveTimeZone = AgentEndpointHandlers.ResolveUserTimeZone(ctx);
    using var scope = bridge.BeginTurn(
      tenantContext.TenantId, tenantContext.UserId, tenantContext.Rights, sessionId, culture.Name, effectiveTimeZone);

    await foreach (var evt in provider.StreamTurnAsync(turnReq, ct))
    {
      await sse.WriteEventAsync(evt, ct);
    }
  }

  private static Task WriteStatusAsync(AgentSseWriter sse, string status, CancellationToken ct) =>
    sse.WriteEventAsync(new AgentStreamEventDto { Type = AgentStreamEventType.Status, Text = status }, ct);
}
