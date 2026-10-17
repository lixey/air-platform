namespace Air.Data.Querying;

public sealed record SortKey(LambdaExpression Selector, bool Descending);
