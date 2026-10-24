namespace Platform.Data.Contracts;

/// <summary>
/// Names vocabulary classes whose const string members define the allowed union of string values.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OneOfAttribute(params Type[] vocabularies) : Attribute
{
  public IReadOnlyList<Type> Vocabularies { get; } = vocabularies;
}
