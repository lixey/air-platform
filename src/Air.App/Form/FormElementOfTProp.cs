namespace Platform.App.Form;

public class FormElement<TProp>(FormElementDto dto) : FormElement<object, TProp>(dto)
{
  public new FormElement<TProp> Title(string title)
  {
    base.Title(title);
    return this;
  }

  public new FormElement<TProp> Title(string uiSource, string dbSource)
  {
    base.Title(uiSource, dbSource);
    return this;
  }
}
