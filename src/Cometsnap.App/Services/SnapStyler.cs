using System.Windows.Media.Imaging;
using Cometsnap.App.Theme;
using Cometsnap.Core.Settings;
using Cometsnap.Core.Styling;

namespace Cometsnap.App.Services;

/// <summary>Applies the share-ready style (Settings > Style) to a WPF image. Safe on any thread.</summary>
public static class SnapStyler
{
    /// <summary>The capture in the style chosen in settings, in the current capture colors.</summary>
    public static BitmapSource Apply(BitmapSource image, AppSettings settings) =>
        StyledBitmaps.Apply(image, SnapStyles.Get(settings.SnapStyle), CaptureLook.Theme.Accent);

}
