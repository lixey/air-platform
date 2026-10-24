namespace Platform.Api.Modules;

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
