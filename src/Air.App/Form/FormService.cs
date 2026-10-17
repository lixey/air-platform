using System.Text.Json;
using Air.App.Components;
using Air.App.Modules;
using Air.App.Nav;
using Air.App.Overlay;
using Air.App.Panels;
using Air.App.Screens;
using Air.Data;
using Air.Data.Entities;

namespace Air.App.Form;

public interface IFormService
{
  Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct) => BuildScreenAsync(servicePath, null, ct);
  Task<object?> GetModelAsync(long? id, CancellationToken ct = default);
  Task<object> SaveAsync(JsonElement json, CancellationToken ct = default);
  Task DeleteAsync(long id, CancellationToken ct = default);
}

public abstract class FormService<TEntity>(PlatformDbContext db) : IFormService
  where TEntity : class, IEntity, new()
{
  public virtual Feature Feature => new("", GetType().Name.Replace("FormService", "").ToLowerInvariant(), GetType());

  protected virtual string Title => typeof(TEntity).Name;

  protected virtual void SetElements(Elements<TEntity> elements) { }

  protected virtual Task SetElementsAsync(Elements<TEntity> elements, CancellationToken ct = default)
  {
    SetElements(elements);
    return Task.CompletedTask;
  }

  protected virtual OverlayBuilder Presentation(string handle) => OverlayModel.Dialog(handle).Screen(handle).Width(720);

  protected virtual string RecordCaption(TEntity? entity, long? id)
  {
    if (id is null or 0)
    {
      return $"New {Title}";
    }

    var nameProp = entity?.GetType().GetProperty("Name") ?? entity?.GetType().GetProperty("Title");
    var label = nameProp?.GetValue(entity)?.ToString();

    return string.IsNullOrWhiteSpace(label)
      ? $"{Title} #{id}"
      : $"{Title} #{id} {label}";
  }

  public virtual async Task<ScreenDto> BuildScreenAsync(string servicePath, long? id = null, CancellationToken ct = default)
  {
    var elements = new Elements<TEntity>();
    await SetElementsAsync(elements, ct);

    var formModel = new FormModel
    {
      Service = servicePath,
      Elements = elements.List.ToList(),
      Tabs = elements.Tabs.Count > 0 ? elements.Tabs.ToList() : null,
      IsReadOnly = Feature.IsReadOnly
    };

    TEntity? entity = null;
    if (id is not null && id > 0)
    {
      entity = await db.Set<TEntity>().FindAsync([id.Value], ct);
    }

    var caption = RecordCaption(entity, id);

    var navStart = new List<NavElementModel>();
    var navElements = new List<NavElementModel>();

    if (!Feature.IsReadOnly)
    {
      navStart.Add(new FormNavSaveCloseModel
      {
        Key = "save-close",
        Label = "Save & Close",
        Icon = UiIcon.Save,
        Color = UiColor.Green,
        Variant = ButtonVariant.Soft
      });

      navElements.Add(new FormNavSaveModel
      {
        Key = "save",
        Label = "Save",
        Color = UiColor.Green,
        Variant = ButtonVariant.Soft
      });

      navElements.Add(new NavCloseModel
      {
        Key = "form-close",
        Label = "Close",
        Icon = UiIcon.Close,
        Color = UiColor.Slate,
        Variant = ButtonVariant.Soft
      });
    }
    else
    {
      navStart.Add(new NavCloseModel
      {
        Key = "form-close",
        Label = "Close",
        Icon = UiIcon.Close,
        Color = UiColor.Slate,
        Variant = ButtonVariant.Soft
      });
    }

    navElements.Add(new NavTabModel
    {
      Key = "record",
      Group = "tabs",
      Value = "record",
      Label = caption,
      Color = UiColor.Sky,
      IsSelected = true
    });

    if (elements.Tabs.Count > 0)
    {
      foreach (var tab in elements.Tabs)
      {
        navElements.Add(new NavTabModel
        {
          Key = tab.Key,
          Group = "tabs",
          Value = tab.Key,
          Label = tab.Label,
          Variant = ButtonVariant.Soft
        });
      }
    }

    var navEnd = new List<NavElementModel>
    {
      new NavCloseModel
      {
        Key = "close",
        Icon = UiIcon.Close,
        Color = UiColor.Slate,
        Variant = ButtonVariant.Ghost,
        Title = "Close"
      }
    };

    var nav = new NavModel
    {
      Key = $"{servicePath}.nav",
      Start = navStart,
      Elements = navElements,
      End = navEnd
    };

    var layout = PanelLayout.Column(servicePath)
      .Panel("bar", p => p.Model(nav).Auto().Pinned().Rigid())
      .Panel("form", p => p.Model(formModel).Fill())
      .Build();

    return new ScreenDto { Layout = layout };
  }

  public virtual async Task<object?> GetModelAsync(long? id, CancellationToken ct = default)
  {
    if (id is null or 0)
    {
      return new TEntity();
    }

    return await db.Set<TEntity>().FindAsync([id.Value], ct)
      ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");
  }

  public virtual async Task<object> SaveAsync(JsonElement json, CancellationToken ct = default)
  {
    var entityJson = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("value", out var val)
      ? val
      : json;

    var entity = JsonSerializer.Deserialize<TEntity>(entityJson.GetRawText(), PlatformJsonOptions.DefaultOptions)
      ?? throw new InvalidOperationException("Failed to deserialize form entity payload.");

    if (entity.Id == 0 && json.ValueKind == JsonValueKind.Object &&
        json.TryGetProperty("id", out var idProp))
    {
      if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var rootId) && rootId > 0)
      {
        entity.Id = rootId;
      }
      else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var strId) && strId > 0)
      {
        entity.Id = strId;
      }
    }

    if (entity.Id == 0)
    {
      db.Set<TEntity>().Add(entity);
    }
    else
    {
      var existing = await db.Set<TEntity>().FindAsync([entity.Id], ct);
      if (existing is not null)
      {
        db.Entry(existing).CurrentValues.SetValues(entity);
        entity = existing;
      }
      else
      {
        db.Set<TEntity>().Update(entity);
      }
    }

    await db.SaveChangesAsync(ct);
    return entity;
  }

  public virtual async Task DeleteAsync(long id, CancellationToken ct = default)
  {
    var entity = await db.Set<TEntity>().FindAsync([id], ct)
      ?? throw new KeyNotFoundException($"Entity {typeof(TEntity).Name} with ID {id} not found.");
    db.Set<TEntity>().Remove(entity);
    await db.SaveChangesAsync(ct);
  }
}
