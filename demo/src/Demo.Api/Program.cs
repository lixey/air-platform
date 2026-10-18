using Air.App.Host;
using DemoApp;
using DemoEcom;
using DemoOrg;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatformModule<EcomModule>();
builder.AddPlatformModule<OrgModule>();
builder.AddPlatformModule<AppModule>();

// CORS for Angular / Vite dev server
builder.Services.AddCors(options =>
{
  options.AddDefaultPolicy(policy =>
  {
    policy.SetIsOriginAllowed(origin => new Uri(origin).IsLoopback)
      .AllowAnyHeader()
      .AllowAnyMethod()
      .AllowCredentials();
  });
});

var app = builder.Build();

app.UseCors();

app.MapPlatform();

app.Run();
