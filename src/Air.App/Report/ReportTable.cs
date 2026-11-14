namespace Platform.App.Report;

public static class ReportTable
{
  public static ReportTableBuilder<TRow> For<TRow>(IEnumerable<TRow> rows, IPlatformService? host = null) =>
    new(rows, host);
}
