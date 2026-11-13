namespace Platform.Api.Localization;

public sealed class Localizer : ILocalizer
{
  private static int _assemblyGeneration = 1;

  static Localizer()
  {
    AppDomain.CurrentDomain.AssemblyLoad += static (_, _) => Interlocked.Increment(ref _assemblyGeneration);
  }

  public static readonly Localizer Instance = new();

  private readonly Localizer? _root;
  private readonly string? _moduleHandle;
  private readonly Lock _sync = new();
  private readonly List<System.Resources.ResourceManager> _managers = [];
  private volatile System.Resources.ResourceManager[] _managersSnapshot = [];
  private readonly ConcurrentDictionary<string, string> _canonicalKeys = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<(string Culture, string Key), string> _cache = new();
  private readonly ConcurrentDictionary<string, Localizer> _moduleLocalizers = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> _loadedNames = new(StringComparer.Ordinal);
  private volatile ICultureCatalog _cultures = CultureCatalog.DefaultInstance;
  private volatile bool _hasUnindexedManager;
  private int _loadedGeneration;

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

  public void ConfigureCultures(ICultureCatalog cultures)
  {
    ArgumentNullException.ThrowIfNull(cultures);
    if (_root != null)
    {
      _root.ConfigureCultures(cultures);
      return;
    }

    lock (_sync)
    {
      _cultures = cultures;
      foreach (var mgr in _managers)
      {
        IndexManagerCulturesLocked(mgr);
      }

      _cache.Clear();
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
    _managersSnapshot = [.. _managers];
    IndexKeysLocked(mgr, System.Globalization.CultureInfo.InvariantCulture);
    IndexManagerCulturesLocked(mgr);
    _cache.Clear();
  }

  private void IndexManagerCulturesLocked(System.Resources.ResourceManager mgr)
  {
    foreach (var registered in _cultures.Cultures)
    {
      IndexKeysLocked(mgr, registered.CultureInfo);
      if (!string.IsNullOrEmpty(registered.CultureInfo.Parent.Name))
      {
        IndexKeysLocked(mgr, registered.CultureInfo.Parent);
      }
    }
  }

  private void IndexKeysLocked(System.Resources.ResourceManager mgr, System.Globalization.CultureInfo culture)
  {
    try
    {
      var set = mgr.GetResourceSet(culture, true, true);
      if (set == null)
      {
        return;
      }

      foreach (System.Collections.DictionaryEntry entry in set)
      {
        if (entry.Key is string k)
        {
          _canonicalKeys.TryAdd(k, k);
        }
      }
    }
    catch
    {
      _hasUnindexedManager = true;
    }
  }

  public void EnsureManagersLoaded()
  {
    if (_root != null)
    {
      _root.EnsureManagersLoaded();
      return;
    }

    var gen = Volatile.Read(ref _assemblyGeneration);
    if (Volatile.Read(ref _loadedGeneration) == gen)
    {
      return;
    }

    lock (_sync)
    {
      if (_loadedGeneration == gen)
      {
        return;
      }

      var assemblies = AppDomain.CurrentDomain.GetAssemblies();
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

      Volatile.Write(ref _loadedGeneration, gen);
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
    var cacheKey = (culture.Name, key);
    if (_cache.TryGetValue(cacheKey, out var cached))
    {
      result = cached;
      return true;
    }

    if (TryFormatSourceTitle(key, out var formattedSourceTitle))
    {
      _cache.TryAdd(cacheKey, formattedSourceTitle);
      result = formattedSourceTitle;
      return true;
    }

    _canonicalKeys.TryGetValue(key, out var canonical);
    if (canonical != null || _hasUnindexedManager)
    {
      var snapshot = _managersSnapshot;
      foreach (var mgr in snapshot)
      {
        try
        {
          var text = (canonical != null ? mgr.GetString(canonical, culture) : null)
            ?? (canonical != key ? mgr.GetString(key, culture) : null);
          if (!string.IsNullOrEmpty(text))
          {
            _cache.TryAdd(cacheKey, text);
            result = text;
            return true;
          }
        }
        catch { }
      }
    }

    if (UiTexts.English.TryGetValue(key, out var val) ||
        canonical != null && UiTexts.English.TryGetValue(canonical, out val))
    {
      _cache.TryAdd(cacheKey, val);
      result = val;
      return true;
    }

    result = key;
    return false;
  }

  private bool TryFormatSourceTitle(string key, out string formatted)
  {
    if (!string.Equals(key, "LegacyTitleFormat", StringComparison.OrdinalIgnoreCase) &&
        _managersSnapshot is [var rm, ..])
    {
      var cultures = (_root ?? this)._cultures;
      if (TryParseSourceTitle(rm, cultures, key, out var uiSource, out var dbSource) &&
          TryGetString("LegacyTitleFormat", out var template))
      {
        formatted = string.Format(System.Globalization.CultureInfo.CurrentUICulture, template, uiSource, dbSource);
        return true;
      }
    }

    formatted = string.Empty;
    return false;
  }

  private static bool TryParseSourceTitle(System.Resources.ResourceManager rm, ICultureCatalog cultures, string key, out string uiSource, out string dbSource)
  {
    if (TryParseByTemplate(key, rm.GetString("LegacyTitleFormat", System.Globalization.CultureInfo.InvariantCulture), out uiSource, out dbSource))
    {
      return true;
    }

    foreach (var registered in cultures.Cultures)
    {
      if (TryParseByTemplate(key, rm.GetString("LegacyTitleFormat", registered.CultureInfo), out uiSource, out dbSource))
      {
        return true;
      }
    }

    uiSource = string.Empty;
    dbSource = string.Empty;
    return false;
  }

  private static bool TryParseByTemplate(string key, string? template, out string uiSource, out string dbSource)
  {
    uiSource = string.Empty;
    dbSource = string.Empty;
    if (string.IsNullOrEmpty(template))
    {
      return false;
    }

    var p0 = template.IndexOf("{0}", StringComparison.Ordinal);
    var p1 = template.IndexOf("{1}", StringComparison.Ordinal);
    if (p0 < 0 || p1 <= p0 + 3)
    {
      return false;
    }

    var prefix = template[..p0];
    var sep = template[(p0 + 3)..p1];
    var suffix = template[(p1 + 3)..];
    if (!key.StartsWith(prefix, StringComparison.Ordinal) || !key.EndsWith(suffix, StringComparison.Ordinal))
    {
      return false;
    }

    var sepIdx = key.IndexOf(sep, prefix.Length, StringComparison.Ordinal);
    if (sepIdx < 0 || sepIdx + sep.Length > key.Length - suffix.Length)
    {
      return false;
    }

    uiSource = key[prefix.Length..sepIdx];
    dbSource = key[(sepIdx + sep.Length)..(key.Length - suffix.Length)];
    return true;
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
    return string.IsNullOrWhiteSpace(moduleHandle)
      ? root
      : root._moduleLocalizers.GetOrAdd(moduleHandle, static (h, r) => new Localizer(r, h), root);
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

  public string Of(string key) => this[key];

  public string this[string key, params object[] args]
  {
    get
    {
      var template = this[key];
      return args.Length > 0 ? string.Format(System.Globalization.CultureInfo.CurrentUICulture, template, args) : template;
    }
  }
}
