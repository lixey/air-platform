using DemoApp;
using DemoApp.Data;
using DemoApp.Data.Models;
using DemoEcom;
using DemoOrg;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Host.Internals;

namespace Platform.Arch.Tests;

public sealed class RoutingAndSecuritySealTests
{
  [Fact]
  public void EndpointGraph_HasZeroCatchAllRoutes_AndExactAllowAnonymousSurface()
  {
    var prevCatalog = ModuleCatalog.Instance;
    try
    {
      var builder = WebApplication.CreateBuilder();
      builder.Configuration["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=arch_test";
      builder.Configuration["Auth:TokenSecret"] = "arch-test-token-signing-secret-0123456789";
      builder.AddPlatformModule<AppModule>();
      builder.AddPlatformModule<OrgModule>();
      builder.AddPlatformModule<EcomModule>();
      builder.Services.AddPlatformAgent();

      using var app = builder.Build();
      app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
      app.MapPlatform();
      app.MapPlatformAgent();

      var routeEndpoints = ((IEndpointRouteBuilder)app).DataSources
        .SelectMany(ds => ds.Endpoints)
        .OfType<RouteEndpoint>()
        .ToList();

      var patterns = routeEndpoints
        .Select(e => e.RoutePattern.RawText ?? string.Empty)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      // 1. Zero catch-all wildcards on /api
      Assert.DoesNotContain(
        routeEndpoints,
        e => (e.RoutePattern.RawText ?? string.Empty).StartsWith("/api", StringComparison.OrdinalIgnoreCase) &&
             (e.RoutePattern.RawText ?? string.Empty).Contains("{*", StringComparison.Ordinal));

      // 2. Explicit auto-mapped routes for Metadata, Grid, Form, Agent RPCs, declared actions, and flat status transitions
      Assert.Contains("/api/metadata/get-data", patterns);
      Assert.Contains("/api/app/user/get-data", patterns);
      Assert.Contains("/api/app/user/deactivate", patterns);
      Assert.Contains("/api/app/user-form/save", patterns);
      Assert.Contains("/api/ecom/customer-order/get-data", patterns);
      Assert.Contains("/api/ecom/customer-order/move-status-confirm", patterns);
      Assert.Contains("/api/ecom/customer-order-form/save", patterns);
      Assert.Contains("/api/ecom/customer-order-form/move-status-confirm", patterns);
      Assert.Contains("/api/agent/get-status", patterns);
      Assert.Contains("/api/agent/reset", patterns);
      Assert.Contains("/api/agent/confirm-proposal", patterns);
      Assert.Contains("/api/agent/confirm-batch", patterns);
      Assert.Contains("/api/agent/confirm-memory", patterns);
      Assert.Contains("/api/agent/undo-memory", patterns);
      Assert.Contains("/api/agent/upload", patterns);
      Assert.Contains("/api/agent/chat", patterns);
      Assert.Contains("/api/agent/sync", patterns);
      Assert.Contains("/api/agent/attachments/{id}", patterns);

      // Zero legacy non-kebab or ad-hoc duplicate routes
      Assert.DoesNotContain("/api/app/user/getdata", patterns);
      Assert.DoesNotContain("/api/app/user/get-data-async", patterns);
      Assert.DoesNotContain("/api/auth/getloginscreen", patterns);
      Assert.DoesNotContain("/api/agent/status", patterns);
      Assert.DoesNotContain("/api/agent/memory-confirm", patterns);
      Assert.DoesNotContain("/api/agent/memory-undo", patterns);
      Assert.DoesNotContain("/api/agent/proposals/{id}/confirm", patterns);
      Assert.DoesNotContain("/api/agent/proposals/confirm-batch", patterns);

      // 3. Exact AllowAnonymous surface
      string[] expectedAnonymous =
      [
        "/api/auth/get-login-screen",
        "/api/auth/login",
        "/api/auth/logout",
        "/api/auth/refresh",
        "/api/health",
        "/api/platform-files/{token}",
        "/api/platform-language/switch"
      ];
      Array.Sort(expectedAnonymous, StringComparer.OrdinalIgnoreCase);

      var actualAnonymous = routeEndpoints
        .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        .Select(e => e.RoutePattern.RawText ?? string.Empty)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

      Assert.Equal(expectedAnonymous, actualAnonymous);
    }
    finally
    {
      ModuleCatalog.Instance = prevCatalog;
    }
  }

  [Fact]
  public async Task SecurityHeadersAndSafeExceptionMiddleware_EnforcePentestInvariants()
  {
    var prodEnv = new StubHostEnvironment { EnvironmentName = Environments.Production };

    var middleware = new SecurityHeadersMiddleware(
      async ctx =>
      {
        ctx.Response.StatusCode = StatusCodes.Status200OK;
        await ctx.Response.WriteAsync("ok");
      },
      prodEnv);

    var authCtx = new DefaultHttpContext();
    authCtx.Request.Scheme = "https";
    authCtx.Request.Path = "/api/auth/login";
    authCtx.Response.Body = new MemoryStream();
    await middleware.InvokeAsync(authCtx);
    Assert.Equal("nosniff", authCtx.Response.Headers["X-Content-Type-Options"].ToString());
    Assert.Equal("DENY", authCtx.Response.Headers["X-Frame-Options"].ToString());
    Assert.Equal("no-store", authCtx.Response.Headers["Cache-Control"].ToString());
    Assert.Equal("strict-origin-when-cross-origin", authCtx.Response.Headers["Referrer-Policy"].ToString());
    Assert.Equal("camera=(), microphone=(), geolocation=()", authCtx.Response.Headers["Permissions-Policy"].ToString());
    Assert.Contains("max-age=31536000", authCtx.Response.Headers["Strict-Transport-Security"].ToString(), StringComparison.Ordinal);

    var fileCtx = new DefaultHttpContext();
    fileCtx.Request.Path = "/api/platform-files/signed-token";
    fileCtx.Response.Body = new MemoryStream();
    await middleware.InvokeAsync(fileCtx);
    Assert.False(fileCtx.Response.Headers.ContainsKey("X-Frame-Options"));

    var safeEx = new SafeExceptionMiddleware(
      _ => throw new InvalidOperationException("SuperSecretConnectionStringPassword=hunter2"),
      NullLogger<SafeExceptionMiddleware>.Instance);
    var errCtx = new DefaultHttpContext { TraceIdentifier = "trace-pentest-500" };
    errCtx.Response.Body = new MemoryStream();
    await safeEx.InvokeAsync(errCtx);
    Assert.Equal(StatusCodes.Status500InternalServerError, errCtx.Response.StatusCode);
    errCtx.Response.Body.Seek(0, SeekOrigin.Begin);
    var body = await new StreamReader(errCtx.Response.Body).ReadToEndAsync();
    Assert.Contains("trace-pentest-500", body, StringComparison.Ordinal);
    Assert.DoesNotContain("hunter2", body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("InvalidOperationException", body, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task EntityWriteConventions_ProtectsRootUserAtDataLayer()
  {
    using var conn = new SqliteConnection("DataSource=:memory:");
    await conn.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;

    using (var seedScope = AsyncLocalTenantContext.UseSystemScope(correlationId: "seed"))
    await using (var seedDb = new AppDbContext(options, AsyncLocalTenantContext.Instance))
    {
      await seedDb.Database.EnsureCreatedAsync();
      seedDb.AppTenant.Add(new AppTenant { Id = 1, Handle = "Main", Name = "Main", IsActive = true });
      seedDb.AppUser.Add(new AppUser { Id = 1, TenantId = 1, Login = "admin", PasswordHash = "hash", IsRoot = true, IsActive = true });
      seedDb.AppUser.Add(new AppUser { Id = 2, TenantId = 1, Login = "operator", PasswordHash = "hash", IsRoot = false, IsActive = true });
      seedDb.AppUser.Add(new AppUser { Id = 3, TenantId = 1, Login = "co_root", PasswordHash = "hash", IsRoot = true, IsActive = true });
      await seedDb.SaveChangesAsync();
    }

    // 1. Root cannot be deactivated even by root
    using (var rootScope = AsyncLocalTenantContext.Use(1, new HashSet<string>(["*"]), userId: 1))
    await using (var db = new AppDbContext(options, AsyncLocalTenantContext.Instance))
    {
      var root = await db.AppUser.SingleAsync(u => u.Id == 1);
      root.IsActive = false;
      await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    // 2. Root cannot be deleted
    using (var rootScope = AsyncLocalTenantContext.Use(1, new HashSet<string>(["*"]), userId: 1))
    await using (var db = new AppDbContext(options, AsyncLocalTenantContext.Instance))
    {
      var root = await db.AppUser.SingleAsync(u => u.Id == 1);
      db.AppUser.Remove(root);
      await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    // 3. Root cannot be modified by another user, nor can another user self-escalate to IsRoot
    using (var actorScope = AsyncLocalTenantContext.Use(1, new HashSet<string>(["*"]), userId: 2))
    await using (var db = new AppDbContext(options, AsyncLocalTenantContext.Instance))
    {
      var root = await db.AppUser.SingleAsync(u => u.Id == 1);
      root.Name = "Hijacked";
      await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
      db.ChangeTracker.Clear();

      var op = await db.AppUser.SingleAsync(u => u.Id == 2);
      op.IsRoot = true;
      await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
      db.ChangeTracker.Clear();

      db.AppUser.Add(new AppUser { TenantId = 1, Login = "sneaky_root", PasswordHash = "hash", IsRoot = true, IsActive = true });
      await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    // 4. Another root user CAN edit a root user's profile fields (matching CoversUserGrantsAsync)
    using (var coRootScope = AsyncLocalTenantContext.Use(1, new HashSet<string>(["*"]), userId: 3))
    await using (var db = new AppDbContext(options, AsyncLocalTenantContext.Instance))
    {
      var root = await db.AppUser.SingleAsync(u => u.Id == 1);
      root.Name = "Updated By Co-Root";
      await db.SaveChangesAsync();
      Assert.Equal("Updated By Co-Root", root.Name);
    }
  }

  [Fact]
  public void ContractExporter_EndpointTables_MatchReflectedEndpoints_Exactly()
  {
    var generated = ContractExporter.GenerateContracts(typeof(AgentEndpointsExtensions).Assembly);
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AirPlatform.slnx")))
    {
      dir = dir.Parent;
    }

    Assert.NotNull(dir);
    var committedContracts = File.ReadAllText(Path.Combine(dir.FullName, "air-ui/src/core/generated/contracts.ts"));

    (string InterfaceName, Type ServiceType)[] baseTables =
    [
      ("GridServiceEndpoints", typeof(GridService<,>)),
      ("FileGridServiceEndpoints", typeof(FileGridService<>)),
      ("ScreenServiceEndpoints", typeof(ScreenService)),
      ("ReportServiceEndpoints", typeof(ReportService)),
      ("FormServiceEndpoints", typeof(FormService<>))
    ];

    foreach (var (interfaceName, serviceType) in baseTables)
    {
      var block = ExtractInterfaceBlock(committedContracts, interfaceName);
      var generatedBlock = ExtractInterfaceBlock(generated, interfaceName);
      Assert.Equal(NormalizeWhitespace(generatedBlock), NormalizeWhitespace(block));

      foreach (var ep in EndpointRegistry.DescribeServiceType(serviceType))
      {
        Assert.Contains(ep.ActionName, block, StringComparison.Ordinal);
      }
    }

    Type[] platformServices =
    [
      typeof(PlatformAuthService),
      typeof(PlatformLanguageService),
      typeof(PlatformTenantService),
      typeof(PlatformMetadataService),
      typeof(PlatformAgentService)
    ];

    var platformBlock = ExtractInterfaceBlock(committedContracts, "PlatformEndpoints");
    Assert.Equal(NormalizeWhitespace(ExtractInterfaceBlock(generated, "PlatformEndpoints")), NormalizeWhitespace(platformBlock));

    foreach (var serviceType in platformServices)
    {
      var routeAttr = serviceType.GetCustomAttribute<ServiceRouteAttribute>();
      Assert.NotNull(routeAttr);
      foreach (var ep in EndpointRegistry.DescribeServiceType(serviceType))
      {
        Assert.Contains($"'{routeAttr.Route}/{ep.ActionName}':", platformBlock, StringComparison.Ordinal);
      }
    }
  }

  private static string ExtractInterfaceBlock(string source, string interfaceName)
  {
    var header = $"export interface {interfaceName} {{";
    var start = source.IndexOf(header, StringComparison.Ordinal);
    Assert.True(start >= 0, $"Missing interface '{interfaceName}' in contracts.ts.");
    var end = source.IndexOf("\n}", start, StringComparison.Ordinal);
    Assert.True(end > start, $"Unterminated interface '{interfaceName}' in contracts.ts.");
    return source[start..(end + 2)];
  }

  private static string NormalizeWhitespace(string text) =>
    Regex.Replace(text.Replace("\r\n", "\n"), @"\s+", " ").Trim();
}
