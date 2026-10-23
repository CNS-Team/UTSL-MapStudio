using System.Reflection.PortableExecutable;
using TEdit.Terraria;
using ThreeWorlds.Live;

namespace ThreeWorlds;

internal static class LiveTests
{
    public static void Run(string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("拒绝覆盖旧测试证据。");
        Directory.CreateDirectory(output); WorldIO.Initialize();
        var checks = new List<object>(); int passed = 0, failed = 0;
        void Check(string name, Func<bool> body)
        {
            try { if (!body()) throw new Exception("assertion false"); checks.Add(new { name, pass = true }); passed++; }
            catch (Exception ex) { checks.Add(new { name, pass = false, error = ex.ToString() }); failed++; }
        }
        bool Reject(Action a) { try { a(); return false; } catch { return true; } }
        string bin = Path.Combine(output, "frame.bin");
        int[] pixels = [unchecked((int)0xff123456), unchecked((int)0xffabcdef), -1, 0];
        LiveProtocol.WriteFrame(bin, new(19, 12440, 1200, 2, 2, pixels));
        Check("actual_binary_frame_roundtrip", () => { var f = LiveProtocol.ReadFrame(bin); return f.Sequence == 19 && f.WorldWidth == 12440 && f.Pixels.SequenceEqual(pixels); });
        Check("frame_length_is_bounded", () => new FileInfo(bin).Length == 44);
        Check("oversized_frame_rejected", () => Reject(() => LiveProtocol.WriteFrame(bin, new(1, 2, 2, 50000, 50000, []))));
        Check("negative_frame_dimensions_rejected", () => Reject(() => LiveProtocol.WriteFrame(bin, new(1, 2, 2, -1, 2, []))));
        Check("pixel_count_mismatch_rejected", () => Reject(() => LiveProtocol.WriteFrame(bin, new(1, 2, 2, 2, 2, [0]))));
        Check("bad_write_keeps_previous_frame", () => LiveProtocol.ReadFrame(bin).Sequence == 19);
        string broken = Path.Combine(output, "broken.bin"); File.WriteAllBytes(broken, [1, 2, 3, 4]);
        Check("malformed_frame_rejected", () => Reject(() => LiveProtocol.ReadFrame(broken)));
        File.WriteAllBytes(broken, File.ReadAllBytes(bin)[..^2]);
        Check("truncated_frame_rejected", () => Reject(() => LiveProtocol.ReadFrame(broken)));
        Check("atomic_frame_read_write_race", () =>
        {
            var writes = Task.Run(() => { for (int i = 0; i < 40; i++) LiveProtocol.WriteFrame(bin, new(i, 20, 20, 2, 2, pixels)); });
            try { for (int i = 0; i < 40; i++) { var f = LiveProtocol.ReadFrame(bin); if (f.Pixels.Length != 4) return false; } return true; }
            finally { writes.GetAwaiter().GetResult(); }
        });
        Check("no_temporary_frame_leak", () => !Directory.EnumerateFiles(output, "*.tmp").Any());
        string json = Path.Combine(output, "live-status.json");
        LiveProtocol.WriteStatus(json, new(2, DateTimeOffset.Now, "native", "地形", "真实", .26, .37, 20, 20, 2, 2, 5.1, 2, "actual"));
        Check("status_preserves_native_progress_not_time_estimate", () => LiveProtocol.ReadStatus(json) is { NativeProgress: .26, PassProgress: .37, Pass: "地形" });
        foreach (var dims in new[] { (4200, 1200), (6400, 1500), (8400, 2400) })
        {
            var o = new Options { Width = dims.Item1, Height = dims.Item2, Output = output };
            Check($"{o.Width}_overlap80_inset40_all_layers", () => { o.Validate(); return o.Overlap == 80 && o.EdgeInset == 40 && o.Specs()[1].SourceX + 40 == o.Specs()[0].SourceX + o.Width - 40; });
        }
        foreach (var s in StyleCatalog.All)
            Check("style_available_" + s.Id, () => { new Options { Style = s.Id, Output = output }.Validate(); return StyleCatalog.Get(s.Id) == s; });
        Check("unknown_style_rejected", () => Reject(() => new Options { Style = "fake", Output = output }.Validate()));
        Check("unreachable_edge20_rejected", () => Reject(() => new Options { EdgeInset = 20, Output = output }.Validate()));
        Check("unbounded_preview_frequency_rejected", () => Reject(() => new Options { PreviewIntervalMs = 1, Output = output }.Validate()));
        Check("seed_newline_rejected", () => Reject(() => new Options { Seed = "1\n2", Output = output }.Validate()));
        Check("apphost_is_GUI_no_command_window", () => { using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "UTSL.ThreeWorlds.exe")); using var pe = new PEReader(s); return pe.PEHeaders.PEHeader!.Subsystem == Subsystem.WindowsGui; });
        var options = new Options { Name = "特殊样式夹具", Width = 400, Height = 450, Output = output };
        World Fixture(bool sky) => new(450, options.SourceWidth, "Fixture", 73)
        {
            Version = 326, GameMode = 2, Tiles = new Tile[options.SourceWidth, 450],
            RightWorld = options.SourceWidth * 16, BottomWorld = 450 * 16,
            GroundLevel = 130, RockLevel = 260, SpawnX = options.SourceWidth / 2, SpawnY = sky ? 99 : 349,
            SkyblockWorld = sky, RemixWorld = !sky, ZenithWorld = !sky
        };
        foreach (bool sky in new[] { true, false })
        {
            var w = Fixture(sky);
            if (!sky) for (int x = 0; x < w.TilesWide; x++) for (int y = 350; y < 370; y++) w.Tiles[x, y] = new Tile { IsActive = true, Type = 1 };
            string source = Path.Combine(output, sky ? "sky-source.wld" : "zenith-source.wld"); WorldIO.Save(w, source); w.Tiles = null!; WorldIO.Collect();
            var results = options.Specs().Select(s => WorldIO.Slice(source, Files.Inspect(source).Sha256, s, options, Path.Combine(output, (sky ? "sky-" : "zenith-") + s.Role + ".wld"))).ToArray();
            Check((sky ? "sky" : "zenith") + "_three_independent_worlds", () => results.Select(s => s.WorldGuid).Distinct().Count() == 3);
            foreach (var result in results)
            {
                var actual = WorldIO.Load(result.File.Path);
                Check((sky ? "sky" : "zenith") + "_" + result.Spec.Role + "_safe_center_layer", () => WorldIO.SpawnSafe(actual) && actual.SpawnX == 200 && Math.Abs(actual.SpawnY - (sky ? 99 : 349)) <= 1);
                Check((sky ? "sky" : "zenith") + "_" + result.Spec.Role + "_special_flags_preserved", () => sky ? actual.SkyblockWorld : actual.ZenithWorld && actual.RemixWorld);
                Check((sky ? "sky" : "zenith") + "_" + result.Spec.Role + "_no_unlisted_source_changes", () => result.SourceTileMismatches == 0);
                actual.Tiles = null!;
            }
            Check((sky ? "sky" : "zenith") + "_west_middle_all_depths_seam0", () => WorldIO.Compare(results[0].File.Path, results[1].File.Path, 80).Mismatches == 0);
            Check((sky ? "sky" : "zenith") + "_middle_east_all_depths_seam0", () => WorldIO.Compare(results[1].File.Path, results[2].File.Path, 80).Mismatches == 0);
        }
        Files.Write(Path.Combine(output, "result.json"), new { passed, failed, checks, product = Files.Inspect(Path.Combine(AppContext.BaseDirectory, "UTSL.ThreeWorlds.dll")), gameE2E = "UNVERIFIED", productionTouched = false });
        if (failed != 0) throw new Exception("live-tests failed=" + failed);
    }
}
