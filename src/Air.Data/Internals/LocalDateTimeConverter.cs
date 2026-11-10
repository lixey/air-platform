using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Platform.Data.Internals;

internal sealed class LocalDateTimeConverter() : ValueConverter<DateTime, DateTime>(
  v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified),
  v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified)
);
