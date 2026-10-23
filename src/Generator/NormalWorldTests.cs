using TEdit.Terraria;

namespace ThreeWorlds;

internal static class NormalWorldTests
{
    internal static void Run(string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("测试目录非空。");
        Directory.CreateDirectory(output); WorldIO.Initialize(); var checks = new List<object>(); int passed = 0, failed = 0;
        void Check(string name, Func<bool> test) { try { if (!test()) throw new Exception("assertion false"); checks.Add(new { name, pass = true }); passed++; } catch (Exception ex) { checks.Add(new { name, pass = false, error = ex.ToString() }); failed++; } }
        bool Reject(Action a) { try { a(); return false; } catch { return true; } }
        Check("normal_layout_valid", () => { new Options { WorldLayout = "normal-stitch", Output = output }.Validate(); return true; });
        Check("unknown_layout_rejected", () => Reject(() => new Options { WorldLayout = "wrong", Output = output }.Validate()));
        Check("normal_skyblock_conflict_rejected", () => Reject(() => new Options { WorldLayout = "normal-stitch", Style = "skyblock", Output = output }.Validate()));
        Check("normal_source_import_conflict_rejected", () => Reject(() => new Options { WorldLayout = "normal-stitch", Source = "some.wld", Output = output }.Validate()));
        Check("middle_seed_unchanged", () => Pipeline.NormalSeed("123", 1) == "123");
        Check("three_seeds_distinct", () => Enumerable.Range(0, 3).Select(i => Pipeline.NormalSeed("123", i)).Distinct().Count() == 3);
        Check("derived_seeds_deterministic", () => Pipeline.NormalSeed("text", 0) == Pipeline.NormalSeed("text", 0));
        const int width = 800, height = 600; var options = new Options { Width = width, Height = height, Output = output, WorldLayout = "normal-stitch" }; var inputs = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var w = new World(height, width, "fixture-" + i, 73) { Version = 326, GameMode = 2, Tiles = new Tile[width, height], RightWorld = width * 16, BottomWorld = height * 16, GroundLevel = 160 + i * 10, RockLevel = 310 + i * 10, SpawnX = width / 2, SpawnY = 179 };
            for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
            {
                bool coast = x < 220 || x >= width - 220; int floor = coast ? 230 + i * 4 : 180;
                if (y >= floor) w.Tiles[x, y] = new Tile { IsActive = true, Type = y > 300 ? (ushort)1 : (ushort)53 };
                else if (coast && y >= 150 + i * 10) w.Tiles[x, y] = new Tile { LiquidAmount = 255, LiquidType = LiquidType.Water };
            }
            var chest = new Chest(350, 178, "内陆战利品"); chest.Items[0].NetId = 8; chest.Items[0].StackSize = 17; w.Chests.Add(chest);
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++) w.Tiles[350 + dx, 178 + dy] = new Tile { IsActive = true, Type = 21, U = (short)(dx * 18), V = (short)(dy * 18) };
            string p = Path.Combine(output, "input-" + i + ".wld"); WorldIO.Save(w, p); inputs.Add(p); w.Tiles = null!; WorldIO.Collect();
        }
        var before = inputs.Select(Files.Inspect).ToArray(); var audits = NormalWorlds.Prepare(inputs.ToArray(), options, output, CancellationToken.None);
        Check("same_sea_y_three_worlds", () => audits.Select(a => a.SeaY).Distinct().Single() == 160);
        Check("same_ground_level", () => audits.Select(a => a.GroundLevel).Distinct().Count() == 1);
        Check("same_rock_level", () => audits.Select(a => a.RockLevel).Distinct().Count() == 1);
        Check("inland_exactly_unchanged", () => audits.All(a => a.InlandBefore == a.InlandAfter));
        for (int i = 0; i < 3; i++)
        {
            var w = WorldIO.Load(audits[i].PreparedFile);
            int n = i; Check("physical_both_coast_seaY_" + i, () => NormalWorlds.FindSea(w, 40) == 160 && NormalWorlds.FindSea(w, width - 41) == 160);
            Check("inland_loot_preserved_" + i, () => w.Chests.Single().X == 350 && w.Chests.Single().Items[0].StackSize == 17);
            Check("input_sha_unchanged_" + i, () => Files.Inspect(inputs[n]).Sha256 == before[n].Sha256); w.Tiles = null!; WorldIO.Collect();
        }
        Check("west_middle_full_depth_all_fields_equal", () => WorldIO.Compare(audits[0].PreparedFile, audits[1].PreparedFile, 80).Mismatches == 0);
        Check("middle_east_full_depth_all_fields_equal", () => WorldIO.Compare(audits[1].PreparedFile, audits[2].PreparedFile, 80).Mismatches == 0);
        var bad = WorldIO.Load(inputs[0]); bad.Chests.Add(new Chest(40, 180, "保护"));
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++) bad.Tiles[40 + dx, 180 + dy] = new Tile { IsActive = true, Type = 21, U = (short)(dx * 18), V = (short)(dy * 18) };
        string blocked = Path.Combine(output, "blocked.wld"); WorldIO.Save(bad, blocked); bad.Tiles = null!; WorldIO.Collect();
        var protectedAudit = NormalWorlds.Prepare([blocked, inputs[1], inputs[2]], options, output, CancellationToken.None);
        Check("seam_chest_preserved_once_sameY_outside_band", () => protectedAudit[0].ChestMoves is [{ FromX: 40, FromY: 180, ToY: 180, ToX: >= 80 and < 220 }]);
        Check("seam_chest_loot_and_metadata_not_lost", () => { var w = WorldIO.Load(protectedAudit[0].PreparedFile); bool ok = w.Chests.Count == 2 && w.Chests.Count(c => c.Name == "保护") == 1; w.Tiles = null!; return ok; });
        Check("blocked_chest_still_exists", () => { var w = WorldIO.Load(blocked); bool ok = w.Chests.Any(c => c.Name == "保护"); w.Tiles = null!; return ok; });
        var composite = WorldIO.Load(inputs[0]);
        var crossingChest = new Chest(79,228,"交界宝箱"); crossingChest.Items[0].NetId=8; crossingChest.Items[0].StackSize=37; crossingChest.Items[0].Prefix=3; composite.Chests.Add(crossingChest);
        for (int dx=0;dx<2;dx++) for(int dy=0;dy<2;dy++) composite.Tiles[79+dx,228+dy]=new Tile {IsActive=true,Type=21,U=(short)(dx*18),V=(short)(dy*18)};
        for (int dx=0;dx<3;dx++) for(int dy=0;dy<3;dy++) composite.Tiles[76+dx,227+dy]=new Tile {IsActive=true,Type=105,U=(short)(dx*18),V=(short)(dy*18)};
        for(int x=30;x<56;x++) composite.Tiles[x,290]=new Tile {IsActive=true,Type=314};
        string compositePath=Path.Combine(output,"composite-input.wld"); WorldIO.Save(composite,compositePath); composite.Tiles=null!; WorldIO.Collect();
        string compositeHash=Files.Inspect(compositePath).Sha256;
        var comboOutput=Path.Combine(output,"composite"); Directory.CreateDirectory(comboOutput);
        var combo=NormalWorlds.Prepare([compositePath,inputs[1],inputs[2]],options,comboOutput,CancellationToken.None);
        var moved=WorldIO.Load(combo[0].PreparedFile);
        Check("crossing_80_chest_preserved_exactly_once",()=>moved.Chests.Count(c=>c.Name=="交界宝箱")==1 && moved.Chests.Count==2);
        Check("crossing_chest_inventory_prefix_and_y",()=>{var c=moved.Chests.Single(c=>c.Name=="交界宝箱");return c.Y==228&&c.X>=100&&c.X<218&&c.Items[0].NetId==8&&c.Items[0].StackSize==37&&c.Items[0].Prefix==3;});
        Check("composite_statue_and_chest_moved_together",()=>combo[0].ObjectMoves.Any(m=>m.From.Contains(new System.Drawing.Rectangle(76,227,5,4))&&m.ChestMoves.Length==1));
        Check("native_26_column_track_preserved_as_whole",()=>combo[0].ObjectMoves.Any(m=>m.Type==314&&m.From.Width==26&&m.To.Width==26&&m.To.Y==m.From.Y));
        Check("relocated_rectangles_do_not_overlap",()=>combo[0].ObjectMoves.All(a=>combo[0].ObjectMoves.All(b=>ReferenceEquals(a,b)||!a.To.IntersectsWith(b.To))));
        Check("all_serialized_object_digest_exact",()=>combo[0].ObjectMoves.All(m=>FoundationWorlds.RegionDigest(moved,m.To)==m.TileDigest));
        Check("no_boundary_chest_tiles_or_metadata_remaining",()=>!moved.Chests.Any(c=>c.X<80||c.X+2>width-80)&&!moved.Tiles[79,228].IsChest());
        Check("composite_original_source_hash_unchanged",()=>Files.Inspect(compositePath).Sha256==compositeHash);
        moved.Tiles=null!; WorldIO.Collect();
        var huge=WorldIO.Load(inputs[0]); for(int x=20;x<175;x++) huge.Tiles[x,290]=new Tile {IsActive=true,Type=314};
        string hugePath=Path.Combine(output,"large-track-input.wld"); WorldIO.Save(huge,hugePath); huge.Tiles=null!; WorldIO.Collect();
        var hugeOutput=Path.Combine(output,"large-track-rejected"); Directory.CreateDirectory(hugeOutput);
        Check("oversized_structure_rejected_not_deleted",()=>Reject(()=>NormalWorlds.Prepare([hugePath,inputs[1],inputs[2]],options,hugeOutput,CancellationToken.None)));
        Files.Write(Path.Combine(output, "result.json"), new { passed, failed, checks, dll = Files.Inspect(Path.Combine(AppContext.BaseDirectory, "UTSL.ThreeWorlds.dll")), scope = "actual DLL + serialized world fixtures; native-generation tested separately; not game E2E", productionTouched = false });
        if (failed != 0) throw new Exception($"{failed} normal-stitch assertions failed");
    }
}
