namespace Air.App.Form.Actions;

public sealed class ActionInputRuleBuilder<TProp>(FormElementDto dto)
{
  public ActionInputRuleBuilder<TProp> MaximumLength(int maxLength)
  {
    dto.Constraints.MaxLength = maxLength;
    return this;
  }
}
