using Microsoft.EntityFrameworkCore.Metadata;
using Platform.App.Internals;

namespace Platform.App.Form.Internals;

internal static class FormConstraintExtractor
{
  public static void ValidateNavigationElements(Type rootEntityType, IReadOnlyList<FormElementDto> elements)
  {
    foreach (var el in elements)
    {
      var navPropName = FormElementBinding.TryGet(el)?.NavigationPropertyName;
      if (string.IsNullOrEmpty(navPropName))
      {
        continue;
      }

      if (el.ValueKind != FormValueKind.Reference &&
          el.ValueKind != FormValueKind.List &&
          el.Model is not FormInfoModel &&
          !el.IsServerOwned)
      {
        throw new InvalidOperationException(
          $"Navigation property '{rootEntityType.Name}.{navPropName}' added to form elements must configure .AsReference<...>() or .AsInfo().");
      }
    }
  }

  public static void ApplyElementConstraints(
    Type rootEntityType,
    IEntityType? efEntityType,
    IReadOnlyList<FormElementDto> elements)
  {
    ValidateNavigationElements(rootEntityType, elements);

    foreach (var el in elements)
    {
      var binding = FormElementBinding.TryGet(el);
      var navPropName = binding?.NavigationPropertyName;
      if (!string.IsNullOrEmpty(navPropName) && efEntityType != null)
      {
        var efNav = efEntityType.GetNavigations()
          .FirstOrDefault(n => !n.IsCollection && string.Equals(n.Name, navPropName, StringComparison.OrdinalIgnoreCase));
        if (efNav?.ForeignKey.Properties is [{ } fkMetadata])
        {
          el.Path = Elements.ToCamelPath(fkMetadata.Name);
          var mutableBinding = binding ?? FormElementBinding.Of(el);
          mutableBinding.ResolvedPropertyType = fkMetadata.ClrType;
          if (!mutableBinding.HasExplicitLabel)
          {
            el.Label = Elements.PathToLabel(el.ReferencePath ?? navPropName, rootEntityType);
          }
        }
      }

      if (string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var explicitRequired = binding?.ExplicitRequired;
      if ((el.IsDisabled || el.IsServerOwned) && explicitRequired != true)
      {
        el.IsRequired = false;
      }

      var entityProp = PropertyAccessorCache.GetProperty(rootEntityType, el.Path);
      var propType = entityProp?.PropertyType;
      var efProp = efEntityType != null && entityProp != null && !el.IsInputOnly
        ? efEntityType.FindProperty(entityProp.Name)
        : null;

      if (efProp != null)
      {
        el.Constraints.AllowsNull ??= efProp.IsNullable;
        if (efProp.GetMaxLength() is { } storageMax && storageMax >= 0)
        {
          el.Constraints.MaxLength = el.Constraints.MaxLength.HasValue
            ? Math.Min(el.Constraints.MaxLength.Value, storageMax)
            : storageMax;
          el.Constraints.LengthUnit ??= propType == typeof(byte[]) ? FormLengthUnit.Bytes : FormLengthUnit.Utf16;
        }

        if (propType != null && (Nullable.GetUnderlyingType(propType) ?? propType) == typeof(decimal))
        {
          el.Constraints.Precision ??= efProp.GetPrecision();
          el.Constraints.Scale ??= efProp.GetScale();
        }

        if (!efProp.IsNullable && explicitRequired != false && !el.IsDisabled && !el.IsServerOwned)
        {
          if (propType == typeof(string) || efProp.IsForeignKey())
          {
            el.IsRequired = true;
          }
        }
      }

      var annotations = entityProp != null
        ? entityProp.GetCustomAttributes<ValidationAttribute>()
        : [];

      foreach (var attr in annotations)
      {
        switch (attr)
        {
          case RequiredAttribute req:
            if (explicitRequired != false && !el.IsDisabled && !el.IsServerOwned)
            {
              el.IsRequired = true;
              el.Constraints.AllowsNull = false;
              if (propType == typeof(string) && !req.AllowEmptyStrings)
              {
                el.Constraints.RejectsBlank = true;
              }
            }

            break;
          case StringLengthAttribute strLen:
            el.Constraints.MaxLength = el.Constraints.MaxLength.HasValue
              ? Math.Min(el.Constraints.MaxLength.Value, strLen.MaximumLength)
              : strLen.MaximumLength;
            if (strLen.MinimumLength > 0)
            {
              el.Constraints.MinLength = Math.Max(el.Constraints.MinLength ?? 0, strLen.MinimumLength);
            }

            el.Constraints.LengthUnit ??= FormLengthUnit.Utf16;
            break;
          case MaxLengthAttribute maxLen when maxLen.Length >= 0:
            el.Constraints.MaxLength = el.Constraints.MaxLength.HasValue
              ? Math.Min(el.Constraints.MaxLength.Value, maxLen.Length)
              : maxLen.Length;
            el.Constraints.LengthUnit ??= propType == typeof(byte[])
              ? FormLengthUnit.Bytes
              : (propType != null && typeof(ICollection).IsAssignableFrom(propType) ? FormLengthUnit.Items : FormLengthUnit.Utf16);
            break;
          case MinLengthAttribute minLen when minLen.Length > 0:
            el.Constraints.MinLength = Math.Max(el.Constraints.MinLength ?? 0, minLen.Length);
            el.Constraints.LengthUnit ??= propType == typeof(byte[])
              ? FormLengthUnit.Bytes
              : (propType != null && typeof(ICollection).IsAssignableFrom(propType) ? FormLengthUnit.Items : FormLengthUnit.Utf16);
            break;
          case RangeAttribute rangeAttr:
            if (el.Constraints.Min == null && FilterNodeEvaluator.TryConvertToDecimal(rangeAttr.Minimum, out var rangeMin))
            {
              el.Constraints.Min = rangeMin;
            }

            if (el.Constraints.Max == null && FilterNodeEvaluator.TryConvertToDecimal(rangeAttr.Maximum, out var rangeMax))
            {
              el.Constraints.Max = rangeMax;
            }

            break;
        }
      }

      if (propType != null && propType.IsValueType && Nullable.GetUnderlyingType(propType) == null)
      {
        el.Constraints.AllowsNull = false;
      }

      if (el.IsRequired)
      {
        el.Constraints.AllowsNull = false;
        if (propType == typeof(string))
        {
          el.Constraints.RejectsBlank = true;
        }
        else if (propType != null && typeof(ICollection).IsAssignableFrom(propType))
        {
          el.Constraints.MinLength = Math.Max(el.Constraints.MinLength ?? 0, 1);
          el.Constraints.LengthUnit ??= FormLengthUnit.Items;
        }
      }
    }
  }
}
