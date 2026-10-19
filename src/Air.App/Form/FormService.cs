using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Air.App.Grid;
using Air.App.Internal;
using Air.App.Localization;
using Air.App.Modules;
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
  Task<ScreenDto> BuildScreenAsync(string servicePath, FormRequest request, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct) => BuildScreenAsync(servicePath, (long?)null, ct);
  Task<object?> GetModelAsync(long? id, CancellationToken ct = default);
  Task<object?> GetModelAsync(FormRequest request, CancellationToken ct = default);
  Task<object> SaveAsync(JsonElement json, CancellationToken ct = default);
  Task<object> SaveAsync(JsonElement json, FormRequest? request, CancellationToken ct = default);
  Task DeleteAsync(long id, CancellationToken ct = default);
  Task<FormActionDto> MoveStatusAsync(long id, string transitionName, JsonElement? body, int? ver = null, CancellationToken ct = default);
  Task<GridCellDto> SaveCellAsync(long id, string path, JsonElement value, FormRequest request, Func<CancellationToken, Task<object?>> readRow, CancellationToken ct = default);
  Task PopulateReferencesAsync(Dictionary<string, object?> dict, object? entity, ScreenDto screen, CancellationToken ct = default);
  Task PopulateReferencesAsync(Dictionary<string, object?> dict, object? entity, CancellationToken ct = default);
  Task<Dictionary<string, int>?> EvaluateUnavailableAsync(long? id, CancellationToken ct = default) => Task.FromResult<Dictionary<string, int>?>(null);
  void AttachPlatformApp(PlatformAppService app);
  Feature Feature { get; }
}

public abstract partial class FormService<TEntity, TInput>(PlatformDbContext db, PlatformAppService? app = null) : IFormService
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  private PlatformAppService? _app = app;
  private Elements? _lastElements;
  public void AttachPlatformApp(PlatformAppService service) => _app ??= service;

  public PlatformDbContext DbContext => db;
  protected PlatformAppService? PlatformApp => _app;
  protected Auth.IUserContext? User => _app?.User;

  public sealed class Elements : Elements<TInput> { }

  protected Localizer L => Localizer.Instance.For(Feature.Module);

  public virtual Feature Feature => new("", GetType().Name.Replace("FormService", "").ToLowerInvariant(), GetType());

  protected virtual string Title => !string.IsNullOrEmpty(Feature.Label) ? Feature.Label : (!string.IsNullOrEmpty(Feature.Name) ? Feature.Name : typeof(TEntity).Name);

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

    var label = (PropertyAccessorCache.GetValue(entity, "Number")
      ?? PropertyAccessorCache.GetValue(entity, "Name")
      ?? PropertyAccessorCache.GetValue(entity, "Title"))?.ToString();

    return string.IsNullOrWhiteSpace(label)
      ? $"{title} #{id}"
      : $"{title} {label}";
  }
}

public abstract class FormService<TEntity>(PlatformDbContext db, PlatformAppService? app = null) : FormService<TEntity, TEntity>(db, app)
  where TEntity : class, IEntity, new();
