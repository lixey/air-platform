namespace Platform.App.Grid.Internals;

internal sealed class GridSumBucket
{
  public static readonly PropertyInfo[] DecimalProps =
  [
    typeof(GridSumBucket).GetProperty(nameof(D0))!,
    typeof(GridSumBucket).GetProperty(nameof(D1))!,
    typeof(GridSumBucket).GetProperty(nameof(D2))!,
    typeof(GridSumBucket).GetProperty(nameof(D3))!,
    typeof(GridSumBucket).GetProperty(nameof(D4))!,
    typeof(GridSumBucket).GetProperty(nameof(D5))!,
    typeof(GridSumBucket).GetProperty(nameof(D6))!,
    typeof(GridSumBucket).GetProperty(nameof(D7))!
  ];

  public static readonly PropertyInfo[] LongProps =
  [
    typeof(GridSumBucket).GetProperty(nameof(L0))!,
    typeof(GridSumBucket).GetProperty(nameof(L1))!,
    typeof(GridSumBucket).GetProperty(nameof(L2))!,
    typeof(GridSumBucket).GetProperty(nameof(L3))!,
    typeof(GridSumBucket).GetProperty(nameof(L4))!,
    typeof(GridSumBucket).GetProperty(nameof(L5))!,
    typeof(GridSumBucket).GetProperty(nameof(L6))!,
    typeof(GridSumBucket).GetProperty(nameof(L7))!
  ];

  public static readonly PropertyInfo[] DoubleProps =
  [
    typeof(GridSumBucket).GetProperty(nameof(F0))!,
    typeof(GridSumBucket).GetProperty(nameof(F1))!,
    typeof(GridSumBucket).GetProperty(nameof(F2))!,
    typeof(GridSumBucket).GetProperty(nameof(F3))!,
    typeof(GridSumBucket).GetProperty(nameof(F4))!,
    typeof(GridSumBucket).GetProperty(nameof(F5))!,
    typeof(GridSumBucket).GetProperty(nameof(F6))!,
    typeof(GridSumBucket).GetProperty(nameof(F7))!
  ];

  public decimal? D0 { get; set; }
  public decimal? D1 { get; set; }
  public decimal? D2 { get; set; }
  public decimal? D3 { get; set; }
  public decimal? D4 { get; set; }
  public decimal? D5 { get; set; }
  public decimal? D6 { get; set; }
  public decimal? D7 { get; set; }

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
}
