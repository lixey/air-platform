using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Air.App.Components;
using Air.App.Grid;
using Air.App.Internal;
using Air.App.Modules;
using Air.App.Mutations;
using Air.App.Nav;
using Air.App.Overlay;
using Air.App.Panels;
using Air.App.Screens;
using Air.Data.Contracts;
using Air.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Air.App.Form;

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
    IsNew = (id is null or 0);

    var parent = new Parent();
    SetParent(parent);

    TEntity? entity = null;
    if (id is not null && id > 0)
    {
      entity = await db.Set<TEntity>().FindAsync([id.Value], ct);
      if (entity != null)
      {
        RestoreBoundParentId(entity, parent);
      }
    }

    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    LocalizeAndResolveSelectElements(elements);
    _lastElements = elements;

    var formModel = new FormModel
    {
      Service = servicePath,
      Elements = elements.List.ToList(),
      Tabs = elements.Tabs.Count > 0
        ? elements.Tabs.Select(t => new FormTabDto { Key = t.Key, Label = L[t.Label] }).ToList()
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

    if (filesChildren.Count > 0)
    {
      formModel.Files = BuildFilesModels(servicePath, id, filesChildren);
    }

    var nav = BuildFormNavigation(servicePath, caption, elements, hasPages);
    var actionStart = new List<NavElementModel>();
    var actionElements = new List<NavElementModel>();

    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow)
    {
      ConfigureStatusFlowAndActions(servicePath, statusFlow, formModel, entity, id, actionStart);
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
      if (!string.IsNullOrEmpty(el.Placeholder))
      {
        el.Placeholder = L[el.Placeholder];
      }
      if (!string.IsNullOrEmpty(el.Help))
      {
        el.Help = L[el.Help];
      }
      if (el.Model is FormSelectModel select && !string.IsNullOrEmpty(select.ReferenceService))
      {
        if (select.ReferenceLabelPaths == null || select.ReferenceLabelPaths.Count == 0 || (select.ReferenceLabelPaths.Count == 1 && select.ReferenceLabelPaths[0] == "name"))
        {
          var targetServiceType = ModuleCatalog.Instance.FindServiceType(select.ReferenceService);
          if (targetServiceType != null && typeof(IGridService).IsAssignableFrom(targetServiceType))
          {
            try
            {
              if (ModuleCatalog.Instance.CreateServiceInstance(targetServiceType, db, _app) is IGridService gridInst)
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
      }
    }
  }

  internal void ApplyElementConstraints(Elements elements)
  {
    var efEntityType = db.Model.FindEntityType(typeof(TEntity));

    foreach (var el in elements.List)
    {
      if (string.IsNullOrEmpty(el.Path)) continue;

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
        ? new Files.Contracts.FileUploadLimitsDto
        {
          Accept = [.. rules.ContentTypes],
          Extensions = [.. Files.Internal.FileTypeTable.ExtensionsOf(rules.ContentTypes)],
          MaxBytes = rules.MaxBytes,
          SourceMaxBytes = rules.SourceMaxBytes > 0 ? rules.SourceMaxBytes : rules.MaxBytes,
          MaxFiles = rules.MaxFiles,
          MaxTotalBytes = rules.MaxTotalBytes,
          Images = rules.Images,
          ImageMaxSide = rules.ImageMaxSide,
          ImageQuality = rules.ImageQuality,
          MaxPixels = Files.Internal.FileTypeTable.MaxPixels,
          ThumbnailMaxBytes = Files.Internal.FileTypeTable.ThumbnailMaxBytes
        }
        : new Files.Contracts.FileUploadLimitsDto();

      filesModels.Add(new Contracts.FormFilesModel
      {
        Key = !string.IsNullOrEmpty(child.DeclaredKey) ? child.DeclaredKey : "files",
        Label = !string.IsNullOrEmpty(child.DeclaredLabel) ? L[child.DeclaredLabel] : null,
        Service = child.ServicePath,
        ParentService = servicePath,
        ParentId = id.HasValue && id.Value > 0 ? id.Value : null,
        Look = child.Look,
        Limits = limits,
        CanUpload = !IsReadOnly,
        CanDelete = !IsReadOnly,
        CanSetCover = hasCoverFile && !IsReadOnly,
        CanReadMetadata = true
      });
    }

    return filesModels;
  }

  private NavModel BuildFormNavigation(string servicePath, string caption, Elements elements, bool hasPages)
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
          Variant = ButtonVariant.Soft,
          Path = "form"
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

    var navEnd = new List<NavElementModel>
    {
      new NavCloseModel
      {
        Key = "close",
        Icon = UiIcon.Close,
        Color = UiColor.Slate,
        Variant = ButtonVariant.Ghost,
        Title = L["Close"]
      }
    };

    return new NavModel
    {
      Key = $"{servicePath}.nav",
      Start = navStart,
      Elements = navElements,
      End = navEnd
    };
  }

  private void ConfigureStatusFlowAndActions(
    string servicePath,
    IRecordStatuses<TEntity> statusFlow,
    FormModel formModel,
    TEntity? entity,
    long? id,
    List<NavElementModel> actionStart)
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
      Color = s.Color
    }).ToList();

    foreach (var el in formModel.Elements)
    {
      if ((statusMemberName != null && string.Equals(el.Path, statusMemberName, StringComparison.OrdinalIgnoreCase))
          || el.Path.EndsWith("statusId", StringComparison.OrdinalIgnoreCase)
          || el.Path.Equals("status", StringComparison.OrdinalIgnoreCase))
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

    if (entity != null && id.HasValue && id.Value > 0 && !IsReadOnly)
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
          var verbLabel = L.TextOr($"Ecom: {transition.Name}", L[transition.Name]);
          var targetLabel = targetStatus != null ? L[targetStatus.Name] : verbLabel;
          var confirmMessage = transition.Confirm != null ? L[transition.Confirm] : null;
          var action = new FormNavActionModel
          {
            Key = $"status.{transition.Name.ToLowerInvariant()}",
            Label = verbLabel != transition.Name ? verbLabel : targetLabel,
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
              }
          };
          actionStart.Add(action);
        }
      }
    }
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
    if (entity == null) return;
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
    if (entity == null || screen.Layout?.Root == null) return;

    var formModel = FindFormModel(screen.Layout.Root);
    if (formModel == null) return;

    await PopulateReferencesCoreAsync(dict, entity, formModel.Elements, ct);
  }

  private async Task PopulateReferencesCoreAsync(
    Dictionary<string, object?> dict,
    object entity,
    IEnumerable<FormElementDto> elements,
    CancellationToken ct)
  {
    var entityType = entity.GetType();

    foreach (var el in elements)
    {
      if (string.IsNullOrEmpty(el.ReferencePath)) continue;

      long? key = null;
      if (dict.TryGetValue(el.Path, out var rawVal) && rawVal != null)
      {
        if (rawVal is JsonElement je)
        {
          if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var jeId)) key = jeId;
          else if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out var jeParsed)) key = jeParsed;
        }
        else if (rawVal is long l) key = l;
        else if (rawVal is int i) key = i;
        else if (long.TryParse(rawVal.ToString(), out var parsedId)) key = parsedId;
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

      var selectModel = el.Model as FormSelectModel;
      var labelPaths = selectModel?.ReferenceLabelPaths;
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
              var loaded = await db.FindAsync(refEntityType, [key.Value], ct);
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
  }

  private static string? ReadDisplayCaption(object? target, IReadOnlyList<string>? labelPaths = null)
  {
    if (target == null) return null;
    if (labelPaths is { Count: > 0 })
    {
      var parts = new List<string>(labelPaths.Count);
      foreach (var path in labelPaths)
      {
        var partVal = PropertyAccessorCache.GetValue(target, path)?.ToString();
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
    if (node.Model is FormModel fm) return fm;
    if (node.Children != null)
    {
      foreach (var child in node.Children)
      {
        var res = FindFormModel(child);
        if (res != null) return res;
      }
    }
    return null;
  }
}
