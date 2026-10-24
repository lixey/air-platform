namespace Platform.Data.Querying;

public static class QueryErrorExtensions
{
  public static string ToCode(this QueryError error) => error switch
  {
    QueryError.UnknownField => "query.unknownField",
    QueryError.NotFilterable => "query.notFilterable",
    QueryError.NotSortable => "query.notSortable",
    QueryError.NotSearchable => "query.notSearchable",
    QueryError.OperatorNotAllowed => "query.operatorNotAllowed",
    QueryError.InvalidValue => "query.invalidValue",
    QueryError.TooManyValues => "query.tooManyValues",
    QueryError.FilterTooComplex => "query.filterTooComplex",
    QueryError.TooManySortRules => "query.tooManySortRules",
    QueryError.SearchTooLong => "query.searchTooLong",
    _ => "query.error"
  };
}
