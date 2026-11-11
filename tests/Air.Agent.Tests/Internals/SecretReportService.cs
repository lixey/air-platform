namespace Platform.Agent.Tests.Internals;

internal sealed class SecretReportService(PlatformAppService app) : ReportService(app)
{
  public override Feature Feature => AgentTestModule.SecretReport;

  public override Task<ReportDataDto> GetReportAsync(ReportRequest request, CancellationToken ct = default) =>
    Task.FromResult(new ReportDataDto
    {
      Kpis = [new ReportKpiDto { Label = "TopSecretMargin", Value = 999m }]
    });
}
