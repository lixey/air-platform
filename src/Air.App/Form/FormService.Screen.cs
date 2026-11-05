using Platform.App.Files.Internals;
using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity, TInput>
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default) =>
    BuildScreenAsync(servicePath, new FormRequest { Id = id }, ct);

  public virtual async Task<ScreenDto> BuildScreenAsync(string servicePath, FormRequest request, CancellationToken ct = default)
  {
    Request = request;
    var id = request.Id;
    IsNew = id is null or 0;

    var parent = GetParent();

    TEntity? entity = null;
    if (id is not null && id > 0)
    {
      entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id.Value, ct);
      if (entity != null)
      {
        RestoreBoundParentId(entity, parent);
      }
    }

    await PreloadParentRecordAsync(parent, ct);

    CurrentEntity = entity;

    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    if (parent.BoundProperty != null && request.ParentId is > 0)
    {
      var boundCamel = Elements.ToCamelPath(parent.BoundProperty.Name);
      foreach (var el in elements.List)
      {
        if (string.Equals(el.Path, boundCamel, StringComparison.OrdinalIgnoreCase))
        {
          el.IsDisabled = true;
          el.IsServerOwned = true;
        }
      }
    }

    if (IsNew && request.Value is { ValueKind: JsonValueKind.Object } presetJson)
    {
      foreach (var prop in presetJson.EnumerateObject())
      {
        if (prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
          continue;
        }

        var targetName = prop.Name;
        var entityProp = PropertyAccessorCache.GetProperty(typeof(TEntity), targetName);
        if ((entityProp == null || typeof(IEntity).IsAssignableFrom(entityProp.PropertyType)) &&
            !targetName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
            PropertyAccessorCache.GetProperty(typeof(TEntity), targetName + "Id") != null)
        {
          targetName += "Id";
        }

        var targetCamel = Elements.ToCamelPath(targetName);
        foreach (var el in elements.List)
        {
          if (string.Equals(el.Path, targetCamel, StringComparison.OrdinalIgnoreCase))
          {
            el.IsDisabled = true;
          }
        }
      }
    }

    LocalizeAndResolveSelectElements(elements);
    await ResolveTimeZoneInheritLabelsAsync(elements, entity, ct);
    _lastElements = elements;

    var formModel = new FormModel
    {
      Service = servicePath,
      Elements = elements.List.ToList(),
      Tabs = elements.Tabs.Count > 0
        ? elements.Tabs.Select(t => new FormTabDto
        {
          Key = t.Key,
          Label = L[t.Label],
          Title = t.Title != null ? L[t.Title] : null
        }).ToList()
        : null,
      Tables = elements.Tables.Count > 0 ? elements.Tables.ToList() : null,
      IsReadOnly = IsReadOnly,
      DirtyDialog = new OverlayDirtyModel
      {
        Title = L["FormDirtyTitle"],
        Description = L["FormDirtyDescription"],
        SaveLabel = L["Save"],
        DiscardLabel = L["FormDirtyDiscard"],
        ContinueLabel = L["FormDirtyContinue"]
      },
      MutationTexts = ScreenBuilderHelpers.BuildMutationTexts(L)
    };

    var caption = RecordCaption(entity, id);
    var filesChildren = elements.Children.Where(c => c.IsFiles).ToList();
    var pageChildren = elements.Children.Where(c => !c.IsFiles).ToList();
    var hasPages = pageChildren.Count > 0 && id.HasValue && id.Value > 0;

    if (filesChildren.Count > 0 && id.HasValue && id.Value > 0)
    {
      formModel.Files = await BuildFilesModelsAsync(servicePath, id, filesChildren, ct);
    }

    var nav = BuildFormNavigation(servicePath, caption, elements, hasPages, pageChildren);
    var actionStart = new List<NavElementModel>();
    var actionElements = new List<NavElementModel>();

    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      ConfigureStatusFlowAndActions(servicePath, statusFlow, elements.StatusFlowAllowsReadOnly, formModel, entity, id, actionStart);
    }

    if (entity != null && id is > 0)
    {
      var customNav = new Nav(servicePath, L);
      await SetNavAsync(customNav, ct);
      var permittedActions = new List<(NavElementModel Model, bool IsInMenu)>();
      foreach (var entry in customNav.Actions)
      {
        if (ScreenBuilderHelpers.IsActionPermitted(entry.Method.Method, Feature, User, IsReadOnly, entry.IsAllowedInReadOnly))
        {
          if (entry.Ask != null)
          {
            entry.Model.Ask = ScreenBuilderHelpers.BuildActionAsk(entry.Model, entry.Ask, entry.DefaultProvider != null, L);
            entry.Model.Confirm = null;
          }

          permittedActions.Add((entry.Model, entry.IsInMenu));
        }
      }

      ScreenBuilderHelpers.AppendMenuAndInlineActions(actionElements, permittedActions, L);
    }

    var hasActions = actionStart.Count > 0 || actionElements.Count > 0;

    var layout = PanelLayout.Column(servicePath)
      .Panel("bar", p => p.Model(nav).Auto().Pinned().Rigid());

    if (hasActions)
    {
      var actionNav = new NavModel
      {
        Key = $"{servicePath}.actions",
        Start = actionStart,
        Elements = actionElements
      };
      layout.Panel("actions", p => p.Model(actionNav).Auto().Pinned().Rigid());
    }

    layout.Panel("form", p =>
    {
      p.Model(formModel);
      if (hasPages)
      {
        p.Auto().Pinned().PageUnder(768).PageUnderHeight(640).Resizable();
      }
      else if (FillsOverlay(elements))
      {
        p.Fill();
      }
      else
      {
        p.Auto();
      }
    });

    if (hasPages)
    {
      BuildChildPagePanels(layout, servicePath, id!.Value, pageChildren);
    }

    var (overlayWidth, overlayHeight) = OverlayAsk(elements);
    return new ScreenDto
    {
      Layout = layout.Build(),
      OverlayWidth = overlayWidth,
      OverlayHeight = overlayHeight
    };
  }

  private void LocalizeAndResolveSelectElements(Elements elements)
  {
    foreach (var child in elements.Children)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), child.ServiceType, $"AddChild '{child.DeclaredKey}'");
    }

    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      var statusMemberName = StatusMoveNavBuilder.ExtractStatusMemberName(statusFlow.StatusMember);

      var statusOptions = statusFlow.Statuses.Select(s => new OptionDto
      {
        Id = s.Id,
        Name = L[s.Name],
        Color = s.Color,
        Title = s.Title != null ? L[s.Title] : null
      }).ToList();

      foreach (var el in elements.List)
      {
        var isStatusField = statusMemberName != null
          ? string.Equals(el.Path, statusMemberName, StringComparison.OrdinalIgnoreCase)
          : el.Path.EndsWith("statusId", StringComparison.OrdinalIgnoreCase)
            || el.Path.Equals("status", StringComparison.OrdinalIgnoreCase);

        if (isStatusField)
        {
          el.IsDisabled = true;
          el.IsServerOwned = true;
          el.Type = UiFieldType.Text;
          el.Model = new FormSelectModel
          {
            Options = statusOptions
          };
        }
      }
    }

    ApplyElementConstraints(elements);

    foreach (var el in elements.List)
    {
      if (!string.IsNullOrEmpty(el.SeparatorLabel))
      {
        el.SeparatorLabel = L[el.SeparatorLabel];
      }

      if (!string.IsNullOrEmpty(el.Label))
      {
        el.Label = ScreenBuilderHelpers.ResolveFieldLabel(L, Feature, el.Path, el.Label);
      }

      if (!string.IsNullOrEmpty(el.Title))
      {
        el.Title = L[el.Title];
      }

      if (!string.IsNullOrEmpty(el.Placeholder))
      {
        el.Placeholder = L[el.Placeholder];
      }

      if (!string.IsNullOrEmpty(el.Help))
      {
        el.Help = L[el.Help];
      }

      if (el.Model is FormSelectModel select)
      {
        select.Options = ScreenBuilderHelpers.LocalizeOptions(L, select.Options);

        if (!string.IsNullOrEmpty(select.ReferenceService))
        {
          var targetServiceType = el.ReferenceServiceType ?? ModuleCatalog.Instance.FindServiceType(select.ReferenceService);
          ModuleCatalog.EnsureModuleServiceAccessAllowed(GetType(), targetServiceType, $"AsReference on '{el.Path}'");
          if (select.ReferenceLabelPaths == null || select.ReferenceLabelPaths.Count == 0 || select.ReferenceLabelPaths.Count == 1 && select.ReferenceLabelPaths[0] == "name")
          {
            if (targetServiceType != null && typeof(IGridService).IsAssignableFrom(targetServiceType))
            {
              try
              {
                if (ModuleCatalog.Instance.CreateServiceInstance(targetServiceType, db, PlatformApp) is IGridService gridInst)
                {
                  var customPaths = gridInst.GetReferenceLabelPaths();
                  if (customPaths is { Count: > 0 })
                  {
                    select.ReferenceLabelPaths = [.. customPaths];
                  }
                }
              }
              catch { }
            }
          }

          if (el.AllowsCreate && !el.IsDisabled && !el.IsServerOwned && !IsReadOnly)
          {
            var createFormType = el.CreateFormServiceType;
            if (createFormType == null && targetServiceType != null)
            {
              var candidateName = targetServiceType.Name.Replace("GridService", "FormService");
              createFormType = targetServiceType.Assembly.GetType(targetServiceType.Namespace + "." + candidateName)
                ?? targetServiceType.Assembly.GetTypes().FirstOrDefault(t => t.Name == candidateName);
            }

            if (createFormType != null && typeof(IFormService).IsAssignableFrom(createFormType))
            {
              var canCreate = true;
              var featType = targetServiceType ?? createFormType;
              if (Feature.FeaturesByServiceType.TryGetValue(featType, out var targetFeat))
              {
                if (targetFeat.IsReadOnly || User != null && User.IsAuthenticated && !User.Can(targetFeat.Edit) && !User.Can(targetFeat.All) && !User.Can("*"))
                {
                  canCreate = false;
                }
              }

              if (canCreate)
              {
                var createServicePath = ServicePathResolver.Resolve(createFormType);
                try
                {
                  if (ModuleCatalog.Instance.CreateServiceInstance(createFormType, db, PlatformApp) is IFormService createFormInst)
                  {
                    select.CreateOverlay = createFormInst.Overlay(createServicePath);
                  }
                }
                catch { }

                select.CreateOverlay ??= OverlayModel.Dialog($"{createServicePath}.modal").Screen(createServicePath).Build();
              }
            }
          }
        }
      }
      else if (el.IsRightsTree && el.Model is FormCheckboxTreeModel treeModel)
      {
        var held = User is { IsAuthenticated: true } ? User.Permissions : null;
        treeModel.Nodes = Platform.App.Auth.RightsTree.Build(ModuleCatalog.Instance, held);
      }
    }
  }

  internal void ApplyElementConstraints(Elements elements)
  {
    var efEntityType = db.Model.FindEntityType(typeof(TEntity));

    foreach (var el in elements.List)
    {
      if (string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var inputProp = PropertyAccessorCache.GetProperty(typeof(TInput), el.Path);
      var entityProp = !el.IsInputOnly ? PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path) : null;
      var propType = inputProp?.PropertyType ?? entityProp?.PropertyType;
      var efProp = efEntityType != null && entityProp != null ? efEntityType.FindProperty(entityProp.Name) : null;

      if (efProp != null)
      {
        el.Constraints.AllowsNull ??= efProp.IsNullable;
        if (efProp.GetMaxLength() is { } storageMax && storageMax >= 0)
        {
          el.Constraints.MaxLength = el.Constraints.MaxLength.HasValue
            ? Math.Min(el.Constraints.MaxLength.Value, storageMax)
            : storageMax;
          el.Constraints.LengthUnit ??= propType == typeof(byte[]) ? FormLengthUnit.Bytes : FormLengthUnit.Utf16;
        }

        if (propType != null && (Nullable.GetUnderlyingType(propType) ?? propType) == typeof(decimal))
        {
          el.Constraints.Precision ??= efProp.GetPrecision();
          el.Constraints.Scale ??= efProp.GetScale();
        }

        if (!efProp.IsNullable && el.ExplicitRequired != false && !el.IsServerOwned)
        {
          if (propType == typeof(string) || efProp.IsForeignKey())
          {
            el.IsRequired = true;
          }
        }
      }

      var annotations = new[] { inputProp, entityProp }
        .OfType<PropertyInfo>()
        .Distinct()
        .SelectMany(p => p.GetCustomAttributes<ValidationAttribute>());

      foreach (var attr in annotations)
      {
        switch (attr)
        {
          case RequiredAttribute req:
            if (el.ExplicitRequired != false && !el.IsServerOwned)
            {
              el.IsRequired = true;
              el.Constraints.AllowsNull = false;
              if (propType == typeof(string) && !req.AllowEmptyStrings)
              {
                el.Constraints.RejectsBlank = true;
              }
            }

            break;
          case StringLengthAttribute strLen:
            el.Constraints.MaxLength = el.Constraints.MaxLength.HasValue
              ? Math.Min(el.Constraints.MaxLength.Value, strLen.MaximumLength)
              : strLen.MaximumLength;
            if (strLen.MinimumLength > 0)
            {
              el.Constraints.MinLength = Math.Max(el.Constraints.MinLength ?? 0, strLen.MinimumLength);
            }

            el.Constraints.LengthUnit ??= FormLengthUnit.Utf16;
            break;
          case MaxLengthAttribute maxLen when maxLen.Length >= 0:
            el.Constraints.MaxLength = el.Constraints.MaxLength.HasValue
              ? Math.Min(el.Constraints.MaxLength.Value, maxLen.Length)
              : maxLen.Length;
            el.Constraints.LengthUnit ??= propType == typeof(byte[])
              ? FormLengthUnit.Bytes
              : (propType != null && typeof(ICollection).IsAssignableFrom(propType) ? FormLengthUnit.Items : FormLengthUnit.Utf16);
            break;
          case MinLengthAttribute minLen when minLen.Length > 0:
            el.Constraints.MinLength = Math.Max(el.Constraints.MinLength ?? 0, minLen.Length);
            el.Constraints.LengthUnit ??= propType == typeof(byte[])
              ? FormLengthUnit.Bytes
              : (propType != null && typeof(ICollection).IsAssignableFrom(propType) ? FormLengthUnit.Items : FormLengthUnit.Utf16);
            break;
        }
      }

      if (propType != null && propType.IsValueType && Nullable.GetUnderlyingType(propType) == null)
      {
        el.Constraints.AllowsNull = false;
      }

      if (el.IsRequired)
      {
        el.Constraints.AllowsNull = false;
        if (propType == typeof(string))
        {
          el.Constraints.RejectsBlank = true;
        }
        else if (propType != null && typeof(ICollection).IsAssignableFrom(propType))
        {
          el.Constraints.MinLength = Math.Max(el.Constraints.MinLength ?? 0, 1);
          el.Constraints.LengthUnit ??= FormLengthUnit.Items;
        }
      }
    }
  }

  private async Task<List<Contracts.FormFilesModel>> BuildFilesModelsAsync(
    string servicePath,
    long? id,
    List<ChildGridBuilder> filesChildren,
    CancellationToken ct)
  {
    var filesModels = new List<Contracts.FormFilesModel>(filesChildren.Count);
    var parentId = id is > 0 ? id.Value : (long?)null;
    foreach (var child in filesChildren)
    {
      var key = !string.IsNullOrEmpty(child.DeclaredKey) ? child.DeclaredKey : "files";
      var label = !string.IsNullOrEmpty(child.DeclaredLabel) ? L[child.DeclaredLabel] : null;
      if (child.ServiceType != null &&
          ModuleCatalog.Instance.CreateServiceInstance(child.ServiceType, db, PlatformApp) is IFormFilesSource fileSource)
      {
        var model = await fileSource.BuildFormFilesAsync(key, label, child.Tab, servicePath, parentId, child.Look, ct);
        if (IsReadOnly)
        {
          model.CanUpload = false;
          model.CanDelete = false;
          model.CanSetCover = false;
        }

        filesModels.Add(model);
      }
    }

    return filesModels;
  }

  private NavModel BuildFormNavigation(
    string servicePath,
    string caption,
    Elements elements,
    bool hasPages,
    List<ChildGridBuilder> pageChildren)
  {
    var navStart = new List<NavElementModel>();
    var navElements = new List<NavElementModel>();

    if (!IsReadOnly)
    {
      navStart.Add(new FormNavSaveCloseModel
      {
        Key = "save-close",
        Label = L["SaveAndClose"],
        Icon = UiIcon.Save,
        Color = UiColor.Green,
        Variant = ButtonVariant.Soft
      });

      navElements.Add(new FormNavSaveModel
      {
        Key = "save",
        Label = L["Save"],
        Color = UiColor.Green,
        Variant = ButtonVariant.Soft
      });

      navElements.Add(new NavCloseModel
      {
        Key = "form-close",
        Label = L["Close"],
        Icon = UiIcon.Close,
        Color = UiColor.Slate,
        Variant = ButtonVariant.Soft
      });
    }
    else
    {
      navStart.Add(new NavCloseModel
      {
        Key = "form-close",
        Label = L["Close"],
        Icon = UiIcon.Close,
        Color = UiColor.Slate,
        Variant = ButtonVariant.Soft
      });
    }

    if (elements.Tabs.Count == 0)
    {
      navElements.Add(new NavLabelModel
      {
        Key = "record",
        Label = caption,
        Color = UiColor.Sky,
        Path = "form"
      });
    }
    else
    {
      navElements.Add(new NavTabModel
      {
        Key = FormTab.Record,
        Group = FormTab.Group,
        Value = FormTab.Record,
        Label = caption,
        Color = UiColor.Sky,
        Variant = ButtonVariant.Soft,
        IsSelected = true,
        Path = "form"
      });

      foreach (var tab in elements.Tabs)
      {
        navElements.Add(new NavTabModel
        {
          Key = tab.Key,
          Group = FormTab.Group,
          Value = tab.Key,
          Label = L[tab.Label],
          Title = tab.Title != null ? L[tab.Title] : null,
          Color = UiColor.Sky,
          Variant = ButtonVariant.Soft,
          Path = "form"
        });
      }
    }

    if (!IsReadOnly && IsNew && pageChildren.Count > 0)
    {
      foreach (var child in pageChildren)
      {
        var childLabel = !string.IsNullOrEmpty(child.DeclaredLabel) ? L[child.DeclaredLabel] : child.DeclaredKey;
        navElements.Add(new FormNavSaveOpenModel
        {
          Key = child.DeclaredKey,
          Label = childLabel,
          Color = UiColor.Indigo,
          Variant = ButtonVariant.Soft,
          Path = child.DeclaredKey
        });
      }
    }

    if (hasPages)
    {
      navElements.Add(new NavTabsModel
      {
        Key = "children",
        Color = UiColor.Indigo,
        IsPagedOnly = true
      });
    }

    var navEnd = new List<NavElementModel>();
    if (BuildHistoryOverlay(servicePath) is { } historyOverlay)
    {
      navEnd.Add(historyOverlay);
    }

    navEnd.Add(new NavCloseModel
    {
      Key = "close",
      Icon = UiIcon.Close,
      Color = UiColor.Slate,
      Variant = ButtonVariant.Ghost,
      Title = L["Close"]
    });

    return new NavModel
    {
      Key = $"{servicePath}.nav",
      Start = navStart,
      Elements = navElements,
      End = navEnd
    };
  }

  private NavOverlayModel? BuildHistoryOverlay(string servicePath)
  {
    if (IsNew || Request.Id is not > 0 || !Feature.HasHistory)
    {
      return null;
    }

    if (Attribute.IsDefined(typeof(TEntity), typeof(NoAuditAttribute), inherit: true))
    {
      return null;
    }

    var changeFeature = ModuleCatalog.Instance.AllFeatures
      .FirstOrDefault(f => f.ServiceType != null && typeof(ChangeGridService).IsAssignableFrom(f.ServiceType));
    if (changeFeature?.ServiceType == null)
    {
      return null;
    }

    if (User is { IsAuthenticated: true } &&
        !User.Can(Feature.History) &&
        !User.Can(Feature.All) &&
        !User.Can(changeFeature.View) &&
        !User.Can(changeFeature.All) &&
        !User.Can("*"))
    {
      return null;
    }

    var changeServicePath = ServicePathResolver.Resolve(changeFeature.ServiceType);
    return new NavOverlayModel
    {
      Key = "history",
      Icon = UiIcon.Clock,
      Color = UiColor.Slate,
      Variant = ButtonVariant.Ghost,
      Title = L["History"],
      Overlay = OverlayModel.Dialog($"{servicePath}.history")
        .Screen(changeServicePath, new { parentService = servicePath, parentId = Request.Id.Value })
        .Width(1040)
        .Height(640)
        .Build()
    };
  }

  private void ConfigureStatusFlowAndActions(
    string servicePath,
    IRecordStatuses<TEntity> statusFlow,
    bool allowReadOnly,
    FormModel formModel,
    TEntity? entity,
    long? id,
    List<NavElementModel> actionStart)
  {
    if (entity != null && id.HasValue && id.Value > 0 && (!IsReadOnly || allowReadOnly))
    {
      var currentStatus = statusFlow.StatusMember.Compile()(entity);
      foreach (var transition in statusFlow.Transitions)
      {
        if (!StatusMoveNavBuilder.CanMove(Feature, User, transition.Name) || !transition.From.Contains(currentStatus))
        {
          continue;
        }

        var meta = StatusMoveNavBuilder.BuildTransitionMeta(Feature, L, statusFlow, transition);
        actionStart.Add(new FormNavActionModel
        {
          Key = meta.Key,
          Label = meta.Label,
          Title = meta.Title,
          Color = meta.Color,
          Service = servicePath,
          Method = meta.Method,
          Confirm = meta.Confirm,
          Ask = meta.Ask,
          UnavailableReasons = meta.UnavailableReasons
        });
      }
    }
  }

  public virtual async Task<Dictionary<string, int>?> EvaluateUnavailableAsync(long? id, CancellationToken ct = default)
  {
    if (id is not > 0)
    {
      return null;
    }

    var elements = await EnsureElementsAsync(ct);

    var offers = new List<(string ActionKey, int ReasonIndex, Expression<Func<TEntity, bool>> Condition)>();
    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      foreach (var transition in statusFlow.Transitions)
      {
        var actionKey = $"status.{transition.Name.ToLowerInvariant()}";
        for (var i = 0; i < transition.Offers.Count; i++)
        {
          if (transition.Offers[i].Condition is Expression<Func<TEntity, bool>> cond)
          {
            offers.Add((actionKey, i, cond));
          }
        }
      }
    }

    var customNav = new Nav(Service, L);
    await SetNavAsync(customNav, ct);
    foreach (var entry in customNav.Actions)
    {
      for (var i = 0; i < entry.Offers.Count; i++)
      {
        if (entry.Offers[i].Condition is Expression<Func<TEntity, bool>> cond)
        {
          offers.Add((entry.Model.Key, i, cond));
        }
      }
    }

    if (offers.Count == 0)
    {
      return null;
    }

    var evaluated = await OfferBatchEvaluator.EvaluateAsync(db, [id.Value], offers, ct);
    return evaluated.TryGetValue(id.Value, out var unav) && unav.Count > 0 ? unav : null;
  }

  private void BuildChildPagePanels(PanelLayoutBuilder layout, string servicePath, long id, List<ChildGridBuilder> pageChildren)
  {
    layout.Exclusive();
    var strip = new NavModel
    {
      Key = $"{servicePath}.tabs",
      Start = [new NavTabsModel { Key = "tabs", Color = UiColor.Indigo }]
    };

    foreach (var child in pageChildren)
    {
      layout.Panel(child.DeclaredKey, p => p
        .Model(new FormChildScreenModel
        {
          Service = child.ServicePath,
          ParentService = servicePath,
          ParentId = id,
          Tabs = strip
        })
        .Label(L[child.DeclaredLabel])
        .Fill()
        .Min(240)
      );
    }
  }
}
