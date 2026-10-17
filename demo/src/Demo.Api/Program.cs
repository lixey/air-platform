using Air.App.Host;
using Air.Data.Postgres;
using DemoApp;
using DemoApp.Data;
using DemoEcom;
using DemoEcom.Data;
using DemoOrg;
using DemoOrg.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure databases: PostgreSQL if configured, otherwise SQLite
var postgresConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(postgresConnection))
{
  builder.AddPlatformPostgres<AppDbContext>();
  builder.AddPlatformPostgres<OrgDbContext>();
  builder.AddPlatformPostgres<EcomDbContext>();
}
else
{
  builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=demo.db"));
  builder.Services.AddDbContext<OrgDbContext>(options => options.UseSqlite("Data Source=demo.db"));
  builder.Services.AddDbContext<EcomDbContext>(options => options.UseSqlite("Data Source=demo.db"));
}

builder.AddPlatformModule<AppModule>();
builder.AddPlatformModule<OrgModule>();
builder.AddPlatformModule<EcomModule>();

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
