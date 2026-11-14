using Platform.App.Form.Internals;
using Platform.App.Internals;
using Platform.Data.Internals;

namespace Platform.App.Form;

public sealed class FormElement<TEntity, TProp>(FormElementDto dto, bool hasSpan = false)
{
  internal bool HasExplicitSpan { get; private set; } = hasSpan;
  public FormElementDto Dto => dto;
  public IReadOnlySet<string> TreeKeys { get; internal set; } = new HashSet<string>();

  public FormElement<TEntity, TProp> Label(string label)
  {
    dto.HasExplicitLabel = true;
    dto.Label = label;
    return this;
  }

  public FormElement<TEntity, TProp> Title(string title)
  {
    dto.Title = title;
    return this;
  }

  public FormElement<TEntity, TProp> Title(string uiSource, string dbSource) =>
    Title(Feature.FormatSourceTitle(uiSource, dbSource));

  public FormElement<TEntity, TProp> Span(int cols)
  {
    HasExplicitSpan = true;
    dto.Span = cols;
    return this;
  }

  public FormElement<TEntity, TProp> Required(bool required = true)
  {
    dto.ExplicitRequired = required;
    dto.IsRequired = required;
    return this;
  }

  public FormElement<TEntity, TProp> Disabled(bool disabled = true)
  {
    dto.IsDisabled = disabled;
    return this;
  }

  public FormElement<TEntity, TProp> Hidden(bool hidden = true)
  {
    dto.IsHidden = hidden;
    return this;
  }

  public FormElement<TEntity, TProp> VisibleWhen(FilterNode condition)
  {
    dto.VisibleWhen = condition;
    return this;
  }

  public FormElement<TEntity, TProp> VisibleWhen(Expression<Func<TEntity, bool>> predicate)
  {
    dto.VisibleWhen = FilterNodeTranslator.Translate(predicate);
    return this;
  }

  public FormElement<TEntity, TProp> DisabledWhen(FilterNode condition)
  {
    dto.DisabledWhen = condition;
    return this;
  }

  public FormElement<TEntity, TProp> DisabledWhen(Expression<Func<TEntity, bool>> predicate)
  {
    dto.DisabledWhen = FilterNodeTranslator.Translate(predicate);
    return this;
  }

  public FormElement<TEntity, TProp> Focus(bool focus = true)
  {
    dto.IsFocused = focus;
    return this;
  }

  public FormElement<TEntity, TProp> Placeholder(string placeholder)
  {
    dto.Placeholder = placeholder;
    return this;
  }

  public FormElement<TEntity, TProp> Tab(string tab)
  {
    dto.Tab = tab;
    return this;
  }

  public FormElement<TEntity, TProp> Help(string help)
  {
    dto.Help = help;
    return this;
  }

  public FormElement<TEntity, TProp> InputOnly()
  {
    dto.IsInputOnly = true;
    return this;
  }

  public FormElement<TEntity, TProp> ServerOwned(bool serverOwned = true)
  {
    dto.IsServerOwned = serverOwned;
    dto.IsDisabled |= serverOwned;
    return this;
  }

  public FormElement<TEntity, TProp> SaveOnChange(bool save = true)
  {
    dto.SavesOnChange = save;
    return this;
  }

  public FormElement<TEntity, TProp> Unique(string? message = null)
  {
    dto.UniqueMessage = message ?? "FormValidationUnique";
    dto.UniqueWithin = [];
    return this;
  }

  public FormElement<TEntity, TProp> Unique(string? message, params Expression<Func<TEntity, object?>>[] withKeys)
  {
    dto.UniqueMessage = message ?? "FormValidationUnique";
    dto.UniqueWithin = withKeys.Select(k => GetMemberName(k.Body)).Where(n => n != null).Select(n => n!).ToList();
    return this;
  }

  public FormElement<TEntity, TProp> AsInfo()
  {
    dto.IsDisabled = true;
    dto.IsServerOwned = true;
    dto.Model = new FormInfoModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsSelect(IEnumerable<OptionDto> options)
  {
    StripDefaultIdLabel();
    dto.Type = UiFieldType.Text;
    dto.Model = new FormSelectModel
    {
      Options = options.Select(o => new OptionDto
      {
        Id = o.Id,
        Name = o.Name,
        Color = o.Color,
        Title = o.Title
      }).ToList()
    };
    return this;
  }

  public FormElement<TEntity, TProp> AsReference<TGridService>() =>
    ConfigureReference<TGridService>(null);

  public FormElement<TEntity, TProp> AsReference<TGridService>(
    Expression<Func<TProp, object?>> firstLabel,
    params Expression<Func<TProp, object?>>[] moreLabels) =>
    ConfigureReference<TGridService>(
      new[] { firstLabel }.Concat(moreLabels).Select(expr => Elements.ToCamelPath(MemberPath.Of(expr))));

  public FormElement<TEntity, TSource> AsReference<TGridService, TSource>(
    params Expression<Func<TSource, object?>>[] label)
  {
    ConfigureReference<TGridService>(
      label is { Length: > 0 }
        ? label.Select(expr => Elements.ToCamelPath(MemberPath.Of(expr)))
        : null);
    return new FormElement<TEntity, TSource>(dto, HasExplicitSpan);
  }

  public FormElement<TEntity, TProp> AsReferences<TGridService>()
  {
    ConfigureReferences<TGridService>();
    return this;
  }

  public FormElement<TEntity, TSource> AsReferences<TGridService, TSource>()
  {
    ConfigureReferences<TGridService>();
    return new FormElement<TEntity, TSource>(dto, HasExplicitSpan);
  }

  public FormElement<TEntity, TJoin> AsReferences<TGridService, TJoin>(
    Expression<Func<TJoin, long>> ownerKey,
    Expression<Func<TJoin, long>> referenceKey)
    where TJoin : class, IEntity, new()
  {
    var refEl = AsReferences<TGridService, TJoin>();
    FormJoinReferenceSyncer.Configure(dto, ownerKey, referenceKey);
    return refEl;
  }

  public FormElement<TEntity, TProp> PatchOnSelect(
    Expression<Func<TEntity, object?>> target,
    Expression<Func<TProp, object?>> source,
    bool onlyWhenEmpty = false) =>
    AddPatchOnSelect(MemberPath.Of(target), MemberPath.Of(source), onlyWhenEmpty);

  public FormElement<TEntity, TProp> PatchOnSelect<TSource>(
    Expression<Func<TEntity, object?>> target,
    Expression<Func<TSource, object?>> source,
    bool onlyWhenEmpty = false) =>
    AddPatchOnSelect(MemberPath.Of(target), MemberPath.Of(source), onlyWhenEmpty);

  public FormElement<TEntity, TProp> Where<TSourceVal, TDraftVal>(
    Expression<Func<TProp, TSourceVal>> sourceMember,
    Expression<Func<TEntity, TDraftVal>> draftMember) =>
    AddDraftFilterRule(MemberPath.Of(sourceMember), MemberPath.Of(draftMember));

  public FormElement<TEntity, TProp> Where<TSource, TSourceVal, TDraftVal>(
    Expression<Func<TSource, TSourceVal>> sourceMember,
    Expression<Func<TEntity, TDraftVal>> draftMember) =>
    AddDraftFilterRule(MemberPath.Of(sourceMember), MemberPath.Of(draftMember));

  public FormElement<TEntity, TProp> Where<TVal>(
    Expression<Func<TProp, TVal>> sourceMember,
    TVal value) =>
    AddConstantFilterRule(MemberPath.Of(sourceMember), value);

  public FormElement<TEntity, TProp> Where<TSource, TVal>(
    Expression<Func<TSource, TVal>> sourceMember,
    TVal value) =>
    AddConstantFilterRule(MemberPath.Of(sourceMember), value);

  public FormElement<TEntity, TProp> Where(Expression<Func<TProp, bool>> predicate) =>
    AddPredicateFilterRule(predicate);

  public FormElement<TEntity, TProp> Where<TSource>(Expression<Func<TSource, bool>> predicate) =>
    AddPredicateFilterRule(predicate);

  public FormElement<TEntity, TProp> AllowCreate(bool allow = true)
  {
    dto.AllowsCreate = allow;
    return this;
  }

  public FormElement<TEntity, TProp> AllowCreate<TFormService>() where TFormService : class
  {
    dto.AllowsCreate = true;
    dto.CreateFormServiceType = typeof(TFormService);
    return this;
  }

  private FormElement<TEntity, TProp> ConfigureReference<TGridService>(IEnumerable<string>? rawLabelPaths)
  {
    StripDefaultIdLabel();
    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.Reference;
    dto.ReferenceServiceType = typeof(TGridService);
    var effectivePropType = dto.ResolvedPropertyType ?? typeof(TProp);
    if (dto.ExplicitRequired == null &&
        effectivePropType.IsValueType &&
        Nullable.GetUnderlyingType(effectivePropType) == null &&
        !dto.IsDisabled &&
        !dto.IsServerOwned)
    {
      dto.IsRequired = true;
    }

    var labelPaths = rawLabelPaths?
      .Where(n => !string.IsNullOrEmpty(n))
      .ToList();
    if (labelPaths is null || labelPaths.Count == 0)
    {
      labelPaths = ["name"];
    }

    ReferenceLookupAllowlist.RegisterLabels(typeof(TGridService), labelPaths);
    dto.ReferencePath ??= dto.Path.EndsWith("Id", StringComparison.Ordinal) ? dto.Path[..^2] : dto.Path;
    dto.Model = new FormSelectModel
    {
      ReferenceService = ServicePathResolver.Resolve(typeof(TGridService)),
      ReferenceLabelPaths = labelPaths
    };
    return this;
  }

  private void ConfigureReferences<TGridService>()
  {
    StripDefaultIdLabel();
    if (!dto.HasExplicitLabel && dto.Label is { } lbl && lbl.EndsWith(" Ids", StringComparison.Ordinal) && lbl.Length > 4)
    {
      dto.Label = lbl[..^4] + "s";
    }

    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.List;
    dto.ReferenceServiceType = typeof(TGridService);
    ReferenceLookupAllowlist.RegisterLabels(typeof(TGridService), ["name"]);
    dto.ReferencePath ??= dto.Path.EndsWith("Ids", StringComparison.Ordinal)
      ? dto.Path[..^3] + "s"
      : (dto.Path.EndsWith("Id", StringComparison.Ordinal) ? dto.Path[..^2] : dto.Path + "Refs");
    dto.Model = new FormSelectModel
    {
      ReferenceService = ServicePathResolver.Resolve(typeof(TGridService)),
      ReferenceLabelPaths = ["name"],
      IsMultiple = true
    };
  }

  private FormElement<TEntity, TProp> AddPatchOnSelect(string targetPath, string sourcePath, bool onlyWhenEmpty)
  {
    if (!string.IsNullOrEmpty(targetPath) && !string.IsNullOrEmpty(sourcePath))
    {
      targetPath = ResolveEntityFkOrPath(targetPath);

      var camelSource = Elements.ToCamelPath(sourcePath);
      ReferenceLookupAllowlist.RegisterPatchColumn(dto.ReferenceServiceType, camelSource);
      dto.SelectPatches ??= [];
      dto.SelectPatches.Add(new SelectPatchDto
      {
        TargetField = Elements.ToCamelPath(targetPath),
        SourceColumn = camelSource,
        OnlyWhenEmpty = onlyWhenEmpty
      });
    }

    return this;
  }

  private FormElement<TEntity, TProp> AddDraftFilterRule(string sourceMemberPath, string draftMemberPath)
  {
    if (dto.Model is FormSelectModel selectModel &&
        !string.IsNullOrEmpty(sourceMemberPath) &&
        !string.IsNullOrEmpty(draftMemberPath))
    {
      draftMemberPath = ResolveEntityFkOrPath(draftMemberPath);

      var sourcePath = Elements.ToCamelPath(sourceMemberPath);
      var draftPath = Elements.ToCamelPath(draftMemberPath);
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

  private static string ResolveEntityFkOrPath(string memberPath)
  {
    if (!memberPath.EndsWith("Id", StringComparison.Ordinal) &&
        PropertyAccessorCache.GetProperty(typeof(TEntity), memberPath) is { } navProp &&
        typeof(IEntity).IsAssignableFrom(navProp.PropertyType))
    {
      var explicitFkName = navProp.GetCustomAttribute<ForeignKeyAttribute>()?.Name;
      if (PropertyAccessorCache.GetProperty(typeof(TEntity), explicitFkName ?? memberPath + "Id") is { } fkProp)
      {
        return fkProp.Name;
      }
    }

    return memberPath;
  }

  private FormElement<TEntity, TProp> AddConstantFilterRule<TVal>(string sourceMemberPath, TVal value)
  {
    if (dto.Model is FormSelectModel selectModel && !string.IsNullOrEmpty(sourceMemberPath))
    {
      var sourcePath = Elements.ToCamelPath(sourceMemberPath);
      ReferenceLookupAllowlist.RegisterFilterField(dto.ReferenceServiceType, sourcePath);
      var rule = FilterNode.Rule(sourcePath, FilterOperator.Equals, value);
      selectModel.ReferenceFilter = CombineFilter(selectModel.ReferenceFilter, rule);
    }

    return this;
  }

  private FormElement<TEntity, TProp> AddPredicateFilterRule(LambdaExpression predicate)
  {
    if (dto.Model is FormSelectModel selectModel)
    {
      var translated = FilterNodeTranslator.Translate(predicate);
      RegisterFilterNodeFields(dto.ReferenceServiceType, translated);
      selectModel.ReferenceFilter = CombineFilter(selectModel.ReferenceFilter, translated);
    }

    return this;
  }

  private static void RegisterFilterNodeFields(Type? referenceServiceType, FilterNode node)
  {
    if (node is FilterRule rule)
    {
      ReferenceLookupAllowlist.RegisterFilterField(referenceServiceType, rule.Field);
    }
    else if (node is FilterGroup group)
    {
      foreach (var child in group.Items)
      {
        RegisterFilterNodeFields(referenceServiceType, child);
      }
    }
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

  internal static string? GetMemberName(Expression expr)
  {
    if (expr is UnaryExpression ue)
    {
      expr = ue.Operand;
    }

    return expr is MemberExpression me ? me.Member.Name : null;
  }

  private void StripDefaultIdLabel()
  {
    if (!dto.HasExplicitLabel && dto.Label is { } lbl && lbl.EndsWith(" Id", StringComparison.Ordinal) && lbl.Length > 3)
    {
      dto.Label = lbl[..^3];
    }
  }
}
