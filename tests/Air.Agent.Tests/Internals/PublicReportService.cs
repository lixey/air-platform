namespace Platform.Agent.Tests.Internals;

internal sealed class PublicReportService(PlatformAppService app) : ReportService(app)
{
  public override Feature Feature => AgentTestModule.PublicReport;

  public override Task<ReportDataDto> GetReportAsync(ReportRequest request, CancellationToken ct = default) =>
    Task.FromResult(new ReportDataDto
    {
      Kpis = [new ReportKpiDto { Label = "TotalPublicRevenue", Value = 420m }]
    });
}
