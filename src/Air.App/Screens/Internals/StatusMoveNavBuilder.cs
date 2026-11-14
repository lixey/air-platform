using Platform.App.Form.Internals;
using Platform.App.Internals;

namespace Platform.App.Screens.Internals;

internal static class StatusMoveNavBuilder
{
  public static string? ExtractStatusMemberName(LambdaExpression statusMember) =>
    statusMember.Body switch
    {
      MemberExpression memberExpr => memberExpr.Member.Name,
      UnaryExpression { Operand: MemberExpression ueMember } => ueMember.Member.Name,
      _ => null
    };

  public static string ResolveStatusPath(LambdaExpression statusMember) =>
    ExtractStatusMemberName(statusMember) is { Length: > 0 } name
      ? char.ToLowerInvariant(name[0]) + name[1..]
      : "docStatusId";

  public static bool CanMove(Feature? feature, IUserContext? user, string transitionName, Right? explicitRight = null) =>
    Feature.CanMove(feature, user, transitionName, explicitRight);

  public static bool CanMoveOnScreen(
    Feature? feature,
    IUserContext? user,
    RecordTransition transition,
    bool isServiceLockedReadOnly,
    bool allowReadOnly,
    bool isParentReadOnly,
    bool isReadOnlyEntity)
  {
    if (isReadOnlyEntity || (isServiceLockedReadOnly || isParentReadOnly) && !allowReadOnly)
    {
      return false;
    }

    return Feature.CanMove(feature, user, transition.Name, transition.RequiredRight);
  }

  public static List<FormElementDto> LocalizeElements(IReadOnlyList<FormElementDto> elements, Localizer l) =>
    [.. elements.Select(e =>
    {
      if (string.IsNullOrEmpty(e.Label))
      {
        return e;
      }

      var clone = ElementResolver.CloneElement(e, label: l[e.Label]);
      FormElementBinding.Link(e, clone);
      return clone;
    })];

  public static async Task<(
    string Key,
    string Method,
    string Label,
    string? Title,
    string? Color,
    OverlayConfirmModel? Confirm,
    FormAskModel? Ask,
    List<string>? UnavailableReasons)> BuildTransitionMetaAsync<TEntity>(
      Type ownerServiceType,
      Feature feature,
      PlatformDbContext db,
      PlatformAppService? app,
      IUserContext user,
      Localizer l,
      IRecordStatuses<TEntity> statusFlow,
      RecordTransition transition,
      CancellationToken ct = default)
    where TEntity : class, IEntity
  {
    var target = statusFlow.Statuses.FirstOrDefault(s => s.Id == transition.To);
    var verbLabel = !string.IsNullOrEmpty(feature.Module)
      ? l.TextOr($"{feature.Module}: {transition.Name}", l[transition.Name])
      : l[transition.Name];
    var targetLabel = target != null ? l[target.Name] : verbLabel;
    var label = !string.IsNullOrEmpty(transition.Label)
      ? l[transition.Label]
      : (verbLabel != transition.Name ? verbLabel : targetLabel);
    var title = !string.IsNullOrEmpty(transition.Title)
      ? l[transition.Title]
      : (target?.Title != null ? l[target.Title] : null);
    var confirmMessage = transition.Confirm != null ? l[transition.Confirm] : null;
    var confirm = transition.Ask is not null || confirmMessage is null
      ? null
      : new OverlayConfirmModel { Message = confirmMessage, Color = target?.Color };
    FormAskModel? ask = null;
    if (transition.Ask != null)
    {
      var resolvedElements = await transition.Ask.BuildResolvedElementsAsync(ownerServiceType, feature, db, app, user, l, ct);
      ask = new FormAskModel
      {
        Title = label,
        Message = confirmMessage,
        Color = target?.Color,
        Elements = [.. resolvedElements]
      };
    }

    var unavailableReasons = transition.Offers.Count == 0
      ? null
      : transition.Offers.Select(o => l[o.Reason]).ToList();

    return (
      $"status.{transition.Name.ToLowerInvariant()}",
      BuildMoveStatusMethod(transition.Name),
      label,
      title,
      target?.Color,
      confirm,
      ask,
      unavailableReasons);
  }

  public const string MoveStatusPrefix = "move-status-";

  public static string BuildMoveStatusMethod(string transitionName) =>
    $"{MoveStatusPrefix}{ServicePathResolver.ToKebabCase(transitionName)}";

  public static bool TryParseMoveStatusEndpoint(string method, out string transitionKey)
  {
    if (!string.IsNullOrWhiteSpace(method) &&
        method.StartsWith(MoveStatusPrefix, StringComparison.OrdinalIgnoreCase) &&
        method.Length > MoveStatusPrefix.Length)
    {
      transitionKey = method[MoveStatusPrefix.Length..];
      return true;
    }

    transitionKey = string.Empty;
    return false;
  }

  public static bool MatchesTransition(string declaredName, string requestedName) =>
    string.Equals(declaredName, requestedName, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(ServicePathResolver.ToKebabCase(declaredName), requestedName, StringComparison.OrdinalIgnoreCase);

  public static string ExtractActionMethodKey(JsonElement? body) =>
    body is { ValueKind: JsonValueKind.Object } root &&
    root.TryGetProperty("method", out var mProp) &&
    mProp.ValueKind == JsonValueKind.String
      ? mProp.GetString() ?? string.Empty
      : string.Empty;

  public static ActionInputDto ToActionInputDto(object? defaults)
  {
    if (defaults is Dictionary<string, object?> dict)
    {
      return new ActionInputDto { Value = dict };
    }

    if (defaults is null)
    {
      return new ActionInputDto { Value = [] };
    }

    var json = JsonSerializer.Serialize(defaults, PlatformJsonOptions.DefaultOptions);
    var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, PlatformJsonOptions.DefaultOptions) ?? [];
    return new ActionInputDto { Value = parsed };
  }

  public static async Task<Dictionary<string, object?>> ResolveActionDefaultsAsync(
    Func<CancellationToken, Task<object?>>? defaultProvider,
    RecordAsk? ask,
    PlatformDbContext db,
    PlatformAppService? app,
    CancellationToken ct)
  {
    if (defaultProvider == null)
    {
      return [];
    }

    var rawDefaults = await defaultProvider(ct);
    if (rawDefaults == null)
    {
      return [];
    }

    var jsonText = JsonSerializer.Serialize(rawDefaults, PlatformJsonOptions.DefaultOptions);
    var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonText) ?? [];
    if (ask != null)
    {
      foreach (var el in ask.BuildElements())
      {
        if (string.IsNullOrEmpty(el.ReferencePath) ||
            el.Model is not FormSelectModel selectModel ||
            string.IsNullOrEmpty(selectModel.ReferenceService))
        {
          continue;
        }

        if (dict.TryGetValue(el.Path, out var rawVal) &&
            rawVal is JsonElement { ValueKind: JsonValueKind.Number } je &&
            je.TryGetInt64(out var refId) &&
            refId > 0)
        {
          var refServiceType = FormElementBinding.TryGet(el)?.ReferenceServiceType
            ?? ModuleCatalog.Instance.FindServiceType(selectModel.ReferenceService);
          if (ModuleCatalog.GetServiceEntityType(refServiceType) is { } refEntityType)
          {
            var refGrid = refServiceType != null
              ? ModuleCatalog.Instance.CreateServiceInstance(refServiceType, db, app) as IGridService
              : null;
            var loadedById = await ReferenceCaptionResolver.LoadEntitiesByIdsAsync(
              db,
              refEntityType,
              [refId],
              selectModel.ReferenceLabelPaths,
              refGrid,
              ct);
            var name = loadedById.TryGetValue(refId, out var loaded)
              ? ReferenceCaptionResolver.ReadDisplayCaption(loaded, selectModel.ReferenceLabelPaths, refGrid)
              : null;
            dict[el.ReferencePath] = new Reference { Id = refId, Name = !string.IsNullOrEmpty(name) ? name : $"#{refId}" };
          }
        }
      }
    }

    return dict;
  }

  public static void AddStatusAndFeatureRoutes(
    HashSet<string> routes,
    IRecordStatuses? directFlow,
    Feature? feature,
    Type entityType,
    ModuleCatalog catalog)
  {
    if (directFlow is not null)
    {
      AddFlowTransitionRoutes(routes, directFlow);
    }

    foreach (var svcType in catalog.AllServiceTypes)
    {
      if (!typeof(IRecordStatuses).IsAssignableFrom(svcType) ||
          ModuleCatalog.GetServiceEntityType(svcType) != entityType)
      {
        continue;
      }

      if (catalog.CreateServiceInstance(svcType) is IRecordStatuses flowProbe &&
          (feature is null || (flowProbe as IPlatformService)?.Feature == feature))
      {
        AddFlowTransitionRoutes(routes, flowProbe);
      }
    }

    if (feature is null)
    {
      return;
    }

    foreach (var right in feature.Rights.Values)
    {
      if (!right.IsPattern && !Right.StandardActions.Contains(right.Action))
      {
        var kebab = ServicePathResolver.ToKebabCase(right.Action);
        var lower = right.Action.ToLowerInvariant();
        routes.Add(kebab);
        routes.Add(lower);
        routes.Add(BuildMoveStatusMethod(right.Action));
        routes.Add($"{MoveStatusPrefix}{lower}");
      }
    }
  }

  private static void AddFlowTransitionRoutes(HashSet<string> routes, IRecordStatuses flow)
  {
    foreach (var transition in flow.AllTransitions)
    {
      if (!string.IsNullOrWhiteSpace(transition.Name))
      {
        routes.Add(BuildMoveStatusMethod(transition.Name));
        routes.Add($"{MoveStatusPrefix}{transition.Name.ToLowerInvariant()}");
      }
    }
  }
}
