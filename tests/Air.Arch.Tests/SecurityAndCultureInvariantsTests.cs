using DemoApp;
using DemoApp.AgentMemory;
using DemoApp.AgentModel;
using DemoApp.Data;
using DemoEcom;
using DemoOrg;
using DemoOrg.Brand;

namespace Platform.Arch.Tests;

public sealed class SecurityAndCultureInvariantsTests
{
  [Fact]
  public void CultureCatalog_ResolvesDynamicLanguagesWithoutHardcodedIds()
  {
    var catalog = new CultureCatalog(
      [
        new PlatformCulture(1, "En", "en-US", "en", "English", CultureInfo.GetCultureInfo("en-US")),
        new PlatformCulture(2, "Ua", "uk-UA", "uk", "Українська", CultureInfo.GetCultureInfo("uk-UA")),
        new PlatformCulture(3, "Pl", "pl-PL", "pl", "Polski", CultureInfo.GetCultureInfo("pl-PL"))
      ],
      defaultToken: "uk-UA");

    Assert.Equal("Ua", catalog.Default.Handle);
    Assert.Equal("Pl", catalog.Resolve("pl").Handle);
    Assert.Equal("Pl", catalog.Resolve("pl-PL").Handle);
    Assert.Equal("Pl", catalog.ResolveById(3).Handle);
    Assert.Equal("Ua", catalog.ResolveById(999).Handle);

    var ctx = new DefaultHttpContext();
    ctx.Request.Headers.AcceptLanguage = "pl-PL,pl;q=0.9,en-US;q=0.8";
    Assert.Equal("pl-PL", catalog.ResolveRequest(ctx.Request).Code);

    ctx.Request.Headers.Cookie = "culture=en";
    Assert.Equal("en-US", catalog.ResolveRequest(ctx.Request).Code);

    ctx.Request.QueryString = new QueryString("?lang=pl");
    Assert.Equal("pl-PL", catalog.ResolveRequest(ctx.Request).Code);

    Assert.Throws<InvalidOperationException>(() =>
      new CultureCatalog([new PlatformCulture(4, "pl", "pl-PL", "pl", "Polski", CultureInfo.GetCultureInfo("pl-PL"))]));
  }

  [Fact]
  public void ModuleCatalog_Freeze_PreventsPostStartupRegistrations()
  {
    var catalog = new ModuleCatalog();
    catalog.Register(new AppModule());
    catalog.Freeze();

    Assert.True(catalog.IsFrozen);
    Assert.NotNull(catalog.FindFeature("app/user"));
    Assert.Throws<InvalidOperationException>(() => catalog.Register(new OrgModule()));
  }

  [Fact]
  public void BannedSymbolSuppressions_AreRestrictedToSanctionedFiles()
  {
    var root = FindRepoRoot();
    string[] expected =
    [
      "src/Air.Api/Mutations/MutationContext.cs",
      "src/Air.Data/CodeRows.cs",
      "src/Air.Data/Schema/ModuleSchema.cs",
      "src/Air.Data/Schema/PlatformMigrationRunner.cs",
      "tests/Air.Agent.Tests/AgentWave3CapabilitiesTests.cs"
    ];
    Array.Sort(expected, StringComparer.Ordinal);

    var pragmaRegex = new Regex(@"^\s*#\s*pragma\s+warning\s+disable\b.*\bRS0030\b", RegexOptions.Multiline);
    var actual = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
      .Select(path => (FullPath: path, RelPath: Path.GetRelativePath(root, path).Replace('\\', '/')))
      .Where(f => !f.RelPath.Contains("/bin/", StringComparison.Ordinal) && !f.RelPath.Contains("/obj/", StringComparison.Ordinal))
      .Where(f => pragmaRegex.IsMatch(File.ReadAllText(f.FullPath)))
      .Select(f => f.RelPath)
      .Order(StringComparer.Ordinal)
      .ToArray();

    Assert.Equal(expected, actual);
  }

  [Fact]
  public void CanLookup_DeniesUnreferencedAdminGrids_AndAllowsDeclaredReferrers()
  {
    var prevCatalog = ModuleCatalog.Instance;
    var catalog = new ModuleCatalog();
    catalog.Register(new AppModule());
    catalog.Register(new OrgModule());
    catalog.Register(new EcomModule());
    catalog.Freeze();

    var rights = new HashSet<string>(["Org.Product.View"], StringComparer.OrdinalIgnoreCase);
    using var scope = AsyncLocalTenantContext.Use(1, rights, userId: 42);
    var tenant = AsyncLocalTenantContext.Instance;

    try
    {
      ModuleCatalog.Instance = catalog;

      using var conn = new SqliteConnection("DataSource=:memory:");
      conn.Open();
      var orgDbOptions = new DbContextOptionsBuilder<DemoOrg.Data.OrgDbContext>()
        .UseSqlite(conn)
        .Options;
      using var orgDb = new DemoOrg.Data.OrgDbContext(orgDbOptions, tenant);

      var appDbOptions = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(conn)
        .Options;
      using var appDb = new AppDbContext(appDbOptions, tenant);

      var httpCtx = new DefaultHttpContext
      {
        User = new ClaimsPrincipal(new ClaimsIdentity(
          [
            new Claim(AuthClaims.Sub, "42"),
            new Claim(AuthClaims.Login, "viewer")
          ],
          "PlatformAuth"))
      };
      httpCtx.Items[AuthClaims.SnapshotItem] = new UserAuthSnapshot(
        Id: 42,
        Login: "viewer",
        Name: "Viewer",
        Email: "",
        TimeZone: "UTC",
        IsActive: true,
        IsRoot: false,
        TokenVersion: 1,
        MustChangePassword: false,
        HomeTenantId: 1,
        Permissions: ["Org.Product.View"],
        Global: false,
        AllowedTenantIds: [1],
        LanguageCode: "uk");

      var services = new ServiceCollection()
        .AddSingleton(catalog)
        .BuildServiceProvider();
      var userCtx = new UserContext(new HttpContextAccessor { HttpContext = httpCtx });
      var appSvc = new AppService(userCtx, services, tenant, Localizer.Instance);
      var orgSvc = new OrgService(userCtx, services, tenant, Localizer.Instance);

      var memoryGrid = new AppAgentMemoryGridService(appDb, appSvc);
      var modelGrid = new AppAgentModelGridService(appDb, appSvc);
      var brandGrid = new OrgBrandGridService(orgDb, orgSvc);

      var lookupReq = new GridRequest { IsReferencePicker = true };
      Assert.False(memoryGrid.CanLookup(lookupReq, userCtx));
      Assert.False(modelGrid.CanLookup(lookupReq, userCtx));
      Assert.True(brandGrid.CanLookup(lookupReq, userCtx));
    }
    finally
    {
      ModuleCatalog.Instance = prevCatalog;
    }
  }

  [Fact]
  public void ReadOnlyEntities_RequireReadOnlyFeatures()
  {
    Assert.False(typeof(IReadOnlyEntity).IsAssignableFrom(typeof(BaseAppTenant)));

    var catalog = new ModuleCatalog();
    catalog.Register(new AppModule());
    catalog.Register(new OrgModule());
    catalog.Register(new EcomModule());
    catalog.Freeze();

    PlatformValidator.Validate(catalog);

    foreach (var serviceType in catalog.AllServiceTypes)
    {
      var entityType = FindServiceEntityType(serviceType);
      if (entityType is not null && typeof(IReadOnlyEntity).IsAssignableFrom(entityType))
      {
        var feat = catalog.FindFeature(serviceType);
        Assert.NotNull(feat);
        Assert.True(
          feat!.IsReadOnly,
          $"Service '{serviceType.FullName}' over IReadOnlyEntity '{entityType.Name}' must bind a .ReadOnly() Feature ('{feat.Key}').");
      }
    }
  }

  private static Type? FindServiceEntityType(Type? type)
  {
    for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
    {
      if (current.IsGenericType)
      {
        var def = current.GetGenericTypeDefinition();
        if (def == typeof(GridService<,>) || def == typeof(FormService<>))
        {
          return current.GetGenericArguments()[0];
        }
      }
    }

    return null;
  }

  private static string FindRepoRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AirPlatform.slnx")))
    {
      dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Could not locate AirPlatform.slnx root.");
  }
}
