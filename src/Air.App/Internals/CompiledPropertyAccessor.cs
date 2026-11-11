namespace Platform.App.Internals;

internal sealed record CompiledPropertyAccessor(
  PropertyInfo Property,
  Func<object, object?>? Getter,
  Action<object, object?>? Setter);
