namespace DemoOrg.Data;

public static class OrgData
{
  public const string ModuleHandle = "Org";

  public static class Currency
  {
    public const long UahId = 1;
    public const long UsdId = 2;
    public const long EurId = 3;
    public const string Uah = "Uah";
    public const string Usd = "Usd";
    public const string Eur = "Eur";
    public const string UahCode = "UAH";
    public const string UsdCode = "USD";
    public const string EurCode = "EUR";
  }

  public static class Unit
  {
    public const long PieceId = 1;
    public const long SetId = 2;
    public const long LitreId = 3;
    public const long KilogramId = 4;
    public const long MetreId = 5;
    public const string Piece = "Piece";
    public const string Set = "Set";
    public const string Litre = "Litre";
    public const string Kilogram = "Kilogram";
    public const string Metre = "Metre";
  }

  public static class PaymentTerm
  {
    public const long FullPrepaymentId = 1;
    public const long PartialPrepaymentId = 2;
    public const long PaymentOnDeliveryId = 3;
    public const long NoPaymentId = 4;
    public const string FullPrepayment = "FullPrepayment";
    public const string PartialPrepayment = "PartialPrepayment";
    public const string PaymentOnDelivery = "PaymentOnDelivery";
    public const string NoPayment = "NoPayment";
  }
}
