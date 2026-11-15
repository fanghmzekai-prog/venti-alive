using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aeris.WindowProbe;

internal partial class DesktopControlWindow : Window
{
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int CaptionTextColor = 36;

    internal DesktopControlWindow(string? spritePath)
    {
        InitializeComponent();
        if (spritePath is null) return;

        try
        {
            using FileStream file = File.OpenRead(spritePath);
            var portrait = new BitmapImage();
            portrait.BeginInit();
            portrait.StreamSource = file;
            portrait.CacheOption = BitmapCacheOption.OnLoad;
            portrait.DecodePixelHeight = 448;
            portrait.EndInit();
            portrait.Freeze();
            Portrait.Source = portrait;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            // The artwork has already been validated for the desktop sprite. A late read may fail independently.
            Portrait.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || SystemParameters.HighContrast) return;

        nint window = new WindowInteropHelper(this).Handle;
        SetFrameColor(window, CaptionColor, Color.FromRgb(24, 60, 49));
        SetFrameColor(window, CaptionTextColor, Color.FromRgb(239, 247, 235));
        SetFrameColor(window, BorderColor, Color.FromRgb(84, 121, 106));
    }

    private static void SetFrameColor(nint window, int attribute, Color color)
    {
        uint colorRef = (uint)(color.R | color.G << 8 | color.B << 16);
        DwmSetWindowAttribute(window, attribute, in colorRef, sizeof(uint));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, in uint value, int size);
}
