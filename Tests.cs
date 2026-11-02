using System.IO;
using System.Text.Json;
namespace CodexQuotaRing;
static class Tests
{
    public static void Run()
    {
        Quota Parse(string s) { using var d = JsonDocument.Parse(s); return QuotaClient.Parse(d.RootElement); }
        void Check(bool ok) { if (!ok) throw new Exception("Quota parser test failed"); }
        var a = Parse("""{"rateLimits":{"primary":{"usedPercent":18,"windowDurationMins":300},"secondary":{"usedPercent":67,"windowDurationMins":10080}}}""");
        Check(a.Week?.Remaining == 33 && a.FiveHour?.Remaining == 82);
        var b = Parse("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":100,"windowDurationMins":10080},"secondary":null},"spark":{"primary":{"usedPercent":3,"windowDurationMins":300}}}}""");
        Check(b.Week?.Remaining == 0 && b.FiveHour == null);
        var c = Parse("""{"rateLimits":{"primary":{"usedPercent":30,"windowDurationMins":15}}}""");
        Check(c.Week == null && c.FiveHour == null);
        var d = Parse("""{"rateLimits":{"primary":{"usedPercent":null,"windowDurationMins":300}}}""");
        Check(d.FiveHour == null);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"tests.txt"),"PASS: swapped/legacy windows, multi-bucket selection, missing windows, unknown durations, null usage.");
    }
}
