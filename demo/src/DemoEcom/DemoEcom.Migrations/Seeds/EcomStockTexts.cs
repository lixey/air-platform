namespace DemoEcom.Migrations.Seeds;

/// <summary>The notes of a language: the opening receipt's, and those of the purchases, the issues and the transfers.</summary>
internal sealed record EcomStockTexts(string OpeningNote, string[] PurchaseNotes, string[] IssueNotes, string[] TransferNotes);
