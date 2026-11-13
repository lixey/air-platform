namespace Platform.App.Tests.Internals;

internal sealed class TestStrictCtorPlatformService : PlatformService
{
  private static int _nullDbProbes;

  public static int NullDbProbes => Volatile.Read(ref _nullDbProbes);
  public static void ResetProbes() => Interlocked.Exchange(ref _nullDbProbes, 0);

  public TestStrictCtorPlatformService(AppTestDbContext? db = null, PlatformAppService? app = null) : base(app)
  {
    if (db is null)
    {
      Interlocked.Increment(ref _nullDbProbes);
    }
  }

  public override Feature Feature => TestAppModule.Invoices;

  [Endpoint]
  public Task<OptionDto> Ping() => Task.FromResult(new OptionDto { Id = 1, Name = "ok" });
}
