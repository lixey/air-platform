namespace DemoEcom.Migrations.Seeds;

internal sealed class EcomPurchasingTurns
{
  private int _short;
  private int _received;

  public int NextShort() => _short++;

  public int NextReceived() => _received++;
}
