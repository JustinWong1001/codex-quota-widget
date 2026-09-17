using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

public sealed class QuotaFailure : Exception
{
    public readonly string Kind;
    public QuotaFailure(string kind, string message) : base(message) { Kind = kind; }
}

public sealed class QuotaClient : IDisposable
{
    Process server;
    int nextId;
    string stderrHint = "";
    readonly string folder;
    public QuotaClient(string folder) { this.folder = folder; }
    public static object Get(Dictionary<string, object> d, string key) { object value; return d != null && d.TryGetValue(key, out value) ? value : null; }
    public static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }
    public static string Safe(string message)
    {
        string value = Regex.Replace(message ?? "未知错误", @"(?i)Bearer\s+\S+|eyJ[A-Za-z0-9_.-]+|sk-[A-Za-z0-9_-]+", "[已隐藏]");
        value = Regex.Replace(value, @"https?://\S+", "[服务地址]");
        return value.Length > 400 ? value.Substring(0, 400) : value;
    }
    public static QuotaFailure Classify(string message)
    {
        string lower = (message ?? "").ToLowerInvariant();
        string kind = "service";
        if (Regex.IsMatch(lower, @"\b401\b|unauthoriz|not logged|login required|refresh_token|token.*expired|authentication")) kind = "auth";
        else if (Regex.IsMatch(lower, @"\b429\b|too many requests|rate.?limit(?:s)? (?:exceeded|reached)|rate_limit_exceeded")) kind = "throttled";
        else if (Regex.IsMatch(lower, @"\b403\b|forbidden")) kind = "forbidden";
        else if (Regex.IsMatch(lower, @"timeout|timed out|network|connect|dns|tls|ssl|error sending request|连接|超时|网络")) kind = "network";
        else if (Regex.IsMatch(lower, @"access.*denied|permission|拒绝访问")) kind = "permission";
        return new QuotaFailure(kind, Safe(message));
    }
    public static int RetrySeconds(int failures, string kind)
    {
        if (kind == "auth" || kind == "forbidden") return 300;
        if (kind == "throttled") return Math.Min(600, 120 * Math.Max(1, failures));
        return (int)Math.Min(300, 15 * Math.Pow(2, Math.Min(5, Math.Max(0, failures - 1))));
    }
    public static Dictionary<string, object> SelectLimit(Dictionary<string, object> result)
    {
        var buckets = Map(Get(result, "rateLimitsByLimitId"));
        var limits = Map(Get(buckets, "codex"));
        if (limits == null) limits = Map(Get(result, "rateLimits"));
        if (limits == null) throw new QuotaFailure("account", "该账号暂未提供 Codex 额度。请使用 ChatGPT 登录 Codex CLI。");
        return limits;
    }
    static string Locate()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"npm\node_modules\@openai\codex");
        if (Directory.Exists(root)) { string[] files = Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories); if (files.Length > 0) return files[0]; }
        foreach (string part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        { if (String.IsNullOrWhiteSpace(part)) continue; string file = Path.Combine(part.Trim('"'), "codex.exe"); if (File.Exists(file)) return file; }
        throw new QuotaFailure("install", "未找到 Codex CLI，请安装或更新 Codex CLI。");
    }
    static void ApplySystemProxy(ProcessStartInfo info)
    {
        // Respect explicit proxy environment variables. Otherwise use the current
        // Windows HTTP proxy, including one enabled after the widget was started.
        if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("HTTPS_PROXY")) || !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALL_PROXY"))) return;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
        {
            if (key == null || Convert.ToInt32(key.GetValue("ProxyEnable", 0)) != 1) return;
            string raw = Convert.ToString(key.GetValue("ProxyServer", ""));
            string http = null, https = null;
            foreach (string part in raw.Split(';'))
            {
                var pair = part.Split(new char[] { '=' }, 2);
                if (pair.Length == 1) http = https = part;
                else if (pair[0].Trim().Equals("http", StringComparison.OrdinalIgnoreCase)) http = pair[1];
                else if (pair[0].Trim().Equals("https", StringComparison.OrdinalIgnoreCase)) https = pair[1];
            }
            if (https == null) https = http;
            if (!String.IsNullOrWhiteSpace(https)) info.EnvironmentVariables["HTTPS_PROXY"] = https.Contains("://") ? https : "http://" + https.Trim();
            if (!String.IsNullOrWhiteSpace(http)) info.EnvironmentVariables["HTTP_PROXY"] = http.Contains("://") ? http : "http://" + http.Trim();
        }
    }
    void Start()
    {
        if (server != null && !server.HasExited) return;
        CloseServer();
        string state = Path.Combine(folder, "runtime"); Directory.CreateDirectory(state);
        string args = "-c \"sqlite_home=\\\"" + state.Replace('\\', '/') + "\\\"\" app-server";
        var info = new ProcessStartInfo(Locate(), args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder };
        ApplySystemProxy(info);
        stderrHint = "";
        server = new Process { StartInfo = info };
        server.ErrorDataReceived += (s, e) => {
            if (e.Data == null) return;
            if (e.Data.Contains("拒绝访问") || e.Data.ToLowerInvariant().Contains("access is denied")) stderrHint = "Codex 本地文件访问被拒绝。";
            else if (e.Data.Contains("failed to initialize")) stderrHint = "Codex 本地运行状态初始化失败。";
        };
        server.Start(); server.BeginErrorReadLine();
        Request("initialize", new { clientInfo = new { name = "codex_quota_widget", version = "3.0.0" } });
        server.StandardInput.WriteLine("{\"method\":\"initialized\"}"); server.StandardInput.Flush();
    }
    Dictionary<string, object> Request(string method, object parameters)
    {
        int id = ++nextId;
        var json = new JavaScriptSerializer();
        server.StandardInput.WriteLine(json.Serialize(new { id = id, method = method, @params = parameters })); server.StandardInput.Flush();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var read = server.StandardOutput.ReadLineAsync();
            int wait = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
            if (!read.Wait(wait)) throw new QuotaFailure("network", "额度查询超过 30 秒，将自动重连。");
            if (read.Result == null) throw Classify(String.IsNullOrEmpty(stderrHint) ? "Codex 查询连接中断，将重新连接。" : stderrHint);
            var message = json.Deserialize<Dictionary<string, object>>(read.Result);
            if (Convert.ToString(Get(message, "id")) != id.ToString()) continue;
            var error = Map(Get(message, "error"));
            if (error != null) throw Classify(Convert.ToString(Get(error, "message")) + " [RPC " + Convert.ToString(Get(error, "code")) + "]");
            return Map(Get(message, "result"));
        }
        throw new QuotaFailure("network", "额度查询超时，将自动重连。");
    }
    public Dictionary<string, object> Read()
    {
        try
        {
            Start();
            try { return SelectLimit(Request("account/rateLimits/read", null)); }
            catch (QuotaFailure error)
            {
                if (error.Kind != "auth") throw;
                // Let the official CLI refresh its managed login only after an
                // authentication failure; never copy or handle tokens ourselves.
                Request("account/read", new { refreshToken = true });
                return SelectLimit(Request("account/rateLimits/read", null));
            }
        }
        catch { CloseServer(); throw; }
    }
    void CloseServer()
    {
        var old = server; server = null;
        if (old == null) return;
        try { if (!old.HasExited) { old.StandardInput.Close(); if (!old.WaitForExit(1200)) { old.Kill(); old.WaitForExit(1200); } } } catch { }
        old.Dispose();
    }
    public void Dispose() { CloseServer(); }
}
