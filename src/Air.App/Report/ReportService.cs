using Platform.App.Report.Internals;

namespace Platform.App.Report;

public abstract class ReportService(PlatformAppService? app = null) : PlatformService(app), IReportService
{
  protected PlatformAppService? App => PlatformApp;
  internal PlatformAppService? HostApp => PlatformApp;

  public abstract override Feature Feature { get; }

  protected static ReportFilters Filters(ReportRequest? request) => new(request?.Filters);

  protected ReportTableBuilder<TRow> Table<TRow>(IEnumerable<TRow> rows) =>
    ReportTable.For(rows, this);

  public abstract Task<ReportDataDto> GetReportAsync(ReportRequest request, CancellationToken ct = default);

  [Endpoint(Auth = EndpointAuth.View)]
  public virtual async Task<ScreenDto> GetData(
    ReportRequest? request,
    HttpRequest httpRequest,
    CancellationToken ct = default)
  {
    var req = request ?? new ReportRequest();
    if (req.IsDataOnly)
    {
      var data = await GetReportAsync(req, ct);
      ValidateAndFilterReport(GetType(), data, User, Catalog);
      return new ScreenDto { Data = data };
    }

    var serviceName = ScreenService.ExtractRouteServicePath(httpRequest) ?? ServicePathResolver.Resolve(GetType());
    return await BuildScreenAsync(serviceName, req, ct);
  }

  public Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default) =>
    BuildScreenAsync(servicePath, null, ct);

  public virtual async Task<ScreenDto> BuildScreenAsync(
    string servicePath,
    ReportRequest? request,
    CancellationToken ct = default)
  {
    var data = await GetReportAsync(request ?? new ReportRequest(), ct);
    ValidateAndFilterReport(GetType(), data, User, Catalog);

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

  public static void ValidateAndFilterReport(
    Type hostServiceType,
    ReportDataDto data,
    IUserContext? user,
    ModuleCatalog catalog)
  {
    if (data.Tables is not { Count: > 0 })
    {
      return;
    }

    foreach (var table in data.Tables)
    {
      var openFormType = ReportTableRuntime.TryGetOpenFormType(table);
      if (openFormType is not null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(hostServiceType, openFormType, "ReportTable.OpenWith", catalog);
        if (user is not null && catalog.FindFeature(openFormType) is { } targetFeat && !ServiceEndpointsExtensions.CanView(targetFeat, user))
        {
          foreach (var row in table.Rows)
          {
            row.Open = null;
          }
        }
      }

      foreach (var row in table.Rows)
      {
        if (row.Open is not { Service.Length: > 0 } open)
        {
          continue;
        }

        var rowTargetType = catalog.FindServiceType(open.Service) ?? catalog.FindFeature(open.Service)?.ServiceType;
        if (rowTargetType is not null)
        {
          ModuleCatalog.EnsureModuleServiceAccessAllowed(hostServiceType, rowTargetType, $"RowDto.Open '{open.Service}'", catalog);
        }

        if (user is not null)
        {
          var rowFeat = (rowTargetType is not null ? catalog.FindFeature(rowTargetType) : null)
            ?? catalog.FindFeature(open.Service);
          if (rowFeat is not null && !ServiceEndpointsExtensions.CanView(rowFeat, user))
          {
            row.Open = null;
          }
        }
      }
    }
  }
}
