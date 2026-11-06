using Platform.Api.Serialization.Internals;

namespace Platform.Api.Serialization;

public static class PlatformJsonOptions
{
  public static JsonSerializerOptions DefaultOptions
  {
    get
    {
      if (field is not null)
      {
        return field;
      }

      var opts = new JsonSerializerOptions();
      ConfigurePlatformJson(opts);
      return field = opts;
    }
  }

  public static void ConfigurePlatformJson(JsonSerializerOptions options)
  {
    var resolver = new DefaultJsonTypeInfoResolver();
    resolver.Modifiers.Add(ModifySlotPolymorphism);
    options.TypeInfoResolver = resolver;
    options.PropertyNameCaseInsensitive = true;
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.ReferenceHandler = ReferenceHandler.IgnoreCycles;

    // Resilient scalar converters for numbers & dates
    JsonConverter[] converters =
    [
      new ResilientDecimalConverter(),
      new ResilientNullableDecimalConverter(),
      new ResilientIntConverter(),
      new ResilientNullableIntConverter(),
      new ResilientLongConverter(),
      new ResilientNullableLongConverter(),
      new ResilientDateTimeConverter(),
      new ResilientNullableDateTimeConverter()
    ];

    foreach (var converter in converters)
    {
      var converterType = converter.GetType();
      if (!options.Converters.Any(c => c.GetType() == converterType))
      {
        options.Converters.Add(converter);
      }
    }
  }

  private static void ModifySlotPolymorphism(JsonTypeInfo info)
  {
    if (info.Kind != JsonTypeInfoKind.Object)
    {
      return;
    }

    if (!info.Type.IsAbstract && info.Type.IsClass && typeof(IUiSlotModel).IsAssignableFrom(info.Type))
    {
      var kind = info.Type.Name.EndsWith("Model") ? info.Type.Name[..^5] : info.Type.Name;
      if (!info.Properties.Any(p => p.Name.Equals("kind", StringComparison.OrdinalIgnoreCase)))
      {
        var property = info.CreateJsonPropertyInfo(typeof(string), "kind");
        property.Get = _ => kind;
        property.Order = int.MinValue;
        info.Properties.Insert(0, property);
      }
    }
    else if (info.Type.IsAbstract && info.Type.IsClass && typeof(IUiSlotModel).IsAssignableFrom(info.Type))
    {
      var slotType = info.Type;
      var derived = slotType.Assembly.GetTypes()
        .Concat(AppDomain.CurrentDomain.GetAssemblies().SelectMany(a =>
        {
          try
          {
            return a.GetTypes();
          }
          catch
          {
            return [];
          }
        }))
        .Where(t => t.IsClass && !t.IsAbstract && slotType.IsAssignableFrom(t))
        .Distinct()
        .ToList();

      info.PolymorphismOptions = new JsonPolymorphismOptions();
      foreach (var type in derived)
      {
        info.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(type));
      }
    }
  }
}
