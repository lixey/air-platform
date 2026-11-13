using Platform.App.Form.Internals;
using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
{
  private ElementResolveScope CreateResolveScope() =>
    new(GetType(), Feature, db, PlatformApp, User, L, IsReadOnly);

  private async Task<FormElements<TEntity>> BuildElementsAsync(CancellationToken ct = default)
  {
    Elements = new FormElements<TEntity>();
    await SetElementsAsync(ct);
    return Elements;
  }

  private async Task<FormActions<TEntity>> BuildActionsAsync(CancellationToken ct = default)
  {
    Actions = new FormActions<TEntity>();
    await SetActionsAsync(ct);
    return Actions;
  }

  private async Task<FormNav> BuildNavAsync(CancellationToken ct = default)
  {
    Nav = new FormNav();
    await SetNavAsync(ct);
    return Nav;
  }

  private FormRules<TEntity> BuildRules()
  {
    Rules = new FormRules<TEntity>();
    SetRules();
    return Rules;
  }

  private async Task<FormElements<TEntity>> EnsureElementsAsync(CancellationToken ct = default, bool reload = false)
  {
    if (!reload && State.LastElements != null)
    {
      return Elements = State.LastElements;
    }

    await EnsureEntityAndParentLoadedAsync(ct);
    var elements = await BuildElementsAsync(ct);
    (elements.StatusFlow as IPlatformService)?.BindContext(PlatformApp);
    var parent = GetParent();
    LockBoundParentElement(elements, parent, Request.ParentId);
    await ElementResolver.ResolveAsync<TEntity>(CreateResolveScope(), elements, ct);
    State.LastElements = elements;
    return elements;
  }

  private static void LockBoundParentElement(FormElements<TEntity> elements, FormParent<TEntity> parent, long? parentId)
  {
    if (parent.BoundProperty == null || parentId is not > 0)
    {
      return;
    }

    var boundPath = Form.Elements.ToCamelPath(parent.BoundProperty.Name);
    foreach (var el in elements.List)
    {
      if (string.Equals(el.Path, boundPath, StringComparison.OrdinalIgnoreCase))
      {
        el.IsDisabled = true;
        el.IsServerOwned = true;
      }
    }
  }

  private static bool IsCompactPresentation(FormElements<TEntity> elements) =>
    elements.Tabs.Count == 0 &&
    elements.Tables.Count == 0 &&
    elements.Children.All(c => c.IsFiles) &&
    elements.List.Count(e => !e.IsHidden && !e.IsRightsTree) <= 8;

  private OverlayDto BuildResolvedOverlay(string service, FormElements<TEntity> elements)
  {
    var handle = $"{service}.modal";
    var hasChildPages = elements.Children.Any(c => !c.IsFiles);
    var builder = Presentation(handle)
      ?? (IsCompactPresentation(elements) ? OverlayModel.Popover(handle) : OverlayModel.Drawer(handle));
    var dto = builder.Screen(service).Build();
    if (dto.Presentation != OverlayPresentation.Drawer)
    {
      if (hasChildPages)
      {
        dto.Presentation = OverlayPresentation.Drawer;
        dto.Width = null;
        dto.Height = null;
      }
      else if (dto.Presentation == OverlayPresentation.Popover && dto.Width is null)
      {
        dto.Width = PanelSize.Px(720);
      }
    }

    return dto;
  }

  public virtual async Task<OverlayDto> OverlayAsync(string service, CancellationToken ct = default) =>
    BuildResolvedOverlay(service, await ProbeElementsForOverlayAsync(ct));

  public virtual OverlayDto Overlay(string service) =>
    BuildResolvedOverlay(service, State.LastElements ?? ProbeElementsSafeSync());

  protected virtual (string? Width, string? Height) OverlayAsk(FormElements<TEntity> elements)
  {
    var explicitBuilder = Presentation($"{Service}.modal");
    var hasChildPages = elements.Children.Any(c => !c.IsFiles);
    var presentation = explicitBuilder?.Presentation
      ?? (IsCompactPresentation(elements) ? OverlayPresentation.Popover : OverlayPresentation.Drawer);
    if (presentation != OverlayPresentation.Drawer && hasChildPages)
    {
      presentation = OverlayPresentation.Drawer;
    }

    if (presentation is not (OverlayPresentation.Dialog or OverlayPresentation.Popover) ||
        explicitBuilder?.WidthValue is not null ||
        explicitBuilder?.HeightValue is not null)
    {
      return (null, null);
    }

    return hasChildPages ? (PanelSize.Fill, PanelSize.Fill) : (PanelSize.Px(720), null);
  }

  protected virtual bool FillsOverlay(FormElements<TEntity> elements)
  {
    var explicitBuilder = Presentation($"{Service}.modal");
    var hasChildPages = elements.Children.Any(c => !c.IsFiles);
    var presentation = explicitBuilder?.Presentation
      ?? (IsCompactPresentation(elements) ? OverlayPresentation.Popover : OverlayPresentation.Drawer);
    if (presentation != OverlayPresentation.Drawer && hasChildPages)
    {
      presentation = OverlayPresentation.Drawer;
    }

    return presentation == OverlayPresentation.Drawer ||
           explicitBuilder?.HeightValue == PanelSize.Fill ||
           OverlayAsk(elements).Height == PanelSize.Fill;
  }

  protected virtual string RecordCaption(TEntity? entity, long? id) =>
    FormatRecordCaption(entity, id, resolvedLabel: null);

  protected virtual async Task<string> RecordCaptionAsync(TEntity? entity, long? id, CancellationToken ct = default)
  {
    var customMethod = GetType().GetMethod(
      nameof(RecordCaption),
      BindingFlags.Instance | BindingFlags.NonPublic,
      [typeof(TEntity), typeof(long?)]);
    if (customMethod != null && customMethod.DeclaringType != typeof(FormService<TEntity>))
    {
      return RecordCaption(entity, id);
    }

    if (id is null or 0)
    {
      return FormatRecordCaption(entity, id, resolvedLabel: null);
    }

    string? label = null;
    if (State.LastElements?.RecordLabelGridType is { } gridType && entity != null)
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(gridType, db, PlatformApp) is Grid.IGridService gridInst)
        {
          label = await gridInst.GetRecordLabelAsync(entity, ct);
        }
      }
      catch
      {
      }
    }

    return FormatRecordCaption(entity, id, label);
  }

  private string FormatRecordCaption(TEntity? entity, long? id, string? resolvedLabel)
  {
    var rawTitle = Title;
    var title = L[rawTitle];
    if (id is null or 0)
    {
      return L.TryGetString($"New {rawTitle}", out var named) ? named : L["FormNewRecord", title];
    }

    var label = resolvedLabel;
    if (label == null && State.LastElements?.RecordLabelGridType is { } gridType && entity != null)
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(gridType, db, PlatformApp) is Grid.IGridService gridInst)
        {
          label = gridInst.GetRecordLabel(entity);
        }
      }
      catch
      {
      }
    }

    return string.IsNullOrWhiteSpace(label)
      ? L["FormRecord", title, id]
      : L["FormRecordNamed", title, label];
  }

  public string GetTitle() => L[Title];
  public string GetRecordCaption(object? entity, long? id) => RecordCaption(entity as TEntity ?? State.LastSavedEntity, id);
  public Task<string> GetRecordCaptionAsync(object? entity, long? id, CancellationToken ct = default) =>
    RecordCaptionAsync(entity as TEntity ?? State.LastSavedEntity, id, ct);

  private async Task<IReadOnlyList<ChildGridBuilder>> GetSumChildrenAsync(CancellationToken ct)
  {
    if (State.LastElements != null)
    {
      return State.LastElements.Children.Where(c => !c.IsFiles && c.SumMappings.Count > 0).ToList();
    }

    var probe = await ProbeElementsSafeAsync(ct);
    return probe.Children.Where(c => !c.IsFiles && c.SumMappings.Count > 0).ToList();
  }

  private FormElements<TEntity> ProbeElementsSafeSync()
  {
    Elements = new FormElements<TEntity>();
    var probe = Elements;
    try
    {
      SetElementsAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
    catch
    {
      if (probe.List.Count == 0 && probe.Children.Count == 0)
      {
        try
        {
          SetElements();
        }
        catch
        {
        }
      }
    }

    return probe;
  }

  private async Task<FormElements<TEntity>> ProbeElementsSafeAsync(CancellationToken ct)
  {
    Elements = new FormElements<TEntity>();
    var probe = Elements;
    try
    {
      await SetElementsAsync(ct);
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch
    {
      if (probe.List.Count == 0 && probe.Children.Count == 0)
      {
        try
        {
          SetElements();
        }
        catch
        {
        }
      }
    }

    return probe;
  }

  private async Task<FormElements<TEntity>> ProbeElementsForOverlayAsync(CancellationToken ct = default)
  {
    if (State.LastElements is not null)
    {
      return State.LastElements;
    }

    if (db != null)
    {
      await EnsureEntityAndParentLoadedAsync(ct);
    }

    return await ProbeElementsSafeAsync(ct);
  }

  private FormActions<TEntity> ProbeActions()
  {
    Actions = new FormActions<TEntity>();
    var actions = Actions;
    try
    {
      SetActionsAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
    catch (Exception ex) when (db == null || ex is ParentNotLoadedException)
    {
      if (actions.Entries.Count == 0)
      {
        try
        {
          SetActions();
        }
        catch (ParentNotLoadedException)
        {
        }
      }

      var overridesAsync = GetType()
        .GetMethod(nameof(SetActionsAsync), BindingFlags.Instance | BindingFlags.NonPublic)?
        .DeclaringType != typeof(FormService<TEntity>);
      if (db == null && overridesAsync && actions.Entries.Count == 0)
      {
        throw new InvalidOperationException(
          $"{GetType().Name}.{nameof(SetActionsAsync)} accessed null runtime dependencies during startup action discovery. Guard runtime-only queries with 'if (CurrentEntity != null)' or 'if (db != null)' before registering actions.",
          ex);
      }
    }

    return actions;
  }

  private async Task<FormActions<TEntity>> ProbeActionsAsync(CancellationToken ct)
  {
    Actions = new FormActions<TEntity>();
    var actions = Actions;
    try
    {
      await SetActionsAsync(ct);
    }
    catch (Exception ex) when (db == null || ex is ParentNotLoadedException)
    {
      if (actions.Entries.Count == 0)
      {
        try
        {
          SetActions();
        }
        catch (ParentNotLoadedException)
        {
        }
      }
    }

    return actions;
  }

  public async Task RegisterMetadataAsync(CancellationToken ct = default)
  {
    if (db != null)
    {
      await GetElementsMetadataAsync(ct);
    }
    else
    {
      var probe = await ProbeElementsSafeAsync(ct);
      foreach (var el in probe.List)
      {
        if (el.ReferenceServiceType != null)
        {
          ReferenceLookupAllowlist.RegisterReferrer(el.ReferenceServiceType, GetType());
        }
      }

      (probe.StatusFlow as IRecordStatuses)?.RegisterMetadata();
    }

    (State.LastElements?.StatusFlow as IRecordStatuses)?.RegisterMetadata();

    foreach (var entry in (await ProbeActionsAsync(ct)).Entries)
    {
      entry.Ask?.ValidateConfiguration(GetType());
    }
  }

  public async Task<(IReadOnlyList<FormElementDto> Elements, IReadOnlyList<ChildGridBuilder> Children)> GetElementsMetadataAsync(CancellationToken ct = default)
  {
    var elements = await EnsureElementsAsync(ct, reload: true);
    if (Feature.IsReadOnly)
    {
      foreach (var child in elements.Children)
      {
        child.ReadOnly();
      }
    }

    return (elements.List, elements.Children);
  }

  async Task<bool> IFormService.HasStatusFlowAsync(CancellationToken ct) =>
    (await ProbeElementsSafeAsync(ct)).StatusFlow is IRecordStatuses<TEntity>;

  IEnumerable<string> IPlatformService.GetDeclaredActions() =>
    ProbeActions().Entries
      .Select(e => e.Permission.GetDeclaredActionName(e.Handle))
      .Where(n => !string.IsNullOrWhiteSpace(n))!;

  IReadOnlyList<string> IFormService.GetRoutableActionMethods(ModuleCatalog catalog)
  {
    var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in ProbeActions().Entries)
    {
      routes.Add(entry.Method);
      routes.Add(entry.Key);
    }

    StatusMoveNavBuilder.AddStatusAndFeatureRoutes(
      routes,
      ProbeElementsSafeSync().StatusFlow as IRecordStatuses,
      Feature,
      typeof(TEntity),
      catalog);
    return [.. routes];
  }

  void IPlatformService.ValidateConfiguration(ModuleCatalog catalog)
  {
    var formType = GetType();
    ServiceOverrideValidator.EnsureSingleSyncOrAsyncOverride(
      formType,
      typeof(FormService<>),
      (nameof(SetElements), nameof(SetElementsAsync)),
      (nameof(SetActions), nameof(SetActionsAsync)),
      (nameof(SetNav), nameof(SetNavAsync)));

    _ = GetParent();

    var probe = ProbeElementsSafeSync();
    FormConstraintExtractor.ValidateNavigationElements(typeof(TEntity), probe.List);

    if (probe.RecordLabelGridType != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(formType, probe.RecordLabelGridType, "UseRecordLabelOf");
    }

    if (probe.StatusFlow != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(formType, probe.StatusFlow.GetType(), "UseStatusOf");
    }

    foreach (var child in probe.Children)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(formType, child.ServiceType, $"AddChild '{child.DeclaredKey}'");
    }

    foreach (var el in probe.List)
    {
      if (el.ReferenceServiceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(formType, el.ReferenceServiceType, $"AsReference on '{el.Path}'");
        ReferenceLookupAllowlist.RegisterReferrer(el.ReferenceServiceType, formType);
      }

      if (el.CreateFormServiceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(formType, el.CreateFormServiceType, $"AllowCreate on '{el.Path}'");
      }
    }

    var actions = ProbeActions();
    var seenHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in actions.Entries)
    {
      if (!seenHandles.Add(entry.Handle))
      {
        throw new InvalidOperationException(
          $"Form '{formType.FullName}' declares duplicate action handle '{entry.Handle}'.");
      }

      entry.ValidateConfiguration(formType, Feature);
    }
  }

  void IFormService.ValidateInheritedGridActions(Type gridType)
  {
    foreach (var entry in ProbeActions().Entries)
    {
      if (entry.ConfirmProvider != null && string.IsNullOrEmpty(entry.ConfirmMessage) && entry.Ask == null)
      {
        throw new InvalidOperationException(
          $"Form action '{entry.Handle}' on '{GetType().FullName}' uses ConfirmAsync without a static fallback confirm message, so it cannot be inherited by grid '{gridType.FullName}' via UseActionsOf. Pass gridConfirm to ConfirmAsync(..., gridConfirm: \"...\") or chain .Confirm(\"...\") first.");
      }
    }
  }
}
