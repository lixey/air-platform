namespace Platform.App.Form;

public static class FormElementTextExtensions
{
  public static FormElement<TEntity, TText> AsTextarea<TEntity, TText>(this FormElement<TEntity, TText> element)
    where TText : IEquatable<string>?
  {
    element.Dto.Model = new FormTextareaModel();
    if (!element.HasExplicitSpan)
    {
      element.Dto.Span = 12;
    }

    return element;
  }

  public static FormElement<TEntity, TText> AsPassword<TEntity, TText>(this FormElement<TEntity, TText> element)
    where TText : IEquatable<string>?
  {
    element.Dto.Type = FormFieldType.Password;
    element.Dto.IsSecret = true;
    element.Dto.Model = new FormInputModel();
    return element;
  }

  public static FormElement<TEntity, TText> AsEmail<TEntity, TText>(this FormElement<TEntity, TText> element)
    where TText : IEquatable<string>?
  {
    element.Dto.Type = FormFieldType.Email;
    element.Dto.Model = new FormInputModel();
    return element;
  }

  public static FormElement<TEntity, TText> AsUrl<TEntity, TText>(this FormElement<TEntity, TText> element)
    where TText : IEquatable<string>?
  {
    element.Dto.Type = FormFieldType.Url;
    element.Dto.Model = new FormInputModel();
    return element;
  }

  public static FormElement<TEntity, TText> AsPhone<TEntity, TText>(
    this FormElement<TEntity, TText> element,
    params string[] masks)
    where TText : IEquatable<string>?
  {
    element.Dto.Type = UiFieldType.Text;
    element.Dto.Model = new FormPhoneModel
    {
      Masks = masks.Length > 0 ? [.. masks] : ["+380 (##) ###-##-##", "+1 (###) ###-####"]
    };
    return element;
  }

  public static FormElement<TEntity, TText> AsTimeZone<TEntity, TText>(this FormElement<TEntity, TText> element)
    where TText : IEquatable<string>?
  {
    element.Dto.Type = UiFieldType.Text;
    element.Dto.Model = new FormTimeZoneModel
    {
      Zones = [.. TimeZones.Offered],
      Inherit = Localizer.Instance["TimeZoneByBrowser"]
    };
    return element;
  }

  public static FormElement<TEntity, TText> MaxLength<TEntity, TText>(
    this FormElement<TEntity, TText> element,
    int maxLength)
    where TText : IEquatable<string>?
  {
    element.Dto.Constraints.MaxLength = maxLength;
    element.Dto.Constraints.LengthUnit ??= FormLengthUnit.Utf16;
    return element;
  }

  public static FormElement<TEntity, TText> MinLength<TEntity, TText>(
    this FormElement<TEntity, TText> element,
    int minLength)
    where TText : IEquatable<string>?
  {
    element.Dto.Constraints.MinLength = minLength;
    element.Dto.Constraints.LengthUnit ??= FormLengthUnit.Utf16;
    return element;
  }
}
