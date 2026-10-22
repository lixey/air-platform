namespace Air.App.Report;

public interface IReportService : IScreenService
{
  Feature Feature { get; }
  Task<ReportDataDto> GetReportAsync(ReportRequest request, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, ReportRequest? request, CancellationToken ct = default);
}

public abstract class ReportService(PlatformAppService? app = null) : IReportService
{
  protected PlatformAppService? App { get; private set; } = app;
  protected ILocalizer L => Localizer.Instance;

  public abstract Feature Feature { get; }

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
