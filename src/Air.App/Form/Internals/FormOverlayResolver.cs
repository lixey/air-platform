namespace Platform.App.Form.Internals;

internal static class FormOverlayResolver
{
  public static bool IsCompactPresentation<TEntity>(FormService<TEntity>.Elements elements)
    where TEntity : class, IEntity, new() =>
    elements.Tabs.Count == 0 &&
    elements.Tables.Count == 0 &&
    elements.Children.All(c => c.IsFiles) &&
    elements.List.Count(e => !e.IsHidden && !e.IsRightsTree) <= 8;

  public static OverlayDto BuildResolvedOverlay<TEntity>(
    FormService<TEntity> form,
    string service,
    FormService<TEntity>.Elements elements)
    where TEntity : class, IEntity, new()
  {
    var handle = $"{service}.modal";
    var hasChildPages = elements.Children.Any(c => !c.IsFiles);
    var builder = form.GetPresentation(handle)
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

  public static (string? Width, string? Height) ResolveOverlayAsk<TEntity>(
    FormService<TEntity> form,
    FormService<TEntity>.Elements elements)
    where TEntity : class, IEntity, new()
  {
    var explicitBuilder = form.GetPresentation($"{form.Service}.modal");
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

  public static bool FillsOverlay<TEntity>(
    FormService<TEntity> form,
    FormService<TEntity>.Elements elements)
    where TEntity : class, IEntity, new()
  {
    var explicitBuilder = form.GetPresentation($"{form.Service}.modal");
    var hasChildPages = elements.Children.Any(c => !c.IsFiles);
    var presentation = explicitBuilder?.Presentation
      ?? (IsCompactPresentation(elements) ? OverlayPresentation.Popover : OverlayPresentation.Drawer);
    if (presentation != OverlayPresentation.Drawer && hasChildPages)
    {
      presentation = OverlayPresentation.Drawer;
    }

    return presentation == OverlayPresentation.Drawer ||
           explicitBuilder?.HeightValue == PanelSize.Fill ||
           form.GetOverlayAsk(elements).Height == PanelSize.Fill;
  }

  public static async Task<string> ResolveRecordCaptionAsync<TEntity>(
    FormService<TEntity> form,
    TEntity? entity,
    long? id,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var customMethod = form.GetType().GetMethod(
      "RecordCaption",
      BindingFlags.Instance | BindingFlags.NonPublic,
      [typeof(TEntity), typeof(long?)]);
    if (customMethod != null && customMethod.DeclaringType != typeof(FormService<TEntity>))
    {
      return form.InvokeRecordCaption(entity, id);
    }

    if (id is null or 0)
    {
      return FormatRecordCaption(form, entity, id, resolvedLabel: null);
    }

    string? label = null;
    if (form.State.LastElements?.RecordLabelGridType is { } gridType && entity != null)
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(gridType, form.DbContext, form.PlatformApp) is Grid.IGridService gridInst)
        {
          label = await gridInst.GetRecordLabelAsync(entity, ct);
        }
      }
      catch
      {
      }
    }

    return FormatRecordCaption(form, entity, id, label);
  }

  public static string FormatRecordCaption<TEntity>(
    FormService<TEntity> form,
    TEntity? entity,
    long? id,
    string? resolvedLabel)
    where TEntity : class, IEntity, new()
  {
    var title = form.L[form.GetRawTitle()];
    if (id is null or 0)
    {
      return form.L.TryGetString($"New {form.GetRawTitle()}", out var named) ? named : form.L["FormNewRecord", title];
    }

    var label = resolvedLabel;
    if (label == null && form.State.LastElements?.RecordLabelGridType is { } gridType && entity != null)
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(gridType, form.DbContext, form.PlatformApp) is Grid.IGridService gridInst)
        {
          label = gridInst.GetRecordLabel(entity);
        }
      }
      catch
      {
      }
    }

    return string.IsNullOrWhiteSpace(label)
      ? form.L["FormRecord", title, id]
      : form.L["FormRecordNamed", title, label];
  }
}
