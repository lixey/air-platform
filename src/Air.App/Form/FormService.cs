using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Air.App.Components;
using Air.App.Localization;
using Air.App.Modules;
using Air.App.Nav;
using Air.App.Overlay;
using Air.App.Panels;
using Air.App.Screens;
using Air.Data;
using Air.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Air.App.Form;

public interface IFormService
{
  PlatformDbContext DbContext { get; }
  OverlayModelDto Overlay(string service);
  Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct) => BuildScreenAsync(servicePath, null, ct);
  Task<object?> GetModelAsync(long? id, CancellationToken ct = default);
  Task<object> SaveAsync(JsonElement json, CancellationToken ct = default);
  Task DeleteAsync(long id, CancellationToken ct = default);
  Task<FormActionDto> MoveStatusAsync(long id, string transitionName, JsonElement? body, CancellationToken ct = default);
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
  public bool Changed(params Expression<Func<TInput, object?>>[] properties) => true;
}

public class RuleContext<TInput>
{
  public required object? Target { get; init; }
  public required FormSaveContext<object, TInput> SaveContext { get; init; }
  public List<string> Failures { get; } = [];
  public void AddFailure(string message) => Failures.Add(message);
}

public class RuleBuilder<TInput, TProp>(Func<FormSaveContext<object, TInput>, TProp> getter)
{
  private readonly List<Func<TProp, RuleContext<TInput>, CancellationToken, Task>> _validators = [];

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

  internal async Task ValidateAsync(FormSaveContext<object, TInput> ctx, RuleContext<TInput> ruleCtx, CancellationToken ct)
  {
    var val = getter(ctx);
    foreach (var v in _validators)
    {
      await v(val, ruleCtx, ct);
    }
  }
}

public abstract class FormService<TEntity, TInput>(PlatformDbContext db) : IFormService
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  public PlatformDbContext DbContext => db;

  public sealed class Elements : Elements<TInput> { }

  protected Localizer L => Localizer.Instance;

  public virtual Feature Feature => new("", GetType().Name.Replace("FormService", "").ToLowerInvariant(), GetType());

  protected virtual string Title => !string.IsNullOrEmpty(Feature.Name) ? Feature.Name : typeof(TEntity).Name;

  protected bool IsNew { get; set; }
  protected virtual bool IsReadOnly => Feature.IsReadOnly;
  protected FormRequest Request { get; set; } = new();
  public sealed class Parent
  {
    public void Bind<TParentService>(Expression<Func<TEntity, long>> member, bool optional = false) { }
    public void Bind<TParentService>(Expression<Func<TEntity, long?>> member, bool optional = false) { }
  }

  protected virtual void SetParent(Parent parent) { }

  public sealed class Hooks
  {
    internal List<Func<FormLoadContext<TEntity, TInput>, CancellationToken, Task>> AfterLoadActions { get; } = [];
    internal List<Action<FormSaveContext<TEntity, TInput>>> BeforeValidateActions { get; } = [];
    internal List<Func<FormSaveContext<TEntity, TInput>, CancellationToken, Task>> BeforeSaveActions { get; } = [];
    internal List<Func<FormSaveContext<TEntity, TInput>, CancellationToken, Task>> AfterSaveActions { get; } = [];

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
  }

  public sealed class Rules
  {
    internal List<Func<FormSaveContext<object, TInput>, RuleContext<TInput>, CancellationToken, Task>> Validators { get; } = [];

    public RuleBuilder<TInput, TProp> For<TProp>(Expression<Func<TInput, TProp>> member)
    {
      var compiled = member.Compile();
      var builder = new RuleBuilder<TInput, TProp>(ctx => compiled(ctx.Input));
      Validators.Add((ctx, rCtx, ct) => builder.ValidateAsync(ctx, rCtx, ct));
      return builder;
    }
  }

  protected static FormSaveContext<TEntity, TInput> SaveContextOf(RuleContext<TInput> context) =>
    new()
    {
      Entity = (TEntity)context.SaveContext.Entity,
      Input = context.SaveContext.Input,
      IsNew = context.SaveContext.IsNew
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

  public virtual async Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default)
  {
    Request.Id = id;
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
        el.Label = L[el.Label];
      }
    }

    var formModel = new FormModel
    {
      Service = servicePath,
      Elements = elements.List.ToList(),
      Tabs = elements.Tabs.Count > 0 ? elements.Tabs.ToList() : null,
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

    var hasPages = elements.Children.Count > 0 && id.HasValue && id.Value > 0;

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

      if (entity != null && id.HasValue && id.Value > 0)
      {
        var currentStatus = statusFlow.StatusMember.Compile()(entity);
        foreach (var transition in statusFlow.Transitions)
        {
          if (transition.From.Contains(currentStatus))
          {
            var targetStatus = statusFlow.Statuses.FirstOrDefault(s => s.Id == transition.To);
            var action = new FormNavActionModel
            {
              Key = $"status.{transition.Name.ToLowerInvariant()}",
              Label = targetStatus?.Name ?? transition.Name,
              Color = targetStatus?.Color,
              Service = servicePath,
              Method = $"move-status/{transition.Name.ToLowerInvariant()}",
              Confirm = transition.Ask is not null || transition.Confirm is null
                ? null
                : new OverlayConfirmModel { Message = transition.Confirm, Color = targetStatus?.Color },
              Ask = transition.Ask is null
                ? null
                : new FormAskModel
                {
                  Title = targetStatus?.Name ?? transition.Name,
                  Message = transition.Confirm,
                  Color = targetStatus?.Color,
                  Elements = [.. transition.Ask.BuildElements()]
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

      foreach (var child in elements.Children)
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

  public virtual async Task<object?> GetModelAsync(long? id, CancellationToken ct = default)
  {
    Request.Id = id;
    IsNew = (id is null or 0);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);

    TEntity entity;
    if (IsNew)
    {
      entity = new TEntity();
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
    }

    var input = new TInput();
    CopyMatchingProperties(entity, input);

    var hooks = new Hooks();
    SetHooks(hooks);

    var loadCtx = new FormLoadContext<TEntity, TInput>
    {
      Entity = entity,
      Input = input,
      IsNew = IsNew
    };

    foreach (var h in hooks.AfterLoadActions)
    {
      await h(loadCtx, ct);
    }

    CopyMatchingProperties(entity, input);
    return input;
  }

  public virtual async Task<object> SaveAsync(JsonElement json, CancellationToken ct = default)
  {
    var entityJson = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("value", out var val)
      ? val
      : json;

    var input = JsonSerializer.Deserialize<TInput>(entityJson.GetRawText(), PlatformJsonOptions.DefaultOptions)
      ?? new TInput();

    long? id = null;
    if (input is IEntity inpEntity && inpEntity.Id > 0)
    {
      id = inpEntity.Id;
    }
    else if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("id", out var idProp))
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

    Request.Id = id;
    IsNew = (id is null or 0);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);

    TEntity entity;
    if (IsNew)
    {
      entity = new TEntity();
    }
    else
    {
      entity = await db.Set<TEntity>().FindAsync([id!.Value], ct)
        ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");
    }

    CopyMatchingProperties(input, entity);
    if (id.HasValue && id.Value > 0)
    {
      entity.Id = id.Value;
    }

    var saveCtx = new FormSaveContext<TEntity, TInput>
    {
      Entity = entity,
      Input = input,
      IsNew = IsNew
    };

    var hooks = new Hooks();
    SetHooks(hooks);

    foreach (var h in hooks.BeforeValidateActions)
    {
      h(saveCtx);
    }

    var rules = new Rules();
    SetRules(rules);

    var ruleCtx = new RuleContext<TInput>
    {
      Target = input,
      SaveContext = new FormSaveContext<object, TInput> { Entity = entity, Input = input, IsNew = IsNew }
    };

    foreach (var r in rules.Validators)
    {
      await r(ruleCtx.SaveContext, ruleCtx, ct);
    }

    if (ruleCtx.Failures.Count > 0)
    {
      throw new InvalidOperationException(string.Join("; ", ruleCtx.Failures));
    }

    foreach (var h in hooks.BeforeSaveActions)
    {
      await h(saveCtx, ct);
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

  public virtual async Task DeleteAsync(long id, CancellationToken ct = default)
  {
    var entity = await db.Set<TEntity>().FindAsync([id], ct)
      ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");
    db.Set<TEntity>().Remove(entity);
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

    object? input = transition.Ask?.Read(body);

    var context = new Mutations.MutationContext(db);
    await statusFlow.MoveAsync(context, [entity], transition.Name, input, ct);

    if (context.Errors.Count > 0)
    {
      return new FormActionDto
      {
        Outcome = "refused",
        Warnings = context.Errors.Select(e => e.ToMessage()).ToList()
      };
    }

    return new FormActionDto
    {
      Outcome = MutationOutcome.Committed
    };
  }

  private static void CopyMatchingProperties(object source, object target)
  {
    var srcProps = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
    var tgtProps = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.CanWrite)
      .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    foreach (var sp in srcProps)
    {
      if (sp.CanRead && tgtProps.TryGetValue(sp.Name, out var tp) && tp.PropertyType.IsAssignableFrom(sp.PropertyType))
      {
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

public abstract class FormService<TEntity>(PlatformDbContext db) : FormService<TEntity, TEntity>(db)
  where TEntity : class, IEntity, new();
