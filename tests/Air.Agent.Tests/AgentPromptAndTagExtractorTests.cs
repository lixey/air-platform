using Platform.Agent.Internals;

namespace Platform.Agent.Tests;

public sealed class AgentPromptAndTagExtractorTests
{
  [Fact]
  public async Task BuildPromptAsync_IncludesExplicitVisualBlockJsonSchemasWithoutHardcodedCurrency()
  {
    var req = new AgentTurnRequest
    {
      SessionId = "prompt-schema-1",
      Owner = new AgentOwnerScope(1, 1),
      Message = "Show sales chart"
    };

    var prompt = await AgentPromptBuilder.BuildPromptAsync(req, contextJson: "{}", ct: CancellationToken.None);
    Assert.Contains("\"slices\"", prompt, StringComparison.Ordinal);
    Assert.Contains("\"categories\"", prompt, StringComparison.Ordinal);
    Assert.Contains("\"series\"", prompt, StringComparison.Ordinal);
    Assert.Contains("\"columns\"", prompt, StringComparison.Ordinal);
    Assert.Contains("<ui_kpi>[{", prompt, StringComparison.Ordinal);
    Assert.Contains("<ui_chart>{", prompt, StringComparison.Ordinal);
    Assert.Contains("<ui_table>{", prompt, StringComparison.Ordinal);
    Assert.DoesNotContain("\"UAH\"", prompt, StringComparison.Ordinal);
  }

  [Fact]
  public void TagExtractor_IgnoresInlineCodeMentionsAndExtractsRealBlocksFollowingMentions()
  {
    const string proseOnly = "Я можу вивести `<ui_proposal>...</ui_proposal>` або `<agent_memory>{\"kind\":\"Fact\",\"title\":\"T\",\"content\":\"C\"}</agent_memory>` у прикладі.";
    Assert.Empty(AgentTagBlockExtractor.ExtractInnerBlocks(proseOnly, "<ui_proposal>", "</ui_proposal>"));
    Assert.False(AgentMemoryTagParser.ContainsTag(proseOnly));
    Assert.Empty(AgentMemoryTagParser.ParsePersonalCandidates(proseOnly));

    const string mentionThenReal =
      "Чернетка через `<ui_proposal>`:\n\n<ui_proposal>{\"service\":\"ecom/order\",\"operation\":\"create\"}</ui_proposal>\n\n" +
      "Пам'ять через `<agent_memory>`:\n\n<agent_memory>{\"kind\":\"Preference\",\"title\":\"Currency\",\"content\":\"Prefer EUR\"}</agent_memory>";

    var proposals = AgentTagBlockExtractor.ExtractInnerBlocks(mentionThenReal, "<ui_proposal>", "</ui_proposal>");
    Assert.Single(proposals);
    Assert.Contains("\"ecom/order\"", proposals[0], StringComparison.Ordinal);

    Assert.True(AgentMemoryTagParser.ContainsTag(mentionThenReal));
    var memories = AgentMemoryTagParser.ParsePersonalCandidates(mentionThenReal);
    var mem = Assert.Single(memories);
    Assert.Equal(("Preference", "Currency", "Prefer EUR"), mem);
  }

  [Fact]
  public async Task StageInlineProposalsAndMemoriesAsync_DoesNotEmitWarningForInlineCodeProposalMention()
  {
    var req = new AgentTurnRequest
    {
      SessionId = "inline-mention-1",
      Owner = new AgentOwnerScope(1, 1),
      Message = "Explain proposals"
    };

    var (warnings, memories) = await AgentPromptBuilder.StageInlineProposalsAndMemoriesAsync(
      "Використовуйте `<ui_proposal>...</ui_proposal>` у відповіді.",
      req,
      CancellationToken.None);

    Assert.Empty(warnings);
    Assert.Empty(memories);
  }
}
