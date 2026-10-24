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
    options.Converters.Add(new ResilientDecimalConverter());
    options.Converters.Add(new ResilientIntConverter());
    options.Converters.Add(new ResilientLongConverter());
    options.Converters.Add(new ResilientDateTimeConverter());
    options.Converters.Add(new ResilientNullableDateTimeConverter());
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
