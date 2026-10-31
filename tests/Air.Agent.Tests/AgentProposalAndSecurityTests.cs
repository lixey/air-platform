using Platform.Agent.Internals;
using Platform.Agent.Tests.Internals;

namespace Platform.Agent.Tests;

public sealed class AgentProposalAndSecurityTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public AgentProposalAndSecurityTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    ModuleCatalog.Instance.Register(new AgentTestModuleImpl());
  }

  public void Dispose() => _connection.Dispose();

  private (IServiceProvider Services, AgentTestDbContext Db, AgentStore Store) CreateTestServices()
  {
    var options = new DbContextOptionsBuilder<AgentTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var tenant = new FixedTenant(1, 10, new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase));
    var user = new FixedUser(10, "*");
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
  public async Task AllowAgent_IsEnforcedOnDirectServiceResolution_AndQueryRecords()
  {
    var (services, db, _) = CreateTestServices();
    var app = services.GetRequiredService<PlatformAppService>();
    var descriptors = AgentFeatureRegistry.DiscoverForms(db, app);

    var secretFormPath = ServicePathResolver.Resolve(typeof(SecretFormService));
    var receiptFormPath = ServicePathResolver.Resolve(typeof(ReceiptFormService));
    var secretGridPath = ServicePathResolver.Resolve(typeof(SecretGridService));

    var resolvedRestricted = AgentFeatureRegistry.ResolveFormDescriptor(descriptors, secretFormPath, db, app);
    Assert.Null(resolvedRestricted);

    var allowedResolved = AgentFeatureRegistry.ResolveFormDescriptor(descriptors, receiptFormPath, db, app);
    Assert.NotNull(allowedResolved);

    var querySecretArgs = JsonSerializer.SerializeToElement(new { service = secretGridPath });
    await Assert.ThrowsAsync<InvalidOperationException>(async () => await AgentContextQueryService.QueryRecordsAsync(querySecretArgs, services, CancellationToken.None));
  }

  [Fact]
  public async Task CommitProposalAsync_RollsBackParentAndPrerequisites_WhenChildLineFails()
  {
    var (services, db, store) = CreateTestServices();
    var owner = AgentStore.ResolveOwner(services);
    var categoryFormPath = ServicePathResolver.Resolve(typeof(CategoryFormService));
    var receiptFormPath = ServicePathResolver.Resolve(typeof(ReceiptFormService));
    var receiptLineFormPath = ServicePathResolver.Resolve(typeof(ReceiptLineFormService));

    var prereqArgs = JsonSerializer.SerializeToElement(new
    {
      sessionId = "sess-1",
      service = categoryFormPath,
      values = new { name = "AutoParts" }
    });
    var prereqProposal = await AgentProposalBuilder.StageProposalAsync(prereqArgs, services, CancellationToken.None);

    var payload = JsonSerializer.SerializeToElement(new StagedProposalPayload
    {
      Service = receiptFormPath,
      Values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
      {
        ["title"] = "Receipt-Atomic-Test",
        ["amount"] = 100m
      },
      NewReferences =
      [
        new PendingNewReference
        {
          FieldPath = "categoryId",
          Name = "AutoParts",
          FormService = categoryFormPath,
          ProposalId = prereqProposal.Id
        }
      ],
      ChildFormService = receiptLineFormPath,
      HasLinesUpdate = true,
      LinesMode = "replace",
      Lines =
      [
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["name"] = "Valid Line", ["amount"] = 50m },
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["name"] = "Invalid Negative Line", ["amount"] = -10m }
      ]
    });

    var mainProposal = new AgentProposalDto
    {
      Id = "prop-main-1",
      SessionId = "sess-1",
      Kind = receiptFormPath,
      Service = receiptFormPath,
      Title = "Receipt-Atomic-Test",
      Payload = payload,
      Status = "pending"
    };
    store.StageProposal(mainProposal, owner);

    await Assert.ThrowsAsync<InvalidOperationException>(async () => await AgentProposalExecutor.CommitProposalAsync(mainProposal, services, CancellationToken.None));

    Assert.Equal(0, await db.Receipts.CountAsync());
    Assert.Equal(0, await db.ReceiptLines.CountAsync());
    Assert.Equal(0, await db.Categories.CountAsync());

    Assert.Equal("pending", mainProposal.Status);
    Assert.Equal("pending", prereqProposal.Status);
    Assert.Null(prereqProposal.CommittedRecordId);
  }

  [Fact]
  public async Task CommitProposalAsync_DetectsOptimisticConcurrencyConflict_WhenEditedRecordChangedAfterStaging()
  {
    var (services, db, _) = CreateTestServices();
    var receiptFormPath = ServicePathResolver.Resolve(typeof(ReceiptFormService));

    var receipt = new AgentTestReceipt { Title = "Initial", Amount = 100m };
    using (db.BeginMutationRun())
    {
      db.Receipts.Add(receipt);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    Assert.Equal(1, receipt.Ver);

    var stageEditArgs = JsonSerializer.SerializeToElement(new
    {
      sessionId = "sess-2",
      service = receiptFormPath,
      recordId = receipt.Id,
      values = new { title = "Agent Edited", amount = 200m }
    });
    var editProposal = await AgentProposalBuilder.StageProposalAsync(stageEditArgs, services, CancellationToken.None);

    using (db.BeginMutationRun())
    {
      receipt.Title = "Concurrent Edit";
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    Assert.Equal(2, receipt.Ver);

    await Assert.ThrowsAsync<InvalidOperationException>(async () => await AgentProposalExecutor.CommitProposalAsync(editProposal, services, CancellationToken.None));
  }
}
