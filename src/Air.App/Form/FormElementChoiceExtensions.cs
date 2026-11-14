using Platform.App.Form.Internals;

namespace Platform.App.Form;

public static class FormElementChoiceExtensions
{
  public static FormElement<TEntity, bool> AsCheckbox<TEntity>(this FormElement<TEntity, bool> element) =>
    ApplyCheckbox(element);

  public static FormElement<TEntity, bool?> AsCheckbox<TEntity>(this FormElement<TEntity, bool?> element) =>
    ApplyCheckbox(element);

  public static FormElement<TEntity, TList> AsRightsTree<TEntity, TList>(
    this FormElement<TEntity, TList> element,
    ModuleCatalog? catalog = null,
    IReadOnlySet<string>? held = null)
    where TList : IEnumerable<string>?
  {
    element.Dto.Type = UiFieldType.Text;
    element.Dto.ValueKind = FormValueKind.List;
    FormElementBinding.Of(element.Dto).IsRightsTree = held == null;
    if (!element.HasExplicitSpan)
    {
      element.Dto.Span = 12;
    }

    catalog ??= ModuleCatalog.Instance;
    var nodes = RightsTree.Build(catalog, held);
    element.Dto.Model = new FormCheckboxTreeModel { Nodes = nodes };
    element.TreeKeys = RightsTree.FlattenKeys(nodes);
    return element;
  }

  public static FormElement<TEntity, TList> AsCheckboxSet<TEntity, TList>(
    this FormElement<TEntity, TList> element,
    IEnumerable<OptionDto>? options = null)
    where TList : IEnumerable<long>?
  {
    element.Dto.Type = UiFieldType.Text;
    element.Dto.ValueKind = FormValueKind.List;
    if (!element.HasExplicitSpan)
    {
      element.Dto.Span = 12;
    }

    element.Dto.Model = new FormCheckboxSetModel { Options = options?.ToList() ?? [] };
    return element;
  }

  private static FormElement<TEntity, T> ApplyCheckbox<TEntity, T>(FormElement<TEntity, T> element)
  {
    element.Dto.Type = UiFieldType.Checkbox;
    element.Dto.Model = new FormCheckboxModel();
    return element;
  }
}
