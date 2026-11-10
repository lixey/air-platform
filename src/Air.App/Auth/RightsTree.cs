namespace Platform.App.Auth;

public static class RightsTree
{
  public static List<CheckboxTreeNodeDto> Build(ModuleCatalog catalog, IReadOnlySet<string>? held = null)
  {
    var L = Localizer.Instance;
    var modules = new List<CheckboxTreeNodeDto>();

    foreach (var module in catalog.Modules)
    {
      var features = module.Features;
      if (features.Count == 0)
      {
        continue;
      }

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
        if (feat.HasExport)
        {
          rights.Add(Node(feat.Export.Key, L["Export"]));
        }

        if (feat.HasHistory)
        {
          rights.Add(Node(feat.History.Key, L["History"]));
        }

        foreach (var accessRight in GetContainerRights(feat))
        {
          rights.Add(Node(accessRight.Key, modL[FormatSentenceCase(accessRight.Action)]));
        }

        foreach (var customRight in GetCustomActionRights(feat))
        {
          if (rights.All(r => !string.Equals(r.Key, customRight.Key, StringComparison.OrdinalIgnoreCase)))
          {
            var spaced = Grid.Columns.PathToLabel(customRight.Action);
            rights.Add(Node(customRight.Key, modL.TextOr(customRight.Action, modL[spaced])));
          }
        }

        var featureLabel = !string.IsNullOrWhiteSpace(feat.Name) ? modL[feat.Name] : modL[feat.Handle];
        featureNodes.Add(Node(
          feat.All.Key,
          featureLabel,
          rights,
          feat.Handle
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

  private static IEnumerable<Right> GetContainerRights(Feature feature) =>
    feature.Rights.Values
      .Where(r => r.Action.StartsWith(FeatureAccess.AccessToPrefix, StringComparison.OrdinalIgnoreCase))
      .OrderBy(r => r.Key, StringComparer.Ordinal);

  private static IEnumerable<Right> GetCustomActionRights(Feature feature) =>
    feature.Rights.Values
      .Where(r => !Right.StandardActions.Contains(r.Action) &&
                  !r.Action.StartsWith(FeatureAccess.AccessToPrefix, StringComparison.OrdinalIgnoreCase))
      .OrderBy(r => r.Key, StringComparer.Ordinal);

  private static string FormatSentenceCase(string pascalName)
  {
    var spaced = Grid.Columns.PathToLabel(pascalName);
    return spaced.Length > 1
      ? string.Concat(spaced.AsSpan(0, 1), spaced[1..].ToLowerInvariant())
      : spaced;
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
