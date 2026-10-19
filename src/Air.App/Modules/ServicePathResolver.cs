using System.Text;

namespace Air.App.Modules;

public static class ServicePathResolver
{
  public static string Resolve(Type serviceType)
  {
    var name = serviceType.Name;
    if (name.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
    {
      name = name[..^"Service".Length];
    }

    var prefixes = ModuleCatalog.Instance.Modules
      .Select(m => m.Handle)
      .Concat(["App", "Org", "Ecom", "Doc"])
      .Where(p => !string.IsNullOrWhiteSpace(p))
      .Distinct(StringComparer.OrdinalIgnoreCase);

    foreach (var prefix in prefixes)
    {
      if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
          name.Length > prefix.Length &&
          char.IsUpper(name[prefix.Length]))
      {
        var rest = name[prefix.Length..];
        return $"{prefix.ToLowerInvariant()}/{rest.ToKebabCase()}";
      }
    }

    return name.ToKebabCase();
  }

  public static string ToKebabCase(this string? input)
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
