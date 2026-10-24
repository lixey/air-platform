namespace Platform.Data.Querying;

[Flags]
public enum FieldCapabilities
{
  None = 0,
  Filter = 1,
  Sort = 2,
  Search = 4,
  All = Filter | Sort | Search
}
