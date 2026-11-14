using System.Globalization;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

public sealed class Wave5RbacIdempotencyAndPickTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public Wave5RbacIdempotencyAndPickTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    ModuleCatalog.Instance.Register(new TestAppModuleImpl());
  }

  public void Dispose() => _connection.Dispose();

  private AppTestDbContext CreateDbContext(long tenantId = 1, long userId = 10, params string[] rights)
  {
    var options = new DbContextOptionsBuilder<AppTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var effectiveRights = rights.Length > 0
      ? new HashSet<string>(rights, StringComparer.OrdinalIgnoreCase)
      : new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase);
    var db = new AppTestDbContext(options, new FixedTenant(tenantId, userId, effectiveRights));
    db.Database.EnsureCreated();
    return db;
  }

  private static IEnumerable<NavElementModel> FlattenNavElements(PanelNodeDto node)
  {
    static IEnumerable<NavElementModel> FromNav(NavModel nav)
    {
      foreach (var item in nav.Start.Concat(nav.Elements).Concat(nav.End).Concat(nav.More))
      {
        yield return item;
        if (item is GridNavStatusModel statusModel)
        {
          foreach (var move in statusModel.Moves)
          {
            yield return move.Action;
          }
        }
      }
    }

    if (node.Model is NavModel navModel)
    {
      foreach (var item in FromNav(navModel))
      {
        yield return item;
      }
    }
    else if (node.Model is GridModel gridModel)
    {
      foreach (var item in FromNav(gridModel.Nav).Concat(FromNav(gridModel.ActionNav)))
      {
        yield return item;
      }
    }

    foreach (var child in node.Children ?? [])
    {
      foreach (var item in FlattenNavElements(child))
      {
        yield return item;
      }
    }
  }

  private static TModel FindPanelModel<TModel>(PanelNodeDto node) where TModel : PanelModel
  {
    if (node.Model is TModel match)
    {
      return match;
    }

    foreach (var child in node.Children ?? [])
    {
      try
      {
        return FindPanelModel<TModel>(child);
      }
      catch (InvalidOperationException)
      {
      }
    }

    throw new InvalidOperationException($"Panel model '{typeof(TModel).Name}' not found.");
  }

  [Fact]
  public async Task SaveCellAsync_IsIdempotentEndToEnd_AndRejectsDifferentPayloadOnSameKey()
  {
    await using var db = CreateDbContext();
    var user = new FixedUser(10, "*");
    var app = new TestPlatformAppService(db, user);
    var services = new ServiceCollection()
      .AddLogging()
      .AddSingleton<PlatformDbContext>(db)
      .AddSingleton(db)
      .AddSingleton<IUserContext>(user)
      .AddSingleton<PlatformAppService>(app)
      .AddTransient<TestInvoiceFormService>()
      .AddTransient<TestInvoiceGridService>()
      .BuildServiceProvider();

    var grid = services.GetRequiredService<TestInvoiceGridService>();

    var inv = new TestInvoice { Number = "INV-100", Status = 1 };
    db.Invoices.Add(inv);
    await db.SaveChangesAsync();
    var initialVer = inv.Ver;

    var key = $"cell-{Guid.NewGuid():N}";
    var rawPayload = $$"""{"id":{{inv.Id}},"ver":{{initialVer}},"path":"number","value":"INV-200"}""";

    var first = await grid.SaveCellAsync(
      new GridCellRequest
      {
        Id = inv.Id,
        Ver = initialVer,
        Path = "number",
        Value = JsonSerializer.SerializeToElement("INV-200"),
        IdempotencyKey = key,
        RawPayload = rawPayload
      },
      services,
      CancellationToken.None);

    Assert.Equal(MutationOutcome.Committed, first.Outcome);
    var afterFirst = await db.Invoices.AsNoTracking().FirstAsync(x => x.Id == inv.Id);
    Assert.Equal("INV-200", afterFirst.Number);
    Assert.Equal(initialVer + 1, afterFirst.Ver);

    // Replay with the same IdempotencyKey and stale Ver returns Committed without double-incrementing Ver
    var replay = await grid.SaveCellAsync(
      new GridCellRequest
      {
        Id = inv.Id,
        Ver = initialVer,
        Path = "number",
        Value = JsonSerializer.SerializeToElement("INV-200"),
        IdempotencyKey = key,
        RawPayload = rawPayload
      },
      services,
      CancellationToken.None);

    Assert.Equal(MutationOutcome.Committed, replay.Outcome);
    var afterReplay = await db.Invoices.AsNoTracking().FirstAsync(x => x.Id == inv.Id);
    Assert.Equal(initialVer + 1, afterReplay.Ver);

    // Reusing the same IdempotencyKey with a different payload throws IdempotencyConflictException (409)
    await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
      grid.SaveCellAsync(
        new GridCellRequest
        {
          Id = inv.Id,
          Ver = initialVer,
          Path = "number",
          Value = JsonSerializer.SerializeToElement("INV-300"),
          IdempotencyKey = key,
          RawPayload = $$"""{"id":{{inv.Id}},"ver":{{initialVer}},"path":"number","value":"INV-300"}"""
        },
        services,
        CancellationToken.None));
  }

  [Fact]
  public async Task UnifiedCanMove_EnforcesViewOnlyDenial_TransitionRightWithoutEdit_AndExplicitAllowOnReadOnlyFeature()
  {
    await using var db = CreateDbContext();
    var inv = new TestInvoice { Number = "INV-FLOW-1", Status = 1 };
    var ord = new TestInvoice { Number = "ORD-FLOW-1", Status = 1 };
    db.Invoices.AddRange(inv, ord);
    await db.SaveChangesAsync();

    // 1. View-only user cannot see or execute transitions
    var viewOnlyUser = new FixedUser(11, "Test.OrderDocs.View");
    var viewOnlyApp = new TestPlatformAppService(db, viewOnlyUser);
    Assert.False(Feature.CanMove(TestAppModule.OrderDocs, viewOnlyUser, "Complete"));

    var viewGrid = new TestOrderMasterGridService(db, viewOnlyApp);
    var viewGridScreen = await viewGrid.BuildScreenAsync("test/order-docs", new GridRequest(), CancellationToken.None);
    Assert.DoesNotContain(FlattenNavElements(viewGridScreen.Layout!.Root), n => n.Key == "status.complete");

    var viewDeniedMove = await viewGrid.MoveStatusAsync(
      new GridRequest { SelectedIds = [ord.Id] },
      "Complete",
      null,
      CancellationToken.None);
    Assert.Equal(MutationOutcome.Refused, viewDeniedMove.Outcome);

    // 2. User with View + dedicated transition right (WITHOUT Edit!) sees transition on read-only Form & Grid AND executes it
    var approveOnlyUser = new FixedUser(12, "Test.Invoices.View", "Test.Invoices.Approve");
    var approveOnlyApp = new TestPlatformAppService(db, approveOnlyUser);

    var invForm = new TestInvoiceFormService(db, approveOnlyApp);
    var invFormScreen = await invForm.BuildScreenAsync("test/invoices", inv.Id, CancellationToken.None);
    Assert.True(FindPanelModel<FormModel>(invFormScreen.Layout!.Root).IsReadOnly);
    Assert.Contains(FlattenNavElements(invFormScreen.Layout!.Root), n => n.Key == "status.approve");

    var invGrid = new TestInvoiceGridService(db, approveOnlyApp);
    var invGridScreen = await invGrid.BuildScreenAsync("test/invoices", new GridRequest(), CancellationToken.None);
    Assert.True(FindPanelModel<GridModel>(invGridScreen.Layout!.Root).IsReadOnly);
    Assert.Contains(FlattenNavElements(invGridScreen.Layout!.Root), n => n.Key == "status.approve");

    var approvedRes = await invForm.MoveStatusAsync(inv.Id, "Approve", null, inv.Ver, CancellationToken.None);
    Assert.Equal(MutationOutcome.Committed, approvedRes.Outcome);
    Assert.Equal(2L, (await db.Invoices.AsNoTracking().FirstAsync(x => x.Id == inv.Id)).Status);

    // 3. User with Edit ONLY (without dedicated Complete right) is denied Complete, while View + Complete succeeds
    var editWithoutCompleteUser = new FixedUser(13, "Test.OrderDocs.View", "Test.OrderDocs.Edit");
    Assert.False(Feature.CanMove(TestAppModule.OrderDocs, editWithoutCompleteUser, "Complete"));

    var completeOnlyUser = new FixedUser(14, "Test.OrderDocs.View", "Test.OrderDocs.Complete");
    var completeOnlyApp = new TestPlatformAppService(db, completeOnlyUser);
    var ordGrid = new TestOrderMasterGridService(db, completeOnlyApp);
    var ordScreen = await ordGrid.BuildScreenAsync("test/order-docs", new GridRequest(), CancellationToken.None);
    var navKeys = FlattenNavElements(ordScreen.Layout!.Root).Select(n => n.Key).ToHashSet();
    Assert.Contains("status.complete", navKeys);
    Assert.DoesNotContain("status.cancel", navKeys);

    var completedRes = await ordGrid.MoveStatusAsync(
      new GridRequest { SelectedIds = [ord.Id] },
      "Complete",
      null,
      CancellationToken.None);
    Assert.Equal(MutationOutcome.Committed, completedRes.Outcome);

    // 4. ReadOnly feature denies default transitions, but allows transition with explicit .Allow(explicitRight)
    var roFeature = new Feature("Test", "ReadOnlyJournal").ReadOnly();
    var overrideRight = roFeature.Action("ForceSync");
    var overrideUser = new FixedUser(15, "Test.ReadOnlyJournal.View", overrideRight.Key);
    Assert.False(Feature.CanMove(roFeature, overrideUser, "ForceSync", explicitRight: null));
    Assert.True(Feature.CanMove(roFeature, overrideUser, "ForceSync", explicitRight: overrideRight));

    // 5. Service-level record lock (override IsReadOnly => base.IsReadOnly || ...) blocks moves on locked records,
    //    while still permitting View + Approve (without Edit) on unlocked records
    var lockedInv = new TestInvoice { Number = "INV-LOCKED-1", Status = 1, PrintedCount = 99 };
    var unlockedInv = new TestInvoice { Number = "INV-UNLOCKED-1", Status = 1, PrintedCount = 0 };
    db.Invoices.AddRange(lockedInv, unlockedInv);
    await db.SaveChangesAsync();

    var lockedForm = new MockLockedInvoiceFormService(db, approveOnlyApp);
    var lockedScreen = await lockedForm.BuildScreenAsync("test/invoices", lockedInv.Id, CancellationToken.None);
    Assert.DoesNotContain(FlattenNavElements(lockedScreen.Layout!.Root), n => n.Key == "status.approve");

    var lockedMoveRes = await lockedForm.MoveStatusAsync(lockedInv.Id, "Approve", null, lockedInv.Ver, CancellationToken.None);
    Assert.Equal(MutationOutcome.Refused, lockedMoveRes.Outcome);

    var unlockedScreen = await lockedForm.BuildScreenAsync("test/invoices", unlockedInv.Id, CancellationToken.None);
    Assert.True(FindPanelModel<FormModel>(unlockedScreen.Layout!.Root).IsReadOnly);
    Assert.Contains(FlattenNavElements(unlockedScreen.Layout!.Root), n => n.Key == "status.approve");

    var unlockedMoveRes = await lockedForm.MoveStatusAsync(unlockedInv.Id, "Approve", null, unlockedInv.Ver, CancellationToken.None);
    Assert.Equal(MutationOutcome.Committed, unlockedMoveRes.Outcome);
    Assert.Equal(2L, (await db.Invoices.AsNoTracking().FirstAsync(x => x.Id == unlockedInv.Id)).Status);
  }

  [Fact]
  public async Task GridPick_FailsClosedOnUnregisteredAction_RejectsFilteredRecord_AndAttachesValidRecord()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));

    var parentInv = new TestInvoice { Number = "INV-PICK-1", Status = 1 };
    var activeTag = new TestTag { Name = "Priority", IsActive = true };
    var inactiveTag = new TestTag { Name = "Archived", IsActive = false };
    db.Invoices.Add(parentInv);
    db.Tags.AddRange(activeTag, inactiveTag);
    await db.SaveChangesAsync();

    var parentPath = ServicePathResolver.Resolve(typeof(TestInvoiceFormService));
    var lineGrid = new TestInvoiceLineGridService(db, app);

    // 1. Unregistered picker action fails closed even for a valid active tag
    var unregisteredRes = await lineGrid.AttachUnregisteredTag(
      new GridPickRequest { ParentService = parentPath, ParentId = parentInv.Id, PickedId = activeTag.Id });
    Assert.Equal(MutationOutcome.Refused, unregisteredRes.Outcome);

    // 2. Registered picker action enforces TestTagGridService's Query.Where(t => t.IsActive)
    var inactiveRes = await lineGrid.AttachTag(
      new GridPickRequest { ParentService = parentPath, ParentId = parentInv.Id, PickedId = inactiveTag.Id });
    Assert.Equal(MutationOutcome.Refused, inactiveRes.Outcome);

    // 3. Valid active tag attaches cleanly
    var validRes = await lineGrid.AttachTag(
      new GridPickRequest { ParentService = parentPath, ParentId = parentInv.Id, PickedId = activeTag.Id });
    Assert.Equal(MutationOutcome.Committed, validRes.Outcome);
    var savedLine = Assert.Single(await db.InvoiceLines.AsNoTracking().Where(l => l.InvoiceId == parentInv.Id).ToListAsync());
    Assert.Equal("Priority", savedLine.Title);
  }

  [Fact]
  public async Task ReadOnlyEntityService_EnforcesIsReadOnlyAndStartupCheck_AndAsSelectLocalizesPerCulture()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));

    // 1. Even if .ReadOnly() was forgotten on Feature, GridService<AppLanguage> renders IsReadOnly = true and ValidateConfiguration fails fast
    var misconfiguredGrid = new MockBadReadOnlyEntityGridService(db, app);
    var badScreen = await misconfiguredGrid.BuildScreenAsync("test/bad-lang", new GridRequest(), CancellationToken.None);
    Assert.True(FindPanelModel<GridModel>(badScreen.Layout!.Root).IsReadOnly);

    var cfgEx = Assert.Throws<InvalidOperationException>(() =>
      ((IPlatformService)misconfiguredGrid).ValidateConfiguration(ModuleCatalog.Instance));
    Assert.Contains("IReadOnlyEntity", cfgEx.Message, StringComparison.Ordinal);

    // 2. AsSelect option labels localize dynamically per request culture instead of freezing startup culture
    var prevCulture = CultureInfo.CurrentUICulture;
    try
    {
      var tagForm = new TestTagEnvelopeFormService(db, app);

      CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
      var enScreen = await tagForm.BuildScreenAsync("test/tags", (long?)null, CancellationToken.None);
      var enSelect = Assert.IsType<FormSelectModel>(FindPanelModel<FormModel>(enScreen.Layout!.Root).Elements.First(e => e.Path == "categoryId").Model);
      Assert.Equal("Yes", Assert.Single(enSelect.Options!).Name);

      CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");
      var ukScreen = await tagForm.BuildScreenAsync("test/tags", (long?)null, CancellationToken.None);
      var ukSelect = Assert.IsType<FormSelectModel>(FindPanelModel<FormModel>(ukScreen.Layout!.Root).Elements.First(e => e.Path == "categoryId").Model);
      Assert.Equal("Так", Assert.Single(ukSelect.Options!).Name);
    }
    finally
    {
      CultureInfo.CurrentUICulture = prevCulture;
    }
  }
}
