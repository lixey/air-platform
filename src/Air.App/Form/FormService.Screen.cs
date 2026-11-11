using Platform.App.Files.Internals;
using Platform.App.Form.Internals;
using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
  where TEntity : class, IEntity, new()
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
      entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id.Value, ct);
      if (entity != null)
      {
        RestoreBoundParentId(entity, parent);
      }
    }

    CurrentEntity = entity;
    await PreloadParentRecordAsync(parent, ct);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    LockBoundParentElement(elements, parent, Request.ParentId);

    if (IsNew && request.Value is { ValueKind: JsonValueKind.Object } presetJson)
    {
      foreach (var prop in presetJson.EnumerateObject())
      {
        if (prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
          continue;
        }

        var targetName = ResolvePresetPropertyName(prop.Name);
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

    await FormReferenceResolver.LocalizeAndResolveSelectElementsAsync<TEntity>(
      GetType(),
      Feature,
      db,
      PlatformApp,
      User,
      L,
      IsReadOnly,
      elements,
      ct);

    await FormReferenceResolver.ResolveTimeZoneInheritLabelsAsync(db, L, ParentId, elements, entity, ct);
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
    var filesChildren = elements.Children.Where(c => c.IsFiles && IsChildPermitted(c)).ToList();
    var pageChildren = elements.Children.Where(c => !c.IsFiles && IsChildPermitted(c)).ToList();
    var hasPages = pageChildren.Count > 0 && id is > 0;

    if (filesChildren.Count > 0 && id is > 0)
    {
      formModel.Files = await BuildFilesModelsAsync(servicePath, id, filesChildren, ct);
    }

    var nav = BuildFormNavigation(servicePath, caption, elements, hasPages, pageChildren);
    var actionStart = new List<NavElementModel>();
    var actionElements = new List<NavElementModel>();

    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      ConfigureStatusFlowAndActions(servicePath, statusFlow, elements.StatusFlowAllowsReadOnly, entity, id, actionStart);
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

  private void ConfigureStatusFlowAndActions(
    string servicePath,
    IRecordStatuses<TEntity> statusFlow,
    bool allowReadOnly,
    TEntity? entity,
    long? id,
    List<NavElementModel> actionStart)
  {
    if (PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
    {
      flowSvc.AttachPlatformApp(PlatformApp);
    }

    if (entity != null && id is > 0 && (!IsReadOnly || allowReadOnly))
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

    var idChanged = Request.Id != id;
    if (idChanged)
    {
      Request.Id = id;
    }

    var elements = await EnsureElementsAsync(ct, reload: idChanged);

    var offers = new List<(string ActionKey, int ReasonIndex, Expression<Func<TEntity, bool>> Condition)>();
    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      if (PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
      {
        flowSvc.AttachPlatformApp(PlatformApp);
      }

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
        .Min(240));
    }
  }
}
