using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Platform.Mcp;

public static class McpEndpointsExtensions
{
  private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

  public static IServiceCollection AddAirMcp(this IServiceCollection services)
  {
    services.TryAddSingleton<McpExecutionBridge>();
    return services;
  }

  public static IServiceCollection AddMcpTool<TTool>(this IServiceCollection services)
    where TTool : class, IMcpTool
  {
    services.AddAirMcp();
    services.TryAddEnumerable(ServiceDescriptor.Scoped<IMcpTool, TTool>());
    return services;
  }

  public static IEndpointRouteBuilder MapAirMcp(this IEndpointRouteBuilder endpoints, string pattern = "/api/mcp")
  {
    endpoints.MapPost(
      pattern,
      async (HttpContext httpContext, CancellationToken ct) =>
      {
        var bridge = httpContext.RequestServices.GetRequiredService<McpExecutionBridge>();
        var userContext = httpContext.RequestServices.GetService<IUserContext>();
        var tenantContext = httpContext.RequestServices.GetService<ITenantContext>();

        var isUserAuthenticated = userContext is { IsAuthenticated: true, UserId: > 0 };
        McpCallContext? tokenContext = null;

        if (!isUserAuthenticated)
        {
          var remoteIp = httpContext.Connection.RemoteIpAddress;
          if (remoteIp is not null && !IPAddress.IsLoopback(remoteIp))
          {
            return Results.Unauthorized();
          }

          var rawToken = httpContext.Request.Headers["X-Air-Mcp-Token"].FirstOrDefault();
          if (string.IsNullOrWhiteSpace(rawToken))
          {
            var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader is not null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
              rawToken = authHeader["Bearer ".Length..].Trim();
            }
          }

          tokenContext = bridge.ResolveByTurnToken(rawToken);
          if (tokenContext is not { UserId: > 0 })
          {
            return Results.Unauthorized();
          }
        }

        JsonDocument doc;
        try
        {
          doc = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
          return JsonRpcError(null, -32700, "Parse error.");
        }

        using (doc)
        {
          var root = doc.RootElement;
          if (root.ValueKind != JsonValueKind.Object)
          {
            return JsonRpcError(null, -32600, "Invalid Request.");
          }

          var hasId = root.TryGetProperty("id", out var idEl);
          var rpcId = hasId ? idEl.Clone() : (JsonElement?)null;
          var method = root.TryGetProperty("method", out var mEl) && mEl.ValueKind == JsonValueKind.String
            ? mEl.GetString()
            : null;

          if (string.IsNullOrWhiteSpace(method))
          {
            return JsonRpcError(rpcId, -32600, "Invalid Request: missing method.");
          }

          if (!hasId || method.StartsWith("notifications/", StringComparison.Ordinal))
          {
            return Results.Accepted();
          }

          var effectiveTenantId = tokenContext is not null ? tokenContext.TenantId : tenantContext?.TenantId;
          var effectiveUserId = tokenContext is not null ? tokenContext.UserId : userContext?.UserId;
          var effectiveRights = tokenContext is not null
            ? tokenContext.Rights
            : tenantContext?.Rights ?? userContext?.Permissions;
          var effectiveSessionId = tokenContext?.SessionId;

          using var ambientScope = tokenContext is not null
            ? AsyncLocalTenantContext.Use(effectiveTenantId, effectiveRights, effectiveUserId)
            : null;

          await using var serviceScope = httpContext.RequestServices.CreateAsyncScope();
          var tools = serviceScope.ServiceProvider.GetServices<IMcpTool>().ToList();

          switch (method)
          {
            case "initialize":
              return JsonRpcResult(
                rpcId,
                new
                {
                  protocolVersion = "2024-11-05",
                  capabilities = new { tools = new { } },
                  serverInfo = new { name = "air-mcp", version = "1.0.0" }
                });

            case "ping":
              return JsonRpcResult(rpcId, new { });

            case "tools/list":
              var toolSpecs = tools
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => new
                {
                  name = t.Name,
                  description = t.Description,
                  inputSchema = t.InputSchema
                })
                .ToList();
              return JsonRpcResult(rpcId, new { tools = toolSpecs });

            case "tools/call":
              if (!root.TryGetProperty("params", out var paramsEl) || paramsEl.ValueKind != JsonValueKind.Object ||
                  !paramsEl.TryGetProperty("name", out var toolNameEl) || toolNameEl.ValueKind != JsonValueKind.String)
              {
                return JsonRpcError(rpcId, -32602, "Invalid params: missing tool name.");
              }

              var toolName = toolNameEl.GetString()!;
              var tool = tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
              if (tool is null)
              {
                return JsonRpcError(rpcId, -32601, $"Unknown tool '{toolName}'.");
              }

              var arguments = paramsEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object
                ? argsEl.Clone()
                : JsonSerializer.SerializeToElement(new { }, WebJson);

              var callSessionId = tokenContext is not null
                ? tokenContext.SessionId
                : arguments.TryGetProperty("sessionId", out var sEl) && sEl.ValueKind == JsonValueKind.String
                  ? sEl.GetString()
                  : effectiveSessionId;

              using (var _ = tokenContext is not null
                       ? bridge.UseTurnContext(tokenContext)
                       : bridge.BeginTurn(effectiveTenantId, effectiveUserId, effectiveRights, callSessionId))
              {
                try
                {
                  var output = await tool.ExecuteAsync(arguments, serviceScope.ServiceProvider, ct);
                  var text = output is string s ? s : JsonSerializer.Serialize(output, WebJson);
                  return JsonRpcResult(
                    rpcId,
                    new
                    {
                      content = new[] { new { type = "text", text } },
                      isError = false
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                  return JsonRpcResult(
                    rpcId,
                    new
                    {
                      content = new[] { new { type = "text", text = ex.Message } },
                      isError = true
                    });
                }
              }

            default:
              return JsonRpcError(rpcId, -32601, $"Method '{method}' not found.");
          }
        }
      });

    return endpoints;
  }

  private static IResult JsonRpcResult(JsonElement? id, object result) =>
    Results.Json(
      new
      {
        jsonrpc = "2.0",
        id,
        result
      },
      WebJson);

  private static IResult JsonRpcError(JsonElement? id, int code, string message) =>
    Results.Json(
      new
      {
        jsonrpc = "2.0",
        id,
        error = new { code, message }
      },
      WebJson);
}
