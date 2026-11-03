using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexQuotaRing;
public class Settings
{
    public double CenterX { get; set; } = 80;
    public double Bottom { get; set; } = 24;
    public double Size { get; set; } = 38;
    public string Center { get; set; } = "min";
}
public static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        if (args.Contains("--watch"))
        {
            using var watcher = new Mutex(true, "Local\\CodexQuotaRing.Watcher.v1", out bool firstWatcher);
            if (!firstWatcher) return;
            while (true)
            {
                using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true });
                if (child == null) return;
                child.WaitForExit();
                // Explicit menu exit stops recovery until the next launch/login.
                if (child.ExitCode == 42) return;
                Thread.Sleep(3000);
            }
        }
        if (args.Contains("--gallery")) { Gallery.Render(); return; }
        if (args.Contains("--menu-preview"))
        {
            var previewApp = new Application(); var commands = new ContextMenu();
            foreach (string label in new[] { "立即刷新", "中间显示：每周剩余", "中间显示：5 小时剩余", "中间显示：较低余额", "位置向上微调", "位置向下微调", "位置向左微调", "位置向右微调", "恢复默认位置", "退出额度环" }) { if (commands.Items.Count is 4 or 10) commands.Items.Add(new Separator()); commands.Items.Add(new MenuItem { Header = label }); }
            var previewMenu = new MicaMenu(commands, "min");
            previewApp.Startup += (_, _) =>
            {
                previewMenu.OpenAt(new Point(450,800));
                var panel = (FrameworkElement)previewMenu.Content;
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)panel.ActualWidth*2,(int)panel.ActualHeight*2,192,192,PixelFormats.Pbgra32); bmp.Render(panel);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var file = File.Create(Path.Combine(AppContext.BaseDirectory,"menu-preview.png")); png.Save(file);
                previewApp.Shutdown();
            };
            previewApp.Run(); return;
        }
        if (args.Contains("--render"))
        {
            var v = new RingVisual { Week = 61, Five = 82, Label = "61%", Width = 38, Height = 38 };
            v.Measure(new Size(38,38)); v.Arrange(new System.Windows.Rect(0,0,38,38)); v.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(228,228,576,576,PixelFormats.Pbgra32); bitmap.Render(v);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(AppContext.BaseDirectory,"preview.png")); encoder.Save(file); return;
        }
        if (args.Contains("--check"))
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "check.json"), JsonSerializer.Serialize(QuotaClient.Read(CancellationToken.None).GetAwaiter().GetResult())); }
            catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "check.json"), JsonSerializer.Serialize(new { Error = e.Message })); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--test")) { Tests.Run(); return; }
        using var mutex = new Mutex(true, "Local\\CodexQuotaRing.Custom.v1", out bool first);
        if (!first) return;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var ring = new RingWindow(args.Contains("--preview"));
        app.Run();
    }
}
public class RingWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Pt { public int X, Y; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref Pt pt);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly CancellationTokenSource cancel = new();
    readonly RingVisual visual;
    MicaMenu? tip;
    readonly Settings settings;
    readonly bool preview;
    MicaMenu? flyout;
    IntPtr handle;
    IntPtr codexWindow;
    DateTime nextWindowSearch;
    Quota quota = new(null, null);
    DateTime next, updated;
    bool busy, stale;
    bool pointerDown, dragging;
    Point dragStart;
    double dragCenterX, dragBottom;
    string error = "正在读取额度…";
    readonly string config = Path.Combine(AppContext.BaseDirectory, "settings.json");
    public RingWindow(bool preview)
    {
        this.preview = preview;
        try { settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(config)) ?? new(); } catch { settings = new(); }
        // Migrate the old default only; preserve deliberately customized positions.
        if (settings.CenterX == 32 && settings.Bottom == 68) { settings.CenterX = 80; settings.Bottom = 24; }
        settings.Size = Math.Clamp(settings.Size, 32, 56);
        Title = "Codex 额度环"; Width = Height = settings.Size;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = preview;
        visual = new RingVisual(); Content = visual;
        var menu = new ContextMenu();
        void Add(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("立即刷新", () => next = DateTime.MinValue);
        Add("中间显示：每周剩余", () => { settings.Center = "week"; Save(); });
        Add("中间显示：5 小时剩余", () => { settings.Center = "5h"; Save(); });
        Add("中间显示：较低余额", () => { settings.Center = "min"; Save(); });
        menu.Items.Add(new Separator());
        Add("位置向上微调", () => { settings.Bottom += 2; Save(); });
        Add("位置向下微调", () => { settings.Bottom -= 2; Save(); });
        Add("位置向左微调", () => { settings.CenterX -= 2; Save(); });
        Add("位置向右微调", () => { settings.CenterX += 2; Save(); });
        Add("恢复默认位置", () => { settings.CenterX = 80; settings.Bottom = 24; Save(); });
        menu.Items.Add(new Separator()); Add("退出额度环", () => { Environment.ExitCode = 42; cancel.Cancel(); timer.Stop(); Close(); Application.Current.Shutdown(42); });
        visual.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true; HideTip();
            if (flyout?.IsVisible == true) { flyout.Close(); return; }
            flyout = new MicaMenu(menu, settings.Center);
            flyout.OpenAt(visual.PointToScreen(new Point(ActualWidth + 6, ActualHeight)));
        };
        visual.MouseLeftButtonDown += (_, e) =>
        {
            if (preview) return;
            dragStart = visual.PointToScreen(e.GetPosition(visual));
            dragCenterX = settings.CenterX; dragBottom = settings.Bottom;
            pointerDown = visual.CaptureMouse(); dragging = false;
            flyout?.Close(); flyout = null;
            e.Handled = true;
        };
        visual.MouseMove += (_, e) =>
        {
            if (!pointerDown) return;
            if (e.LeftButton != MouseButtonState.Pressed) { FinishDrag(); return; }
            if (!GetClientRect(codexWindow, out var bounds)) { FinishDrag(); return; }
            double scale = GetDpiForWindow(codexWindow) / 96.0;
            if (scale <= 0) return;
            var delta = (visual.PointToScreen(e.GetPosition(visual)) - dragStart) / scale;
            if (!dragging && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragging = true; HideTip(); visual.Cursor = Cursors.SizeAll;
            double half = settings.Size / 2;
            settings.CenterX = Math.Clamp(dragCenterX + delta.X, half, Math.Max(half, bounds.Right / scale - half));
            settings.Bottom = Math.Clamp(dragBottom - delta.Y, half, Math.Max(half, bounds.Bottom / scale - half));
            Tick();
            e.Handled = true;
        };
        visual.MouseLeftButtonUp += (_, e) =>
        {
            bool wasDragging = dragging;
            FinishDrag();
            if (!wasDragging) { if (tip?.IsVisible == true) HideTip(); else UpdateTip(); }
            e.Handled = true;
        };
        visual.LostMouseCapture += (_, _) => FinishDrag();
        visual.MouseEnter += (_, _) => { if (!pointerDown && flyout?.IsVisible != true) UpdateTip(); };
        visual.MouseLeave += (_, _) => HideTip();
        handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000L | 0x80L));
        HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) => { if (msg == 0x21) { handled = true; return new IntPtr(3); } return IntPtr.Zero; });
        timer.Tick += (_, _) => Tick(); timer.Start();
        Closed += (_, _) => { cancel.Cancel(); timer.Stop(); Application.Current.Shutdown(Environment.ExitCode); };
        if (preview) { quota = new(new(72, 0), new(86, 0)); error = "预览数据"; updated = DateTime.Now; Left = 200; Top = 200; Show(); }
    }
    void Save() { try { File.WriteAllText(config, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); } catch { } Render(); }
    void FinishDrag()
    {
        bool save = dragging;
        pointerDown = false; dragging = false; visual.Cursor = null;
        if (visual.IsMouseCaptured) visual.ReleaseMouseCapture();
        if (save) Save();
    }
    bool IsCodex(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        try { using var p = Process.GetProcessById((int)pid); var path = p.MainModule?.FileName ?? ""; return (p.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase)) && path.Contains("OpenAI.Codex", StringComparison.OrdinalIgnoreCase); } catch { return false; }
    }
    void Tick()
    {
        if (preview) { if (!IsVisible) Show(); SetWindowPos(handle, new IntPtr(-1), 240, 240, 76, 76, 0x50); }
        var fg = GetForegroundWindow();
        if (!preview)
        {
            if (DateTime.UtcNow >= nextWindowSearch || !IsWindow(codexWindow))
            {
                nextWindowSearch = DateTime.UtcNow.AddSeconds(2);
                IntPtr found = IntPtr.Zero;
                foreach (string name in new[] { "ChatGPT", "Codex" })
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        try
                        {
                            var candidate = process.MainWindowHandle;
                            if (candidate != IntPtr.Zero && IsCodex(candidate))
                            {
                                if (found == IntPtr.Zero || candidate == fg || candidate == codexWindow) found = candidate;
                                if (candidate == fg) break;
                            }
                        }
                        catch (InvalidOperationException) { }
                    }
                }
                if (found != codexWindow)
                {
                    codexWindow = found;
                    // Native ownership keeps the ring above Codex but below apps covering it.
                    SetWindowLongPtr(handle, -8, codexWindow);
                }
            }
            bool cloaked = codexWindow != IntPtr.Zero && DwmGetWindowAttribute(codexWindow, 14, out int hidden, 4) == 0 && hidden != 0;
            if (codexWindow != IntPtr.Zero && IsWindowVisible(codexWindow) && !IsIconic(codexWindow) && !cloaked && GetClientRect(codexWindow, out var r))
            {
                var origin = new Pt(); ClientToScreen(codexWindow, ref origin); double scale = GetDpiForWindow(codexWindow) / 96.0;
                int size = (int)Math.Round(settings.Size * scale);
                int x = origin.X + (int)Math.Round(settings.CenterX * scale - size / 2.0);
                int y = origin.Y + r.Bottom - (int)Math.Round(settings.Bottom * scale + size / 2.0);
                if (!IsVisible) Show();
                SetWindowPos(handle, IntPtr.Zero, x, y, size, size, 0x54);
            }
            else { FinishDrag(); HideTip(); flyout?.Close(); flyout = null; Hide(); }
            if (!busy && DateTime.UtcNow >= next) _ = Refresh();
        }
        Render();
    }
    async Task Refresh()
    {
        busy = true;
        try { quota = await QuotaClient.Read(cancel.Token); updated = DateTime.Now; stale = false; error = quota.Week == null && quota.FiveHour == null ? "接口未返回每周或 5 小时额度" : ""; }
        catch (OperationCanceledException) { stale = true; error = "读取超时，稍后重试"; }
        catch (Exception) { stale = true; error = "读取失败，请检查 Codex 登录或网络"; }
        finally { busy = false; next = DateTime.UtcNow.AddSeconds(60); Render(); }
    }
    void Render()
    {
        var center = settings.Center == "week" ? quota.Week?.Remaining : settings.Center == "5h" ? quota.FiveHour?.Remaining : quota.Week == null ? quota.FiveHour?.Remaining : quota.FiveHour == null ? quota.Week.Remaining : Math.Min(quota.Week.Remaining, quota.FiveHour.Remaining);
        visual.Week = quota.Week?.Remaining; visual.Five = quota.FiveHour?.Remaining;
        visual.Label = center.HasValue ? Math.Floor(center.Value).ToString("0") + "%" : "--";
        visual.Stale = stale; visual.InvalidateVisual();
    }
    void UpdateTip()
    {
        HideTip();
        tip = new MicaMenu(BuildDetails(quota, stale, error, updated));
        tip.OpenAt(visual.PointToScreen(new Point(ActualWidth + 6, ActualHeight)));
    }
    internal static FrameworkElement BuildDetails(Quota quota, bool stale, string error, DateTime updated)
    {
        var panel = new StackPanel { Margin = new Thickness(18,15,18,14) };
        panel.Children.Add(new TextBlock { Text = "剩余额度", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,14) });
        void AddLimit(string name, Limit? limit, string color)
        {
            var row = new Grid { Margin = new Thickness(0,0,0,4) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = "●  " + name, Foreground = (Brush)new BrushConverter().ConvertFromString(color)!, VerticalAlignment = VerticalAlignment.Center });
            var number = new TextBlock { Text = limit == null ? "未提供" : limit.Remaining.ToString("0.#") + "%", FontSize = limit == null ? 13 : 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(number,1); row.Children.Add(number); panel.Children.Add(row);
            string reset = "服务端暂未返回此项额度";
            if (limit != null) { reset = "重置时间未提供"; try { if (limit.Reset > 0) reset = DateTimeOffset.FromUnixTimeSeconds(limit.Reset).LocalDateTime.ToString("M/d HH:mm") + " 重置"; } catch { } }
            panel.Children.Add(new TextBlock { Text = reset, FontSize = 11, Opacity = .6, Margin = new Thickness(15,0,0,13) });
        }
        AddLimit("每周 · 外环", quota.Week, "#20A77A"); AddLimit("5 小时 · 内环", quota.FiveHour, "#478CE8");
        panel.Children.Add(new Border { Height = 1, Background = Brushes.Gray, Opacity = .2, Margin = new Thickness(0,0,0,10) });
        panel.Children.Add(new TextBlock { Text = (stale ? "旧数据 · " : "") + (error.Length > 0 ? error : "更新于 " + updated.ToString("HH:mm:ss") + " · 每 60 秒刷新"), FontSize = 11, Opacity = .65, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "左键拖动位置 · 右键设置", FontSize = 11, Opacity = .5, Margin = new Thickness(0,4,0,0) });
        return panel;
    }
    void HideTip() { var old = tip; tip = null; old?.Close(); }
}
public class RingVisual : FrameworkElement
{
    public double? Week, Five;
    public string Label = "--";
    public bool Stale;
    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight), c = size / 2;
        dc.DrawEllipse(Brushes.Transparent, null, new(c,c), c,c);
        void Ring(double radius, double? value, string color)
        {
            var track = new Pen(new SolidColorBrush(Color.FromArgb(55, 130, 145, 155)), 2.6);
            dc.DrawEllipse(null, track, new(c,c), radius, radius);
            if (value is not > 0) return;
            var brush = (Brush)new BrushConverter().ConvertFromString(Stale ? "#A5AAB0" : color)!;
            var pen = new Pen(brush, 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (value >= 99.999) { dc.DrawEllipse(null, pen, new(c,c), radius,radius); return; }
            double angle = value.Value / 100 * Math.PI * 2;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open()) { ctx.BeginFigure(new(c, c-radius), false, false); ctx.ArcTo(new(c+radius*Math.Sin(angle), c-radius*Math.Cos(angle)), new(radius,radius), 0, angle > Math.PI, SweepDirection.Clockwise, true,false); }
            dc.DrawGeometry(null,pen,geometry);
        }
        Ring(c-2.5, Week, "#20A77A"); Ring(c-7, Five, "#478CE8");
        // A tiny neutral center keeps the number readable in both app themes.
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(240,245,248)), null, new(c,c), c-10, c-10);
        var text = new FormattedText(Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), Label.Length > 3 ? 8.5 : 10, new SolidColorBrush(Color.FromRgb(46,60,69)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new(c-text.Width/2,c-text.Height/2));
        if (Stale) dc.DrawEllipse(Brushes.Orange, null, new(size-3,3),2.5,2.5);
    }
}
