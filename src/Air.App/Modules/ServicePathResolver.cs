namespace Air.App.Modules;

public static class ServicePathResolver
{
  public static string Resolve(Type serviceType)
  {
    if (Feature.FeaturesByServiceType.TryGetValue(serviceType, out var feat))
    {
      return feat.Path;
    }

    var name = serviceType.Name;
    if (name.EndsWith("GridService", StringComparison.OrdinalIgnoreCase))
    {
      name = name[..^"GridService".Length];
    }
    else if (name.EndsWith("FormService", StringComparison.OrdinalIgnoreCase))
    {
      name = name[..^"Service".Length];
    }
    else if (name.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
    {
      name = name[..^"Service".Length];
    }

    foreach (var prefix in new[] { "App", "Org", "Ecom", "Doc" })
    {
      if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
      {
        var rest = name[prefix.Length..];
        return $"{prefix.ToLowerInvariant()}/{rest.ToLowerInvariant()}";
      }
    }

    return name.ToLowerInvariant();
  }
}
