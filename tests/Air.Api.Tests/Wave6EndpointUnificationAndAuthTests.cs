using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Auth.Internals;
using Platform.Api.Caching.Internals;
using Platform.Api.Host.Internals;
using Platform.Api.Tests.Internals;

namespace Platform.Api.Tests;

public sealed class Wave6EndpointUnificationAndAuthTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public Wave6EndpointUnificationAndAuthTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    SessionTokenService.ConfigureSecret("wave6-unit-test-secret-with-more-than-32-characters");
  }

  public void Dispose() => _connection.Dispose();

  [Fact]
  public void EndpointRegistry_RegistersSingleCanonicalKebabRoute_AndMarksFiveCookieEndpoints()
  {
    var authEndpoints = EndpointRegistry.DescribeServiceType(typeof(PlatformAuthService));
    var authActions = authEndpoints.Select(static e => e.ActionName).Order(StringComparer.Ordinal).ToArray();
    Assert.Equal(
      ["change-password", "get-login-screen", "get-profile", "login", "logout", "refresh"],
      authActions);

    // Non-canonical route variants are rejected
    Assert.Null(EndpointRegistry.FindReflectedEndpoint(typeof(PlatformAuthService), "getloginscreen"));
    Assert.Null(EndpointRegistry.FindReflectedEndpoint(typeof(PlatformAuthService), "changepassword"));
    Assert.Null(EndpointRegistry.FindReflectedEndpoint(typeof(FakeAppService), "echopositional"));
    Assert.Null(EndpointRegistry.FindReflectedEndpoint(typeof(FakeAppService), "calculate-summary-async"));

    // Unsupported primitive/string body parameters fail fast at startup
    Assert.Throws<InvalidOperationException>(() =>
      EndpointRegistry.DescribeServiceType(typeof(InvalidPrimitiveEndpointProbe)));

    // Startup parameter classification is deterministic without a runtime IServiceProvider
    var loginEp = EndpointRegistry.FindReflectedEndpoint(typeof(PlatformAuthService), "login")!;
    Assert.True(loginEp.RequireClientHeader);
    Assert.Equal(EndpointParameterKind.HttpContext, loginEp.Parameters[0].Kind);
    Assert.Equal(EndpointParameterKind.FromServices, loginEp.Parameters[1].Kind);
    Assert.Equal(EndpointParameterKind.FromServices, loginEp.Parameters[2].Kind);
    Assert.Equal(EndpointParameterKind.JsonBody, loginEp.Parameters[3].Kind);
    Assert.True(loginEp.Parameters[3].IsNullable);
    Assert.NotNull(loginEp.Parameters[3].Factory);
    Assert.Equal(EndpointParameterKind.CancellationToken, loginEp.Parameters[4].Kind);

    // All 5 cookie-sensitive endpoints enforce RequireClientHeader
    Assert.True(EndpointRegistry.FindReflectedEndpoint(typeof(PlatformAuthService), "refresh")!.RequireClientHeader);
    Assert.True(EndpointRegistry.FindReflectedEndpoint(typeof(PlatformAuthService), "logout")!.RequireClientHeader);
    Assert.True(EndpointRegistry.FindReflectedEndpoint(typeof(PlatformLanguageService), "switch")!.RequireClientHeader);
    Assert.True(EndpointRegistry.FindReflectedEndpoint(typeof(PlatformTenantService), "switch")!.RequireClientHeader);

    // Read-only / bearer-compatible endpoints do not require cookie client header
    Assert.False(EndpointRegistry.FindReflectedEndpoint(typeof(PlatformAuthService), "get-login-screen")!.RequireClientHeader);
    Assert.False(EndpointRegistry.FindReflectedEndpoint(typeof(PlatformAuthService), "get-profile")!.RequireClientHeader);
  }

  [Fact]
  public async Task CookieSensitiveEndpoints_AllFive_Return403WithoutClientHeader()
  {
    var services = new ServiceCollection()
      .AddLogging()
      .AddSingleton<IUserContext>(new FixedRightsUser(isAuthenticated: true, "*"))
      .BuildServiceProvider();

    (Type Type, IPlatformService Instance, string Action)[] targets =
    [
      (typeof(PlatformAuthService), new PlatformAuthService(), "login"),
      (typeof(PlatformAuthService), new PlatformAuthService(), "refresh"),
      (typeof(PlatformAuthService), new PlatformAuthService(), "logout"),
      (typeof(PlatformLanguageService), new PlatformLanguageService(), "switch"),
      (typeof(PlatformTenantService), new PlatformTenantService(), "switch")
    ];

    foreach (var (type, instance, action) in targets)
    {
      var ctx = new DefaultHttpContext { RequestServices = services };
      ctx.Request.ContentType = "application/json";
      var bytes = "{}"u8.ToArray();
      ctx.Request.ContentLength = bytes.Length;
      ctx.Request.Body = new MemoryStream(bytes);
      ctx.Response.Body = new MemoryStream();

      var res = await EndpointExecutor.DispatchAsync(
        type,
        instance,
        action,
        instance.Feature,
        ctx.Request,
        services,
        CancellationToken.None);
      await res.ExecuteAsync(ctx);
      Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }
  }

  [Fact]
  public async Task PlatformAuthService_Refresh_RateLimitsInvalidTokens_IgnoresEmptyCookies_AndIsolatesGraceCoordinators()
  {
    var clock = new ControllableTimeProvider(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
    var dbOptions = new DbContextOptionsBuilder<ApiTestDbContext>().UseSqlite(_connection).Options;

    ServiceProvider BuildContainer()
    {
      var sc = new ServiceCollection();
      sc.AddLogging();
      sc.AddHttpContextAccessor();
      sc.AddHybridCache();
      sc.AddSingleton<TimeProvider>(clock);
      sc.AddSingleton(new PlatformOptions());
      sc.AddSingleton<ICacheProvider, CacheProvider>();
      sc.AddSingleton<IUserAuthCache>(sp =>
        new UserAuthCache(sp.GetRequiredService<ICacheProvider>(), sp.GetRequiredService<IServiceScopeFactory>()));
      sc.AddSingleton<LoginRateLimiter>();
      sc.AddSingleton<SessionRefreshCoordinator>();
      sc.AddSingleton<IPasswordHasher, PasswordHasher>();
      sc.AddScoped<PlatformAuthService>();
      sc.AddScoped<ITenantContext, HttpTenantContext>();
      sc.AddScoped(sp => new ApiTestDbContext(dbOptions, sp.GetRequiredService<ITenantContext>()));
      sc.AddScoped<IAuthStore, AuthStore<TestAuthUser, TestAuthSession, TestAuthRole, TestAuthGrant, TestAuthAssignment, TestAuthTenant, ApiTestDbContext>>();
      return sc.BuildServiceProvider();
    }

    await using var containerA = BuildContainer();
    await using var containerB = BuildContainer();

    await using (var seedScope = containerA.CreateAsyncScope())
    {
      var db = seedScope.ServiceProvider.GetRequiredService<ApiTestDbContext>();
      await db.Database.EnsureCreatedAsync();
      var hasher = seedScope.ServiceProvider.GetRequiredService<IPasswordHasher>();
      using (AsyncLocalTenantContext.UseSystemScope())
      {
        db.Tenants.Add(new TestAuthTenant { Id = 1, Handle = "Main", Name = "Main", IsActive = true });
        db.Users.Add(new TestAuthUser
        {
          Id = 10,
          TenantId = 1,
          Login = "bob",
          Name = "Bob",
          PasswordHash = hasher.HashPassword("StrongPass#2026"),
          IsActive = true
        });
        await db.SaveChangesAsync();
      }
    }

    // 1. Empty-cookie /auth/refresh calls (anonymous tabs) return 401 without tripping 429 rate limit
    for (var i = 0; i < LoginRateLimiter.MaxRefreshFailuresPerIp + 5; i++)
    {
      await using var scope = containerA.CreateAsyncScope();
      var ctx = CreateClientHttpContext(scope.ServiceProvider, "198.51.100.77");
      var svc = scope.ServiceProvider.GetRequiredService<PlatformAuthService>();
      var res = await EndpointExecutor.DispatchAsync(typeof(PlatformAuthService), svc, "refresh", svc.Feature, ctx.Request, scope.ServiceProvider, CancellationToken.None);
      await res.ExecuteAsync(ctx);
      Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    // 2. Login password spray from an office NAT IP does NOT block live session refreshes
    var limiterA = containerA.GetRequiredService<LoginRateLimiter>();
    for (var i = 0; i < LoginRateLimiter.MaxTotalFailuresPerIp; i++)
    {
      limiterA.RecordFailure("198.51.100.88", $"sprayed-{i}");
    }

    Assert.True(limiterA.IsLimited("198.51.100.88", "bob"));
    Assert.False(limiterA.IsRefreshLimited("198.51.100.88"));

    // 3. Repeated invalid refresh tokens from an attacker IP trip 429 TooManyRequests,
    //    and refreshing a valid session mid-way does NOT reset the attacker's failure count
    var validRefreshToken = SessionTokenService.CreateRefreshToken();
    await using (var validSeedScope = containerA.CreateAsyncScope())
    {
      var authStore = validSeedScope.ServiceProvider.GetRequiredService<IAuthStore>();
      authStore.CreateSession(
        10,
        1,
        SessionTokenService.HashRefreshToken(validRefreshToken),
        clock.GetUtcNow().AddDays(30),
        "test-agent",
        "198.51.100.99");
      await authStore.SaveChangesAsync();
    }

    for (var i = 0; i < LoginRateLimiter.MaxRefreshFailuresPerIp / 2; i++)
    {
      await using var scope = containerA.CreateAsyncScope();
      var ctx = CreateClientHttpContext(scope.ServiceProvider, "198.51.100.99");
      ctx.Request.Headers.Cookie = $"{AuthClaims.RefreshCookie}=forged-token-a-{i}";
      var svc = scope.ServiceProvider.GetRequiredService<PlatformAuthService>();
      var res = await EndpointExecutor.DispatchAsync(typeof(PlatformAuthService), svc, "refresh", svc.Feature, ctx.Request, scope.ServiceProvider, CancellationToken.None);
      await res.ExecuteAsync(ctx);
      Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    await using (var validRefreshScope = containerA.CreateAsyncScope())
    {
      var ctx = CreateClientHttpContext(validRefreshScope.ServiceProvider, "198.51.100.99");
      ctx.Request.Headers.Cookie = $"{AuthClaims.RefreshCookie}={validRefreshToken}";
      var svc = validRefreshScope.ServiceProvider.GetRequiredService<PlatformAuthService>();
      var res = await EndpointExecutor.DispatchAsync(typeof(PlatformAuthService), svc, "refresh", svc.Feature, ctx.Request, validRefreshScope.ServiceProvider, CancellationToken.None);
      await res.ExecuteAsync(ctx);
      Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }

    for (var i = 0; i < LoginRateLimiter.MaxRefreshFailuresPerIp / 2; i++)
    {
      await using var scope = containerA.CreateAsyncScope();
      var ctx = CreateClientHttpContext(scope.ServiceProvider, "198.51.100.99");
      ctx.Request.Headers.Cookie = $"{AuthClaims.RefreshCookie}=forged-token-b-{i}";
      var svc = scope.ServiceProvider.GetRequiredService<PlatformAuthService>();
      var res = await EndpointExecutor.DispatchAsync(typeof(PlatformAuthService), svc, "refresh", svc.Feature, ctx.Request, scope.ServiceProvider, CancellationToken.None);
      await res.ExecuteAsync(ctx);
      Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    await using (var blockedScope = containerA.CreateAsyncScope())
    {
      var ctx = CreateClientHttpContext(blockedScope.ServiceProvider, "198.51.100.99");
      ctx.Request.Headers.Cookie = $"{AuthClaims.RefreshCookie}=forged-token-blocked";
      var svc = blockedScope.ServiceProvider.GetRequiredService<PlatformAuthService>();
      var res = await EndpointExecutor.DispatchAsync(typeof(PlatformAuthService), svc, "refresh", svc.Feature, ctx.Request, blockedScope.ServiceProvider, CancellationToken.None);
      await res.ExecuteAsync(ctx);
      Assert.Equal(StatusCodes.Status429TooManyRequests, ctx.Response.StatusCode);
    }

    // 3. SessionRefreshCoordinator is isolated per DI container (zero static dictionary leakage across hosts)
    var coordinatorA = containerA.GetRequiredService<SessionRefreshCoordinator>();
    var coordinatorB = containerB.GetRequiredService<SessionRefreshCoordinator>();
    Assert.NotSame(coordinatorA, coordinatorB);
  }

  [Fact]
  public async Task PlatformTenantService_Switch_ReturnsLocalizedErrors_AndSwitchesSessionTenant()
  {
    var clock = new ControllableTimeProvider(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
    var dbOptions = new DbContextOptionsBuilder<ApiTestDbContext>().UseSqlite(_connection).Options;

    var sc = new ServiceCollection();
    sc.AddLogging();
    sc.AddHttpContextAccessor();
    sc.AddSingleton<TimeProvider>(clock);
    sc.AddSingleton(new PlatformOptions());
    sc.AddSingleton<IUserContext>(new FixedRightsUser(isAuthenticated: true, "*"));
    sc.AddScoped<PlatformTenantService>();
    sc.AddScoped<ITenantContext, HttpTenantContext>();
    sc.AddScoped(sp => new ApiTestDbContext(dbOptions, sp.GetRequiredService<ITenantContext>()));
    sc.AddScoped<IAuthStore, AuthStore<TestAuthUser, TestAuthSession, TestAuthRole, TestAuthGrant, TestAuthAssignment, TestAuthTenant, ApiTestDbContext>>();
    await using var provider = sc.BuildServiceProvider();

    await using (var seedScope = provider.CreateAsyncScope())
    {
      var db = seedScope.ServiceProvider.GetRequiredService<ApiTestDbContext>();
      await db.Database.EnsureCreatedAsync();
      using (AsyncLocalTenantContext.UseSystemScope())
      {
        db.Tenants.Add(new TestAuthTenant { Id = 1, Handle = "Main", Name = "Main", IsActive = true });
        db.Tenants.Add(new TestAuthTenant { Id = 2, Handle = "Branch", Name = "Branch", IsActive = true });
        db.Tenants.Add(new TestAuthTenant { Id = 3, Handle = "Archived", Name = "Archived", IsActive = false });
        db.Users.Add(new TestAuthUser
        {
          Id = 10,
          TenantId = 1,
          Login = "root",
          Name = "Root",
          PasswordHash = "hash",
          IsRoot = true,
          IsActive = true
        });
        db.Sessions.Add(new TestAuthSession
        {
          Id = 1,
          UserId = 10,
          TenantId = 1,
          RefreshTokenHash = "hash-1",
          CreatedAt = clock.GetUtcNow(),
          ExpiresAt = clock.GetUtcNow().AddDays(7)
        });
        await db.SaveChangesAsync();
      }
    }

    async Task<(int Status, string Body)> CallSwitchAsync(long id, string acceptLanguage)
    {
      await using var scope = provider.CreateAsyncScope();
      var ctx = CreateClientHttpContext(scope.ServiceProvider, "203.0.113.10");
      ctx.Request.Headers.AcceptLanguage = acceptLanguage;
      var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id }));
      ctx.Request.ContentType = "application/json";
      ctx.Request.ContentLength = bytes.Length;
      ctx.Request.Body = new MemoryStream(bytes);

      var svc = scope.ServiceProvider.GetRequiredService<PlatformTenantService>();
      var res = await PlatformEndpointsExtensions.WithRequestCultureAsync(
        ctx.Request,
        _ => EndpointExecutor.DispatchAsync(
          typeof(PlatformTenantService),
          svc,
          "switch",
          svc.Feature,
          ctx.Request,
          scope.ServiceProvider,
          CancellationToken.None));
      await res.ExecuteAsync(ctx);
      ctx.Response.Body.Position = 0;
      return (ctx.Response.StatusCode, await new StreamReader(ctx.Response.Body).ReadToEndAsync());
    }

    // Missing/zero tenant ID returns localized FormValidationRequired in Ukrainian
    var (missingStatusUk, missingBodyUk) = await CallSwitchAsync(0, "uk-UA");
    Assert.Equal(StatusCodes.Status400BadRequest, missingStatusUk);
    var prevUi = CultureInfo.CurrentUICulture;
    try
    {
      CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");
      Assert.Contains(Localizer.Instance["FormValidationRequired"], missingBodyUk, StringComparison.Ordinal);
    }
    finally
    {
      CultureInfo.CurrentUICulture = prevUi;
    }

    // Inactive tenant returns localized TenantInactive in English and Ukrainian
    var (inactiveStatusEn, inactiveBodyEn) = await CallSwitchAsync(3, "en-US");
    var (inactiveStatusUk, inactiveBodyUk) = await CallSwitchAsync(3, "uk-UA");
    Assert.Equal(StatusCodes.Status400BadRequest, inactiveStatusEn);
    Assert.Equal(StatusCodes.Status400BadRequest, inactiveStatusUk);
    Assert.NotEqual(inactiveBodyEn, inactiveBodyUk);

    // Active tenant switch updates session.TenantId to 2
    var (okStatus, _) = await CallSwitchAsync(2, "en-US");
    Assert.Equal(StatusCodes.Status200OK, okStatus);
    await using (var verifyScope = provider.CreateAsyncScope())
    {
      var authStore = verifyScope.ServiceProvider.GetRequiredService<IAuthStore>();
      var updatedSession = await authStore.FindSessionAsync(1);
      Assert.Equal(2L, updatedSession!.TenantId);
    }
  }

  [Fact]
  public async Task CustomScopedRequest_PreservesBodyOverQueryPrecedence_AndOnlyCapturesRawPayloadWhenIdempotent()
  {
    var services = new ServiceCollection()
      .AddLogging()
      .AddSingleton<IUserContext>(new FixedRightsUser(isAuthenticated: true, "*"))
      .BuildServiceProvider();

    var svc = new FakeAppService();
    svc.Feature.Freeze();

    // 1. Body {"id": 42} wins over query ?id=999&parentId=77, while query ?parentId=77 fills omitted parentId;
    //    RawPayload is NOT allocated when Idempotency-Key header is absent
    var ctx1 = CreateClientHttpContext(services, "203.0.113.20");
    ctx1.Request.QueryString = new QueryString("?id=999&parentId=77");
    var bytes1 = """{"id":42}"""u8.ToArray();
    ctx1.Request.ContentType = "application/json";
    ctx1.Request.ContentLength = bytes1.Length;
    ctx1.Request.Body = new MemoryStream(bytes1);

    var res1 = await EndpointExecutor.DispatchAsync(typeof(FakeAppService), svc, "echo-custom-scoped", svc.Feature, ctx1.Request, services, CancellationToken.None);
    await res1.ExecuteAsync(ctx1);
    ctx1.Response.Body.Position = 0;
    using var doc1 = await JsonDocument.ParseAsync(ctx1.Response.Body);
    Assert.Equal(42L, doc1.RootElement.GetProperty("id").GetInt64());
    Assert.Equal(77L, doc1.RootElement.GetProperty("parentId").GetInt64());
    Assert.False(doc1.RootElement.GetProperty("hasRawPayload").GetBoolean());

    // 2. When Idempotency-Key is present, RawPayload IS captured
    var ctx2 = CreateClientHttpContext(services, "203.0.113.20");
    ctx2.Request.Headers["Idempotency-Key"] = "idem-check-1";
    ctx2.Request.ContentType = "application/json";
    ctx2.Request.ContentLength = bytes1.Length;
    ctx2.Request.Body = new MemoryStream(bytes1);

    var res2 = await EndpointExecutor.DispatchAsync(typeof(FakeAppService), svc, "echo-custom-scoped", svc.Feature, ctx2.Request, services, CancellationToken.None);
    await res2.ExecuteAsync(ctx2);
    ctx2.Response.Body.Position = 0;
    using var doc2 = await JsonDocument.ParseAsync(ctx2.Response.Body);
    Assert.True(doc2.RootElement.GetProperty("hasRawPayload").GetBoolean());
  }

  private static DefaultHttpContext CreateClientHttpContext(IServiceProvider services, string remoteIp)
  {
    var ctx = new DefaultHttpContext { RequestServices = services };
    ctx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
    ctx.Request.Host = new HostString("localhost");
    ctx.Request.Headers[AuthClaims.ClientHeader] = "browser";
    ctx.Response.Body = new MemoryStream();
    return ctx;
  }
}
