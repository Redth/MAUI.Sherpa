using Microsoft.Maui.Controls;

namespace MauiSherpa.Pages.Forms;

public partial class HybridFormBridge
{
    internal static Button CreateHeaderActionButton(ContentPage page, HybridFormBridge bridge, HybridHeaderAction action)
    {
        var button = new Button
        {
            Text = action.FallbackText ?? string.Empty,
            FontSize = 13,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 6,
            Padding = new Thickness(8, 4),
            WidthRequest = 32,
            HeightRequest = 28,
            MinimumWidthRequest = 32,
            IsEnabled = action.IsEnabled,
            IsVisible = action.IsVisible,
            VerticalOptions = LayoutOptions.Center,
        };
        button.SetDynamicResource(Button.TextColorProperty, FormTheme.TextSecondary);
        if (!string.IsNullOrEmpty(action.ToolTip))
            ToolTipProperties.SetText(button, action.ToolTip);

        button.Clicked += async (_, _) =>
        {
            try
            {
                await bridge.RequestActionAsync(action.Id);
            }
            catch (Exception ex)
            {
                await page.Dispatcher.DispatchAsync(() => page.DisplayAlert("Error", ex.Message, "OK"));
            }
        };
        return button;
    }

#if MACOSAPP
    internal static void SyncMacHeaderActionButtons(
        ContentPage page,
        View? headerContainer,
        HybridFormBridge bridge,
        ref List<AppKit.NSButton>? macButtons)
    {
        if (macButtons != null)
        {
            foreach (var oldBtn in macButtons)
            {
                oldBtn.RemoveFromSuperview();
                oldBtn.Dispose();
            }
            macButtons.Clear();
        }

        if (headerContainer?.Handler?.PlatformView is not AppKit.NSView containerView)
            return;

        var visibleActions = bridge.HeaderActions.Where(a => a.IsVisible).ToList();
        if (visibleActions.Count == 0)
            return;

        macButtons ??= new List<AppKit.NSButton>();

        var bounds = containerView.Bounds;
        var containerWidth = bounds.Width > 0 ? bounds.Width : 540;
        var containerHeight = bounds.Height > 0 ? bounds.Height : 56;

        nfloat buttonWidth = 30;
        nfloat buttonHeight = 26;
        nfloat spacing = 6;
        nfloat rightEdge = containerWidth - 24;
        nfloat y = (nfloat)Math.Max(4, (containerHeight - buttonHeight) / 2 + 3);

        var symbolConfig = AppKit.NSImageSymbolConfiguration.Create(13f, AppKit.NSFontWeight.Medium);

        for (var i = visibleActions.Count - 1; i >= 0; i--)
        {
            var action = visibleActions[i];
            var actionId = action.Id;
            rightEdge -= buttonWidth;

            var nsButton = AppKit.NSButton.CreateButton(action.FallbackText ?? string.Empty, async () =>
            {
                try
                {
                    await bridge.RequestActionAsync(actionId);
                }
                catch (Exception ex)
                {
                    await page.Dispatcher.DispatchAsync(() => page.DisplayAlert("Error", ex.Message, "OK"));
                }
            });
            nsButton.BezelStyle = AppKit.NSBezelStyle.AccessoryBarAction;
            nsButton.ControlSize = AppKit.NSControlSize.Regular;
            nsButton.Enabled = action.IsEnabled;
            if (!string.IsNullOrEmpty(action.ToolTip))
                nsButton.ToolTip = action.ToolTip;

            var symbol = AppKit.NSImage.GetSystemSymbol(action.Icon, null);
            if (symbol != null)
            {
                if (symbolConfig != null)
                    symbol = symbol.GetImage(symbolConfig) ?? symbol;
                nsButton.Image = symbol;
                nsButton.ImagePosition = AppKit.NSCellImagePosition.ImageOnly;
                nsButton.Title = string.Empty;
            }

            nsButton.Frame = new CoreGraphics.CGRect(rightEdge, y, buttonWidth, buttonHeight);
            nsButton.AutoresizingMask = AppKit.NSViewResizingMask.MinXMargin;

            containerView.AddSubview(nsButton);
            macButtons.Add(nsButton);

            rightEdge -= spacing;
        }
    }
#endif
}
