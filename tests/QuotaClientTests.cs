using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
static class QuotaClientTests
{
    static int count;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
    static void Main()
    {
        Check(QuotaClient.Classify("error sending request: TLS connection reset").Kind == "network", "network errors must not ask user to log in");
        Check(QuotaClient.Classify("failed to fetch codex rate limits: error sending request for url (https://chatgpt.com/backend-api/wham/usage) [RPC -32603]").Kind == "network", "API name rate limits must not imply HTTP 429");
        Check(QuotaClient.Classify("HTTP 401 Unauthorized").Kind == "auth", "401 requires token refresh");
        Check(QuotaClient.Classify("HTTP 403 Forbidden").Kind == "forbidden", "403 is distinct from token expiry");
        Check(QuotaClient.Classify("429 Too Many Requests").Kind == "throttled", "429 must back off");
        Check(QuotaClient.RetrySeconds(1,"network") == 15 && QuotaClient.RetrySeconds(2,"network") == 30 && QuotaClient.RetrySeconds(3,"network") == 60, "network retry schedule");
        Check(QuotaClient.RetrySeconds(100,"network") == 300, "network retry maximum");
        Check(QuotaClient.RetrySeconds(1,"throttled") >= 120, "rate limit backoff");
        Check(QuotaClient.RetrySeconds(1,"auth") == 300, "avoid repeated token refresh attempts");
        var json = new JavaScriptSerializer();
        var response = json.Deserialize<Dictionary<string,object>>("{\"rateLimits\":{\"primary\":{\"usedPercent\":99}},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":0}}}}");
        var primary = QuotaClient.Map(QuotaClient.Get(QuotaClient.SelectLimit(response),"primary"));
        Check(Convert.ToInt32(QuotaClient.Get(primary,"usedPercent")) == 0, "prefer codex bucket and preserve zero usage");
        var missing = json.Deserialize<Dictionary<string,object>>("{\"rateLimits\":{\"primary\":null}}");
        Check(QuotaClient.Get(QuotaClient.SelectLimit(missing),"primary") == null, "missing is unknown, not zero");
        Check(!QuotaClient.Safe("Bearer secret-value https://example.test?token=private").Contains("secret-value") && !QuotaClient.Safe("https://example.test?token=private").Contains("private"), "redact credentials and URLs in diagnostics");
        Console.WriteLine(count + " checks passed");
    }
}
