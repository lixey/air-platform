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
  Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct) => BuildScreenAsync(servicePath, null, ct);
  Task<object?> GetModelAsync(long? id, CancellationToken ct = default);
  Task<object> SaveAsync(JsonElement json, CancellationToken ct = default);
  Task DeleteAsync(long id, CancellationToken ct = default);
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
  public sealed class Elements : Elements<TInput> { }

  protected Localizer L => Localizer.Instance;

  public virtual Feature Feature => new("", GetType().Name.Replace("FormService", "").ToLowerInvariant(), GetType());

  protected virtual string Title => typeof(TEntity).Name;

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

  protected virtual OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle).Screen(handle).Width(720);

  protected virtual string RecordCaption(TEntity? entity, long? id)
  {
    if (id is null or 0)
    {
      return $"New {Title}";
    }

    var nameProp = entity?.GetType().GetProperty("Name") ?? entity?.GetType().GetProperty("Title");
    var label = nameProp?.GetValue(entity)?.ToString();

    return string.IsNullOrWhiteSpace(label)
      ? $"{Title} #{id}"
      : $"{Title} #{id} {label}";
  }

  public virtual async Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default)
  {
    Request.Id = id;
    IsNew = (id is null or 0);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);

    var formModel = new FormModel
    {
      Service = servicePath,
      Elements = elements.List.ToList(),
      Tabs = elements.Tabs.Count > 0 ? elements.Tabs.ToList() : null,
      IsReadOnly = IsReadOnly
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
        Label = "Save & Close",
        Icon = UiIcon.Save,
        Color = UiColor.Green,
        Variant = ButtonVariant.Soft
      });

      navElements.Add(new FormNavSaveModel
      {
        Key = "save",
        Label = "Save",
        Color = UiColor.Green,
        Variant = ButtonVariant.Soft
      });

      navElements.Add(new NavCloseModel
      {
        Key = "form-close",
        Label = "Close",
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
        Label = "Close",
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
        Key = "record",
        Group = "tabs",
        Value = "record",
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
          Group = "tabs",
          Value = tab.Key,
          Label = tab.Label,
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
        Title = "Close"
      }
    };

    var nav = new NavModel
    {
      Key = $"{servicePath}.nav",
      Start = navStart,
      Elements = navElements,
      End = navEnd
    };

    var layout = PanelLayout.Column(servicePath)
      .Panel("bar", p => p.Model(nav).Auto().Pinned().Rigid())
      .Panel("form", p =>
      {
        p.Model(formModel);
        if (hasPages)
        {
          p.Auto().Pinned().PageUnder(768).PageUnderHeight(640).Resizable();
        }
        else
        {
          p.Fill();
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
          .Label(child.DeclaredLabel)
          .Fill()
          .Min(240)
        );
      }
    }

    return new ScreenDto { Layout = layout.Build() };
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
    }
    else
    {
      entity = await db.Set<TEntity>().FindAsync([id!.Value], ct)
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
