using Microsoft.Win32;
using System.Windows.Media;

namespace CodexQuotaRing;
static class Theme
{
    public static string Mode = "system";
    public static bool Dark { get; private set; }
    public static event Action? Changed;
    static DateTime next;
    public static readonly SolidColorBrush Foreground = new();
    public static readonly SolidColorBrush Surface = new();
    public static readonly SolidColorBrush Border = new();
    public static readonly SolidColorBrush Hover = new();
    public static readonly SolidColorBrush Week = new();
    public static readonly SolidColorBrush Five = new();
    public static readonly SolidColorBrush Track = new();
    static Theme() { Refresh(true); }
    public static void Refresh(bool force = false)
    {
        if (!force && DateTime.UtcNow < next) return;
        next = DateTime.UtcNow.AddSeconds(1);
        bool dark = Dark;
        try { dark = Mode == "dark" || (Mode != "light" && (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0); }
        catch { }
        if (!force && dark == Dark) return;
        Dark = dark;
        Color C(string value) => (Color)ColorConverter.ConvertFromString(value);
        Foreground.Color = C(dark ? "#F2F4F7" : "#232A32");
        Surface.Color = C(dark ? "#23262B" : "#F4F7FA");
        Border.Color = C(dark ? "#28FFFFFF" : "#19000000");
        Hover.Color = C(dark ? "#20FFFFFF" : "#10000000");
        Week.Color = C(dark ? "#45D6A4" : "#168663");
        Five.Color = C(dark ? "#78B2FF" : "#3479D5");
        Track.Color = C(dark ? "#657F8C99" : "#3782919B");
        Changed?.Invoke();
    }
}
