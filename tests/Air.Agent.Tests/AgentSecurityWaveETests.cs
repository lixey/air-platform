using Microsoft.Extensions.Hosting;
using Platform.Agent.Internals;
using Platform.Agent.Providers.Internals;
using Platform.Agent.Tests.Internals;

namespace Platform.Agent.Tests;

public sealed class AgentSecurityWaveETests : IDisposable
{
  private readonly SqliteConnection _connection;

  public AgentSecurityWaveETests()
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

    var sc = new ServiceCollection();
    sc.AddSingleton<PlatformDbContext>(db);
    sc.AddSingleton<PlatformAppService>(app);
    sc.AddSingleton<ITenantContext>(tenant);
    sc.AddSingleton<IUserContext>(user);
    sc.AddSingleton(store);
    return (sc.BuildServiceProvider(), db, store);
  }

  [Fact]
  public async Task ProposalSeal_RejectsTamperedProposalFieldsOrPayload_AndAllowsUntamperedCommit()
  {
    var (services, db, store) = CreateTestServices();
    var receiptFormPath = ServicePathResolver.Resolve<ReceiptFormService>();

    var stageArgs = JsonSerializer.SerializeToElement(new
    {
      sessionId = "sess-seal",
      service = receiptFormPath,
      values = new { title = "Original Sealed Receipt", amount = 100m }
    });
    var tamperedServiceProposal = await AgentProposalBuilder.StageProposalAsync(stageArgs, services, CancellationToken.None);
    tamperedServiceProposal.Service = ServicePathResolver.Resolve<SecretFormService>();

    await Assert.ThrowsAsync<InvalidOperationException>(
      async () => await AgentProposalExecutor.CommitProposalAsync(tamperedServiceProposal, services, CancellationToken.None));
    Assert.Equal(0, await db.Receipts.CountAsync());

    var stagePayloadArgs = JsonSerializer.SerializeToElement(new
    {
      sessionId = "sess-seal-2",
      service = receiptFormPath,
      values = new { title = "Original Amount", amount = 100m }
    });
    var tamperedPayloadProposal = await AgentProposalBuilder.StageProposalAsync(stagePayloadArgs, services, CancellationToken.None);
    tamperedPayloadProposal.Payload = JsonSerializer.SerializeToElement(new StagedProposalPayload
    {
      Operation = AgentProposalOperation.Create,
      Service = receiptFormPath,
      Values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
      {
        ["title"] = "Tampered Amount",
        ["amount"] = 999_999m
      }
    });

    await Assert.ThrowsAsync<InvalidOperationException>(
      async () => await AgentProposalExecutor.CommitProposalAsync(tamperedPayloadProposal, services, CancellationToken.None));
    Assert.Equal(0, await db.Receipts.CountAsync());

    var validArgs = JsonSerializer.SerializeToElement(new
    {
      sessionId = "sess-seal-valid",
      service = receiptFormPath,
      values = new { title = "Untampered Sealed Receipt", amount = 150m }
    });
    var validProposal = await AgentProposalBuilder.StageProposalAsync(validArgs, services, CancellationToken.None);
    store.FinalizeTurnProposals("sess-seal-valid", AgentStore.ResolveOwner(services), validProposal.TurnId ?? "turn-1");

    var committed = await AgentProposalExecutor.CommitProposalAsync(validProposal, services, CancellationToken.None);
    Assert.True(committed.RecordId > 0);
    Assert.Equal(150m, (await db.Receipts.SingleAsync(r => r.Id == committed.RecordId)).Amount);
  }

  [Fact]
  public async Task InlineAgentMemory_StagesAsPendingWithoutDbWrite_RejectsWrongUser_AndPersistsOnlyAfterOwnerConfirm()
  {
    var (servicesUser10, db, store) = CreateTestServices(tenantId: 1, userId: 10);

    var tenantUser20 = new FixedTenant(1, 20, new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase));
    var user20 = new FixedUser(20, "*");
    var dbOptions = new DbContextOptionsBuilder<AgentTestDbContext>().UseSqlite(_connection).Options;
    var dbUser20 = new AgentTestDbContext(dbOptions, tenantUser20);
    var scUser20 = new ServiceCollection();
    scUser20.AddSingleton<PlatformDbContext>(dbUser20);
    scUser20.AddSingleton<PlatformAppService>(new TestPlatformAppService(dbUser20, user20));
    scUser20.AddSingleton<ITenantContext>(tenantUser20);
    scUser20.AddSingleton<IUserContext>(user20);
    scUser20.AddSingleton(store);
    await using var servicesUser20 = scUser20.BuildServiceProvider();

    var reqUser10 = new AgentTurnRequest
    {
      SessionId = "sess-confirm-mem",
      Owner = new AgentOwnerScope(1, 10),
      Message = "Remember ISO date format",
      Services = servicesUser10
    };
    var reply = """
      I can remember that preference for you.
      <agent_memory>{"kind":"Preference","title":"Date Format","content":"Always format dates as YYYY-MM-DD."}</agent_memory>
      """;

    var staged = await AgentPromptBuilder.SaveInlineMemoriesAsync(reply, reqUser10, CancellationToken.None);
    var pendingItem = Assert.Single(staged);
    Assert.Equal(0, pendingItem.Id);
    Assert.Equal(AgentSavedMemoryStatus.Pending, pendingItem.Status);
    Assert.False(string.IsNullOrWhiteSpace(pendingItem.PendingToken));

    // Zero silent database writes before explicit user confirmation.
    Assert.False(await db.AppAgentMemory.IgnoreQueryFilters().AnyAsync(m => m.Title == "Date Format"));

    // Another user cannot confirm, consume, or evict User 10's pending token via flood.
    for (var i = 0; i < PendingMemoryStore.MaxEntriesPerOwner + 10; i++)
    {
      store.PendingMemories.Stage(new AgentOwnerScope(1, 20), AgentMemoryKinds.Fact, $"Flood {i}", "v", TimeSpan.FromHours(1));
    }

    var wrongUserResult = await AgentMemoryService.ConfirmInlineMemoryAsync(
      pendingItem.PendingToken,
      new AgentOwnerScope(1, 20),
      servicesUser20,
      CancellationToken.None);
    Assert.Null(wrongUserResult);
    Assert.False(await db.AppAgentMemory.IgnoreQueryFilters().AnyAsync(m => m.Title == "Date Format"));

    // Hitting the 100-memory cap leaves User 10's pending token intact for retry after freeing a slot.
    var fillerRows = Enumerable.Range(1, 100).Select(i => new AppAgentMemory
    {
      TenantId = 1,
      UserId = 10,
      Kind = AgentMemoryKinds.Fact,
      Title = $"Cap Filler {i}",
      Content = "v",
      Priority = 100,
      IsActive = true
    }).ToList();
    using (db.BeginMutationRun())
    {
      db.AppAgentMemory.AddRange(fillerRows);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    var blockedByCap = await AgentMemoryService.ConfirmInlineMemoryAsync(
      pendingItem.PendingToken,
      new AgentOwnerScope(1, 10),
      servicesUser10,
      CancellationToken.None);
    Assert.Null(blockedByCap);

    using (db.BeginMutationRun())
    {
      db.AppAgentMemory.Remove(fillerRows[0]);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    // Owner retries with the same token -> row is persisted once and token cannot be replayed.
    var confirmed = await AgentMemoryService.ConfirmInlineMemoryAsync(
      pendingItem.PendingToken,
      new AgentOwnerScope(1, 10),
      servicesUser10,
      CancellationToken.None);
    Assert.NotNull(confirmed);
    Assert.True(confirmed.Id > 0);
    Assert.Equal(AgentSavedMemoryStatus.Saved, confirmed.Status);
    Assert.Null(confirmed.PendingToken);

    var persisted = await db.AppAgentMemory.IgnoreQueryFilters().SingleAsync(m => m.Id == confirmed.Id);
    Assert.Equal(1, persisted.TenantId);
    Assert.Equal(10, persisted.UserId);
    Assert.Equal("Always format dates as YYYY-MM-DD.", persisted.Content);

    var replay = await AgentMemoryService.ConfirmInlineMemoryAsync(
      pendingItem.PendingToken,
      new AgentOwnerScope(1, 10),
      servicesUser10,
      CancellationToken.None);
    Assert.Null(replay);
  }

  [Fact]
  public async Task AllowLocalCliInProduction_BlocksCliProvidersByDefaultInProduction_AndAllowsWhenExplicitlyEnabled()
  {
    var prodEnv = new FakeHostEnvironment(Environments.Production);
    var devEnv = new FakeHostEnvironment(Environments.Development);

    var defaultOptions = new AgentOptions();
    Assert.False(defaultOptions.AllowLocalCliInProduction);
    var storeDefault = new AgentStore(defaultOptions);

    var agyProd = new AgyCliAgentProvider(storeDefault, defaultOptions, prodEnv);
    var claudeProd = new ClaudeCliAgentProvider(storeDefault, prodEnv);
    var codexProd = new CodexCliAgentProvider(storeDefault, prodEnv);

    Assert.False(agyProd.IsAvailable);
    Assert.False(claudeProd.IsAvailable);
    Assert.False(codexProd.IsAvailable);
    Assert.Empty(await agyProd.GetModelsAsync());
    Assert.Empty(await claudeProd.GetModelsAsync());
    Assert.Empty(await codexProd.GetModelsAsync());

    var events = new List<AgentStreamEventDto>();
    await foreach (var evt in agyProd.StreamTurnAsync(
      new AgentTurnRequest { SessionId = "sess-prod", Owner = new AgentOwnerScope(1, 10), Message = "Hi" },
      CancellationToken.None))
    {
      events.Add(evt);
    }

    var errorEvt = Assert.Single(events);
    Assert.Equal(AgentStreamEventType.Error, errorEvt.Type);
    Assert.Equal(Localizer.Instance["AgentErrorCliDisabledInProduction"], errorEvt.Text);

    Assert.True(CliTurnRunner.IsCliAllowed(defaultOptions, devEnv));

    var optedInOptions = new AgentOptions { AllowLocalCliInProduction = true };
    Assert.True(CliTurnRunner.IsCliAllowed(optedInOptions, prodEnv));
  }
}
