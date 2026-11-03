using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace CodexQuotaRing;
static class Gallery
{
    public static void RenderThemes()
    {
        _ = new Application();
        foreach (string mode in new[] { "light", "dark" })
        {
            Theme.Mode = mode; Theme.Refresh(true);
            var canvas = new Canvas { Width = 680, Height = 590, Background = Theme.Surface };
            void Place(UIElement item, double x, double y) { Canvas.SetLeft(item,x); Canvas.SetTop(item,y); canvas.Children.Add(item); }
            Place(new RingVisual { Width = 76, Height = 76, Week = 54, Five = 82, Label = "54%" }, 35, 25);
            var details = new MicaMenu(RingWindow.BuildDetails(new(new(54,0),new(82,0)),false,"主题预览",DateTime.Now));
            var commands = new ContextMenu();
            foreach (var label in new[] { "立即刷新", "中间显示：每周剩余", "中间显示：5 小时剩余", "中间显示：较低余额", "主题：跟随系统", "主题：浅色", "主题：深色", "退出额度环" }) commands.Items.Add(new MenuItem { Header = label });
            var menu = new MicaMenu(commands,"min");
            void Card(MicaMenu window, double x, double y, double width)
            {
                var content = (Border)window.Content;
                window.Content = null; content.Width = width;
                System.Windows.Documents.TextElement.SetForeground(content,Theme.Foreground);
                content.Background = Theme.Surface;
                Place(content,x,y);
            }
            Card(details,25,135,300); Card(menu,355,25,300);
            canvas.Measure(new Size(680,590)); canvas.Arrange(new Rect(0,0,680,590)); canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap(680,590,96,96,PixelFormats.Pbgra32); bitmap.Render(canvas);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(AppContext.BaseDirectory,$"theme-{mode}.png")); png.Save(file);
            details.Close(); menu.Close();
        }
    }
    public static void Render()
    {
        _ = new Application();
        var canvas = new Canvas { Width = 1000, Height = 760, Background = new SolidColorBrush(Color.FromRgb(233,239,245)) };
        void Place(UIElement e, double x, double y) { Canvas.SetLeft(e,x); Canvas.SetTop(e,y); canvas.Children.Add(e); }
        Place(new TextBlock { Text = "Codex Quota Ring", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray }, 40,30);
        Place(new TextBlock { Text = "左下角双环 · 悬停详情 · 圆角菜单", FontSize = 16, Foreground = Brushes.SlateGray }, 42,77);
        var sidebar = new Border { Width = 70, Height = 530, CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Color.FromRgb(244,248,251)) };
        Place(sidebar,40,130);
        Place(new TextBlock { Text = "⌂", FontSize = 28, Foreground = Brushes.SlateGray },62,155);
        Place(new RingVisual { Width=38, Height=38, Week=61, Five=82, Label="61%" },56,554);
        Place(new Border { Width=32,Height=32,CornerRadius=new CornerRadius(16),Background=new SolidColorBrush(Color.FromRgb(207,221,233)),Child=new TextBlock { Text="T",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.SlateGray } },59,610);
        var detail = new MicaMenu(RingWindow.BuildDetails(new(new(61,0),new(82,0)),false,"示例数据 · 每 60 秒刷新",DateTime.Now));
        var detailContent = (FrameworkElement)detail.Content; detail.Content = null;
        ((Border)detailContent).Background = new SolidColorBrush(Color.FromRgb(246,248,251));
        detailContent.Width=300; Place(detailContent,135,325);
        var commands=new ContextMenu();
        string[] labels=["立即刷新","中间显示：每周剩余","中间显示：5 小时剩余","中间显示：较低余额","位置向上微调","位置向下微调","位置向左微调","位置向右微调","恢复默认位置","退出额度环"];
        for(int i=0;i<labels.Length;i++) { if(i is 4 or 9) commands.Items.Add(new Separator()); commands.Items.Add(new MenuItem { Header=labels[i] }); }
        var menu=new MicaMenu(commands,"min"); var menuContent=(FrameworkElement)menu.Content; menu.Content=null;
        ((Border)menuContent).Background=new SolidColorBrush(Color.FromRgb(246,248,251)); menuContent.Width=258; Place(menuContent,530,130);
        Place(new TextBlock { Text="界面渲染预览，使用示例额度。Mica 壁纸混色效果以实际 Windows 桌面为准。",FontSize=13,Foreground=Brushes.SlateGray },42,710);
        canvas.Measure(new Size(1000,760)); canvas.Arrange(new Rect(0,0,1000,760)); canvas.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1500,1140,144,144,PixelFormats.Pbgra32); bitmap.Render(canvas);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"ui-preview.png")); encoder.Save(stream);
        detail.Close(); menu.Close();
    }
}
