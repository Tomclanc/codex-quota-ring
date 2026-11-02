using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexQuotaRing;
public record Limit(double Remaining, long Reset);
public record Quota(Limit? Week, Limit? FiveHour);
public static class QuotaClient
{
    public static Quota Parse(JsonElement result)
    {
        JsonElement bucket;
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object && buckets.TryGetProperty("codex", out bucket)) { }
        else if (!result.TryGetProperty("rateLimits", out bucket)) return new(null, null);
        Limit? week = null, five = null;
        foreach (string name in new[] { "primary", "secondary" })
        {
            if (bucket.ValueKind != JsonValueKind.Object || !bucket.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) continue;
            if (!w.TryGetProperty("usedPercent", out var u) || u.ValueKind != JsonValueKind.Number || !u.TryGetDouble(out var used) || !double.IsFinite(used)) continue;
            if (!w.TryGetProperty("windowDurationMins", out var d) || d.ValueKind != JsonValueKind.Number || !d.TryGetInt32(out int duration)) continue;
            long reset = w.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out var epoch) ? epoch : 0;
            var limit = new Limit(Math.Clamp(100 - used, 0, 100), reset);
            if (duration == 10080) week = limit;
            if (duration == 300) five = limit;
        }
        return new(week, five);
    }
    public static string FindCli()
    {
        var custom = Environment.GetEnvironmentVariable("CODEX_BINARY");
        if (File.Exists(custom)) return custom!;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(root))
        {
            var exe = Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (exe != null) return exe;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            var path = Path.Combine(dir.Trim('"'), "codex.exe");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("未找到 Codex CLI");
    }
    public static async Task<Quota> Read(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var p = new Process { StartInfo = new(FindCli(), "app-server") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        p.Start();
        p.ErrorDataReceived += (_, _) => { }; p.BeginErrorReadLine();
        try
        {
            async Task<JsonElement> Request(int id, string method, object? args = null)
            {
                await p.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, method, @params = args }));
                await p.StandardInput.FlushAsync(timeout.Token);
                while (true)
                {
                    string? line = await p.StandardOutput.ReadLineAsync(timeout.Token);
                    if (line == null) throw new IOException("Codex 接口已关闭");
                    using var doc = JsonDocument.Parse(line);
                    var msg = doc.RootElement;
                    if (!msg.TryGetProperty("id", out var rid) || !rid.TryGetInt32(out var n) || n != id) continue;
                    if (msg.TryGetProperty("error", out _)) throw new IOException("额度接口暂不可用，请检查 Codex 登录状态");
                    return msg.GetProperty("result").Clone();
                }
            }
            await Request(1, "initialize", new { clientInfo = new { name = "codex-quota-ring", version = "1.0.0" } });
            await p.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            return Parse(await Request(2, "account/rateLimits/read"));
        }
        finally { if (!p.HasExited) p.Kill(true); await p.WaitForExitAsync(); }
    }
}
