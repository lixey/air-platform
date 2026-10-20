using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Air.App.Modules;

public sealed class Right
{
  public const int MaxKeyLength = 200;

  internal Right(string key, string action, Feature? feature, bool isPattern = false)
  {
    Key = key;
    Action = action;
    Feature = feature;
    IsPattern = isPattern;
  }

  public string Key { get; }
  public string Action { get; }
  public Feature? Feature { get; }
  public bool IsPattern { get; }

  public override string ToString() => Key;
  public static implicit operator string(Right right) => right.Key;
}

public abstract class FeatureAccess
{
  public const string AccessToPrefix = "AccessTo";

  internal Feature Feature { get; set; } = null!;

  protected Right Right([CallerMemberName] string name = "")
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    return Feature.Action(AccessToPrefix + name);
  }
}

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
    Path = $"{module.ToKebabCase()}/{name.ToKebabCase()}";
    configure?.Invoke(this);
    if (serviceType != null)
    {
      FeaturesByServiceType[serviceType] = this;
    }
  }

  public string Label { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;

  private static string PascalToSpaced(string s) =>
    System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z])", "$1 $2");

  private static string Pluralize(string word)
  {
    if (string.IsNullOrEmpty(word)) return string.Empty;
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
    CleanName = source.CleanName;
    ServiceType = source.ServiceType;
    Path = source.Path;
    IsReadOnly = source.IsReadOnly;
    HasDelete = source.HasDelete;
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

  public Feature<TAccess> WithAccess<TAccess>() where TAccess : FeatureAccess, new()
  {
    var typed = new Feature<TAccess>(this);
    typed.AccessTo.Feature = typed;
    return typed;
  }
}

public sealed class Feature<TAccess> : Feature where TAccess : FeatureAccess, new()
{
  internal Feature(Feature source) : base(source)
  {
    AccessTo = new TAccess { Feature = this };
  }

  public TAccess AccessTo { get; }

  public new Feature<TAccess> ReadOnly(bool isReadOnly = true)
  {
    base.ReadOnly(isReadOnly);
    return this;
  }

  public new Feature<TAccess> WithoutDelete()
  {
    base.WithoutDelete();
    return this;
  }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class ModuleAttribute : Attribute
{
  public ModuleAttribute(string handle)
  {
    Air.Data.CodeRows.EnsurePascalCaseHandle(handle, "Module");
    Handle = handle;
  }

  public string Handle { get; }
  public string? Name { get; set; }
  public string? Icon { get; set; }
}

public abstract class ModuleDefinition
{
  public abstract string GetHandle();
  public abstract string GetName();
  public virtual string GetIcon() => "folder";
  public abstract IReadOnlyList<Feature> GetFeatures();
}

public abstract class ModuleDefinition<TSelf> : ModuleDefinition where TSelf : ModuleDefinition<TSelf>
{
  private static readonly ModuleAttribute ModuleAttr =
    typeof(TSelf).GetCustomAttribute<ModuleAttribute>()
    ?? throw new InvalidOperationException($"'{typeof(TSelf).Name}' must declare [Module(handle)].");

  private static readonly Lazy<IReadOnlyList<Feature>> LazyFeatures = new(() =>
  {
    var list = new List<Feature>();
    foreach (var field in typeof(TSelf).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
      if (typeof(Feature).IsAssignableFrom(field.FieldType) && field.GetValue(null) is Feature feat)
      {
        list.Add(feat);
      }
    }
    foreach (var prop in typeof(TSelf).GetProperties(BindingFlags.Public | BindingFlags.Static))
    {
      if (typeof(Feature).IsAssignableFrom(prop.PropertyType) && prop.GetValue(null) is Feature feat)
      {
        list.Add(feat);
      }
    }
    return list.AsReadOnly();
  });

  public static string ModuleHandle => ModuleAttr.Handle;
  public static Right All => new($"{ModuleHandle}.*", "*", null, isPattern: true);

  public sealed override string GetHandle() => ModuleAttr.Handle;
  public sealed override string GetName() => ModuleAttr.Name ?? (ModuleAttr.Handle.Length > 0 ? char.ToUpperInvariant(ModuleAttr.Handle[0]) + ModuleAttr.Handle[1..] : ModuleAttr.Handle);
  public sealed override string GetIcon() => ModuleAttr.Icon ?? "folder";
  public sealed override IReadOnlyList<Feature> GetFeatures() => LazyFeatures.Value;

  protected static Feature Feature<TService>([CallerMemberName] string name = "") =>
    new(ModuleHandle, name, typeof(TService));

  protected static Feature Feature<TService>(Action<Feature> configure, [CallerMemberName] string name = "") =>
    new(ModuleHandle, name, typeof(TService), configure);

  protected static Feature Feature([CallerMemberName] string name = "") =>
    new(ModuleHandle, name);

  protected static Feature Feature(Action<Feature> configure, [CallerMemberName] string name = "") =>
    new(ModuleHandle, name, configure: configure);
}
