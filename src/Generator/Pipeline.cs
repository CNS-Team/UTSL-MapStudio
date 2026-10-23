using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ThreeWorlds;

public sealed record GenerationRecord(Options Options, Artifact Source, int SourceOffset, SliceResult[] Worlds,
    SeamResult[] Seams, string Status, string PlayerE2E, DateTimeOffset CompletedAt);

internal sealed partial class Pipeline(Action<string> log)
{
    public Action<string, double>? Stage { get; init; }
    public Action<string, bool>? PreviewSource { get; init; }
    static readonly SemaphoreSlim Gate = new(1, 1);
    public async Task RunAsync(Options o, CancellationToken ct)
    {
        o.Validate();
        if (o.WorldLayout == "normal-stitch") { await RunIndependentAsync(o, ct); return; }
        await Gate.WaitAsync(ct);
        bool ownsOutput = false;
        string output = Path.GetFullPath(o.Output);
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("拒绝覆盖非空目录，请选择一个新的输出目录。");
            Directory.CreateDirectory(output); ownsOutput = true;
            o.Output = output;
            Files.Write(Path.Combine(output, "options.json"), o);
            File.WriteAllText(Path.Combine(output, "INCOMPLETE.txt"), "生成未完成，勿用于部署。\n");
            WorldIO.Initialize();
            Stage?.Invoke("初始化离线引擎", 0);
            string source;
            if (o.Source is null)
            {
                log($"原生生成连续母图 {o.SourceWidth}×{o.Height}，难度={o.Difficulty}，种子={o.Seed}；可能需要较长时间。");
                var work = Path.Combine(output, "native-source");
                Stage?.Invoke("原生生成连续母图（下方为真实图格预览）", 0.03);
                await RunWorker(work, o.SourceWidth, o.Height, o.Seed, o.Difficulty, null, ct, o);
                source = Path.Combine(work, "Worlds", "SeamSource.wld");
            }
            else
            {
                // Own a stable copy. Never write, lock, or trim the user's original world.
                source = Path.Combine(output, "source.wld");
                File.Copy(Path.GetFullPath(o.Source), source, false);
                if (Files.Inspect(o.Source).Sha256 != Files.Inspect(source).Sha256)
                    throw new IOException("复制期间源地图发生变化，请在地图离线/静止时导入。");
            }
            ct.ThrowIfCancellationRequested();
            var w = WorldIO.Load(source);
            Stage?.Invoke("母图已完成，检查尺寸/难度", 0.72);
            if (w.TilesWide < o.SourceWidth || w.TilesHigh != o.Height || w.GameMode != o.Difficulty)
                throw new InvalidDataException($"母图实际 {w.TilesWide}×{w.TilesHigh}/难度{w.GameMode}，需要至少 {o.SourceWidth}×{o.Height}/难度{o.Difficulty}；禁止拉伸或偷偷改难度。");
            int offset = (w.TilesWide - o.SourceWidth) / 2;
            var retained = new System.Drawing.Rectangle(offset, 0, o.SourceWidth, o.Height);
            var before = FoundationWorlds.Scan(w, retained);
            bool applies = o.Foundations.Applies(w);
            string[] missing = applies ? before.Where(s => o.Foundations.Features.Contains(s.Id) && !s.Present).Select(s => s.Id).ToArray() : [];
            var patches = new List<FoundationPatch>();
            if (missing.Length != 0)
            {
                w.Tiles = null!; WorldIO.Collect();
                Stage?.Invoke("生成原生基础环境素材（不改接缝）", .73);
                int donorWidth = o.Height == 2400 ? 8400 : o.Height == 1500 ? 6400 : 4200;
                string donorSeed = (BitConverter.ToUInt32(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(o.Seed + "|foundations")), 0) & 0x7fffffff).ToString();
                var donors = new List<string>();
                foreach (int evil in missing.Contains("crimson") && missing.Any(id => id != "crimson") ? new[] { 1, 2 } : new[] { missing.Contains("crimson") ? 2 : 1 })
                {
                    var donorWork = Path.Combine(output, "native-foundations-" + evil);
                    log($"原生素材：标准世界 {donorWidth}×{o.Height}，evil={evil}；保留真实建筑和原生战利品，不跳过进度。");
                    await RunWorker(donorWork, donorWidth, o.Height, donorSeed, o.Difficulty, null, ct,
                        new Options { Style = "classic", LivePreview = o.LivePreview, PreviewIntervalMs = o.PreviewIntervalMs }, evil);
                    donors.Add(Path.Combine(donorWork, "Worlds", "SeamSource.wld"));
                }
                w = WorldIO.Load(source);
                foreach (string donor in donors)
                {
                    missing = FoundationWorlds.Missing(w, o.Foundations, retained);
                    if (missing.Length == 0) break;
                    patches.AddRange(FoundationWorlds.AddFromDonor(w, donor, o, missing, log, ct, offset, patches));
                }
                missing = FoundationWorlds.Missing(w, o.Foundations, retained);
                if (missing.Length != 0) throw new InvalidDataException("原生素材补全后仍缺少基础环境：" + string.Join(",", missing));
                source = Path.Combine(output, "foundation-source.wld"); WorldIO.Save(w, source);
                string metadata = WorldIO.MetadataDigest(w); w.Tiles = null!; WorldIO.Collect(); w = WorldIO.Load(source);
                if (metadata != WorldIO.MetadataDigest(w)) throw new InvalidDataException("基础环境对象元数据保存不一致。");
                foreach (var patch in patches) if (FoundationWorlds.RegionDigest(w, patch.Destination) != patch.TileDigest)
                    throw new InvalidDataException("基础环境落盘后与原生素材不一致。");
                if (FoundationWorlds.Missing(w, o.Foundations, retained).Length != 0) throw new InvalidDataException("基础环境落盘后验收失败。");
            }
            Files.Write(Path.Combine(output, "foundation-result.json"), new { policy = o.Foundations, applies, before, after = FoundationWorlds.Scan(w, retained), patches,
                scope = "whole-three-world-chain, not each slice; tile/altar/loot preservation, NOT boss/game E2E; no online digging/liquid synchronization", productionTouched = false });
            Stage?.Invoke("整形两处接缝同Y通道（不改出生点）", .77);
            var prepared = SeamPreparation.Prepare(w, o, offset);
            source = Path.Combine(output, "prepared-source.wld"); WorldIO.Save(w, source);
            Files.Write(Path.Combine(output, "seam-passage-result.json"), new { prepared, policy = "8-column shared geological profile; object rows preserved; obstructed bodies explicitly counted (user accepts clipping); exact-Y + 1-2 tile inset; no online carving", productionTouched = false });
            PreviewSource?.Invoke(Path.Combine(output, "native-source"), true);
            var sourceInfo = Files.Inspect(source);
            w.Tiles = null!; WorldIO.Collect();
            var specs = o.Specs(offset); var results = new List<SliceResult>();
            string worlds = Path.Combine(output, "Worlds"); Directory.CreateDirectory(worlds);
            foreach (var spec in specs)
            {
                Stage?.Invoke("切分并验证：" + spec.Name, 0.74 + results.Count * 0.04);
                ct.ThrowIfCancellationRequested(); log($"切分并重读 {spec.Name}：母图 x={spec.SourceX}..{spec.SourceX + spec.Width - 1}");
                results.Add(WorldIO.Slice(source, sourceInfo.Sha256, spec, o, Path.Combine(worlds, spec.Name + ".wld")));
            }
            WriteConfigs(output, o, offset);
            Stage?.Invoke("逐格验证接缝", 0.87);
            log("逐格校验两处接缝（包括所有深度、液体/墙/线/斜坡/涂漆/隐形/发光字段）……");
            var seams = new[] { WorldIO.Compare(results[0].File.Path, results[1].File.Path, o.Overlap),
                WorldIO.Compare(results[1].File.Path, results[2].File.Path, o.Overlap) };
            Files.Write(Path.Combine(output, "seam-checks.json"), seams);
            if (seams.Any(s => s.Mismatches != 0 || s.LeftDigest != s.RightDigest))
                throw new InvalidDataException("接缝有不一致图格，输出禁止部署，详情见 seam-checks.json。");
            foreach (var result in results)
            {
                Stage?.Invoke("原生离线读图：" + result.Spec.Name, 0.90 + Array.IndexOf(specs, result.Spec) * 0.02);
                ct.ThrowIfCancellationRequested(); log("UTSL 实际原生读取验证（网络启动前退出）：" + result.Spec.Name);
                await RunWorker(Path.Combine(output, "native-load-" + result.Spec.Role), o.Width, o.Height, o.Seed, o.Difficulty, result.File.Path, ct);
                using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "native-load-" + result.Spec.Role, "native-result.json")));
                if (native.RootElement.GetProperty("spawnX").GetInt32() != result.SpawnX || native.RootElement.GetProperty("spawnY").GetInt32() != result.SpawnY)
                    throw new InvalidDataException("UTSL原生读取后的出生点与落盘记录不一致。");
            }
            ct.ThrowIfCancellationRequested(); log("生成三联预览与接缝局部图……");
            Stage?.Invoke("生成最终预览与完整校验", 0.97);
            Preview.Render(output, results.ToArray(), o);
            if (Files.Inspect(source).Sha256 != sourceInfo.Sha256) throw new IOException("工作母图被意外改写。");
            var record = new GenerationRecord(o, sourceInfo, offset, results.ToArray(), seams,
                "LOCAL_GENERATION_AND_NATIVE_LOAD_VERIFIED_NOT_DEPLOYED", "UNVERIFIED", DateTimeOffset.Now);
            Files.Write(Path.Combine(output, "generation-result.json"), record);
            VerifyExisting(output, log);
            var artifacts = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith("INCOMPLETE.txt", StringComparison.OrdinalIgnoreCase)).Order().Select(p => new
                { path = Path.GetRelativePath(output, p), file = Files.Inspect(p) }).ToArray();
            Files.Write(Path.Combine(output, "artifact-manifest.json"), artifacts);
            File.Delete(Path.Combine(output, "INCOMPLETE.txt"));
            Files.Write(Path.Combine(output, "COMPLETE.json"), new { record.Status, record.PlayerE2E, result = Files.Inspect(Path.Combine(output, "generation-result.json")), productionTouched = false });
            log("完成：" + output + "（本地生成/静态接缝/原生读图通过；未上装，真人切换和渲染未验证）");
            Stage?.Invoke("本地生成与验证完成（未部署）", 1);
        }
        catch (Exception ex)
        {
            if (ownsOutput) Files.Write(Path.Combine(output, "FAILED.json"), new { status = "FAILED_NOT_FOR_DEPLOYMENT", error = ex.ToString(), at = DateTimeOffset.Now });
            throw;
        }
        finally { Gate.Release(); }
    }
    async Task RunWorker(string work, int width, int height, string seed, int difficulty, string? load, CancellationToken ct, Options? options = null, int evil = 0)
    {
        string request = work + ".request.json";
        Directory.CreateDirectory(Path.GetDirectoryName(work)!);
        Files.Write(request, new { Style = options?.Style ?? "classic", LivePreview = options?.LivePreview ?? false,
            PreviewIntervalMs = options?.PreviewIntervalMs ?? 1000, Evil = evil, NativeSeeds = options?.NativeSeeds ?? [], SeedTraits = options?.SeedTraits ?? [] });
        if (load == null) PreviewSource?.Invoke(work, width > 8400);
        var psi = new ProcessStartInfo(Path.Combine(Program.ToolRoot, "worker", "UTSL.WorldGen.Worker.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var a in new[] { Path.Combine(Program.ToolRoot, "engine"), work, width.ToString(), height.ToString(), seed, difficulty.ToString() }) psi.ArgumentList.Add(a);
        if (load != null) psi.ArgumentList.Add(load);
        psi.ArgumentList.Add("--request=" + request);
        using var process = Process.Start(psi) ?? throw new IOException("原生生成进程无法创建。");
        log($"本地worker PID={process.Id}；不启动游戏监听、不加载插件。");
        Directory.CreateDirectory(Path.GetDirectoryName(work)!);
        using var stdout = new StreamWriter(work + ".stdout.log", false, Encoding.UTF8) { AutoFlush = true };
        using var stderr = new StreamWriter(work + ".stderr.log", false, Encoding.UTF8) { AutoFlush = true };
        async Task Drain(StreamReader reader, StreamWriter writer)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                await writer.WriteLineAsync(line);
                if (line.StartsWith("PROGRESS ") || line.StartsWith("NATIVE_WORLD_SAVED")) log(line);
            }
        }
        var outTask = Drain(process.StandardOutput, stdout); var errTask = Drain(process.StandardError, stderr);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(60));
        try
        {
            string last = "";
            var wait = process.WaitForExitAsync(timeout.Token);
            while (!process.HasExited)
            {
                if (await Task.WhenAny(wait, Task.Delay(5000, timeout.Token)) == wait) { await wait; break; }
                timeout.Token.ThrowIfCancellationRequested();
                string progress = Path.Combine(work, "progress.txt");
                if (File.Exists(progress))
                {
                    try { string text = File.ReadAllText(progress); if (text != last) { log(text); last = text; } }
                    catch (IOException) { /* brief writer collision, next poll retains the evidence */ }
                }
            }
        }
        catch
        {
            // Only this locally owned worker PID, never a process name/tree or production process.
            if (!process.HasExited) process.Kill(entireProcessTree: false);
            await process.WaitForExitAsync(); await Task.WhenAll(outTask, errTask); throw;
        }
        await Task.WhenAll(outTask, errTask);
        if (process.ExitCode != 0) throw new IOException($"原生worker退出{process.ExitCode}，详见 {work}.stderr.log");
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(work, "native-result.json")));
        var r = result.RootElement;
        if (!r.GetProperty("completed").GetBoolean() || r.GetProperty("width").GetInt32() != width ||
            r.GetProperty("height").GetInt32() != height || r.GetProperty("difficulty").GetInt32() != difficulty ||
            r.GetProperty("networkStarted").GetBoolean() || r.GetProperty("pluginsLoaded").GetBoolean())
            throw new InvalidDataException("原生尺寸/难度/离线边界验证不一致。");
    }
    public static void WriteConfigs(string output, Options o, int offset)
    {
        string dir = Path.Combine(output, "config-fragments"); Directory.CreateDirectory(dir);
        var specs = o.Specs(offset);
        Files.Write(Path.Combine(dir, "launcher.fragment.json"), new
        {
            launcher = new { joinServer = "first", autoStartServers = new[] { specs[1], specs[0], specs[2] }
                .Select(s => new { name = s.Name, worldName = s.Name, seed = o.Seed,
                    difficulty = new[] { "normal", "expert", "master", "creative" }[o.Difficulty],
                    size = o.Width == 8400 ? "large" : o.Width == 6400 ? "medium" : "small", evil = "random" }).ToArray() },
            _notice = "仅launcher字段片段，不是完整生产配置；先放置本批三张Worlds/同名.wld，再合并现有配置。缺图可能触发UTSL重新生成独立地图，因此绝不能缺图直接启动。"
        });
        var edges = new Dictionary<string, object>();
        edges[o.Name + "·西"] = new Dictionary<string, int?> { ["西"] = null, ["东"] = o.Width - o.EdgeInset };
        edges[o.Name] = new Dictionary<string, int?> { ["西"] = o.EdgeInset, ["东"] = o.Width - o.EdgeInset };
        edges[o.Name + "·东"] = new Dictionary<string, int?> { ["西"] = o.EdgeInset, ["东"] = null };
        Files.Write(Path.Combine(dir, "EdgeStitch.json"), new Dictionary<string, object>
        {
            ["世界链"] = specs.Select(s => s.Name).ToArray(), ["首尾相连成环"] = false,
            ["边缘带宽度_格"] = o.EdgeInset, ["传送冷却_毫秒"] = 250, ["落点内缩_格"] = 2, ["仅朝外移动时触发"] = true,
            ["强制同Y穿越"] = true,
            ["陆地边缘"] = edges, ["边缘触发余量_格"] = 0, ["切片间距_格"] = o.Stride,
            ["启用"] = true, ["换乘日志"] = true, ["诊断日志"] = false, ["诊断间隔_毫秒"] = 5000
        });
        var routes = new List<object>();
        for (int i = 0; i < 2; i++)
        {
            int globalX = specs[i].SourceX + o.Width - o.EdgeInset;
            if (globalX != specs[i + 1].SourceX + o.EdgeInset) throw new InvalidDataException("边界不重合。");
            routes.Add(new { from = specs[i].Name, to = specs[i + 1].Name, sourceX = o.Width - o.EdgeInset, targetX = o.EdgeInset, globalX, y = "unchanged", direction = "east" });
            routes.Add(new { from = specs[i + 1].Name, to = specs[i].Name, sourceX = o.EdgeInset, targetX = o.Width - o.EdgeInset, globalX, y = "unchanged", direction = "west" });
        }
        Files.Write(Path.Combine(dir, "seam-routing-contract.json"), new { defaultWorld = o.Name, routes,
            landingInsetTiles = 2, yPolicy = "exact-source-player-position-Y-HARD-PRIORITY-no-floor-search-no-spawn-fallback", velocityPolicy = "restore-source-velocity-after-native-teleport",
            trigger = "接触可活动边界，非接近即传送；目标碰撞箱在对侧边缘内1–2格；逻辑接缝同坐标，实际落点有1–2格安全内缩；不使用世界出生点",
            requires = "需要同包EdgeStitch 1.3.0或兼容实现；候选未自动安装，不能声称旧版已支持；不修改维度插件", e2e = "UNVERIFIED" });
    }
    public static void VerifyExisting(string output, Action<string> log)
    {
        var record = Files.Read<GenerationRecord>(Path.Combine(output, "generation-result.json"));
        var o = record.Options;
        var expectedSpecs = o.Specs(record.SourceOffset);
        for (int i = 0; i < record.Worlds.Length && i < expectedSpecs.Length; i++)
            if (record.Worlds[i].Spec != expectedSpecs[i]) throw new InvalidDataException("世界顺序/窗口不是西—中—东。");
        if (record.Worlds.Length != 3 || record.Worlds.Select(w => w.WorldGuid).Distinct().Count() != 3 || record.Worlds.Select(w => w.WorldId).Distinct().Count() != 3)
            throw new InvalidDataException("必须恰好三个独立世界身份。");
        var paths = new List<string>();
        int? seaY = null;
        double? seaGround = null, seaRock = null;
        foreach (var s in record.Worlds)
        {
            string path = Path.Combine(output, "Worlds", s.Spec.Name + ".wld"); paths.Add(path);
            var actual = Files.Inspect(path);
            if (actual.Bytes != s.File.Bytes || actual.Sha256 != s.File.Sha256) throw new InvalidDataException("地图大小/SHA不符：" + path);
            var w = WorldIO.Load(path);
            if (o.WorldLayout == "normal-stitch")
            {
                int y = NormalWorlds.FindSea(w, o.EdgeInset); seaY ??= y; seaGround ??= w.GroundLevel; seaRock ??= w.RockLevel;
                if (y != seaY || NormalWorlds.FindSea(w, o.Width - o.EdgeInset - 1) != seaY || w.GroundLevel != seaGround || w.RockLevel != seaRock)
                    throw new InvalidDataException("三图物理海面或世界层Y不一致。");
            }
            if (w.TilesWide != o.Width || w.TilesHigh != o.Height || w.GameMode != o.Difficulty ||
                w.WorldGUID != s.WorldGuid || w.WorldId != s.WorldId || w.Title != s.Spec.Name ||
                w.SpawnX != s.SpawnX || w.SpawnY != s.SpawnY || !WorldIO.SpawnSafe(w) ||
                WorldIO.Digest(w) != s.TileDigest || WorldIO.MetadataDigest(w) != s.MetadataDigest)
                throw new InvalidDataException("地图内容/身份/出生点不符合记录：" + path);
            w.Tiles = null!; WorldIO.Collect();
        }
        var a = WorldIO.Compare(paths[0], paths[1], o.Overlap); var b = WorldIO.Compare(paths[1], paths[2], o.Overlap);
        if (a.Mismatches != 0 || b.Mismatches != 0 || a.LeftDigest != a.RightDigest || b.LeftDigest != b.RightDigest)
            throw new InvalidDataException("接缝被改动。");
        using var edge = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "config-fragments", "EdgeStitch.json")));
        var e = edge.RootElement;
        if (e.GetProperty("切片间距_格").GetInt32() != o.Stride || e.GetProperty("边缘触发余量_格").GetInt32() != 0 ||
            e.GetProperty("首尾相连成环").GetBoolean() || !e.GetProperty("世界链").EnumerateArray().Select(x => x.GetString()).SequenceEqual(expectedSpecs.Select(x => x.Name)))
            throw new InvalidDataException("EdgeStitch片段与本批地图不一致。");
        using var launcher = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "config-fragments", "launcher.fragment.json")));
        var l = launcher.RootElement.GetProperty("launcher");
        if (l.GetProperty("joinServer").GetString() != "first" ||
            !l.GetProperty("autoStartServers").EnumerateArray().Select(s => s.GetProperty("worldName").GetString()).SequenceEqual(new[] { o.Name, o.Name + "·西", o.Name + "·东" }))
            throw new InvalidDataException("默认进入中间世界的启动片段不一致。");
        log($"验证通过：3世界SHA/完整图格与元数据摘要/中央安全出生点；两接缝{a.ComparedTiles + b.ComparedTiles:N0}格全部一致。");
    }
}
