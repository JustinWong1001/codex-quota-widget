using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;

// All decoration is vector-drawn; no external images or animation are needed.
sealed class QuotaTheme
{
    public readonly string Code, Chinese, English, Font;
    public readonly Color Background, Foreground, Secondary, Edge, First, Second, Warning;
    public readonly double Radius;
    QuotaTheme(string code, string zh, string en, string font, string bg, string fg,
        string secondary, string edge, string first, string second, string warning, double radius)
    {
        Code = code; Chinese = zh; English = en; Font = font; Radius = radius;
        Background = C(bg); Foreground = C(fg); Secondary = C(secondary); Edge = C(edge);
        First = C(first); Second = C(second); Warning = C(warning);
    }
    static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    public static readonly QuotaTheme[] All = {
        new QuotaTheme("glass", "经典玻璃", "Classic glass", "Segoe UI", "#1C2431", "#FFFFFF", "#BED0E4", "#607489", "#6FF1C7", "#87C5FF", "#FFC767", 23),
        new QuotaTheme("drive", "汽车仪表", "Night drive", "Bahnschrift", "#10171D", "#EAF5F8", "#A0B5BD", "#49616C", "#53E1C0", "#76B9FF", "#FFAE6A", 17),
        new QuotaTheme("diesel", "柴油朋克", "Dieselpunk", "Georgia", "#29241E", "#F6E6C7", "#C9B590", "#9F8051", "#EBCB83", "#D3A36E", "#FF9870", 10),
        new QuotaTheme("hud", "科幻 HUD", "Orbital HUD", "Consolas", "#0B1D2A", "#D7FAFF", "#8AC0D3", "#398397", "#5DE6F5", "#B3A0FF", "#FFC36F", 7),
        new QuotaTheme("paper", "浅色纸卡", "Paper card", "Segoe UI", "#F3EFE5", "#243A3A", "#526763", "#BDCBC0", "#297B65", "#496E9B", "#AC4B26", 21)
    };
    public static QuotaTheme Find(string code)
    {
        foreach (var theme in All) if (theme.Code == code) return theme;
        return All[0];
    }
    public SolidColorBrush Brush(Color color) { return new SolidColorBrush(color); }
}

sealed class QuotaDecoration : FrameworkElement
{
    public QuotaTheme Theme = QuotaTheme.All[0];
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var edge = new Pen(Theme.Brush(Theme.Edge), 0.7);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        if (Theme.Code == "drive")
        {
            // Fine diagonal carbon-weave marks, kept away from the readings.
            dc.PushOpacity(0.13);
            for (double x = -h; x < w; x += 7) dc.DrawLine(edge, new Point(x, 0), new Point(x + h, h));
            dc.Pop();
            dc.DrawLine(edge, new Point(0, 22), new Point(w, 22));
            dc.DrawLine(edge, new Point(w / 2, 34), new Point(w / 2, h - 25));
        }
        else if (Theme.Code == "diesel")
        {
            dc.PushOpacity(0.10);
            for (double y = 0; y < h; y += 3) dc.DrawLine(edge, new Point(0, y), new Point(w, y));
            dc.Pop();
            dc.DrawRoundedRectangle(null, edge, new Rect(0.5, 0.5, w - 1, h - 1), 4, 4);
            foreach (Point p in new[] { new Point(5, 5), new Point(w - 5, 5), new Point(5, h - 5), new Point(w - 5, h - 5) })
            {
                dc.DrawEllipse(Theme.Brush(Theme.Edge), null, p, 2, 2);
                dc.DrawLine(new Pen(Theme.Brush(Theme.Background), 0.8), new Point(p.X - 1, p.Y + 1), new Point(p.X + 1, p.Y - 1));
            }
            dc.DrawLine(edge, new Point(16, 22), new Point(w - 16, 22));
        }
        else if (Theme.Code == "hud")
        {
            dc.PushOpacity(0.11);
            for (double y = 0; y < h; y += 5) dc.DrawLine(edge, new Point(0, y), new Point(w, y));
            dc.Pop();
            double a = 9;
            foreach (Point p in new[] { new Point(0, 0), new Point(w, 0), new Point(0, h), new Point(w, h) })
            {
                double dx = p.X == 0 ? a : -a, dy = p.Y == 0 ? a : -a;
                dc.DrawLine(edge, p, new Point(p.X + dx, p.Y)); dc.DrawLine(edge, p, new Point(p.X, p.Y + dy));
            }
            dc.DrawLine(edge, new Point(0, 23), new Point(w * 0.66, 23));
            dc.DrawLine(edge, new Point(w * 0.66, 23), new Point(w * 0.66 + 5, 18));
            dc.DrawLine(edge, new Point(w / 2, 38), new Point(w / 2, h - 29));
        }
        else if (Theme.Code == "paper")
        {
            dc.DrawLine(edge, new Point(0, 23), new Point(w, 23));
            dc.DrawLine(edge, new Point(0, h - 19), new Point(w, h - 19));
        }
        dc.Pop();
    }
}

sealed class QuotaInstrument : FrameworkElement
{
    protected override AutomationPeer OnCreateAutomationPeer() { return new FrameworkElementAutomationPeer(this); }
    public double? Value;
    public Color Tint;
    public QuotaTheme Theme = QuotaTheme.All[0];
    public string ReserveLabel = "剩余";
    string Number { get { return Value.HasValue ? Value.Value.ToString("0.#", CultureInfo.CurrentCulture) + "%" : "—"; } }
    static Point Polar(Point center, double radius, double degrees)
    {
        double angle = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }
    void Label(DrawingContext dc, string text, Point center, double size, Color color, string font, bool bold)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(font + ", Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }
    void Arc(DrawingContext dc, Point center, double radius, double start, double sweep, Pen pen)
    {
        var path = new StreamGeometry();
        using (var g = path.Open()) { g.BeginFigure(Polar(center, radius, start), false, false); g.ArcTo(Polar(center, radius, start + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false); }
        dc.DrawGeometry(null, pen, path);
    }
    protected override void OnRender(DrawingContext dc)
    {
        double amount = Value.HasValue ? Math.Max(0, Math.Min(100, Value.Value)) : 0;
        var lit = Theme.Brush(Tint); var edge = Theme.Brush(Theme.Edge);
        if (Theme.Code == "drive")
        {
            var c = new Point(52.5, 39); double start = 155, sweep = 230;
            Arc(dc, c, 34, start, sweep, new Pen(edge, 3));
            if (Value.HasValue && amount > 0) Arc(dc, c, 34, start, sweep * amount / 100, new Pen(lit, 3));
            for (int i = 0; i <= 20; i++)
            {
                double angle = start + sweep * i / 20;
                dc.DrawLine(new Pen(i <= 2 ? Theme.Brush(Theme.Warning) : Theme.Brush(Theme.Secondary), i % 5 == 0 ? 1.2 : 0.6),
                    Polar(c, i % 5 == 0 ? 26 : 29, angle), Polar(c, 31, angle));
            }
            Label(dc, "0", new Point(16, 53), 7, Theme.Secondary, Theme.Font, false);
            Label(dc, "100", new Point(91, 53), 7, Theme.Secondary, Theme.Font, false);
            if (Value.HasValue)
            {
                double angle = start + sweep * amount / 100;
                dc.DrawLine(new Pen(lit, 1.6), Polar(c, 5, angle + 180), Polar(c, 25, angle));
                dc.DrawEllipse(Theme.Brush(Theme.Background), new Pen(lit, 1), c, 3, 3);
            }
            Label(dc, Number, new Point(52.5, 58), 15, Theme.Foreground, Theme.Font, true);
        }
        else if (Theme.Code == "diesel")
        {
            var c = new Point(52.5, 32);
            dc.DrawEllipse(Theme.Brush(Color.FromRgb(29, 27, 23)), new Pen(edge, 2.4), c, 30, 30);
            dc.DrawEllipse(null, new Pen(Theme.Brush(Theme.Secondary), 0.5), c, 27, 27);
            for (int i = 0; i <= 20; i++)
            {
                double angle = 145 + i * 12.5;
                dc.DrawLine(new Pen(Theme.Brush(Theme.Secondary), i % 5 == 0 ? 1.4 : 0.7), Polar(c, i % 5 == 0 ? 21 : 24, angle), Polar(c, 26, angle));
            }
            Label(dc, "0", Polar(c, 17, 145), 6, Theme.Secondary, "Georgia", false);
            Label(dc, "100", Polar(c, 17, 395), 6, Theme.Secondary, "Georgia", false);
            if (Value.HasValue)
            {
                double angle = 145 + amount * 2.5;
                var needle = new StreamGeometry();
                using (var g = needle.Open()) { g.BeginFigure(Polar(c, 2.5, angle - 90), true, true); g.LineTo(Polar(c, 22, angle), true, false); g.LineTo(Polar(c, 2.5, angle + 90), true, false); }
                dc.DrawGeometry(lit, null, needle);
                dc.DrawEllipse(edge, new Pen(lit, 0.6), c, 3.1, 3.1);
            }
            dc.DrawRoundedRectangle(Theme.Brush(Color.FromRgb(39, 34, 26)), new Pen(edge, 0.7), new Rect(31, 46, 43, 16), 2, 2);
            Label(dc, Number, new Point(52.5, 53), 12, Tint, "Consolas", true);
        }
        else if (Theme.Code == "hud")
        {
            var c = new Point(30, 32);
            for (int i = 0; i < 32; i++)
                Arc(dc, c, 24, -90 + i * 11.25, 6.5, new Pen(Value.HasValue && i < (int)Math.Round(amount * 32 / 100) ? lit : edge, 2.8));
            dc.DrawEllipse(null, new Pen(edge, 0.5), c, 18, 18);
            dc.DrawLine(new Pen(edge, 0.5), new Point(17, 32), new Point(25, 32));
            dc.DrawLine(new Pen(edge, 0.5), new Point(35, 32), new Point(43, 32));
            dc.DrawLine(new Pen(edge, 0.5), new Point(30, 19), new Point(30, 27));
            dc.DrawLine(new Pen(edge, 0.5), new Point(30, 37), new Point(30, 45));
            dc.DrawEllipse(lit, null, c, 1.8, 1.8);
            Label(dc, Number, new Point(80, 29), 16, Tint, Theme.Font, true);
            Label(dc, ReserveLabel, new Point(79, 45), 8, Theme.Secondary, Theme.Font, false);
        }
        else if (Theme.Code == "paper")
        {
            Label(dc, Number, new Point(52.5, 26), 27, Theme.Foreground, Theme.Font, true);
            dc.DrawRoundedRectangle(Theme.Brush(Color.FromRgb(218, 224, 213)), null, new Rect(6, 49, 93, 5), 2.5, 2.5);
            if (Value.HasValue && amount > 0) dc.DrawRoundedRectangle(lit, null, new Rect(6, 49, 93 * amount / 100, 5), 2.5, 2.5);
            for (int i = 1; i < 4; i++) dc.DrawLine(new Pen(Theme.Brush(Theme.Background), 1.2), new Point(6 + 93 * i / 4, 49), new Point(6 + 93 * i / 4, 54));
        }
    }
}
