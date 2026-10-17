namespace Air.Data.Internal;

internal sealed class ValueHolder<T>(T value)
{
  public T Value { get; } = value;
}
