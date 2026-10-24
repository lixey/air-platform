namespace Air.App.Report;

public interface IReportService : IScreenService
{
  new Feature Feature { get; }
  Feature IPlatformService.Feature => Feature;
  Task<ReportDataDto> GetReportAsync(ReportRequest request, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, ReportRequest? request, CancellationToken ct = default);
}

public abstract class ReportService(PlatformAppService? app = null) : PlatformService(app), IReportService
{
  protected PlatformAppService? App => PlatformApp;

  public abstract override Feature Feature { get; }

  public abstract Task<ReportDataDto> GetReportAsync(ReportRequest request, CancellationToken ct = default);

  public Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default) =>
    BuildScreenAsync(servicePath, null, ct);

  public virtual async Task<ScreenDto> BuildScreenAsync(
    string servicePath,
    ReportRequest? request,
    CancellationToken ct = default)
  {
    var data = await GetReportAsync(request ?? new ReportRequest(), ct);
    var layout = PanelLayout
      .Column(servicePath)
      .Panel("main", new ReportModel { Service = servicePath, Title = L[Feature.Name] })
      .Build();

    return new ScreenDto
    {
      Layout = layout,
      Data = data
    };
  }
}
