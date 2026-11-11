namespace Platform.App.Screens.Internals;

internal static class ScreenBuilderHelpers
{
  public static MutationTextsDto BuildMutationTexts(Localizer l) =>
    new()
    {
      ConflictTitle = l["MutationConflict"],
      ReloadLabel = l["MutationReload"],
      UnknownTitle = l["MutationUnknown"],
      UnknownDescription = l["MutationUnknownDescription"],
      VerifyLabel = l["MutationVerify"],
      CommittedWarningTitle = l["MutationCommittedWarning"],
      SelectionCorrectionLabel = l["MutationSelectionCorrection"]
    };

  public static string ResolveFieldLabel(Localizer l, Feature? feature, string? path, string fallbackLabel) =>
    feature != null && !string.IsNullOrEmpty(path)
      ? l.TextOr($"{feature.Handle}.{path}",
          l.TextOr($"{feature.Label}.{path}",
            l.TextOr($"{feature.Name}.{path}", l[fallbackLabel])))
      : l[fallbackLabel];

  public static List<OptionDto>? LocalizeOptions(Localizer l, IReadOnlyList<OptionDto>? options) =>
    options is { Count: > 0 }
      ? options
        .Select(opt => string.IsNullOrEmpty(opt.Name)
          ? opt
          : new OptionDto
          {
            Id = opt.Id,
            Name = l[opt.Name],
            Color = opt.Color,
            Title = opt.Title != null ? l[opt.Title] : null
          })
        .ToList()
      : options?.ToList();

  public static Task<FormAskModel> BuildActionAskAsync(
    Type ownerServiceType,
    Feature feature,
    PlatformDbContext db,
    PlatformAppService? app,
    IUserContext user,
    FormNavActionModel model,
    RecordAsk ask,
    bool hasDefaults,
    Localizer l,
    CancellationToken ct = default) =>
    BuildActionAskCoreAsync(ownerServiceType, feature, db, app, user, model.Label, model.Confirm, model.Color, hasDefaults, ask, l, ct);

  public static Task<FormAskModel> BuildActionAskAsync(
    Type ownerServiceType,
    Feature feature,
    PlatformDbContext db,
    PlatformAppService? app,
    IUserContext user,
    GridNavActionModel model,
    RecordAsk ask,
    bool hasDefaults,
    Localizer l,
    CancellationToken ct = default) =>
    BuildActionAskCoreAsync(ownerServiceType, feature, db, app, user, model.Label, model.Confirm, model.Color, hasDefaults, ask, l, ct);

  private static async Task<FormAskModel> BuildActionAskCoreAsync(
    Type ownerServiceType,
    Feature feature,
    PlatformDbContext db,
    PlatformAppService? app,
    IUserContext user,
    string? label,
    OverlayConfirmModel? confirm,
    string? color,
    bool hasDefaults,
    RecordAsk ask,
    Localizer l,
    CancellationToken ct)
  {
    var elements = await ask.BuildResolvedElementsAsync(ownerServiceType, feature, db, app, user, l, ct);
    return new FormAskModel
    {
      Title = label,
      Message = confirm?.Message,
      Color = color,
      HasDefaults = hasDefaults,
      Elements = [.. elements]
    };
  }

  public static void AppendMenuAndInlineActions(
    List<NavElementModel> target,
    IEnumerable<(NavElementModel Model, bool IsInMenu)> permittedActions,
    Localizer l)
  {
    var list = permittedActions as IReadOnlyList<(NavElementModel Model, bool IsInMenu)> ?? permittedActions.ToList();
    var inMenuActions = list.Where(a => a.IsInMenu).Select(a => a.Model).ToList();
    if (inMenuActions.Count > 0)
    {
      target.Add(new NavMenuModel
      {
        Key = "actions",
        Label = l["Actions"],
        Variant = ButtonVariant.Soft,
        Elements = inMenuActions
      });
    }

    foreach (var (model, _) in list.Where(a => !a.IsInMenu))
    {
      target.Add(model);
    }
  }
}
