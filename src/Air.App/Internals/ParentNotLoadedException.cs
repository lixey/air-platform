namespace Platform.App.Internals;

internal sealed class ParentNotLoadedException(Type parentType, Type serviceType)
  : InvalidOperationException(
    $"Parent '{parentType.Name}' is required by '{serviceType.Name}', but no parent record is loaded.");
