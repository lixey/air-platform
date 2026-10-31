namespace Platform.App.Grid.Internals;

internal sealed class SumRowBucket
{
  public decimal? D0 { get; set; }
  public decimal? D1 { get; set; }
  public decimal? D2 { get; set; }
  public decimal? D3 { get; set; }
  public decimal? D4 { get; set; }
  public decimal? D5 { get; set; }
  public decimal? D6 { get; set; }
  public decimal? D7 { get; set; }
  public decimal? D8 { get; set; }
  public decimal? D9 { get; set; }
  public decimal? D10 { get; set; }
  public decimal? D11 { get; set; }
  public decimal? D12 { get; set; }
  public decimal? D13 { get; set; }
  public decimal? D14 { get; set; }
  public decimal? D15 { get; set; }

  public long? L0 { get; set; }
  public long? L1 { get; set; }
  public long? L2 { get; set; }
  public long? L3 { get; set; }
  public long? L4 { get; set; }
  public long? L5 { get; set; }
  public long? L6 { get; set; }
  public long? L7 { get; set; }

  public double? F0 { get; set; }
  public double? F1 { get; set; }
  public double? F2 { get; set; }
  public double? F3 { get; set; }
  public double? F4 { get; set; }
  public double? F5 { get; set; }
  public double? F6 { get; set; }
  public double? F7 { get; set; }

  public static readonly PropertyInfo[] DecimalProps =
    Enumerable.Range(0, 16).Select(i => typeof(SumRowBucket).GetProperty($"D{i}")!).ToArray();

  public static readonly PropertyInfo[] LongProps =
    Enumerable.Range(0, 8).Select(i => typeof(SumRowBucket).GetProperty($"L{i}")!).ToArray();

  public static readonly PropertyInfo[] DoubleProps =
    Enumerable.Range(0, 8).Select(i => typeof(SumRowBucket).GetProperty($"F{i}")!).ToArray();
}
