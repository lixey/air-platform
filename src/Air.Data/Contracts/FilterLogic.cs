namespace Air.Data.Contracts;

public static class FilterLogic
{
  public const string And = "and";
  public const string Or = "or";

  public static bool IsValid(string? logic) =>
    string.Equals(logic, And, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(logic, Or, StringComparison.OrdinalIgnoreCase);

  public static bool IsOr(string? logic) =>
    string.Equals(logic, Or, StringComparison.OrdinalIgnoreCase);
}
