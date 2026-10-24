namespace Platform.Data.Internals;

internal sealed class ValueHolder<T>(T value)
{
  public T Value { get; } = value;
}
