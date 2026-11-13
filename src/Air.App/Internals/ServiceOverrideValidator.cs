namespace Platform.App.Internals;

internal static class ServiceOverrideValidator
{
  public static void EnsureSingleSyncOrAsyncOverride(
    Type serviceType,
    Type baseGenericTypeDefinition,
    params (string SyncMethod, string AsyncMethod)[] methodPairs)
  {
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;

    for (var current = serviceType; current != null && current != typeof(object); current = current.BaseType)
    {
      if (current.IsGenericType && current.GetGenericTypeDefinition() == baseGenericTypeDefinition)
      {
        break;
      }

      foreach (var (syncMethod, asyncMethod) in methodPairs)
      {
        var declaresSync = current.GetMethods(Flags).Any(m => m.Name == syncMethod && IsFrameworkOverride(m, current));
        var declaresAsync = current.GetMethods(Flags).Any(m => m.Name == asyncMethod && IsFrameworkOverride(m, current));
        if (declaresSync && declaresAsync)
        {
          throw new InvalidOperationException(
            $"Service '{current.FullName}' overrides both '{syncMethod}' and '{asyncMethod}' in the same class. Override '{syncMethod}' when synchronous, or '{asyncMethod}' when awaiting database queries — never both.");
        }
      }
    }
  }

  private static bool IsFrameworkOverride(MethodInfo method, Type declaringClass) =>
    method.IsVirtual && method.GetBaseDefinition().DeclaringType != declaringClass;
}
