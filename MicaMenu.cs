using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;

namespace CodexQuotaRing;
public sealed class MicaMenu : Window
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int value, int size);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor,Work; public uint Flags; }
    readonly Border surface;
    readonly bool dark;
    bool passive;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    public MicaMenu(FrameworkElement details) : this(new ContextMenu(), "min")
    {
        passive = true; ShowActivated = false; Width = 300; Title = "额度详情";
        surface.Child = details;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(h, -20, new IntPtr(GetWindowLongPtr(h, -20).ToInt64() | 0x08000000L | 0x80L));
            HwndSource.FromHwnd(h)!.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) => { if (msg == 0x21) { handled = true; return new IntPtr(3); } return IntPtr.Zero; });
        };
    }
    public MicaMenu(ContextMenu commands, string selected)
    {
        dark = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0;
        Title = "额度环设置"; Width = 258; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Background = Brushes.Transparent;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(-1), CornerRadius = new CornerRadius(12), UseAeroCaptionButtons = false });
        var foreground = new SolidColorBrush(dark ? Color.FromRgb(242,244,247) : Color.FromRgb(35,42,50));
        Foreground = foreground; FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 13;
        var stack = new StackPanel { Margin = new Thickness(6) };
        var heading = new StackPanel { Margin = new Thickness(12,9,12,10) };
        heading.Children.Add(new TextBlock { Text = "额度环", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = foreground });
        heading.Children.Add(new TextBlock { Text = "每 60 秒自动刷新", FontSize = 11, Opacity = .6, Margin = new Thickness(0,3,0,0), Foreground = foreground });
        stack.Children.Add(heading);
        string hover = dark ? "#20FFFFFF" : "#10000000";
        var template = (ControlTemplate)XamlReader.Parse($"""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Border x:Name="tile" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" CornerRadius="6" Background="Transparent" Padding="10,7">
            <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="tile" Property="Background" Value="{hover}"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="tile" Property="Background" Value="{hover}"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="tile" Property="Opacity" Value="0.65"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
        string[] icons = ["↻", "◉", "◉", "◉", "↑", "↓", "←", "→", "↺", "⏻"];
        int index = 0;
        foreach (var entry in commands.Items)
        {
            if (entry is Separator) { stack.Children.Add(new Border { Height = 1, Margin = new Thickness(10,5,10,5), Background = foreground, Opacity = .1 }); continue; }
            if (entry is not MenuItem item) continue;
            int current = index++;
            bool chosen = current == (selected == "week" ? 1 : selected == "5h" ? 2 : 3);
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.Children.Add(new TextBlock { Text = icons[current], FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = foreground });
            var label = new TextBlock { Text = item.Header.ToString()!.Replace("中间显示：", "").Replace("位置向", "向"), VerticalAlignment = VerticalAlignment.Center, Foreground = foreground }; Grid.SetColumn(label,1); row.Children.Add(label);
            if (chosen) { var check = new TextBlock { Text = "✓", Foreground = new SolidColorBrush(Color.FromRgb(32,167,122)), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(check,2); row.Children.Add(check); }
            var button = new Button { Content = row, Template = template, Margin = new Thickness(0,1,0,1), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = item.Header };
            button.Click += (_, _) => { Close(); item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)); };
            stack.Children.Add(button);
        }
        surface = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(28,255,255,255) : Color.FromArgb(25,0,0,0)), Background = Brushes.Transparent, Child = stack };
        Content = surface;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(h)!.CompositionTarget.BackgroundColor = Colors.Transparent;
            int corners = 2, theme = dark ? 1 : 0, mica = 2;
            DwmSetWindowAttribute(h, 33, ref corners, 4); DwmSetWindowAttribute(h, 20, ref theme, 4);
            int result = DwmSetWindowAttribute(h, 38, ref mica, 4);
            if (result != 0) surface.Background = new SolidColorBrush(dark ? Color.FromRgb(35,38,43) : Color.FromRgb(244,247,250));
        };
        Deactivated += (_, _) => { if (!passive) Close(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
    public void OpenAt(Point anchor)
    {
        // Establish monitor DPI before showing, then clamp the flyout inside its work area.
        var h = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(h, IntPtr.Zero, (int)anchor.X, (int)anchor.Y, 0,0, 0x15);
        Show(); UpdateLayout();
        double scale = GetDpiForWindow(h) / 96.0;
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(new POINT { X = (int)anchor.X, Y = (int)anchor.Y }, 2), ref info);
        int width = (int)Math.Ceiling(ActualWidth * scale), height = (int)Math.Ceiling(ActualHeight * scale);
        int x = Math.Max(info.Work.Left + 8, Math.Min((int)anchor.X, info.Work.Right-width-8));
        int y = Math.Max(info.Work.Top + 8, Math.Min((int)anchor.Y-height, info.Work.Bottom-height-8));
        SetWindowPos(h, new IntPtr(-1), x,y,width,height,0x50);
        if (!passive) Activate();
    }
}
