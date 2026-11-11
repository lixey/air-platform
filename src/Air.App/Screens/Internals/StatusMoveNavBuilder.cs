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

  public static bool CanMove(Feature feature, IUserContext? user, string transitionName) =>
    user is { IsAuthenticated: true } &&
    !string.IsNullOrEmpty(feature.Module) &&
    (feature.TryGetRight(transitionName, out var right) && user.Can(right) ||
     !feature.IsReadOnly && user.Can(feature.Edit) ||
     user.Can(feature.All) ||
     user.Can("*"));

  public static List<FormElementDto> LocalizeElements(IReadOnlyList<FormElementDto> elements, Localizer l) =>
    [.. elements.Select(e =>
    {
      if (!string.IsNullOrEmpty(e.Label))
      {
        e.Label = l[e.Label];
      }

      return e;
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
      $"move-status/{transition.Name.ToLowerInvariant()}",
      label,
      title,
      target?.Color,
      confirm,
      ask,
      unavailableReasons);
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
          var refServiceType = el.ReferenceServiceType ?? ModuleCatalog.Instance.FindServiceType(selectModel.ReferenceService);
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
}
