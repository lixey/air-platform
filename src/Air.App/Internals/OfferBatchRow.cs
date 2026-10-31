namespace Platform.App.Internals;

internal sealed class OfferBatchRow
{
  public long Id { get; set; }
  public bool B0 { get; set; }
  public bool B1 { get; set; }
  public bool B2 { get; set; }
  public bool B3 { get; set; }
  public bool B4 { get; set; }
  public bool B5 { get; set; }
  public bool B6 { get; set; }
  public bool B7 { get; set; }

  public bool GetFlag(int index) =>
    index switch
    {
      0 => B0,
      1 => B1,
      2 => B2,
      3 => B3,
      4 => B4,
      5 => B5,
      6 => B6,
      7 => B7,
      _ => true
    };
}
