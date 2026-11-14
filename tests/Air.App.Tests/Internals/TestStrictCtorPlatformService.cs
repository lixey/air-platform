namespace Platform.App.Tests.Internals;

internal sealed class TestStrictCtorPlatformService : PlatformService, IPlatformService
{
  private static int _nullDbProbes;
  private static int _validationCalls;

  public static int NullDbProbes => Volatile.Read(ref _nullDbProbes);
  public static int ValidationCalls => Volatile.Read(ref _validationCalls);

  public static void ResetProbes()
  {
    Interlocked.Exchange(ref _nullDbProbes, 0);
    Interlocked.Exchange(ref _validationCalls, 0);
  }

  public TestStrictCtorPlatformService(AppTestDbContext? db = null, PlatformAppService? app = null) : base(app)
  {
    if (db is null)
    {
      Interlocked.Increment(ref _nullDbProbes);
    }
  }

  public override Feature Feature => TestAppModule.Invoices;

  void IPlatformService.ValidateConfiguration(ModuleCatalog catalog) =>
    Interlocked.Increment(ref _validationCalls);

  [Endpoint]
  public Task<OptionDto> Ping() => Task.FromResult(new OptionDto { Id = 1, Name = "ok" });

  [Endpoint]
  public void DoVoid() { }

  [Endpoint]
  public OptionDto? SyncNullDto() => null;

  [Endpoint]
  public Task<MutationResultDto?> AsyncNullMutation() => Task.FromResult<MutationResultDto?>(null);
}
