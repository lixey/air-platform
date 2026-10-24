namespace Platform.Api.Modules;

public class Feature
{
  public static readonly ConcurrentDictionary<Type, Feature> FeaturesByServiceType = new();
  private readonly ConcurrentDictionary<string, Right> _rights = new(StringComparer.OrdinalIgnoreCase);

  public Feature(string module, string name, Type? serviceType = null, Action<Feature>? configure = null)
  {
    Module = module;
    CleanName = name;
    Label = PascalToSpaced(name);
    Name = PascalToSpaced(Pluralize(name));
    ServiceType = serviceType;
    Path = $"{ServicePathResolver.ToKebabCase(module)}/{ServicePathResolver.ToKebabCase(name)}";
    configure?.Invoke(this);
    if (serviceType != null)
    {
      FeaturesByServiceType[serviceType] = this;
    }
  }

  public string Label { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string? Title { get; set; }

  private static string PascalToSpaced(string s) =>
    System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z])", "$1 $2");

  private static string Pluralize(string word)
  {
    if (string.IsNullOrEmpty(word))
    {
      return string.Empty;
    }

    if (word.EndsWith("y", StringComparison.OrdinalIgnoreCase))
    {
      if (word.EndsWith("ay", StringComparison.OrdinalIgnoreCase) ||
          word.EndsWith("ey", StringComparison.OrdinalIgnoreCase) ||
          word.EndsWith("iy", StringComparison.OrdinalIgnoreCase) ||
          word.EndsWith("oy", StringComparison.OrdinalIgnoreCase) ||
          word.EndsWith("uy", StringComparison.OrdinalIgnoreCase))
      {
        return word + "s";
      }

      return word[..^1] + "ies";
    }

    if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("z", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("sh", StringComparison.OrdinalIgnoreCase) ||
        word.EndsWith("ch", StringComparison.OrdinalIgnoreCase))
    {
      return word + "es";
    }

    return word + "s";
  }

  protected Feature(Feature source)
  {
    ArgumentNullException.ThrowIfNull(source);
    Module = source.Module;
    Name = source.Name;
    Label = source.Label;
    Title = source.Title;
    CleanName = source.CleanName;
    ServiceType = source.ServiceType;
    Path = source.Path;
    IsReadOnly = source.IsReadOnly;
    HasDelete = source.HasDelete;
    AllowsAgent = source.AllowsAgent;
    foreach (var kv in source._rights)
    {
      _rights[kv.Key] = new Right(kv.Value.Key, kv.Value.Action, this, kv.Value.IsPattern);
    }

    if (ServiceType != null)
    {
      FeaturesByServiceType[ServiceType] = this;
    }
  }

  public string Module { get; }
  public string CleanName { get; }
  public string Path { get; }
  public Type? ServiceType { get; set; }
  public bool IsReadOnly { get; private set; }
  public bool HasDelete { get; private set; } = true;
  public bool AllowsAgent { get; private set; }

  public string Key => $"{Module}.{CleanName}";

  public Right View => Action("View");
  public Right MenuView => Action("MenuView");
  public Right Edit => Action("Edit");
  public Right Delete => Action("Delete");
  public Right Export => Action("Export");
  public Right All => Action("*");

  public IReadOnlyDictionary<string, Right> Rights => _rights;

  public bool HasAction(string action) => _rights.ContainsKey(action);

  public Right Action(string action) =>
    _rights.GetOrAdd(action, act => new Right($"{Key}.{act}", act, this, act == "*"));

  public Feature ReadOnly(bool isReadOnly = true)
  {
    IsReadOnly = isReadOnly;
    return this;
  }

  public Feature WithoutDelete()
  {
    HasDelete = false;
    return this;
  }

  public Feature AllowAgent(bool allow = true)
  {
    AllowsAgent = allow;
    return this;
  }

  public Feature WithTitle(string title)
  {
    Title = title;
    return this;
  }

  public Feature WithTitle(string legacyUi, string legacyDb)
  {
    Title = $"Легасі UI: {legacyUi} | БД: {legacyDb}";
    return this;
  }

  public Feature<TAccess> WithAccess<TAccess>() where TAccess : FeatureAccess, new()
  {
    var typed = new Feature<TAccess>(this);
    typed.AccessTo.Feature = typed;
    return typed;
  }
}
