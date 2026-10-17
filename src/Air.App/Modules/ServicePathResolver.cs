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
    foreach (var suffix in new[] { "GridService", "FormService", "Service" })
    {
      if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
      {
        name = name[..^suffix.Length];
        break;
      }
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
