using System.Diagnostics.CodeAnalysis;

namespace Platform.Api.Modules;

public class Feature
{
  private readonly ConcurrentDictionary<string, Right> _rights = new(StringComparer.OrdinalIgnoreCase);
  private string _label;
  private string _name;
  private string? _title;
  private Type? _serviceType;

  public Feature(string module, string name, Type? serviceType = null, Action<Feature>? configure = null)
  {
    CodeRows.EnsurePascalCaseHandle(module, "Feature.Module");
    CodeRows.EnsurePascalCaseHandle(name, "Feature");

    Module = module;
    Handle = name;
    _label = PascalToSpaced(name);
    _name = PascalToSpaced(Pluralize(name));
    _serviceType = serviceType;
    Path = $"{ServicePathResolver.ToKebabCase(module)}/{ServicePathResolver.ToKebabCase(name)}";

    (View, MenuView, Edit, Delete, Export, History, All) = CreateStandardRights();
    SyncStandardRights();
    configure?.Invoke(this);
  }

  protected Feature(Feature source)
  {
    ArgumentNullException.ThrowIfNull(source);
    Module = source.Module;
    Handle = source.Handle;
    _name = source.Name;
    _label = source.Label;
    _title = source.Title;
    _serviceType = source.ServiceType;
    Path = source.Path;
    IsReadOnly = source.IsReadOnly;
    HasDelete = source.HasDelete;
    HasExport = source.HasExport;
    HasHistory = source.HasHistory;
    AgentAccess = source.AgentAccess;

    (View, MenuView, Edit, Delete, Export, History, All) = CreateStandardRights();

    foreach (var kv in source._rights)
    {
      _rights[kv.Key] = new Right(kv.Value.Key, kv.Value.Action, this, kv.Value.IsPattern);
    }

    SyncStandardRights();
  }

  private (Right View, Right MenuView, Right Edit, Right Delete, Right Export, Right History, Right All) CreateStandardRights() =>
  (
    new Right($"{Key}.View", "View", this),
    new Right($"{Key}.MenuView", "MenuView", this),
    new Right($"{Key}.Edit", "Edit", this),
    new Right($"{Key}.Delete", "Delete", this),
    new Right($"{Key}.Export", "Export", this),
    new Right($"{Key}.History", "History", this),
    new Right($"{Key}.*", "*", this, isPattern: true)
  );

  public string Module { get; }
  public string Handle { get; }
  public string Path { get; }
  public string Key => $"{Module}.{Handle}";

  public string Label
  {
    get => _label;
    set
    {
      EnsureMutable();
      _label = value;
    }
  }

  public string Name
  {
    get => _name;
    set
    {
      EnsureMutable();
      _name = value;
    }
  }

  public string? Title
  {
    get => _title;
    set
    {
      EnsureMutable();
      _title = value;
    }
  }

  public Type? ServiceType
  {
    get => _serviceType;
    set
    {
      EnsureMutable();
      _serviceType = value;
    }
  }

  public bool IsReadOnly { get; private set; }
  public bool HasDelete { get; private set; } = true;
  public bool HasExport { get; private set; } = true;
  public bool HasHistory { get; private set; } = true;
  public AgentAccess AgentAccess { get; private set; }
  public bool AllowsAgent => AgentAccess != AgentAccess.None;
  public bool AllowsAgentWrite => AgentAccess == AgentAccess.ReadWrite && !IsReadOnly;
  public bool IsFrozen { get; private set; }

  public Right View { get; }
  public Right MenuView { get; }
  public Right Edit { get; }
  public Right Delete { get; }
  public Right Export { get; }
  public Right History { get; }
  public Right All { get; }

  public IReadOnlyDictionary<string, Right> Rights => _rights;

  public bool HasAction(string action) => _rights.ContainsKey(action);

  public bool TryGetRight(string action, [NotNullWhen(true)] out Right? right) =>
    _rights.TryGetValue(action, out right);

  public Right Action(string action)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(action);
    if (_rights.TryGetValue(action, out var existing))
    {
      return existing;
    }

    if (IsFrozen)
    {
      throw new InvalidOperationException(
        $"Cannot declare new right '{Key}.{action}' after feature '{Key}' has been frozen.");
    }

    var created = new Right($"{Key}.{action}", action, this, action == "*");
    return _rights.GetOrAdd(action, created);
  }

  public void Freeze() => IsFrozen = true;

  public Feature ReadOnly(bool isReadOnly = true)
  {
    EnsureMutable();
    IsReadOnly = isReadOnly;
    SyncStandardRights();
    return this;
  }

  public Feature WithoutDelete()
  {
    EnsureMutable();
    HasDelete = false;
    SyncStandardRights();
    return this;
  }

  public Feature WithoutExport()
  {
    EnsureMutable();
    HasExport = false;
    SyncStandardRights();
    return this;
  }

  public Feature WithoutHistory()
  {
    EnsureMutable();
    HasHistory = false;
    SyncStandardRights();
    return this;
  }

  public Feature AllowAgent(AgentAccess access = AgentAccess.ReadWrite)
  {
    EnsureMutable();
    AgentAccess = access;
    return this;
  }

  public Feature AllowAgent(bool allow) =>
    AllowAgent(allow ? AgentAccess.ReadWrite : AgentAccess.None);

  public static string FormatSourceTitle(string uiSource, string dbSource) =>
    Localizer.Instance["LegacyTitleFormat", uiSource, dbSource];

  public Feature WithTitle(string title)
  {
    EnsureMutable();
    _title = title;
    return this;
  }

  public Feature WithTitle(string uiSource, string dbSource) =>
    WithTitle(FormatSourceTitle(uiSource, dbSource));

  public Feature<TAccess> WithAccess<TAccess>() where TAccess : FeatureAccess, new()
  {
    EnsureMutable();
    var typed = new Feature<TAccess>(this);
    typed.AccessTo.Feature = typed;
    return typed;
  }

  private void EnsureMutable()
  {
    if (IsFrozen)
    {
      throw new InvalidOperationException($"Feature '{Key}' is frozen and cannot be modified at runtime.");
    }
  }

  private void SyncStandardRights()
  {
    _rights["View"] = View;
    _rights["MenuView"] = MenuView;
    _rights["*"] = All;

    if (!IsReadOnly)
    {
      _rights["Edit"] = Edit;
      if (HasDelete)
      {
        _rights["Delete"] = Delete;
      }
      else
      {
        _rights.TryRemove("Delete", out _);
      }
    }
    else
    {
      _rights.TryRemove("Edit", out _);
      _rights.TryRemove("Delete", out _);
    }

    if (HasExport)
    {
      _rights["Export"] = Export;
    }
    else
    {
      _rights.TryRemove("Export", out _);
    }

    if (HasHistory)
    {
      _rights["History"] = History;
    }
    else
    {
      _rights.TryRemove("History", out _);
    }
  }

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
}
