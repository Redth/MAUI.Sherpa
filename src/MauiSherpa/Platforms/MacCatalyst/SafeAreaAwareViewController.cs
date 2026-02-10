using Microsoft.Maui.Platform;

namespace MauiSherpa;

public class SafeAreaAwarePageViewController : PageViewController
{
	public SafeAreaAwarePageViewController(IView page, IMauiContext mauiContext) : base(page, mauiContext)
	{
		this._page = page;
	}

	readonly IView _page;
	
	public override void ViewSafeAreaInsetsDidChange()
	{
		base.ViewSafeAreaInsetsDidChange();

		if (OperatingSystem.IsIOSVersionAtLeast(11) || OperatingSystem.IsMacCatalystVersionAtLeast(10, 15))
		{
			if (_page is Page page)
			{
                var window = CurrentPlatformView?.Window;
                
                if (window?.RootViewController?.AdditionalSafeAreaInsets.Top == 0 && window.SafeAreaInsets.Top > 0)
                {
                    window.RootViewController.AdditionalSafeAreaInsets = new UIKit.UIEdgeInsets(-window.SafeAreaInsets.Top, 0, 0, 0);
                    window.RootViewController.View?.SetNeedsLayout();
                }
			}
		}
	}
}