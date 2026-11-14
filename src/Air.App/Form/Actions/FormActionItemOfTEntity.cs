using Platform.App.Screens.Internals;

namespace Platform.App.Form.Actions;

public sealed class FormActionItem<TEntity>
  where TEntity : class, IEntity
{
  internal FormActionItem(string handle, ActionPermission permission)
  {
    Handle = handle;
    Key = handle.ToLowerInvariant();
    Method = ServicePathResolver.ToKebabCase(handle);
    Permission = permission;
  }

  public string Handle { get; }
  public string Key { get; }
  public string Method { get; }
  internal ActionPermission Permission { get; }

  public string? LabelText { get; set; }
  public string? TitleText { get; set; }
  public string? IconHandle { get; set; }
  public string? ColorHandle { get; set; }
  public string? VariantHandle { get; set; }
  public string? ConfirmMessage { get; set; }
  internal Func<TEntity, CancellationToken, Task<string?>>? ConfirmProvider { get; set; }
  public bool IsInMenu { get; set; }
  public bool IsAllowedInReadOnly { get; set; }
  internal RecordAsk? Ask { get; set; }
  internal Func<TEntity?, CancellationToken, Task<object?>>? DefaultProvider { get; set; }
  internal List<RecordAvailability> Offers { get; } = [];
  internal List<(Func<FormActionContext<TEntity>, TEntity, object?, CancellationToken, Task<bool>> Guard, string Refusal, string? Path)> Guards { get; } = [];
  internal Func<FormActionContext<TEntity>, TEntity, object?, CancellationToken, Task>? Handler { get; set; }

  public bool Matches(string actionKey) =>
    string.Equals(Handle, actionKey, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(Key, actionKey, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(Method, actionKey, StringComparison.OrdinalIgnoreCase);

  internal void ValidateConfiguration(Type hostServiceType, Feature hostFeature)
  {
    Permission.ValidateConfiguration(hostServiceType, hostFeature, Handle);
    if (Handler == null)
    {
      throw new InvalidOperationException(
        $"Form action '{Handle}' in '{hostServiceType.FullName}' must declare a handler via .Do(...).");
    }

    Ask?.ValidateConfiguration(hostServiceType);
  }

  internal async Task<FormNavActionModel> BuildModelAsync(
    Type ownerServiceType,
    Feature feature,
    string servicePath,
    TEntity entity,
    PlatformDbContext db,
    PlatformAppService? app,
    IUserContext user,
    Localizer l,
    CancellationToken ct)
  {
    var rawLabel = LabelText ?? Grid.Columns.PathToLabel(Handle);
    var color = ColorHandle ?? UiColor.Blue;
    var resolvedConfirm = ConfirmProvider != null
      ? await ConfirmProvider(entity, ct)
      : ConfirmMessage;
    var confirm = !string.IsNullOrEmpty(resolvedConfirm)
      ? new OverlayConfirmModel { Message = l[resolvedConfirm], Color = ColorHandle ?? UiColor.Red }
      : null;

    var model = new FormNavActionModel
    {
      Service = servicePath,
      Key = Key,
      Method = Method,
      Label = l[rawLabel],
      Title = TitleText != null ? l[TitleText] : null,
      Icon = IconHandle,
      Color = color,
      Variant = VariantHandle ?? ButtonVariant.Soft,
      Confirm = confirm,
      UnavailableReasons = Offers.Count > 0 ? Offers.Select(o => l[o.Reason]).ToList() : null
    };

    if (Ask != null)
    {
      model.Ask = await ScreenBuilderHelpers.BuildActionAskAsync(
        ownerServiceType,
        feature,
        db,
        app,
        user,
        model,
        Ask,
        DefaultProvider != null,
        l,
        ct);
      model.Confirm = null;
    }

    return model;
  }

  internal async Task<GridNavActionModel> BuildGridModelAsync(
    Type ownerServiceType,
    Feature feature,
    string gridServicePath,
    PlatformDbContext db,
    PlatformAppService? app,
    IUserContext user,
    Localizer l,
    CancellationToken ct)
  {
    var rawLabel = LabelText ?? Grid.Columns.PathToLabel(Handle);
    var color = ColorHandle ?? UiColor.Blue;
    var confirm = !string.IsNullOrEmpty(ConfirmMessage)
      ? new OverlayConfirmModel { Message = l[ConfirmMessage], Color = ColorHandle ?? UiColor.Red }
      : null;

    var model = new GridNavActionModel
    {
      Service = gridServicePath,
      Key = Key,
      Method = Method,
      Label = l[rawLabel],
      Title = TitleText != null ? l[TitleText] : null,
      Icon = IconHandle,
      Color = color,
      Variant = VariantHandle ?? ButtonVariant.Soft,
      NeedsSelection = true,
      IsSingle = true,
      Confirm = confirm,
      UnavailableReasons = Offers.Count > 0 ? Offers.Select(o => l[o.Reason]).ToList() : null
    };

    if (Ask != null)
    {
      model.Ask = await ScreenBuilderHelpers.BuildActionAskAsync(
        ownerServiceType,
        feature,
        db,
        app,
        user,
        model,
        Ask,
        DefaultProvider != null,
        l,
        ct);
      model.Confirm = null;
    }

    return model;
  }
}
