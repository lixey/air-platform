namespace Platform.Data.Internals;

internal static class BoundValue
{
  public static Expression Of(object? value, Type type)
  {
    var holderType = typeof(ValueHolder<>).MakeGenericType(type);
    var holder = Activator.CreateInstance(holderType, value);
    return Expression.Property(Expression.Constant(holder, holderType), nameof(ValueHolder<object>.Value));
  }
}
