using Platform.App.Host.Internals;
using Platform.App.Internals;

namespace Platform.App.Form.Internals;

internal static class FormMetadataValidator
{
  public static void ValidateConfiguration<TEntity>(FormService<TEntity> form)
    where TEntity : class, IEntity, new()
  {
    ServiceOverrideValidator.EnsureSingleSyncOrAsyncOverride(
      form.GetType(),
      typeof(FormService<>),
      ("SetElements", "SetElementsAsync"),
      ("SetActions", "SetActionsAsync"));

    _ = form.GetParentDeclaration();

    var probe = new FormService<TEntity>.Elements();
    try
    {
      form.InvokeSetElements(probe);
    }
    catch (ParentNotLoadedException)
    {
    }

    if (form.DbContext == null && probe.List.Count == 0 && probe.Children.Count == 0)
    {
      probe = form.ProbeElementsSafeSync();
    }

    FormConstraintExtractor.ValidateNavigationElements(typeof(TEntity), probe.List);

    if (probe.RecordLabelGridType != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(form.GetType(), probe.RecordLabelGridType, "UseRecordLabelOf");
    }

    if (probe.StatusFlow != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(form.GetType(), probe.StatusFlow.GetType(), "UseStatusOf");
    }

    foreach (var child in probe.Children)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(form.GetType(), child.ServiceType, $"AddChild '{child.DeclaredKey}'");
    }

    foreach (var el in probe.List)
    {
      if (el.ReferenceServiceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(form.GetType(), el.ReferenceServiceType, $"AsReference on '{el.Path}'");
        ReferenceLookupAllowlist.RegisterReferrer(el.ReferenceServiceType, form.GetType());
      }

      if (el.CreateFormServiceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(form.GetType(), el.CreateFormServiceType, $"AllowCreate on '{el.Path}'");
      }
    }

    var actions = new FormService<TEntity>.Actions();
    try
    {
      form.InvokeSetActionsAsync(actions, CancellationToken.None).GetAwaiter().GetResult();
    }
    catch (Exception ex) when (form.DbContext == null || ex is ParentNotLoadedException)
    {
      if (actions.Entries.Count == 0)
      {
        try
        {
          form.InvokeSetActions(actions);
        }
        catch (ParentNotLoadedException)
        {
        }
      }
    }

    var seenHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in actions.Entries)
    {
      if (!seenHandles.Add(entry.Handle))
      {
        throw new InvalidOperationException(
          $"Form '{form.GetType().FullName}' declares duplicate action handle '{entry.Handle}'.");
      }

      entry.ValidateConfiguration(form.GetType(), form.Feature);
    }
  }

  public static void ValidateInheritedGridActions<TEntity>(FormService<TEntity> form, Type gridType)
    where TEntity : class, IEntity, new()
  {
    foreach (var entry in form.ProbeActions().Entries)
    {
      if (entry.ConfirmProvider != null && string.IsNullOrEmpty(entry.ConfirmMessage) && entry.Ask == null)
      {
        throw new InvalidOperationException(
          $"Form action '{entry.Handle}' on '{form.GetType().FullName}' uses ConfirmAsync without a static fallback confirm message, so it cannot be inherited by grid '{gridType.FullName}' via UseActionsOf. Pass gridConfirm to ConfirmAsync(..., gridConfirm: \"...\") or chain .Confirm(\"...\") first.");
      }
    }
  }

  public static IReadOnlyList<string> GetRoutableActionMethods<TEntity>(FormService<TEntity> form)
    where TEntity : class, IEntity, new()
  {
    var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in form.ProbeActions().Entries)
    {
      routes.Add(entry.Method);
      routes.Add(entry.Key);
    }

    AppServiceRouteMapper.AddFeatureCustomRightRoutes(routes, form.Feature);
    return [.. routes];
  }
}
