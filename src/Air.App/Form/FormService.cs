using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Air.App.Components;
using Air.App.Grid;
using Air.App.Localization;
using Air.App.Modules;
using Air.App.Nav;
using Air.App.Overlay;
using Air.App.Panels;
using Air.App.Screens;
using Air.Data;
using Air.Data.Contracts;
using Air.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Air.App.Form;

public interface IFormService
{
  PlatformDbContext DbContext { get; }
  OverlayModelDto Overlay(string service);
  Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, FormRequest request, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct) => BuildScreenAsync(servicePath, (long?)null, ct);
  Task<object?> GetModelAsync(long? id, CancellationToken ct = default);
  Task<object?> GetModelAsync(FormRequest request, CancellationToken ct = default);
  Task<object> SaveAsync(JsonElement json, CancellationToken ct = default);
  Task<object> SaveAsync(JsonElement json, FormRequest? request, CancellationToken ct = default);
  Task DeleteAsync(long id, CancellationToken ct = default);
  Task<FormActionDto> MoveStatusAsync(long id, string transitionName, JsonElement? body, CancellationToken ct = default);
  Task<GridCellDto> SaveCellAsync(long id, string path, JsonElement value, FormRequest request, Func<CancellationToken, Task<object?>> readRow, CancellationToken ct = default);
  void AttachPlatformApp(PlatformAppService app);
}

public sealed class FormRefusedException(
  string outcome,
  string title,
  IReadOnlyList<MessageDto> messages) : Exception(title)
{
  public string Outcome { get; } = outcome;
  public string Title { get; } = title;
  public IReadOnlyList<MessageDto> Messages { get; } = messages;
}

public class FormLoadContext<TEntity, TInput>
{
  public required TEntity Entity { get; init; }
  public required TInput Input { get; init; }
  public bool IsNew { get; init; }
}

public class FormSaveContext<TEntity, TInput>
{
  public required TEntity Entity { get; init; }
  public required TInput Input { get; init; }
  public bool IsNew { get; init; }
  internal IReadOnlyDictionary<string, object?>? OriginalValues { get; init; }
  public List<MessageDto> Errors { get; } = [];

  public void AddError(string detail, string? path = null) =>
    Errors.Add(new MessageDto { Detail = detail, Path = path, Type = MessageType.Error });

  public bool Changed(params Expression<Func<TInput, object?>>[] properties)
  {
    if (IsNew || OriginalValues is null) return true;
    if (properties.Length == 0) return true;

    foreach (var propExpr in properties)
    {
      var body = propExpr.Body is UnaryExpression ue ? ue.Operand : propExpr.Body;
      if (body is MemberExpression me && me.Member is PropertyInfo pi)
      {
        var currentVal = pi.GetValue(Input);
        OriginalValues.TryGetValue(pi.Name, out var origVal);
        if (!Equals(currentVal, origVal))
        {
          return true;
        }
      }
    }

    return false;
  }
}

public class FormDeleteContext<TEntity>
{
  public required TEntity Entity { get; init; }
  public List<MessageDto> Errors { get; } = [];

  public void AddError(string detail, string? path = null) =>
    Errors.Add(new MessageDto { Detail = detail, Path = path, Type = MessageType.Error });
}

public class RuleContext<TInput>
{
  public required object? Target { get; init; }
  public required FormSaveContext<object, TInput> SaveContext { get; init; }
  public List<MessageDto> Failures { get; } = [];
  internal string? CurrentPath { get; set; }

  public void AddFailure(string message, string? path = null) =>
    Failures.Add(new MessageDto
    {
      Detail = message,
      Path = path ?? CurrentPath,
      Type = MessageType.Error
    });
}

public class RuleBuilder<TInput, TProp>(Func<FormSaveContext<object, TInput>, TProp> getter, string? path = null)
{
  private readonly List<Func<TProp, RuleContext<TInput>, CancellationToken, Task>> _validators = [];
  private string? _lastErrorMessage;
  private string? _lastErrorCode;

  public RuleBuilder<TInput, TProp> Custom(Action<TProp, RuleContext<TInput>> action)
  {
    _validators.Add((val, ctx, ct) => { action(val, ctx); return Task.CompletedTask; });
    return this;
  }

  public RuleBuilder<TInput, TProp> CustomAsync(Func<TProp, RuleContext<TInput>, CancellationToken, Task> func)
  {
    _validators.Add(func);
    return this;
  }

  public RuleBuilder<TInput, TProp> Matches(string pattern)
  {
    var regex = new System.Text.RegularExpressions.Regex(pattern);
    _validators.Add((val, ctx, ct) =>
    {
      if (val is string str && !string.IsNullOrEmpty(str) && !regex.IsMatch(str))
      {
        ctx.AddFailure(_lastErrorMessage ?? "Value does not match required pattern.", path);
      }
      return Task.CompletedTask;
    });
    return this;
  }

  public RuleBuilder<TInput, TProp> WithMessage(string message)
  {
    _lastErrorMessage = message;
    return this;
  }

  public RuleBuilder<TInput, TProp> WithErrorCode(string code)
  {
    _lastErrorCode = code;
    return this;
  }

  internal async Task ValidateAsync(FormSaveContext<object, TInput> ctx, RuleContext<TInput> ruleCtx, CancellationToken ct)
  {
    var prevPath = ruleCtx.CurrentPath;
    ruleCtx.CurrentPath = path;
    try
    {
      var val = getter(ctx);
      foreach (var v in _validators)
      {
        await v(val, ruleCtx, ct);
      }
    }
    finally
    {
      ruleCtx.CurrentPath = prevPath;
    }
  }
}

public abstract class FormService<TEntity, TInput>(PlatformDbContext db, PlatformAppService? app = null) : IFormService
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  private PlatformAppService? _app = app;
  public void AttachPlatformApp(PlatformAppService service) => _app ??= service;

  public PlatformDbContext DbContext => db;
  protected PlatformAppService? PlatformApp => _app;
  protected Air.App.Auth.IUserContext? User => _app?.User;

  public sealed class Elements : Elements<TInput> { }

  protected Localizer L => Localizer.Instance;

  public virtual Feature Feature => new("", GetType().Name.Replace("FormService", "").ToLowerInvariant(), GetType());

  protected virtual string Title => !string.IsNullOrEmpty(Feature.Name) ? Feature.Name : typeof(TEntity).Name;

  protected bool IsNew { get; set; }
  protected virtual bool IsReadOnly => Feature.IsReadOnly || (User != null && User.IsAuthenticated && !User.Can(Feature.Edit) && !User.Can(Feature.All));
  protected FormRequest Request { get; set; } = new();
  protected long? ParentId => Request.ParentId;

  protected TParent? ParentRecord<TParent>() where TParent : class, IEntity
  {
    if (ParentId == null || ParentId.Value <= 0)
      return null;
    var tracked = db.Set<TParent>().Local.FirstOrDefault(e => e.Id == ParentId.Value);
    if (tracked != null) return tracked;
    var efModel = db.Model.FindEntityType(typeof(TParent));
    IQueryable<TParent> query = db.Set<TParent>();
    if (efModel != null)
    {
      foreach (var nav in efModel.GetNavigations())
      {
        if (!nav.IsCollection)
          query = query.Include(nav.Name);
      }
    }
    return query.FirstOrDefault(e => e.Id == ParentId.Value);
  }

  public sealed class Parent
  {
    public string? BoundParentService { get; private set; }
    public PropertyInfo? BoundProperty { get; private set; }
    public bool IsOptional { get; private set; }

    public void Bind<TParentService>(Expression<Func<TEntity, long>> member, bool optional = false)
    {
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      IsOptional = optional;
      var expr = member.Body;
      if (expr is UnaryExpression ue) expr = ue.Operand;
      if (expr is MemberExpression me && me.Member is PropertyInfo pi)
      {
        BoundProperty = pi;
      }
    }

    public void Bind<TParentService>(Expression<Func<TEntity, long?>> member, bool optional = false)
    {
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      IsOptional = optional;
      var expr = member.Body;
      if (expr is UnaryExpression ue) expr = ue.Operand;
      if (expr is MemberExpression me && me.Member is PropertyInfo pi)
      {
        BoundProperty = pi;
      }
    }
  }

  protected virtual void SetParent(Parent parent) { }

  public sealed class Hooks
  {
    internal List<Func<FormLoadContext<TEntity, TInput>, CancellationToken, Task>> AfterLoadActions { get; } = [];
    internal List<Action<FormSaveContext<TEntity, TInput>>> BeforeValidateActions { get; } = [];
    internal List<Func<FormSaveContext<TEntity, TInput>, CancellationToken, Task>> BeforeSaveActions { get; } = [];
    internal List<Func<FormSaveContext<TEntity, TInput>, CancellationToken, Task>> AfterSaveActions { get; } = [];
    internal List<Func<FormDeleteContext<TEntity>, CancellationToken, Task>> BeforeDeleteActions { get; } = [];
    internal List<Func<FormDeleteContext<TEntity>, CancellationToken, Task>> AfterDeleteActions { get; } = [];

    public void AfterLoad(Func<FormLoadContext<TEntity, TInput>, CancellationToken, Task> handler) => AfterLoadActions.Add(handler);
    public void AfterLoad(Action<FormLoadContext<TEntity, TInput>> handler) =>
      AfterLoadActions.Add((c, ct) => { handler(c); return Task.CompletedTask; });

    public void BeforeValidate(Action<FormSaveContext<TEntity, TInput>> handler) => BeforeValidateActions.Add(handler);

    public void BeforeSave(Func<FormSaveContext<TEntity, TInput>, CancellationToken, Task> handler) => BeforeSaveActions.Add(handler);
    public void BeforeSave(Action<FormSaveContext<TEntity, TInput>> handler) =>
      BeforeSaveActions.Add((c, ct) => { handler(c); return Task.CompletedTask; });

    public void AfterSave(Func<FormSaveContext<TEntity, TInput>, CancellationToken, Task> handler) => AfterSaveActions.Add(handler);
    public void AfterSave(Action<FormSaveContext<TEntity, TInput>> handler) =>
      AfterSaveActions.Add((c, ct) => { handler(c); return Task.CompletedTask; });

    public void BeforeDelete(Func<FormDeleteContext<TEntity>, CancellationToken, Task> handler) => BeforeDeleteActions.Add(handler);
    public void BeforeDelete(Action<FormDeleteContext<TEntity>> handler) =>
      BeforeDeleteActions.Add((c, ct) => { handler(c); return Task.CompletedTask; });
    public void ValidateDelete(Func<FormDeleteContext<TEntity>, CancellationToken, Task> handler) => BeforeDelete(handler);
    public void ValidateDelete(Action<FormDeleteContext<TEntity>> handler) => BeforeDelete(handler);

    public void AfterDelete(Func<FormDeleteContext<TEntity>, CancellationToken, Task> handler) => AfterDeleteActions.Add(handler);
    public void AfterDelete(Action<FormDeleteContext<TEntity>> handler) =>
      AfterDeleteActions.Add((c, ct) => { handler(c); return Task.CompletedTask; });
  }

  public sealed class Rules
  {
    internal List<Func<FormSaveContext<object, TInput>, RuleContext<TInput>, CancellationToken, Task>> Validators { get; } = [];

    public RuleBuilder<TInput, TProp> For<TProp>(Expression<Func<TInput, TProp>> member)
    {
      var compiled = member.Compile();
      string? path = null;
      var expr = member.Body is UnaryExpression ue ? ue.Operand : member.Body;
      if (expr is MemberExpression me)
      {
        path = char.ToLowerInvariant(me.Member.Name[0]) + me.Member.Name[1..];
      }
      var builder = new RuleBuilder<TInput, TProp>(ctx => compiled(ctx.Input), path);
      Validators.Add((ctx, rCtx, ct) => builder.ValidateAsync(ctx, rCtx, ct));
      return builder;
    }
  }

  protected static FormSaveContext<TEntity, TInput> SaveContextOf(RuleContext<TInput> context) =>
    new()
    {
      Entity = (TEntity)context.SaveContext.Entity,
      Input = context.SaveContext.Input,
      IsNew = context.SaveContext.IsNew,
      OriginalValues = context.SaveContext.OriginalValues
    };

  protected virtual void SetElements(Elements elements) { }

  protected virtual Task SetElementsAsync(Elements elements, CancellationToken ct = default)
  {
    SetElements(elements);
    return Task.CompletedTask;
  }

  protected virtual void SetRules(Rules rules) { }
  protected virtual void SetHooks(Hooks hooks) { }

  public string Service => ServicePathResolver.Resolve(GetType());

  protected virtual OverlayBuilder Presentation(string handle) => OverlayModel.Dialog(handle);

  public virtual OverlayModelDto Overlay(string service) => Presentation($"{service}.modal").Screen(service).Build();

  protected virtual (string? Width, string? Height) OverlayAsk(Elements elements)
  {
    var declared = Overlay(Service);
    if (declared.Presentation != OverlayPresentation.Dialog || declared.Width is not null || declared.Height is not null)
    {
      return (null, null);
    }

    return elements.Children.Count > 0 ? (PanelSize.Fill, PanelSize.Fill) : (PanelSize.Px(720), null);
  }

  protected virtual bool FillsOverlay(Elements elements)
  {
    var declared = Overlay(Service);
    return declared.Presentation == OverlayPresentation.Drawer
           || declared.Height == PanelSize.Fill
           || OverlayAsk(elements).Height == PanelSize.Fill;
  }

  protected virtual string RecordCaption(TEntity? entity, long? id)
  {
    var title = L[Title];
    if (id is null or 0)
    {
      var newTitleKey = $"New {Title}";
      var localizedNew = L[newTitleKey];
      return localizedNew != newTitleKey ? localizedNew : $"{L["New"]} {title}";
    }

    var labelProp = entity?.GetType().GetProperty("Number")
      ?? entity?.GetType().GetProperty("Name")
      ?? entity?.GetType().GetProperty("Title");
    var label = labelProp?.GetValue(entity)?.ToString();

    return string.IsNullOrWhiteSpace(label)
      ? $"{title} #{id}"
      : $"{title} {label}";
  }

  public virtual Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default) =>
    BuildScreenAsync(servicePath, new FormRequest { Id = id }, ct);

  public virtual async Task<ScreenDto> BuildScreenAsync(string servicePath, FormRequest request, CancellationToken ct = default)
  {
    Request = request;
    var id = request.Id;
    IsNew = (id is null or 0);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);

    foreach (var el in elements.List)
    {
      if (!string.IsNullOrEmpty(el.SeparatorLabel))
      {
        el.SeparatorLabel = L[el.SeparatorLabel];
      }
      if (!string.IsNullOrEmpty(el.Label))
      {
        el.Label = !string.IsNullOrEmpty(Feature.Name) && !string.IsNullOrEmpty(el.Path)
          ? L.TextOr($"{Feature.Name}.{el.Path}", L[el.Label])
          : L[el.Label];
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
              var ctors = targetServiceType.GetConstructors();
              var ctor = ctors.FirstOrDefault();
              if (ctor != null)
              {
                var pValues = ctor.GetParameters().Select(p => p.ParameterType.IsAssignableFrom(db.GetType()) ? (object)db : null).ToArray();
                if (Activator.CreateInstance(targetServiceType, pValues) is IGridService gridInst)
                {
                  var customPaths = gridInst.GetReferenceLabelPaths();
                  if (customPaths is { Count: > 0 })
                  {
                    select.ReferenceLabelPaths = [.. customPaths];
                  }
                }
              }
            }
            catch { }
          }
        }
      }
    }

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

    TEntity? entity = null;
    if (id is not null && id > 0)
    {
      entity = await db.Set<TEntity>().FindAsync([id.Value], ct);
    }

    var caption = RecordCaption(entity, id);

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

    var filesChildren = elements.Children.Where(c => c.IsFiles).ToList();
    var pageChildren = elements.Children.Where(c => !c.IsFiles).ToList();
    var hasPages = pageChildren.Count > 0 && id.HasValue && id.Value > 0;

    if (filesChildren.Count > 0)
    {
      formModel.Files = [];
      foreach (var child in filesChildren)
      {
        Air.Data.Entities.FileRulesAttribute? rules = null;
        var hasCoverFile = false;
        if (child.ServiceType != null)
        {
          var cur = child.ServiceType;
          while (cur != null && cur != typeof(object))
          {
            if (cur.IsGenericType && cur.Name.StartsWith("FileGridService"))
            {
              var fileEntity = cur.GetGenericArguments()[0];
              rules = Air.Data.Entities.FileRulesAttribute.TryGet(fileEntity);
              hasCoverFile = typeof(Air.Data.Entities.IHasCoverFile).IsAssignableFrom(fileEntity);
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

        formModel.Files.Add(new Contracts.FormFilesModel
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

    var nav = new NavModel
    {
      Key = $"{servicePath}.nav",
      Start = navStart,
      Elements = navElements,
      End = navEnd
    };

    var actionStart = new List<NavElementModel>();
    var actionElements = new List<NavElementModel>();

    if (elements.StatusFlow is Screens.IRecordStatuses<TEntity> statusFlow)
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
            ParentId = id!.Value,
            Tabs = strip
          })
          .Label(L[child.DeclaredLabel])
          .Fill()
          .Min(240)
        );
      }
    }

    var (overlayWidth, overlayHeight) = OverlayAsk(elements);
    return new ScreenDto
    {
      Layout = layout.Build(),
      OverlayWidth = overlayWidth,
      OverlayHeight = overlayHeight
    };
  }

  public virtual Task<object?> GetModelAsync(long? id, CancellationToken ct = default) =>
    GetModelAsync(new FormRequest { Id = id }, ct);

  public virtual async Task<object?> GetModelAsync(FormRequest request, CancellationToken ct = default)
  {
    Request = request;
    var id = request.Id;
    IsNew = (id is null or 0);

    var parent = new Parent();
    SetParent(parent);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);

    TEntity entity;
    if (IsNew)
    {
      entity = new TEntity();
      if (request.ParentId.HasValue && parent.BoundProperty != null)
      {
        if (parent.BoundProperty.PropertyType == typeof(long))
          parent.BoundProperty.SetValue(entity, request.ParentId.Value);
        else if (parent.BoundProperty.PropertyType == typeof(long?))
          parent.BoundProperty.SetValue(entity, (long?)request.ParentId.Value);
      }
      if (elements.StatusFlow is Screens.IRecordStatuses<TEntity> statusFlow && statusFlow.Initial != null)
      {
        statusFlow.Initialize(entity);
      }
    }
    else
    {
      IQueryable<TEntity> query = db.Set<TEntity>();
      var efModel = db.Model.FindEntityType(typeof(TEntity));
      if (efModel != null)
      {
        foreach (var nav in efModel.GetNavigations())
        {
          if (!nav.IsCollection)
          {
            query = query.Include(nav.Name);
          }
        }
      }

      entity = await query.FirstOrDefaultAsync(e => e.Id == id!.Value, ct)
        ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");

      if (entity != null && parent.BoundProperty != null)
      {
        var storedParentId = parent.BoundProperty.GetValue(entity) as long?;
        if (storedParentId.HasValue && !Request.ParentId.HasValue)
        {
          Request.ParentId = storedParentId.Value;
        }
      }
    }

    var input = new TInput();
    CopyMatchingProperties(entity!, input);

    var hooks = new Hooks();
    SetHooks(hooks);

    var loadCtx = new FormLoadContext<TEntity, TInput>
    {
      Entity = entity!,
      Input = input,
      IsNew = IsNew
    };

    foreach (var h in hooks.AfterLoadActions)
    {
      await h(loadCtx, ct);
    }

    return input;
  }

  public virtual Task<object> SaveAsync(JsonElement json, CancellationToken ct = default) =>
    SaveAsync(json, null, ct);

  public virtual async Task<object> SaveAsync(JsonElement json, FormRequest? request = null, CancellationToken ct = default)
  {
    if (IsReadOnly)
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        L["MutationRefused"],
        [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]);
    }

    var entityJson = json.ValueKind == JsonValueKind.Object && (json.TryGetProperty("value", out var val) || json.TryGetProperty("entity", out val))
      ? val
      : json;

    var input = JsonSerializer.Deserialize<TInput>(entityJson.GetRawText(), PlatformJsonOptions.DefaultOptions)
      ?? new TInput();

    long? id = request?.Id;
    if ((id is null or <= 0) && input is IEntity inpEntity && inpEntity.Id > 0)
    {
      id = inpEntity.Id;
    }
    else if ((id is null or <= 0) && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("id", out var idProp))
    {
      if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var rootId) && rootId > 0)
      {
        id = rootId;
      }
      else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var strId) && strId > 0)
      {
        id = strId;
      }
    }

    int? clientVer = request?.Ver;
    if (clientVer is null && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("ver", out var verProp) && verProp.ValueKind == JsonValueKind.Number && verProp.TryGetInt32(out var rootVer))
    {
      clientVer = rootVer;
    }
    else if (clientVer is null && input is IHasVersion verEntity && verEntity.Ver > 0)
    {
      clientVer = verEntity.Ver;
    }

    request ??= new FormRequest();
    request.Id = id;
    request.Ver = clientVer;
    if (!request.ParentId.HasValue && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("parentId", out var pidProp) && pidProp.ValueKind == JsonValueKind.Number && pidProp.TryGetInt64(out var jsonPid))
    {
      request.ParentId = jsonPid;
    }
    if (string.IsNullOrEmpty(request.ParentService) && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("parentService", out var psProp) && psProp.ValueKind == JsonValueKind.String && psProp.GetString() is { } jsonPs)
    {
      request.ParentService = jsonPs;
    }

    Request = request;
    IsNew = (id is null or 0);

    var parent = new Parent();
    SetParent(parent);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);

    var hooks = new Hooks();
    SetHooks(hooks);

    TEntity entity;
    Dictionary<string, object?>? originalValues = null;
    if (IsNew)
    {
      entity = new TEntity();
      if (Request.ParentId.HasValue && parent.BoundProperty != null)
      {
        if (parent.BoundProperty.PropertyType == typeof(long))
          parent.BoundProperty.SetValue(entity, Request.ParentId.Value);
        else if (parent.BoundProperty.PropertyType == typeof(long?))
          parent.BoundProperty.SetValue(entity, (long?)Request.ParentId.Value);
      }
      if (elements.StatusFlow is Screens.IRecordStatuses<TEntity> statusFlow && statusFlow.Initial != null)
      {
        statusFlow.Initialize(entity);
      }
    }
    else
    {
      entity = await db.Set<TEntity>().FindAsync([id!.Value], ct)
        ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");

      if (clientVer.HasValue && entity is IHasVersion versionedEntity && versionedEntity.Ver != clientVer.Value)
      {
        throw new FormRefusedException(
          MutationOutcome.Conflict,
          L["MutationConflict"],
          [new MessageDto { Detail = L["FormConcurrentEdit"], Type = MessageType.Error }]);
      }

      if (parent.BoundProperty != null)
      {
        var storedParentId = parent.BoundProperty.GetValue(entity) as long?;
        if (storedParentId.HasValue && !Request.ParentId.HasValue)
        {
          Request.ParentId = storedParentId.Value;
        }
      }

      originalValues = SnapshotValues(entity);
    }

    var protectedPaths = BuildProtectedPaths(elements);
    CopyMatchingProperties(input, entity, protectedPaths);
    if (id.HasValue && id.Value > 0)
    {
      entity.Id = id.Value;
    }
    if (Request.ParentId.HasValue && parent.BoundProperty != null)
    {
      if (parent.BoundProperty.PropertyType == typeof(long))
        parent.BoundProperty.SetValue(entity, Request.ParentId.Value);
      else if (parent.BoundProperty.PropertyType == typeof(long?))
        parent.BoundProperty.SetValue(entity, (long?)Request.ParentId.Value);
    }

    var saveCtx = new FormSaveContext<TEntity, TInput>
    {
      Entity = entity,
      Input = input,
      IsNew = IsNew,
      OriginalValues = originalValues
    };

    foreach (var h in hooks.BeforeValidateActions)
    {
      h(saveCtx);
    }

    var rules = new Rules();
    SetRules(rules);

    var ruleCtx = new RuleContext<TInput>
    {
      Target = input,
      SaveContext = new FormSaveContext<object, TInput>
      {
        Entity = entity,
        Input = input,
        IsNew = IsNew,
        OriginalValues = originalValues
      }
    };

    foreach (var r in rules.Validators)
    {
      await r(ruleCtx.SaveContext, ruleCtx, ct);
    }

    if (ruleCtx.Failures.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["FormValidationFailed"], ruleCtx.Failures);
    }

    foreach (var h in hooks.BeforeSaveActions)
    {
      await h(saveCtx, ct);
    }

    if (saveCtx.Errors.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["MutationRefused"], saveCtx.Errors);
    }

    if (IsNew)
    {
      db.Set<TEntity>().Add(entity);
      await db.SaveChangesAsync(ct);
    }
    else
    {
      db.Set<TEntity>().Update(entity);
      await db.SaveChangesAsync(ct);
    }

    foreach (var h in hooks.AfterSaveActions)
    {
      await h(saveCtx, ct);
    }

    await db.SaveChangesAsync(ct);
    CopyMatchingProperties(entity, input);
    return input;
  }

  public virtual async Task<GridCellDto> SaveCellAsync(
    long id,
    string path,
    JsonElement value,
    FormRequest request,
    Func<CancellationToken, Task<object?>> readRow,
    CancellationToken ct = default)
  {
    if (IsReadOnly)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Path = path, Type = MessageType.Error }]
      };
    }

    Request = request;
    IsNew = false;

    var parent = new Parent();
    SetParent(parent);

    var entity = await db.Set<TEntity>().FindAsync([id], ct)
      ?? throw new KeyNotFoundException(L["FormNotFound"]);

    if (request.Ver.HasValue && entity is IHasVersion versionedEntity && versionedEntity.Ver != request.Ver.Value)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Conflict,
        Warnings = [new MessageDto { Detail = L["FormConcurrentEdit"], Path = path, Type = MessageType.Error }]
      };
    }

    if (parent.BoundProperty != null)
    {
      var storedParentId = parent.BoundProperty.GetValue(entity) as long?;
      if (storedParentId.HasValue && !Request.ParentId.HasValue)
      {
        Request.ParentId = storedParentId.Value;
      }
    }

    var originalValues = SnapshotValues(entity);

    var input = new TInput();
    CopyMatchingProperties(entity, input);

    var hooks = new Hooks();
    SetHooks(hooks);

    var loadCtx = new FormLoadContext<TEntity, TInput>
    {
      Entity = entity,
      Input = input,
      IsNew = false
    };

    foreach (var h in hooks.AfterLoadActions)
    {
      await h(loadCtx, ct);
    }

    SetPropertyValue(input, path, value);
    SetPropertyValue(entity, path, value);

    var saveCtx = new FormSaveContext<TEntity, TInput>
    {
      Entity = entity,
      Input = input,
      IsNew = false,
      OriginalValues = originalValues
    };

    foreach (var h in hooks.BeforeValidateActions)
    {
      h(saveCtx);
    }

    var rules = new Rules();
    SetRules(rules);

    var ruleCtx = new RuleContext<TInput>
    {
      Target = input,
      SaveContext = new FormSaveContext<object, TInput>
      {
        Entity = entity,
        Input = input,
        IsNew = false,
        OriginalValues = originalValues
      }
    };

    foreach (var r in rules.Validators)
    {
      await r(ruleCtx.SaveContext, ruleCtx, ct);
    }

    if (ruleCtx.Failures.Count > 0)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = ruleCtx.Failures.Select(f => new MessageDto
        {
          Detail = f.Detail,
          Path = f.Path ?? path,
          Type = MessageType.Error
        }).ToList()
      };
    }

    foreach (var h in hooks.BeforeSaveActions)
    {
      await h(saveCtx, ct);
    }

    if (saveCtx.Errors.Count > 0)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = saveCtx.Errors
      };
    }

    db.Set<TEntity>().Update(entity);
    await db.SaveChangesAsync(ct);

    foreach (var h in hooks.AfterSaveActions)
    {
      await h(saveCtx, ct);
    }
    await db.SaveChangesAsync(ct);

    var row = await readRow(ct);
    return new GridCellDto
    {
      Row = row,
      Outcome = MutationOutcome.Committed
    };
  }

  internal static void SetPropertyValue(object target, string path, JsonElement val)
  {
    if (target == null || string.IsNullOrEmpty(path)) return;
    var current = target;
    var parts = path.Split('.');
    for (int i = 0; i < parts.Length - 1; i++)
    {
      var p = current.GetType().GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
      if (p == null) return;
      var next = p.GetValue(current);
      if (next == null) return;
      current = next;
    }

    var prop = current.GetType().GetProperty(parts[^1], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
    if (prop == null || !prop.CanWrite) return;

    var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
    object? converted = null;

    if (val.ValueKind == JsonValueKind.Object && (targetType == typeof(long) || targetType == typeof(int) || targetType == typeof(string)))
    {
      if (val.TryGetProperty("id", out var idElem))
      {
        val = idElem;
      }
    }

    if (val.ValueKind == JsonValueKind.Null || val.ValueKind == JsonValueKind.Undefined)
    {
      converted = null;
    }
    else if (targetType == typeof(string))
    {
      converted = val.ValueKind == JsonValueKind.String ? val.GetString() : val.GetRawText();
    }
    else if (targetType == typeof(decimal))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetDecimal() : decimal.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }
    else if (targetType == typeof(double))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetDouble() : double.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var d) ? d : 0d;
    }
    else if (targetType == typeof(float))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetSingle() : float.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var f) ? f : 0f;
    }
    else if (targetType == typeof(int))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetInt32() : int.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var i) ? i : 0;
    }
    else if (targetType == typeof(long))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetInt64() : long.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var l) ? l : 0L;
    }
    else if (targetType == typeof(short))
    {
      converted = val.ValueKind == JsonValueKind.Number ? val.GetInt16() : short.TryParse(val.GetString(), CultureInfo.InvariantCulture, out var s) ? s : (short)0;
    }
    else if (targetType == typeof(bool))
    {
      converted = val.ValueKind switch
      {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(val.GetString(), out var b) && b,
        _ => false
      };
    }
    else if (targetType == typeof(DateTime))
    {
      converted = val.GetDateTime();
    }
    else if (targetType == typeof(DateOnly))
    {
      converted = DateOnly.Parse(val.GetString()!);
    }
    else if (targetType == typeof(TimeOnly))
    {
      converted = TimeOnly.Parse(val.GetString()!);
    }
    else if (targetType.IsEnum)
    {
      converted = val.ValueKind == JsonValueKind.Number
        ? Enum.ToObject(targetType, val.GetInt64())
        : Enum.Parse(targetType, val.GetString()!, true);
    }
    else
    {
      converted = JsonSerializer.Deserialize(val.GetRawText(), prop.PropertyType);
    }

    prop.SetValue(current, converted);
  }

  public virtual async Task DeleteAsync(long id, CancellationToken ct = default)
  {
    if (User != null && User.IsAuthenticated && !User.Can(Feature.Delete) && !User.Can(Feature.All))
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        L["MutationRefused"],
        [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]);
    }

    var entity = await db.Set<TEntity>().FindAsync([id], ct)
      ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");

    var hooks = new Hooks();
    SetHooks(hooks);

    var deleteCtx = new FormDeleteContext<TEntity> { Entity = entity };
    foreach (var h in hooks.BeforeDeleteActions)
    {
      await h(deleteCtx, ct);
    }

    if (deleteCtx.Errors.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["MutationRefused"], deleteCtx.Errors);
    }

    db.Set<TEntity>().Remove(entity);
    await db.SaveChangesAsync(ct);

    foreach (var h in hooks.AfterDeleteActions)
    {
      await h(deleteCtx, ct);
    }
    await db.SaveChangesAsync(ct);
  }

  public virtual async Task<FormActionDto> MoveStatusAsync(
    long id,
    string transitionName,
    JsonElement? body,
    CancellationToken ct = default
  )
  {
    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    if (elements.StatusFlow is not Screens.IRecordStatuses<TEntity> statusFlow)
    {
      throw new InvalidOperationException($"Form '{GetType().Name}' does not use a status flow.");
    }

    var entity = await db.Set<TEntity>().FindAsync([id], ct);
    if (entity == null)
    {
      throw new InvalidOperationException($"Record #{id} not found.");
    }

    var transition = statusFlow.Transitions.FirstOrDefault(t =>
      string.Equals(t.Name, transitionName, StringComparison.OrdinalIgnoreCase));
    if (transition == null)
    {
      throw new InvalidOperationException($"Transition '{transitionName}' not found in '{statusFlow.GetType().Name}'.");
    }

    if (User != null && User.IsAuthenticated && !User.Can(Feature.Action(transition.Name)) && !User.Can(Feature.Edit) && !User.Can(Feature.All))
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
      };
    }

    object? input = transition.Ask?.Read(body);

    var context = new Mutations.MutationContext(db);
    await statusFlow.MoveAsync(context, [entity], transition.Name, input, ct);

    if (context.Errors.Count > 0)
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = context.Errors.Select(e => e.ToMessage()).ToList()
      };
    }

    return new FormActionDto
    {
      Outcome = MutationOutcome.Committed
    };
  }

  private static Dictionary<string, object?> SnapshotValues(object entity)
  {
    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    foreach (var prop in entity.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
      if (!prop.CanRead) continue;
      if (typeof(IEntity).IsAssignableFrom(prop.PropertyType)) continue;
      if (typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType) &&
          prop.PropertyType != typeof(string) &&
          prop.PropertyType != typeof(byte[]))
      {
        continue;
      }
      try
      {
        dict[prop.Name] = prop.GetValue(entity);
      }
      catch { }
    }
    return dict;
  }

  private static HashSet<string> BuildProtectedPaths(Elements elements)
  {
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "Id",
      "Ver",
      "CreatedAt",
      "CreatedBy",
      "UpdatedAt",
      "UpdatedBy"
    };

    foreach (var el in elements.List)
    {
      if ((el.IsServerOwned || el.IsInputOnly) && !string.IsNullOrEmpty(el.Path))
      {
        set.Add(el.Path);
      }
    }

    foreach (var prop in typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
      if (prop.GetCustomAttribute<Air.Data.Entities.SensitiveAttribute>() != null)
      {
        set.Add(prop.Name);
      }
    }

    return set;
  }

  private static void CopyMatchingProperties(object source, object target, HashSet<string>? protectedPaths = null)
  {
    var srcProps = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
    var tgtProps = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.CanWrite)
      .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    foreach (var sp in srcProps)
    {
      if (protectedPaths != null && protectedPaths.Contains(sp.Name))
      {
        continue;
      }

      if (sp.CanRead && tgtProps.TryGetValue(sp.Name, out var tp) && tp.PropertyType.IsAssignableFrom(sp.PropertyType))
      {
        if (typeof(IEntity).IsAssignableFrom(tp.PropertyType))
        {
          continue;
        }

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(tp.PropertyType) &&
            tp.PropertyType != typeof(string) &&
            tp.PropertyType != typeof(byte[]))
        {
          continue;
        }

        try
        {
          var val = sp.GetValue(source);
          tp.SetValue(target, val);
        }
        catch { }
      }
    }
  }
}

public abstract class FormService<TEntity>(PlatformDbContext db, PlatformAppService? app = null) : FormService<TEntity, TEntity>(db, app)
  where TEntity : class, IEntity, new();
