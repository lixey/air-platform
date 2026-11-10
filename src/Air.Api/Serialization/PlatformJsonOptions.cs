using Platform.Api.Serialization.Internals;

namespace Platform.Api.Serialization;

public static class PlatformJsonOptions
{
  private static readonly ConcurrentDictionary<Type, Type[]> DerivedSlotTypesCache = new();
  private static readonly ConditionalWeakTable<IJsonTypeInfoResolver, object> ConfiguredResolvers = new();
  private static readonly DefaultJsonTypeInfoResolver SharedSlotResolver = CreateSlotResolver();

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
    ArgumentNullException.ThrowIfNull(options);

    options.PropertyNameCaseInsensitive = true;
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.ReferenceHandler = ReferenceHandler.IgnoreCycles;

    if (options.TypeInfoResolver is null)
    {
      options.TypeInfoResolver = SharedSlotResolver;
    }
    else if (!ReferenceEquals(options.TypeInfoResolver, SharedSlotResolver) &&
             !ConfiguredResolvers.TryGetValue(options.TypeInfoResolver, out _))
    {
      var wrapped = options.TypeInfoResolver.WithAddedModifier(ModifySlotPolymorphism);
      ConfiguredResolvers.AddOrUpdate(wrapped, new object());
      options.TypeInfoResolver = wrapped;
    }

    JsonConverter[] converters =
    [
      new StrictNumberConverterFactory(),
      new FlexibleDateTimeConverter(),
      new FlexibleNullableDateTimeConverter()
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

  private static DefaultJsonTypeInfoResolver CreateSlotResolver()
  {
    var resolver = new DefaultJsonTypeInfoResolver();
    resolver.Modifiers.Add(ModifySlotPolymorphism);
    return resolver;
  }

  private static void ModifySlotPolymorphism(JsonTypeInfo info)
  {
    if (info.Kind != JsonTypeInfoKind.Object)
    {
      return;
    }

    if (!info.Type.IsAbstract && info.Type.IsClass && typeof(IUiSlotModel).IsAssignableFrom(info.Type))
    {
      var kind = info.Type.Name.EndsWith("Model", StringComparison.Ordinal) ? info.Type.Name[..^5] : info.Type.Name;
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
      var derived = DerivedSlotTypesCache.GetOrAdd(info.Type, static slotType =>
        slotType.Assembly.GetTypes()
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
          .Where(t => t is { IsClass: true, IsAbstract: false } && slotType.IsAssignableFrom(t))
          .Distinct()
          .ToArray());

      info.PolymorphismOptions = new JsonPolymorphismOptions();
      foreach (var type in derived)
      {
        info.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(type));
      }
    }
  }
}
