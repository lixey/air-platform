using Platform.App.Internals;
using Platform.Data.Internals;

namespace Platform.App.Form;

public class FormReferenceElement<TEntity, TProp, TSource>(FormElementDto dto) : FormElement<TEntity, TProp>(dto)
{
  public FormReferenceElement<TEntity, TProp, TSource> PatchOnSelect(
    Expression<Func<TEntity, object?>> target,
    Expression<Func<TSource, object?>> source,
    bool onlyWhenEmpty = false) =>
    PatchOnSelect<TSource>(target, source, onlyWhenEmpty);

  public FormReferenceElement<TEntity, TProp, TSource> PatchOnSelect<TSourceRow>(
    Expression<Func<TEntity, object?>> target,
    Expression<Func<TSourceRow, object?>> source,
    bool onlyWhenEmpty = false)
  {
    var targetPath = GetMemberName(target.Body);
    var sourcePath = GetMemberName(source.Body);
    if (targetPath != null && sourcePath != null)
    {
      var camelSource = ToCamel(sourcePath);
      ReferenceLookupAllowlist.RegisterPatchColumn(dto.ReferenceServiceType, camelSource);
      dto.SelectPatches ??= [];
      dto.SelectPatches.Add(new SelectPatchDto
      {
        TargetField = ToCamel(targetPath),
        SourceColumn = camelSource,
        OnlyWhenEmpty = onlyWhenEmpty
      });
    }

    return this;
  }

  public FormReferenceElement<TEntity, TProp, TSource> Where<TSourceVal, TDraftVal>(
    Expression<Func<TSource, TSourceVal>> sourceMember,
    Expression<Func<TEntity, TDraftVal>> draftMember)
  {
    if (dto.Model is FormSelectModel selectModel)
    {
      var sourcePath = Elements.ToCamelPath(MemberPath.Of(sourceMember));
      var draftPath = Elements.ToCamelPath(MemberPath.Of(draftMember));
      ReferenceLookupAllowlist.RegisterFilterField(dto.ReferenceServiceType, sourcePath);
      selectModel.ReferenceDraftRules ??= [];
      selectModel.ReferenceDraftRules.Add(new ReferenceDraftRuleDto
      {
        SourceField = sourcePath,
        DraftField = draftPath
      });
    }

    return this;
  }

  public FormReferenceElement<TEntity, TProp, TSource> Where<TVal>(
    Expression<Func<TSource, TVal>> sourceMember,
    TVal value)
  {
    if (dto.Model is FormSelectModel selectModel)
    {
      var sourcePath = Elements.ToCamelPath(MemberPath.Of(sourceMember));
      ReferenceLookupAllowlist.RegisterFilterField(dto.ReferenceServiceType, sourcePath);
      var rule = FilterNode.Rule(sourcePath, FilterOperator.Equals, value);
      selectModel.ReferenceFilter = CombineFilter(selectModel.ReferenceFilter, rule);
    }

    return this;
  }

  private static FilterNode CombineFilter(FilterNode? existing, FilterNode addition)
  {
    if (existing is null)
    {
      return addition;
    }

    if (existing is FilterGroup { Logic: FilterLogic.And } group)
    {
      group.Items.Add(addition);
      return group;
    }

    return FilterNode.All(existing, addition);
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Required(bool required = true)
  {
    base.Required(required);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Disabled(bool disabled = true)
  {
    base.Disabled(disabled);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Span(int cols)
  {
    base.Span(cols);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Label(string label)
  {
    base.Label(label);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Title(string title)
  {
    base.Title(title);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Title(string legacyUi, string legacyDb)
  {
    base.Title(legacyUi, legacyDb);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Focus(bool focus = true)
  {
    base.Focus(focus);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> SaveOnChange(bool save = true)
  {
    base.SaveOnChange(save);
    return this;
  }

  public FormReferenceElement<TEntity, TProp, TSource> AllowCreate(bool allow = true)
  {
    Dto.AllowsCreate = allow;
    return this;
  }

  public FormReferenceElement<TEntity, TProp, TSource> AllowCreate<TFormService>() where TFormService : class
  {
    Dto.AllowsCreate = true;
    Dto.CreateFormServiceType = typeof(TFormService);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> VisibleWhen(FilterNode condition)
  {
    base.VisibleWhen(condition);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> VisibleWhen(Expression<Func<TEntity, bool>> predicate)
  {
    base.VisibleWhen(predicate);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> DisabledWhen(FilterNode condition)
  {
    base.DisabledWhen(condition);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> DisabledWhen(Expression<Func<TEntity, bool>> predicate)
  {
    base.DisabledWhen(predicate);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> RequiredWhen(FilterNode condition)
  {
    base.RequiredWhen(condition);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> RequiredWhen(Expression<Func<TEntity, bool>> predicate)
  {
    base.RequiredWhen(predicate);
    return this;
  }
}
