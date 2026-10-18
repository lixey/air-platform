using System.Diagnostics.CodeAnalysis;

namespace Air.App.Localization;

/// <summary>
/// Provides string-returning localization lookup for the active request culture.
/// </summary>
public interface ILocalizer
{
  /// <summary>
  /// Gets the localized string for the specified key.
  /// </summary>
  string this[string name] { get; }

  /// <summary>
  /// Gets the formatted localized string for the specified key and arguments.
  /// </summary>
  string this[string name, params object[] arguments] { get; }

  /// <summary>
  /// Scopes localization lookups to a specific module handle context.
  /// </summary>
  ILocalizer For(string? moduleHandle) => this;

  /// <summary>
  /// Resolves the localized string for the specified key in the module's context, returning whether a localized entry was found.
  /// </summary>
  bool TryGetString(string? moduleHandle, string name, [NotNullWhen(true)] out string? value)
  {
    if (string.IsNullOrWhiteSpace(moduleHandle))
    {
      return TryGetString(name, out value);
    }

    return For(moduleHandle).TryGetString(name, out value);
  }

  /// <summary>
  /// Resolves the localized string for the specified key without module context, returning whether a localized entry was found.
  /// </summary>
  bool TryGetString(string name, [NotNullWhen(true)] out string? value)
  {
    value = null;
    return false;
  }
}
