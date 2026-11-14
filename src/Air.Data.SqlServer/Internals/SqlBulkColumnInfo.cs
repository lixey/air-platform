using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Platform.Data.SqlServer.Internals;

internal readonly record struct SqlBulkColumnInfo(
  IProperty Property,
  IClrPropertyGetter Getter,
  string ColumnName,
  ValueConverter? Converter);
