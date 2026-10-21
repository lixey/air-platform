using System.Text.Json;
using Air.Data.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Air.Mcp;

public static class McpEndpoints
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = false
  };

  public static IServiceCollection AddAirMcp(this IServiceCollection services)
  {
    services.TryAddSingleton<McpExecutionBridge>();
    return services;
  }

  public static IServiceCollection AddMcpTool<TTool>(this IServiceCollection services)
    where TTool : class, IMcpTool
  {
    services.AddAirMcp();
    services.AddScoped<IMcpTool, TTool>();
    return services;
  }

  public static void MapAirMcp(this IEndpointRouteBuilder app, string path = "/api/mcp")
  {
    app.MapGet(path, () => Results.Ok(new
    {
      name = "air-mcp",
      version = "1.0.0",
      status = "ready"
    }));

    app.MapPost(path, async (HttpContext ctx, McpExecutionBridge bridge, CancellationToken ct) =>
    {
      using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct);
      var root = doc.RootElement;

      var method = root.TryGetProperty("method", out var mProp) ? mProp.GetString() : null;
      var hasId = root.TryGetProperty("id", out var idElement);
      object? id = hasId ? ExtractId(idElement) : null;

      if (string.IsNullOrEmpty(method))
      {
        return Results.BadRequest();
      }

      // Notifications (no id)
      if (!hasId || method.StartsWith("notifications/", StringComparison.Ordinal))
      {
        return Results.Accepted();
      }

      if (method == "initialize")
      {
        var protocolVersion = "2024-11-05";
        if (root.TryGetProperty("params", out var initParams) &&
            initParams.TryGetProperty("protocolVersion", out var clientPv) &&
            clientPv.GetString() is { Length: > 0 } pv)
        {
          protocolVersion = pv;
        }

        return JsonRpcOk(id, new
        {
          protocolVersion,
          capabilities = new
          {
            tools = new { listChanged = false }
          },
          serverInfo = new
          {
            name = "air-mcp",
            version = "1.0.0"
          }
        });
      }

      if (method == "ping")
      {
        return JsonRpcOk(id, new { });
      }

      var tools = ctx.RequestServices.GetServices<IMcpTool>().ToList();

      if (method == "tools/list")
      {
        var toolList = tools.Select(t => new
        {
          name = t.Name,
          description = t.Description,
          inputSchema = t.InputSchema
        }).ToList();

        return JsonRpcOk(id, new { tools = toolList });
      }

      if (method == "tools/call")
      {
        if (!root.TryGetProperty("params", out var callParams) ||
            !callParams.TryGetProperty("name", out var nameProp))
        {
          return JsonRpcError(id, -32602, "Missing tool name.");
        }

        var toolName = nameProp.GetString();
        var tool = tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
          return JsonRpcError(id, -32602, $"Unknown tool: {toolName}");
        }

        var args = callParams.TryGetProperty("arguments", out var argsProp)
          ? argsProp.Clone()
          : JsonSerializer.SerializeToElement(new { });

        var active = bridge.Active;
        var tenantId = active?.TenantId ?? ctx.RequestServices.GetService<ITenantContext>()?.TenantId ?? 1;
        var userId = active?.UserId ?? ctx.RequestServices.GetService<ITenantContext>()?.UserId;
        var rights = active?.Rights ?? ctx.RequestServices.GetService<ITenantContext>()?.Rights;

        try
        {
          using (AsyncLocalTenantContext.Use(tenantId, rights, userId, ctx.TraceIdentifier))
          {
            var result = await tool.ExecuteAsync(args, ctx.RequestServices, ct);
            var text = result is string s ? s : JsonSerializer.Serialize(result, JsonOptions);
            return JsonRpcOk(id, new
            {
              content = new[]
              {
                new { type = "text", text }
              },
              isError = false
            });
          }
        }
        catch (Exception ex)
        {
          return JsonRpcOk(id, new
          {
            content = new[]
            {
              new { type = "text", text = $"Error: {ex.Message}" }
            },
            isError = true
          });
        }
      }

      return JsonRpcError(id, -32601, $"Method not found: {method}");
    });
  }

  private static object? ExtractId(JsonElement el) =>
    el.ValueKind switch
    {
      JsonValueKind.Number when el.TryGetInt64(out var l) => l,
      JsonValueKind.String => el.GetString(),
      _ => null
    };

  private static IResult JsonRpcOk(object? id, object result) =>
    Results.Json(new
    {
      jsonrpc = "2.0",
      id,
      result
    }, JsonOptions);

  private static IResult JsonRpcError(object? id, int code, string message) =>
    Results.Json(new
    {
      jsonrpc = "2.0",
      id,
      error = new { code, message }
    }, JsonOptions);
}
