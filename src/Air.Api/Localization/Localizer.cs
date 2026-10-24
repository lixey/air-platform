namespace Platform.Api.Localization;

public sealed class Localizer : ILocalizer
{
  public static readonly Localizer Instance = new();

  private readonly Localizer? _root;
  private readonly string? _moduleHandle;
  private readonly Lock _sync = new();
  private readonly List<System.Resources.ResourceManager> _managers = [];
  private readonly Dictionary<string, string> _canonicalKeys = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> _loadedNames = new(StringComparer.Ordinal);
  private int _lastAssemblyCount;

  public Localizer()
  {
    Register(typeof(Platform.Api.Resources.PlatformResource));
    EnsureManagersLoaded();
  }

  private Localizer(Localizer root, string moduleHandle)
  {
    _root = root;
    _moduleHandle = moduleHandle;
  }

  public void Register(Type resourceType)
  {
    if (_root != null)
    {
      _root.Register(resourceType);
      return;
    }

    if (resourceType == null)
    {
      return;
    }

    lock (_sync)
    {
      RegisterLocked(resourceType);
    }
  }

  private void RegisterLocked(Type resourceType)
  {
    if (!_loadedNames.Add(resourceType.FullName ?? resourceType.Name))
    {
      return;
    }

    var mgr = new System.Resources.ResourceManager(resourceType) { IgnoreCase = true };
    _managers.Add(mgr);
    IndexKeysLocked(mgr, System.Globalization.CultureInfo.InvariantCulture);
    try
    {
      IndexKeysLocked(mgr, new System.Globalization.CultureInfo("uk"));
    }
    catch { }
  }

  private void IndexKeysLocked(System.Resources.ResourceManager mgr, System.Globalization.CultureInfo culture)
  {
    try
    {
      var set = mgr.GetResourceSet(culture, true, false);
      if (set == null)
      {
        return;
      }

      foreach (System.Collections.DictionaryEntry entry in set)
      {
        if (entry.Key is string k && !_canonicalKeys.ContainsKey(k))
        {
          _canonicalKeys[k] = k;
        }
      }
    }
    catch { }
  }

  public void EnsureManagersLoaded()
  {
    if (_root != null)
    {
      _root.EnsureManagersLoaded();
      return;
    }

    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
    if (assemblies.Length == _lastAssemblyCount)
    {
      return;
    }

    lock (_sync)
    {
      if (assemblies.Length == _lastAssemblyCount)
      {
        return;
      }

      foreach (var asm in assemblies)
      {
        if (asm.IsDynamic)
        {
          continue;
        }

        try
        {
          foreach (var type in asm.GetTypes())
          {
            if (type.Name.EndsWith("Resource", StringComparison.OrdinalIgnoreCase) && !type.IsAbstract && !type.IsGenericType)
            {
              RegisterLocked(type);
            }
          }
        }
        catch { }
      }

      _lastAssemblyCount = assemblies.Length;
    }
  }

  public bool TryGetString(string key, out string result)
  {
    if (string.IsNullOrEmpty(key))
    {
      result = string.Empty;
      return false;
    }

    if (_root != null)
    {
      if (!string.IsNullOrWhiteSpace(_moduleHandle) &&
          _root.TryGetString($"{_moduleHandle}: {key}", out result))
      {
        return true;
      }

      return _root.TryGetString(key, out result);
    }

    var culture = System.Globalization.CultureInfo.CurrentUICulture;
    System.Resources.ResourceManager[] snapshot;
    string? canonical;
    lock (_sync)
    {
      snapshot = [.. _managers];
      _canonicalKeys.TryGetValue(key, out canonical);
    }

    foreach (var mgr in snapshot)
    {
      try
      {
        var text = mgr.GetString(key, culture)
          ?? (canonical != null && canonical != key ? mgr.GetString(canonical, culture) : null);
        if (!string.IsNullOrEmpty(text))
        {
          result = text;
          return true;
        }
      }
      catch { }
    }

    if (UiTexts.English.TryGetValue(key, out var val) ||
        canonical != null && UiTexts.English.TryGetValue(canonical, out val))
    {
      result = val;
      return true;
    }

    result = key;
    return false;
  }

  public string TextOr(string key, string fallback)
  {
    if (TryGetString(key, out var result) && result != key)
    {
      return result;
    }

    return fallback;
  }

  public Localizer For(string? moduleHandle)
  {
    var root = _root ?? this;
    return string.IsNullOrWhiteSpace(moduleHandle) ? root : new Localizer(root, moduleHandle);
  }

  ILocalizer ILocalizer.For(string? moduleHandle) => For(moduleHandle);

  public string this[string key]
  {
    get
    {
      if (TryGetString(key, out var res))
      {
        return res;
      }

      return key;
    }
  }

  public string of(string key) => this[key];

  public string this[string key, params object[] args]
  {
    get
    {
      var template = this[key];
      return args.Length > 0 ? string.Format(System.Globalization.CultureInfo.CurrentUICulture, template, args) : template;
    }
  }
}
