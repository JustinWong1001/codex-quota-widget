using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Render the real card offline with explicit sample values. No account is read.
static class ThemePreview
{
    static void Save(Visual visual, int w, int h, string path)
    {
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
    }
    static FrameworkElement Card(string theme, string lang, string style, double? first, double? second)
    {
        var card = Widget.PreviewCard(new Preferences { Theme = theme, Language = lang, DisplayStyle = style, Glass = 0.88 }, first, second);
        card.Measure(new Size(270, 190)); card.Arrange(new Rect(0, 0, 270, 190)); card.UpdateLayout(); return card;
    }
    static void Text(DrawingContext dc, string text, double x, double y, double size, string hex)
    {
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei UI"), size, new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), 1), new Point(x, y));
    }
    [STAThread] static void Main(string[] args)
    {
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var application = new Application();
        foreach (var theme in QuotaTheme.All)
            foreach (string language in new[] { "zh", "en" })
                foreach (string style in new[] { "rings", "grid" })
                {
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen()) {
                        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(51, 60, 68)), null, new Rect(0, 0, 540, 380));
                        dc.PushTransform(new ScaleTransform(2, 2));
                        dc.DrawRectangle(new VisualBrush(Card(theme.Code, language, style, 76, 42)), null, new Rect(0, 0, 270, 190)); dc.Pop();
                    }
                    Save(visual, 540, 380, Path.Combine(output, theme.Code + "-" + language + "-" + style + ".png"));
                }
        // Boundary values must have distinct appearances, including missing data.
        foreach (var theme in QuotaTheme.All)
            foreach (string style in new[] { "rings", "grid" }) {
                Save(Card(theme.Code, "en", style, 0, null), 270, 190, Path.Combine(output, theme.Code + "-" + style + "-missing.png"));
                Save(Card(theme.Code, "zh", style, 100, 10), 270, 190, Path.Combine(output, theme.Code + "-" + style + "-bounds.png"));
            }
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 28, 36)), null, new Rect(0, 0, 960, 800));
            Text(dc, "CODEX / 新的桌面表情", 38, 22, 25, "#EAF2F5");
            Text(dc, "四种可切换主题 · 保留重置时间、中英文与方格显示", 39, 62, 13, "#93A8B7");
            string[] names = { "01  NIGHT DRIVE / 汽车仪表", "02  DIESELPUNK / 柴油朋克", "03  ORBITAL HUD / 科幻", "04  PAPER CARD / 浅色纸卡" };
            string[] captions = { "指针刻度 · 冷色灯光 · 夜间驾驶舱", "黄铜表圈 · 铆钉面板 · 机械读数", "分段光环 · 十字准星 · 轨道终端", "大号数字 · 柔和浅底 · 简洁进度条" };
            for (int i = 0; i < 4; i++) {
                double x = 29 + i % 2 * 465, y = 109 + i / 2 * 329;
                Text(dc, names[i], x + 12, y, 16, "#D5E5EC");
                dc.DrawRectangle(new VisualBrush(Card(QuotaTheme.All[i + 1].Code, "zh", "rings", 76, 42)), null, new Rect(x, y + 25, 432, 304));
                Text(dc, captions[i], x + 13, y + 309, 12, "#91A5B4");
            }
            Text(dc, "预览使用示例额度；实际程序显示当前账号数据。右键组件 → 界面主题。", 41, 766, 12, "#91A5B4");
        }
        Save(sheet, 960, 800, Path.Combine(output, "theme-overview.png"));
        Console.WriteLine("Rendered 41 offline theme previews.");
    }
}
