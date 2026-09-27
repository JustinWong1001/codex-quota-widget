using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

static class Program
{
    public static readonly string Folder = AppDomain.CurrentDomain.BaseDirectory;
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint id);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder value, int length, out int needed);
    public static string DesktopName()
    {
        var value = new StringBuilder(512); int needed;
        return GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, value, value.Capacity * 2, out needed) ? value.ToString() : "unknown";
    }
    public static void Status(string stage, object detail)
    {
        try { File.WriteAllText(Path.Combine(Folder, "widget-status.json"), new JavaScriptSerializer().Serialize(new { time = DateTime.Now.ToString("s"), pid = Process.GetCurrentProcess().Id, desktop = DesktopName(), stage = stage, detail = detail })); } catch { }
    }
    public static void Log(string kind, string message)
    {
        try {
            string file = Path.Combine(Folder, "widget.log");
            if (File.Exists(file) && new FileInfo(file).Length > 131072) File.WriteAllText(file, "");
            File.AppendAllText(file, DateTime.Now.ToString("s") + " " + kind + " " + QuotaClient.Safe(message) + Environment.NewLine);
        } catch { }
    }
    [STAThread] static void Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--check")
            {
                try { using (var client = new QuotaClient(Folder)) File.WriteAllText(Path.Combine(Folder, "check.json"), new JavaScriptSerializer().Serialize(client.Read())); }
                catch (Exception e) { File.WriteAllText(Path.Combine(Folder, "check.json"), new JavaScriptSerializer().Serialize(new { error = QuotaClient.Safe(e.Message) })); Environment.ExitCode = 1; }
                return;
            }
            string name = "Local\\CodexQuotaWidgetV3_" + WindowsIdentity.GetCurrent().User.Value + "_" + DesktopName();
            bool created;
            using (var mutex = new Mutex(true, name, out created))
            {
                if (!created) { try { using (var signal = EventWaitHandle.OpenExisting(name + "_show")) signal.Set(); } catch { } return; }
                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, name + "_show"))
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.Run(new Widget(show));
                }
            }
        }
        catch (Exception e) { Log("startup-error", e.ToString()); MessageBox.Show("启动失败：" + QuotaClient.Safe(e.Message), "Codex 额度"); }
    }
}

sealed class Preferences
{
    public double Left { get; set; }
    public double Top { get; set; }
    public bool HasPosition { get; set; }
    public bool Topmost { get; set; }
    public bool AutoStart { get; set; }
    public double Glass { get; set; }
    public string Language { get; set; }
    public string DisplayStyle { get; set; }
    public Preferences() { Topmost = true; AutoStart = true; Glass = 0.66; Language = "zh"; DisplayStyle = "rings"; }
    public static Preferences Load()
    {
        try { return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(Program.Folder, "settings.json"))); }
        catch { return new Preferences(); }
    }
    public void Save() { try { File.WriteAllText(Path.Combine(Program.Folder, "settings.json"), new JavaScriptSerializer().Serialize(this)); } catch { } }
}

static class StartupLink
{
    public static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Codex Quota.lnk"); } }
    public static void Set(bool enabled)
    {
        if (!enabled) { if (File.Exists(FilePath)) File.Delete(FilePath); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic link = shell.CreateShortcut(FilePath);
        try {
            link.TargetPath = Path.Combine(Program.Folder, "CodexQuota.exe"); link.Arguments = "--startup";
            link.WorkingDirectory = Program.Folder; link.Description = "Codex quota widget";
            link.IconLocation = Path.Combine(Program.Folder, "CodexQuota.ico") + ",0"; link.Save();
        } finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
    }
}

sealed class QuotaRing : FrameworkElement
{
    public double? Value;
    public Color Tint;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double r = 25; var center = new Point(29, 29);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), 4), center, r, r);
        if (!Value.HasValue || Value.Value <= 0) return;
        double fraction = Math.Max(0, Math.Min(1, Value.Value / 100));
        var pen = new Pen(new SolidColorBrush(Tint), 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (fraction >= 0.9999) { dc.DrawEllipse(null, pen, center, r, r); return; }
        double angle = fraction * Math.PI * 2;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open()) { ctx.BeginFigure(new Point(29, 4), false, false); ctx.ArcTo(new Point(29 + r * Math.Sin(angle), 29 - r * Math.Cos(angle)), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true, false); }
        dc.DrawGeometry(null, pen, geometry);
    }
}

// The grid is a present-time quota gauge. Cells never pretend to show daily
// history, which the rate-limits endpoint does not provide.
sealed class QuotaGrid : FrameworkElement
{
    public double? Value;
    public Color Tint;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        int filled = Value.HasValue ? (int)Math.Round(Math.Max(0, Math.Min(100, Value.Value)) / 2.0, MidpointRounding.AwayFromZero) : 0;
        var dim = new SolidColorBrush(Color.FromArgb(46, 207, 226, 244));
        var lit = new SolidColorBrush(Tint);
        for (int col = 0; col < 10; col++)
            for (int row = 0; row < 5; row++)
            {
                int index = col * 5 + row;
                dc.DrawRoundedRectangle(index < filled ? lit : dim, null,
                    new Rect(col * 9.4 + 1, row * 9.4 + 1, 7, 7), 1.6, 1.6);
            }
    }
}

sealed class Widget : Window
{
    readonly QuotaClient client = new QuotaClient(Program.Folder);
    readonly Preferences prefs = Preferences.Load();
    readonly TextBlock[] numbers = new TextBlock[2], labels = new TextBlock[2], resets = new TextBlock[2], heatNumbers = new TextBlock[2];
    readonly QuotaRing[] rings = new QuotaRing[2];
    readonly QuotaGrid[] heatmaps = new QuotaGrid[2];
    readonly StackPanel[] ringViews = new StackPanel[2], heatViews = new StackPanel[2];
    readonly Border[] quotaPanels = new Border[2];
    readonly Color mint = Color.FromRgb(111, 241, 199), blue = Color.FromRgb(135, 197, 255), amber = Color.FromRgb(255, 199, 103);
    readonly TextBlock footer = new TextBlock();
    readonly Border dot = new Border();
    readonly Border card = new Border();
    readonly Button refresh = new Button();
    readonly DispatcherTimer timer = new DispatcherTimer();
    readonly EventWaitHandle showRequest;
    readonly Forms.NotifyIcon tray;
    DateTime nextRefresh = DateTime.UtcNow, lastSuccess = DateTime.MinValue;
    Dictionary<string, object> current;
    bool busy, closing;
    int failures;
    string lastError = "", failureKind = "";
    string L(string zh, string en) { return prefs.Language == "en" ? en : zh; }
    public Widget(EventWaitHandle show)
    {
        showRequest = show;
        Title = L("Codex 剩余额度", "Codex Quota"); Width = 270; Height = 190;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(Path.Combine(Program.Folder, "CodexQuota.ico")));
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = prefs.Topmost; ShowInTaskbar = true;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); Foreground = Brushes.White;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        var area = SystemParameters.WorkArea;
        Left = prefs.HasPosition ? prefs.Left : area.Right - Width - 20; Top = prefs.HasPosition ? prefs.Top : area.Top + 32;
        EnsureOnScreen();
        card.Margin = new Thickness(8); card.CornerRadius = new CornerRadius(23); card.Padding = new Thickness(14, 10, 14, 9);
        card.BorderThickness = new Thickness(1); card.BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
        card.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.22, Color = Colors.Black };
        ApplyGlass(); Content = card;
        var body = new Grid(); card.Child = body;
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(19) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(113) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
        var header = new Grid(); body.Children.Add(header);
        var title = Text("C O D E X", 10, FontWeights.SemiBold); title.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(title);
        dot.Width = dot.Height = 5; dot.CornerRadius = new CornerRadius(3); dot.HorizontalAlignment = HorizontalAlignment.Right; dot.VerticalAlignment = VerticalAlignment.Center; dot.Margin = new Thickness(0, 0, 25, 0); dot.Background = new SolidColorBrush(amber); header.Children.Add(dot);
        refresh.Content = "↻"; refresh.FontSize = 17; refresh.Width = 20; refresh.Height = 20; refresh.HorizontalAlignment = HorizontalAlignment.Right;
        refresh.Padding = new Thickness(0); refresh.Foreground = Brushes.White; refresh.Background = Brushes.Transparent; refresh.BorderThickness = new Thickness(0); refresh.Cursor = Cursors.Hand;
        refresh.ToolTip = L("立即刷新（也可双击卡片）", "Refresh now (or double-click the card)"); refresh.FocusVisualStyle = null;
        var template = new ControlTemplate(typeof(Button)); var buttonBorder = new FrameworkElementFactory(typeof(Border)); buttonBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent); var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); buttonBorder.AppendChild(presenter); template.VisualTree = buttonBorder; refresh.Template = template;
        refresh.Click += async (s, e) => await RefreshQuota(); header.Children.Add(refresh);
        var columns = new Grid(); Grid.SetRow(columns, 1); body.Children.Add(columns);
        columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 2; i++)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
            quotaPanels[i] = new Border { Child = stack }; Grid.SetColumn(quotaPanels[i], i); columns.Children.Add(quotaPanels[i]);
            var visual = new Grid { Width = 105, Height = 66 }; stack.Children.Add(visual);
            ringViews[i] = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            visual.Children.Add(ringViews[i]);
            var circle = new Grid { Width = 58, Height = 58 }; ringViews[i].Children.Add(circle);
            rings[i] = new QuotaRing { Width = 58, Height = 58, Tint = i == 0 ? mint : blue }; circle.Children.Add(rings[i]);
            numbers[i] = Text("—", 18, FontWeights.SemiBold); numbers[i].HorizontalAlignment = HorizontalAlignment.Center; numbers[i].VerticalAlignment = VerticalAlignment.Center; circle.Children.Add(numbers[i]);
            heatViews[i] = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            visual.Children.Add(heatViews[i]);
            heatNumbers[i] = Text("—", 15, FontWeights.SemiBold); heatNumbers[i].HorizontalAlignment = HorizontalAlignment.Center; heatViews[i].Children.Add(heatNumbers[i]);
            heatmaps[i] = new QuotaGrid { Width = 97, Height = 48, Tint = i == 0 ? mint : blue }; heatViews[i].Children.Add(heatmaps[i]);
            labels[i] = Text(i == 0 ? L("5 小时", "5 hours") : L("每周", "Weekly"), 10, FontWeights.Normal); labels[i].HorizontalAlignment = HorizontalAlignment.Center; labels[i].Foreground = new SolidColorBrush(Color.FromRgb(225, 232, 242)); stack.Children.Add(labels[i]);
            resets[i] = Text(L("重置时间未知", "Reset unknown"), 9, FontWeights.Normal); resets[i].HorizontalAlignment = HorizontalAlignment.Center; resets[i].Foreground = new SolidColorBrush(Color.FromRgb(190, 206, 225)); resets[i].Margin = new Thickness(0, 3, 0, 0); stack.Children.Add(resets[i]);
        }
        footer.Text = L("正在连接…", "Connecting…"); footer.FontSize = 9; footer.HorizontalAlignment = HorizontalAlignment.Center; footer.VerticalAlignment = VerticalAlignment.Bottom;
        footer.Foreground = new SolidColorBrush(Color.FromRgb(205, 216, 230)); Grid.SetRow(footer, 2); body.Children.Add(footer);
        MouseLeftButtonDown += async (s, e) => {
            if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2) { await RefreshQuota(); return; }
            try { DragMove(); SavePosition(); } catch { }
        };
        ContextMenu = BuildMenu();
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(Path.Combine(Program.Folder, "CodexQuota.ico"), 32, 32), Text = L("Codex 额度", "Codex Quota"), Visible = true };
        SetTrayMenu(); tray.DoubleClick += (s, e) => Dispatcher.Invoke(new Action(RestoreWindow));
        timer.Interval = TimeSpan.FromSeconds(1); timer.Tick += async (s, e) => { if (showRequest.WaitOne(0)) RestoreWindow(); UpdateFooter(); if (!busy && DateTime.UtcNow >= nextRefresh) await RefreshQuota(); };
        Loaded += async (s, e) => {
            try { StartupLink.Set(prefs.AutoStart); Program.Log("autostart", prefs.AutoStart ? "enabled" : "disabled"); } catch (Exception error) { Program.Log("autostart-error", error.Message); MessageBox.Show(this, "开机自启设置失败：" + error.Message, "Codex 额度"); }
            prefs.Save(); ApplyStyle(); LoadCache(); timer.Start(); await RefreshQuota();
        };
        Closing += (s, e) => { closing = true; timer.Stop(); SavePosition(); tray.Dispose(); client.Dispose(); };
    }
    static bool IsInsideButton(DependencyObject value) { while (value != null) { if (value is Button) return true; if (!(value is Visual)) return false; value = VisualTreeHelper.GetParent(value); } return false; }
    TextBlock Text(string value, double size, FontWeight weight) { return new TextBlock { Text = value, FontSize = size, FontWeight = weight }; }
    void SetTrayMenu()
    {
        var previous = tray.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(L("显示组件", "Show widget"), null, (s, e) => Dispatcher.Invoke(new Action(RestoreWindow)));
        menu.Items.Add(L("立即刷新", "Refresh now"), null, (s, e) => Dispatcher.Invoke(new Action(async () => await RefreshQuota())));
        menu.Items.Add(L("退出", "Exit"), null, (s, e) => Dispatcher.Invoke(new Action(Close)));
        tray.ContextMenuStrip = menu; if (previous != null) previous.Dispose();
    }
    void ApplyStyle()
    {
        bool grid = prefs.DisplayStyle == "grid";
        for (int i = 0; i < 2; i++) { ringViews[i].Visibility = grid ? Visibility.Collapsed : Visibility.Visible; heatViews[i].Visibility = grid ? Visibility.Visible : Visibility.Collapsed; }
    }
    void ApplyLanguage()
    {
        Title = L("Codex 剩余额度", "Codex Quota");
        refresh.ToolTip = L("立即刷新（也可双击卡片）", "Refresh now (or double-click the card)");
        ContextMenu = BuildMenu(); SetTrayMenu();
        RenderData(); UpdateFooter();
    }
    void ApplyGlass() { prefs.Glass = Math.Max(0.35, Math.Min(0.95, prefs.Glass)); card.Background = new SolidColorBrush(Color.FromArgb((byte)(255 * prefs.Glass), 28, 36, 49)); }
    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var update = new MenuItem { Header = L("立即刷新", "Refresh now") }; update.Click += async (s, e) => await RefreshQuota(); menu.Items.Add(update);
        var detail = new MenuItem { Header = L("额度与连接详情", "Quota and connection details") }; detail.Click += (s, e) => MessageBox.Show(this, Details(), L("Codex 额度详情", "Codex quota details")); menu.Items.Add(detail);
        menu.Items.Add(new Separator());
        var language = new MenuItem { Header = L("语言", "Language") }; menu.Items.Add(language);
        foreach (var option in new[] { new { Code = "zh", Name = "中文" }, new { Code = "en", Name = "English" } })
        {
            string code = option.Code;
            var item = new MenuItem { Header = option.Name, IsCheckable = true, IsChecked = prefs.Language == code };
            item.Click += (s, e) => { prefs.Language = code; prefs.Save(); ApplyLanguage(); };
            language.Items.Add(item);
        }
        var style = new MenuItem { Header = L("显示样式", "Display style") }; menu.Items.Add(style);
        foreach (var option in new[] { new { Code = "rings", Name = L("圆环", "Rings") }, new { Code = "grid", Name = L("方格", "Contribution grid") } })
        {
            string code = option.Code;
            var item = new MenuItem { Header = option.Name, IsCheckable = true, IsChecked = prefs.DisplayStyle == code };
            item.Click += (s, e) => { prefs.DisplayStyle = code; prefs.Save(); ApplyStyle(); ContextMenu = BuildMenu(); };
            style.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var startup = new MenuItem { Header = L("开机自启（登录 Windows 后）", "Start when Windows signs in"), IsCheckable = true, IsChecked = prefs.AutoStart };
        startup.Click += (s, e) => { try { StartupLink.Set(startup.IsChecked); prefs.AutoStart = startup.IsChecked; prefs.Save(); } catch (Exception err) { startup.IsChecked = prefs.AutoStart; MessageBox.Show(this, err.Message, L("无法修改自启", "Could not change startup")); } }; menu.Items.Add(startup);
        var pin = new MenuItem { Header = L("始终置顶", "Always on top"), IsCheckable = true, IsChecked = prefs.Topmost }; pin.Click += (s, e) => { Topmost = prefs.Topmost = pin.IsChecked; prefs.Save(); }; menu.Items.Add(pin);
        var glass = new MenuItem { Header = L("背景透明度", "Background opacity") }; menu.Items.Add(glass);
        foreach (double opacity in new double[] { 0.45, 0.66, 0.88 }) { double selected = opacity; var item = new MenuItem { Header = opacity == 0.45 ? L("更透明", "More transparent") : opacity == 0.66 ? L("均衡（默认）", "Balanced (default)") : L("更清晰", "More opaque") }; item.Click += (s, e) => { prefs.Glass = selected; ApplyGlass(); prefs.Save(); }; glass.Items.Add(item); }
        menu.Items.Add(new Separator());
        var hide = new MenuItem { Header = L("隐藏到托盘", "Hide to tray") }; hide.Click += (s, e) => Hide(); menu.Items.Add(hide);
        var exit = new MenuItem { Header = L("退出", "Exit") }; exit.Click += (s, e) => Close(); menu.Items.Add(exit);
        return menu;
    }
    void EnsureOnScreen()
    {
        bool visible = false;
        var source = PresentationSource.FromVisual(this); double sx = 1, sy = 1;
        if (source != null) { sx = source.CompositionTarget.TransformFromDevice.M11; sy = source.CompositionTarget.TransformFromDevice.M22; }
        foreach (var screen in Forms.Screen.AllScreens) { var a = screen.WorkingArea; if (new Rect(a.Left * sx, a.Top * sy, a.Width * sx, a.Height * sy).Contains(new Rect(Left, Top, Width, Height))) visible = true; }
        if (!visible) { var area = SystemParameters.WorkArea; Left = area.Right - Width - 20; Top = area.Top + 32; }
    }
    void SavePosition() { prefs.Left = Left; prefs.Top = Top; prefs.HasPosition = true; prefs.Save(); }
    void RestoreWindow() { Show(); WindowState = WindowState.Normal; EnsureOnScreen(); Activate(); Program.Status("window-restored", "再次打开已唤回窗口"); }
    string WindowDetails(int index)
    {
        var data = QuotaClient.Map(QuotaClient.Get(current, index == 0 ? "primary" : "secondary"));
        object reset = QuotaClient.Get(data, "resetsAt");
        return labels[index].Text + L("剩余 ", " remaining ") + numbers[index].Text + "\n" + L("重置时间：", "Resets: ") + (reset == null ? L("未知", "unknown") : DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(reset)).LocalDateTime.ToString("MM-dd HH:mm:ss"));
    }
    string Details() { return WindowDetails(0) + "\n\n" + WindowDetails(1) + "\n\n" + L("上次成功：", "Last success: ") + (lastSuccess == DateTime.MinValue ? L("尚无数据", "no data") : lastSuccess.ToLocalTime().ToString("MM-dd HH:mm:ss")) + (lastError == "" ? L("\n连接正常，每 60 秒刷新。", "\nConnected; refreshes every 60 seconds.") : "\n\n" + L("错误类型：", "Error type: ") + failureKind + "\n" + lastError + L("\n将自动重试；当前显示的是旧数据。", "\nRetrying automatically; displayed values are stale.")) + "\n\n" + L("开机自启：", "Start with Windows: ") + (File.Exists(StartupLink.FilePath) ? L("已启用", "enabled") : L("未启用", "disabled")); }
    void RenderData()
    {
        for (int i = 0; i < 2; i++)
        {
            var data = QuotaClient.Map(QuotaClient.Get(current, i == 0 ? "primary" : "secondary"));
            object used = QuotaClient.Get(data, "usedPercent");
            double? remaining = used == null ? (double?)null : Math.Max(0, Math.Min(100, 100 - Convert.ToDouble(used)));
            numbers[i].Text = remaining.HasValue ? remaining.Value.ToString("0.#") + "%" : "—";
            heatNumbers[i].Text = numbers[i].Text;
            rings[i].Value = remaining; rings[i].Tint = remaining.HasValue && remaining <= 10 ? amber : i == 0 ? mint : blue; rings[i].InvalidateVisual();
            heatmaps[i].Value = remaining; heatmaps[i].Tint = rings[i].Tint; heatmaps[i].InvalidateVisual();
            object minutes = QuotaClient.Get(data, "windowDurationMins");
            if (minutes != null) { int m = Convert.ToInt32(minutes); labels[i].Text = m == 10080 ? L("每周", "Weekly") : m % 60 == 0 ? (m / 60) + L(" 小时", " hours") : m + L(" 分钟", " minutes"); }
            else labels[i].Text = i == 0 ? L("短期", "Short term") : L("每周", "Weekly");
            object reset = QuotaClient.Get(data, "resetsAt");
            resets[i].Text = reset == null ? L("重置时间未知", "Reset unknown") : L("重置 ", "Reset ") + DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(reset)).LocalDateTime.ToString("MM/dd HH:mm");
            quotaPanels[i].ToolTip = WindowDetails(i);
        }
        tray.Text = L("Codex 剩余 ", "Codex left ") + numbers[0].Text + " / " + numbers[1].Text;
    }
    void LoadCache()
    {
        try {
            var cache = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Program.Folder, "quota-cache.json")));
            lastSuccess = DateTime.Parse(Convert.ToString(QuotaClient.Get(cache, "updated")), null, System.Globalization.DateTimeStyles.RoundtripKind);
            current = QuotaClient.Map(QuotaClient.Get(cache, "limits")); RenderData(); lastError = "cache"; failureKind = "cache"; UpdateFooter();
        } catch { }
    }
    void UpdateFooter()
    {
        dot.Background = new SolidColorBrush(lastError == "" && lastSuccess != DateTime.MinValue ? mint : amber);
        if (busy) footer.Text = L("正在更新…", "Updating…");
        else if (lastError != "") footer.Text = (lastSuccess == DateTime.MinValue ? L("连接失败", "Offline") : L("旧数据", "Stale")) + " · " + L("下次尝试 ", "Retry ") + nextRefresh.ToLocalTime().ToString("HH:mm:ss");
        else if (lastSuccess != DateTime.MinValue) footer.Text = L("下次数据更新 ", "Next update ") + nextRefresh.ToLocalTime().ToString("HH:mm:ss");
        else footer.Text = L("正在连接…", "Connecting…");
        footer.ToolTip = Details();
    }
    async Task RefreshQuota()
    {
        if (busy || closing) return;
        busy = true; refresh.IsEnabled = false; UpdateFooter();
        try {
            var data = await Task.Run(() => client.Read()); if (closing) return;
            current = data; lastSuccess = DateTime.UtcNow; lastError = ""; failureKind = ""; failures = 0;
            nextRefresh = DateTime.UtcNow.AddSeconds(60); RenderData();
            try { File.WriteAllText(Path.Combine(Program.Folder, "quota-cache.json"), new JavaScriptSerializer().Serialize(new { updated = lastSuccess.ToString("o"), limits = data })); } catch { }
            Program.Status("updated", new { primary = numbers[0].Text, secondary = numbers[1].Text, nextRefresh = nextRefresh.ToString("o"), autoStart = File.Exists(StartupLink.FilePath) });
            Program.Log("updated", numbers[0].Text + " / " + numbers[1].Text);
        }
        catch (Exception error) {
            if (closing) return;
            var failure = error as QuotaFailure ?? QuotaClient.Classify(error.Message);
            lastError = failure.Message; failureKind = failure.Kind; failures++;
            nextRefresh = DateTime.UtcNow.AddSeconds(QuotaClient.RetrySeconds(failures, failureKind));
            tray.Text = L("Codex 额度：连接失败，正在自动重试", "Codex quota: offline, retrying");
            Program.Log(failureKind, lastError);
            Program.Status("refresh-error", new { kind = failureKind, error = lastError, nextRetry = nextRefresh.ToString("o"), lastSuccess = lastSuccess.ToString("o") });
        }
        finally { busy = false; if (!closing) { refresh.IsEnabled = true; UpdateFooter(); } }
    }
}
