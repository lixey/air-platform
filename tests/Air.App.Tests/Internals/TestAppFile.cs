namespace Platform.App.Tests.Internals;

[FileRules(MaxBytes = 65536, ContentTypes = ["text/plain"])]
internal sealed class TestAppFile : BaseAppFile
{
  public long? InvoiceId { get; set; }
}
