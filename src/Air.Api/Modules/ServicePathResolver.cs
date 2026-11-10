namespace Platform.Api.Modules;

public static class ServicePathResolver
{
  private static readonly ConcurrentDictionary<Assembly, string[]> AssemblyPrefixes = new();
  private static readonly ConcurrentDictionary<Type, string> ResolvedWithModule = new();

  public static string Resolve(Type serviceType) => Resolve(serviceType, null);

  internal static string Resolve(Type serviceType, IEnumerable<string>? additionalHandles)
  {
    ArgumentNullException.ThrowIfNull(serviceType);
    if (ResolvedWithModule.TryGetValue(serviceType, out var cached))
    {
      return cached;
    }

    var resolved = ResolveCore(serviceType, additionalHandles);
    if (resolved.Contains('/'))
    {
      ResolvedWithModule[serviceType] = resolved;
    }

    return resolved;
  }

  private static string ResolveCore(Type serviceType, IEnumerable<string>? additionalHandles)
  {
    var name = serviceType.Name;
    if (name.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
    {
      name = name[..^"Service".Length];
    }

    var assemblyHandles = AssemblyPrefixes.GetOrAdd(serviceType.Assembly, static asm =>
    {
      try
      {
        return asm.GetTypes()
          .Select(t => t.GetCustomAttribute<ModuleAttribute>()?.Handle)
          .Where(h => !string.IsNullOrWhiteSpace(h))
          .Select(h => h!)
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .ToArray();
      }
      catch
      {
        return [];
      }
    });

    var prefixes = ModuleCatalog.Instance.Modules
      .Select(m => m.Handle)
      .Concat(assemblyHandles)
      .Concat(additionalHandles ?? [])
      .Where(p => !string.IsNullOrWhiteSpace(p))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderByDescending(p => p.Length);

    foreach (var prefix in prefixes)
    {
      if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
          name.Length > prefix.Length &&
          char.IsUpper(name[prefix.Length]))
      {
        var rest = name[prefix.Length..];
        return $"{ToKebabCase(prefix)}/{ToKebabCase(rest)}";
      }
    }

    return ToKebabCase(name);
  }

  public static string ToKebabCase(string? input)
  {
    if (string.IsNullOrEmpty(input))
    {
      return string.Empty;
    }

    var sb = new StringBuilder(input.Length + 5);
    for (var i = 0; i < input.Length; i++)
    {
      var c = input[i];
      if (char.IsUpper(c))
      {
        if (i > 0 && input[i - 1] != '-' && input[i - 1] != '/')
        {
          sb.Append('-');
        }

        sb.Append(char.ToLowerInvariant(c));
      }
      else
      {
        sb.Append(c);
      }
    }

    return sb.ToString();
  }
}
