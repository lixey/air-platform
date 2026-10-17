using System.Linq.Expressions;
using Air.App.Form;
using Air.App.Form.Actions;
using DemoEcom.Data;
using DemoEcom.Data.Documents;

namespace DemoEcom.Documents;

public static class EcomDocActions
{
  public const string NotEditable = "doc.notEditable";
  public const string NoteFull = "doc.noteFull";
  public const int ReasonMaxLength = 200;
  public const string ReasonPath = "reason";

  public static void AskReason<TInput>(ActionInput<TInput> input, Expression<Func<TInput, string?>> reason)
    where TInput : class
  {
    ArgumentNullException.ThrowIfNull(input);
    input.Add(reason).Label("Reason").AsTextarea().Required().Rules(r => r.MaximumLength(ReasonMaxLength));
  }

  public static async Task SaveAsync(
    EcomDbContext db,
    FormSaveContext<EcomDoc, EcomDoc> context,
    long? userId,
    EcomDocStatus status,
    CancellationToken cancellationToken
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(status);
    var doc = context.Entity;
    if (context.IsNew)
    {
      await EcomDocNumbers.IssueAsync(db, doc, cancellationToken);
      await EcomDocDefaults.FromOrganizationAsync(db, doc, cancellationToken);
    }

    if (status.IsEditable)
    {
      await EcomDocDefaults.FromCustomerAsync(db, doc, userId, cancellationToken);
      await EcomDocTotals.ApplyAsync(db, doc, cancellationToken);
    }
  }
}
