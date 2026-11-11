using Platform.App.Internals;
using Platform.Data.Internals;

namespace Platform.App.Form;

public sealed class FormElement<TEntity, TProp>(FormElementDto dto, bool hasSpan = false)
{
  private bool _hasSpan = hasSpan;
  public FormElementDto Dto => dto;

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
    _hasSpan = true;
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

  public FormElement<TEntity, TProp> Range(double min, double max)
  {
    dto.Constraints.Min = JsonSerializer.SerializeToElement(min);
    dto.Constraints.ExclusiveMin = null;
    dto.Constraints.Max = JsonSerializer.SerializeToElement(max);
    return this;
  }

  public FormElement<TEntity, TProp> Min(decimal min)
  {
    dto.Constraints.Min = JsonSerializer.SerializeToElement(min);
    dto.Constraints.ExclusiveMin = null;
    return this;
  }

  public FormElement<TEntity, TProp> Max(decimal max)
  {
    dto.Constraints.Max = JsonSerializer.SerializeToElement(max);
    return this;
  }

  public FormElement<TEntity, TProp> AsPercent()
  {
    dto.Model = new FormPercentModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsMoney(string? currency = null)
  {
    dto.Type = UiFieldType.Number;
    dto.Model = new FormMoneyModel { Currency = currency };
    return this;
  }

  public FormElement<TEntity, TProp> AsTextarea()
  {
    dto.Model = new FormTextareaModel();
    if (!_hasSpan)
    {
      dto.Span = 12;
    }

    return this;
  }

  public FormElement<TEntity, TProp> AsCheckbox()
  {
    dto.Type = UiFieldType.Checkbox;
    dto.Model = new FormCheckboxModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsDate()
  {
    dto.Type = UiFieldType.Date;
    dto.Model = new FormDateModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsDatetime()
  {
    dto.Type = UiFieldType.Datetime;
    dto.Model = new FormDatetimeModel();
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
        Name = Localizer.Instance[o.Name],
        Color = o.Color,
        Title = o.Title != null ? Localizer.Instance[o.Title] : null
      }).ToList()
    };
    return this;
  }

  public FormElement<TEntity, TProp> AsPassword()
  {
    dto.Type = FormFieldType.Password;
    dto.IsSecret = true;
    dto.Model = new FormInputModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsEmail()
  {
    dto.Type = FormFieldType.Email;
    dto.Model = new FormInputModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsTimeZone()
  {
    dto.Type = UiFieldType.Text;
    dto.Model = new FormTimeZoneModel
    {
      Zones = [.. TimeZones.Offered],
      Inherit = Localizer.Instance["TimeZoneByBrowser"]
    };
    return this;
  }

  public IReadOnlySet<string> TreeKeys { get; private set; } = new HashSet<string>();

  public FormElement<TEntity, TProp> AsRightsTree(ModuleCatalog? catalog = null, IReadOnlySet<string>? held = null)
  {
    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.List;
    dto.IsRightsTree = held == null;
    if (!_hasSpan)
    {
      dto.Span = 12;
    }

    catalog ??= ModuleCatalog.Instance;
    var nodes = RightsTree.Build(catalog, held);
    dto.Model = new FormCheckboxTreeModel { Nodes = nodes };
    TreeKeys = RightsTree.FlattenKeys(nodes);
    return this;
  }

  public FormElement<TEntity, TProp> AsInfo()
  {
    dto.IsDisabled = true;
    dto.IsServerOwned = true;
    dto.Model = new FormInfoModel();
    return this;
  }

  public FormElement<TEntity, TProp> Help(string help)
  {
    dto.Help = help;
    return this;
  }

  public FormElement<TEntity, TProp> Grouped(bool isGrouped = true)
  {
    if (dto.Model is FormNumberModel num)
    {
      num.IsGrouped = isGrouped;
    }
    else if (dto.Model is FormInfoModel info)
    {
      info.IsGrouped = isGrouped;
    }

    return this;
  }

  public FormElement<TEntity, TProp> AsUrl()
  {
    dto.Type = FormFieldType.Url;
    dto.Model = new FormInputModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsPhone(params string[] masks)
  {
    dto.Type = UiFieldType.Text;
    dto.Model = new FormPhoneModel
    {
      Masks = masks.Length > 0 ? [.. masks] : ["+380 (##) ###-##-##", "+1 (###) ###-####"]
    };
    return this;
  }

  public FormElement<TEntity, TProp> InputOnly()
  {
    dto.IsInputOnly = true;
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

  public FormElement<TEntity, TProp> AsCheckboxSet(IEnumerable<OptionDto>? options = null)
  {
    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.List;
    if (!_hasSpan)
    {
      dto.Span = 12;
    }

    dto.Model = new FormCheckboxSetModel { Options = options?.ToList() ?? [] };
    return this;
  }

  public FormElement<TEntity, TProp> ServerOwned(bool serverOwned = true)
  {
    dto.IsServerOwned = serverOwned;
    dto.IsDisabled |= serverOwned;
    return this;
  }

  public FormElement<TEntity, TProp> Stepper(bool hasStepper = true)
  {
    if (dto.Model is FormNumberModel numModel)
    {
      numModel.HasStepper = hasStepper;
    }

    return this;
  }

  public FormElement<TEntity, TProp> Step(double step)
  {
    if (dto.Model is FormNumberModel numModel)
    {
      numModel.Step = step;
    }

    return this;
  }

  public FormElement<TEntity, TProp> SaveOnChange(bool save = true)
  {
    dto.SavesOnChange = save;
    return this;
  }

  public FormElement<TEntity, TProp> MaxLength(int maxLength)
  {
    dto.Constraints.MaxLength = maxLength;
    dto.Constraints.LengthUnit ??= FormLengthUnit.Utf16;
    return this;
  }

  public FormElement<TEntity, TProp> MinLength(int minLength)
  {
    dto.Constraints.MinLength = minLength;
    dto.Constraints.LengthUnit ??= FormLengthUnit.Utf16;
    return this;
  }

  public FormElement<TEntity, TProp> GreaterThan(decimal value)
  {
    dto.Constraints.Min = JsonSerializer.SerializeToElement(value);
    dto.Constraints.ExclusiveMin = true;
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
    return new FormElement<TEntity, TSource>(dto, _hasSpan);
  }

  public FormElement<TEntity, TProp> AsReferences<TGridService>()
  {
    ConfigureReferences<TGridService>();
    return this;
  }

  public FormElement<TEntity, TSource> AsReferences<TGridService, TSource>()
  {
    ConfigureReferences<TGridService>();
    return new FormElement<TEntity, TSource>(dto, _hasSpan);
  }

  public FormElement<TEntity, TJoin> AsReferences<TGridService, TJoin>(
    Expression<Func<TJoin, long>> ownerKey,
    Expression<Func<TJoin, long>> referenceKey)
    where TJoin : class, IEntity, new()
  {
    var refEl = AsReferences<TGridService, TJoin>();
    var ownerProp = (ownerKey.Body is UnaryExpression u1 ? u1.Operand : ownerKey.Body) is MemberExpression m1
      ? m1.Member as PropertyInfo
      : null;
    var refProp = (referenceKey.Body is UnaryExpression u2 ? u2.Operand : referenceKey.Body) is MemberExpression m2
      ? m2.Member as PropertyInfo
      : null;
    var getRefId = referenceKey.Compile();

    dto.JoinLoader = async (db, ownerId, ct) =>
    {
      var param = ownerKey.Parameters[0];
      var ownerPred = Expression.Lambda<Func<TJoin, bool>>(
        Expression.Equal(ownerKey.Body, Expression.Constant(ownerId)),
        param);
      return await db.Set<TJoin>()
        .AsNoTracking()
        .Where(ownerPred)
        .OrderBy(x => x.Id)
        .Select(referenceKey)
        .ToListAsync(ct);
    };

    dto.JoinSyncer = async (db, ownerId, desiredIds, ct) =>
    {
      var distinctDesired = desiredIds.Where(id => id > 0).Distinct().ToList();
      var param = ownerKey.Parameters[0];
      var ownerPred = Expression.Lambda<Func<TJoin, bool>>(
        Expression.Equal(ownerKey.Body, Expression.Constant(ownerId)),
        param);
      var existingRows = await db.Set<TJoin>().Where(ownerPred).ToListAsync(ct);
      var toRemove = existingRows.Where(r => !distinctDesired.Contains(getRefId(r))).ToList();
      if (toRemove.Count > 0)
      {
        db.Set<TJoin>().RemoveRange(toRemove);
      }

      var existingRefIds = existingRows.Select(getRefId).ToHashSet();
      var added = false;
      foreach (var refId in distinctDesired.Where(id => !existingRefIds.Contains(id)))
      {
        var joinEntity = new TJoin();
        ownerProp?.SetValue(joinEntity, ownerId);
        refProp?.SetValue(joinEntity, refId);
        if (joinEntity is IHasTenant tenantJoin && tenantJoin.TenantId == 0 && db is PlatformDbContext pDb && pDb.TenantContext.TenantId is { } tid)
        {
          tenantJoin.TenantId = tid;
        }

        db.Set<TJoin>().Add(joinEntity);
        added = true;
      }

      return toRemove.Count > 0 || added;
    };

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
      var explicitFkName = navProp.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.ForeignKeyAttribute>()?.Name;
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
