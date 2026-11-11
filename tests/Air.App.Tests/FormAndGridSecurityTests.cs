using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Platform.App.Host.Internals;
using Platform.App.Internals;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

public sealed class FormAndGridSecurityTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public FormAndGridSecurityTests()
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

  private static TestPlatformAppService CreateAppWithParentServices(AppTestDbContext db, IUserContext user)
  {
    TestPlatformAppService? app = null;
    var services = new ServiceCollection()
      .AddLogging()
      .AddSingleton<IUserContext>(user)
      .AddTransient<PlatformAppService>(_ => app!)
      .AddTransient(_ => new TestInvoiceFormService(db, app))
      .AddTransient(_ => new TestSiblingInvoiceFormService(db, app))
      .AddTransient(_ => new TestInvoiceGridService(db, app))
      .AddTransient(_ => new TestRichLineFormService(db, app!))
      .BuildServiceProvider();
    app = new TestPlatformAppService(db, user, services);
    return app;
  }

  [Fact]
  public async Task SingleGenericNavigationReference_InfersForeignKey_LocksDisabledFields_AndRejectsOrphanNav()
  {
    await using var db = CreateDbContext();
    var app = CreateAppWithParentServices(db, new FixedUser(10, "*"));

    var inv = new TestInvoice { Number = "INV-NAV", Status = 1 };
    db.Invoices.Add(inv);
    await db.SaveChangesAsync();

    var tag = new TestTag { Name = "OEM Filter", DefaultPrice = 125.50m, CategoryId = inv.Id, IsActive = true };
    db.Tags.Add(tag);
    await db.SaveChangesAsync();

    var parentFormPath = ServicePathResolver.Resolve(typeof(TestInvoiceFormService));
    var form = new TestRichLineFormService(db, app);
    var screen = await form.BuildScreenAsync(
      "test/rich-line-form",
      new FormRequest { ParentService = parentFormPath, ParentId = inv.Id },
      CancellationToken.None);
    var formModel = FindPanelModel<FormModel>(screen.Layout!.Root);

    // 1. Bound parent reference field is disabled, NOT required, and skips CreateOverlay
    var invoiceElement = Assert.Single(formModel.Elements, e => e.Path == "invoiceId");
    Assert.True(invoiceElement.IsDisabled);
    Assert.False(invoiceElement.IsRequired);
    Assert.Null(Assert.IsType<FormSelectModel>(invoiceElement.Model).CreateOverlay);

    // 2. Single-generic navigation reference infers FK, label, Where, and PatchOnSelect
    var tagElement = Assert.Single(formModel.Elements, e => e.Path == "tagId");
    Assert.Equal("Tag", tagElement.Label);
    var selectModel = Assert.IsType<FormSelectModel>(tagElement.Model);
    Assert.NotNull(selectModel.CreateOverlay);
    Assert.Single(selectModel.ReferenceDraftRules!, r => r.SourceField == "categoryId" && r.DraftField == "invoiceId");
    Assert.Single(tagElement.SelectPatches!, p => p.TargetField == "amount" && p.SourceColumn == "defaultPrice");

    // 3. Preset-locked reference on create disables field, clears inferred IsRequired, and skips CreateOverlay
    var presetForm = new TestRichLineFormService(db, app);
    var presetScreen = await presetForm.BuildScreenAsync(
      "test/rich-line-form",
      new FormRequest
      {
        ParentService = parentFormPath,
        ParentId = inv.Id,
        Value = JsonSerializer.SerializeToElement(new { tagId = tag.Id })
      },
      CancellationToken.None);
    var presetModel = FindPanelModel<FormModel>(presetScreen.Layout!.Root);
    var presetTagEl = Assert.Single(presetModel.Elements, e => e.Path == "tagId");
    Assert.True(presetTagEl.IsDisabled);
    Assert.False(presetTagEl.IsRequired);
    Assert.Null(Assert.IsType<FormSelectModel>(presetTagEl.Model).CreateOverlay);

    // 4. StatusFlow-managed field is disabled and NOT required
    var invForm = new TestInvoiceFormService(db, app);
    var invScreen = await invForm.BuildScreenAsync("test/invoice-form", new FormRequest(), CancellationToken.None);
    var invModel = FindPanelModel<FormModel>(invScreen.Layout!.Root);
    var statusEl = Assert.Single(invModel.Elements, e => e.Path == "status");
    Assert.True(statusEl.IsDisabled);
    Assert.False(statusEl.IsRequired);

    // 5. Navigation property without matching <Nav>Id or [ForeignKey] fails fast
    Assert.Throws<InvalidOperationException>(() =>
      new Elements().Add<TestInvoiceLine, TestTag>(l => l.OrphanNav));

    // 6. PrepareDraftAsync applies PatchOnSelect accurately
    var draft = await form.PrepareDraftAsync(
      recordId: null,
      overrides: new Dictionary<string, object?> { ["title"] = "Line 1", ["tagId"] = tag.Id },
      parentId: inv.Id,
      parentService: parentFormPath,
      triggerField: "tagId",
      ct: CancellationToken.None);

    Assert.Equal(125.50m, Convert.ToDecimal(draft.Values["amount"], CultureInfo.InvariantCulture));
  }

  [Fact]
  public async Task ChildGrid_BlocksReferencePicker_ParentZero_SiblingService_AndDirectFeatureBypasses()
  {
    await using var db = CreateDbContext();
    var app = CreateAppWithParentServices(db, new FixedUser(10, "*"));

    var inv = new TestInvoice { Number = "INV-CHILD", Status = 1 };
    db.Invoices.Add(inv);
    await db.SaveChangesAsync();

    db.InvoiceLines.Add(new TestInvoiceLine { InvoiceId = inv.Id, Title = "Confidential Line", Amount = 77m });
    await db.SaveChangesAsync();

    var siblingFormPath = ServicePathResolver.Resolve(typeof(TestSiblingInvoiceFormService));

    // 1. IsReferencePicker + ReferenceLabelPaths without ParentId must NOT bypass parent filter
    var pickerGrid = new TestInvoiceLineGridService(db, app);
    var pickerPage = await pickerGrid.GetDataAsync(new GridRequest
    {
      IsDataOnly = true,
      IsReferencePicker = true,
      ReferenceLabelPaths = ["title"]
    }, CancellationToken.None);
    Assert.Empty(pickerPage.Rows);

    // 2. ParentId = 0 must NOT bypass parent filter
    var zeroParentGrid = new TestInvoiceLineGridService(db, app);
    var zeroPage = await zeroParentGrid.GetDataAsync(new GridRequest
    {
      IsDataOnly = true,
      ParentId = 0
    }, CancellationToken.None);
    Assert.Empty(zeroPage.Rows);

    // 3. Sibling FormService sharing TEntity = TestInvoice must NOT match Parent.Bind<TestInvoiceFormService>
    var siblingParentGrid = new TestInvoiceLineGridService(db, app);
    var siblingPage = await siblingParentGrid.GetDataAsync(new GridRequest
    {
      IsDataOnly = true,
      ParentService = siblingFormPath,
      ParentId = inv.Id
    }, CancellationToken.None);
    Assert.Empty(siblingPage.Rows);

    // 4. Child grid whose Feature.ServiceType == GetType() must STILL enforce Parent.Bind when ParentId is omitted
    var directChildGrid = new TestDirectBoundChildGridService(db, app);
    var directPageNoParent = await directChildGrid.GetDataAsync(new GridRequest { IsDataOnly = true }, CancellationToken.None);
    Assert.Empty(directPageNoParent.Rows);

    var directChildWithParent = new TestDirectBoundChildGridService(db, app);
    var directPageWithParent = await directChildWithParent.GetDataAsync(new GridRequest
    {
      IsDataOnly = true,
      ParentService = ServicePathResolver.Resolve(typeof(TestInvoiceFormService)),
      ParentId = inv.Id
    }, CancellationToken.None);
    Assert.Single(directPageWithParent.Rows);
  }

  [Fact]
  public async Task ChildFormAndGrid_EnforceParentCanViewAndReachable_IncludingUnauthenticatedFailClosed()
  {
    await using var db = CreateDbContext();
    var reachableInv = new TestInvoice { Number = "INV-OK", Status = 1, IsHidden = false };
    var hiddenInv = new TestInvoice { Number = "INV-SECRET", Status = 1, IsHidden = true };
    db.Invoices.AddRange(reachableInv, hiddenInv);
    await db.SaveChangesAsync();

    var lineOnReachable = new TestInvoiceLine { InvoiceId = reachableInv.Id, Title = "Visible Child", Amount = 10m };
    var lineOnHidden = new TestInvoiceLine { InvoiceId = hiddenInv.Id, Title = "Hidden Parent Child", Amount = 99m };
    db.InvoiceLines.AddRange(lineOnReachable, lineOnHidden);
    await db.SaveChangesAsync();

    // 1. User has DirectLines.* rights, NOT Invoices.View -> GetModelAsync with omitted ParentId throws KeyNotFoundException
    var restrictedApp = CreateAppWithParentServices(db, new FixedUser(10, "Test.DirectLines.*"));
    var childFormNoParentRight = new TestRichLineFormService(db, restrictedApp);
    await Assert.ThrowsAsync<KeyNotFoundException>(() =>
      childFormNoParentRight.GetModelAsync(lineOnReachable.Id, CancellationToken.None));

    // 2. Unauthenticated user (userId = 0) fails closed on ParentScopeValidator even if rights array had "*"
    var unauthApp = CreateAppWithParentServices(db, new FixedUser(0, "*"));
    var unauthChildGrid = new TestInvoiceLineGridService(db, unauthApp);
    var unauthPage = await unauthChildGrid.GetDataAsync(new GridRequest
    {
      IsDataOnly = true,
      ParentId = reachableInv.Id
    }, CancellationToken.None);
    Assert.Empty(unauthPage.Rows);

    // 3. Full-rights user still blocked when parent is unreachable (IsHidden = true) even if ParentId is omitted
    var adminApp = CreateAppWithParentServices(db, new FixedUser(10, "*"));
    var childFormHiddenParent = new TestRichLineFormService(db, adminApp);
    await Assert.ThrowsAsync<KeyNotFoundException>(() =>
      childFormHiddenParent.GetModelAsync(lineOnHidden.Id, CancellationToken.None));

    // 4. BuildScreenAsync on unreachable parent throws KeyNotFoundException
    var screenFormHiddenParent = new TestRichLineFormService(db, adminApp);
    await Assert.ThrowsAsync<KeyNotFoundException>(() =>
      screenFormHiddenParent.BuildScreenAsync(
        "test/rich-line-form",
        new FormRequest
        {
          ParentService = ServicePathResolver.Resolve(typeof(TestInvoiceFormService)),
          ParentId = hiddenInv.Id
        },
        CancellationToken.None));
  }

  [Fact]
  public async Task SaveCell_BlocksViewOnlyUsers_OutOfScopeRows_AndDotPaths()
  {
    await using var db = CreateDbContext();
    var visibleInv = new TestInvoice { Number = "INV-1", Status = 1, IsHidden = false };
    var hiddenInv = new TestInvoice { Number = "INV-2", Status = 1, IsHidden = true };
    db.Invoices.AddRange(visibleInv, hiddenInv);
    await db.SaveChangesAsync();

    // 1. View-only user hitting save-cell via AppServiceEndpointDispatcher gets 403 Forbidden
    var viewOnlyApp = CreateAppWithParentServices(db, new FixedUser(10, "Test.Invoices.View"));
    var dispatcher = new AppServiceEndpointDispatcher();
    var ctx = new DefaultHttpContext { RequestServices = viewOnlyApp.Services };
    var payload = JsonSerializer.Serialize(new GridCellRequest { Id = visibleInv.Id, Path = "number", Value = JsonSerializer.SerializeToElement("HACK") });
    ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
    ctx.Request.ContentType = "application/json";

    var result = await dispatcher.DispatchAsync(
      typeof(TestInvoiceGridService),
      "test/invoices",
      "save-cell",
      null,
      TestAppModule.Invoices,
      ctx.Request,
      viewOnlyApp.Services,
      CancellationToken.None);
    await result.ExecuteAsync(ctx);
    Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);

    // 2. Authorized editor cannot save-cell on an out-of-RestrictedQuery row or dot-path
    var editorApp = CreateAppWithParentServices(db, new FixedUser(10, "*"));
    var grid = new TestInvoiceGridService(db, editorApp);
    var hiddenRes = await grid.SaveCellAsync(
      new GridCellRequest { Id = hiddenInv.Id, Path = "number", Value = JsonSerializer.SerializeToElement("HACK") },
      editorApp.Services,
      CancellationToken.None);
    Assert.Equal(MutationOutcome.Refused, hiddenRes.Outcome);

    var dotRes = await grid.SaveCellAsync(
      new GridCellRequest { Id = visibleInv.Id, Path = "nested.number", Value = JsonSerializer.SerializeToElement("HACK") },
      editorApp.Services,
      CancellationToken.None);
    Assert.Equal(MutationOutcome.Refused, dotRes.Outcome);
  }

  [Fact]
  public async Task FormSave_BlocksHiddenAndDotPathWrites_ValidatesIntAndEnumOptions_AndCompositeNullUnique()
  {
    await using var db = CreateDbContext();
    var app = CreateAppWithParentServices(db, new FixedUser(10, "*"));

    var inv = new TestInvoice { Number = "INV-ORIG", Status = 1 };
    db.Invoices.Add(inv);
    await db.SaveChangesAsync();

    var parentFormPath = ServicePathResolver.Resolve(typeof(TestInvoiceFormService));

    // 1. Invalid int option (999) is rejected
    var badIntForm = new TestRichLineFormService(db, app);
    var badIntJson = JsonSerializer.SerializeToElement(new { title = "Item A", tierCode = 999, priority = 1 });
    await Assert.ThrowsAsync<FormRefusedException>(() => badIntForm.SaveAsync(badIntJson, new FormRequest
    {
      ParentService = parentFormPath,
      ParentId = inv.Id,
      Value = badIntJson
    }, CancellationToken.None));

    // 2. Invalid enum option (99) is rejected
    var badEnumForm = new TestRichLineFormService(db, app);
    var badEnumJson = JsonSerializer.SerializeToElement(new { title = "Item A", tierCode = 1, priority = 99 });
    await Assert.ThrowsAsync<FormRefusedException>(() => badEnumForm.SaveAsync(badEnumJson, new FormRequest
    {
      ParentService = parentFormPath,
      ParentId = inv.Id,
      Value = badEnumJson
    }, CancellationToken.None));

    // 3. Malicious client writes to HiddenNote (.Hidden()) and invoice.number (dot-path) are ignored
    var saveForm = new TestRichLineFormService(db, app);
    var saveJson = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
    {
      ["title"] = "Unique Null Scope",
      ["tierCode"] = 2,
      ["priority"] = (int)TestPriority.Normal,
      ["hiddenNote"] = "CLIENT_INJECTED_SECRET",
      ["invoice.number"] = "PWNED_INVOICE"
    });
    var savedObj = await saveForm.SaveAsync(saveJson, new FormRequest
    {
      ParentService = parentFormPath,
      ParentId = inv.Id,
      Value = saveJson
    }, CancellationToken.None);

    var savedLine = Assert.IsType<TestInvoiceLine>(savedObj);
    Assert.Equal("Unique Null Scope", savedLine.Title);
    Assert.Null(savedLine.HiddenNote);
    Assert.Equal(2, savedLine.TierCode);
    Assert.Equal(TestPriority.Normal, savedLine.Priority);
    var reloadedInv = await db.Invoices.FirstAsync(i => i.Id == inv.Id);
    Assert.Equal("INV-ORIG", reloadedInv.Number);

    // 4. Composite .Unique(..., l => l.TagId) rejects duplicate title when TagId is null on both rows
    var dupForm = new TestRichLineFormService(db, app);
    var dupJson = JsonSerializer.SerializeToElement(new { title = "Unique Null Scope", tierCode = 1, priority = 1 });
    await Assert.ThrowsAsync<FormRefusedException>(() => dupForm.SaveAsync(dupJson, new FormRequest
    {
      ParentService = parentFormPath,
      ParentId = inv.Id,
      Value = dupJson
    }, CancellationToken.None));

    // 5. DisabledWhen(l => l.Priority == TestPriority.Urgent) protects Title on edit when Urgent
    var urgentEditForm = new TestRichLineFormService(db, app);
    var urgentEditJson = JsonSerializer.SerializeToElement(new
    {
      title = "OVERWRITTEN_WHILE_URGENT",
      tierCode = 2,
      priority = (int)TestPriority.Urgent
    });
    var urgentUpdatedObj = await urgentEditForm.SaveAsync(urgentEditJson, new FormRequest
    {
      Id = savedLine.Id,
      Ver = savedLine.Ver,
      ParentService = parentFormPath,
      ParentId = inv.Id,
      Value = urgentEditJson
    }, CancellationToken.None);
    var urgentUpdated = Assert.IsType<TestInvoiceLine>(urgentUpdatedObj);
    Assert.Equal("Unique Null Scope", urgentUpdated.Title);
    Assert.Equal(TestPriority.Urgent, urgentUpdated.Priority);
  }

  [Fact]
  public async Task ReferenceLookupAllowlist_FailsClosed_AndQueryMapRestrictsUndeclaredAndNonFilterableFields()
  {
    await using var db = CreateDbContext();
    var app = CreateAppWithParentServices(db, new FixedUser(10, "*"));

    db.Invoices.Add(new TestInvoice { Number = "INV-Q", Status = 1, PrintedCount = 5, InternalMemo = "TOP_SECRET_MEMO" });
    await db.SaveChangesAsync();

    // 1. ReferenceLookupAllowlist.TrySanitizeFilter fails closed (false) on unauthorized filter field
    var okSanitize = ReferenceLookupAllowlist.TrySanitizeFilter(
      typeof(TestTagGridService),
      FilterNode.Rule("name", FilterOperator.Contains, "Secret"),
      out var sanitized);
    Assert.False(okSanitize);
    Assert.Null(sanitized);

    // 2. Normal grid query on undeclared entity property (InternalMemo) is rejected by QueryMap
    var grid = new TestInvoiceGridService(db, app);
    await Assert.ThrowsAnyAsync<Exception>(() => grid.GetDataAsync(new GridRequest
    {
      IsDataOnly = true,
      Filter = FilterNode.Rule("internalMemo", FilterOperator.Contains, "TOP_SECRET")
    }, CancellationToken.None));

    // 3. Column declared with .Filterable(false) and .Sortable(false) rejects filter & sort
    await Assert.ThrowsAnyAsync<Exception>(() => grid.GetDataAsync(new GridRequest
    {
      IsDataOnly = true,
      Filter = FilterNode.Rule("printedCount", FilterOperator.Equals, 5)
    }, CancellationToken.None));

    await Assert.ThrowsAnyAsync<Exception>(() => grid.GetDataAsync(new GridRequest
    {
      IsDataOnly = true,
      Sort = [SortRule.Desc("printedCount")]
    }, CancellationToken.None));
  }

  [Fact]
  public async Task PrepareDraft_PreservesComputedZero_AndStripsSensitiveFields()
  {
    await using var db = CreateDbContext();
    var app = CreateAppWithParentServices(db, new FixedUser(10, "*"));

    var inv = new TestInvoice { Number = "INV-SENS", Status = 1, ApiSecret = "SUPER_SECRET_KEY" };
    db.Invoices.Add(inv);
    await db.SaveChangesAsync();

    // 1. Existing record with [Sensitive] ApiSecret does NOT leak ApiSecret in PrepareDraftAsync
    var invForm = new TestInvoiceFormService(db, app);
    var invDraft = await invForm.PrepareDraftAsync(
      recordId: inv.Id,
      overrides: new Dictionary<string, object?> { ["number"] = "INV-SENS-UPD" },
      ct: CancellationToken.None);
    Assert.DoesNotContain(invDraft.Values.Keys, k => string.Equals(k, "apiSecret", StringComparison.OrdinalIgnoreCase));

    // 2. Compute hook setting Amount = 0m from non-zero draft value (50m -> 0m) IS preserved in PrepareDraftAsync
    var lineForm = new TestRichLineFormService(db, app);
    var lineDraft = await lineForm.PrepareDraftAsync(
      recordId: null,
      overrides: new Dictionary<string, object?> { ["title"] = "FREE", ["amount"] = 50m },
      parentId: inv.Id,
      parentService: ServicePathResolver.Resolve(typeof(TestInvoiceFormService)),
      ct: CancellationToken.None);
    Assert.Equal(0m, Convert.ToDecimal(lineDraft.Values["amount"], CultureInfo.InvariantCulture));
  }

  [Fact]
  public void FilterNodeTranslator_Evaluator_AndPropertyAccessorCache_HandleNullableNegationAndHotCacheHits()
  {
    // 1. Flipped constant on LHS (0 < i.Id -> id > 0)
    Expression<Func<TestInvoice, bool>> flipped = i => 0 < i.Id;
    var node = FilterNodeTranslator.Translate(flipped);
    var rule = Assert.IsType<FilterRule>(node);
    Assert.Equal("id", rule.Field);
    Assert.Equal(FilterOperator.GreaterThan, rule.Operator);
    Assert.True(FilterNodeEvaluator.Evaluate(node, new TestInvoice { Id = 5 }));
    Assert.False(FilterNodeEvaluator.Evaluate(node, new TestInvoice { Id = 0 }));

    // 2. Double negation !!i.IsHidden
    Expression<Func<TestInvoice, bool>> doubleNeg = i => !!i.IsHidden;
    var doubleNegNode = FilterNodeTranslator.Translate(doubleNeg);
    Assert.True(FilterNodeEvaluator.Evaluate(doubleNegNode, new TestInvoice { IsHidden = true }));
    Assert.False(FilterNodeEvaluator.Evaluate(doubleNegNode, new TestInvoice { IsHidden = false }));

    // 3. Nullable relational negation: !(l.TagId > 5) is TRUE when TagId is null or <= 5, FALSE when > 5
    Expression<Func<TestInvoiceLine, bool>> negatedNullableRel = l => !(l.TagId > 5);
    var nullableRelNode = FilterNodeTranslator.Translate(negatedNullableRel);
    Assert.True(FilterNodeEvaluator.Evaluate(nullableRelNode, new TestInvoiceLine { TagId = null }));
    Assert.True(FilterNodeEvaluator.Evaluate(nullableRelNode, new TestInvoiceLine { TagId = 5 }));
    Assert.False(FilterNodeEvaluator.Evaluate(nullableRelNode, new TestInvoiceLine { TagId = 10 }));

    // 4. Field-to-field comparison throws NotSupportedException
    Expression<Func<TestInvoice, bool>> fieldToField = i => i.Id == i.Status;
    Assert.Throws<NotSupportedException>(() => FilterNodeTranslator.Translate(fieldToField));

    // 5. Cached path segments continue hitting lock-free cache (ReferenceEquals) even after 6,000 misses
    var warmSegments = PropertyAccessorCache.GetPathSegments("invoice.number");
    for (var i = 0; i < 6000; i++)
    {
      _ = PropertyAccessorCache.GetPathSegments($"uncached.random.path.{i}");
      Assert.Null(PropertyAccessorCache.GetProperty(typeof(TestInvoice), $"nonExistentRandomField_{i}"));
    }

    var afterFloodSegments = PropertyAccessorCache.GetPathSegments("invoice.number");
    Assert.Same(warmSegments, afterFloodSegments);
    Assert.NotNull(PropertyAccessorCache.GetProperty(typeof(TestInvoice), "number"));
  }

  private static TModel FindPanelModel<TModel>(PanelNodeDto node) where TModel : PanelModel
  {
    if (node.Model is TModel match)
    {
      return match;
    }

    if (node.Children is not null)
    {
      foreach (var child in node.Children)
      {
        try
        {
          return FindPanelModel<TModel>(child);
        }
        catch (InvalidOperationException)
        {
        }
      }
    }

    throw new InvalidOperationException($"Panel model '{typeof(TModel).Name}' not found.");
  }
}
