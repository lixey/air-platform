using System.Globalization;
using Air.App.Localization;
using DemoEcom.Data.Documents;

namespace DemoEcom.Documents;

public sealed class EcomDocTexts
{
  private readonly string _transition;
  private readonly string _noLines;
  private readonly string _noWarehouse;
  private readonly string _foreignWarehouse;
  private readonly string _stockShort;
  private readonly string _noAccount;
  private readonly string _foreignAccount;
  private readonly string _moneyShort;
  private readonly string _followed;
  private readonly string _overSource;
  private readonly string _sourceNotPosted;
  private readonly string _nothingOpen;
  private readonly string _sourceProduct;
  private readonly string _lineOverSource;
  private readonly string _purchaseNoWarehouse;

  private EcomDocTexts(Localizer localizer)
  {
    SameWarehouse = localizer["A transfer goes to another warehouse than the one it leaves."];
    ForeignProduct = localizer["The product belongs to another supplier."];
    ForeignLines = localizer["The document's lines name products of another supplier."];
    CancelConfirm = localizer["Change the status to Canceled?"];
    NotEditable = localizer["The document is not in an editable status and cannot be deleted."];
    _transition = localizer["The document cannot change its status to {0}."];
    _noLines = localizer["The document has no lines and cannot take the status {0}."];
    _noWarehouse = localizer["The document names no warehouse and cannot take the status {0}."];
    _foreignWarehouse = localizer["The warehouse belongs to another organization."];
    _stockShort = localizer["The document cannot take the status {0}: {1} is short by {2:0.###}."];
    _noAccount = localizer["The document names no money account and cannot take the status {0}."];
    _foreignAccount = localizer["The money account belongs to another organization."];
    _moneyShort = localizer["The document cannot take the status {0}: the account {1} is short by {2:0.00}."];
    _followed = localizer["The document has documents entered on it and cannot take the status {0}."];
    _overSource = localizer["The document cannot take the status {0}: {1} is over what its source has open by {2:0.###}."];
    _sourceNotPosted = localizer["The document is not posted and nothing can be entered on it."];
    _nothingOpen = localizer["The document has nothing open to enter."];
    _sourceProduct = localizer["An entered line keeps the product of its source."];
    _lineOverSource = localizer["An entered line takes no more than its source has open."];
    _purchaseNoWarehouse = localizer["The document names no warehouse and nothing can be purchased for it."];
    NothingToPurchase = localizer["The selected lines need no purchase."];
    NoteFull = localizer["The note has no room for the reason."];
  }

  public string SameWarehouse { get; }
  public string ForeignProduct { get; }
  public string ForeignLines { get; }
  public string CancelConfirm { get; }
  public string NotEditable { get; }
  public string NothingToPurchase { get; }
  public string NoteFull { get; }

  public static EcomDocTexts Of(Localizer localizer)
  {
    ArgumentNullException.ThrowIfNull(localizer);
    return new EcomDocTexts(localizer);
  }

  public string Refusal(EcomDocRefusal refusal, string status)
  {
    ArgumentNullException.ThrowIfNull(refusal);
    ArgumentNullException.ThrowIfNull(status);
    var phrase = refusal.Code switch
    {
      EcomDocRefusal.NoLines => _noLines,
      EcomDocRefusal.NoWarehouse => _noWarehouse,
      EcomDocRefusal.ForeignWarehouse => _foreignWarehouse,
      EcomDocRefusal.StockShort => _stockShort,
      EcomDocRefusal.NoAccount => _noAccount,
      EcomDocRefusal.ForeignAccount => _foreignAccount,
      EcomDocRefusal.MoneyShort => _moneyShort,
      EcomDocRefusal.Followed => _followed,
      EcomDocRefusal.OverSource => _overSource,
      EcomDocRefusal.SourceNotPosted => _sourceNotPosted,
      EcomDocRefusal.NothingOpen => _nothingOpen,
      EcomDocRefusal.SourceProduct => _sourceProduct,
      _ => $"Refusal {refusal.Code}"
    };
    return string.Format(CultureInfo.CurrentUICulture, phrase, status, refusal.Product ?? refusal.Account, refusal.Short);
  }

  public string Transition(string status) => string.Format(CultureInfo.CurrentUICulture, _transition, status);
  public string Entering(string code) => code switch
  {
    EcomDocRefusal.SourceNotPosted => _sourceNotPosted,
    EcomDocRefusal.NothingOpen => _nothingOpen,
    _ => code
  };
  public string Purchasing(string code) => code switch
  {
    EcomDocRefusal.NoWarehouse => _purchaseNoWarehouse,
    _ => code
  };
  public string Line(string code) => code switch
  {
    EcomDocRefusal.SourceProduct => _sourceProduct,
    EcomDocRefusal.OverSource => _lineOverSource,
    _ => code
  };
}
