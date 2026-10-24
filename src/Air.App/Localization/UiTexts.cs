using System.Collections.Frozen;

namespace Air.App.Localization;

public static class UiTexts
{
  public static readonly IReadOnlyDictionary<string, string> English = BuildEnglish(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
  {
    ["ActualSize"] = "Actual size",
    ["AddCondition"] = "Add a condition",
    ["AddConditionGroup"] = "Add a condition group",
    ["AgentAttachTitle"] = "Attach photo",
    ["AgentChangedBadge"] = "Changed",
    ["AgentCheckBeforeSave"] = "Check the details before saving",
    ["AgentConfirm"] = "Confirm",
    ["AgentCopied"] = "Copied",
    ["AgentCopyMessage"] = "Copy",
    ["AgentDefaultPhotoPrompt"] = "Analyze the attached photo",
    ["AgentDropHint"] = "Drop photo or document here",
    ["AgentDuplicateFileWarning"] = "This receipt file is already attached to record {0}",
    ["AgentDuplicateReceiptWarning"] = "A record with this date and amount already exists: {0}",
    ["AgentDurationSec"] = "{0} s",
    ["AgentEditBadge"] = "Edit",
    ["AgentEmptyDescription"] = "Describe what you want to create or edit, or drop a photo/receipt (drag & drop or press \u2318V).",
    ["AgentEmptyTitle"] = "AI Assistant",
    ["AgentErrorCliMissing"] = "CLI 'agy' was not found on the system.",
    ["AgentErrorRequest"] = "Could not get a response from the AI assistant.",
    ["AgentErrorSave"] = "Could not save the record.",
    ["AgentErrorUpload"] = "Could not upload the file.",
    ["AgentInputPlaceholder"] = "Write a message or paste a photo\u2026",
    ["AgentLinesTitle"] = "Items ({0})",
    ["AgentModel"] = "AI Model",
    ["AgentNewBadge"] = "New",
    ["AgentNewChat"] = "New chat",
    ["AgentPhotoWillAttach"] = "Attached file will be linked to the record",
    ["AgentProposalNotFound"] = "The proposal was not found or has expired.",
    ["AgentRetryMessage"] = "Run again",
    ["AgentReturnToInput"] = "Return to chat",
    ["AgentSaved"] = "Saved: {0}",
    ["AgentSendTitle"] = "Send (Enter)",
    ["AgentStatusAnalyzing"] = "Analyzing\u2026",
    ["AgentStatusPreparing"] = "Preparing record\u2026",
    ["AgentStatusReadingPhoto"] = "Reading photo\u2026",
    ["AgentStopTitle"] = "Stop",
    ["AgentTitle"] = "AI Assistant",
    ["AllAreTrue"] = "All of the following are true\u2026",
    ["Any"] = "Any",
    ["AnyIsTrue"] = "Any of the following is true\u2026",
    ["Apply"] = "Apply",
    ["Cancel"] = "Cancel",
    ["CancelUpload"] = "Cancel the upload of {0}",
    ["CellSaveQuestion"] = "Save the change?",
    ["Changed"] = "Changed",
    ["ChooseDate"] = "Choose a date",
    ["ChooseDatetime"] = "Choose a date and time",
    ["ChooseFiles"] = "Choose files",
    ["ChooseHour"] = "Choose an hour",
    ["ChooseMinute"] = "Choose a minute",
    ["ChooseMonth"] = "Choose a month",
    ["ChoosePeriodOf"] = "Choose the period of {0}",
    ["ChooseTime"] = "Choose a time",
    ["ChooseYear"] = "Choose a year",
    ["Clear"] = "Clear",
    ["ClearAll"] = "Clear all",
    ["ClearFilterOf"] = "Clear the filter of {0}",
    ["ClearSearch"] = "Clear the search",
    ["ClearSearchIn"] = "Clear the search in {0}",
    ["Close"] = "Close",
    ["CloseNotification"] = "Close notification",
    ["CloseTab"] = "Close {0}",
    ["Column"] = "Column",
    ["ColumnMenu"] = "Menu of {0}",
    ["ConditionsJoin"] = "How the conditions join",
    ["Confirm"] = "Confirm",
    ["ConfirmTyped"] = "Type {0} to confirm",
    ["Cover"] = "Cover",
    ["Delete"] = "Delete",
    ["DeleteAllFiles"] = "Delete all",
    ["DeleteAllFilesConfirm"] = "Delete all {0} files?",
    ["DeleteFile"] = "Delete {0}",
    ["DeleteFileConfirm"] = "Delete the file {0}?",
    ["Download"] = "Download",
    ["DownloadAllFiles"] = "Download all",
    ["DropFilesHere"] = "Drop files here or choose them.",
    ["English"] = "English",
    ["Error"] = "Error",
    ["FileAccepts"] = "Accepted: {0}; up to {1} MB.",
    ["FileAdded"] = "Added",
    ["FileCounter"] = "{0} / {1}",
    ["FileDescription"] = "Description",
    ["FileDimensions"] = "Size in pixels",
    ["FileDuplicate"] = "The record already holds this file as {0}.",
    ["FileInfo"] = "Info",
    ["FileInfoSave"] = "Save",
    ["FileLimitCount"] = "A record holds at most {0} files.",
    ["FileLimitTotal"] = "A record's files hold at most {0} MB together.",
    ["FileLimits"] = "{0} - up to {1}",
    ["FileLoadFailed"] = "The file could not be shown.",
    ["FileMetadata"] = "Metadata",
    ["FileMetadataNone"] = "No metadata.",
    ["FileMoveCancelled"] = "{0} put back",
    ["FileName"] = "Name",
    ["FilePixelsRefused"] = "The image is larger than {0} megapixels.",
    ["FilePosition"] = "{0}, position {1} of {2}",
    ["FilePreparing"] = "Preparing\u2026",
    ["FileRefused"] = "The file is refused.",
    ["FileRemove"] = "Remove",
    ["FileSize"] = "Size",
    ["FileTooLarge"] = "The file is larger than {0} MB.",
    ["FileType"] = "Type",
    ["FileTypeRefused"] = "Files of this type are not accepted here; accepted: {0}.",
    ["FileUploadFailed"] = "Not uploaded",
    ["FileUploaded"] = "Uploaded",
    ["FileUploadedBy"] = "Uploaded by",
    ["FileUploading"] = "Uploading\u2026",
    ["FileViewer"] = "Files",
    ["FileWaiting"] = "Waiting",
    ["FilesCount"] = "{0} of {1}",
    ["FilesFull"] = "{0} of {1}: no room for more files",
    ["FilesNoRoom"] = "The record has no room for this file.",
    ["FilesSaveFirst"] = "Save the record to add its files.",
    ["FilesUploadingLeave"] = "Files are still uploading. Leave and cancel them?",
    ["FilesZoneHint"] = "Drop, paste or choose files.",
    ["FilterAnd"] = "AND",
    ["FilterBy"] = "Filter by {0}",
    ["FilterOr"] = "OR",
    ["Filters"] = "Filters",
    ["FitToScreen"] = "Fit to screen",
    ["FormRecordChanged"] = "This record has changed since it was read.",
    ["Hours"] = "Hours",
    ["Leave"] = "Leave",
    ["Loading"] = "Loading\u2026",
    ["MenuOpen"] = "Open the menu",
    ["MetadataAltitude"] = "Altitude",
    ["MetadataAperture"] = "Aperture",
    ["MetadataCamera"] = "Camera",
    ["MetadataExposure"] = "Exposure",
    ["MetadataFocalLength"] = "Focal length",
    ["MetadataIso"] = "ISO",
    ["MetadataLens"] = "Lens",
    ["MetadataOrientation"] = "Orientation",
    ["MetadataPlace"] = "Place",
    ["MetadataSoftware"] = "Software",
    ["MetadataTakenAt"] = "Taken",
    ["Minutes"] = "Minutes",
    ["More"] = "More",
    ["MoveFile"] = "Move {0}",
    ["MoveFileHint"] = "Space takes the file, the arrow keys move it, Space drops it and Escape puts it back.",
    ["Navigation"] = "Navigation",
    ["NavigationHide"] = "Hide navigation",
    ["NavigationShow"] = "Show navigation",
    ["NextFile"] = "Next file",
    ["NextMonth"] = "Next month",
    ["NextPage"] = "Next page",
    ["NextYear"] = "Next year",
    ["NextYears"] = "Next years",
    ["No"] = "No",
    ["NoFiles"] = "No files yet.",
    ["NoFilterYet"] = "No filter yet.",
    ["NoRows"] = "Nothing found.",
    ["NothingToPick"] = "Nothing to pick.",
    ["Now"] = "Now",
    ["NumberDecrease"] = "Decrease",
    ["NumberIncrease"] = "Increase",
    ["Open"] = "Open",
    ["OpenFile"] = "Open {0}",
    ["OpenRecord"] = "Open {0}",
    ["OpensInNewTab"] = "opens in a new tab",
    ["OperatorContains"] = "Contains",
    ["OperatorEndsWith"] = "Ends with",
    ["OperatorEquals"] = "Equals",
    ["OperatorFor"] = "Operator for {0}",
    ["OperatorGreaterThan"] = "Greater than",
    ["OperatorGreaterThanOrEqual"] = "Greater than or equal",
    ["OperatorIn"] = "In",
    ["OperatorIsNotNull"] = "Is not empty",
    ["OperatorIsNull"] = "Is empty",
    ["OperatorLessThan"] = "Less than",
    ["OperatorLessThanOrEqual"] = "Less than or equal",
    ["OperatorMatches"] = "Matches",
    ["OperatorNotContains"] = "Not contains",
    ["OperatorNotEndsWith"] = "Not ends with",
    ["OperatorNotEquals"] = "Not equals",
    ["OperatorNotIn"] = "Not in",
    ["OperatorNotMatches"] = "Not matches",
    ["OperatorNotStartsWith"] = "Not starts with",
    ["OperatorStartsWith"] = "Starts with",
    ["OptionsLoadFailed"] = "The options could not be loaded.",
    ["Pages"] = "Pages",
    ["PeriodFrom"] = "From",
    ["PeriodNextMonth"] = "Next month",
    ["PeriodNextYear"] = "Next year",
    ["PeriodPreviousMonth"] = "Previous month",
    ["PeriodPreviousYear"] = "Previous year",
    ["PeriodQuarter"] = "Quarter {0}",
    ["PeriodThisMonth"] = "This month",
    ["PeriodThisWeek"] = "This week",
    ["PeriodThisYear"] = "This year",
    ["PeriodTo"] = "To",
    ["Periods"] = "Periods",
    ["Reason"] = "Reason",
    ["Pick"] = "Pick {0}",
    ["PickedCount"] = "{0} picked",
    ["Pin"] = "Pin",
    ["PinEntry"] = "Pin {0}",
    ["Pinned"] = "Pinned",
    ["PinnedRow"] = "Pinned row",
    ["PreviousFile"] = "Previous file",
    ["PreviousMonth"] = "Previous month",
    ["PreviousPage"] = "Previous page",
    ["PreviousYear"] = "Previous year",
    ["PreviousYears"] = "Previous years",
    ["Processing"] = "Processing request\u2026",
    ["RemoveAllFilters"] = "Remove every filter",
    ["RemoveCondition"] = "Remove this condition",
    ["RemoveFilterNamed"] = "Remove the filter {0}",
    ["RemoveGroup"] = "Remove this group",
    ["RemoveItem"] = "Remove {0}",
    ["RequestFailed"] = "The request failed.",
    ["ResizePanel"] = "Resize {0}",
    ["Restore"] = "Restore",
    ["RestoreConfirm"] = "Restore the selected journal rows?",
    ["Retry"] = "Try again",
    ["RowCount"] = "{0} rows",
    ["Save"] = "Save",
    ["ScreenLoadFailed"] = "This screen could not be loaded.",
    ["Search"] = "Search",
    ["SearchMenu"] = "Search the menu",
    ["SearchOptions"] = "Search the options",
    ["SelectPage"] = "Select every row of the page",
    ["SelectRow"] = "Select row {0}",
    ["SelectedOf"] = "{0} of {1} selected",
    ["SetCover"] = "Make cover",
    ["SignOut"] = "Sign out",
    ["SortBy"] = "Sort by {0}",
    ["TakePhoto"] = "Take photo",
    ["TakeVideo"] = "Take video",
    ["ThemeDark"] = "Dark theme",
    ["ThemeLight"] = "Light theme",
    ["Today"] = "Today",
    ["TypeAtLeast"] = "Type at least {0} characters",
    ["Ukrainian"] = "\u0423\u043a\u0440\u0430\u0457\u043d\u0441\u044c\u043a\u0430",
    ["Unpin"] = "Unpin",
    ["UnpinEntry"] = "Unpin {0}",
    ["Upload"] = "Upload",
    ["UploadFiles"] = "Upload files",
    ["ValueFor"] = "Value for {0}",
    ["Week"] = "Week",
    ["Where"] = "Where",
    ["Yes"] = "Yes"
  });

  private static FrozenDictionary<string, string> BuildEnglish(Dictionary<string, string> seed)
  {
    try
    {
      var mgr = new System.Resources.ResourceManager(typeof(Air.App.Resources.PlatformResource));
      var set = mgr.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, true);
      if (set != null)
      {
        foreach (System.Collections.DictionaryEntry entry in set)
        {
          if (entry.Key is string key && entry.Value is string val && !seed.ContainsKey(key))
          {
            seed[key] = val;
          }
        }
      }
    }
    catch { }

    return seed.ToFrozenDictionary(StringComparer.Ordinal);
  }
}

public sealed class Localizer : ILocalizer
{
  public static readonly Localizer Instance = new();

  private readonly Localizer? _root;
  private readonly string? _moduleHandle;
  private readonly Lock _sync = new();
  private readonly List<System.Resources.ResourceManager> _managers = [];
  private readonly Dictionary<string, string> _canonicalKeys = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> _loadedNames = new(StringComparer.Ordinal);
  private int _lastAssemblyCount;

  public Localizer()
  {
    Register(typeof(Air.App.Resources.PlatformResource));
    EnsureManagersLoaded();
  }

  private Localizer(Localizer root, string moduleHandle)
  {
    _root = root;
    _moduleHandle = moduleHandle;
  }

  public void Register(Type resourceType)
  {
    if (_root != null)
    {
      _root.Register(resourceType);
      return;
    }

    if (resourceType == null)
    {
      return;
    }

    lock (_sync)
    {
      RegisterLocked(resourceType);
    }
  }

  private void RegisterLocked(Type resourceType)
  {
    if (!_loadedNames.Add(resourceType.FullName ?? resourceType.Name))
    {
      return;
    }

    var mgr = new System.Resources.ResourceManager(resourceType) { IgnoreCase = true };
    _managers.Add(mgr);
    IndexKeysLocked(mgr, System.Globalization.CultureInfo.InvariantCulture);
    try
    {
      IndexKeysLocked(mgr, new System.Globalization.CultureInfo("uk"));
    }
    catch { }
  }

  private void IndexKeysLocked(System.Resources.ResourceManager mgr, System.Globalization.CultureInfo culture)
  {
    try
    {
      var set = mgr.GetResourceSet(culture, true, false);
      if (set == null)
      {
        return;
      }

      foreach (System.Collections.DictionaryEntry entry in set)
      {
        if (entry.Key is string k && !_canonicalKeys.ContainsKey(k))
        {
          _canonicalKeys[k] = k;
        }
      }
    }
    catch { }
  }

  public void EnsureManagersLoaded()
  {
    if (_root != null)
    {
      _root.EnsureManagersLoaded();
      return;
    }

    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
    if (assemblies.Length == _lastAssemblyCount)
    {
      return;
    }

    lock (_sync)
    {
      if (assemblies.Length == _lastAssemblyCount)
      {
        return;
      }

      foreach (var asm in assemblies)
      {
        if (asm.IsDynamic)
        {
          continue;
        }

        try
        {
          foreach (var type in asm.GetTypes())
          {
            if (type.Name.EndsWith("Resource", StringComparison.OrdinalIgnoreCase) && !type.IsAbstract && !type.IsGenericType)
            {
              RegisterLocked(type);
            }
          }
        }
        catch { }
      }

      _lastAssemblyCount = assemblies.Length;
    }
  }

  public bool TryGetString(string key, out string result)
  {
    if (string.IsNullOrEmpty(key))
    {
      result = string.Empty;
      return false;
    }

    if (_root != null)
    {
      if (!string.IsNullOrWhiteSpace(_moduleHandle) &&
          _root.TryGetString($"{_moduleHandle}: {key}", out result))
      {
        return true;
      }

      return _root.TryGetString(key, out result);
    }

    var culture = System.Globalization.CultureInfo.CurrentUICulture;
    System.Resources.ResourceManager[] snapshot;
    string? canonical;
    lock (_sync)
    {
      snapshot = [.. _managers];
      _canonicalKeys.TryGetValue(key, out canonical);
    }

    foreach (var mgr in snapshot)
    {
      try
      {
        var text = mgr.GetString(key, culture)
          ?? (canonical != null && canonical != key ? mgr.GetString(canonical, culture) : null);
        if (!string.IsNullOrEmpty(text))
        {
          result = text;
          return true;
        }
      }
      catch { }
    }

    if (UiTexts.English.TryGetValue(key, out var val) ||
        canonical != null && UiTexts.English.TryGetValue(canonical, out val))
    {
      result = val;
      return true;
    }

    result = key;
    return false;
  }

  public string TextOr(string key, string fallback)
  {
    if (TryGetString(key, out var result) && result != key)
    {
      return result;
    }

    return fallback;
  }

  public Localizer For(string? moduleHandle)
  {
    var root = _root ?? this;
    return string.IsNullOrWhiteSpace(moduleHandle) ? root : new Localizer(root, moduleHandle);
  }

  ILocalizer ILocalizer.For(string? moduleHandle) => For(moduleHandle);

  public string this[string key]
  {
    get
    {
      if (TryGetString(key, out var res))
      {
        return res;
      }

      return key;
    }
  }

  public string of(string key) => this[key];

  public string this[string key, params object[] args]
  {
    get
    {
      var template = this[key];
      return args.Length > 0 ? string.Format(System.Globalization.CultureInfo.CurrentUICulture, template, args) : template;
    }
  }
}
