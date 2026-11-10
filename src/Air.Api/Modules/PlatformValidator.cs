namespace Platform.Api.Modules;

/// <summary>
/// Fast fail-closed startup doctor that verifies registered modules, features, rights, routes,
/// service bindings, and vertical module isolation before the host accepts traffic.
/// </summary>
public static class PlatformValidator
{
  public static void Validate(ModuleCatalog catalog, IServiceProvider? services = null)
  {
    ArgumentNullException.ThrowIfNull(catalog);

    var moduleHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var registeredFeatures = catalog.AllFeatures.ToHashSet();

    foreach (var module in catalog.Modules)
    {
      CodeRows.EnsurePascalCaseHandle(module.Handle, $"Module '{module.GetType().Name}'");
      if (!moduleHandles.Add(module.Handle))
      {
        throw new InvalidOperationException($"Duplicate module handle '{module.Handle}'.");
      }

      foreach (var node in ModuleCatalog.Flatten(module.Menu.Nodes))
      {
        if (!node.IsSeparator && !string.IsNullOrWhiteSpace(node.Handle))
        {
          CodeRows.EnsurePascalCaseHandle(node.Handle, $"Menu node in module '{module.Handle}'");
        }

        if (node.Feature is not null && !registeredFeatures.Contains(node.Feature))
        {
          throw new InvalidOperationException(
            $"Menu node '{node.Handle}' in module '{module.Handle}' references unregistered feature '{node.Feature.Key}'.");
        }
      }
    }

    var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var feat in registeredFeatures)
    {
      CodeRows.EnsurePascalCaseHandle(feat.Module, $"Feature '{feat.Key}' module");
      CodeRows.EnsurePascalCaseHandle(feat.Handle, $"Feature '{feat.Key}' handle");

      if (!moduleHandles.Contains(feat.Module))
      {
        throw new InvalidOperationException(
          $"Feature '{feat.Key}' declares module '{feat.Module}', which is not registered in ModuleCatalog.");
      }

      if (!feat.IsFrozen)
      {
        throw new InvalidOperationException($"Feature '{feat.Key}' was not frozen during module registration.");
      }

      if (!seenKeys.Add(feat.Key))
      {
        throw new InvalidOperationException($"Duplicate feature key '{feat.Key}'.");
      }

      if (!seenPaths.Add(feat.Path))
      {
        throw new InvalidOperationException($"Duplicate feature path '{feat.Path}'.");
      }

      if (feat.ServiceType is not null)
      {
        if (feat.ServiceType.IsAbstract || !typeof(IPlatformService).IsAssignableFrom(feat.ServiceType))
        {
          throw new InvalidOperationException(
            $"Feature '{feat.Key}' binds invalid service type '{feat.ServiceType.FullName}' (must be a concrete IPlatformService).");
        }
      }

      foreach (var right in feat.Rights.Values)
      {
        if (right.Key.Length > Right.MaxKeyLength)
        {
          throw new InvalidOperationException(
            $"Right '{right.Key}' exceeds maximum length {Right.MaxKeyLength}.");
        }
      }
    }

    using var scope = services?.CreateScope();
    var scopedServices = scope?.ServiceProvider;
    var seenServicePaths = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

    var knownHandles = catalog.Modules.Select(m => m.Handle).ToArray();
    foreach (var module in catalog.Modules)
    {
      var serviceTypes = ModuleCatalog.GetModuleServiceTypes(module, knownHandles);

      foreach (var serviceType in serviceTypes)
      {
        var routePath = ServicePathResolver.Resolve(serviceType, knownHandles);
        if (!routePath.Contains('/'))
        {
          throw new InvalidOperationException(
            $"Service '{serviceType.FullName}' resolved to '{routePath}' without a module prefix. Prefix the service class name with its module handle (e.g. '{module.Handle}...Service').");
        }

        if (seenServicePaths.TryGetValue(routePath, out var otherType) && otherType != serviceType)
        {
          throw new InvalidOperationException(
            $"Duplicate service route '{routePath}' shared by '{otherType.FullName}' and '{serviceType.FullName}'.");
        }

        if (catalog.FindServiceType(routePath) is { } registeredType && registeredType != serviceType)
        {
          throw new InvalidOperationException(
            $"Catalog route mismatch for '{routePath}': catalog registered '{registeredType.FullName}' instead of '{serviceType.FullName}'.");
        }

        if (catalog.FindServiceType(routePath) is null)
        {
          throw new InvalidOperationException(
            $"Service '{serviceType.FullName}' resolved to '{routePath}' but was not registered under that route in ModuleCatalog.");
        }

        seenServicePaths[routePath] = serviceType;

        var instance = (scopedServices?.GetService(serviceType) ?? catalog.CreateServiceInstance(serviceType, scopedServices)) as IPlatformService
          ?? throw new InvalidOperationException(
            $"Cannot instantiate service '{serviceType.FullName}' during startup validation.");

        var feat = instance.Feature
          ?? throw new InvalidOperationException($"Service '{serviceType.FullName}' returned a null Feature.");

        if (string.IsNullOrWhiteSpace(feat.Module) || !registeredFeatures.Contains(feat))
        {
          throw new InvalidOperationException(
            $"Service '{serviceType.FullName}' binds feature '{feat.Key}', which is not registered in any module.");
        }

        ValidateServiceMethodAttributes(catalog, serviceType, feat);
        instance.ValidateConfiguration(catalog);
      }
    }
  }

  private static void ValidateServiceMethodAttributes(ModuleCatalog catalog, Type serviceType, Feature serviceFeature)
  {
    foreach (var method in serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
    {
      if (method.GetCustomAttribute<RequireRightAttribute>(inherit: true) is { } reqRight)
      {
        var targetFeat = catalog.FindFeature(reqRight.Service)
          ?? throw new InvalidOperationException(
            $"Method '{serviceType.Name}.{method.Name}' references '[RequireRight<{reqRight.Service.Name}>(\"{reqRight.ActionName}\")]', whose service has no registered Feature.");

        if (!targetFeat.HasAction(reqRight.ActionName))
        {
          throw new InvalidOperationException(
            $"Method '{serviceType.Name}.{method.Name}' requires undeclared right '{targetFeat.Key}.{reqRight.ActionName}'.");
        }
      }

      if (method.GetCustomAttribute<RequireActionAttribute>(inherit: true) is { FallbackToView: false } reqAction)
      {
        if (!serviceFeature.HasAction(reqAction.ActionName))
        {
          throw new InvalidOperationException(
            $"Method '{serviceType.Name}.{method.Name}' requires undeclared action '{serviceFeature.Key}.{reqAction.ActionName}'.");
        }
      }
    }
  }
}
