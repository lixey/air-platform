namespace Platform.App.Form;

public static class FormElementNumberExtensions
{
  public static FormElement<TEntity, TProp> Range<TEntity, TProp>(
    this FormElement<TEntity, TProp> element,
    decimal min,
    decimal max)
    where TProp : struct, INumber<TProp> =>
    ApplyRange(element, min, max);

  public static FormElement<TEntity, TProp?> Range<TEntity, TProp>(
    this FormElement<TEntity, TProp?> element,
    decimal min,
    decimal max)
    where TProp : struct, INumber<TProp> =>
    ApplyRange(element, min, max);

  public static FormElement<TEntity, TProp> Min<TEntity, TProp>(
    this FormElement<TEntity, TProp> element,
    decimal min)
    where TProp : struct, INumber<TProp> =>
    ApplyMin(element, min);

  public static FormElement<TEntity, TProp?> Min<TEntity, TProp>(
    this FormElement<TEntity, TProp?> element,
    decimal min)
    where TProp : struct, INumber<TProp> =>
    ApplyMin(element, min);

  public static FormElement<TEntity, TProp> Max<TEntity, TProp>(
    this FormElement<TEntity, TProp> element,
    decimal max)
    where TProp : struct, INumber<TProp> =>
    ApplyMax(element, max);

  public static FormElement<TEntity, TProp?> Max<TEntity, TProp>(
    this FormElement<TEntity, TProp?> element,
    decimal max)
    where TProp : struct, INumber<TProp> =>
    ApplyMax(element, max);

  public static FormElement<TEntity, TProp> GreaterThan<TEntity, TProp>(
    this FormElement<TEntity, TProp> element,
    decimal value)
    where TProp : struct, INumber<TProp> =>
    ApplyGreaterThan(element, value);

  public static FormElement<TEntity, TProp?> GreaterThan<TEntity, TProp>(
    this FormElement<TEntity, TProp?> element,
    decimal value)
    where TProp : struct, INumber<TProp> =>
    ApplyGreaterThan(element, value);

  public static FormElement<TEntity, TProp> AsPercent<TEntity, TProp>(this FormElement<TEntity, TProp> element)
    where TProp : struct, INumber<TProp> =>
    ApplyPercent(element);

  public static FormElement<TEntity, TProp?> AsPercent<TEntity, TProp>(this FormElement<TEntity, TProp?> element)
    where TProp : struct, INumber<TProp> =>
    ApplyPercent(element);

  public static FormElement<TEntity, TProp> AsMoney<TEntity, TProp>(
    this FormElement<TEntity, TProp> element,
    string? currency = null)
    where TProp : struct, INumber<TProp> =>
    ApplyMoney(element, currency);

  public static FormElement<TEntity, TProp?> AsMoney<TEntity, TProp>(
    this FormElement<TEntity, TProp?> element,
    string? currency = null)
    where TProp : struct, INumber<TProp> =>
    ApplyMoney(element, currency);

  public static FormElement<TEntity, TProp> Stepper<TEntity, TProp>(
    this FormElement<TEntity, TProp> element,
    bool hasStepper = true)
    where TProp : struct, INumber<TProp> =>
    ApplyStepper(element, hasStepper);

  public static FormElement<TEntity, TProp?> Stepper<TEntity, TProp>(
    this FormElement<TEntity, TProp?> element,
    bool hasStepper = true)
    where TProp : struct, INumber<TProp> =>
    ApplyStepper(element, hasStepper);

  public static FormElement<TEntity, TProp> Step<TEntity, TProp>(
    this FormElement<TEntity, TProp> element,
    double step)
    where TProp : struct, INumber<TProp> =>
    ApplyStep(element, step);

  public static FormElement<TEntity, TProp?> Step<TEntity, TProp>(
    this FormElement<TEntity, TProp?> element,
    double step)
    where TProp : struct, INumber<TProp> =>
    ApplyStep(element, step);

  public static FormElement<TEntity, TProp> Grouped<TEntity, TProp>(
    this FormElement<TEntity, TProp> element,
    bool isGrouped = true)
    where TProp : struct, INumber<TProp> =>
    ApplyGrouped(element, isGrouped);

  public static FormElement<TEntity, TProp?> Grouped<TEntity, TProp>(
    this FormElement<TEntity, TProp?> element,
    bool isGrouped = true)
    where TProp : struct, INumber<TProp> =>
    ApplyGrouped(element, isGrouped);

  private static FormElement<TEntity, T> ApplyRange<TEntity, T>(FormElement<TEntity, T> element, decimal min, decimal max)
  {
    element.Dto.Constraints.Min = min;
    element.Dto.Constraints.ExclusiveMin = null;
    element.Dto.Constraints.Max = max;
    return element;
  }

  private static FormElement<TEntity, T> ApplyMin<TEntity, T>(FormElement<TEntity, T> element, decimal min)
  {
    element.Dto.Constraints.Min = min;
    element.Dto.Constraints.ExclusiveMin = null;
    return element;
  }

  private static FormElement<TEntity, T> ApplyMax<TEntity, T>(FormElement<TEntity, T> element, decimal max)
  {
    element.Dto.Constraints.Max = max;
    return element;
  }

  private static FormElement<TEntity, T> ApplyGreaterThan<TEntity, T>(FormElement<TEntity, T> element, decimal value)
  {
    element.Dto.Constraints.Min = value;
    element.Dto.Constraints.ExclusiveMin = true;
    return element;
  }

  private static FormElement<TEntity, T> ApplyPercent<TEntity, T>(FormElement<TEntity, T> element)
  {
    element.Dto.Model = new FormPercentModel();
    return element;
  }

  private static FormElement<TEntity, T> ApplyMoney<TEntity, T>(FormElement<TEntity, T> element, string? currency)
  {
    element.Dto.Type = UiFieldType.Number;
    element.Dto.Model = new FormMoneyModel { Currency = currency };
    return element;
  }

  private static FormElement<TEntity, T> ApplyStepper<TEntity, T>(FormElement<TEntity, T> element, bool hasStepper)
  {
    if (element.Dto.Model is FormNumberModel numModel)
    {
      numModel.HasStepper = hasStepper;
    }

    return element;
  }

  private static FormElement<TEntity, T> ApplyStep<TEntity, T>(FormElement<TEntity, T> element, double step)
  {
    if (element.Dto.Model is FormNumberModel numModel)
    {
      numModel.Step = step;
    }

    return element;
  }

  private static FormElement<TEntity, T> ApplyGrouped<TEntity, T>(FormElement<TEntity, T> element, bool isGrouped)
  {
    if (element.Dto.Model is FormNumberModel num)
    {
      num.IsGrouped = isGrouped;
    }
    else if (element.Dto.Model is FormInfoModel info)
    {
      info.IsGrouped = isGrouped;
    }

    return element;
  }
}
