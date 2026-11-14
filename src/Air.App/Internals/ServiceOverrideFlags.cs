namespace Platform.App.Internals;

internal sealed record ServiceOverrideFlags(
  bool OverridesSetElements,
  bool OverridesSetElementsAsync,
  bool OverridesSetFormActions,
  bool OverridesSetFormActionsAsync,
  bool OverridesSetFormNav,
  bool OverridesSetFormNavAsync,
  bool OverridesSetColumns,
  bool OverridesSetColumnsAsync,
  bool OverridesSetGridActions,
  bool OverridesSetGridActionsAsync,
  bool OverridesSetGridNav,
  bool OverridesSetGridNavAsync,
  bool OverridesSetPanels,
  bool OverridesSetPanelsAsync)
{
  private static readonly ConcurrentDictionary<Type, ServiceOverrideFlags> Cache = new();

  public bool HasAsyncFormMetadata =>
    OverridesSetElementsAsync || OverridesSetFormActionsAsync || OverridesSetFormNavAsync;

  public bool HasAsyncGridMetadata =>
    OverridesSetColumnsAsync || OverridesSetGridActionsAsync || OverridesSetGridNavAsync;

  public static ServiceOverrideFlags For(Type serviceType) =>
    Cache.GetOrAdd(serviceType, Build);

  private static ServiceOverrideFlags Build(Type serviceType)
  {
    var formBase = FindOpenGenericBase(serviceType, typeof(FormService<>));
    var gridBase = FindOpenGenericBase(serviceType, typeof(GridService<,>));
    var isScreen = typeof(ScreenService).IsAssignableFrom(serviceType);

    return new ServiceOverrideFlags(
      OverridesSetElements: formBase != null && OverridesMethod(serviceType, formBase, "SetElements"),
      OverridesSetElementsAsync: formBase != null && OverridesMethod(serviceType, formBase, "SetElementsAsync"),
      OverridesSetFormActions: formBase != null && OverridesMethod(serviceType, formBase, "SetActions"),
      OverridesSetFormActionsAsync: formBase != null && OverridesMethod(serviceType, formBase, "SetActionsAsync"),
      OverridesSetFormNav: formBase != null && OverridesMethod(serviceType, formBase, "SetNav"),
      OverridesSetFormNavAsync: formBase != null && OverridesMethod(serviceType, formBase, "SetNavAsync"),
      OverridesSetColumns: gridBase != null && OverridesMethod(serviceType, gridBase, "SetColumns"),
      OverridesSetColumnsAsync: gridBase != null && OverridesMethod(serviceType, gridBase, "SetColumnsAsync"),
      OverridesSetGridActions: gridBase != null && OverridesMethod(serviceType, gridBase, "SetActions"),
      OverridesSetGridActionsAsync: gridBase != null && OverridesMethod(serviceType, gridBase, "SetActionsAsync"),
      OverridesSetGridNav: gridBase != null && OverridesMethod(serviceType, gridBase, "SetNav"),
      OverridesSetGridNavAsync: gridBase != null && OverridesMethod(serviceType, gridBase, "SetNavAsync"),
      OverridesSetPanels: isScreen && OverridesMethod(serviceType, typeof(ScreenService), "SetPanels"),
      OverridesSetPanelsAsync: isScreen && OverridesMethod(serviceType, typeof(ScreenService), "SetPanelsAsync"));
  }

  private static bool OverridesMethod(Type serviceType, Type rootBaseType, string methodName)
  {
    var method = serviceType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
    return method != null && method.DeclaringType != rootBaseType;
  }

  private static Type? FindOpenGenericBase(Type type, Type openGeneric)
  {
    for (var cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
    {
      if (cur.IsGenericType && cur.GetGenericTypeDefinition() == openGeneric)
      {
        return cur;
      }
    }

    return null;
  }
}
