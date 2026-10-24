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

    var parent = new Parent();
    SetParent(parent);

    TEntity? entity = null;
    if (id is not null && id > 0)
    {
      entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id.Value, ct);
      if (entity != null)
      {
        RestoreBoundParentId(entity, parent);
      }
    }

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
      IsReadOnly = IsReadOnly,
      DirtyDialog = new OverlayDirtyModel
      {
        Title = L["FormDirtyTitle"],
        Description = L["FormDirtyDescription"],
        SaveLabel = L["Save"],
        DiscardLabel = L["FormDirtyDiscard"],
        ContinueLabel = L["FormDirtyContinue"]
      },
      MutationTexts = new MutationTextsDto
      {
        ConflictTitle = L["MutationConflict"],
        ReloadLabel = L["MutationReload"],
        UnknownTitle = L["MutationUnknown"],
        UnknownDescription = L["MutationUnknownDescription"],
        VerifyLabel = L["MutationVerify"],
        CommittedWarningTitle = L["MutationCommittedWarning"],
        SelectionCorrectionLabel = L["MutationSelectionCorrection"]
      }
    };

    var caption = RecordCaption(entity, id);
    var filesChildren = elements.Children.Where(c => c.IsFiles).ToList();
    var pageChildren = elements.Children.Where(c => !c.IsFiles).ToList();
    var hasPages = pageChildren.Count > 0 && id.HasValue && id.Value > 0;

    if (filesChildren.Count > 0 && id.HasValue && id.Value > 0)
    {
      formModel.Files = BuildFilesModels(servicePath, id, filesChildren);
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
      var isUserReadOnly = User != null && User.IsAuthenticated && !User.Can(Feature.Edit) && !User.Can(Feature.All);
      var permittedActions = new List<FormNavActionEntry>();
      foreach (var entry in customNav.Actions)
      {
        var methodInfo = entry.Method.Method;
        var reqAttr = methodInfo.GetCustomAttribute<RequireRightAttribute>(inherit: true);
        var reqAction = methodInfo.GetCustomAttribute<RequireActionAttribute>(inherit: true);
        var hasRight = true;
        if (reqAttr != null && User != null && User.IsAuthenticated)
        {
          if (Feature.FeaturesByServiceType.TryGetValue(reqAttr.Service, out var targetFeat))
          {
            hasRight = User.Can(targetFeat.Action(reqAttr.ActionName)) || User.Can(targetFeat.All) || User.Can(Feature.All);
          }
        }
        else if (reqAction != null && User != null && User.IsAuthenticated)
        {
          hasRight = User.Can(Feature.Action(reqAction.ActionName)) || User.Can(Feature.All);
        }
        else if (!entry.IsAllowedInReadOnly && (IsReadOnly || isUserReadOnly))
        {
          hasRight = false;
        }

        if (hasRight)
        {
          if (entry.Ask != null)
          {
            entry.Model.Ask = new FormAskModel
            {
              Title = entry.Model.Label,
              Message = entry.Model.Confirm?.Message,
              Color = entry.Model.Color,
              HasDefaults = entry.DefaultProvider != null,
              Elements = [.. entry.Ask.BuildElements().Select(e =>
              {
                if (!string.IsNullOrEmpty(e.Label))
                {
                  e.Label = L[e.Label];
                }

                return e;
              })]
            };
            entry.Model.Confirm = null;
          }

          permittedActions.Add(entry);
        }
      }

      var inMenuActions = permittedActions.Where(a => a.IsInMenu).Select(a => (NavElementModel)a.Model).ToList();
      if (inMenuActions.Count > 0)
      {
        actionElements.Add(new NavMenuModel
        {
          Key = "actions",
          Label = L["Actions"],
          Variant = ButtonVariant.Soft,
          Elements = inMenuActions
        });
      }

      foreach (var entry in permittedActions.Where(a => !a.IsInMenu))
      {
        actionElements.Add(entry.Model);
      }
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
    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      string? statusMemberName = null;
      if (statusFlow.StatusMember.Body is MemberExpression memberExpr)
      {
        statusMemberName = memberExpr.Member.Name;
      }
      else if (statusFlow.StatusMember.Body is UnaryExpression { Operand: MemberExpression ueMember })
      {
        statusMemberName = ueMember.Member.Name;
      }

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
        el.Label = !string.IsNullOrEmpty(el.Path)
          ? L.TextOr($"{Feature.CleanName}.{el.Path}",
              L.TextOr($"{Feature.Label}.{el.Path}",
                L.TextOr($"{Feature.Name}.{el.Path}", L[el.Label])))
          : L[el.Label];
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
        if (select.Options is { Count: > 0 })
        {
          select.Options = select.Options
            .Select(opt => string.IsNullOrEmpty(opt.Name)
              ? opt
              : new OptionDto
              {
                Id = opt.Id,
                Name = L[opt.Name],
                Color = opt.Color,
                Title = opt.Title != null ? L[opt.Title] : null
              })
            .ToList();
        }

        if (!string.IsNullOrEmpty(select.ReferenceService))
        {
          var targetServiceType = el.ReferenceServiceType ?? ModuleCatalog.Instance.FindServiceType(select.ReferenceService);
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

  private async Task ResolveTimeZoneInheritLabelsAsync(Elements elements, TEntity? entity, CancellationToken ct)
  {
    var tzModels = elements.List.Select(e => e.Model).OfType<FormTimeZoneModel>().ToList();
    if (tzModels.Count == 0)
    {
      return;
    }

    var isTenantForm = typeof(BaseAppTenant).IsAssignableFrom(typeof(TEntity));
    var startTenantId = isTenantForm
      ? entity is BaseAppTenant tenantEntity ? tenantEntity.ParentId : ParentId
      : entity is IHasTenant hasTenant && hasTenant.TenantId > 0
        ? hasTenant.TenantId
        : db.TenantContext.TenantId;
    string? resolvedZone = null;
    if (startTenantId is > 0)
    {
      var tenantClrType = db.Model.GetEntityTypes()
        .Select(t => t.ClrType)
        .FirstOrDefault(t => typeof(BaseAppTenant).IsAssignableFrom(t) && !t.IsAbstract);

      if (tenantClrType != null)
      {
        var setMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(tenantClrType);
        if (setMethod.Invoke(db, null) is IQueryable<BaseAppTenant> tenantQuery)
        {
          var currentId = startTenantId;
          var visited = new HashSet<long>();
          while (currentId is > 0 && visited.Add(currentId.Value))
          {
            var row = await tenantQuery.IgnoreQueryFilters()
              .AsNoTracking()
              .Where(t => t.Id == currentId.Value)
              .Select(t => new { t.ParentId, t.TimeZone })
              .FirstOrDefaultAsync(ct);
            if (row == null)
            {
              break;
            }

            if (!string.IsNullOrWhiteSpace(row.TimeZone))
            {
              resolvedZone = row.TimeZone;
              break;
            }

            currentId = row.ParentId;
          }
        }
      }
    }

    var inheritLabel = !string.IsNullOrWhiteSpace(resolvedZone)
      ? (isTenantForm
          ? L["TimeZoneAsParentOrganisation", resolvedZone]
          : L["TimeZoneAsOrganisation", resolvedZone])
      : L["TimeZoneByBrowser"];

    foreach (var tz in tzModels)
    {
      tz.Inherit = inheritLabel;
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

  private List<Contracts.FormFilesModel> BuildFilesModels(string servicePath, long? id, List<ChildGridBuilder> filesChildren)
  {
    var filesModels = new List<Contracts.FormFilesModel>(filesChildren.Count);
    foreach (var child in filesChildren)
    {
      FileRulesAttribute? rules = null;
      var hasCoverFile = false;
      if (child.ServiceType != null)
      {
        var cur = child.ServiceType;
        while (cur != null && cur != typeof(object))
        {
          if (cur.IsGenericType && cur.Name.StartsWith("FileGridService"))
          {
            var fileEntity = cur.GetGenericArguments()[0];
            rules = FileRulesAttribute.TryGet(fileEntity);
            hasCoverFile = typeof(IHasCoverFile).IsAssignableFrom(fileEntity);
            break;
          }

          cur = cur.BaseType;
        }
      }

      var limits = rules != null
        ? new FileUploadLimitsDto
        {
          Accept = [.. FileTypeTable.TypesOf(rules.ContentTypes)],
          Extensions = [.. FileTypeTable.ExtensionsOf(rules.ContentTypes)],
          MaxBytes = rules.MaxBytes,
          SourceMaxBytes = rules.SourceMaxBytes > 0 ? rules.SourceMaxBytes : rules.MaxBytes,
          MaxFiles = rules.MaxFiles,
          MaxTotalBytes = rules.MaxTotalBytes,
          Images = rules.Images,
          ImageMaxSide = rules.ImageMaxSide,
          ImageQuality = rules.ImageQuality,
          MaxPixels = FileTypeTable.MaxPixels,
          ThumbnailMaxBytes = FileTypeTable.ThumbnailMaxBytes
        }
        : new FileUploadLimitsDto();

      var childFeat = child.ServiceType != null
        ? ModuleCatalog.Instance.FindFeature(child.ServicePath)
        : null;
      var canEditFiles = !IsReadOnly && (User == null || !User.IsAuthenticated || User.Can(Feature.Edit) || User.Can(Feature.All));
      var canReadMeta = childFeat != null && childFeat.Rights.Any(r => r.Key.EndsWith(".FileMetadata", StringComparison.OrdinalIgnoreCase))
        && (User == null || !User.IsAuthenticated || User.Can(childFeat.Action("FileMetadata")) || User.Can(childFeat.All));

      filesModels.Add(new Contracts.FormFilesModel
      {
        Key = !string.IsNullOrEmpty(child.DeclaredKey) ? child.DeclaredKey : "files",
        Label = !string.IsNullOrEmpty(child.DeclaredLabel) ? L[child.DeclaredLabel] : null,
        Tab = child.Tab,
        Service = child.ServicePath,
        ParentService = servicePath,
        ParentId = id.HasValue && id.Value > 0 ? id.Value : null,
        Look = child.Look,
        Limits = limits,
        CanUpload = canEditFiles,
        CanDelete = canEditFiles,
        CanSetCover = hasCoverFile && canEditFiles,
        CanReadMetadata = canReadMeta
      });
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

    if (typeof(AppPlatformChange).IsAssignableFrom(typeof(TEntity)) ||
        typeof(BaseAppAuthSession).IsAssignableFrom(typeof(TEntity)) ||
        Attribute.IsDefined(typeof(TEntity), typeof(NoAuditAttribute), inherit: true))
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
        if (User != null && User.IsAuthenticated && !User.Can(Feature.Action(transition.Name)) && !User.Can(Feature.Edit) && !User.Can(Feature.All))
        {
          continue;
        }

        if (transition.From.Contains(currentStatus))
        {
          var targetStatus = statusFlow.Statuses.FirstOrDefault(s => s.Id == transition.To);
          var verbLabel = !string.IsNullOrEmpty(Feature.Module)
            ? L.TextOr($"{Feature.Module}: {transition.Name}", L[transition.Name])
            : L[transition.Name];
          var targetLabel = targetStatus != null ? L[targetStatus.Name] : verbLabel;
          var confirmMessage = transition.Confirm != null ? L[transition.Confirm] : null;
          var action = new FormNavActionModel
          {
            Key = $"status.{transition.Name.ToLowerInvariant()}",
            Label = verbLabel != transition.Name ? verbLabel : targetLabel,
            Title = targetStatus?.Title != null ? L[targetStatus.Title] : null,
            Color = targetStatus?.Color,
            Service = servicePath,
            Method = $"move-status/{transition.Name.ToLowerInvariant()}",
            Confirm = transition.Ask is not null || confirmMessage is null
              ? null
              : new OverlayConfirmModel { Message = confirmMessage, Color = targetStatus?.Color },
            Ask = transition.Ask is null
              ? null
              : new FormAskModel
              {
                Title = verbLabel != transition.Name ? verbLabel : targetLabel,
                Message = confirmMessage,
                Color = targetStatus?.Color,
                Elements = transition.Ask.BuildElements().Select(e =>
                {
                  if (!string.IsNullOrEmpty(e.Label))
                  {
                    e.Label = L[e.Label];
                  }

                  return e;
                }).ToList()
              },
            UnavailableReasons = transition.Offers.Count == 0
              ? null
              : [.. transition.Offers.Select(o => L[o.Reason])]
          };
          actionStart.Add(action);
        }
      }
    }
  }

  public virtual async Task<Dictionary<string, int>?> EvaluateUnavailableAsync(long? id, CancellationToken ct = default)
  {
    if (id is not > 0)
    {
      return null;
    }

    if (_lastElements == null)
    {
      var elements = new Elements();
      await SetElementsAsync(elements, ct);
      LocalizeAndResolveSelectElements(elements);
      _lastElements = elements;
    }

    Dictionary<string, int>? unavailable = null;
    if (_lastElements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      foreach (var transition in statusFlow.Transitions)
      {
        if (transition.Offers.Count == 0)
        {
          continue;
        }

        for (var i = 0; i < transition.Offers.Count; i++)
        {
          if (transition.Offers[i].Condition is Expression<Func<TEntity, bool>> cond)
          {
            var passes = await db.Set<TEntity>().Where(e => e.Id == id.Value).AnyAsync(cond, ct);
            if (!passes)
            {
              unavailable ??= new Dictionary<string, int>(StringComparer.Ordinal);
              unavailable[$"status.{transition.Name.ToLowerInvariant()}"] = i;
              break;
            }
          }
        }
      }
    }

    var customNav = new Nav(Service, L);
    await SetNavAsync(customNav, ct);
    foreach (var entry in customNav.Actions)
    {
      if (entry.Offers.Count == 0)
      {
        continue;
      }

      for (var i = 0; i < entry.Offers.Count; i++)
      {
        if (entry.Offers[i].Condition is Expression<Func<TEntity, bool>> cond)
        {
          var passes = await db.Set<TEntity>().Where(e => e.Id == id.Value).AnyAsync(cond, ct);
          if (!passes)
          {
            unavailable ??= new Dictionary<string, int>(StringComparer.Ordinal);
            unavailable[entry.Model.Key] = i;
            break;
          }
        }
      }
    }

    return unavailable;
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

  public virtual async Task PopulateReferencesAsync(
    Dictionary<string, object?> dict,
    object? entity,
    CancellationToken ct = default)
  {
    if (entity == null)
    {
      return;
    }

    if (_lastElements == null)
    {
      var elements = new Elements();
      await SetElementsAsync(elements, ct);
      LocalizeAndResolveSelectElements(elements);
      _lastElements = elements;
    }

    await PopulateReferencesCoreAsync(dict, entity, _lastElements.List, ct);
  }

  public virtual async Task PopulateReferencesAsync(
    Dictionary<string, object?> dict,
    object? entity,
    ScreenDto screen,
    CancellationToken ct = default)
  {
    if (entity == null || screen.Layout?.Root == null)
    {
      return;
    }

    var formModel = FindFormModel(screen.Layout.Root);
    if (formModel == null)
    {
      return;
    }

    await PopulateReferencesCoreAsync(dict, entity, formModel.Elements, ct);
  }

  private async Task PopulateReferencesCoreAsync(
    Dictionary<string, object?> dict,
    object entity,
    IEnumerable<FormElementDto> elements,
    CancellationToken ct)
  {
    var entityType = entity.GetType();
    var refPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var el in elements)
    {
      if (string.IsNullOrEmpty(el.ReferencePath))
      {
        continue;
      }

      refPaths.Add(el.ReferencePath);

      var selectModel = el.Model as FormSelectModel;
      var labelPaths = selectModel?.ReferenceLabelPaths;

      if (selectModel?.IsMultiple == true)
      {
        var keys = new List<long>();
        if (dict.TryGetValue(el.Path, out var multiRaw) && multiRaw != null)
        {
          if (multiRaw is JsonElement jeArr && jeArr.ValueKind == JsonValueKind.Array)
          {
            foreach (var item in jeArr.EnumerateArray())
            {
              if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var jeId) && jeId > 0)
              {
                keys.Add(jeId);
              }
            }
          }
          else if (multiRaw is IEnumerable enumerable and not string)
          {
            foreach (var item in enumerable)
            {
              if (item is long l && l > 0)
              {
                keys.Add(l);
              }
              else if (item is int i && i > 0)
              {
                keys.Add(i);
              }
              else if (item != null && long.TryParse(item.ToString(), out var parsed) && parsed > 0)
              {
                keys.Add(parsed);
              }
            }
          }
        }

        var refs = new List<Reference>(keys.Count);
        if (keys.Count > 0 && !string.IsNullOrEmpty(selectModel.ReferenceService))
        {
          var refServiceType = el.ReferenceServiceType
            ?? ModuleCatalog.Instance.FindServiceType(selectModel.ReferenceService)
            ?? ModuleCatalog.Instance.FindFeature(selectModel.ReferenceService)?.ServiceType;
          var refEntityType = refServiceType != null ? ResolveServiceEntityType(refServiceType) : null;
          var targetDb = refEntityType != null && db.Model.FindEntityType(refEntityType) != null
            ? db
            : (refServiceType != null ? (ModuleCatalog.Instance.CreateServiceInstance(refServiceType, db, PlatformApp) as IGridService)?.DbContext : null) ?? db;

          var loadedById = refEntityType != null
            ? await LoadEntitiesByIdsAsync(targetDb, refEntityType, keys, ct)
            : [];

          foreach (var k in keys)
          {
            var itemCaption = loadedById.TryGetValue(k, out var loaded)
              ? ReadDisplayCaption(loaded, labelPaths)
              : null;
            refs.Add(new Reference { Id = k, Name = !string.IsNullOrEmpty(itemCaption) ? L[itemCaption] : $"#{k}" });
          }
        }

        dict[el.ReferencePath] = refs;
        continue;
      }

      long? key = null;
      if (dict.TryGetValue(el.Path, out var rawVal) && rawVal != null)
      {
        if (rawVal is JsonElement je)
        {
          if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var jeId))
          {
            key = jeId;
          }
          else if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out var jeParsed))
          {
            key = jeParsed;
          }
        }
        else if (rawVal is long l)
        {
          key = l;
        }
        else if (rawVal is int i)
        {
          key = i;
        }
        else if (long.TryParse(rawVal.ToString(), out var parsedId))
        {
          key = parsedId;
        }
      }

      if (key == null || key <= 0)
      {
        dict[el.ReferencePath] = null;
        continue;
      }

      var navObj = PropertyAccessorCache.GetValue(entity, el.ReferencePath);
      if (navObj == null)
      {
        var candidateName = el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
          ? el.Path[..^2]
          : el.Path;
        if (PropertyAccessorCache.GetProperty(entityType, candidateName) is not null)
        {
          navObj = PropertyAccessorCache.GetValue(entity, candidateName);
        }
      }

      var refName = ReadDisplayCaption(navObj, labelPaths);

      if (string.IsNullOrEmpty(refName) && selectModel != null && !string.IsNullOrEmpty(selectModel.ReferenceService))
      {
        var refServiceType = ModuleCatalog.Instance.FindServiceType(selectModel.ReferenceService)
          ?? ModuleCatalog.Instance.FindFeature(selectModel.ReferenceService)?.ServiceType;

        if (refServiceType != null)
        {
          var refEntityType = ResolveServiceEntityType(refServiceType);
          if (refEntityType != null)
          {
            try
            {
              var targetDb = db.Model.FindEntityType(refEntityType) != null
                ? db
                : (ModuleCatalog.Instance.CreateServiceInstance(refServiceType, db, PlatformApp) as IGridService)?.DbContext ?? db;
              var loaded = await targetDb.FindAsync(refEntityType, [key.Value], ct);
              refName = ReadDisplayCaption(loaded, labelPaths);
            }
            catch { }
          }
        }
      }

      dict[el.ReferencePath] = new Reference
      {
        Id = key.Value,
        Name = !string.IsNullOrEmpty(refName) ? L[refName] : $"#{key.Value}"
      };
    }

    var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var el in elements)
    {
      if (!string.IsNullOrEmpty(el.Path) && !el.IsSecret && !string.Equals(el.Type, "password", StringComparison.OrdinalIgnoreCase))
      {
        allowedKeys.Add(el.Path);
      }

      if (!string.IsNullOrEmpty(el.ReferencePath))
      {
        allowedKeys.Add(el.ReferencePath);
      }
    }

    foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
      if (Attribute.IsDefined(prop, typeof(SensitiveAttribute)))
      {
        allowedKeys.Remove(JsonNamingPolicy.CamelCase.ConvertName(prop.Name));
      }
    }

    if (allowedKeys.Count > 0)
    {
      foreach (var key in dict.Keys.ToList())
      {
        if (!allowedKeys.Contains(key))
        {
          dict.Remove(key);
        }
      }
    }
  }

  private static readonly MethodInfo LoadEntitiesByIdsGenericMethod =
    typeof(FormService<TEntity, TInput>).GetMethod(nameof(LoadEntitiesByIdsCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static async Task<Dictionary<long, object>> LoadEntitiesByIdsAsync(
    DbContext targetDb,
    Type entityType,
    IReadOnlyList<long> ids,
    CancellationToken ct)
  {
    if (ids.Count == 0 || !typeof(IEntity).IsAssignableFrom(entityType))
    {
      return [];
    }

    try
    {
      var task = (Task<Dictionary<long, object>>)LoadEntitiesByIdsGenericMethod
        .MakeGenericMethod(entityType)
        .Invoke(null, [targetDb, ids, ct])!;
      return await task;
    }
    catch
    {
      return [];
    }
  }

  private static async Task<Dictionary<long, object>> LoadEntitiesByIdsCoreAsync<TRefEntity>(
    DbContext targetDb,
    IReadOnlyList<long> ids,
    CancellationToken ct)
    where TRefEntity : class, IEntity
  {
    var rows = await targetDb.Set<TRefEntity>()
      .AsNoTracking()
      .Where(e => ids.Contains(e.Id))
      .ToListAsync(ct);
    return rows.ToDictionary(r => r.Id, r => (object)r);
  }

  private static string? ReadDisplayCaption(object? target, IReadOnlyList<string>? labelPaths = null)
  {
    if (target == null)
    {
      return null;
    }

    if (labelPaths is { Count: > 0 })
    {
      var parts = new List<string>(labelPaths.Count);
      foreach (var path in labelPaths)
      {
        var rawPart = PropertyAccessorCache.GetValue(target, path);
        if (rawPart is null or Reference or IEntity)
        {
          continue;
        }

        var partVal = rawPart.ToString();
        if (!string.IsNullOrWhiteSpace(partVal))
        {
          parts.Add(partVal);
        }
      }

      if (parts.Count > 0)
      {
        return string.Join(" ", parts);
      }
    }

    var val = PropertyAccessorCache.GetValue(target, "Name")
      ?? PropertyAccessorCache.GetValue(target, "Title")
      ?? PropertyAccessorCache.GetValue(target, "Number")
      ?? PropertyAccessorCache.GetValue(target, "Login");
    return val?.ToString();
  }

  private static Type? ResolveServiceEntityType(Type serviceType)
  {
    var cur = serviceType;
    while (cur != null && cur != typeof(object))
    {
      if (cur.IsGenericType)
      {
        var def = cur.GetGenericTypeDefinition();
        if (def == typeof(GridService<,>) || def == typeof(FormService<,>))
        {
          return cur.GetGenericArguments()[0];
        }
      }

      cur = cur.BaseType;
    }

    return null;
  }

  private static FormModel? FindFormModel(PanelNodeDto node)
  {
    if (node.Model is FormModel fm)
    {
      return fm;
    }

    if (node.Children != null)
    {
      foreach (var child in node.Children)
      {
        var res = FindFormModel(child);
        if (res != null)
        {
          return res;
        }
      }
    }

    return null;
  }
}
