namespace Platform.App.Tests.Internals;

internal sealed class TestRestrictedEnrichedGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoice>(db, app)
{
  public List<RowDto> ShapedRowsSeen { get; } = [];

  public override Feature Feature => TestAppModule.Invoices;

  protected override void SetRestrictions() =>
    Restrictions.Restrict(i => !i.IsHidden);

  protected override void SetColumns()
  {
    Columns.Add(i => i.Number)
      .Editable()
      .ConfirmEdit("Confirm invoice number change?")
      .Color(i => i.AuditedTotal >= 100m ? UiColor.Emerald : UiColor.Slate);
    Columns.Add<string>("computedLabel").Computed().Label("Computed");
  }

  protected override void SetQuery()
  {
    Query.SortBy(i => i.Id);
    Query.FlagRows("vip", i => i.AuditedTotal >= 100m);
    Query.Compute(async (IReadOnlyList<GridComputedRow<TestInvoice>> rows, CancellationToken ct) =>
    {
      var visibleTotal = await ReachableEntities().CountAsync(ct);
      foreach (var row in rows)
      {
        row["computedLabel"] = $"{row.Record.Number}/{visibleTotal}";
      }
    });
  }

  protected override RowDto ShapeRow(
    TestInvoice row,
    IReadOnlyList<GridColumnDto> columns,
    IReadOnlyList<(string Name, Func<TestInvoice, bool> Predicate)>? rowFlags = null)
  {
    var dto = base.ShapeRow(row, columns, rowFlags);
    ShapedRowsSeen.Add(dto);
    return dto;
  }
}
