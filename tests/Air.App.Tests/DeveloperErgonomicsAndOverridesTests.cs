using Microsoft.AspNetCore.Http;
using Platform.Api.Host.Internals;
using Platform.App.Internals;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

public sealed class DeveloperErgonomicsAndOverridesTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public DeveloperErgonomicsAndOverridesTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    ModuleCatalog.Instance.Register(new TestAppModuleImpl());
  }

  public void Dispose() => _connection.Dispose();

  private AppTestDbContext CreateDbContext()
  {
    var options = new DbContextOptionsBuilder<AppTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var rights = new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase);
    var db = new AppTestDbContext(options, new FixedTenant(1, 7, rights));
    db.Database.EnsureCreated();
    return db;
  }

  [Fact]
  public void DualSyncAndAsyncOverride_InSameClass_ThrowsClearStartupError()
  {
    var ex = Assert.Throws<InvalidOperationException>(() =>
      ServiceOverrideValidator.EnsureSingleSyncOrAsyncOverride(
        typeof(TestDualElementsAbstractForm),
        typeof(FormService<>),
        ("SetElements", "SetElementsAsync"),
        ("SetActions", "SetActionsAsync")));

    Assert.Contains("SetElements", ex.Message, StringComparison.Ordinal);
    Assert.Contains("SetElementsAsync", ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void DualSetColumnsSyncAndAsync_InSameClass_ThrowsClearStartupError()
  {
    var ex = Assert.Throws<InvalidOperationException>(() =>
      ServiceOverrideValidator.EnsureSingleSyncOrAsyncOverride(
        typeof(TestDualColumnsAbstractGrid),
        typeof(GridService<,>),
        ("SetColumns", "SetColumnsAsync"),
        ("SetNav", "SetNavAsync"),
        ("SetActions", "SetActionsAsync")));

    Assert.Contains("SetColumns", ex.Message, StringComparison.Ordinal);
    Assert.Contains("SetColumnsAsync", ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task OpenMetadataLists_AndAsyncNavCustomization_WorkOnGridAndForm()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(7, "*"));
    db.Invoices.Add(new TestInvoice { Number = "INV-OPEN-1", Status = 1, PrintedCount = 2 });
    await db.SaveChangesAsync();

    var grid = new TestOpenMetadataGridService(db, app);
    var gridScreen = await grid.BuildScreenAsync("test/open-metadata-grid", new GridRequest());
    var gridModel = ExtractGridModel(gridScreen);

    Assert.Equal(["id", "number", "printedCount"], gridModel.Columns.Select(c => c.Path));
    Assert.Equal("Invoice #", gridModel.Columns[1].Label);
    Assert.Equal("Printed (1)", gridModel.Columns[2].Label);
    Assert.DoesNotContain(gridModel.Nav.End ?? [], e => e.Key == "export");
    Assert.Contains(gridModel.Nav.End ?? [], e => e is NavLabelModel { Key: "summary", Label: "Ready" });

    var form = new TestOpenMetadataFormService(db, app);
    var formScreen = await form.BuildScreenAsync("test/open-metadata-form", 1L);
    var formModel = ExtractFormModel(formScreen);
    Assert.Equal(["number", "printedCount"], formModel.Elements.Select(f => f.Path));
    Assert.Equal("Number First", formModel.Elements[0].Label);

    var formNavs = ExtractNavModels(formScreen);
    Assert.DoesNotContain(formNavs.SelectMany(n => n.Start ?? []), e => e is FormNavSaveCloseModel);
    Assert.Contains(formNavs.SelectMany(n => n.End ?? []), e => e is NavLabelModel { Key: "count", Label: "Total: 1" });
  }

  [Fact]
  public void EndpointRegistry_SkipsStartupProbeForNonDynamicServices_AndHonorsExplicitGuardOverrides()
  {
    var catalog = new ModuleCatalog();
    catalog.Register(new TestAppModuleImpl());
    catalog.RegisterPlatformService<TestStrictCtorPlatformService>("strict-rpc");
    TestStrictCtorPlatformService.ResetProbes();

    var registry = EndpointRegistry.Build(catalog);
    Assert.True(registry.ByRoute.ContainsKey("/strict-rpc/ping"));
    Assert.Equal(0, TestStrictCtorPlatformService.NullDbProbes);

    var fileDelete = EndpointRegistry
      .DescribeServiceType(typeof(TestAppFileGridService))
      .First(e => e.Method.Name == nameof(FileGridService<>.Delete));
    Assert.Equal(EndpointAuth.Default, fileDelete.Auth);

    var elements = new Elements();
    elements.AddTab("Details");
    elements.AddReportTable(new ReportTableDto { Key = "t1", Columns = [], Rows = [] });
    elements.AddChild<TestInvoiceLineGridService>();
    elements.Clear();
    Assert.Empty(elements.Items);
    Assert.Empty(elements.TabItems);
    Assert.Empty(elements.ChildItems);
    Assert.Empty(elements.TableItems);

    var flow = new Flow<TestInvoice>();
    flow.Status(1, "Draft", UiColor.Slate);
    flow.FinalStatus(2, "Done", UiColor.Emerald);
    flow.Initial(1);
    Assert.NotNull(flow.Find(1));
    Assert.True(flow.Remove(1));
    Assert.Null(flow.InitialStatusId);
    Assert.Single(flow.Items);
    flow.Clear();
    Assert.Empty(flow.Items);
  }

  [Fact]
  public async Task PlatformMetadataService_ExecutesEndToEnd_AndNullableDtoEndpointPreservesNull()
  {
    await using var db = CreateDbContext();
    var user = new FixedUser(7, "*");
    var app = new TestPlatformAppService(db, user);

    var file = new TestAppFile
    {
      Name = "empty.png",
      ContentType = "image/png",
      Size = 16,
      StorageKey = "k1",
      Sha256 = "abc"
    };
    db.AppFiles.Add(file);
    await db.SaveChangesAsync();

    var catalog = new ModuleCatalog();
    catalog.Register(new TestAppModuleImpl());
    catalog.RegisterPlatformService<PlatformMetadataService>("metadata");
    catalog.RegisterPlatformService<TestStrictCtorPlatformService>("strict-rpc");
    catalog.RegisterPlatformService<TestStrictCtorPlatformService>("strict-rpc-alias");

    var services = new ServiceCollection();
    services.AddSingleton(catalog);
    services.AddScoped<IUserContext>(_ => user);
    services.AddScoped<PlatformAppService>(_ => app);
    services.AddScoped<IAuthStore, StubMetadataAuthStore>();
    services.AddScoped<PlatformMetadataService>();
    services.AddScoped(_ => new TestAppFileGridService(db, app));
    await using var sp = services.BuildServiceProvider();

    var prevCatalog = ModuleCatalog.Instance;
    try
    {
      ModuleCatalog.Instance = catalog;
      TestStrictCtorPlatformService.ResetProbes();
      PlatformValidator.Validate(catalog, sp);
      Assert.Equal(1, TestStrictCtorPlatformService.ValidationCalls);

      var registry = EndpointRegistry.Build(catalog);
      var metaDescriptor = registry.ByRoute["/metadata/get-data"];

      var metaHttpContext = new DefaultHttpContext { RequestServices = sp };
      metaHttpContext.Request.Method = "POST";
      metaHttpContext.Request.Path = "/api/metadata/get-data";

      var metaResult = await EndpointExecutor.ExecuteAsync(metaDescriptor, metaHttpContext.Request, sp, CancellationToken.None);
      var okMeta = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<AppMetadataDto>>(metaResult);
      Assert.NotNull(okMeta.Value);
      Assert.NotEmpty(okMeta.Value!.Texts);
      Assert.NotEmpty(okMeta.Value.Routes);

      var fileMetaDescriptor = registry.ByRoute[$"/{TestAppModule.AppFiles.Path}/file-metadata"];
      var fileHttpContext = new DefaultHttpContext { RequestServices = sp };
      fileHttpContext.Request.Method = "POST";
      fileHttpContext.Request.Path = $"/api/{TestAppModule.AppFiles.Path}/file-metadata";
      fileHttpContext.Request.ContentType = "application/json";
      var bodyBytes = System.Text.Encoding.UTF8.GetBytes($$"""{"id":{{file.Id}}}""");
      fileHttpContext.Request.Body = new MemoryStream(bodyBytes);
      fileHttpContext.Request.ContentLength = bodyBytes.Length;

      var fileMetaResult = await EndpointExecutor.ExecuteAsync(fileMetaDescriptor, fileHttpContext.Request, sp, CancellationToken.None);
      var okNull = Assert.IsAssignableFrom<IValueHttpResult>(fileMetaResult);
      Assert.Null(okNull.Value);

      var rpcInstance = new TestStrictCtorPlatformService(db, app);
      var voidInvoker = EndpointInvokerCompiler.Compile(typeof(TestStrictCtorPlatformService), typeof(TestStrictCtorPlatformService).GetMethod(nameof(TestStrictCtorPlatformService.DoVoid))!);
      var voidRes = Assert.IsAssignableFrom<IValueHttpResult>(await voidInvoker(rpcInstance, []));
      Assert.Equal(MutationOutcome.Committed, Assert.IsType<MutationResultDto>(voidRes.Value).Outcome);

      var syncNullInvoker = EndpointInvokerCompiler.Compile(typeof(TestStrictCtorPlatformService), typeof(TestStrictCtorPlatformService).GetMethod(nameof(TestStrictCtorPlatformService.SyncNullDto))!);
      var syncNullRes = Assert.IsAssignableFrom<IValueHttpResult>(await syncNullInvoker(rpcInstance, []));
      Assert.Null(syncNullRes.Value);

      var asyncNullMutInvoker = EndpointInvokerCompiler.Compile(typeof(TestStrictCtorPlatformService), typeof(TestStrictCtorPlatformService).GetMethod(nameof(TestStrictCtorPlatformService.AsyncNullMutation))!);
      var asyncNullMutRes = Assert.IsAssignableFrom<IValueHttpResult>(await asyncNullMutInvoker(rpcInstance, []));
      Assert.Equal(MutationOutcome.Committed, Assert.IsType<MutationResultDto>(asyncNullMutRes.Value).Outcome);
    }
    finally
    {
      ModuleCatalog.Instance = prevCatalog;
    }
  }

  [Fact]
  public async Task ConfirmAsync_EvaluatesDynamicPromptPerRecord_WithoutSetActionsAsync()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(7, "*"));

    var invEmpty = new TestInvoice { Number = "INV-1", Status = 1 };
    var invWithLines = new TestInvoice { Number = "INV-2", Status = 1 };
    db.Invoices.AddRange(invEmpty, invWithLines);
    await db.SaveChangesAsync();

    db.InvoiceLines.Add(new TestInvoiceLine { InvoiceId = invWithLines.Id, Title = "Item", Amount = 100m });
    await db.SaveChangesAsync();

    var formEmpty = new TestConfirmAsyncFormService(db, app);
    var screenEmpty = await formEmpty.BuildScreenAsync("test/confirm-async", invEmpty.Id);
    var actionsEmpty = ExtractActionButtons(screenEmpty);
    var approveEmpty = Assert.Single(actionsEmpty, a => a.Key == "approve");
    Assert.Null(approveEmpty.Confirm);

    var formWithLines = new TestConfirmAsyncFormService(db, app);
    var screenWithLines = await formWithLines.BuildScreenAsync("test/confirm-async", invWithLines.Id);
    var actionsWithLines = ExtractActionButtons(screenWithLines);
    var approveWithLines = Assert.Single(actionsWithLines, a => a.Key == "approve");
    Assert.Equal("Approve 1 lines?", approveWithLines.Confirm?.Message);
  }

  [Fact]
  public async Task RequiredParent_ReturnsLoadedParent_AndThrowsWhenMissing()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(7, "*"));

    var inv = new TestInvoice { Number = "INV-PARENT", Status = 1 };
    db.Invoices.Add(inv);
    await db.SaveChangesAsync();

    var missingParentForm = new TestRequiredParentLineFormService(db, app);
    ((IPlatformService)missingParentForm).ValidateConfiguration(ModuleCatalog.Instance);
    var probeOverlay = await missingParentForm.OverlayAsync("test/required-parent-line");
    Assert.NotNull(probeOverlay);
    Assert.ThrowsAny<InvalidOperationException>(() => missingParentForm.ReadRequiredParent());

    var boundForm = new TestRequiredParentLineFormService(db, app);
    await boundForm.BuildScreenAsync(
      "test/required-parent-line",
      new FormRequest { ParentService = ServicePathResolver.Resolve(typeof(TestInvoiceFormService)), ParentId = inv.Id });
    var loaded = boundForm.ReadRequiredParent();
    Assert.Equal("INV-PARENT", loaded.Number);
  }

  [Fact]
  public async Task UseActionsOf_WithConfirmAsync_RequiresGridFallbackAndUsesItInGrid()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(7, "*"));
    var catalog = new ModuleCatalog();
    catalog.Register(new TestAppModuleImpl());

    var brokenGrid = new MockBadConfirmAsyncGridService(db, app);
    var ex = Assert.Throws<InvalidOperationException>(() =>
      ((IPlatformService)brokenGrid).ValidateConfiguration(catalog));
    Assert.Contains("ConfirmAsync", ex.Message, StringComparison.Ordinal);

    var validGrid = new TestConfirmAsyncGridWithFallbackService(db, app);
    ((IPlatformService)validGrid).ValidateConfiguration(catalog);
    var screen = await validGrid.BuildScreenAsync("test/confirm-async-grid-fallback", new GridRequest());
    var gridModel = ExtractGridModel(screen);
    var gridActions = (gridModel.ActionNav.Start ?? [])
      .Concat(gridModel.ActionNav.Elements ?? [])
      .OfType<GridNavActionModel>()
      .ToList();
    var approve = Assert.Single(gridActions, a => a.Key == "approve");
    Assert.Equal("Approve selected invoices?", approve.Confirm?.Message);
  }

  private static GridModel ExtractGridModel(ScreenDto screen)
  {
    GridModel? found = null;
    void Walk(PanelNodeDto? node)
    {
      if (node == null || found != null)
      {
        return;
      }

      if (node.Model is GridModel gm)
      {
        found = gm;
        return;
      }

      if (node.Children != null)
      {
        foreach (var child in node.Children)
        {
          Walk(child);
        }
      }
    }

    Walk(screen.Layout?.Root);
    return Assert.IsType<GridModel>(found);
  }

  private static FormModel ExtractFormModel(ScreenDto screen)
  {
    FormModel? found = null;
    void Walk(PanelNodeDto? node)
    {
      if (node == null || found != null)
      {
        return;
      }

      if (node.Model is FormModel fm)
      {
        found = fm;
        return;
      }

      if (node.Children != null)
      {
        foreach (var child in node.Children)
        {
          Walk(child);
        }
      }
    }

    Walk(screen.Layout?.Root);
    return Assert.IsType<FormModel>(found);
  }

  private static List<NavModel> ExtractNavModels(ScreenDto screen)
  {
    var result = new List<NavModel>();
    void Walk(PanelNodeDto? node)
    {
      if (node == null)
      {
        return;
      }

      if (node.Model is NavModel nm)
      {
        result.Add(nm);
      }

      if (node.Children != null)
      {
        foreach (var child in node.Children)
        {
          Walk(child);
        }
      }
    }

    Walk(screen.Layout?.Root);
    return result;
  }

  private static List<FormNavActionModel> ExtractActionButtons(ScreenDto screen)
  {
    var result = new List<FormNavActionModel>();
    void Walk(PanelNodeDto? node)
    {
      if (node == null)
      {
        return;
      }

      if (node.Model is NavModel nav)
      {
        if (nav.Start != null)
        {
          result.AddRange(nav.Start.OfType<FormNavActionModel>());
        }

        if (nav.Elements != null)
        {
          result.AddRange(nav.Elements.OfType<FormNavActionModel>());
        }
      }

      if (node.Children != null)
      {
        foreach (var child in node.Children)
        {
          Walk(child);
        }
      }
    }

    Walk(screen.Layout?.Root);
    return result;
  }
}
