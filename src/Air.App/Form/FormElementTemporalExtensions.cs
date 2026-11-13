namespace Platform.App.Form;

public static class FormElementTemporalExtensions
{
  public static FormElement<TEntity, DateOnly> AsDate<TEntity>(this FormElement<TEntity, DateOnly> element) =>
    ApplyDate(element);

  public static FormElement<TEntity, DateOnly?> AsDate<TEntity>(this FormElement<TEntity, DateOnly?> element) =>
    ApplyDate(element);

  public static FormElement<TEntity, DateTime> AsDate<TEntity>(this FormElement<TEntity, DateTime> element) =>
    ApplyDate(element);

  public static FormElement<TEntity, DateTime?> AsDate<TEntity>(this FormElement<TEntity, DateTime?> element) =>
    ApplyDate(element);

  public static FormElement<TEntity, DateTimeOffset> AsDate<TEntity>(this FormElement<TEntity, DateTimeOffset> element) =>
    ApplyDate(element);

  public static FormElement<TEntity, DateTimeOffset?> AsDate<TEntity>(this FormElement<TEntity, DateTimeOffset?> element) =>
    ApplyDate(element);

  public static FormElement<TEntity, DateTime> AsDateTime<TEntity>(this FormElement<TEntity, DateTime> element) =>
    ApplyDateTime(element);

  public static FormElement<TEntity, DateTime?> AsDateTime<TEntity>(this FormElement<TEntity, DateTime?> element) =>
    ApplyDateTime(element);

  public static FormElement<TEntity, DateTimeOffset> AsDateTime<TEntity>(this FormElement<TEntity, DateTimeOffset> element) =>
    ApplyDateTime(element);

  public static FormElement<TEntity, DateTimeOffset?> AsDateTime<TEntity>(this FormElement<TEntity, DateTimeOffset?> element) =>
    ApplyDateTime(element);

  private static FormElement<TEntity, T> ApplyDate<TEntity, T>(FormElement<TEntity, T> element)
  {
    element.Dto.Type = UiFieldType.Date;
    element.Dto.Model = new FormDateModel();
    return element;
  }

  private static FormElement<TEntity, T> ApplyDateTime<TEntity, T>(FormElement<TEntity, T> element)
  {
    element.Dto.Type = UiFieldType.DateTime;
    element.Dto.Model = new FormDateTimeModel();
    return element;
  }
}
