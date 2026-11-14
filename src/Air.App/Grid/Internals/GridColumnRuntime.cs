using System.Runtime.CompilerServices;

namespace Platform.App.Grid.Internals;

internal sealed class GridColumnRuntime
{
  private static readonly ConditionalWeakTable<GridColumnDto, GridColumnRuntime> Runtimes = new();

  public static GridColumnRuntime Of(GridColumnDto column) =>
    Runtimes.GetOrCreateValue(column);

  public static GridColumnRuntime? TryGet(GridColumnDto column) =>
    Runtimes.TryGetValue(column, out var runtime) ? runtime : null;

  public static void Link(GridColumnDto source, GridColumnDto clone)
  {
    if (Runtimes.TryGetValue(source, out var runtime))
    {
      Runtimes.AddOrUpdate(clone, runtime);
    }
  }

  public Func<object, string?>? ColorEvaluator { get; set; }
  public LambdaExpression? ColorExpression { get; set; }
  public string? ColorExpressionKey { get; set; }
  public string? ColorNavPath { get; set; }
  public bool IsSummed { get; set; }
  public bool IsComputed { get; set; }
  public bool? ExplicitRequired { get; set; }
  public Type? MemberType { get; set; }
  public Type? PickerSourceType { get; set; }
  public MethodInfo? PickerTargetMethod { get; set; }
  public Type? ReferenceServiceType { get; set; }
  public Func<object, Type?>? FormServiceResolver { get; set; }
  public LambdaExpression? FilterExpression { get; set; }
  public Func<object?, object?>? ValueFormatter { get; set; }
  public Func<object, object?>? ValueEvaluator { get; set; }
  public LambdaExpression? ValueExpression { get; set; }
  public bool IsSubqueryExpression { get; set; }
}
