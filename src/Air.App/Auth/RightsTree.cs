using Air.App.Form;
using Air.App.Localization;
using Air.App.Modules;
using Air.Data.Tenancy;

namespace Air.App.Auth;

public static class RightsTree
{
  public static List<CheckboxTreeNodeDto> Build(ModuleCatalog catalog, IReadOnlySet<string>? held = null)
  {
    var L = Localizer.Instance;
    var modules = new List<CheckboxTreeNodeDto>();

    foreach (var module in catalog.Modules)
    {
      var features = module.Features;
      if (features.Count == 0) continue;

      var modL = L.For(module.Handle);
      var featureNodes = new List<CheckboxTreeNodeDto>();
      foreach (var feat in features)
      {
        var rights = new List<CheckboxTreeNodeDto>
        {
          Node(feat.View.Key, L["View"])
        };

        if (!feat.IsReadOnly)
        {
          rights.Add(Node(feat.Edit.Key, L["Edit"]));
          if (feat.HasDelete)
          {
            rights.Add(Node(feat.Delete.Key, L["Delete"]));
          }
        }
        rights.Add(Node(feat.Export.Key, L["Export"]));

        var featureLabel = !string.IsNullOrWhiteSpace(feat.Name) ? modL[feat.Name] : modL[feat.CleanName];
        featureNodes.Add(Node(
          feat.All.Key,
          featureLabel,
          rights,
          feat.CleanName
        ));
      }

      modules.Add(Node(
        $"{module.Handle}.*",
        modL[module.Name],
        featureNodes
      ));
    }

    return [Node("*", L.TextOr("RightsTreeAll", L["All rights"]), modules)];

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

  public static bool Covers(IReadOnlySet<string> held, string key) =>
    HeldRights.Covers(held, key);
}
