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
        rights.Add(Node(feat.MenuView.Key, L["Menu view"]));
        rights.Add(Node(feat.Export.Key, L["Export"]));
        if (feat.ServiceType != null && typeof(Changes.ChangeGridService).IsAssignableFrom(feat.ServiceType))
        {
          rights.Add(Node(feat.Action("Restore").Key, L["Restore"]));
        }

        foreach (var accessRight in GetContainerRights(feat))
        {
          var labelKey = System.Text.RegularExpressions.Regex.Replace(accessRight.Action, "([a-z])([A-Z])", "$1 $2");
          if (labelKey.Length > 1)
          {
            labelKey = labelKey[0] + labelKey[1..].ToLowerInvariant();
          }
          rights.Add(Node(accessRight.Key, modL[labelKey]));
        }

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

  private static IEnumerable<Right> GetContainerRights(Feature feature)
  {
    if (feature.GetType().GetProperty("AccessTo")?.GetValue(feature) is not FeatureAccess access)
    {
      return [];
    }

    return access.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
      .Where(p => p.PropertyType == typeof(Right) && p.GetIndexParameters().Length == 0)
      .OrderBy(p => p.MetadataToken)
      .Select(p => (Right)p.GetValue(access)!);
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

  public static HashSet<string> FlattenDisabledKeys(IEnumerable<CheckboxTreeNodeDto> nodes)
  {
    var set = new HashSet<string>(StringComparer.Ordinal);
    foreach (var node in nodes)
    {
      if (node.IsDisabled)
      {
        set.Add(node.Key);
      }
      foreach (var childKey in FlattenDisabledKeys(node.Children))
      {
        set.Add(childKey);
      }
    }
    return set;
  }

  public static List<string> Normalize(IEnumerable<CheckboxTreeNodeDto> nodes, IEnumerable<string> selectedKeys)
  {
    var selected = selectedKeys.ToHashSet(StringComparer.Ordinal);
    var result = new List<string>();
    CollectNormalized(nodes, selected, result);
    return result;

    static void CollectNormalized(IEnumerable<CheckboxTreeNodeDto> currentNodes, HashSet<string> sel, List<string> acc)
    {
      foreach (var node in currentNodes)
      {
        if (sel.Contains(node.Key))
        {
          acc.Add(node.Key);
        }
        else if (node.Children.Count > 0)
        {
          CollectNormalized(node.Children, sel, acc);
        }
      }
    }
  }

  public static bool Covers(IReadOnlySet<string> held, string key) =>
    HeldRights.Covers(held, key);
}
