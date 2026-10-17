using Air.App.Form;
using Air.App.Modules;

namespace Air.App.Auth;

public static class RightsTree
{
  public static List<CheckboxTreeNodeDto> Build(ModuleCatalog catalog, IReadOnlySet<string>? held = null)
  {
    var modules = new List<CheckboxTreeNodeDto>();

    foreach (var module in catalog.Modules)
    {
      var features = module.Features;
      if (features.Count == 0) continue;

      var featureNodes = new List<CheckboxTreeNodeDto>();
      foreach (var feat in features)
      {
        var rights = new List<CheckboxTreeNodeDto>
        {
          Node(feat.View.Key, "View")
        };

        if (!feat.IsReadOnly)
        {
          rights.Add(Node(feat.Edit.Key, "Edit"));
          if (feat.HasDelete)
          {
            rights.Add(Node(feat.Delete.Key, "Delete"));
          }
        }
        rights.Add(Node(feat.Export.Key, "Export"));

        featureNodes.Add(Node(
          feat.All.Key,
          feat.Name,
          rights,
          feat.CleanName
        ));
      }

      modules.Add(Node(
        $"{module.Handle}.*",
        module.Name,
        featureNodes
      ));
    }

    return [Node("*", "All rights", modules)];

    CheckboxTreeNodeDto Node(string key, string label, List<CheckboxTreeNodeDto>? children = null, string? note = null) =>
      new()
      {
        Key = key,
        Label = label,
        Note = note,
        IsDisabled = held != null && !Covers(held, key),
        Children = children ?? []
      };
  }

  public static HashSet<string> FlattenKeys(IEnumerable<CheckboxTreeNodeDto> nodes)
  {
    var set = new HashSet<string>(StringComparer.Ordinal);
    foreach (var node in nodes)
    {
      set.Add(node.Key);
      foreach (var childKey in FlattenKeys(node.Children))
      {
        set.Add(childKey);
      }
    }
    return set;
  }

  public static bool Covers(IReadOnlySet<string> held, string key)
  {
    if (held.Contains("*")) return true;
    if (held.Contains(key)) return true;

    var parts = key.Split('.');
    if (parts.Length >= 2 && held.Contains($"{parts[0]}.*")) return true;
    if (parts.Length >= 3 && held.Contains($"{parts[0]}.{parts[1]}.*")) return true;

    return false;
  }
}
