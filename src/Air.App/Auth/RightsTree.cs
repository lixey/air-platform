namespace Platform.App.Auth;

public static class RightsTree
{
  private static readonly ConcurrentDictionary<(string Module, string FeatureKey), IReadOnlyList<Right>> DiscoveredFeatureRightsCache = new();

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
        rights.Add(Node(feat.Export.Key, L["Export"]));
        if (feat.HasHistory)
        {
          rights.Add(Node(feat.History.Key, L["History"]));
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

        foreach (var customRight in GetOrDiscoverFeatureActionRights(catalog, module, feat))
        {
          if (rights.All(r => !string.Equals(r.Key, customRight.Key, StringComparison.OrdinalIgnoreCase)))
          {
            var labelKey = System.Text.RegularExpressions.Regex.Replace(customRight.Action, "([a-z])([A-Z])", "$1 $2");
            rights.Add(Node(customRight.Key, modL.TextOr(customRight.Action, modL[labelKey])));
          }
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

  private static IEnumerable<Right> GetContainerRights(Feature feature) =>
    feature.Rights.Values
      .Where(r => r.Action.StartsWith(FeatureAccess.AccessToPrefix, StringComparison.OrdinalIgnoreCase))
      .OrderBy(r => r.Key, StringComparer.Ordinal);

  private static readonly HashSet<string> StandardActions = new(StringComparer.OrdinalIgnoreCase)
  {
    "View", "MenuView", "Edit", "Delete", "Export", "History", "*"
  };

  private static IReadOnlyList<Right> GetOrDiscoverFeatureActionRights(ModuleCatalog catalog, IPlatformModule module, Feature feature) =>
    DiscoveredFeatureRightsCache.GetOrAdd(
      (module.Handle, feature.Key),
      _ => DiscoverFeatureActionRights(catalog, module, feature).ToList());

  private static IEnumerable<Right> DiscoverFeatureActionRights(ModuleCatalog catalog, IPlatformModule module, Feature feature)
  {
    var discovered = new List<Right>();

    foreach (var r in feature.Rights.Values)
    {
      if (!StandardActions.Contains(r.Action) &&
          !r.Action.StartsWith(FeatureAccess.AccessToPrefix, StringComparison.OrdinalIgnoreCase))
      {
        discovered.Add(r);
      }
    }

    var assembly = module.GetType().Assembly;
    Type[] moduleTypes;
    try
    {
      moduleTypes = assembly.GetTypes();
    }
    catch
    {
      return discovered;
    }

    var prefix = module.Handle + feature.CleanName;
    var relatedServices = moduleTypes
      .Where(t => t is { IsClass: true, IsAbstract: false } &&
                  (t == feature.ServiceType ||
                   Feature.FeaturesByServiceType.TryGetValue(t, out var mapped) && mapped == feature ||
                   t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && t.Name.EndsWith("Service", StringComparison.OrdinalIgnoreCase)))
      .ToList();

    var entityTypes = new HashSet<Type>();
    foreach (var st in relatedServices)
    {
      foreach (var method in st.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
      {
        if (method.GetCustomAttribute<RequireActionAttribute>(inherit: true) is { } reqAct &&
            !StandardActions.Contains(reqAct.ActionName))
        {
          discovered.Add(feature.Action(reqAct.ActionName));
        }
      }

      var cur = st.BaseType;
      while (cur != null && cur != typeof(object))
      {
        if (cur.IsGenericType)
        {
          var def = cur.GetGenericTypeDefinition();
          if (def == typeof(Grid.GridService<,>) || def == typeof(Form.FormService<>))
          {
            entityTypes.Add(cur.GetGenericArguments()[0]);
            break;
          }
        }

        cur = cur.BaseType;
      }
    }

    if (entityTypes.Count > 0)
    {
      foreach (var entityType in entityTypes)
      {
        var statusFlowInterface = typeof(Screens.IRecordStatuses<>).MakeGenericType(entityType);
        foreach (var flowType in moduleTypes.Where(t => t is { IsClass: true, IsAbstract: false } && statusFlowInterface.IsAssignableFrom(t)))
        {
          try
          {
            if (catalog.CreateServiceInstance(flowType) is not object flowInstance)
            {
              continue;
            }

            if (flowInstance is IPlatformService flowPlatformService &&
                !string.IsNullOrEmpty(flowPlatformService.Feature.Module) &&
                flowPlatformService.Feature != feature)
            {
              continue;
            }

            if (statusFlowInterface.GetProperty(nameof(Screens.IRecordStatuses<>.AllTransitions))?.GetValue(flowInstance) is System.Collections.IEnumerable transitions)
            {
              foreach (var tr in transitions)
              {
                if (tr is Screens.RecordTransition rt && !string.IsNullOrWhiteSpace(rt.Name))
                {
                  discovered.Add(feature.Action(rt.Name));
                }
              }
            }
          }
          catch { }
        }
      }
    }

    return discovered;
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
