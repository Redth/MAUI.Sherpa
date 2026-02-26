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
    private Gtk.HeaderBar? _headerBar;
    private Gtk.Window? _window;
    private readonly List<Gtk.Widget> _endWidgets = new();
    private readonly List<Gtk.Widget> _retainedWidgets = new(); // prevent GC of removed GTK widgets
    private Gtk.SearchEntry? _searchEntry;
    private Gtk.Button? _copilotButton;
    private Gtk.Image? _copilotImage;

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

    public LinuxToolbarManager(IToolbarService toolbarService, ICopilotContextService copilotContext, IThemeService themeService)
    {
        _toolbarService = toolbarService;
        _copilotContext = copilotContext;
        _themeService = themeService;
        _toolbarService.ToolbarChanged += OnToolbarChanged;
        _themeService.ThemeChanged += OnThemeChanged;
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

    private void RebuildToolbar()
    {
        // Retain references to removed widgets to prevent GObject toggle-ref GC crashes.
        // GirCore can crash if GTK4 tries to toggle a ref on a .NET-collected wrapper.
        _retainedWidgets.AddRange(_endWidgets);
        if (_searchEntry != null) _retainedWidgets.Add(_searchEntry);

        // Remove existing action widgets
        foreach (var widget in _endWidgets)
            _headerBar!.Remove(widget);
        _endWidgets.Clear();

        if (_searchEntry != null)
        {
            _headerBar!.Remove(_searchEntry);
            _searchEntry = null;
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
