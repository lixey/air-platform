namespace Platform.App.Grid.Internals;

internal sealed class GridCountBucket
{
  public static readonly PropertyInfo[] IntProps =
  [
    typeof(GridCountBucket).GetProperty(nameof(C0))!,
    typeof(GridCountBucket).GetProperty(nameof(C1))!,
    typeof(GridCountBucket).GetProperty(nameof(C2))!,
    typeof(GridCountBucket).GetProperty(nameof(C3))!,
    typeof(GridCountBucket).GetProperty(nameof(C4))!,
    typeof(GridCountBucket).GetProperty(nameof(C5))!,
    typeof(GridCountBucket).GetProperty(nameof(C6))!,
    typeof(GridCountBucket).GetProperty(nameof(C7))!,
    typeof(GridCountBucket).GetProperty(nameof(C8))!,
    typeof(GridCountBucket).GetProperty(nameof(C9))!,
    typeof(GridCountBucket).GetProperty(nameof(C10))!,
    typeof(GridCountBucket).GetProperty(nameof(C11))!,
    typeof(GridCountBucket).GetProperty(nameof(C12))!,
    typeof(GridCountBucket).GetProperty(nameof(C13))!,
    typeof(GridCountBucket).GetProperty(nameof(C14))!,
    typeof(GridCountBucket).GetProperty(nameof(C15))!,
    typeof(GridCountBucket).GetProperty(nameof(C16))!,
    typeof(GridCountBucket).GetProperty(nameof(C17))!,
    typeof(GridCountBucket).GetProperty(nameof(C18))!,
    typeof(GridCountBucket).GetProperty(nameof(C19))!,
    typeof(GridCountBucket).GetProperty(nameof(C20))!,
    typeof(GridCountBucket).GetProperty(nameof(C21))!,
    typeof(GridCountBucket).GetProperty(nameof(C22))!,
    typeof(GridCountBucket).GetProperty(nameof(C23))!
  ];

  public static readonly Func<GridCountBucket, int>[] Readers =
  [
    static b => b.C0,
    static b => b.C1,
    static b => b.C2,
    static b => b.C3,
    static b => b.C4,
    static b => b.C5,
    static b => b.C6,
    static b => b.C7,
    static b => b.C8,
    static b => b.C9,
    static b => b.C10,
    static b => b.C11,
    static b => b.C12,
    static b => b.C13,
    static b => b.C14,
    static b => b.C15,
    static b => b.C16,
    static b => b.C17,
    static b => b.C18,
    static b => b.C19,
    static b => b.C20,
    static b => b.C21,
    static b => b.C22,
    static b => b.C23
  ];

  public int C0 { get; set; }
  public int C1 { get; set; }
  public int C2 { get; set; }
  public int C3 { get; set; }
  public int C4 { get; set; }
  public int C5 { get; set; }
  public int C6 { get; set; }
  public int C7 { get; set; }
  public int C8 { get; set; }
  public int C9 { get; set; }
  public int C10 { get; set; }
  public int C11 { get; set; }
  public int C12 { get; set; }
  public int C13 { get; set; }
  public int C14 { get; set; }
  public int C15 { get; set; }
  public int C16 { get; set; }
  public int C17 { get; set; }
  public int C18 { get; set; }
  public int C19 { get; set; }
  public int C20 { get; set; }
  public int C21 { get; set; }
  public int C22 { get; set; }
  public int C23 { get; set; }
}
