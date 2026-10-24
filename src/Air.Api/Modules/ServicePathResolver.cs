namespace Platform.Api.Modules;

public static class ServicePathResolver
{
  private static readonly ConcurrentDictionary<Assembly, string[]> AssemblyPrefixes = new();

  public static string Resolve(Type serviceType)
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
      .Where(p => !string.IsNullOrWhiteSpace(p))
      .Distinct(StringComparer.OrdinalIgnoreCase);

    foreach (var prefix in prefixes)
    {
      if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
          name.Length > prefix.Length &&
          char.IsUpper(name[prefix.Length]))
      {
        var rest = name[prefix.Length..];
        return $"{prefix.ToLowerInvariant()}/{ToKebabCase(rest)}";
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
