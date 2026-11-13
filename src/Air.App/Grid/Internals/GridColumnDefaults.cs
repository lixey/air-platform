namespace Platform.App.Grid.Internals;

internal static class GridColumnDefaults
{
  public static CellModel ResolveDefaultCell(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(bool))
    {
      return new GridCellCheckboxModel();
    }

    if (underlying == typeof(DateOnly))
    {
      return new GridCellDateModel();
    }

    if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
    {
      return new GridCellDateTimeModel();
    }

    if (underlying == typeof(TimeOnly) || underlying == typeof(TimeSpan))
    {
      return new GridCellTimeModel();
    }

    if (underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
    {
      return new GridCellNumberModel { Decimals = 2, IsGrouped = true };
    }

    if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) || underlying == typeof(byte))
    {
      return new GridCellNumberModel { Decimals = 0, IsGrouped = true };
    }

    return new GridCellTextModel();
  }
}
