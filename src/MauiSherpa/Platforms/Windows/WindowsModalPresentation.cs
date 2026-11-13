using MauiSherpa.Pages.Forms;
using Microsoft.Maui.Controls.Shapes;

namespace MauiSherpa.Platforms.Windows;

internal static class WindowsModalPresentation
{
    private static readonly BindableProperty PreferredWidthProperty = BindableProperty.CreateAttached(
        "PreferredWidth", typeof(double), typeof(WindowsModalPresentation), 720d);

    private static readonly BindableProperty PreferredHeightProperty = BindableProperty.CreateAttached(
        "PreferredHeight", typeof(double), typeof(WindowsModalPresentation), 640d);

    internal static void SetPreferredSize(Page page, double width, double height)
    {
        page.SetValue(PreferredWidthProperty, width);
        page.SetValue(PreferredHeightProperty, height);
    }

    internal static void Prepare(Page modal)
    {
        if (modal is not ContentPage page)
            return;

        // A non-default background keeps the previous MAUI navigation root in the
        // visual tree, where MAUI also blocks input and traps focus for the modal.
        page.BackgroundColor = Colors.Transparent;
        if (page.Content is ModalOverlay)
            return;

        page.Appearing -= OnModalAppearing;
        page.Appearing += OnModalAppearing;
    }

    private static void OnModalAppearing(object? sender, EventArgs e)
    {
        var page = (ContentPage)sender!;
        page.Appearing -= OnModalAppearing;

        var content = page.Content
            ?? throw new InvalidOperationException($"Modal page {page.GetType().Name} has no content.");
        page.Content = null;
        page.Content = new ModalOverlay(
            page,
            content,
            (double)page.GetValue(PreferredWidthProperty),
            (double)page.GetValue(PreferredHeightProperty));
    }

    private sealed class ModalOverlay : Grid
    {
        private const double OuterMargin = 32;
        private readonly ContentPage _page;
        private readonly Border _dialog;
        private readonly double _preferredWidth;
        private readonly double _preferredHeight;

        public ModalOverlay(ContentPage page, View content, double preferredWidth, double preferredHeight)
        {
            _page = page;
            _preferredWidth = preferredWidth;
            _preferredHeight = preferredHeight;
            BackgroundColor = Color.FromArgb("#66000000");
            AutomationId = "windows-modal-overlay";

            _dialog = new Border
            {
                AutomationId = "windows-modal-dialog",
                Content = content,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Shadow = new Shadow
                {
                    Brush = Brush.Black,
                    Opacity = 0.3f,
                    Radius = 20,
                    Offset = new Point(0, 8)
                }
            };
            _dialog.SetDynamicResource(BackgroundColorProperty, FormTheme.PageBg);
            Children.Add(_dialog);
            SizeChanged += OnSizeChanged;
            Loaded += OnLoaded;
        }

        private void OnSizeChanged(object? sender, EventArgs e)
        {
            _dialog.WidthRequest = Math.Min(_preferredWidth, Math.Max(1, Width - OuterMargin * 2));
            _dialog.HeightRequest = Math.Min(_preferredHeight, Math.Max(1, Height - OuterMargin * 2));
        }

        private void OnLoaded(object? sender, EventArgs e)
        {
            Loaded -= OnLoaded;
            if (_dialog.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement dialog)
                return;

            dialog.DispatcherQueue.TryEnqueue(() =>
            {
                if (_page.Window?.Page?.Navigation.ModalStack.LastOrDefault() != _page || dialog.XamlRoot == null)
                    return;

                var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(dialog.XamlRoot)
                    as Microsoft.UI.Xaml.DependencyObject;
                for (var current = focused; current != null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
                {
                    if (current == dialog)
                        return;
                }

                if (Microsoft.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(dialog) is Microsoft.UI.Xaml.UIElement first)
                    first.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            });
        }
    }
}
