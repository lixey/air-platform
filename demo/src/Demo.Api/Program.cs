await PlatformHost.RunAsync(
  args,
  options =>
    options
      .AddModule<EcomModule>()
      .AddModule<OrgModule>()
      .AddModule<AppModule>()
);
