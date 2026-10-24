namespace Platform.Data.Tenancy;

/// <summary>
/// What held permissions cover, the one answer behind every check of a right: a key {Module}.{Feature}.{Action} is
/// covered by itself, its feature's {Module}.{Feature}.*, its module's {Module}.*, *.*.{Action}, *.{Action}, or *; a feature
/// pattern by itself, its module's pattern or *; a module pattern by itself or *; * by * alone.
/// </summary>
public static class HeldRights
{
  public const string All = "*";

  public static bool Covers(IReadOnlySet<string> held, string permission)
  {
    ArgumentNullException.ThrowIfNull(held);
    if (string.IsNullOrWhiteSpace(permission))
    {
      return false;
    }

    if (held.Contains(All) || held.Contains(permission))
    {
      return true;
    }

    return permission.Split('.') switch
    {
      [_, "*"] => false,
      [var module, _, "*"] => held.Contains($"{module}.*"),
      [var module, var feature, var action] =>
        held.Contains($"{module}.*") ||
        held.Contains($"{module}.{feature}.*") ||
        held.Contains($"*.{action}") ||
        held.Contains($"*.*.{action}"),
      _ => false
    };
  }
}
