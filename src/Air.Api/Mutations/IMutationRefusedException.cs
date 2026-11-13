namespace Platform.Api.Mutations;

public interface IMutationRefusedException
{
  string Outcome { get; }
  string Title { get; }
  IReadOnlyList<MessageDto> Messages { get; }
}
