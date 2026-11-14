using System.Runtime.CompilerServices;

namespace Platform.App.Report.Internals;

internal static class ReportTableRuntime
{
  private static readonly ConditionalWeakTable<ReportTableDto, Type> OpenFormTypes = [];

  public static void RegisterOpenFormType(ReportTableDto table, Type formServiceType) =>
    OpenFormTypes.AddOrUpdate(table, formServiceType);

  public static Type? TryGetOpenFormType(ReportTableDto table) =>
    OpenFormTypes.TryGetValue(table, out var formType) ? formType : null;
}
