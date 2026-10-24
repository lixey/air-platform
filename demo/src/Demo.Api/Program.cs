await PlatformHost.RunAsync(
  args,
  options =>
    options
      .AddModule<EcomModule>()
      .AddModule<OrgModule>()
      .AddModule<AppModule>(),
  app =>
  {
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
  }
);
