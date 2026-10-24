namespace Platform.Data.Querying;

public enum QueryError
{
  UnknownField,
  NotFilterable,
  NotSortable,
  NotSearchable,
  OperatorNotAllowed,
  InvalidValue,
  TooManyValues,
  FilterTooComplex,
  TooManySortRules,
  SearchTooLong
}
