using System.Collections;
using System.Globalization;
using System.Resources;
using Platform.Api.Mutations.Internals;
using Platform.Api.Tests.Internals;

namespace Platform.Api.Tests;

public sealed class Wave5ApiLocalizationAndAuthTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public Wave5ApiLocalizationAndAuthTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    IdempotencyStore.Default.Clear();
    SessionTokenService.ConfigureSecret("unit-test-secret-for-wave5-api-localization-and-auth");
  }

  public void Dispose()
  {
    IdempotencyStore.Default.Clear();
    _connection.Dispose();
  }

  private ApiTestDbContext CreateDbContext(long tenantId = 1, long userId = 10)
  {
    var options = new DbContextOptionsBuilder<ApiTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var db = new ApiTestDbContext(options, new FixedTenant(tenantId, userId));
    db.Database.EnsureCreated();
    return db;
  }

  [Fact]
  public void UiTexts_English_IsSingleSourcedFromPlatformResource_WithUkrainianParity()
  {
    var mgr = new ResourceManager(typeof(PlatformResource));
    var invariantSet = mgr.GetResourceSet(
      CultureInfo.InvariantCulture,
      createIfNotExists: true,
      tryParents: true)!;
    var ukCulture = CultureInfo.GetCultureInfo("uk-UA");

    var expectedCount = 0;
    foreach (DictionaryEntry entry in invariantSet)
    {
      if (entry.Key is string key && entry.Value is string value)
      {
        expectedCount++;
        Assert.True(UiTexts.English.TryGetValue(key, out var uiValue), $"Missing key '{key}' in UiTexts.English.");
        Assert.Equal(value, uiValue);

        var ukValue = mgr.GetString(key, ukCulture);
        Assert.False(string.IsNullOrWhiteSpace(ukValue), $"Missing Ukrainian translation for '{key}'.");
      }
    }

    Assert.Equal(expectedCount, UiTexts.English.Count);
    Assert.True(UiTexts.English.ContainsKey("FormValidationInvalid"));
    Assert.True(UiTexts.English.ContainsKey("FileNotFound"));
    Assert.True(UiTexts.English.ContainsKey("NoRecordsSelected"));
    Assert.True(UiTexts.English.ContainsKey("IdempotencyKeyReused"));
  }

  [Fact]
  public async Task SourceTitle_FormatsDynamicallyPerCulture_AndIdempotencyErrorsFollowCurrentCulture()
  {
    await using var db = CreateDbContext();
    var mgr = new ResourceManager(typeof(PlatformResource));
    var sourceTitle = Feature.FormatSourceTitle("Sales Orders", "dbo.Orders");

    var prevCulture = CultureInfo.CurrentUICulture;
    try
    {
      CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
      Assert.Equal("Legacy UI: Sales Orders | DB: dbo.Orders", Localizer.Instance[sourceTitle]);

      CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");
      Assert.Equal("Легасі UI: Sales Orders | БД: dbo.Orders", Localizer.Instance[sourceTitle]);

      var badKeyEx = await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
        MutationRunner.RunIdempotentMutationAsync(
          db,
          "svc/op",
          new string('x', 130),
          "{}",
          _ => Task.FromResult(new MutationResultDto { Outcome = MutationOutcome.Committed })));
      Assert.Equal(mgr.GetString("IdempotencyKeyInvalid", CultureInfo.CurrentUICulture), badKeyEx.Message);
    }
    finally
    {
      CultureInfo.CurrentUICulture = prevCulture;
    }
  }

  [Fact]
  public void SessionTokenService_RespectsCustomTimeProvider_AcrossExpirationBoundary()
  {
    var start = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
    var clock = new ControllableTimeProvider(start);
    var token = SessionTokenService.CreateAccessToken(sessionId: 77, userId: 12, expiresAt: start.AddMinutes(15), tokenVersion: 3);

    clock.Advance(TimeSpan.FromMinutes(14));
    Assert.True(SessionTokenService.TryValidateAccessToken(token, out var sid, out var uid, out _, out var ver, clock));
    Assert.Equal(77L, sid);
    Assert.Equal(12L, uid);
    Assert.Equal(3, ver);

    clock.Advance(TimeSpan.FromMinutes(2));
    Assert.False(SessionTokenService.TryValidateAccessToken(token, out _, out _, out _, out _, clock));
  }

  [Fact]
  public async Task RunIdempotentCreateAsync_ThrowsWhenReloadByIdIsNull_AndReplaysViaReloadById()
  {
    await using var db = CreateDbContext();

    await Assert.ThrowsAsync<ArgumentNullException>(() =>
      MutationRunner.RunIdempotentCreateAsync<ApiTestItem>(
        db,
        "test/create",
        "idem-null-guard",
        """{"name":"A"}""",
        async ct =>
        {
          var created = new ApiTestItem { Name = "A" };
          db.Items.Add(created);
          await db.SaveChangesAsync(ct);
          return created;
        },
        reloadById: null!,
        extractId: r => r.Id));

    var createRuns = 0;
    var reloadRuns = 0;

    async Task<ApiTestItem> RunOnceAsync() =>
      await MutationRunner.RunIdempotentCreateAsync<ApiTestItem>(
        db,
        "test/create",
        "idem-create-1",
        """{"name":"Widget"}""",
        async ct =>
        {
          createRuns++;
          var created = new ApiTestItem { Name = "Widget" };
          db.Items.Add(created);
          await db.SaveChangesAsync(ct);
          return created;
        },
        async (id, ct) =>
        {
          reloadRuns++;
          return await db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        },
        extractId: r => r.Id);

    var first = await RunOnceAsync();
    var replay = await RunOnceAsync();

    Assert.Equal(1, createRuns);
    Assert.Equal(1, reloadRuns);
    Assert.Equal(first.Id, replay.Id);
    Assert.Equal("Widget", replay.Name);
  }
}
