namespace Platform.App.Tests;

public sealed class ColumnSummaryTests
{
  [Fact]
  public void GridColumn_SelectionSummary_NormalizesUnitAndExcludeColor()
  {
    var columns = new Columns();
    var sumCol = columns.Add<decimal>("amount").SelectionSum("₴", UiColor.Blue);
    var avgCol = columns.Add<decimal>("unitPrice").SelectionSummary(SummaryKind.Avg);
    var countCol = columns.Add<string>("note").SelectionSummary(SummaryKind.Count);

    Assert.NotNull(sumCol.Dto.SelectionSummary);
    Assert.Equal(SummaryKind.Sum, sumCol.Dto.SelectionSummary.Kind);
    Assert.Equal("₴", sumCol.Dto.SelectionSummary.Unit);
    Assert.Equal("blue", sumCol.Dto.SelectionSummary.ExcludeColor);

    Assert.NotNull(avgCol.Dto.SelectionSummary);
    Assert.Equal(SummaryKind.Avg, avgCol.Dto.SelectionSummary.Kind);
    Assert.Null(avgCol.Dto.SelectionSummary.Unit);
    Assert.Null(avgCol.Dto.SelectionSummary.ExcludeColor);

    Assert.NotNull(countCol.Dto.SelectionSummary);
    Assert.Equal(SummaryKind.Count, countCol.Dto.SelectionSummary.Kind);
  }

  [Fact]
  public void GridColumn_SelectionSummary_RejectsUnknownKindOrNonNumericAggregation()
  {
    var columns = new Columns();
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      columns.Add<decimal>("amount").SelectionSummary("median"));

    Assert.Throws<InvalidOperationException>(() =>
      columns.Add<string>("title").SelectionSummary(SummaryKind.Sum));
  }
}
