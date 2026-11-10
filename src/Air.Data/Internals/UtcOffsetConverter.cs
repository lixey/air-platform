using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Platform.Data.Internals;

internal sealed class UtcOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
  v => v.ToUniversalTime(),
  v => v.ToUniversalTime()
);
