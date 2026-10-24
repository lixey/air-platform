namespace Platform.App.Form.Actions;

public sealed class ActionInputRuleBuilder<TProp>(FormElementDto dto)
{
  public ActionInputRuleBuilder<TProp> MaximumLength(int maxLength)
  {
    dto.Constraints.MaxLength = maxLength;
    dto.Constraints.LengthUnit ??= FormLengthUnit.Utf16;
    return this;
  }

  public ActionInputRuleBuilder<TProp> MinimumLength(int minLength)
  {
    dto.Constraints.MinLength = minLength;
    dto.Constraints.LengthUnit ??= FormLengthUnit.Utf16;
    return this;
  }

  public ActionInputRuleBuilder<TProp> GreaterThan(decimal value)
  {
    dto.Constraints.Min = JsonSerializer.SerializeToElement(value + 0.0001m);
    return this;
  }

  public ActionInputRuleBuilder<TProp> GreaterThanOrEqualTo(decimal value)
  {
    dto.Constraints.Min = JsonSerializer.SerializeToElement(value);
    return this;
  }
}
