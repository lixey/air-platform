using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Auth.Internals;
using Platform.Api.Caching.Internals;
using Platform.Api.Host.Internals;
using Platform.Api.Tests.Internals;

namespace Platform.Api.Tests;

public sealed class Wave1AuthExceptionAndTenancyTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public Wave1AuthExceptionAndTenancyTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    SessionTokenService.ConfigureSecret("wave1-unit-test-secret-with-more-than-32-characters");
    AuthEndpointsExtensions.ClearGraceTokensForTesting();
  }

  public void Dispose()
  {
    AuthEndpointsExtensions.ClearGraceTokensForTesting();
    _connection.Dispose();
  }

  [Fact]
  public async Task HttpTenantContext_IsFailClosed_WhenHttpContextNullOrUnauthenticated_AndAuthStoreUsesSystemScope()
  {
    var accessor = new HttpContextAccessor();
    var tenantContext = new HttpTenantContext(accessor);

    // 1. Background / startup context (HttpContext == null) is fail-closed
    Assert.Null(accessor.HttpContext);
    Assert.Null(tenantContext.TenantId);
    Assert.False(tenantContext.IsSystemScope);

    // 2. Unauthenticated HTTP request is fail-closed
    accessor.HttpContext = new DefaultHttpContext();
    Assert.Null(tenantContext.TenantId);
    Assert.False(tenantContext.IsSystemScope);

    // 3. Explicit null-tenant ambient scope is fail-closed
    using (AsyncLocalTenantContext.Use(null))
    {
      Assert.False(tenantContext.IsSystemScope);
    }

    // 4. Explicit system scope enables cross-tenant access and overrides any HTTP tenant claim
    accessor.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(
      new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(AuthClaims.Tenant, "5")], "PlatformAuth"));
    Assert.Equal(5, tenantContext.TenantId);
    Assert.False(tenantContext.IsSystemScope);

    using (AsyncLocalTenantContext.UseSystemScope())
    {
      Assert.True(tenantContext.IsSystemScope);
      Assert.Null(tenantContext.TenantId);
    }

    // 5. Real DbContext + AuthStore with HttpContext == null: tenant entities stay fail-closed while AuthStore works
    accessor.HttpContext = null;
    var dbOptions = new DbContextOptionsBuilder<ApiTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    await using var db = new ApiTestDbContext(dbOptions, tenantContext);
    await db.Database.EnsureCreatedAsync();

    var hasher = new PasswordHasher();
    long seededUserId;
    using (AsyncLocalTenantContext.UseSystemScope())
    {
      var tenant = new TestAuthTenant { Id = 1, Handle = "Main", Name = "Tenant One", IsActive = true };
      db.Tenants.Add(tenant);
      await db.SaveChangesAsync();

      db.Items.Add(new ApiTestItem { TenantId = tenant.Id, Name = "SecretItem" });
      var user = new TestAuthUser
      {
        TenantId = tenant.Id,
        Login = "alice",
        Name = "Alice",
        PasswordHash = hasher.HashPassword("CorrectHorseBattery1!"),
        IsActive = true
      };
      db.Users.Add(user);
      await db.SaveChangesAsync();
      seededUserId = user.Id;
    }

    db.ChangeTracker.Clear();

    // Unscoped read returns 0 rows; unscoped write to tenant entity throws
    Assert.Equal(0, await db.Items.CountAsync());
    db.Items.Add(new ApiTestItem { Name = "BlockedWrite" });
    await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    db.ChangeTracker.Clear();

    // AuthStore pre-auth operations succeed cleanly via explicit system scope
    var authStore = new AuthStore<TestAuthUser, TestAuthSession, TestAuthRole, TestAuthGrant, TestAuthAssignment, TestAuthTenant, ApiTestDbContext>(db);
    var foundUser = await authStore.FindUserByLoginAsync("ALICE");
    Assert.NotNull(foundUser);
    Assert.Equal(seededUserId, foundUser!.Id);

    var createdSession = authStore.CreateSession(
      foundUser.Id,
      1,
      SessionTokenService.HashRefreshToken("initial-refresh-token"),
      TimeProvider.System.GetUtcNow().AddDays(30),
      "test-agent",
      "127.0.0.1");
    await authStore.SaveChangesAsync();
    Assert.True(createdSession.Id > 0);
  }

  [Fact]
  public async Task PlatformJobHostedService_RunsJobsFailClosedByDefault_AndAllowsExplicitSystemScope()
  {
    TrackingPlatformJob.Completed = new TaskCompletionSource<(bool, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);

    var services = new ServiceCollection();
    services.AddHttpContextAccessor();
    services.AddScoped<ITenantContext, HttpTenantContext>();
    services.AddScoped<TrackingPlatformJob>();
    await using var provider = services.BuildServiceProvider();

    var hosted = new PlatformJobHostedService(
      provider.GetRequiredService<IServiceScopeFactory>(),
      NullLogger<PlatformJobHostedService>.Instance,
      [new PlatformJobRegistration(typeof(TrackingPlatformJob), TimeSpan.FromHours(1))]);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await hosted.StartAsync(cts.Token);
    var (defaultSystemScope, explicitSystemScope) = await TrackingPlatformJob.Completed.Task.WaitAsync(cts.Token);
    await hosted.StopAsync(CancellationToken.None);

    Assert.False(defaultSystemScope);
    Assert.True(explicitSystemScope);
  }

  [Fact]
  public async Task UserAuthCache_CoalescedGetOrCreate_SurvivesFirstCallerCancellationAndScopeDisposal()
  {
    var gate = new DisposableScopedGate();
    var services = new ServiceCollection();
    services.AddHybridCache();
    services.AddSingleton<ICacheProvider, CacheProvider>();
    services.AddSingleton(gate);
    services.AddScoped<IAuthStore, DisposableScopedAuthStore>();
    await using var provider = services.BuildServiceProvider();

    var authCache = new UserAuthCache(
      provider.GetRequiredService<ICacheProvider>(),
      provider.GetRequiredService<IServiceScopeFactory>());

    // 1. Coalesced GetUserProfileAsync stampede
    using var ctsA = new CancellationTokenSource();
    var scopeA = provider.CreateAsyncScope();
    var storeA = (DisposableScopedAuthStore)scopeA.ServiceProvider.GetRequiredService<IAuthStore>();

    var callerATask = authCache.GetUserProfileAsync(7, 1, storeA, ctsA.Token);
    await gate.EnteredUserLookup.Task;

    var callerBTask = authCache.GetUserProfileAsync(7, 1, null, CancellationToken.None);

    // Caller A aborts its HTTP request AND disposes its request scope while DB lookup is in-flight
    await ctsA.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callerATask);
    await scopeA.DisposeAsync();
    Assert.True(storeA.IsDisposed);

    // Unblock DB lookup -> Caller B completes cleanly from UserAuthCache's owned scope
    gate.ReleaseUserLookup.TrySetResult();
    var profileB = await callerBTask;
    Assert.NotNull(profileB);
    Assert.Equal(7, profileB!.Id);
    Assert.Equal(3, profileB.TokenVersion);

    // 2. Coalesced GetSessionAsync stampede
    using var ctsSessionA = new CancellationTokenSource();
    var scopeSessionA = provider.CreateAsyncScope();
    var storeSessionA = (DisposableScopedAuthStore)scopeSessionA.ServiceProvider.GetRequiredService<IAuthStore>();

    var sessionATask = authCache.GetSessionAsync(42, storeSessionA, ctsSessionA.Token);
    await gate.EnteredSessionLookup.Task;

    var sessionBTask = authCache.GetSessionAsync(42, null, CancellationToken.None);

    await ctsSessionA.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sessionATask);
    await scopeSessionA.DisposeAsync();
    Assert.True(storeSessionA.IsDisposed);

    gate.ReleaseSessionLookup.TrySetResult();
    var sessionSnapshot = await sessionBTask;
    Assert.NotNull(sessionSnapshot);
    Assert.Equal(42, sessionSnapshot!.Id);
  }

  [Fact]
  public async Task AuthEndpoints_LoginRequiresClientHeader_AndRefreshSlidesExpiryAndReissuesGraceCookies()
  {
    var start = TimeProvider.System.GetUtcNow();
    var clock = new ControllableTimeProvider(start);
    var options = new PlatformOptions
    {
      AccessTokenLifetime = TimeSpan.FromMinutes(30),
      RefreshTokenLifetime = TimeSpan.FromDays(30)
    };

    var dbOptions = new DbContextOptionsBuilder<ApiTestDbContext>()
      .UseSqlite(_connection)
      .Options;

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddHttpContextAccessor();
    services.AddHybridCache();
    services.AddSingleton<TimeProvider>(clock);
    services.AddSingleton(options);
    services.AddSingleton<ICacheProvider, CacheProvider>();
    services.AddSingleton<IUserAuthCache>(sp =>
      new UserAuthCache(sp.GetRequiredService<ICacheProvider>(), sp.GetRequiredService<IServiceScopeFactory>()));
    services.AddSingleton<LoginRateLimiter>();
    services.AddSingleton<IPasswordHasher, PasswordHasher>();
    services.AddScoped<ITenantContext, HttpTenantContext>();
    services.AddScoped(_ => new ApiTestDbContext(dbOptions, _.GetRequiredService<ITenantContext>()));
    services.AddScoped<IAuthStore, AuthStore<TestAuthUser, TestAuthSession, TestAuthRole, TestAuthGrant, TestAuthAssignment, TestAuthTenant, ApiTestDbContext>>();

    await using var provider = services.BuildServiceProvider();

    await using (var seedScope = provider.CreateAsyncScope())
    {
      var db = seedScope.ServiceProvider.GetRequiredService<ApiTestDbContext>();
      await db.Database.EnsureCreatedAsync();
      var hasher = seedScope.ServiceProvider.GetRequiredService<IPasswordHasher>();
      using (AsyncLocalTenantContext.UseSystemScope())
      {
        var tenant = new TestAuthTenant { Id = 1, Handle = "Main", Name = "Main", IsActive = true };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        db.Users.Add(new TestAuthUser
        {
          TenantId = tenant.Id,
          Login = "alice",
          Name = "Alice",
          PasswordHash = hasher.HashPassword("StrongPass#2026"),
          IsActive = true
        });
        await db.SaveChangesAsync();
      }
    }

    // 1. /auth/login WITHOUT X-Platform-Client -> 403 Forbidden (Login CSRF protection)
    await using (var scope = provider.CreateAsyncScope())
    {
      var ctx = CreateHttpContext(scope.ServiceProvider, includeClientHeader: false);
      var res = await AuthEndpointsExtensions.HandleLoginAsync(
        new LoginRequest { Login = "alice", Password = "StrongPass#2026" },
        ctx,
        scope.ServiceProvider.GetRequiredService<IAuthStore>(),
        scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
        CancellationToken.None);
      await res.ExecuteAsync(ctx);
      Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    // 2. /auth/login WITH X-Platform-Client -> 200 OK + sets access & refresh cookies
    string initialRefresh;
    long sessionId;
    await using (var scope = provider.CreateAsyncScope())
    {
      var ctx = CreateHttpContext(scope.ServiceProvider, includeClientHeader: true);
      var authStore = scope.ServiceProvider.GetRequiredService<IAuthStore>();
      var res = await AuthEndpointsExtensions.HandleLoginAsync(
        new LoginRequest { Login = "alice", Password = "StrongPass#2026" },
        ctx,
        authStore,
        scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
        CancellationToken.None);
      await res.ExecuteAsync(ctx);
      Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);

      var cookies = ParseSetCookies(ctx);
      Assert.True(cookies.TryGetValue(AuthClaims.AccessCookie, out var initialAccess));
      Assert.True(cookies.TryGetValue(AuthClaims.RefreshCookie, out initialRefresh!));
      Assert.True(SessionTokenService.TryValidateAccessToken(initialAccess!, out sessionId, out _, out _));

      var dbSession = await authStore.FindSessionAsync(sessionId);
      Assert.Equal(start.AddDays(30), dbSession!.ExpiresAt);
    }

    // 3. Advance clock 10 days -> /auth/refresh slides session.ExpiresAt to (start + 40d) & rotates cookies
    clock.Advance(TimeSpan.FromDays(10));
    var expectedSlidExpiry = clock.GetUtcNow().AddDays(30);
    string rotatedAccess;
    string rotatedRefresh;
    await using (var scope = provider.CreateAsyncScope())
    {
      var ctx = CreateHttpContext(scope.ServiceProvider, includeClientHeader: true);
      ctx.Request.Headers.Cookie = $"{AuthClaims.RefreshCookie}={initialRefresh}";
      var authStore = scope.ServiceProvider.GetRequiredService<IAuthStore>();

      var res = await AuthEndpointsExtensions.HandleRefreshAsync(ctx, authStore, CancellationToken.None);
      await res.ExecuteAsync(ctx);
      Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);

      var cookies = ParseSetCookies(ctx);
      Assert.True(cookies.TryGetValue(AuthClaims.AccessCookie, out rotatedAccess!));
      Assert.True(cookies.TryGetValue(AuthClaims.RefreshCookie, out rotatedRefresh!));
      Assert.NotEqual(initialRefresh, rotatedRefresh);

      var dbSession = await authStore.FindSessionAsync(sessionId);
      Assert.Equal(expectedSlidExpiry, dbSession!.ExpiresAt);
    }

    // 4. Concurrent tab hits /auth/refresh 10s later with stale initialRefresh -> gets rotated access + refresh cookies!
    clock.Advance(TimeSpan.FromSeconds(10));
    await using (var scope = provider.CreateAsyncScope())
    {
      var ctx = CreateHttpContext(scope.ServiceProvider, includeClientHeader: true);
      ctx.Request.Headers.Cookie = $"{AuthClaims.RefreshCookie}={initialRefresh}";
      var authStore = scope.ServiceProvider.GetRequiredService<IAuthStore>();

      var res = await AuthEndpointsExtensions.HandleRefreshAsync(ctx, authStore, CancellationToken.None);
      await res.ExecuteAsync(ctx);
      Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);

      var graceCookies = ParseSetCookies(ctx);
      Assert.True(graceCookies.TryGetValue(AuthClaims.AccessCookie, out var graceAccess));
      Assert.True(graceCookies.TryGetValue(AuthClaims.RefreshCookie, out var graceRefresh));
      Assert.Equal(rotatedAccess, graceAccess);
      Assert.Equal(rotatedRefresh, graceRefresh);
    }

    // 5. Replay initialRefresh after 30s grace window -> 401 Unauthorized & revokes session for reuse
    clock.Advance(TimeSpan.FromSeconds(30));
    await using (var scope = provider.CreateAsyncScope())
    {
      var ctx = CreateHttpContext(scope.ServiceProvider, includeClientHeader: true);
      ctx.Request.Headers.Cookie = $"{AuthClaims.RefreshCookie}={initialRefresh}";
      var authStore = scope.ServiceProvider.GetRequiredService<IAuthStore>();

      var res = await AuthEndpointsExtensions.HandleRefreshAsync(ctx, authStore, CancellationToken.None);
      await res.ExecuteAsync(ctx);
      Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);

      var dbSession = await authStore.FindSessionAsync(sessionId);
      Assert.NotNull(dbSession!.RevokedAt);
      Assert.Equal("reuse", dbSession.RevokedReason);
    }
  }

  [Fact]
  public void LoginRateLimiter_ScopesByIpAndNormalizedLogin_WithGlobalPerIpCap()
  {
    var clock = new ControllableTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
    var limiter = new LoginRateLimiter(clock);
    const string officeNatIp = "198.51.100.25";

    for (var i = 0; i < LoginRateLimiter.MaxAccountFailuresPerIp; i++)
    {
      limiter.RecordFailure(officeNatIp, "Alice");
    }

    // Alice is limited on that IP (case-insensitive), but Bob behind the same NAT is NOT limited
    Assert.True(limiter.IsLimited(officeNatIp, "alice"));
    Assert.False(limiter.IsLimited(officeNatIp, "bob"));

    // Resetting Alice on valid login clears Alice's pair bucket without wiping global IP spray count
    limiter.Reset(officeNatIp, "ALICE");
    Assert.False(limiter.IsLimited(officeNatIp, "alice"));

    // Spraying distinct usernames eventually trips the global per-IP safety cap (100 total failures)
    for (var i = 0; i < LoginRateLimiter.MaxTotalFailuresPerIp; i++)
    {
      limiter.RecordFailure(officeNatIp, $"sprayed-user-{i}");
    }

    Assert.True(limiter.IsLimited(officeNatIp, "bob"));
    Assert.False(limiter.IsLimited("198.51.100.99", "bob"));
  }

  [Fact]
  public async Task ServiceEndpoints_Maps400And409Exceptions_AndNormalizesAsyncMethodSuffix()
  {
    var feat = new Feature("Fake", "App", typeof(FakeAppService));
    feat.Freeze();

    var services = new ServiceCollection()
      .AddLogging()
      .AddSingleton<IUserContext>(new FixedRightsUser(isAuthenticated: true, "*"))
      .BuildServiceProvider();

    var service = new FakeAppService();

    // 1. Async suffix normalization: CalculateSummaryAsync resolves via both 'calculate-summary' and 'calculate-summary-async'
    Assert.NotNull(ServiceRequestBinder.ResolveCallableActionMethod(typeof(FakeAppService), "calculate-summary"));
    Assert.NotNull(ServiceRequestBinder.ResolveCallableActionMethod(typeof(FakeAppService), "calculate-summary-async"));

    var validJson = """{"count":5,"total":10,"amount":25.5,"createdOn":"2026-05-10T00:00:00Z"}""";
    var (okStatus, okBody) = await InvokeCustomActionAsync(service, feat, services, "calculate-summary", validJson);
    Assert.Equal(StatusCodes.Status200OK, okStatus);
    Assert.Contains("sum:5:25.5", okBody, StringComparison.Ordinal);

    // 2. JsonException during typed request deserialization -> 400 Bad Request without leaking CLR internals
    var badTypedJson = """{"count":"12.9","total":1,"amount":1,"createdOn":"2026-05-10T00:00:00Z"}""";
    var (jsonErrStatus, jsonErrBody) = await InvokeCustomActionAsync(service, feat, services, "calculate-summary", badTypedJson);
    Assert.Equal(StatusCodes.Status400BadRequest, jsonErrStatus);
    Assert.DoesNotContain("Int32", jsonErrBody, StringComparison.OrdinalIgnoreCase);
    Assert.Contains(Localizer.Instance["InvalidJsonRequest"], jsonErrBody, StringComparison.Ordinal);

    // 3. QueryRequestException -> 400 Bad Request (both via DispatchCustomServiceMethodAsync & WithRequestCultureAsync)
    var (queryStatus, queryBody) = await InvokeCustomActionAsync(service, feat, services, "throw-query-error", "{}");
    Assert.Equal(StatusCodes.Status400BadRequest, queryStatus);
    Assert.Contains("secretField", queryBody, StringComparison.Ordinal);

    // 4. IdempotencyConflictException -> 409 Conflict with MutationOutcome.Conflict
    var (idemStatus, idemBody) = await InvokeCustomActionAsync(service, feat, services, "throw-idempotency-conflict", "{}");
    Assert.Equal(StatusCodes.Status409Conflict, idemStatus);
    var idemDto = JsonSerializer.Deserialize<MutationResultDto>(idemBody, PlatformJsonOptions.DefaultOptions);
    Assert.Equal(MutationOutcome.Conflict, idemDto?.Outcome);

    // 5. DbUpdateConcurrencyException -> 409 Conflict with MutationOutcome.Conflict
    var (concStatus, concBody) = await InvokeCustomActionAsync(service, feat, services, "throw-concurrency-conflict", "{}");
    Assert.Equal(StatusCodes.Status409Conflict, concStatus);
    var concDto = JsonSerializer.Deserialize<MutationResultDto>(concBody, PlatformJsonOptions.DefaultOptions);
    Assert.Equal(MutationOutcome.Conflict, concDto?.Outcome);

    // 6. PlatformEndpointsExtensions.WithRequestCulture also maps 400/409
    var syncCtx = CreateHttpContext(services, includeClientHeader: true);
    var syncResult = PlatformEndpointsExtensions.WithRequestCulture(
      syncCtx.Request,
      _ => throw new IdempotencyConflictException("Duplicate key"));
    await syncResult.ExecuteAsync(syncCtx);
    Assert.Equal(StatusCodes.Status409Conflict, syncCtx.Response.StatusCode);
  }

  [Fact]
  public void SecretHardening_AndPlatformLockKeys_EnforceProductionSecretAndTenantIsolation()
  {
    // Production startup without secret fails fast
    Assert.Throws<InvalidOperationException>(() =>
      SessionTokenService.EnsureConfiguredForEnvironment(null, isProduction: true));
    Assert.Throws<InvalidOperationException>(() =>
      SessionTokenService.EnsureConfiguredForEnvironment("   ", isProduction: true));
    Assert.Throws<InvalidOperationException>(() =>
      SessionTokenService.EnsureConfiguredForEnvironment("too-short-secret", isProduction: false));

    // Non-production startup without explicit secret permits dev fallback
    SessionTokenService.EnsureConfiguredForEnvironment(null, isProduction: false);

    // Tenant-scoped and composite lock keys include tenantId; global single-PK lock keys remain shared
    var dbOptions = new DbContextOptionsBuilder<ApiTestDbContext>().UseSqlite(_connection).Options;
    using var dbTenant1 = new ApiTestDbContext(dbOptions, new FixedTenant(1, 10));
    using var dbTenant2 = new ApiTestDbContext(dbOptions, new FixedTenant(2, 10));

    var itemLockT1 = MutationContext.LockKeyOf(typeof(ApiTestItem), [100], dbTenant1);
    var itemLockT2 = MutationContext.LockKeyOf(typeof(ApiTestItem), [100], dbTenant2);
    Assert.NotEqual(itemLockT1, itemLockT2);

    var compositeLockT1 = MutationContext.LockKeyOf(typeof(TestAuthTenant), [10, 20], dbTenant1);
    var compositeLockT2 = MutationContext.LockKeyOf(typeof(TestAuthTenant), [10, 20], dbTenant2);
    Assert.NotEqual(compositeLockT1, compositeLockT2);

    var globalSinglePkT1 = MutationContext.LockKeyOf(typeof(TestAuthTenant), [5], dbTenant1);
    var globalSinglePkT2 = MutationContext.LockKeyOf(typeof(TestAuthTenant), [5], dbTenant2);
    Assert.Equal(globalSinglePkT1, globalSinglePkT2);
  }

  [Fact]
  public async Task UsePlatformForwardedHeaders_DisabledBlocksLoopbackSpoof_EnabledHonorsKnownProxiesOnce()
  {
    var defaultBuilder = WebApplication.CreateBuilder();
    defaultBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Auth:TokenSecret"] = "wave1-unit-test-secret-with-more-than-32-characters"
    });
    defaultBuilder.AddPlatformApiModule<TestApiModule>();
    await using var defaultApp = defaultBuilder.Build();

    var defaultPipeline = ((IApplicationBuilder)defaultApp).New();
    defaultPipeline.UsePlatformForwardedHeaders();
    defaultPipeline.UsePlatformAuth();
    IPAddress? observedDefaultIp = null;
    defaultPipeline.Run(ctx =>
    {
      observedDefaultIp = ctx.Connection.RemoteIpAddress;
      return Task.CompletedTask;
    });
    var defaultDelegate = defaultPipeline.Build();

    var spoofedLoopback = new DefaultHttpContext { RequestServices = defaultApp.Services };
    spoofedLoopback.Connection.RemoteIpAddress = IPAddress.Loopback;
    spoofedLoopback.Request.Headers["X-Forwarded-For"] = "198.51.100.77";
    await defaultDelegate(spoofedLoopback);
    Assert.Equal(IPAddress.Loopback, observedDefaultIp);

    var enabledBuilder = WebApplication.CreateBuilder();
    enabledBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Auth:TokenSecret"] = "wave1-unit-test-secret-with-more-than-32-characters",
      ["ForwardedHeaders:Enabled"] = "true",
      ["ForwardedHeaders:ForwardLimit"] = "1",
      ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5",
      ["ForwardedHeaders:KnownProxies:1"] = "172.16.0.0/12"
    });
    enabledBuilder.AddPlatformApiModule<TestApiModule>();
    await using var enabledApp = enabledBuilder.Build();

    var enabledPipeline = ((IApplicationBuilder)enabledApp).New();
    enabledPipeline.UsePlatformForwardedHeaders();
    enabledPipeline.UsePlatformAuth();
    IPAddress? observedEnabledIp = null;
    enabledPipeline.Run(ctx =>
    {
      observedEnabledIp = ctx.Connection.RemoteIpAddress;
      return Task.CompletedTask;
    });
    var enabledDelegate = enabledPipeline.Build();

    // Default 127.0.0.1 loopback proxy is cleared when KnownProxies is explicitly set.
    var untrustedLoopback = new DefaultHttpContext { RequestServices = enabledApp.Services };
    untrustedLoopback.Connection.RemoteIpAddress = IPAddress.Loopback;
    untrustedLoopback.Request.Headers["X-Forwarded-For"] = "198.51.100.88";
    await enabledDelegate(untrustedLoopback);
    Assert.Equal(IPAddress.Loopback, observedEnabledIp);

    // Trusted proxy 10.0.0.5 with ForwardLimit=1 runs middleware once (resolving 198.51.100.2, not 198.51.100.1).
    var trustedProxyCtx = new DefaultHttpContext { RequestServices = enabledApp.Services };
    trustedProxyCtx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
    trustedProxyCtx.Request.Headers["X-Forwarded-For"] = "198.51.100.1, 198.51.100.2";
    await enabledDelegate(trustedProxyCtx);
    Assert.Equal(IPAddress.Parse("198.51.100.2"), observedEnabledIp);

    // CIDR KnownProxies entry (172.16.0.0/12) populates KnownIPNetworks and honors X-Forwarded-For.
    var trustedCidrCtx = new DefaultHttpContext { RequestServices = enabledApp.Services };
    trustedCidrCtx.Connection.RemoteIpAddress = IPAddress.Parse("172.20.4.9");
    trustedCidrCtx.Request.Headers["X-Forwarded-For"] = "198.51.100.44";
    await enabledDelegate(trustedCidrCtx);
    Assert.Equal(IPAddress.Parse("198.51.100.44"), observedEnabledIp);

    // Invalid KnownProxies entry fails closed at startup instead of running with an empty trust list.
    var invalidProxyBuilder = WebApplication.CreateBuilder();
    invalidProxyBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Auth:TokenSecret"] = "wave1-unit-test-secret-with-more-than-32-characters",
      ["ForwardedHeaders:Enabled"] = "true",
      ["ForwardedHeaders:KnownProxies:0"] = "not-an-ip-or-cidr"
    });
    Assert.Throws<InvalidOperationException>(() => invalidProxyBuilder.AddPlatformApiModule<TestApiModule>());
  }

  private static DefaultHttpContext CreateHttpContext(IServiceProvider services, bool includeClientHeader)
  {
    var ctx = new DefaultHttpContext
    {
      RequestServices = services
    };
    ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.50");
    ctx.Request.Host = new HostString("localhost");
    ctx.Response.Body = new MemoryStream();
    if (includeClientHeader)
    {
      ctx.Request.Headers[AuthClaims.ClientHeader] = "browser";
    }

    return ctx;
  }

  private static Dictionary<string, string> ParseSetCookies(HttpContext ctx)
  {
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var raw in ctx.Response.Headers.SetCookie)
    {
      if (string.IsNullOrEmpty(raw))
      {
        continue;
      }

      var firstPart = raw.Split(';', 2)[0];
      var eq = firstPart.IndexOf('=');
      if (eq > 0)
      {
        map[firstPart[..eq].Trim()] = Uri.UnescapeDataString(firstPart[(eq + 1)..].Trim());
      }
    }

    return map;
  }

  private static async Task<(int StatusCode, string Body)> InvokeCustomActionAsync(
    FakeAppService service,
    Feature feat,
    IServiceProvider services,
    string method,
    string jsonBody)
  {
    var bytes = Encoding.UTF8.GetBytes(jsonBody);
    var ctx = new DefaultHttpContext
    {
      RequestServices = services
    };
    ctx.Request.ContentType = "application/json";
    ctx.Request.ContentLength = bytes.Length;
    ctx.Request.Body = new MemoryStream(bytes);
    ctx.Response.Body = new MemoryStream();

    var result = await ServiceEndpointsExtensions.DispatchCustomServiceMethodAsync(
      typeof(FakeAppService),
      service,
      method,
      feat,
      ctx.Request,
      services,
      CancellationToken.None);

    await result.ExecuteAsync(ctx);
    ctx.Response.Body.Position = 0;
    var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
    return (ctx.Response.StatusCode, body);
  }
}
