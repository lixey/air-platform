using System.Runtime.CompilerServices;

namespace Platform.App.Form.Internals;

internal sealed class FormElementBinding
{
  private static readonly ConditionalWeakTable<FormElementDto, FormElementBinding> Bindings = new();

  public static FormElementBinding Of(FormElementDto element) =>
    Bindings.GetOrCreateValue(element);

  public static FormElementBinding? TryGet(FormElementDto element) =>
    Bindings.TryGetValue(element, out var binding) ? binding : null;

  public static void Link(FormElementDto source, FormElementDto clone)
  {
    if (Bindings.TryGetValue(source, out var binding))
    {
      Bindings.AddOrUpdate(clone, binding);
    }
  }

  public Func<DbContext, long, CancellationToken, Task<List<long>>>? JoinLoader { get; set; }
  public Func<DbContext, long, IReadOnlyList<long>, CancellationToken, Task<bool>>? JoinSyncer { get; set; }
  public string? UniqueMessage { get; set; }
  public List<string>? UniqueWithin { get; set; }
  public bool? ExplicitRequired { get; set; }
  public bool HasExplicitLabel { get; set; }
  public string? NavigationPropertyName { get; set; }
  public Type? ResolvedPropertyType { get; set; }
  public Type? ReferenceServiceType { get; set; }
  public bool AllowsCreate { get; set; }
  public Type? CreateFormServiceType { get; set; }
  public bool IsRightsTree { get; set; }
  public Func<object, object?>? InfoValueGetter { get; set; }
  public List<string>? InfoNavigationPaths { get; set; }
}
