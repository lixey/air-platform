using Platform.App.Files.Internals;
using Platform.App.Form.Internals;
using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
{
  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default) =>
    BuildScreenAsync(servicePath, new FormRequest { Id = id }, ct);

  public virtual async Task<ScreenDto> BuildScreenAsync(string servicePath, FormRequest request, CancellationToken ct = default)
  {
    SetRequest(request);
    var id = request.Id;
    IsNew = id is null or 0;

    var parent = GetParent();

    TEntity? entity = null;
    if (id is > 0)
    {
      entity = await LoadEntityWithIncludesAsync(id.Value, State.LastElements, asTracking: true, ct);
      if (entity != null)
      {
        RestoreBoundParentId(entity, parent);
      }
    }

    CurrentEntity = entity;
    await PreloadParentRecordAsync(parent, ct);

    var elements = await BuildElementsAsync(ct);
    (elements.StatusFlow as IPlatformService)?.BindContext(PlatformApp);
    LockBoundParentElement(elements, parent, Request.ParentId);

    HashSet<string>? presetDisabledPaths = null;
    if (IsNew && request.Value is { ValueKind: JsonValueKind.Object } presetJson)
    {
      foreach (var prop in presetJson.EnumerateObject())
      {
        if (prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
          continue;
        }

        var targetName = ResolvePresetPropertyName(prop.Name);
        var targetCamel = Form.Elements.ToCamelPath(targetName);
        presetDisabledPaths ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        presetDisabledPaths.Add(targetCamel);
      }
    }

    var scope = CreateResolveScope();
    ElementResolver.ConfigureStructuralElements<TEntity>(scope, elements);
    var inheritTimeZoneLabel = await ResolveTimeZoneInheritLabelAsync(elements, entity, ct);
    State.LastElements = elements;
    var projectedElements = await ElementResolver.ProjectWireElementsAsync(
      scope,
      elements.List,
      inheritTimeZoneLabel,
      presetDisabledPaths,
      includeCreateOverlays: true,
      ct);

    var formModel = new FormModel
    {
      Service = servicePath,
      Elements = projectedElements,
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

    var caption = await GetRecordCaptionAsync(entity, id, ct);
    var filesChildren = elements.Children.Where(c => c.IsFiles && IsChildPermitted(c)).ToList();
    var pageChildren = elements.Children.Where(c => !c.IsFiles && IsChildPermitted(c)).ToList();
    var hasPages = pageChildren.Count > 0 && id is > 0;

    if (filesChildren.Count > 0 && id is > 0)
    {
      formModel.Files = await BuildFilesModelsAsync(servicePath, id, filesChildren, ct);
    }

    var formNav = await BuildNavAsync(ct);
    var nav = BuildFormNavigation(servicePath, caption, elements, formNav, hasPages, pageChildren);
    var actionStart = new List<NavElementModel>();
    var actionElements = new List<NavElementModel>();

    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      await ConfigureStatusFlowAndActionsAsync(servicePath, statusFlow, elements.StatusFlowAllowsReadOnly, entity, id, actionStart, ct);
    }

    if (entity != null && id is > 0)
    {
      var isParentReadOnly = (await ResolveParentChildBuilderAsync(ct: ct))?.IsReadOnly == true;
      var actions = await EnsureActionsAsync(ct);
      var permittedActions = new List<(NavElementModel Model, bool IsInMenu)>();
      foreach (var entry in actions.Entries)
      {
        if (entry.Permission.IsPermitted(Feature, User, entry.Handle, IsReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly))
        {
          var model = await entry.BuildModelAsync(GetType(), Feature, servicePath, entity, db, PlatformApp, User, L, ct);
          permittedActions.Add((model, entry.IsInMenu));
        }
      }

      ScreenBuilderHelpers.AppendMenuAndInlineActions(actionElements, permittedActions, L);
    }

    var actionNav = new NavModel
    {
      Key = $"{servicePath}.actions",
      Start = actionStart,
      Elements = actionElements
    };

    foreach (var customize in formNav.Customizers)
    {
      customize(nav, actionNav);
    }

    var hasActions = actionNav.Start.Count > 0 ||
                     actionNav.Elements.Count > 0 ||
                     actionNav.End.Count > 0 ||
                     actionNav.More.Count > 0;

    var layout = PanelLayout.Column(servicePath)
      .Panel("bar", p => p.Model(nav).Auto().Pinned().Rigid());

    if (hasActions)
    {
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

  public virtual async Task<Dictionary<string, int>?> EvaluateUnavailableAsync(long? id, CancellationToken ct = default)
  {
    if (id is not > 0)
    {
      return null;
    }

    var idChanged = Request.Id != id;
    if (idChanged)
    {
      Request.Id = id;
    }

    var elements = await EnsureElementsAsync(ct, reload: idChanged);

    var offers = new List<(string ActionKey, int ReasonIndex, Expression<Func<TEntity, bool>> Condition)>();
    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      (statusFlow as IPlatformService)?.BindContext(PlatformApp);

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

    var actions = await EnsureActionsAsync(ct, reload: idChanged);
    foreach (var entry in actions.Entries)
    {
      for (var i = 0; i < entry.Offers.Count; i++)
      {
        if (entry.Offers[i].Condition is Expression<Func<TEntity, bool>> cond)
        {
          offers.Add((entry.Key, i, cond));
        }
      }
    }

    if (offers.Count == 0)
    {
      return null;
    }

    var inMemoryTarget = CurrentEntity?.Id == id.Value
      ? CurrentEntity
      : State.LastSavedEntity?.Id == id.Value
        ? State.LastSavedEntity
        : null;
    if (inMemoryTarget != null &&
        DirectScalarPredicateEvaluator.TryEvaluateAll(inMemoryTarget, offers, out var inMemoryUnavailable))
    {
      return inMemoryUnavailable is { Count: > 0 } ? inMemoryUnavailable : null;
    }

    var evaluated = await OfferBatchEvaluator.EvaluateAsync(db, [id.Value], offers, ct);
    return evaluated.TryGetValue(id.Value, out var unav) && unav.Count > 0 ? unav : null;
  }

  private static string ResolvePresetPropertyName(string propName)
  {
    var entityProp = PropertyAccessorCache.GetProperty(typeof(TEntity), propName);
    if ((entityProp == null || typeof(IEntity).IsAssignableFrom(entityProp.PropertyType)) &&
        !propName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
        PropertyAccessorCache.GetProperty(typeof(TEntity), propName + "Id") != null)
    {
      return propName + "Id";
    }

    return propName;
  }

  private async Task<List<FormFilesModel>> BuildFilesModelsAsync(
    string servicePath,
    long? id,
    List<ChildGridBuilder> filesChildren,
    CancellationToken ct)
  {
    var filesModels = new List<FormFilesModel>(filesChildren.Count);
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
    FormElements<TEntity> elements,
    FormNav formNav,
    bool hasPages,
    List<ChildGridBuilder> pageChildren)
  {
    var navStart = new List<NavElementModel>();
    var navElements = new List<NavElementModel>();

    if (!IsReadOnly)
    {
      if (formNav.ShowSaveAndClose)
      {
        navStart.Add(new FormNavSaveCloseModel
        {
          Key = "save-close",
          Label = L["SaveAndClose"],
          Icon = UiIcon.Save,
          Color = UiColor.Green,
          Variant = ButtonVariant.Soft
        });
      }

      if (formNav.ShowSave)
      {
        navElements.Add(new FormNavSaveModel
        {
          Key = "save",
          Label = L["Save"],
          Color = UiColor.Green,
          Variant = ButtonVariant.Soft
        });
      }

      if (formNav.ShowClose)
      {
        navElements.Add(new NavCloseModel
        {
          Key = "form-close",
          Label = L["Close"],
          Icon = UiIcon.Close,
          Color = UiColor.Slate,
          Variant = ButtonVariant.Soft
        });
      }
    }
    else if (formNav.ShowClose)
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

    if (formNav.Start.Count > 0)
    {
      navStart.AddRange(formNav.Start);
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
          Title = child.DeclaredTitle != null ? L[child.DeclaredTitle] : null,
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

    if (formNav.Elements.Count > 0)
    {
      navElements.AddRange(formNav.Elements);
    }

    var navEnd = new List<NavElementModel>();
    if (formNav.End.Count > 0)
    {
      navEnd.AddRange(formNav.End);
    }

    if (formNav.ShowHistory && BuildHistoryOverlay(servicePath) is { } historyOverlay)
    {
      navEnd.Add(historyOverlay);
    }

    if (formNav.ShowClose)
    {
      navEnd.Add(new NavCloseModel
      {
        Key = "close",
        Icon = UiIcon.Close,
        Color = UiColor.Slate,
        Variant = ButtonVariant.Ghost,
        Title = L["Close"]
      });
    }

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

    var canSeeHistory = User is { IsAuthenticated: true } && (
      User.Can(Feature.History) ||
      User.Can(Feature.All) ||
      User.Can("*") ||
      ServiceEndpointsExtensions.CanView(changeFeature, User));
    if (!canSeeHistory)
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

  private bool IsChildPermitted(ChildGridBuilder child)
  {
    if (child.ServiceType == null)
    {
      return false;
    }

    var childFeat = ModuleCatalog.Instance.FindFeature(child.ServiceType);
    return childFeat != null && ServiceEndpointsExtensions.CanView(childFeat, User);
  }

  private async Task ConfigureStatusFlowAndActionsAsync(
    string servicePath,
    IRecordStatuses<TEntity> statusFlow,
    bool allowReadOnly,
    TEntity? entity,
    long? id,
    List<NavElementModel> actionStart,
    CancellationToken ct)
  {
    (statusFlow as IPlatformService)?.BindContext(PlatformApp);

    if (entity != null && id is > 0)
    {
      var isServiceLocked = IsServiceLockedReadOnly;
      var isParentReadOnly = ResolveParentChildBuilder()?.IsReadOnly == true;
      var isReadOnlyEntity = typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity));
      var currentStatus = statusFlow.ReadStatus(entity);
      foreach (var transition in statusFlow.Transitions)
      {
        if (!StatusMoveNavBuilder.CanMoveOnScreen(
              Feature,
              User,
              transition,
              isServiceLocked,
              allowReadOnly,
              isParentReadOnly,
              isReadOnlyEntity) ||
            !transition.From.Contains(currentStatus))
        {
          continue;
        }

        var meta = await StatusMoveNavBuilder.BuildTransitionMetaAsync(
          GetType(),
          Feature,
          db,
          PlatformApp,
          User,
          L,
          statusFlow,
          transition,
          ct);
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

  private void BuildChildPagePanels(
    PanelLayoutBuilder layout,
    string servicePath,
    long id,
    List<ChildGridBuilder> pageChildren)
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
        .Min(240));
    }
  }
}
