using System.Drawing;
using TEdit.Geometry;
using TEdit.Terraria;

namespace ThreeWorlds;

internal static class FoundationTests
{
    public static void Run(string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("测试证据不得覆盖。");
        Directory.CreateDirectory(output); WorldIO.Initialize(); int passed = 0, failed = 0; var checks = new List<object>();
        void Check(string name, Func<bool> test)
        {
            try { if (!test()) throw new Exception("assertion false"); passed++; checks.Add(new { name, pass = true }); }
            catch (Exception e) { failed++; checks.Add(new { name, pass = false, error = e.ToString() }); }
        }
        bool Reject(Action a) { try { a(); return false; } catch { return true; } }
        World Empty(int width = 3440) => new(600, width, "基础环境夹具", 73) { Version = 326, GameMode = 2,
            Tiles = new Tile[width, 600], GroundLevel = 200, RockLevel = 350, SpawnX = width / 2, SpawnY = 159,
            RightWorld = width * 16, BottomWorld = 600 * 16, SkyblockWorld = true };
        var w = Empty(); var policies = new FoundationOptions();
        Check("default_auto_applies_to_actual_skyblock_flag", () => policies.Applies(w));
        w.SkyblockWorld = false;
        Check("ordinary_world_not_silently_changed_by_default", () => !policies.Applies(w));
        Check("custom_explicit_enabled_applies", () => new FoundationOptions { Mode = "enabled" }.Applies(w));
        w.SkyblockWorld = true;
        Check("disabled_never_applies_even_skyblock", () => !new FoundationOptions { Mode = "disabled" }.Applies(w));
        Check("unknown_feature_rejected", () => Reject(() => new FoundationOptions { Features = ["fake"] }.Validate()));
        Check("unknown_policy_rejected", () => Reject(() => new FoundationOptions { Mode = "maybe" }.Validate()));
        Check("duplicate_features_rejected", () => Reject(() => new FoundationOptions { Features = ["jungle", "jungle"] }.Validate()));
        Check("six_required_features_explicit_by_default", () => policies.Features.SequenceEqual(new[] { "dungeon", "temple", "jungle", "crimson", "corruption", "shimmer" }));
        Check("empty_map_six_missing_not_fake_present", () => FoundationWorlds.Missing(w, policies).Length == 6);
        var donor = Empty(900); donor.SkyblockWorld = false; donor.DungeonX = 55; donor.DungeonY = 130;
        foreach (var f in FoundationCatalog.All)
        {
            int index = Array.IndexOf(FoundationCatalog.All, f), x0 = 25 + index % 3 * 260, y0 = 60 + index / 3 * 160;
            for (int x = x0; x < x0 + 60; x++) for (int y = y0; y < y0 + 60; y++) donor.Tiles[x, y] = f.Id == "shimmer" ?
                new Tile { LiquidAmount = 222, LiquidType = LiquidType.Shimmer } : new Tile { IsActive = true, Type = f.TileTypes[0], Wall = (ushort)(f.Id == "dungeon" ? 7 : 0) };
        }
        foreach (var f in FoundationCatalog.All.Where(f => f.Id != "temple"))
            Check("actual_saved_types_recognize_" + f.Id, () => FoundationWorlds.Scan(donor).Single(s => s.Id == f.Id).Present);
        Check("temple_bricks_without_altar_not_complete", () => !FoundationWorlds.Scan(donor).Single(s => s.Id == "temple").Present);
        for (int x = 300; x < 303; x++) for (int y = 100; y < 102; y++) donor.Tiles[x, y] = new Tile { IsActive = true, Type = 237, U = (short)((x - 300) * 18), V = (short)((y - 100) * 18) };
        Check("temple_requires_real_altar_tiles", () => FoundationWorlds.Scan(donor).Single(s => s.Id == "temple").Present);
        var chest = new Chest(70, 140, "原生锁箱夹具"); chest.Items[0].NetId = 8; chest.Items[0].StackSize = 17; donor.Chests.Add(chest);
        for (int x = 70; x < 72; x++) for (int y = 140; y < 142; y++) donor.Tiles[x, y] = new Tile { IsActive = true, Type = 21, U = (short)(36 + (x - 70) * 18), V = (short)((y - 140) * 18) };
        donor.Signs.Add(new Sign(80, 145, "建筑签名")); donor.PressurePlates.Add(new PressurePlate { PosX = 92, PosY = 145 });
        donor.NPCs.Add(new NPC { SpriteId = 37, Name = "Old Man", DisplayName = "Old Man", Position = new Vector2FloatObservable(55 * 16, 125 * 16), Home = new Vector2Int32Observable(55, 125) });
        donor.Tiles[90, 146] = new Tile { IsActive = true, Type = 1, Wall = 7, WireBlue = true, WireRed = true, InvisibleWall = true, FullBrightBlock = true, TileColor = 5, BrickStyle = BrickStyle.SlopeBottomLeft };
        var o = new Options { Width = 1200, Height = 600, Output = output };
        var box = new Rectangle(16, 32, 180, 150);
        var destination = FoundationWorlds.FindPlacement(w, donor, box, o, []);
        var patch = FoundationWorlds.Transplant(w, donor, new("fixture", 0, "FIXTURE"), ["dungeon"], box, destination);
        Check("patch_preserves_all_serialized_tile_fields", () => FoundationWorlds.RegionDigest(w, destination) == patch.TileDigest);
        Check("native_chest_payload_and_lock_frame_preserved", () => w.Chests.Count == 1 && w.Chests[0].Items[0].NetId == 8 && w.Chests[0].Items[0].StackSize == 17 && w.Tiles[w.Chests[0].X, w.Chests[0].Y].U == 36);
        Check("native_sign_and_pressure_plate_shifted", () => w.Signs.Single().Text == "建筑签名" && w.PressurePlates.Count == 1);
        Check("dungeon_oldman_and_entrance_coordinates_shifted", () => w.NPCs.Single().SpriteId == 37 && w.DungeonX == 55 + destination.X - 16 && w.DungeonY == 130 + destination.Y - 32);
        foreach (var spec in o.Specs())
        {
            Check(spec.Role + "_entire_depth_seam_not_modified", () => !destination.IntersectsWith(new(spec.SourceX, 0, 80, 600)) && !destination.IntersectsWith(new(spec.SourceX + 1120, 0, 80, 600)));
            Check(spec.Role + "_central_spawn_reserved_all_depths", () => !destination.IntersectsWith(new(spec.SourceX + 600 - 64, 0, 128, 600)));
        }
        Check("patch_not_recentered_to_spawn", () => destination.X != w.SpawnX && destination.Y == box.Y);
        string final = Path.Combine(output, "actual-fixture.wld"); WorldIO.Save(w, final); var loaded = WorldIO.Load(final);
        Check("saved_patch_exact_native_tile_digest", () => FoundationWorlds.RegionDigest(loaded, destination) == patch.TileDigest);
        Check("saved_chest_sign_npc_roundtrip", () => WorldIO.MetadataDigest(loaded) == WorldIO.MetadataDigest(w));
        Check("no_boss_progress_unlocked", () => !loaded.DownedGolemBoss && !loaded.HardMode);
        Check("refuse_overwrite_existing_objects", () => Reject(() => FoundationWorlds.Transplant(w, donor, new("x", 0, "x"), [], box, destination)));
        Check("refuse_out_of_bounds_patch", () => Reject(() => FoundationWorlds.Transplant(w, donor, new("x", 0, "x"), [], box, new Rectangle(-1, 0, 180, 150))));
        Check("oversized_structure_fails_closed_not_cut_or_scaled", () => Reject(() => FoundationWorlds.FindPlacement(w, donor, new(0, 0, 6000, 100), o, [])));
        var passage = Empty(); passage.SkyblockWorld = false;
        for (int x = 0; x < passage.TilesWide; x++) for (int y = 210; y < 600; y++)
            passage.Tiles[x, y] = new Tile { IsActive = y < 320 || y > 355 + x % 4, Type = 1, Wall = 2, WireBlue = y == 400, BrickStyle = y == 210 ? BrickStyle.SlopeBottomRight : BrickStyle.Full };
        var originalSpawn = (passage.SpawnX, passage.SpawnY); var prepared = SeamPreparation.Prepare(passage, o, 0);
        Check("two_prepared_seams_full_depth_and_nonzero_changes", () => prepared.Length == 2 && prepared.All(s => s.ChangedTiles > 0));
        foreach (var seam in prepared)
        {
            Check(seam.GlobalX + "_all_integer_pixel_Y_four_landing_combinations", () => SeamPreparation.Verify(passage, seam.GlobalX) is { Checked: > 0, Blocked: 0 });
            Check(seam.GlobalX + "_surface_same_floor_at_opposite_insets", () => Enumerable.Range(-5, 10).All(dx => SeamPreparation.Solid(passage, seam.GlobalX + dx, 210) && !SeamPreparation.Solid(passage, seam.GlobalX + dx, 209)));
            Check(seam.GlobalX + "_cave_passage_keeps_cave_Y_not_surface", () => Enumerable.Range(-5, 10).All(dx => !SeamPreparation.Solid(passage, seam.GlobalX + dx, 335)));
        }
        Check("seam_preparation_never_changes_default_spawn", () => originalSpawn == (passage.SpawnX, passage.SpawnY));
        Check("outside_8_column_bands_not_changed", () => passage.Tiles[100, 210].BrickStyle == BrickStyle.SlopeBottomRight);
        var guarded = Empty(); guarded.Chests.Add(new Chest(1160, 200, "禁止动此箱"));
        Check("seam_chest_preserved_not_duplicate_or_delete", () => SeamPreparation.Prepare(guarded, o, 0)[0].ProtectedObjectRows.Length > 0 && guarded.Chests.Count == 1);
        guarded.Chests.Clear(); guarded.Tiles[1160, 200] = new Tile { IsActive = true, Type = 10 };
        Check("seam_multitile_door_preserved_in_place", () => SeamPreparation.Prepare(guarded, o, 0)[0].ProtectedObjectRows.Length > 0 && guarded.Tiles[1160, 200].Type == 10 && guarded.Tiles[1160, 200].IsActive);
        Check("biome_outside_retained_crop_not_falsely_counted", () => !FoundationWorlds.Scan(w, new Rectangle(0, 0, 80, 600)).Single(s => s.Id == "dungeon").Present);
        var archipelago = Empty(); archipelago.SkyblockWorld = false;
        for (int x = 0; x < archipelago.TilesWide; x++) for (int y = 180; y < 600; y++)
            archipelago.Tiles[x, y] = y < 350 ? new Tile { LiquidAmount = 255, LiquidType = LiquidType.Water } : new Tile { IsActive = true, Type = 1 };
        string archSource = Path.Combine(output, "archipelago-source.wld"); WorldIO.Save(archipelago, archSource);
        var archOptions = new Options { Width = 1200, Height = 600, Style = "archipelago", Output = output };
        var archSlice = WorldIO.Slice(archSource, Files.Inspect(archSource).Sha256, archOptions.Specs()[1], archOptions, Path.Combine(output, "archipelago-middle.wld"));
        Check("archipelago_water_center_gets_dry_surface_island_not_cave", () => archSlice.SpawnX == 600 && archSlice.SpawnY == 179 && archSlice.Changes.Count > 0 && WorldIO.SpawnSafe(WorldIO.Load(archSlice.File.Path)));
        archipelago.Tiles[1720, 180] = new Tile { IsActive = true, Type = 21 }; archipelago.Chests.Add(new Chest(1720, 180, "禁止用出生岛覆盖"));
        string chestSource = Path.Combine(output, "archipelago-chest-source.wld"); WorldIO.Save(archipelago, chestSource);
        Check("archipelago_spawn_island_refuses_chest_destruction", () => Reject(() => WorldIO.Slice(chestSource, Files.Inspect(chestSource).Sha256, archOptions.Specs()[1], archOptions, Path.Combine(output, "archipelago-rejected.wld"))));
        archipelago.Tiles = null!;
        passage.Tiles = null!; guarded.Tiles = null!;
        w.Tiles = null!; loaded.Tiles = null!; donor.Tiles = null!; WorldIO.Collect();
        Files.Write(Path.Combine(output, "result.json"), new { passed, failed, checks, product = Files.Inspect(Path.Combine(AppContext.BaseDirectory, "UTSL.ThreeWorlds.dll")), scope = "actual DLL + serialized wld fixtures; native-template generation tested separately; NOT game/boss E2E", productionTouched = false });
        if (failed != 0) throw new Exception("foundation-tests failed=" + failed);
    }
}
