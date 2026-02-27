using MauiSherpa.Core.Interfaces;

namespace MauiSherpa;

/// <summary>
/// Manages a native GTK4 HeaderBar driven by IToolbarService,
/// mirroring the macOS NSToolbar and Windows TitleBar patterns.
/// Pages call ToolbarService.SetItems() and this manager reflects
/// those actions as native GTK buttons in the window's header bar.
/// </summary>
public class LinuxToolbarManager
{
    private readonly IToolbarService _toolbarService;
    private readonly ICopilotContextService _copilotContext;
    private readonly IThemeService _themeService;
    private readonly IAppleIdentityService _appleIdentityService;
    private readonly IAppleIdentityStateService _appleIdentityState;
    private readonly IGoogleIdentityService _googleIdentityService;
    private readonly IGoogleIdentityStateService _googleIdentityState;
    private Gtk.HeaderBar? _headerBar;
    private Gtk.Window? _window;
    private readonly List<Gtk.Widget> _endWidgets = new();
    private readonly List<Gtk.Widget> _startWidgets = new();
    private readonly List<Gtk.Widget> _retainedWidgets = new(); // prevent GC of removed GTK widgets
    private Gtk.SearchEntry? _searchEntry;
    private Gtk.Button? _copilotButton;
    private Gtk.Image? _copilotImage;
    private Gtk.DropDown? _identityDropdown;
    private string _currentRoute = "";
    private IReadOnlyList<AppleIdentity>? _cachedAppleIdentities;
    private IReadOnlyList<GoogleIdentity>? _cachedGoogleIdentities;

    private static readonly HashSet<string> AppleRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/certificates", "/profiles", "/apple-devices", "/bundle-ids", "/apple-simulators"
    };

    private static readonly HashSet<string> GoogleRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/firebase-push"
    };

    private static readonly Dictionary<string, string> IconMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // SF Symbols used by pages
        ["arrow.clockwise"] = "view-refresh-symbolic",
        ["plus"] = "list-add-symbolic",
        ["plus.circle"] = "list-add-symbolic",
        ["trash"] = "edit-delete-symbolic",
        ["checkmark"] = "object-select-symbolic",
        ["square.and.arrow.down"] = "document-save-symbolic",
        ["square.and.arrow.up"] = "go-up-symbolic",
        ["wand.and.stars"] = "system-run-symbolic",

        // Font Awesome names mapped to GTK icons
        ["fa-cog"] = "emblem-system-symbolic",
        ["fa-download"] = "document-save-symbolic",
        ["fa-sync-alt"] = "view-refresh-symbolic",
    };

    // Icons rendered from Font Awesome as PNGs (dark/white variants for theme support)
    // Format: sfSymbol → (darkFile, whiteFile) relative to Resources/
    private static readonly Dictionary<string, (string Dark, string White)> CustomIconMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fa-stethoscope"] = ("fa-stethoscope-24.png", "fa-stethoscope-white-24.png"),
    };

    public LinuxToolbarManager(
        IToolbarService toolbarService,
        ICopilotContextService copilotContext,
        IThemeService themeService,
        IAppleIdentityService appleIdentityService,
        IAppleIdentityStateService appleIdentityState,
        IGoogleIdentityService googleIdentityService,
        IGoogleIdentityStateService googleIdentityState)
    {
        _toolbarService = toolbarService;
        _copilotContext = copilotContext;
        _themeService = themeService;
        _appleIdentityService = appleIdentityService;
        _appleIdentityState = appleIdentityState;
        _googleIdentityService = googleIdentityService;
        _googleIdentityState = googleIdentityState;
        _toolbarService.ToolbarChanged += OnToolbarChanged;
        _toolbarService.RouteChanged += OnRouteChanged;
        _themeService.ThemeChanged += OnThemeChanged;
        _appleIdentityState.OnSelectionChanged += OnToolbarChanged;
        _googleIdentityState.OnSelectionChanged += OnToolbarChanged;
    }

    public void AttachToWindow(Gtk.Window window)
    {
        _window = window;
        _headerBar = Gtk.HeaderBar.New();
        _headerBar.SetShowTitleButtons(true);

        // App icon on the far left with spacing
        var appIconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "appicon-24.png");
        if (System.IO.File.Exists(appIconPath))
        {
            var appIcon = Gtk.Image.NewFromFile(appIconPath);
            appIcon.SetMarginStart(4);
            appIcon.SetMarginEnd(4);
            _headerBar.PackStart(appIcon);
        }

        // Copilot button next to app icon
        _copilotButton = Gtk.Button.New();
        _copilotButton.SetTooltipText("GitHub Copilot");
        _copilotImage = CreateCopilotIcon();
        _copilotButton.SetChild(_copilotImage);
        _copilotButton.OnClicked += (s, _) => _copilotContext.ToggleOverlay();
        _headerBar.PackStart(_copilotButton);

        _window.SetTitlebar(_headerBar);
    }

    private Gtk.Image CreateCopilotIcon()
    {
        var iconName = _themeService.IsDarkMode ? "ghcp-icon-white-24.png" : "ghcp-icon-24.png";
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", iconName);
        if (System.IO.File.Exists(iconPath))
            return Gtk.Image.NewFromFile(iconPath);
        // Fallback to a standard icon
        return Gtk.Image.NewFromIconName("user-available-symbolic");
    }

    private void OnThemeChanged()
    {
        if (_copilotButton == null) return;
        var newImage = CreateCopilotIcon();
        _copilotButton.SetChild(newImage);
        if (_copilotImage != null) _retainedWidgets.Add(_copilotImage);
        _copilotImage = newImage;

        // Rebuild toolbar buttons so custom PNG icons update for theme
        if (_headerBar != null) RebuildToolbar();
    }

    private void OnToolbarChanged()
    {
        if (_headerBar == null) return;
        RebuildToolbar();
    }

    private void OnRouteChanged(string route)
    {
        _currentRoute = route;
        if (_headerBar == null) return;
        RebuildToolbar();
    }

    private void RebuildToolbar()
    {
        // Retain references to removed widgets to prevent GObject toggle-ref GC crashes.
        // GirCore can crash if GTK4 tries to toggle a ref on a .NET-collected wrapper.
        _retainedWidgets.AddRange(_endWidgets);
        _retainedWidgets.AddRange(_startWidgets);
        if (_searchEntry != null) _retainedWidgets.Add(_searchEntry);
        if (_identityDropdown != null) _retainedWidgets.Add(_identityDropdown);

        // Remove existing action widgets
        foreach (var widget in _endWidgets)
            _headerBar!.Remove(widget);
        _endWidgets.Clear();

        foreach (var widget in _startWidgets)
            _headerBar!.Remove(widget);
        _startWidgets.Clear();

        if (_searchEntry != null)
        {
            _headerBar!.Remove(_searchEntry);
            _searchEntry = null;
        }

        if (_identityDropdown != null)
        {
            _headerBar!.Remove(_identityDropdown);
            _identityDropdown = null;
        }

        // When toolbar is suppressed (e.g. Copilot modal is open), show only a close button
        if (_toolbarService.IsToolbarSuppressed)
        {
            var closeButton = Gtk.Button.NewWithLabel("✕");
            closeButton.AddCssClass("flat");
            closeButton.SetTooltipText("Close Copilot");
            closeButton.OnClicked += (s, _) => _copilotContext.ToggleOverlay();
            _headerBar!.PackEnd(closeButton);
            _endWidgets.Add(closeButton);
            return;
        }

        // Add action buttons (PackEnd in reverse so first item appears leftmost)
        var items = _toolbarService.CurrentItems;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var button = CreateButton(items[i]);
            _headerBar!.PackEnd(button);
            _endWidgets.Add(button);
        }

        // Add search entry to the end section (appears after action buttons)
        if (_toolbarService.SearchPlaceholder != null)
        {
            _searchEntry = Gtk.SearchEntry.New();
            _searchEntry.SetPlaceholderText(_toolbarService.SearchPlaceholder);
            _searchEntry.SetHexpand(false);
            _searchEntry.SetSizeRequest(200, -1);

            _searchEntry.OnSearchChanged += (s, _) =>
            {
                _toolbarService.NotifySearchTextChanged(_searchEntry.GetText());
            };

            _headerBar!.PackEnd(_searchEntry);
        }

        // Add filter dropdowns
        foreach (var filter in _toolbarService.CurrentFilters)
        {
            var dropdown = CreateFilterDropdown(filter);
            _headerBar!.PackEnd(dropdown);
            _endWidgets.Add(dropdown);
        }

        // Add identity picker for Apple/Google routes
        if (AppleRoutes.Contains(_currentRoute))
        {
            _ = AddAppleIdentityPickerAsync();
        }
        else if (GoogleRoutes.Contains(_currentRoute))
        {
            _ = AddGoogleIdentityPickerAsync();
        }
    }

    private async Task AddAppleIdentityPickerAsync()
    {
        try
        {
            _cachedAppleIdentities = await _appleIdentityService.GetIdentitiesAsync();
            if (_cachedAppleIdentities.Count == 0) return;

            var names = _cachedAppleIdentities.Select(i => i.Name).ToArray();
            var selectedIndex = 0;
            if (_appleIdentityState.SelectedIdentity is { } selected)
            {
                var idx = _cachedAppleIdentities.ToList().FindIndex(i => i.Id == selected.Id);
                if (idx >= 0) selectedIndex = idx;
            }

            var stringList = Gtk.StringList.New(names);
            _identityDropdown = Gtk.DropDown.New(stringList, null);
            _identityDropdown.SetSelected((uint)selectedIndex);
            _identityDropdown.SetTooltipText("Apple Identity");

            _identityDropdown.OnNotify += (sender, args) =>
            {
                if (args.Pspec.GetName() != "selected" || _cachedAppleIdentities == null) return;
                var idx = (int)_identityDropdown.GetSelected();
                if (idx >= 0 && idx < _cachedAppleIdentities.Count)
                    _appleIdentityState.SetSelectedIdentity(_cachedAppleIdentities[idx]);
            };

            _headerBar!.PackStart(_identityDropdown);
            _startWidgets.Add(_identityDropdown);
        }
        catch { /* Identity service may not be configured */ }
    }

    private async Task AddGoogleIdentityPickerAsync()
    {
        try
        {
            _cachedGoogleIdentities = await _googleIdentityService.GetIdentitiesAsync();
            if (_cachedGoogleIdentities.Count == 0) return;

            var names = _cachedGoogleIdentities.Select(i => i.Name).ToArray();
            var selectedIndex = 0;
            if (_googleIdentityState.SelectedIdentity is { } selected)
            {
                var idx = _cachedGoogleIdentities.ToList().FindIndex(i => i.Id == selected.Id);
                if (idx >= 0) selectedIndex = idx;
            }

            var stringList = Gtk.StringList.New(names);
            _identityDropdown = Gtk.DropDown.New(stringList, null);
            _identityDropdown.SetSelected((uint)selectedIndex);
            _identityDropdown.SetTooltipText("Google Identity");

            _identityDropdown.OnNotify += (sender, args) =>
            {
                if (args.Pspec.GetName() != "selected" || _cachedGoogleIdentities == null) return;
                var idx = (int)_identityDropdown.GetSelected();
                if (idx >= 0 && idx < _cachedGoogleIdentities.Count)
                    _googleIdentityState.SetSelectedIdentity(_cachedGoogleIdentities[idx]);
            };

            _headerBar!.PackStart(_identityDropdown);
            _startWidgets.Add(_identityDropdown);
        }
        catch { /* Identity service may not be configured */ }
    }

    private Gtk.Button CreateButton(ToolbarAction action)
    {
        var button = Gtk.Button.New();

        if (CustomIconMap.TryGetValue(action.SfSymbol, out var pngFiles))
        {
            var fileName = _themeService.IsDarkMode ? pngFiles.White : pngFiles.Dark;
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", fileName);
            if (System.IO.File.Exists(path))
                button.SetChild(Gtk.Image.NewFromFile(path));
            else
                button.SetLabel(action.Label);
            button.SetTooltipText(action.Label);
        }
        else if (IconMap.TryGetValue(action.SfSymbol, out var gtkIcon))
        {
            button.SetIconName(gtkIcon);
            button.SetTooltipText(action.Label);
        }
        else
        {
            button.SetLabel(action.Label);
        }

        button.SetSensitive(_toolbarService.IsItemEnabled(action.Id));

        var capturedId = action.Id;
        button.OnClicked += (s, _) =>
        {
            _toolbarService.InvokeToolbarItemClicked(capturedId);
        };

        return button;
    }

    private Gtk.DropDown CreateFilterDropdown(ToolbarFilter filter)
    {
        var stringList = Gtk.StringList.New(filter.Options);
        var dropdown = Gtk.DropDown.New(stringList, null);
        dropdown.SetSelected((uint)filter.SelectedIndex);
        dropdown.SetTooltipText(filter.Label);

        var capturedId = filter.Id;
        dropdown.OnNotify += (sender, args) =>
        {
            if (args.Pspec.GetName() == "selected")
            {
                var selected = (int)dropdown.GetSelected();
                _toolbarService.NotifyFilterChanged(capturedId, selected);
            }
        };

        return dropdown;
    }
}
