namespace Platform.App.Tests.Internals;

internal sealed class TestSummaryReportService(PlatformAppService? app = null) : ReportService(app)
{
  public override Feature Feature => TestAppModule.SummaryReport;

  public PlatformAppService? CurrentAppService => PlatformApp;
  public long? LastSeenUserId { get; private set; }

  public override Task<ReportDataDto> GetReportAsync(ReportRequest request, CancellationToken ct = default)
  {
    LastSeenUserId = User?.UserId;
    return Task.FromResult(new ReportDataDto());
  }
}
