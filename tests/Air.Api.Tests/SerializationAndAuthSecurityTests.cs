using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Auth.Internals;
using Platform.Api.Caching.Internals;
using Platform.Api.Mutations.Internals;
using Platform.Api.Tests.Internals;

namespace Platform.Api.Tests;

public sealed class SerializationAndAuthSecurityTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public SerializationAndAuthSecurityTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    SessionTokenService.ConfigureSecret("unit-test-secret-for-serialization-auth-and-file-links");
  }

  public void Dispose() => _connection.Dispose();

  [Fact]
  public void StrictNumberConverters_AcceptEuropeanDecimals_AndNullableBlanks()
  {
    const string json = """
      {
        "count": "42",
        "optionalCount": "",
        "total": "1 250 000",
        "optionalTotal": null,
        "amount": "1 250,50",
        "optionalAmount": "  ",
        "createdOn": "2026-05-10T15:00:00",
        "optionalCreatedOn": ""
      }
      """;

    var parsed = JsonSerializer.Deserialize<StrictNumberPayload>(json, PlatformJsonOptions.DefaultOptions)!;

    Assert.Equal(42, parsed.Count);
    Assert.Null(parsed.OptionalCount);
    Assert.Equal(1_250_000L, parsed.Total);
    Assert.Null(parsed.OptionalTotal);
    Assert.Equal(1250.50m, parsed.Amount);
    Assert.Null(parsed.OptionalAmount);
    Assert.Null(parsed.OptionalCreatedOn);
  }

  [Theory]
  [InlineData("""{"count":null,"total":1,"amount":1,"createdOn":"2026-05-10T00:00:00"}""")]
  [InlineData("""{"count":"","total":1,"amount":1,"createdOn":"2026-05-10T00:00:00"}""")]
  [InlineData("""{"count":12.9,"total":1,"amount":1,"createdOn":"2026-05-10T00:00:00"}""")]
  [InlineData("""{"count":"12.9","total":1,"amount":1,"createdOn":"2026-05-10T00:00:00"}""")]
  [InlineData("""{"count":1,"optionalCount":12.9,"total":1,"amount":1,"createdOn":"2026-05-10T00:00:00"}""")]
  [InlineData("""{"count":9999999999999,"total":1,"amount":1,"createdOn":"2026-05-10T00:00:00"}""")]
  [InlineData("""{"count":1,"total":1,"amount":null,"createdOn":"2026-05-10T00:00:00"}""")]
  [InlineData("""{"count":1,"total":1,"amount":"","createdOn":"2026-05-10T00:00:00"}""")]
  [InlineData("""{"count":1,"total":1,"amount":"(100)","createdOn":"2026-05-10T00:00:00"}""")]
  [InlineData("""{"count":1,"total":1,"amount":"1,250.50","createdOn":"2026-05-10T00:00:00"}""")]
  [InlineData("""{"count":1,"total":1,"amount":1,"createdOn":null}""")]
  [InlineData("""{"count":1,"total":1,"amount":1,"createdOn":""}""")]
  public void StrictConverters_ThrowJsonException_OnSilentZeroTruncationOrAmbiguousInput(string badJson)
  {
    Assert.ThrowsAny<JsonException>(() =>
      JsonSerializer.Deserialize<StrictNumberPayload>(badJson, PlatformJsonOptions.DefaultOptions));
  }

  [Fact]
  public void FlexibleDateTimeConverter_AndCombinedResolver_PreserveUtcAndSlotKind()
  {
    const string offsetJson = """
      {
        "count": 1,
        "total": 1,
        "amount": 1,
        "createdOn": "2026-05-10T15:00:00+03:00",
        "optionalCreatedOn": "2026-05-10T12:00:00.123Z"
      }
      """;

    var parsedOffset = JsonSerializer.Deserialize<StrictNumberPayload>(offsetJson, PlatformJsonOptions.DefaultOptions)!;
    Assert.Equal(DateTimeKind.Utc, parsedOffset.CreatedOn.Kind);
    Assert.Equal(new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc), parsedOffset.CreatedOn);
    Assert.Equal(DateTimeKind.Utc, parsedOffset.OptionalCreatedOn!.Value.Kind);
    Assert.Equal(123, parsedOffset.OptionalCreatedOn.Value.Millisecond);

    var serializedUtc = JsonSerializer.Serialize(parsedOffset, PlatformJsonOptions.DefaultOptions);
    Assert.Contains("\"2026-05-10T12:00:00Z\"", serializedUtc, StringComparison.Ordinal);
    Assert.Contains("\"2026-05-10T12:00:00.123Z\"", serializedUtc, StringComparison.Ordinal);

    const string wallClockJson = """
      {
        "count": 1,
        "total": 1,
        "amount": 1,
        "createdOn": "2026-05-10T15:30:00"
      }
      """;

    var parsedWallClock = JsonSerializer.Deserialize<StrictNumberPayload>(wallClockJson, PlatformJsonOptions.DefaultOptions)!;
    Assert.Equal(DateTimeKind.Unspecified, parsedWallClock.CreatedOn.Kind);

    var serializedWallClock = JsonSerializer.Serialize(parsedWallClock, PlatformJsonOptions.DefaultOptions);
    Assert.Contains("\"2026-05-10T15:30:00\"", serializedWallClock, StringComparison.Ordinal);
    Assert.DoesNotContain("15:30:00Z", serializedWallClock, StringComparison.Ordinal);

    // Combined IJsonTypeInfoResolver still receives ModifySlotPolymorphism
    var customOpts = new JsonSerializerOptions
    {
      TypeInfoResolver = JsonTypeInfoResolver.Combine(new DefaultJsonTypeInfoResolver())
    };
    PlatformJsonOptions.ConfigurePlatformJson(customOpts);
    PlatformJsonOptions.ConfigurePlatformJson(customOpts);

    var slotJson = JsonSerializer.Serialize(new TestSlotModel { Label = "Save" }, customOpts);
    Assert.Contains("\"kind\":\"TestSlot\"", slotJson, StringComparison.Ordinal);
  }

  [Fact]
  public async Task UserAuthCache_PreventsStaleInFlightFill_AfterRemoveUserAsync()
  {
    var services = new ServiceCollection();
    services.AddHybridCache();
    services.AddSingleton<ICacheProvider, CacheProvider>();
    await using var provider = services.BuildServiceProvider();

    var authCache = new UserAuthCache(provider.GetRequiredService<ICacheProvider>());
    var enteredDbRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var allowStaleFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var currentTokenVersion = 1;

    var store = new RacyAuthStore
    {
      OnFindUserByIdAsync = async (id, _) =>
      {
        var capturedVersion = Volatile.Read(ref currentTokenVersion);
        if (capturedVersion == 1)
        {
          enteredDbRead.TrySetResult();
          await allowStaleFinish.Task;
        }

        return new TestAuthUser
        {
          Id = id,
          TenantId = 1,
          Login = "alice",
          TokenVersion = capturedVersion,
          IsActive = true
        };
      }
    };

    var staleRequest = authCache.GetUserProfileAsync(7, 1, store);
    await enteredDbRead.Task;

    Interlocked.Exchange(ref currentTokenVersion, 2);
    await authCache.RemoveUserAsync(7);
    allowStaleFinish.TrySetResult();

    var staleSnapshot = await staleRequest;
    var freshSnapshot = await authCache.GetUserProfileAsync(7, 1, store);

    Assert.Equal(1, staleSnapshot!.TokenVersion);
    Assert.Equal(2, freshSnapshot!.TokenVersion);
  }

  [Fact]
  public void LoginRateLimiter_BlocksAfterTwentyFailures_AndExpiresWithClock()
  {
    var clock = new ControllableTimeProvider(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero));
    var limiter = new LoginRateLimiter(clock);

    for (var i = 0; i < 19; i++)
    {
      limiter.RecordFailure("203.0.113.10");
      Assert.False(limiter.IsLimited("203.0.113.10"));
    }

    limiter.RecordFailure("203.0.113.10");
    Assert.True(limiter.IsLimited("203.0.113.10"));
    Assert.False(limiter.IsLimited("203.0.113.11"));

    clock.Advance(TimeSpan.FromMinutes(5));
    Assert.False(limiter.IsLimited("203.0.113.10"));
  }

  [Fact]
  public async Task IdempotencyStore_IsContainerIsolated_AndReplaysCustomNonEntityDto()
  {
    var options = new DbContextOptionsBuilder<ApiTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    await using var db = new ApiTestDbContext(options, new FixedTenant(1, 10));
    await db.Database.EnsureCreatedAsync();

    var servicesA = new ServiceCollection().AddSingleton<IdempotencyStore>().BuildServiceProvider();
    var servicesB = new ServiceCollection().AddSingleton<IdempotencyStore>().BuildServiceProvider();

    var runsA = 0;
    var r1 = await MutationRunner.RunIdempotentCreateAsync(
      db,
      "demo.custom-form",
      "idem-zero-id",
      "{\"code\":\"X\"}",
      _ =>
      {
        runsA++;
        return Task.FromResult(new TestSlotModel { Label = "CreatedOnce" });
      },
      (_, _) => Task.FromResult<TestSlotModel?>(null),
      services: servicesA);

    var r2 = await MutationRunner.RunIdempotentCreateAsync(
      db,
      "demo.custom-form",
      "idem-zero-id",
      "{\"code\":\"X\"}",
      _ =>
      {
        runsA++;
        return Task.FromResult(new TestSlotModel { Label = "CreatedTwice" });
      },
      (_, _) => Task.FromResult<TestSlotModel?>(null),
      services: servicesA);

    Assert.Equal("CreatedOnce", r1.Label);
    Assert.Equal("CreatedOnce", r2.Label);
    Assert.Equal(1, runsA);

    var runsB = 0;
    await MutationRunner.RunIdempotentCreateAsync(
      db,
      "demo.custom-form",
      "idem-zero-id",
      "{\"code\":\"X\"}",
      _ =>
      {
        runsB++;
        return Task.FromResult(new TestSlotModel { Label = "ContainerB" });
      },
      (_, _) => Task.FromResult<TestSlotModel?>(null),
      services: servicesB);

    Assert.Equal(1, runsB);
  }

  [Fact]
  public void FileLinks_HonorsInjectedTimeProviderForExpiration()
  {
    var clock = new ControllableTimeProvider(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero));
    var links = new FileLinks(clock);
    var key = FileKeys.New();

    var token = links.Sign(key, "application/pdf", "report.pdf", clock.GetUtcNow().AddHours(1));
    Assert.NotNull(links.Read(token));

    clock.Advance(TimeSpan.FromHours(2));
    Assert.Null(links.Read(token));
  }
}
