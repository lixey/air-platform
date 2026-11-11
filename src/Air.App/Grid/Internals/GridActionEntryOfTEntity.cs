using Platform.App.Screens.Internals;

namespace Platform.App.Grid.Internals;

internal sealed class GridActionEntry<TEntity>(string handle, ActionPermission permission)
  where TEntity : class, IEntity
{
  public string Handle { get; } = handle;
  public string Key { get; } = handle.ToLowerInvariant();
  public string Method { get; } = ServicePathResolver.ToKebabCase(handle);
  public ActionPermission Permission { get; } = permission;

  public string? LabelText { get; set; }
  public string? TitleText { get; set; }
  public string? IconHandle { get; set; }
  public string? ColorHandle { get; set; }
  public string? VariantHandle { get; set; }
  public string? ConfirmMessage { get; set; }
  public bool IsInMenu { get; set; }
  public bool IsAllowedInReadOnly { get; set; }
  public bool NeedsSelection { get; set; } = true;
  public bool IgnoresSelection { get; set; }
  public bool IsSingle { get; set; }
  public bool IsWithoutTransaction { get; set; }

  public RecordAsk? Ask { get; set; }
  public Func<IReadOnlyList<long>, CancellationToken, Task<object?>>? DefaultProvider { get; set; }
  public List<RecordAvailability> Offers { get; } = [];
  public List<(Func<GridBatchContext<TEntity>, IReadOnlyList<TEntity>, object?, Localizer, CancellationToken, Task<bool>> Guard, string Refusal, string? Path)> Guards { get; } = [];
  public Func<GridBatchContext<TEntity>, object?, CancellationToken, Task>? Handler { get; set; }

  public bool Matches(string actionKey) =>
    string.Equals(Handle, actionKey, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(Key, actionKey, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(Method, actionKey, StringComparison.OrdinalIgnoreCase);

  public void ValidateConfiguration(Type hostServiceType, Feature hostFeature)
  {
    Permission.ValidateConfiguration(hostServiceType, hostFeature, Handle);
    if (Handler == null)
    {
      throw new InvalidOperationException(
        $"Grid action '{Handle}' in '{hostServiceType.FullName}' must declare a handler via .Do(...).");
    }

    Ask?.ValidateConfiguration(hostServiceType);
  }

  public async Task<GridNavActionModel> BuildModelAsync(
    Type ownerServiceType,
    Feature feature,
    string servicePath,
    PlatformDbContext db,
    PlatformAppService? app,
    IUserContext user,
    Localizer l,
    CancellationToken ct)
  {
    var rawLabel = LabelText ?? Columns.PathToLabel(Handle);
    var color = ColorHandle ?? UiColor.Blue;
    var confirm = !string.IsNullOrEmpty(ConfirmMessage)
      ? new OverlayConfirmModel { Message = l[ConfirmMessage], Color = ColorHandle ?? UiColor.Red }
      : null;

    var model = new GridNavActionModel
    {
      Service = servicePath,
      Key = Key,
      Method = Method,
      Label = l[rawLabel],
      Title = TitleText != null ? l[TitleText] : null,
      Icon = IconHandle,
      Color = color,
      Variant = VariantHandle ?? ButtonVariant.Soft,
      NeedsSelection = NeedsSelection,
      IsSingle = IsSingle,
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
