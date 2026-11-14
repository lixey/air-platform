using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Agent.Tests.Internals;
using Platform.Api.Host.Internals;
using Platform.Api.Mutations.Internals;

namespace Platform.Agent.Tests;

public sealed class Wave6PlatformAgentServiceTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public Wave6PlatformAgentServiceTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    ModuleCatalog.Instance.Register(new AgentTestModuleImpl());
  }

  public void Dispose() => _connection.Dispose();

  private (IServiceProvider Services, AgentTestDbContext Db, AgentStore Store) CreateTestServices(long tenantId = 1, long userId = 10)
  {
    var options = new DbContextOptionsBuilder<AgentTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var tenant = new FixedTenant(tenantId, userId, new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase));
    var user = new FixedUser(userId, "*");
    var db = new AgentTestDbContext(options, tenant);
    db.Database.EnsureCreated();
    var app = new TestPlatformAppService(db, user);
    var store = new AgentStore();
    var idemStore = new IdempotencyStore();

    var sc = new ServiceCollection();
    sc.AddLogging();
    sc.AddSingleton<PlatformDbContext>(db);
    sc.AddSingleton<PlatformAppService>(app);
    sc.AddSingleton<ITenantContext>(tenant);
    sc.AddSingleton<IUserContext>(user);
    sc.AddSingleton(store);
    sc.AddSingleton(idemStore);
    sc.AddSingleton(new AgentOptions());
    sc.AddSingleton<IAgentModelSynchronizer, AgentModelSynchronizer>();
    sc.AddScoped<PlatformAgentService>();
    return (sc.BuildServiceProvider(), db, store);
  }

  [Fact]
  public async Task PlatformAgentService_GetStatusAndReset_DispatchThroughCanonicalEndpoints()
  {
    var (services, _, store) = CreateTestServices();
    var owner = new AgentOwnerScope(1, 10);
    var stageArgs = JsonSerializer.SerializeToElement(new
    {
      sessionId = "sess-reset-wave6",
      service = ServicePathResolver.Resolve<ReceiptFormService>(),
      values = new { title = "Draft Receipt", amount = 50m }
    });
    await AgentProposalBuilder.StageProposalAsync(stageArgs, services, CancellationToken.None);
    Assert.NotEmpty(store.GetSessionProposals("sess-reset-wave6", owner));

    var (statusCode, statusJson) = await InvokeAgentEndpointAsync(services, "get-status", "{}");
    Assert.Equal(StatusCodes.Status200OK, statusCode);
    var statusDto = JsonSerializer.Deserialize<AgentStatusDto>(statusJson, PlatformJsonOptions.DefaultOptions);
    Assert.NotNull(statusDto);
    Assert.NotEmpty(statusDto.ActiveProvider);

    var resetBody = JsonSerializer.Serialize(new AgentResetRequest { SessionId = "sess-reset-wave6" }, PlatformJsonOptions.DefaultOptions);
    var (resetCode, _) = await InvokeAgentEndpointAsync(services, "reset", resetBody);
    Assert.Equal(StatusCodes.Status200OK, resetCode);
    Assert.Empty(store.GetSessionProposals("sess-reset-wave6", owner));
  }

  [Fact]
  public async Task PlatformAgentService_ConfirmProposalAndMemory_EnforceIdempotencyReplay_And409OnPayloadMismatch()
  {
    var (services, db, store) = CreateTestServices();
    var receiptFormPath = ServicePathResolver.Resolve<ReceiptFormService>();

    // 1. Stage 2 proposals and confirm p1 with Idempotency-Key
    var p1 = await AgentProposalBuilder.StageProposalAsync(
      JsonSerializer.SerializeToElement(new
      {
        sessionId = "sess-idem-1",
        service = receiptFormPath,
        values = new { title = "Idempotent Receipt 1", amount = 75m }
      }),
      services,
      CancellationToken.None);
    var p2 = await AgentProposalBuilder.StageProposalAsync(
      JsonSerializer.SerializeToElement(new
      {
        sessionId = "sess-idem-2",
        service = receiptFormPath,
        values = new { title = "Idempotent Receipt 2", amount = 90m }
      }),
      services,
      CancellationToken.None);
    var owner = AgentStore.ResolveOwner(services);
    store.FinalizeTurnProposals("sess-idem-1", owner, p1.TurnId ?? "t1");
    store.FinalizeTurnProposals("sess-idem-2", owner, p2.TurnId ?? "t2");

    var confirmBody1 = JsonSerializer.Serialize(new ConfirmProposalRequest { ProposalId = p1.Id }, PlatformJsonOptions.DefaultOptions);
    var confirmBody2 = JsonSerializer.Serialize(new ConfirmProposalRequest { ProposalId = p2.Id }, PlatformJsonOptions.DefaultOptions);

    var (firstCode, firstJson) = await InvokeAgentEndpointAsync(services, "confirm-proposal", confirmBody1, "idem-prop-key-1");
    Assert.Equal(StatusCodes.Status200OK, firstCode);
    Assert.Equal(1, await db.Receipts.CountAsync());

    // Duplicate network retry with same Idempotency-Key + same payload replays 200 OK without duplicating DB rows
    var (replayCode, replayJson) = await InvokeAgentEndpointAsync(services, "confirm-proposal", confirmBody1, "idem-prop-key-1");
    Assert.Equal(StatusCodes.Status200OK, replayCode);
    Assert.Equal(firstJson, replayJson);
    Assert.Equal(1, await db.Receipts.CountAsync());

    // Reusing the same Idempotency-Key with a DIFFERENT proposalId returns 409 Conflict!
    var (conflictCode, _) = await InvokeAgentEndpointAsync(services, "confirm-proposal", confirmBody2, "idem-prop-key-1");
    Assert.Equal(StatusCodes.Status409Conflict, conflictCode);
    Assert.Equal(1, await db.Receipts.CountAsync());

    // 2. One-shot ConfirmMemory + row-deleting UndoMemory replay 200 OK on retry instead of spurious 404!
    var token = store.PendingMemories.Stage(owner, AgentMemoryKinds.Preference, "Currency", "Prefer USD", TimeSpan.FromHours(1));
    var memConfirmBody = JsonSerializer.Serialize(new AgentMemoryConfirmRequest { PendingToken = token }, PlatformJsonOptions.DefaultOptions);

    var (memCode1, memJson1) = await InvokeAgentEndpointAsync(services, "confirm-memory", memConfirmBody, "idem-mem-confirm-1");
    var (memCode2, memJson2) = await InvokeAgentEndpointAsync(services, "confirm-memory", memConfirmBody, "idem-mem-confirm-1");
    Assert.Equal(StatusCodes.Status200OK, memCode1);
    Assert.Equal(StatusCodes.Status200OK, memCode2);
    Assert.Equal(memJson1, memJson2);

    var savedDto = JsonSerializer.Deserialize<AgentSavedMemoryDto>(memJson1, PlatformJsonOptions.DefaultOptions)!;
    var undoBody = JsonSerializer.Serialize(new AgentMemoryUndoRequest { Id = savedDto.Id }, PlatformJsonOptions.DefaultOptions);

    var (undoCode1, _) = await InvokeAgentEndpointAsync(services, "undo-memory", undoBody, "idem-mem-undo-1");
    var (undoCode2, _) = await InvokeAgentEndpointAsync(services, "undo-memory", undoBody, "idem-mem-undo-1");
    Assert.Equal(StatusCodes.Status200OK, undoCode1);
    Assert.Equal(StatusCodes.Status200OK, undoCode2);
  }

  [Fact]
  public async Task PlatformAgentService_MapsDomainRefusalTo422_WithoutLeakingInternalExceptions()
  {
    var (services, _, store) = CreateTestServices();
    var receiptFormPath = ServicePathResolver.Resolve<ReceiptFormService>();

    // 1. Tampered proposal seal -> domain refusal (422 UnprocessableEntity)
    var tampered = await AgentProposalBuilder.StageProposalAsync(
      JsonSerializer.SerializeToElement(new
      {
        sessionId = "sess-tamper-wave6",
        service = receiptFormPath,
        values = new { title = "Tamper Test", amount = 25m }
      }),
      services,
      CancellationToken.None);
    tampered.Payload = JsonSerializer.SerializeToElement(new { hacked = true });
    var body = JsonSerializer.Serialize(new ConfirmProposalRequest { ProposalId = tampered.Id }, PlatformJsonOptions.DefaultOptions);

    var (refusedCode, _) = await InvokeAgentEndpointAsync(services, "confirm-proposal", body);
    Assert.Equal(StatusCodes.Status422UnprocessableEntity, refusedCode);

    // 2. Unexpected internal exception (not tagged via AgentRefusal) is NOT swallowed as 422;
    //    SafeExceptionMiddleware turns it into 500 without leaking raw exception message!
    var sc = new ServiceCollection();
    sc.AddLogging();
    sc.AddSingleton(store);
    sc.AddSingleton(new AgentOptions());
    sc.AddSingleton<IAgentModelSynchronizer, AgentModelSynchronizer>();
    sc.AddSingleton<IUserContext>(new FixedUser(10, "*"));
    sc.AddSingleton<ITenantContext>(new FixedTenant(1, 10, new HashSet<string>(["*"])));
    sc.AddScoped<PlatformAgentService>();
    await using var minimalServices = sc.BuildServiceProvider();

    var safeMiddleware = new SafeExceptionMiddleware(
      _ => throw new InvalidOperationException("Internal SQL failure: TopSecretDbHunter2!"),
      NullLogger<SafeExceptionMiddleware>.Instance);
    var errCtx = new DefaultHttpContext { RequestServices = minimalServices, TraceIdentifier = "trace-agent-500" };
    errCtx.Response.Body = new MemoryStream();
    await safeMiddleware.InvokeAsync(errCtx);
    Assert.Equal(StatusCodes.Status500InternalServerError, errCtx.Response.StatusCode);
    errCtx.Response.Body.Position = 0;
    var errBody = await new StreamReader(errCtx.Response.Body).ReadToEndAsync();
    Assert.DoesNotContain("TopSecretDbHunter2", errBody, StringComparison.Ordinal);
  }

  private static async Task<(int StatusCode, string Body)> InvokeAgentEndpointAsync(
    IServiceProvider rootServices,
    string action,
    string jsonBody,
    string? idempotencyKey = null)
  {
    await using var scope = rootServices.CreateAsyncScope();
    var ctx = new DefaultHttpContext
    {
      RequestServices = scope.ServiceProvider,
      User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "10")], "TestAuth"))
    };
    var bytes = Encoding.UTF8.GetBytes(jsonBody);
    ctx.Request.ContentType = "application/json";
    ctx.Request.ContentLength = bytes.Length;
    ctx.Request.Body = new MemoryStream(bytes);
    if (!string.IsNullOrEmpty(idempotencyKey))
    {
      ctx.Request.Headers["Idempotency-Key"] = idempotencyKey;
    }

    ctx.Response.Body = new MemoryStream();
    var svc = scope.ServiceProvider.GetRequiredService<PlatformAgentService>();
    var res = await PlatformEndpointsExtensions.WithRequestCultureAsync(
      ctx.Request,
      _ => EndpointExecutor.DispatchAsync(
        typeof(PlatformAgentService),
        svc,
        action,
        svc.Feature,
        ctx.Request,
        scope.ServiceProvider,
        CancellationToken.None));
    await res.ExecuteAsync(ctx);
    ctx.Response.Body.Position = 0;
    return (ctx.Response.StatusCode, await new StreamReader(ctx.Response.Body).ReadToEndAsync());
  }
}
