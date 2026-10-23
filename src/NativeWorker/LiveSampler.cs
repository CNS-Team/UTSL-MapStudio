using System.Diagnostics;
using System.Text.Json;
using Terraria.WorldBuilding;
using ThreeWorlds.Live;
using UnifierTSL.Servers;

internal sealed record WorkerRequest(string Style = "classic", bool LivePreview = true, int PreviewIntervalMs = 1000, bool ProbeOnly = false, int Evil = 0, bool FloodProbe = false,
    string[]? NativeSeeds = null, string[]? SeedTraits = null, bool SeedTests = false);

internal sealed class LiveSampler : IDisposable
{
    readonly ServerContext world;
    readonly object gate = new();
    readonly Timer timer;
    readonly bool enabled;
    readonly Stopwatch elapsed = Stopwatch.StartNew();
    GenerationProgress? progress;
    string pass = "初始化", phase = "bootstrap";
    long sequence, frames;
    double sampleTotal, sampleMax;
    int warnings;
    bool stopped;
    internal LiveSampler(ServerContext world, WorkerRequest request)
    {
        this.world = world; enabled = request.LivePreview;
        int interval = Math.Clamp(request.PreviewIntervalMs, 500, 5000);
        timer = new Timer(_ => Capture(), null, 250, interval);
    }
    public void Enter(string name, GenerationProgress p) { pass = name; progress = p; phase = "native-generation"; }
    public void Capture()
    {
        if (!Monitor.TryEnter(gate)) return;
        try { if (!stopped) CaptureCore(false); }
        finally { Monitor.Exit(gate); }
    }
    public void Finish()
    {
        timer.Change(Timeout.Infinite, Timeout.Infinite);
        lock (gate)
        {
            if (stopped) return;
            phase = "native-saved"; pass = "原生生成完成"; CaptureCore(true); stopped = true;
            File.WriteAllText("preview-summary.json", JsonSerializer.Serialize(new
            {
                frames, warnings, elapsedSeconds = elapsed.Elapsed.TotalSeconds,
                sampleTotalMilliseconds = sampleTotal, sampleMaxMilliseconds = sampleMax,
                source = "actual-native-world-tiles-downsampled", coherentSnapshot = false,
                notZeroOverhead = true, networkStarted = false, pluginsLoaded = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    void CaptureCore(bool final)
    {
        var watch = Stopwatch.StartNew(); sequence++;
        int fw = 0, fh = 0;
        string? warning = null;
        try
        {
            var m = world.Main;
            int width = m.maxTilesX, height = m.maxTilesY;
            if (enabled && width >= 1 && height >= 1)
            {
                int scale = Math.Max(1, Math.Max((width + 1599) / 1600, (height + 799) / 800));
                fw = (width + scale - 1) / scale; fh = (height + scale - 1) / scale;
                var pixels = new int[fw * fh];
                for (int py = 0; py < fh; py++) for (int px = 0; px < fw; px++)
                {
                    int x = Math.Min(px * scale, width - 1), y = Math.Min(py * scale, height - 1);
                    var t = m.tile[x, y];
                    int rgb = y < m.worldSurface || m.worldSurface < 1 ? 0x12243C : y < m.rockLayer ? 0x252029 : 0x15161D;
                    if (t.liquid > 0) rgb = t.lava() ? 0xF26839 : t.honey() ? 0xD9A739 : t.shimmer() ? 0xBA83E5 : 0x3C8BC5;
                    else if (t.active()) rgb = TileRgb(t.type);
                    else if (t.wall > 0) rgb = 0x383039;
                    pixels[py * fw + px] = unchecked((int)0xFF000000) | rgb;
                }
                var frame = new LiveFrame(sequence, width, height, fw, fh, pixels);
                LiveProtocol.WriteFrame("live-frame.bin", frame); frames++;
                // Only eight real sampled frames retained, not an unbounded raw recording.
                if (frames <= 8) LiveProtocol.WriteFrame($"preview-sample-{frames:D2}.bin", frame);
            }
        }
        catch (Exception e) { warnings++; warning = e.GetType().Name + ": " + e.Message; }
        watch.Stop(); sampleTotal += watch.Elapsed.TotalMilliseconds; sampleMax = Math.Max(sampleMax, watch.Elapsed.TotalMilliseconds);
        double p = progress?.TotalProgress ?? 0, part = progress?.Value ?? 0;
        if (!double.IsFinite(p)) p = 0; if (!double.IsFinite(part)) part = 0;
        var status = new LiveStatus(sequence, DateTimeOffset.Now, phase, pass, progress?.Message ?? "初始化原生世界…",
            final ? 1 : Math.Clamp(p, 0, 1), final ? 1 : Math.Clamp(part, 0, 1), world.Main.maxTilesX, world.Main.maxTilesY,
            fw, fh, watch.Elapsed.TotalMilliseconds, frames, "actual-native-world-tiles-downsampled", final, warning);
        try
        {
            LiveProtocol.WriteStatus("live-status.json", status);
            File.AppendAllText("preview-timeline.jsonl", JsonSerializer.Serialize(status) + Environment.NewLine);
        }
        catch (IOException) { warnings++; } // preview telemetry cannot fail generation
    }
    static int TileRgb(int type) => type switch
    {
        0 => 0x93684B, 1 => 0x737D8D, 2 or 60 => 0x71AF5C, 23 or 25 => 0x9873C4,
        53 or 397 => 0xE4CE8B, 59 => 0x80624E, 70 => 0x6478C2,
        109 or 116 or 117 => 0xB5C0EB, 147 or 161 => 0xC0DFF3,
        189 or 196 => 0xE5EAF1, 199 or 200 or 203 => 0xC86E79,
        396 => 0xC09E70, 30 => 0x97644D, 57 or 58 => 0x815458,
        7 => 0xB7956B, 6 => 0xC78960, 8 => 0xD5DDE3, 9 => 0xDDC963,
        21 or 467 or 88 => 0xECC96E, 191 or 192 => 0x739290,
        _ => 0x768879
    };
    public void Dispose() { timer.Dispose(); lock (gate) stopped = true; }
}
