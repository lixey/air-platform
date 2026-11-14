using System.Globalization;
using Platform.Agent.Tests.Internals;

namespace Platform.Agent.Tests;

public sealed class AgentServiceExtensionsTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public AgentServiceExtensionsTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    ModuleCatalog.Instance.Register(new AgentTestModuleImpl());
  }

  public void Dispose() => _connection.Dispose();

  private (AgentTestDbContext Db, TestPlatformAppService App) CreateServices()
  {
    var options = new DbContextOptionsBuilder<AgentTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var tenant = new FixedTenant(1, 10, new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase));
    var user = new FixedUser(10, "*");
    var db = new AgentTestDbContext(options, tenant);
    db.Database.EnsureCreated();
    return (db, new TestPlatformAppService(db, user));
  }

  [Fact]
  public async Task ProviderAndModelExtensions_EnforceInvariantsActionsAndUkrainianLocalization()
  {
    var previousUi = CultureInfo.CurrentUICulture;
    try
    {
      CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
      var (db, app) = CreateServices();
      await using var _ = db;

      var agy = new AppAgentProvider { Handle = "Agy", Name = "Antigravity", IsActive = true, SortOrder = 10 };
      var custom = new AppAgentProvider { Handle = "CustomCli", Name = "Custom CLI", IsActive = false, SortOrder = 20 };
      db.AppAgentProvider.AddRange(agy, custom);
      db.AppAgentModel.AddRange(
        new AppAgentModel { ProviderHandle = "Agy", ProviderName = "Antigravity", ModelId = "agy-1", Name = "AGY One", IsActive = true, IsDefault = true, SortOrder = 10 },
        new AppAgentModel { ProviderHandle = "Agy", ProviderName = "Antigravity", ModelId = "agy-2", Name = "AGY Two", IsActive = false, IsForPhotos = false, SortOrder = 20 },
        new AppAgentModel { ProviderHandle = "CustomCli", ProviderName = "Custom CLI", ModelId = "custom-1", Name = "Custom One", IsActive = false, SortOrder = 10 });
      await db.SaveChangesAsync();

      var providerGrid = new TestAgentProviderGridService(db, app);

      // 1. Built-in provider with models cannot be deleted
      var refusedDelete = await providerGrid.DeleteAsync(new GridRequest { SelectedIds = [agy.Id], SelectedVersions = [agy.Ver] });
      Assert.Equal(MutationOutcome.Refused, refusedDelete.Outcome);
      Assert.Contains(refusedDelete.Messages ?? [], m => (m.Title ?? m.Detail) == "Cannot delete a provider that has models or is built-in.");

      // 2. Last active provider cannot be disabled via form save, and toggle-active preserves it active
      await providerGrid.ExecuteDeclaredActionAsync(
        "toggle-active",
        new GridRequest { SelectedIds = [agy.Id], SelectedVersions = [agy.Ver] },
        null);
      Assert.True((await db.AppAgentProvider.AsNoTracking().FirstAsync(p => p.Id == agy.Id)).IsActive);

      var providerForm = new TestAgentProviderFormService(db, app);
      var disablePayload = JsonSerializer.SerializeToElement(new { handle = "Agy", name = "Antigravity", isActive = false, sortOrder = 10 });
      var disableEx = await Assert.ThrowsAsync<FormRefusedException>(() =>
        providerForm.SaveAsync(disablePayload, new FormRequest { Id = agy.Id, Ver = agy.Ver, Value = disablePayload }, CancellationToken.None));
      Assert.Contains(disableEx.Messages, m => (m.Title ?? m.Detail) == "At least one AI provider must remain active.");

      // 3. Model of a disabled provider cannot be activated via form save, and toggle-active leaves it inactive
      var disabledModel = await db.AppAgentModel.FirstAsync(m => m.ModelId == "custom-1");
      var modelForm = new TestAgentModelFormService(db, app);
      var activatePayload = JsonSerializer.SerializeToElement(new
      {
        providerHandle = "CustomCli",
        providerName = "Custom CLI",
        modelId = "custom-1",
        name = "Custom One",
        isActive = true,
        sortOrder = 10
      });
      var modelEx = await Assert.ThrowsAsync<FormRefusedException>(() =>
        modelForm.SaveAsync(activatePayload, new FormRequest { Id = disabledModel.Id, Ver = disabledModel.Ver, Value = activatePayload }, CancellationToken.None));
      Assert.Contains(modelEx.Messages, m => (m.Title ?? m.Detail) == "Cannot activate a model of a disabled provider.");

      var modelGrid = new TestAgentModelGridService(db, app);
      await modelGrid.ExecuteDeclaredActionAsync(
        "toggle-active",
        new GridRequest { SelectedIds = [disabledModel.Id], SelectedVersions = [disabledModel.Ver] },
        null);
      Assert.False((await db.AppAgentModel.AsNoTracking().FirstAsync(m => m.Id == disabledModel.Id)).IsActive);

      // 4. SetAsDefault and SetForPhotos enforce mutual exclusion and auto-activate
      var secondModel = await db.AppAgentModel.FirstAsync(m => m.ModelId == "agy-2");
      var setDefaultRes = await modelGrid.ExecuteDeclaredActionAsync(
        "set-as-default",
        new GridRequest { SelectedIds = [secondModel.Id], SelectedVersions = [secondModel.Ver] },
        null);
      Assert.NotNull(setDefaultRes);
      Assert.Equal(MutationOutcome.Committed, setDefaultRes.Outcome);

      var reloadedSecond = await db.AppAgentModel.AsNoTracking().FirstAsync(m => m.Id == secondModel.Id);
      var reloadedFirst = await db.AppAgentModel.AsNoTracking().FirstAsync(m => m.ModelId == "agy-1");
      Assert.True(reloadedSecond.IsDefault);
      Assert.True(reloadedSecond.IsActive);
      Assert.False(reloadedFirst.IsDefault);

      var setPhotosRes = await modelGrid.ExecuteDeclaredActionAsync(
        "set-for-photos",
        new GridRequest { SelectedIds = [reloadedFirst.Id], SelectedVersions = [reloadedFirst.Ver] },
        null);
      Assert.NotNull(setPhotosRes);
      Assert.Equal(MutationOutcome.Committed, setPhotosRes.Outcome);
      Assert.True((await db.AppAgentModel.AsNoTracking().FirstAsync(m => m.Id == reloadedFirst.Id)).IsForPhotos);

      // 5. Top-level model grid filters out models of disabled providers (2 active-provider models out of 3)
      var topPage = await new TestAgentModelGridService(db, app).GetDataAsync(
        new GridRequest { IsDataOnly = true },
        CancellationToken.None);
      Assert.Equal(2, topPage.Rows.Count);

      // 6. Ukrainian localization resolves PlatformResource.uk.resx keys cleanly
      CultureInfo.CurrentUICulture = new CultureInfo("uk-UA");
      Assert.Equal("Код провайдера", Localizer.Instance["Provider Handle"]);
      Assert.Equal("Моделі ШІ", Localizer.Instance["AI models"]);
      Assert.Equal("Принаймні один провайдер ШІ має залишатися активним.", Localizer.Instance["At least one AI provider must remain active."]);
    }
    finally
    {
      CultureInfo.CurrentUICulture = previousUi;
    }
  }

  [Fact]
  public async Task MemoryExtensions_EnforcePascalCaseKindStampClockAndTogglePinned()
  {
    var (db, app) = CreateServices();
    await using var _ = db;

    var memoryForm = new TestAgentMemoryFormService(db, app);
    var createJson = JsonSerializer.SerializeToElement(new
    {
      title = "Pricing Rule",
      kind = AgentMemoryKinds.Rule,
      module = "Ecom",
      priority = 10,
      isPinned = false,
      isActive = true,
      content = "Always check stock first."
    });
    var savedObj = await memoryForm.SaveAsync(createJson, new FormRequest { Value = createJson }, CancellationToken.None);
    var saved = Assert.IsType<AppAgentMemory>(savedObj);
    Assert.Equal("Pricing Rule", saved.Title);
    Assert.Equal(AgentMemoryKinds.Rule, saved.Kind);
    Assert.Equal("Ecom", saved.Module);
    Assert.Equal("Always check stock first.", saved.Content);
    Assert.True(saved.UpdatedAt > DateTimeOffset.MinValue);

    var memoryGrid = new TestAgentMemoryGridService(db, app);
    var toggleRes = await memoryGrid.ExecuteDeclaredActionAsync(
      "toggle-pinned",
      new GridRequest { SelectedIds = [saved.Id], SelectedVersions = [saved.Ver] },
      null);
    Assert.NotNull(toggleRes);
    Assert.Equal(MutationOutcome.Committed, toggleRes.Outcome);

    var reloaded = await db.AppAgentMemory.AsNoTracking().FirstAsync(m => m.Id == saved.Id);
    Assert.True(reloaded.IsPinned);
  }
}
