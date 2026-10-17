using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Air.App.Components;

namespace Air.App.Components;

public sealed class ResilientDecimalConverter : JsonConverter<decimal>
{
  public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null) return 0m;
    if (reader.TokenType == JsonTokenType.String && decimal.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
      return val;
    return reader.GetDecimal();
  }
  public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

public sealed class ResilientIntConverter : JsonConverter<int>
{
  public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null) return 0;
    if (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
      return val;
    return reader.GetInt32();
  }
  public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

public sealed class ResilientLongConverter : JsonConverter<long>
{
  public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null) return 0L;
    if (reader.TokenType == JsonTokenType.String && long.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
      return val;
    return reader.GetInt64();
  }
  public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

public sealed class ResilientDateTimeConverter : JsonConverter<DateTime>
{
  public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null) return DateTime.MinValue;
    if (reader.TokenType == JsonTokenType.String && DateTime.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
      return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    return DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);
  }
  public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
    writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("o"));
}

public sealed class ResilientNullableDateTimeConverter : JsonConverter<DateTime?>
{
  public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null) return null;
    if (reader.TokenType == JsonTokenType.String)
    {
      var str = reader.GetString();
      if (string.IsNullOrWhiteSpace(str)) return null;
      if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }
    return DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);
  }
  public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
  {
    if (value is null) writer.WriteNullValue();
    else writer.WriteStringValue(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("o"));
  }
}

public static class PlatformJsonOptions
{
  private static readonly Type[] SlotTypes =
  [
    typeof(PanelModel),
    typeof(CellModel),
    typeof(HeaderModel),
    typeof(FormElementModel),
    typeof(NavElementModel)
  ];

  private static JsonSerializerOptions? _defaultOptions;

  public static JsonSerializerOptions DefaultOptions
  {
    get
    {
      if (_defaultOptions is not null) return _defaultOptions;
      var opts = new JsonSerializerOptions();
      ConfigurePlatformJson(opts);
      return _defaultOptions = opts;
    }
  }

  public static void ConfigurePlatformJson(JsonSerializerOptions options)
  {
    var resolver = new DefaultJsonTypeInfoResolver();
    resolver.Modifiers.Add(ModifySlotPolymorphism);
    options.TypeInfoResolver = resolver;
    options.PropertyNameCaseInsensitive = true;
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

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

    if (!info.Type.IsAbstract && SlotTypes.Any(st => st.IsAssignableFrom(info.Type)))
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
    else if (info.Type.IsAbstract && SlotTypes.Any(st => st.IsAssignableFrom(info.Type)))
    {
      var slotType = info.Type;
      var derived = slotType.Assembly.GetTypes()
        .Concat(AppDomain.CurrentDomain.GetAssemblies().SelectMany(a =>
        {
          try { return a.GetTypes(); } catch { return []; }
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
