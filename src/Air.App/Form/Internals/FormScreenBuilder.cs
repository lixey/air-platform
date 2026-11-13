using Platform.App.Files.Internals;
using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form.Internals;

internal static class FormScreenBuilder
{
  public static async Task<ScreenDto> BuildScreenAsync<TEntity>(
    FormService<TEntity> form,
    string servicePath,
    FormRequest request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    form.SetRequest(request);
    var id = request.Id;
    form.IsNewRecord = id is null or 0;

    var parent = form.GetParentDeclaration();

    TEntity? entity = null;
    if (id is > 0)
    {
      entity = await form.ReachableQuery(asTracking: true).FirstOrDefaultAsync(e => e.Id == id.Value, ct);
      if (entity != null)
      {
        form.RestoreBoundParentId(entity, parent);
      }
    }

    form.Entity = entity;
    await form.PreloadParentRecordInternalAsync(parent, ct);

    var elements = new FormService<TEntity>.Elements();
    await form.InvokeSetElementsAsync(elements, ct);
    FormService<TEntity>.LockBoundParentElement(elements, parent, form.CurrentRequest.ParentId);

    if (form.IsNewRecord && request.Value is { ValueKind: JsonValueKind.Object } presetJson)
    {
      foreach (var prop in presetJson.EnumerateObject())
      {
        if (prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
          continue;
        }

        var targetName = ResolvePresetPropertyName<TEntity>(prop.Name);
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
      form.GetType(),
      form.Feature,
      form.DbContext,
      form.PlatformApp,
      form.User,
      form.L,
      form.IsFormReadOnly,
      elements,
      ct);

    await FormReferenceResolver.ResolveTimeZoneInheritLabelsAsync(form.DbContext, form.L, form.CurrentRequest.ParentId, elements, entity, ct);
    form.State.LastElements = elements;

    var formModel = new FormModel
    {
      Service = servicePath,
      Elements = elements.List.ToList(),
      Tabs = elements.Tabs.Count > 0
        ? elements.Tabs.Select(t => new FormTabDto
        {
          Key = t.Key,
          Label = form.L[t.Label],
          Title = t.Title != null ? form.L[t.Title] : null
        }).ToList()
        : null,
      Tables = elements.Tables.Count > 0 ? elements.Tables.ToList() : null,
      IsReadOnly = form.IsFormReadOnly,
      DirtyDialog = new OverlayDirtyModel
      {
        Title = form.L["FormDirtyTitle"],
        Description = form.L["FormDirtyDescription"],
        SaveLabel = form.L["Save"],
        DiscardLabel = form.L["FormDirtyDiscard"],
        ContinueLabel = form.L["FormDirtyContinue"]
      },
      MutationTexts = ScreenBuilderHelpers.BuildMutationTexts(form.L)
    };

    var caption = await form.GetRecordCaptionAsync(entity, id, ct);
    var filesChildren = elements.Children.Where(c => c.IsFiles && IsChildPermitted(form, c)).ToList();
    var pageChildren = elements.Children.Where(c => !c.IsFiles && IsChildPermitted(form, c)).ToList();
    var hasPages = pageChildren.Count > 0 && id is > 0;

    if (filesChildren.Count > 0 && id is > 0)
    {
      formModel.Files = await BuildFilesModelsAsync(form, servicePath, id, filesChildren, ct);
    }

    var nav = BuildFormNavigation(form, servicePath, caption, elements, hasPages, pageChildren);
    var actionStart = new List<NavElementModel>();
    var actionElements = new List<NavElementModel>();

    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      await ConfigureStatusFlowAndActionsAsync(form, servicePath, statusFlow, elements.StatusFlowAllowsReadOnly, entity, id, actionStart, ct);
    }

    if (entity != null && id is > 0)
    {
      var isParentReadOnly = (await form.ResolveParentChildBuilderAsync(ct: ct))?.IsReadOnly == true;
      var actions = new FormService<TEntity>.Actions();
      await form.InvokeSetActionsAsync(actions, ct);
      var permittedActions = new List<(NavElementModel Model, bool IsInMenu)>();
      foreach (var entry in actions.Entries)
      {
        if (entry.Permission.IsPermitted(form.Feature, form.User, entry.Handle, form.IsFormReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly))
        {
          var model = await entry.BuildModelAsync(form.GetType(), form.Feature, servicePath, entity, form.DbContext, form.PlatformApp, form.User, form.L, ct);
          permittedActions.Add((model, entry.IsInMenu));
        }
      }

      ScreenBuilderHelpers.AppendMenuAndInlineActions(actionElements, permittedActions, form.L);
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
      else if (form.DoesFillOverlay(elements))
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
      BuildChildPagePanels(form, layout, servicePath, id!.Value, pageChildren);
    }

    var (overlayWidth, overlayHeight) = form.GetOverlayAsk(elements);
    return new ScreenDto
    {
      Layout = layout.Build(),
      OverlayWidth = overlayWidth,
      OverlayHeight = overlayHeight
    };
  }

  public static async Task<Dictionary<string, int>?> EvaluateUnavailableAsync<TEntity>(
    FormService<TEntity> form,
    long? id,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    if (id is not > 0)
    {
      return null;
    }

    var idChanged = form.CurrentRequest.Id != id;
    if (idChanged)
    {
      form.CurrentRequest.Id = id;
    }

    var elements = await form.EnsureElementsAsync(ct, reload: idChanged);

    var offers = new List<(string ActionKey, int ReasonIndex, Expression<Func<TEntity, bool>> Condition)>();
    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      if (form.PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
      {
        flowSvc.AttachPlatformApp(form.PlatformApp);
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

    var actions = new FormService<TEntity>.Actions();
    await form.InvokeSetActionsAsync(actions, ct);
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

    var evaluated = await OfferBatchEvaluator.EvaluateAsync(form.DbContext, [id.Value], offers, ct);
    return evaluated.TryGetValue(id.Value, out var unav) && unav.Count > 0 ? unav : null;
  }

  public static string ResolvePresetPropertyName<TEntity>(string propName)
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

  private static async Task<List<Contracts.FormFilesModel>> BuildFilesModelsAsync<TEntity>(
    FormService<TEntity> form,
    string servicePath,
    long? id,
    List<ChildGridBuilder> filesChildren,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var filesModels = new List<Contracts.FormFilesModel>(filesChildren.Count);
    var parentId = id is > 0 ? id.Value : (long?)null;
    foreach (var child in filesChildren)
    {
      var key = !string.IsNullOrEmpty(child.DeclaredKey) ? child.DeclaredKey : "files";
      var label = !string.IsNullOrEmpty(child.DeclaredLabel) ? form.L[child.DeclaredLabel] : null;
      if (child.ServiceType != null &&
          ModuleCatalog.Instance.CreateServiceInstance(child.ServiceType, form.DbContext, form.PlatformApp) is IFormFilesSource fileSource)
      {
        var model = await fileSource.BuildFormFilesAsync(key, label, child.Tab, servicePath, parentId, child.Look, ct);
        if (form.IsFormReadOnly)
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

  private static NavModel BuildFormNavigation<TEntity>(
    FormService<TEntity> form,
    string servicePath,
    string caption,
    FormService<TEntity>.Elements elements,
    bool hasPages,
    List<ChildGridBuilder> pageChildren)
    where TEntity : class, IEntity, new()
  {
    var navStart = new List<NavElementModel>();
    var navElements = new List<NavElementModel>();

    if (!form.IsFormReadOnly)
    {
      navStart.Add(new FormNavSaveCloseModel
      {
        Key = "save-close",
        Label = form.L["SaveAndClose"],
        Icon = UiIcon.Save,
        Color = UiColor.Green,
        Variant = ButtonVariant.Soft
      });

      navElements.Add(new FormNavSaveModel
      {
        Key = "save",
        Label = form.L["Save"],
        Color = UiColor.Green,
        Variant = ButtonVariant.Soft
      });

      navElements.Add(new NavCloseModel
      {
        Key = "form-close",
        Label = form.L["Close"],
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
        Label = form.L["Close"],
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
          Label = form.L[tab.Label],
          Title = tab.Title != null ? form.L[tab.Title] : null,
          Color = UiColor.Sky,
          Variant = ButtonVariant.Soft,
          Path = "form"
        });
      }
    }

    if (!form.IsFormReadOnly && form.IsNewRecord && pageChildren.Count > 0)
    {
      foreach (var child in pageChildren)
      {
        var childLabel = !string.IsNullOrEmpty(child.DeclaredLabel) ? form.L[child.DeclaredLabel] : child.DeclaredKey;
        navElements.Add(new FormNavSaveOpenModel
        {
          Key = child.DeclaredKey,
          Label = childLabel,
          Title = child.DeclaredTitle != null ? form.L[child.DeclaredTitle] : null,
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
    if (BuildHistoryOverlay(form, servicePath) is { } historyOverlay)
    {
      navEnd.Add(historyOverlay);
    }

    navEnd.Add(new NavCloseModel
    {
      Key = "close",
      Icon = UiIcon.Close,
      Color = UiColor.Slate,
      Variant = ButtonVariant.Ghost,
      Title = form.L["Close"]
    });

    return new NavModel
    {
      Key = $"{servicePath}.nav",
      Start = navStart,
      Elements = navElements,
      End = navEnd
    };
  }

  private static NavOverlayModel? BuildHistoryOverlay<TEntity>(FormService<TEntity> form, string servicePath)
    where TEntity : class, IEntity, new()
  {
    if (form.IsNewRecord || form.CurrentRequest.Id is not > 0 || !form.Feature.HasHistory)
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

    var canSeeHistory = form.User is { IsAuthenticated: true } && (
      form.User.Can(form.Feature.History) ||
      form.User.Can(form.Feature.All) ||
      form.User.Can("*") ||
      ServiceEndpointsExtensions.CanView(changeFeature, form.User));
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
      Title = form.L["History"],
      Overlay = OverlayModel.Dialog($"{servicePath}.history")
        .Screen(changeServicePath, new { parentService = servicePath, parentId = form.CurrentRequest.Id.Value })
        .Width(1040)
        .Height(640)
        .Build()
    };
  }

  private static bool IsChildPermitted<TEntity>(FormService<TEntity> form, ChildGridBuilder child)
    where TEntity : class, IEntity, new()
  {
    if (child.ServiceType == null)
    {
      return false;
    }

    var childFeat = ModuleCatalog.Instance.FindFeature(child.ServiceType);
    return childFeat != null && ServiceEndpointsExtensions.CanView(childFeat, form.User);
  }

  private static async Task ConfigureStatusFlowAndActionsAsync<TEntity>(
    FormService<TEntity> form,
    string servicePath,
    IRecordStatuses<TEntity> statusFlow,
    bool allowReadOnly,
    TEntity? entity,
    long? id,
    List<NavElementModel> actionStart,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    if (form.PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
    {
      flowSvc.AttachPlatformApp(form.PlatformApp);
    }

    if (entity != null && id is > 0 && (!form.IsFormReadOnly || allowReadOnly))
    {
      var currentStatus = statusFlow.StatusMember.Compile()(entity);
      foreach (var transition in statusFlow.Transitions)
      {
        if (!StatusMoveNavBuilder.CanMove(form.Feature, form.User, transition.Name) || !transition.From.Contains(currentStatus))
        {
          continue;
        }

        var meta = await StatusMoveNavBuilder.BuildTransitionMetaAsync(
          form.GetType(),
          form.Feature,
          form.DbContext,
          form.PlatformApp,
          form.User,
          form.L,
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

  private static void BuildChildPagePanels<TEntity>(
    FormService<TEntity> form,
    PanelLayoutBuilder layout,
    string servicePath,
    long id,
    List<ChildGridBuilder> pageChildren)
    where TEntity : class, IEntity, new()
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
        .Label(form.L[child.DeclaredLabel])
        .Fill()
        .Min(240));
    }
  }
}
