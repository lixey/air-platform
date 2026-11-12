namespace Platform.App.Form;

public sealed record ChildSumMapping(
  string ParentFieldPath,
  string ChildFieldPath,
  PropertyInfo? ParentProperty = null,
  LambdaExpression? ChildSelector = null);
