namespace Platform.App.Tests.Internals;

internal sealed class TestExecutionScopeService(PlatformAppService? app = null)
  : PlatformService(app)
{
  public override Feature Feature => TestAppModule.Invoices;

  public void StoreString(string key, string value) => SetExecutionItem(key, value);

  public string? ReadString(string key) => GetExecutionItem<string>(key);

  public void StoreTyped<T>(T value) => SetExecutionItem(value);

  public T? ReadTyped<T>() => GetExecutionItem<T>();
}
