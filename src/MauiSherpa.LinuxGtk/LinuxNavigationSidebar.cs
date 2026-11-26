using MauiSherpa.Core.Interfaces;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

namespace MauiSherpa;

/// <summary>
/// Native GTK navigation sidebar for the Linux head. Mirrors the Blazor sidebar's
/// sections and routes, and uses GTK's <c>navigation-sidebar</c> list styling so it
/// follows the active system theme.
/// </summary>
public sealed class LinuxNavigationSidebar : View
{
    public const double SidebarWidth = 250;

    public LinuxNavigationSidebar(IToolbarService toolbarService)
    {
        ToolbarService = toolbarService;
        WidthRequest = SidebarWidth;
        AutomationId = "linux-sidebar";
    }

    public IToolbarService ToolbarService { get; }
}

public sealed class LinuxNavigationSidebarHandler : GtkViewHandler<LinuxNavigationSidebar, Gtk.Box>
{
    private sealed record NavItem(string Route, bool Exact, Gtk.ListBoxRow Row);

    public static readonly IPropertyMapper<LinuxNavigationSidebar, LinuxNavigationSidebarHandler> Mapper =
        new PropertyMapper<LinuxNavigationSidebar, LinuxNavigationSidebarHandler>(ViewMapper);

    private readonly List<NavItem> _items = new();
    private readonly List<Gtk.Widget> _widgets = new(); // keep GirCore wrappers alive
    private Gtk.ListBox? _listBox;
    private bool _syncingSelection;
    private string? _pendingRoute;

    public LinuxNavigationSidebarHandler() : base(Mapper)
    {
    }

    protected override Gtk.Box CreatePlatformView()
    {
        LinuxIconTheme.EnsureRegistered();

        var root = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
        root.AddCssClass("sherpa-sidebar");

        _listBox = Gtk.ListBox.New();
        _listBox.AddCssClass("navigation-sidebar");
        _listBox.SetSelectionMode(Gtk.SelectionMode.Single);
        _listBox.OnRowActivated += OnRowActivated;
        _listBox.OnRowSelected += OnRowSelected;

        AddItem("Dashboard", "/", "sherpa-home-symbolic", exact: true);

        AddHeader("Android", "sherpa-android-symbolic");
        AddItem("Devices & Emulators", "/devices", "sherpa-mobile-screen-button-symbolic");
        AddItem("SDK & JDK", "/android-sdk", "sherpa-cube-symbolic");
        AddItem("Keystores", "/keystores", "sherpa-key-symbolic");
        AddItem("Push Testing", "/firebase-push", "sherpa-paper-plane-symbolic");

        AddHeader("Apple", "sherpa-apple-symbolic");
        AddItem("Registered Devices", "/apple-devices", "sherpa-mobile-screen-button-symbolic");
        AddItem("Bundle IDs", "/bundle-ids", "sherpa-fingerprint-symbolic");
        AddItem("Certificates", "/certificates", "sherpa-certificate-symbolic");
        AddItem("Provisioning Profiles", "/profiles", "sherpa-id-card-symbolic");
        AddItem("Push Testing", "/push-testing", "sherpa-paper-plane-symbolic");

        AddHeader("Secrets", "sherpa-vault-symbolic");
        AddItem("Secrets", "/secrets", "sherpa-key-symbolic", exact: true);
        AddItem("Publish", "/secrets/publish", "sherpa-upload-symbolic");

        AddHeader("Tools", "sherpa-toolbox-symbolic");
        AddItem(".NET SDK Manager", "/dotnet-sdk", "sherpa-layer-group-symbolic");
        AddItem("Profiling", "/profiling", "sherpa-chart-line-symbolic");
        AddItem("App Inspector", "/devflow", "sherpa-wand-magic-sparkles-symbolic");

#if DEBUG
        if (!MauiSherpa.Core.Screenshots.ScreenshotMode.Current.IsEnabled)
            AddItem("Debug UI", "/debug", "sherpa-bug-symbolic");
#endif

        var scroller = Gtk.ScrolledWindow.New();
        scroller.SetPolicy(Gtk.PolicyType.Never, Gtk.PolicyType.Automatic);
        scroller.SetHexpand(true);
        scroller.SetVexpand(true);
        scroller.SetChild(_listBox);

        var separator = Gtk.Separator.New(Gtk.Orientation.Vertical);

        root.Append(scroller);
        root.Append(separator);
        _widgets.Add(scroller);
        _widgets.Add(separator);
        return root;
    }

    protected override void ConnectHandler(Gtk.Box platformView)
    {
        base.ConnectHandler(platformView);
        VirtualView.ToolbarService.RouteChanged += OnRouteChanged;
        SelectRoute(VirtualView.ToolbarService.CurrentRoute);
    }

    protected override void DisconnectHandler(Gtk.Box platformView)
    {
        VirtualView.ToolbarService.RouteChanged -= OnRouteChanged;
        base.DisconnectHandler(platformView);
    }

    private void AddHeader(string title, string iconName)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        box.SetMarginTop(12);
        box.SetMarginStart(6);
        box.AddCssClass("dim-label");

        var icon = Gtk.Image.NewFromIconName(iconName);
        icon.SetPixelSize(12);
        var label = Gtk.Label.New(title);
        label.SetXalign(0);
        label.AddCssClass("caption-heading");
        label.AddCssClass("heading");
        box.Append(icon);
        box.Append(label);

        var row = Gtk.ListBoxRow.New();
        row.SetChild(box);
        row.SetSelectable(false);
        row.SetActivatable(false);
        row.SetFocusable(false);
        _listBox!.Append(row);
        _widgets.AddRange([box, icon, label, row]);
    }

    private void AddItem(string title, string route, string iconName, bool exact = false)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
        var icon = Gtk.Image.NewFromIconName(iconName);
        icon.SetPixelSize(16);
        var label = Gtk.Label.New(title);
        label.SetXalign(0);
        label.SetEllipsize(Pango.EllipsizeMode.End);
        box.Append(icon);
        box.Append(label);

        var row = Gtk.ListBoxRow.New();
        row.SetChild(box);
        row.SetTooltipText(title);
        _listBox!.Append(row);
        _items.Add(new NavItem(route, exact, row));
        _widgets.AddRange([box, icon, label, row]);
    }

    private void OnRowActivated(Gtk.ListBox sender, Gtk.ListBox.RowActivatedSignalArgs args)
        => NavigateTo(args.Row);

    // Selection also changes through keyboard and assistive technologies, which don't
    // always emit row-activated.
    private void OnRowSelected(Gtk.ListBox sender, Gtk.ListBox.RowSelectedSignalArgs args)
        => NavigateTo(args.Row);

    private void NavigateTo(Gtk.ListBoxRow? row)
    {
        if (_syncingSelection || row == null)
            return;

        var handle = row.Handle.DangerousGetHandle();
        var item = _items.FirstOrDefault(i => i.Row.Handle.DangerousGetHandle() == handle);
        // A click emits both row-selected and row-activated; request each route once.
        if (item == null || string.Equals(item.Route, _pendingRoute, StringComparison.OrdinalIgnoreCase))
            return;

        _pendingRoute = item.Route;
        VirtualView?.ToolbarService.RequestNavigation(item.Route);
    }

    private void OnRouteChanged(string route)
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            _pendingRoute = null;
            SelectRoute(route);
            return false;
        });
    }

    private void SelectRoute(string route)
    {
        if (_listBox == null)
            return;

        var match = _items
            .Where(i => string.Equals(route, i.Route, StringComparison.OrdinalIgnoreCase)
                || (!i.Exact && route.StartsWith(i.Route + "/", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(i => i.Route.Length)
            .FirstOrDefault();

        _syncingSelection = true;
        try
        {
            if (match != null)
                _listBox.SelectRow(match.Row);
            else
                _listBox.UnselectAll();
        }
        finally
        {
            _syncingSelection = false;
        }
    }
}
