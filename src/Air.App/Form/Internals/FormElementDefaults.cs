namespace Platform.App.Form.Internals;

internal static class FormElementDefaults
{
  public static FormElementModel ResolveDefaultModel(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(bool))
    {
      return new FormCheckboxModel();
    }

    if (underlying == typeof(DateOnly))
    {
      return new FormDateModel();
    }

    if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
    {
      return new FormDateTimeModel();
    }

    if (underlying == typeof(TimeOnly) || underlying == typeof(TimeSpan))
    {
      return new FormTimeModel();
    }

    if (underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
    {
      return new FormNumberModel { Step = 0.01 };
    }

    if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) || underlying == typeof(byte))
    {
      return new FormNumberModel { Step = 1.0 };
    }

    return new FormInputModel();
  }
}
