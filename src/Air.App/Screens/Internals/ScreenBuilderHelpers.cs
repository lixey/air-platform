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

  public static bool IsActionPermitted(
    MethodInfo methodInfo,
    Feature feature,
    IUserContext? user,
    bool isScreenReadOnly,
    bool isAllowedInReadOnly)
  {
    if (user is not { IsAuthenticated: true })
    {
      return false;
    }

    if (isScreenReadOnly && !isAllowedInReadOnly)
    {
      var reqRight = methodInfo.GetCustomAttribute<RequireRightAttribute>(inherit: true);
      var reqAction = methodInfo.GetCustomAttribute<RequireActionAttribute>(inherit: true);
      var hasDedicatedRight = reqRight != null ||
                              reqAction != null &&
                              !string.Equals(reqAction.ActionName, nameof(EndpointAction.Edit), StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(reqAction.ActionName, nameof(EndpointAction.Delete), StringComparison.OrdinalIgnoreCase);
      if (!hasDedicatedRight)
      {
        return false;
      }
    }

    return ServiceEndpointsExtensions.IsCustomActionPermitted(methodInfo, feature, user);
  }

  public static FormAskModel BuildActionAsk(
    FormNavActionModel model,
    RecordAsk ask,
    bool hasDefaults,
    Localizer l) =>
    BuildActionAskCore(model.Label, model.Confirm, model.Color, hasDefaults, ask, l);

  public static FormAskModel BuildActionAsk(
    GridNavActionModel model,
    RecordAsk ask,
    bool hasDefaults,
    Localizer l) =>
    BuildActionAskCore(model.Label, model.Confirm, model.Color, hasDefaults, ask, l);

  private static FormAskModel BuildActionAskCore(
    string? label,
    OverlayConfirmModel? confirm,
    string? color,
    bool hasDefaults,
    RecordAsk ask,
    Localizer l) =>
    new()
    {
      Title = label,
      Message = confirm?.Message,
      Color = color,
      HasDefaults = hasDefaults,
      Elements = StatusMoveNavBuilder.LocalizeElements(ask.BuildElements(), l)
    };

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
