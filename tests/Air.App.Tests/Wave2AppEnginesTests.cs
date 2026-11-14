using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Platform.Api.Host.Internals;
using Platform.App.Internals;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

[Collection("ModuleCatalog")]
public sealed class Wave2AppEnginesTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public Wave2AppEnginesTests()
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

  [Fact]
  public async Task AbstractParent_EnforcesConcreteRbacAndReachable_AndExecutesMultiFieldTypedSumToAcrossMoves()
  {
    await using var db = CreateDbContext();

    var salesDoc = new TestInvoice { Number = "SAL-01", Status = 10 };
    var hiddenSalesDoc = new TestInvoice { Number = "SAL-HIDDEN", Status = 10, IsHidden = true };
    var purchaseDoc = new TestInvoice { Number = "PUR-01", Status = 20 };
    db.Invoices.AddRange(salesDoc, hiddenSalesDoc, purchaseDoc);
    await db.SaveChangesAsync();

    // 1. User with ONLY SalesDocs.View + DocLines.* (NO PurchaseDocs.View):
    //    Omitting ParentService must fail closed on PurchaseDoc and HiddenSalesDoc (never raw db.FindAsync!),
    //    while succeeding on reachable SalesDoc and executing multi-field typed .SumTo + RecalculateAsync.
    var salesOnlyUser = new FixedUser(
      10,
      TestAppModule.SalesDocs.View,
      TestAppModule.DocLines.View,
      TestAppModule.DocLines.Edit,
      TestAppModule.DocLines.Delete);
    var salesOnlyApp = new TestPlatformAppService(db, salesOnlyUser);

    var unauthorizedGrid = new TestDocLineGridService(db, salesOnlyApp);
    var blockedPurchasePage = await unauthorizedGrid.GetDataAsync(
      new GridRequest { ParentId = purchaseDoc.Id, IsDataOnly = true },
      CancellationToken.None);
    Assert.Empty(blockedPurchasePage.Rows);

    var blockedHiddenPage = await unauthorizedGrid.GetDataAsync(
      new GridRequest { ParentId = hiddenSalesDoc.Id, IsDataOnly = true },
      CancellationToken.None);
    Assert.Empty(blockedHiddenPage.Rows);

    var createPayload1 = JsonSerializer.SerializeToElement(new { title = "Item 1", amount = 30.25m, tierCode = 2 });
    var blockedSaveForm = new TestDocLineFormService(db, salesOnlyApp);
    await Assert.ThrowsAsync<FormRefusedException>(() => blockedSaveForm.SaveAsync(
      createPayload1,
      new FormRequest { ParentId = purchaseDoc.Id, Value = createPayload1 },
      CancellationToken.None));

    await Assert.ThrowsAsync<FormRefusedException>(() => blockedSaveForm.SaveAsync(
      createPayload1,
      new FormRequest { ParentId = hiddenSalesDoc.Id, Value = createPayload1 },
      CancellationToken.None));

    // 2. Saving two child lines with ParentService = null resolves TestSalesDocFormService,
    //    rolls up BOTH .SumTo(AuditedTotal <- Amount) and .SumTo(PrintedCount <- TierCode),
    //    and then runs RecalculateAsync (CachedLineTotal = AuditedTotal * 2).
    var salesSaveForm1 = new TestDocLineFormService(db, salesOnlyApp);
    var line1 = Assert.IsType<TestInvoiceLine>(await salesSaveForm1.SaveAsync(
      createPayload1,
      new FormRequest { ParentId = salesDoc.Id, Value = createPayload1 },
      CancellationToken.None));

    var createPayload2 = JsonSerializer.SerializeToElement(new { title = "Item 2", amount = 19.75m, tierCode = 3 });
    var salesSaveForm2 = new TestDocLineFormService(db, salesOnlyApp);
    var line2 = Assert.IsType<TestInvoiceLine>(await salesSaveForm2.SaveAsync(
      createPayload2,
      new FormRequest { ParentId = salesDoc.Id, Value = createPayload2 },
      CancellationToken.None));

    var afterCreateSales = await db.Invoices.AsNoTracking().FirstAsync(d => d.Id == salesDoc.Id);
    Assert.Equal(50.00m, afterCreateSales.AuditedTotal);
    Assert.Equal(5, afterCreateSales.PrintedCount);
    Assert.Equal(100.00m, afterCreateSales.CachedLineTotal);

    // 3. Moving line2 from SalesDoc (Status=10) to PurchaseDoc (Status=20) with ParentService = null
    //    recalculates BOTH concrete parent subclasses cleanly!
    var fullApp = new TestPlatformAppService(db, new FixedUser(10, "*"));
    var moveAskBody = JsonSerializer.SerializeToElement(new { tagId = purchaseDoc.Id });
    var moveForm = new TestDocLineFormService(db, fullApp);
    var moveRes = await moveForm.ExecuteDeclaredActionAsync(
      "move-to-doc",
      new FormRequest { Id = line2.Id, Ver = line2.Ver },
      moveAskBody,
      CancellationToken.None);
    Assert.NotNull(moveRes);
    Assert.Equal(MutationOutcome.Committed, moveRes.Outcome);
    Assert.Equal(2, moveRes.Affected?.Count);

    var afterMoveSales = await db.Invoices.AsNoTracking().FirstAsync(d => d.Id == salesDoc.Id);
    var afterMovePurchase = await db.Invoices.AsNoTracking().FirstAsync(d => d.Id == purchaseDoc.Id);
    Assert.Equal(30.25m, afterMoveSales.AuditedTotal);
    Assert.Equal(2, afterMoveSales.PrintedCount);
    Assert.Equal(60.50m, afterMoveSales.CachedLineTotal);

    Assert.Equal(19.75m, afterMovePurchase.AuditedTotal);
    Assert.Equal(3, afterMovePurchase.PrintedCount);
    Assert.Equal(39.50m, afterMovePurchase.CachedLineTotal);

    // 4. Deleting line1 with ParentService = null recalculates SalesDoc back to 0
    var deleteGrid = new TestDocLineGridService(db, salesOnlyApp);
    var delRes = await deleteGrid.DeleteAsync(
      new GridRequest { SelectedIds = [line1.Id], SelectedVersions = [line1.Ver] },
      CancellationToken.None);
    Assert.Equal(MutationOutcome.Committed, delRes.Outcome);

    var afterDeleteSales = await db.Invoices.AsNoTracking().FirstAsync(d => d.Id == salesDoc.Id);
    Assert.Equal(0m, afterDeleteSales.AuditedTotal);
    Assert.Equal(0, afterDeleteSales.PrintedCount);
    Assert.Equal(0m, afterDeleteSales.CachedLineTotal);
  }

  [Fact]
  public async Task ChangeGridService_SharedTableGuard_BlocksUnreachableAndSiblingFeatureSnapshots()
  {
    await using var db = CreateDbContext();

    var salesDoc = new TestInvoice { Number = "SAL-100", Status = 10 };
    var hiddenSalesDoc = new TestInvoice { Number = "SAL-SECRET", Status = 10, IsHidden = true };
    var purchaseDoc = new TestInvoice { Number = "PUR-200", Status = 20 };
    using (db.BeginMutationRun())
    {
      db.Invoices.AddRange(salesDoc, hiddenSalesDoc, purchaseDoc);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    var salesChange = await db.AppPlatformChange.SingleAsync(c => c.Table == nameof(AppTestDbContext.Invoices) && c.RowId == salesDoc.Id);
    var purchaseChange = await db.AppPlatformChange.SingleAsync(c => c.Table == nameof(AppTestDbContext.Invoices) && c.RowId == purchaseDoc.Id);

    var salesHistoryUser = new FixedUser(
      10,
      TestAppModule.SalesDocs.View,
      TestAppModule.SalesDocs.History);
    var salesHistoryApp = new TestPlatformAppService(db, salesHistoryUser);
    var salesFormPath = ServicePathResolver.Resolve(typeof(TestSalesDocFormService));

    // 1. Forging SalesDocs ParentService + PurchaseDoc ParentId returns 0 rows
    var changeGrid1 = new TestChangeGridService(db, salesHistoryApp);
    var blockedSiblingData = await changeGrid1.GetDataAsync(
      new GridRequest { ParentService = salesFormPath, ParentId = purchaseDoc.Id, IsDataOnly = true, Sort = [SortRule.Desc("id")] },
      CancellationToken.None);
    Assert.Empty(blockedSiblingData.Rows);

    // 2. Forging SalesDocs ParentService + unreachable HiddenSalesDoc ParentId returns 0 rows
    var changeGrid2 = new TestChangeGridService(db, salesHistoryApp);
    var blockedHiddenData = await changeGrid2.GetDataAsync(
      new GridRequest { ParentService = salesFormPath, ParentId = hiddenSalesDoc.Id, IsDataOnly = true, Sort = [SortRule.Desc("id")] },
      CancellationToken.None);
    Assert.Empty(blockedHiddenData.Rows);

    // 3. Forging BuildDiffScreenAsync with PurchaseDoc ParentId returns empty close bar (no ChangeDiffModel)
    var changeGrid3 = new TestChangeGridService(db, salesHistoryApp);
    var blockedDiffScreen = await changeGrid3.BuildDiffScreenAsync(
      "test/change-grid",
      new GridRequest { Id = purchaseChange.Id, ParentService = salesFormPath, ParentId = purchaseDoc.Id },
      CancellationToken.None);
    Assert.DoesNotContain(
      FlattenModels(blockedDiffScreen.Layout!.Root),
      m => m is ChangeDiffModel);

    // 4. Legitimate SalesDoc ParentId returns salesChange AND strips unauthorized purchaseChange from RelatedChanges siblings!
    var changeGrid4 = new TestChangeGridService(db, salesHistoryApp);
    var allowedData = await changeGrid4.GetDataAsync(
      new GridRequest { ParentService = salesFormPath, ParentId = salesDoc.Id, IsDataOnly = true, Sort = [SortRule.Desc("id")] },
      CancellationToken.None);
    Assert.Single(allowedData.Rows, r => r.Id == salesChange.Id);

    var changeGrid5 = new TestChangeGridService(db, salesHistoryApp);
    var allowedDiffScreen = await changeGrid5.BuildDiffScreenAsync(
      "test/change-grid",
      new GridRequest { Id = salesChange.Id, ParentService = salesFormPath, ParentId = salesDoc.Id },
      CancellationToken.None);
    var diffModel = Assert.Single(FlattenModels(allowedDiffScreen.Layout!.Root).OfType<ChangeDiffModel>());
    Assert.DoesNotContain(diffModel.RelatedChanges, r => r.Id == purchaseChange.Id);
  }

  [Fact]
  public async Task ReferenceLookupAllowlist_WarmsAskElementsAcrossFormGridFlow_AndAllowsAllFilterNodeOperators()
  {
    await using var db = CreateDbContext();
    var user = new FixedUser(10, TestAppModule.AskWarmup.View);
    var app = new TestPlatformAppService(db, user);

    for (var i = 1; i <= 55; i++)
    {
      db.Tags.Add(new TestTag
      {
        Name = $"Tag-{i:D2}",
        IsActive = true,
        DefaultPrice = i * 10m,
        CategoryId = i % 2 == 0 ? 5L : null,
        Entity = i == 4 ? "FlowTag" : "Other",
        Value = i % 3 == 0 ? "set" : null
      });
    }

    await db.SaveChangesAsync();

    var services = new ServiceCollection()
      .AddSingleton<PlatformDbContext>(db)
      .AddSingleton(db)
      .AddSingleton<IUserContext>(user)
      .AddSingleton<PlatformAppService>(app)
      .BuildServiceProvider();

    var prevCatalog = ModuleCatalog.Instance;
    try
    {
      var freshCatalog = new ModuleCatalog();
      freshCatalog.Register(new TestAppModuleImpl());
      ModuleCatalog.Instance = freshCatalog;

      await ReferenceLookupAllowlist.EnsureWarmAsync(services, CancellationToken.None);

      // Warmup must have registered:
      // - FormAction Ask: name, defaultPrice
      // - GridAction Ask: value, categoryId
      // - StatusFlow Transition Ask: entity, categoryId, defaultPrice
      var allTenOperatorsFilter = FilterNode.All(
        FilterNode.Rule("name", FilterOperator.NotEquals, "Blocked"),
        FilterNode.Rule("entity", FilterOperator.Equals, "FlowTag"),
        FilterNode.Rule("value", FilterOperator.IsNull),
        FilterNode.Rule("categoryId", FilterOperator.IsNotNull),
        FilterNode.Rule("categoryId", FilterOperator.In, new[] { 4L, 5L, 6L }),
        FilterNode.Rule("categoryId", FilterOperator.NotIn, new[] { 99L, 100L }),
        FilterNode.Rule("defaultPrice", FilterOperator.GreaterThan, 10m),
        FilterNode.Rule("defaultPrice", FilterOperator.GreaterThanOrEqual, 20m),
        FilterNode.Rule("defaultPrice", FilterOperator.LessThan, 900m),
        FilterNode.Rule("defaultPrice", FilterOperator.LessThanOrEqual, 800m));

      Assert.True(ReferenceLookupAllowlist.TrySanitizeFilter(typeof(TestTagGridService), allTenOperatorsFilter, out var sanitizedAll));
      Assert.NotNull(sanitizedAll);

      // Disallowed operators (Contains) and unallowed columns must still be rejected
      var containsFilter = FilterNode.Rule("name", FilterOperator.Contains, "Tag");
      Assert.False(ReferenceLookupAllowlist.TrySanitizeFilter(typeof(TestTagGridService), containsFilter, out _));

      var unallowedColumnFilter = FilterNode.Rule("secretColumn", FilterOperator.Equals, "x");
      Assert.False(ReferenceLookupAllowlist.TrySanitizeFilter(typeof(TestTagGridService), unallowedColumnFilter, out _));

      // Also verify StatusFlowService .Check localizes via L[refusal] and typed FollowedBy<TFlow>(fk) resolves cleanly
      var prevUi = CultureInfo.CurrentUICulture;
      try
      {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var inv = new TestInvoice { Number = "INV-FLOW", Status = 1 };
        db.Invoices.Add(inv);
        await db.SaveChangesAsync();
        var line = new TestInvoiceLine { InvoiceId = inv.Id, Title = "FlowLine", TierCode = 1 };
        db.InvoiceLines.Add(line);
        await db.SaveChangesAsync();

        var flowTag = await db.Tags.FirstAsync(t => t.Entity == "FlowTag" && t.CategoryId != null && t.DefaultPrice >= 10m);
        var warmupGrid = new TestAskWarmupGridService(db, new TestPlatformAppService(db, new FixedUser(10, "*")));
        var askBody = JsonSerializer.SerializeToElement(new { thirdTagId = flowTag.Id });
        var moveRes = await warmupGrid.MoveStatusAsync(
          new GridRequest { SelectedIds = [line.Id], SelectedVersions = [line.Ver] },
          "Promote",
          askBody,
          CancellationToken.None);

        Assert.Equal(MutationOutcome.Refused, moveRes.Outcome);
        Assert.Null(moveRes.Warnings);
        var guardMsg = Assert.Single(moveRes.Messages!);
        Assert.NotEqual("Required", guardMsg.Title);
        Assert.Equal(Localizer.Instance.For("Test")["Required"], guardMsg.Title);
      }
      finally
      {
        CultureInfo.CurrentUICulture = prevUi;
      }
    }
    finally
    {
      ModuleCatalog.Instance = prevCatalog;
    }
  }

  [Fact]
  public async Task AutoOverlayPresentation_SelectsPopoverVsDrawerAcrossSyncAndAsyncMetadata_AndHonorsExplicitOverride()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));

    // 1. <= 8 visible scalar fields, 0 tabs, 0 non-file children -> Popover (720px default, yet OverlayAsk overrides width!)
    var compactForm = new TestTagEnvelopeFormService(db, app);
    var compactSync = compactForm.Overlay("test/tag-envelope-form");
    var compactAsync = await compactForm.OverlayAsync("test/tag-envelope-form", CancellationToken.None);
    Assert.Equal("popover", compactSync.Presentation);
    Assert.Equal("popover", compactAsync.Presentation);
    Assert.Equal("720px", compactAsync.Width);

    var compactScreen = await compactForm.BuildScreenAsync("test/tag-envelope-form", new FormRequest(), CancellationToken.None);
    Assert.Equal("720px", compactScreen.OverlayWidth);

    // 2. > 8 visible scalar fields -> Drawer
    var manyFieldsForm = new TestAutoDrawerManyFieldsFormService(db, app);
    Assert.Equal("drawer", manyFieldsForm.Overlay("test/many-fields-form").Presentation);
    Assert.Equal("drawer", (await manyFieldsForm.OverlayAsync("test/many-fields-form", CancellationToken.None)).Presentation);

    // 3. Tabs > 0 -> Drawer
    var tabForm = new TestAutoDrawerTabFormService(db, app);
    Assert.Equal("drawer", tabForm.Overlay("test/tab-form").Presentation);
    Assert.Equal("drawer", (await tabForm.OverlayAsync("test/tab-form", CancellationToken.None)).Presentation);

    // 4. Async-only SetElementsAsync with AddChild -> BOTH sync Overlay() and async OverlayAsync() resolve to Drawer!
    var asyncChildForm = new TestAutoDrawerAsyncChildFormService(db, app);
    Assert.Equal("drawer", asyncChildForm.Overlay("test/async-child-form").Presentation);
    Assert.Equal("drawer", (await asyncChildForm.OverlayAsync("test/async-child-form", CancellationToken.None)).Presentation);

    // 5. Explicit override Presentation -> honored verbatim (and OverlayAsk does NOT overwrite custom 480px width with 720px!)
    var explicitDialogForm = new TestExplicitDialogFormService(db, app);
    var dialogDto = await explicitDialogForm.OverlayAsync("test/dialog-form", CancellationToken.None);
    Assert.Equal("dialog", dialogDto.Presentation);
    Assert.Equal("480px", dialogDto.Width);
    var dialogScreen = await explicitDialogForm.BuildScreenAsync("test/dialog-form", new FormRequest(), CancellationToken.None);
    Assert.Null(dialogScreen.OverlayWidth);
  }

  [Fact]
  public async Task StrictFormRequestEnvelope_MessagesVsWarnings_PickedAndFileGridIdempotency_AndSealedContracts()
  {
    await using var db = CreateDbContext();
    var user = new FixedUser(10, "*");
    var app = new TestPlatformAppService(db, user);

    var tempDir = Path.Combine(Path.GetTempPath(), $"platform-w2-files-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    try
    {
      var services = new ServiceCollection()
        .AddLogging()
        .AddSingleton<PlatformDbContext>(db)
        .AddSingleton(db)
        .AddSingleton<IUserContext>(user)
        .AddSingleton<PlatformAppService>(app)
        .AddSingleton<IFileStores>(new DefaultFileStores(new LocalFileStorage(tempDir)))
        .AddTransient<TestTagEnvelopeFormService>()
        .AddTransient<TestDocLineGridService>()
        .AddTransient<TestAppFileGridService>()
        .BuildServiceProvider();
      app = new TestPlatformAppService(db, user, services);

      var tagFormPath = ServicePathResolver.Resolve(typeof(TestTagEnvelopeFormService));

      async Task<(int Status, string Body)> PostAsync(Type svcType, string svcPath, string method, Feature feat, string jsonBody, string? idemKey = null)
      {
        var ctx = new DefaultHttpContext { RequestServices = services };
        ctx.Request.Path = $"/api/{svcPath}/{method}";
        ctx.Request.ContentType = "application/json";
        if (idemKey != null)
        {
          ctx.Request.Headers["Idempotency-Key"] = idemKey;
        }

        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonBody));
        ctx.Response.Body = new MemoryStream();
        var instance = services.GetRequiredService(svcType);
        var res = await EndpointExecutor.DispatchAsync(svcType, instance, method, feat, ctx.Request, services, CancellationToken.None);
        await res.ExecuteAsync(ctx);
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body);
        return (ctx.Response.StatusCode, await reader.ReadToEndAsync());
      }

      // 1. Strict FormRequest envelope: { entity: ... } and flat root JSON rejected with 400 Bad Request,
      //    while { value: { name, entity, value, id: 999 } } succeeds as CREATE (ignoring nested id) and binds Entity & Value!
      var legacyEntityEnvelope = await PostAsync(
        typeof(TestTagEnvelopeFormService),
        tagFormPath,
        "save",
        TestAppModule.Tags,
        """{"entity":{"name":"Legacy"}}""");
      Assert.Equal(StatusCodes.Status400BadRequest, legacyEntityEnvelope.Status);

      var flatRootJson = await PostAsync(
        typeof(TestTagEnvelopeFormService),
        tagFormPath,
        "save",
        TestAppModule.Tags,
        """{"name":"Flat"}""");
      Assert.Equal(StatusCodes.Status400BadRequest, flatRootJson.Status);

      var validEnvelope = await PostAsync(
        typeof(TestTagEnvelopeFormService),
        tagFormPath,
        "save",
        TestAppModule.Tags,
        """{"value":{"id":999,"name":"KeyOne","entity":"OrderEntity","value":"ScalarVal","defaultPrice":12.5}}""");
      Assert.Equal(StatusCodes.Status200OK, validEnvelope.Status);

      var savedTag = await db.Tags.AsNoTracking().SingleAsync(t => t.Name == "KeyOne");
      Assert.NotEqual(999L, savedTag.Id);
      Assert.Equal("OrderEntity", savedTag.Entity);
      Assert.Equal("ScalarVal", savedTag.Value);

      // 2. Picked mutation idempotency (AttachPickedAsync): replays identical key, returns 409 on payload mismatch,
      //    and populates Messages (never Warnings) on Refused!
      var salesDoc = new TestInvoice { Number = "SAL-PICK", Status = 10 };
      db.Invoices.Add(salesDoc);
      await db.SaveChangesAsync();

      var lineGridPath = ServicePathResolver.Resolve(typeof(TestDocLineGridService));
      var pickKey = $"pick-idem-{Guid.NewGuid():N}";
      var pickBody = JsonSerializer.Serialize(new { parentId = salesDoc.Id, pickedId = savedTag.Id });
      var pickFirst = await PostAsync(typeof(TestDocLineGridService), lineGridPath, "pick-tag", TestAppModule.DocLines, pickBody, pickKey);
      Assert.Equal(StatusCodes.Status200OK, pickFirst.Status);
      Assert.Equal(1, await db.InvoiceLines.CountAsync(l => l.InvoiceId == salesDoc.Id));

      var pickReplay = await PostAsync(typeof(TestDocLineGridService), lineGridPath, "pick-tag", TestAppModule.DocLines, pickBody, pickKey);
      Assert.Equal(StatusCodes.Status200OK, pickReplay.Status);
      Assert.Equal(1, await db.InvoiceLines.CountAsync(l => l.InvoiceId == salesDoc.Id));

      var pickConflictBody = JsonSerializer.Serialize(new { parentId = salesDoc.Id, pickedId = 999999L });
      var pickConflict = await PostAsync(typeof(TestDocLineGridService), lineGridPath, "pick-tag", TestAppModule.DocLines, pickConflictBody, pickKey);
      Assert.Equal(StatusCodes.Status409Conflict, pickConflict.Status);

      var directGrid = new TestDocLineGridService(db, app);
      var refusedPick = await directGrid.PickTag(new GridPickRequest { ParentId = salesDoc.Id, PickedId = 999999L }, CancellationToken.None);
      Assert.Equal(MutationOutcome.Refused, refusedPick.Outcome);
      Assert.Null(refusedPick.Warnings);
      Assert.NotEmpty(refusedPick.Messages!);

      // 3. FileGridService<TDbContext, TFile> Upload / SetInfo / DeleteAsync idempotency + Messages on Refused
      var fileGrid = new TestAppFileGridService(db, app);
      var badFiles = new FormFileCollection();
      var refusedUpload = await fileGrid.Upload(new GridUploadRequest { ParentId = salesDoc.Id }, badFiles, CancellationToken.None);
      Assert.Equal(MutationOutcome.Refused, refusedUpload.Outcome);
      Assert.Null(refusedUpload.Warnings);
      Assert.NotEmpty(refusedUpload.Messages!);

      var txtBytes = Encoding.UTF8.GetBytes("hello platform wave 2");
      FormFileCollection MakeTxtCollection()
      {
        var ms = new MemoryStream(txtBytes);
        var ff = new FormFile(ms, 0, txtBytes.Length, "file", "notes.txt")
        {
          Headers = new HeaderDictionary(),
          ContentType = "text/plain"
        };
        return new FormFileCollection { ff };
      }

      var uploadKey = $"upload-idem-{Guid.NewGuid():N}";
      var upload1 = await fileGrid.Upload(
        new GridUploadRequest { ParentId = salesDoc.Id, IdempotencyKey = uploadKey, RawPayload = "u1" },
        MakeTxtCollection(),
        CancellationToken.None);
      Assert.Equal(MutationOutcome.Committed, upload1.Outcome);
      Assert.True(upload1.Id > 0);

      var uploadReplay = await fileGrid.Upload(
        new GridUploadRequest { ParentId = salesDoc.Id, IdempotencyKey = uploadKey, RawPayload = "u1" },
        MakeTxtCollection(),
        CancellationToken.None);
      Assert.Equal(MutationOutcome.Committed, uploadReplay.Outcome);
      Assert.Equal(upload1.Id, uploadReplay.Id);
      Assert.Equal(1, await db.AppFiles.CountAsync());

      await Assert.ThrowsAsync<IdempotencyConflictException>(() => fileGrid.Upload(
        new GridUploadRequest { ParentId = salesDoc.Id, IdempotencyKey = uploadKey, RawPayload = "u2-mutated" },
        MakeTxtCollection(),
        CancellationToken.None));

      var refusedInfo = await fileGrid.SetInfo(
        new FileInfoRequest { Id = upload1.Id, ParentId = salesDoc.Id, Name = "bad/slash" },
        CancellationToken.None);
      Assert.Equal(MutationOutcome.Refused, refusedInfo.Outcome);
      Assert.Null(refusedInfo.Warnings);
      Assert.NotEmpty(refusedInfo.Messages!);

      // 4. ContractExporter wire contract seal assertions
      var tsContracts = ContractExporter.GenerateContracts(
        [typeof(PlatformDbContext).Assembly, typeof(PlatformEndpointsExtensions).Assembly, typeof(ContractExporter).Assembly]);
      Assert.Contains("Platform ContractExporter", tsContracts, StringComparison.Ordinal);
      Assert.DoesNotContain("Air.Platform", tsContracts, StringComparison.Ordinal);
      Assert.Contains("export interface FormDateTimeModel", tsContracts, StringComparison.Ordinal);
      Assert.Contains("export interface GridCellDateTimeModel", tsContracts, StringComparison.Ordinal);
      Assert.DoesNotContain("FormDatetimeModel", tsContracts, StringComparison.Ordinal);
      Assert.DoesNotContain("GridCellDatetimeModel", tsContracts, StringComparison.Ordinal);
      Assert.Contains("'map-pin'", tsContracts, StringComparison.Ordinal);
      Assert.Contains("'panel-right-open'", tsContracts, StringComparison.Ordinal);
      Assert.DoesNotContain("'mapPin'", tsContracts, StringComparison.Ordinal);
      Assert.DoesNotContain("'gear'", tsContracts, StringComparison.Ordinal);
      Assert.DoesNotContain("'reload'", tsContracts, StringComparison.Ordinal);
      Assert.Contains("'metadata/get-data'", tsContracts, StringComparison.Ordinal);
      Assert.Contains("'get-action-input'", tsContracts, StringComparison.Ordinal);
      Assert.Contains("color?: UiColor | null;", tsContracts, StringComparison.Ordinal);
    }
    finally
    {
      try
      {
        Directory.Delete(tempDir, recursive: true);
      }
      catch
      {
      }
    }
  }

  private static IEnumerable<PanelModel> FlattenModels(PanelNodeDto node)
  {
    if (node.Model != null)
    {
      yield return node.Model;
    }

    if (node.Children != null)
    {
      foreach (var child in node.Children)
      {
        foreach (var nested in FlattenModels(child))
        {
          yield return nested;
        }
      }
    }
  }
}
