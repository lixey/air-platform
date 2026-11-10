using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Tests.Internals;

namespace Platform.Api.Tests;

public sealed class TimeProviderAndTimeZoneTests
{
  [Fact]
  public void TimeToday_UsesUserOrTenantTimeZone_AcrossMidnightBoundary()
  {
    var instant = new DateTimeOffset(2026, 11, 10, 22, 30, 0, TimeSpan.Zero);
    var services = new ServiceCollection()
      .AddSingleton<TimeProvider>(new FixedClock(instant))
      .BuildServiceProvider();

    var kyivApp = new PlatformAppService(new FixedZoneUser("Europe/Kyiv"), services);
    var utcApp = new PlatformAppService(new FixedZoneUser("UTC"), services);
    var fallbackApp = new PlatformAppService(new FixedZoneUser("Invalid/Zone"), services);

    Assert.Equal(instant, kyivApp.Time.UtcNow);
    Assert.Equal(new DateOnly(2026, 11, 11), kyivApp.Time.Today);
    Assert.Equal(0, kyivApp.Time.LocalNow.Hour);
    Assert.Equal(30, kyivApp.Time.LocalNow.Minute);

    Assert.Equal(new DateOnly(2026, 11, 10), utcApp.Time.Today);
    Assert.Equal(new DateOnly(2026, 11, 10), fallbackApp.Time.Today);
    Assert.Equal(TimeZoneInfo.Utc, fallbackApp.Time.LocalTimeZone);
  }
}
