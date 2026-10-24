namespace Platform.App.Report;

public interface IReportService : IScreenService
{
  new Feature Feature { get; }
  Feature IPlatformService.Feature => Feature;
  Task<ReportDataDto> GetReportAsync(ReportRequest request, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, ReportRequest? request, CancellationToken ct = default);
}
