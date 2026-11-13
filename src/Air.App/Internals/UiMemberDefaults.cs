namespace Platform.App.Internals;

internal static class UiMemberDefaults
{
  public static string ToCamelPath(string path)
  {
    var parts = path.Split('.');
    for (var i = 0; i < parts.Length; i++)
    {
      if (parts[i].Length > 0)
      {
        parts[i] = char.ToLowerInvariant(parts[i][0]) + parts[i][1..];
      }
    }

    return string.Join('.', parts);
  }

  public static string PathToLabel(string path, Type? ownerType = null)
  {
    var parts = path.Split('.');
    var last = parts[^1];
    if (parts.Length >= 2 &&
        (string.Equals(last, "name", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(last, "title", StringComparison.OrdinalIgnoreCase)))
    {
      last = parts[^2];
    }

    if (last.EndsWith("Id", StringComparison.Ordinal) && last.Length > 2)
    {
      var navCandidate = last[..^2];
      if (ownerType?.GetProperty(navCandidate, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) != null)
      {
        last = navCandidate;
      }
    }

    if (last.Length > 0)
    {
      last = char.ToUpperInvariant(last[0]) + last[1..];
    }

    return Regex.Replace(last, "([a-z0-9])([A-Z])", "$1 $2");
  }

  public static string ResolveFieldType(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(bool))
    {
      return UiFieldType.Checkbox;
    }

    if (underlying == typeof(DateOnly))
    {
      return UiFieldType.Date;
    }

    if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
    {
      return UiFieldType.DateTime;
    }

    if (underlying == typeof(TimeOnly) || underlying == typeof(TimeSpan))
    {
      return UiFieldType.Time;
    }

    if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) || underlying == typeof(byte) ||
        underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
    {
      return UiFieldType.Number;
    }

    return UiFieldType.Text;
  }

  public static string? ResolveTemporalType(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(DateOnly))
    {
      return UiTemporalType.DateOnly;
    }

    if (underlying == typeof(DateTime))
    {
      return UiTemporalType.DateTime;
    }

    if (underlying == typeof(DateTimeOffset))
    {
      return UiTemporalType.DateTimeOffset;
    }

    return null;
  }
}
