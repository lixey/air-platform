using Microsoft.EntityFrameworkCore.Metadata;

namespace Platform.App.Grid.Internals;

internal static class ColumnRequiredResolver
{
  internal static bool? Resolve(Type ownerType, string? path, IModel? efModel = null)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return null;
    }

    var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length == 0)
    {
      return null;
    }

    if (efModel?.FindEntityType(ownerType) is { } efEntity)
    {
      var currentEntity = efEntity;
      var broken = false;
      for (var i = 0; i < segments.Length - 1; i++)
      {
        var nav = currentEntity.GetNavigations().FirstOrDefault(n => string.Equals(n.Name, segments[i], StringComparison.OrdinalIgnoreCase));
        if (nav is null)
        {
          broken = true;
          break;
        }

        if (nav.IsCollection || !nav.IsOnDependent || nav.ForeignKey.Properties.Any(p => p.IsNullable))
        {
          return false;
        }

        currentEntity = nav.TargetEntityType;
      }

      if (!broken)
      {
        var leaf = segments[^1];
        if (currentEntity.GetNavigations().FirstOrDefault(n => string.Equals(n.Name, leaf, StringComparison.OrdinalIgnoreCase)) is { } leafNav)
        {
          return !leafNav.IsCollection && leafNav.IsOnDependent && !leafNav.ForeignKey.Properties.Any(p => p.IsNullable);
        }

        if (currentEntity.GetProperties().FirstOrDefault(p => string.Equals(p.Name, leaf, StringComparison.OrdinalIgnoreCase)) is { } leafProp)
        {
          return !leafProp.IsNullable;
        }

        if (!leaf.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
            currentEntity.GetProperties().FirstOrDefault(p => string.Equals(p.Name, leaf + "Id", StringComparison.OrdinalIgnoreCase)) is { } fkProp)
        {
          return !fkProp.IsNullable;
        }
      }
    }

    var currentType = ownerType;
    var nullability = new NullabilityInfoContext();
    for (var i = 0; i < segments.Length - 1; i++)
    {
      var prop = currentType.GetProperty(segments[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
      if (prop is null)
      {
        return null;
      }

      if (currentType.GetProperty(prop.Name + "Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is { } interFk)
      {
        if (Nullable.GetUnderlyingType(interFk.PropertyType) is not null)
        {
          return false;
        }
      }
      else if (Nullable.GetUnderlyingType(prop.PropertyType) is not null ||
               !prop.PropertyType.IsValueType && nullability.Create(prop).ReadState == NullabilityState.Nullable)
      {
        return false;
      }

      currentType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
    }

    var leafName = segments[^1];
    var leafClr = currentType.GetProperty(leafName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
    var fkClr = !leafName.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
      ? currentType.GetProperty(leafName + "Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
      : null;

    if (fkClr is not null && (leafClr is null || !leafClr.PropertyType.IsValueType && leafClr.PropertyType != typeof(string)))
    {
      return fkClr.GetCustomAttribute<RequiredAttribute>() is not null ||
             fkClr.PropertyType.IsValueType && Nullable.GetUnderlyingType(fkClr.PropertyType) is null;
    }

    if (leafClr is null)
    {
      return null;
    }

    if (leafClr.GetCustomAttribute<RequiredAttribute>() is not null)
    {
      return true;
    }

    if (leafClr.PropertyType.IsValueType)
    {
      return Nullable.GetUnderlyingType(leafClr.PropertyType) is null;
    }

    return nullability.Create(leafClr).ReadState == NullabilityState.NotNull;
  }
}
