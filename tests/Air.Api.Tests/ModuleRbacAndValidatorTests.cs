using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Tests.Internals;

namespace Platform.Api.Tests;

public sealed class ModuleRbacAndValidatorTests
{
  [Fact]
  public void Feature_Freeze_LocksMutators_AndTryGetRightNeverMutates()
  {
    var feat = new Feature("App", "User", typeof(FakeAppService));
    Assert.True(feat.HasExport);
    Assert.True(feat.HasDelete);
    Assert.True(feat.HasHistory);

    var runRight = feat.Action("Run");
    Assert.Equal("App.User.Run", runRight.Key);
    Assert.True(feat.HasAction("Run"));

    feat.WithoutExport().WithoutHistory();
    Assert.False(feat.HasExport);
    Assert.False(feat.HasHistory);
    Assert.False(feat.HasAction("Export"));
    Assert.False(feat.HasAction("History"));

    feat.Freeze();
    Assert.True(feat.IsFrozen);

    Assert.Throws<InvalidOperationException>(() => feat.Action("LateRight"));
    Assert.Throws<InvalidOperationException>(() => feat.ReadOnly());
    Assert.Throws<InvalidOperationException>(() => feat.WithoutDelete());
    Assert.Throws<InvalidOperationException>(() => feat.WithoutExport());
    Assert.Throws<InvalidOperationException>(() => feat.WithoutHistory());
    Assert.Throws<InvalidOperationException>(() => feat.AllowAgent());
    Assert.Throws<InvalidOperationException>(() => feat.Label = "Mutated");

    var countBefore = feat.Rights.Count;
    Assert.False(feat.TryGetRight("NeverDeclared", out _));
    Assert.Equal(countBefore, feat.Rights.Count);
  }

  [Fact]
  public void HandlesAndRights_EnforceStrictPascalCase_AndMaxKeyLength()
  {
    Assert.Throws<InvalidOperationException>(() => new Feature("bad_module", "User"));
    Assert.Throws<InvalidOperationException>(() => new Feature("App", "bad-feature"));
    Assert.Throws<InvalidOperationException>(() => ModuleMenu.Group("bad_group", "Bad Group", _ => { }));

    var feat = new Feature("App", "User");
    var tooLongAction = new string('A', Right.MaxKeyLength);
    Assert.Throws<InvalidOperationException>(() => feat.Action(tooLongAction));
  }

  [Fact]
  public void ServiceEndpoints_FailClosedOnNullFeature_UnauthenticatedUser_WithoutExport_AndUndeclaredActions()
  {
    var admin = new FixedRightsUser(isAuthenticated: true, "*");
    var unauth = new FixedRightsUser(isAuthenticated: false, "*");
    var viewOnly = new FixedRightsUser(isAuthenticated: true, "Fake.App.View");
    var runner = new FixedRightsUser(isAuthenticated: true, "Fake.App.Run");

    Assert.False(ServiceEndpointsExtensions.CanView(null, admin));
    Assert.False(ServiceEndpointsExtensions.CanEdit(null, admin));
    Assert.False(ServiceEndpointsExtensions.CanExport(null, admin));

    var feat = new Feature("Fake", "App", typeof(FakeAppService));
    feat.Action("Run");
    feat.WithoutExport();
    feat.Freeze();

    Assert.False(ServiceEndpointsExtensions.CanView(feat, unauth));
    Assert.False(ServiceEndpointsExtensions.CanEdit(feat, unauth));
    Assert.True(ServiceEndpointsExtensions.CanView(feat, viewOnly));
    Assert.False(ServiceEndpointsExtensions.CanEdit(feat, viewOnly));
    Assert.False(ServiceEndpointsExtensions.CanExport(feat, admin));

    var serviceActionMethod = typeof(FakeAppService).GetMethod(nameof(FakeAppService.ExplicitServiceAction))!;
    var guardedMethod = typeof(FakeAppService).GetMethod(nameof(FakeAppService.GuardedAction))!;
    var undeclaredMethod = typeof(FakeAppService).GetMethod(nameof(FakeAppService.UndeclaredAction))!;

    Assert.False(ServiceEndpointsExtensions.IsCustomActionPermitted(serviceActionMethod, feat, viewOnly));
    Assert.False(ServiceEndpointsExtensions.IsCustomActionPermitted(guardedMethod, feat, viewOnly));
    Assert.True(ServiceEndpointsExtensions.IsCustomActionPermitted(guardedMethod, feat, runner));
    Assert.False(ServiceEndpointsExtensions.IsCustomActionPermitted(undeclaredMethod, feat, admin));

    var readOnlyFeat = new Feature("Fake", "App", typeof(FakeAppService)).ReadOnly();
    readOnlyFeat.Freeze();
    Assert.True(ServiceEndpointsExtensions.IsCustomActionPermitted(serviceActionMethod, readOnlyFeat, viewOnly));
    Assert.False(ServiceEndpointsExtensions.IsCustomActionPermitted(serviceActionMethod, readOnlyFeat, unauth));
  }

  [Fact]
  public async Task DispatchCustomServiceMethodAsync_BindsPositionalRecordOnEmptyBody_AndReadsPascalCaseId()
  {
    var feat = new Feature("Fake", "App", typeof(FakeAppService));
    feat.Freeze();

    var services = new ServiceCollection()
      .AddLogging()
      .AddSingleton<IUserContext>(new FixedRightsUser(isAuthenticated: true, "*"))
      .BuildServiceProvider();

    var service = new FakeAppService();

    // 1. Empty body + query string ?id=77 -> binds positional record using constructor defaults
    var emptyCtx = new DefaultHttpContext { RequestServices = services };
    emptyCtx.Request.QueryString = new QueryString("?id=77");

    var resEmpty = await ServiceEndpointsExtensions.DispatchCustomServiceMethodAsync(
      typeof(FakeAppService),
      service,
      "echo-positional",
      feat,
      emptyCtx.Request,
      services,
      CancellationToken.None);

    await resEmpty.ExecuteAsync(emptyCtx);
    Assert.Equal(StatusCodes.Status200OK, emptyCtx.Response.StatusCode);

    // 2. JSON body with uppercase "Id" + custom record values
    const string bodyJson = """{"Id":99,"title":"Urgent","priority":1}""";
    var jsonBytes = Encoding.UTF8.GetBytes(bodyJson);
    var bodyCtx = new DefaultHttpContext { RequestServices = services };
    bodyCtx.Request.ContentType = "application/json";
    bodyCtx.Request.ContentLength = jsonBytes.Length;
    bodyCtx.Request.Body = new MemoryStream(jsonBytes);
    bodyCtx.Response.Body = new MemoryStream();

    var resBody = await ServiceEndpointsExtensions.DispatchCustomServiceMethodAsync(
      typeof(FakeAppService),
      service,
      "echo-positional",
      feat,
      bodyCtx.Request,
      services,
      CancellationToken.None);

    await resBody.ExecuteAsync(bodyCtx);
    bodyCtx.Response.Body.Position = 0;
    var responseJson = await new StreamReader(bodyCtx.Response.Body).ReadToEndAsync();
    Assert.Contains("99:Urgent:1", responseJson, StringComparison.Ordinal);
  }
}
