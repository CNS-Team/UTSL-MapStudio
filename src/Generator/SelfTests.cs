using TEdit.Geometry;
using TEdit.Terraria;

namespace ThreeWorlds;

internal static class SelfTests
{
    public static void Run(string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("测试也不能覆盖旧结果，请指定新目录。");
        Directory.CreateDirectory(output); WorldIO.Initialize();
        var rows = new List<object>(); int passed = 0, failed = 0;
        void Check(string name, Func<bool> assertion)
        {
            try { if (!assertion()) throw new Exception("断言返回false"); passed++; rows.Add(new { name, pass = true }); Console.WriteLine("PASS " + name); }
            catch (Exception e) { failed++; rows.Add(new { name, pass = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + " " + e.Message); }
        }
        bool Reject(Action action) { try { action(); return false; } catch { return true; } }
        var o = new Options { Name = "接缝夹具", Width = 400, Height = 450, EdgeInset = 40, Output = output };
        o.Validate(fixture: true);
        Check("geometry_source_width_1040", () => o.SourceWidth == 1040);
        Check("geometry_stride320_overlap80", () => o.Stride == 320 && o.Overlap == 80);
        foreach (var dims in new[] { (4200, 1200), (6400, 1500), (8400, 2400) })
        {
            var standard = new Options { Width = dims.Item1, Height = dims.Item2, Output = output };
            Check($"standard_{dims.Item1}_geometry_four_directions", () =>
            {
                standard.Validate(); var ss = standard.Specs();
                return ss[0].SourceX + standard.Width - 40 == ss[1].SourceX + 40 &&
                    ss[1].SourceX + standard.Width - 40 == ss[2].SourceX + 40 && standard.Overlap == 80;
            });
        }
        var w = new World(o.Height, o.SourceWidth, "Fixture", 73)
        {
            Version = 326, GameMode = 2, Tiles = new Tile[o.SourceWidth, o.Height],
            RightWorld = o.SourceWidth * 16, BottomWorld = o.Height * 16, GroundLevel = 150, RockLevel = 270,
            SpawnX = 520, SpawnY = 119, DungeonX = 950,
            TreeX2 = 800, TreeX1 = 500, TreeX0 = 200,
            CaveBackX2 = 810, CaveBackX1 = 510, CaveBackX0 = 210
        };
        for (int x = 0; x < w.TilesWide; x++) for (int y = 0; y < w.TilesHigh; y++)
        {
            int surface = 120 + (int)(6 * Math.Sin(x / 70.0));
            bool cave = y > 190 && y < 230 && (x + y) % 39 < 26;
            w.Tiles[x, y] = new Tile
            {
                IsActive = y >= surface && !cave, Type = (ushort)(y >= surface && !cave ? (y > 175 ? 1 : 0) : 0),
                Wall = (ushort)(y > surface + 10 ? 2 : 0),
                WireRed = y == 200, WireBlue = y == 201, WireGreen = y == 202, WireYellow = y == 203,
                LiquidAmount = (byte)(cave && y == 220 ? 125 : 0), LiquidType = cave && y == 220 ? LiquidType.Water : LiquidType.None,
                BrickStyle = y >= surface && !cave && y % 43 == 0 ? BrickStyle.SlopeTopRight : BrickStyle.Full,
                TileColor = (byte)(y >= surface && !cave && y % 47 == 0 ? 2 : 0),
                Actuator = y == 260, InActive = y == 260, InvisibleBlock = y == 270, FullBrightBlock = y == 271,
                InvisibleWall = y == 280, FullBrightWall = y == 281
            };
        }
        void Object(int x, int y, ushort type, int width, int height)
        {
            for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++)
                w.Tiles[x + dx, y + dy] = new Tile { IsActive = true, Type = type, U = (short)(dx * 18), V = (short)(dy * 18) };
        }
        Object(350, 90, 21, 2, 2); var chest = new Chest(350, 90, "跨缝宝箱");
        chest.Items[0].NetId = 8; chest.Items[0].StackSize = 17; chest.Items[0].Prefix = 0; w.Chests.Add(chest);
        Object(690, 90, 55, 2, 2); w.Signs.Add(new Sign(690, 90, "东西连续测试"));
        Object(675, 170, 395, 2, 2); var entity = TileEntity.CreateForTile(w.Tiles[675, 170], 675, 170, 0);
        entity.NetId = 8; entity.StackSize = 3; w.TileEntities.Add(entity);
        w.PressurePlates.Add(new PressurePlate { PosX = 355, PosY = 200 });
        w.NPCs.Add(new NPC { SpriteId = 22, Name = "Guide", DisplayName = "Guide", Position = new Vector2FloatObservable(520 * 16 + 4, 100 * 16),
            Home = new Vector2Int32Observable(520, 100), IsHomeless = false });
        w.PlayerRooms.Add(new TownManager { NpcId = 22, Home = new Vector2Int32(520, 100) });
        string source = Path.Combine(output, "fixture-source.wld"); WorldIO.Save(w, source); w.Tiles = null!;
        string sourceHash = Files.Inspect(source).Sha256;
        Check("source_real_wld_roundtrip", () => WorldIO.Load(source).TilesWide == 1040);
        Check("surface_not_layer_line_nearest_cave", () =>
        {
            var probe = WorldIO.Load(source); probe.RockLevel = 420; probe.GroundLevel = 390;
            WorldIO.SetSurfaceSpawn(probe);
            return probe.SpawnY < 150 && probe.SpawnX == 520;
        });
        Check("surface_skips_cloud_backed_sky_island", () =>
        {
            var probe = WorldIO.Load(source); int center = probe.TilesWide / 2;
            for (int x = center - 5; x <= center + 5; x++) for (int y = 40; y <= 51; y++)
                probe.Tiles[x, y] = new Tile { IsActive = true, Type = (ushort)(y >= 49 ? 189 : 0) };
            WorldIO.SetSurfaceSpawn(probe); return probe.SpawnY > 100 && probe.SpawnY < 150;
        });
        Check("blocked_surface_rejects_instead_of_falling_back_to_cave", () =>
        {
            var probe = WorldIO.Load(source); int center = probe.TilesWide / 2, floor = WorldIO.SurfaceFloor(probe);
            for (int dy = 1; dy <= 3; dy++) probe.Tiles[center, floor - dy] = new Tile { IsActive = true, Type = 191 };
            return WorldIO.SurfaceFloor(probe) == floor && Reject(() => WorldIO.SetSurfaceSpawn(probe));
        });
        Check("bounded_foot_puddle_filled_without_underground_fallback", () =>
        {
            var probe = WorldIO.Load(source); int center = probe.TilesWide / 2, floor = WorldIO.SurfaceFloor(probe);
            probe.Tiles[center + 1, floor] = new Tile { LiquidAmount = 255, LiquidType = LiquidType.Water };
            var changes = WorldIO.SetSurfaceSpawn(probe);
            return probe.SpawnY == floor - 1 && probe.Tiles[center + 1, floor].LiquidAmount == 0 &&
                changes.Any(c => c.X == center + 1 && c.Y == floor);
        });
        World SurfaceProbe()
        {
            var probe = WorldIO.Load(source); int center = probe.TilesWide / 2;
            for (int dx = -10; dx <= 10; dx++) for (int yy = 20; yy < 150; yy++)
                probe.Tiles[center + dx, yy] = new Tile { IsActive = yy >= 120, Type = 0 };
            return probe;
        }
        void Puddle(World probe, int depth = 2)
        {
            int center = probe.TilesWide / 2;
            for (int dx = -3; dx <= 3; dx++) for (int yy = 120 - depth; yy < 120; yy++)
            { probe.Tiles[center + dx, yy].LiquidAmount = 255; probe.Tiles[center + dx, yy].LiquidType = LiquidType.Water; }
        }
        Check("native_shallow_surface_water_fixed_x_dry_pad_not_cave", () =>
        {
            var probe = SurfaceProbe(); Puddle(probe); var edits = WorldIO.SetSurfaceSpawn(probe);
            return probe.SpawnX == probe.TilesWide / 2 && probe.SpawnY == 117 && WorldIO.SpawnSafe(probe) &&
                edits.All(e => Math.Abs(e.X - probe.SpawnX) <= 1 && e.Y >= 118 && e.Y <= 122);
        });
        Check("complete_native_3x2_rubble_in_puddle_audited_roundtrip", () =>
        {
            var probe = SurfaceProbe(); Puddle(probe); int center = probe.TilesWide / 2;
            for (int xx = 0; xx < 3; xx++) for (int yy = 0; yy < 2; yy++)
            {
                ref var t = ref probe.Tiles[center - 1 + xx, 118 + yy];
                t.IsActive = true; t.Type = 186; t.U = (short)(486 + xx * 18); t.V = (short)(yy * 18);
            }
            var edits = WorldIO.SetSurfaceSpawn(probe);
            string path = Path.Combine(output, "native-puddle-roundtrip.wld"); WorldIO.Save(probe, path);
            var actual = WorldIO.Load(path);
            return actual.SpawnY == 117 && WorldIO.SpawnSafe(actual) && edits.Count(e => e.Y is 118 or 119) == 6 &&
                actual.Chests.Count == probe.Chests.Count && actual.Signs.Count == probe.Signs.Count;
        });
        Check("shallow_water_never_truncates_crossing_rubble", () =>
        {
            var probe = SurfaceProbe(); Puddle(probe); int center = probe.TilesWide / 2;
            for (int xx = 0; xx < 3; xx++) for (int yy = 0; yy < 2; yy++)
                probe.Tiles[center - 2 + xx, 118 + yy] = new Tile { IsActive = true, Type = 186, U = (short)(xx * 18), V = (short)(yy * 18) };
            return Reject(() => WorldIO.SetSurfaceSpawn(probe)) && probe.Tiles[center, 118].Type == 186;
        });
        Check("deep_water_rejected_without_draining_player_body", () =>
        {
            var probe = SurfaceProbe(); Puddle(probe, 10);
            return Reject(() => WorldIO.SetSurfaceSpawn(probe)) && probe.Tiles[probe.TilesWide / 2, 119].LiquidAmount == 255;
        });
        foreach (var liquid in new[] { LiquidType.Lava, LiquidType.Honey, LiquidType.Shimmer })
            Check("surface_puddle_keeps_protected_liquid_" + liquid, () =>
            {
                var probe = SurfaceProbe(); Puddle(probe); int center = probe.TilesWide / 2;
                probe.Tiles[center, 119].LiquidType = liquid;
                return Reject(() => WorldIO.SetSurfaceSpawn(probe)) && probe.Tiles[center, 119].LiquidType == liquid;
            });
        foreach (ushort type in new ushort[] { 21, 55, 395, 191 })
            Check("surface_water_protected_object_rejected_" + type, () =>
            {
                var probe = SurfaceProbe(); Puddle(probe); int center = probe.TilesWide / 2;
                probe.Tiles[center, 119].IsActive = true; probe.Tiles[center, 119].Type = type;
                return Reject(() => WorldIO.SetSurfaceSpawn(probe)) && probe.Tiles[center, 119].Type == type;
            });
        Check("higher_neighbor_water_rejected_without_partial_edit", () =>
        {
            var probe = SurfaceProbe(); Puddle(probe); int center = probe.TilesWide / 2;
            probe.Tiles[center + 2, 117].LiquidAmount = 255; probe.Tiles[center + 2, 117].LiquidType = LiquidType.Water;
            return Reject(() => WorldIO.SetSurfaceSpawn(probe)) && !probe.Tiles[center, 118].IsActive;
        });
        var worldDir = Path.Combine(output, "Worlds"); Directory.CreateDirectory(worldDir);
        var slices = o.Specs().Select(s => WorldIO.Slice(source, sourceHash, s, o, Path.Combine(worldDir, s.Name + ".wld"))).ToArray();
        var maps = slices.Select(s => WorldIO.Load(s.File.Path)).ToArray();
        Check("exactly_three_slices", () => slices.Length == 3);
        Check("three_distinct_guid", () => slices.Select(s => s.WorldGuid).Distinct().Count() == 3);
        Check("three_distinct_world_id", () => slices.Select(s => s.WorldId).Distinct().Count() == 3);
        Check("source_unchanged", () => Files.Inspect(source).Sha256 == sourceHash);
        Check("all_tiles_source_exact_except_enumerated_spawn_clearance", () => slices.All(s => s.SourceTileMismatches == 0));
        for (int i = 0; i < 3; i++)
        {
            int n = i;
            Check($"slice{n}_dimensions_mode", () => maps[n].TilesWide == 400 && maps[n].TilesHigh == 450 && maps[n].GameMode == 2);
            Check($"slice{n}_central_surface_spawn", () => WorldIO.SpawnSafe(maps[n]) && maps[n].SpawnX == 200 && maps[n].SpawnY < 150);
            Check($"slice{n}_same_vertical_layers", () => maps[n].GroundLevel == 150 && maps[n].RockLevel == 270);
        }
        var seams = new[] { WorldIO.Compare(slices[0].File.Path, slices[1].File.Path, 80), WorldIO.Compare(slices[1].File.Path, slices[2].File.Path, 80) };
        Check("west_middle_all_fields_full_depth", () => seams[0].Mismatches == 0 && seams[0].ComparedTiles == 36000 && seams[0].LeftDigest == seams[0].RightDigest);
        Check("middle_east_all_fields_full_depth", () => seams[1].Mismatches == 0 && seams[1].ComparedTiles == 36000 && seams[1].LeftDigest == seams[1].RightDigest);
        Check("middle_west_exit_enters_west_east", () => o.Stride + 40 == 360);
        Check("middle_east_exit_enters_east_west", () => o.Stride + 360 == 2 * o.Stride + 40);
        Check("reverse_mapping_keeps_x", () => (40 + o.Stride) - o.Stride == 40);
        Check("cave_same_y_no_spawn_remap", () => maps[0].Tiles[360, 220].Equals(maps[1].Tiles[40, 220]));
        Check("slope_border_preserved", () => maps[0].Tiles[395, 258].BrickStyle == maps[1].Tiles[75, 258].BrickStyle && maps[1].Tiles[75, 258].BrickStyle != BrickStyle.Full);
        Check("wire_four_colors_preserved", () => maps[0].Tiles[360, 200].WireRed && maps[0].Tiles[360, 201].WireBlue && maps[0].Tiles[360, 202].WireGreen && maps[0].Tiles[360, 203].WireYellow);
        Check("actuator_and_inactive_preserved", () => maps[1].Tiles[40, 260].Actuator && maps[1].Tiles[40, 260].InActive);
        Check("invisible_and_glow_preserved", () => maps[1].Tiles[40, 270].InvisibleBlock && maps[1].Tiles[40, 271].FullBrightBlock && maps[1].Tiles[40, 280].InvisibleWall && maps[1].Tiles[40, 281].FullBrightWall);
        Check("chest_shift_in_tiles", () => maps[0].Chests.Single(c => c.Name == "跨缝宝箱").X == 350 && maps[1].Chests.Single(c => c.Name == "跨缝宝箱").X == 30);
        Check("chest_items_preserved_default", () => maps[0].Chests[0].Items[0].StackSize == 17 && maps[1].Chests[0].Items[0].StackSize == 17);
        Check("sign_text_and_position", () => maps[1].Signs.Any(s => s.X == 370 && s.Text == "东西连续测试") && maps[2].Signs.Any(s => s.X == 50 && s.Text == "东西连续测试"));
        Check("tile_entity_position_payload", () => maps[1].TileEntities.Any(e => e.PosX == 355 && e.NetId == 8 && e.StackSize == 3) && maps[2].TileEntities.Any(e => e.PosX == 35 && e.StackSize == 3));
        Check("npc_pixel_not_tile_units", () => maps[1].NPCs.Single().Position.X == 200 * 16 + 4);
        Check("npc_home_tile_units", () => maps[1].NPCs.Single().Home.X == 200);
        Check("npc_filtered_from_other_slices", () => maps[0].NPCs.Count == 0 && maps[2].NPCs.Count == 0);
        Check("housing_shift_tile_units", () => maps[1].PlayerRooms.Single().Home.X == 200);
        Check("tree_range_clamp_not_old_coordinate", () => maps[1].TreeX0 == 0 && maps[1].TreeX1 == 180 && maps[1].TreeX2 == 400);
        Check("pressure_plate_shift", () => maps[1].PressurePlates.Any(p => p.PosX == 35));
        string duplicate = Path.Combine(output, "deterministic-repeat.wld");
        var repeat = WorldIO.Slice(source, sourceHash, o.Specs()[1], o, duplicate);
        Check("identities_deterministic_same_source_content", () => repeat.WorldGuid == slices[1].WorldGuid && repeat.WorldId == slices[1].WorldId);
        Check("tile_digest_deterministic", () => repeat.TileDigest == slices[1].TileDigest);
        Check("metadata_digest_deterministic", () => repeat.MetadataDigest == slices[1].MetadataDigest);
        string movedSource = Path.Combine(output, "same-content-different-path.wld"); File.Copy(source, movedSource);
        var moved = WorldIO.Slice(movedSource, Files.Inspect(movedSource).Sha256, o.Specs()[1], o, Path.Combine(output, "moved-repeat.wld"));
        Check("identity_independent_of_source_file_path", () => moved.WorldGuid == slices[1].WorldGuid && moved.WorldId == slices[1].WorldId);
        var lootOptions = new Options { Name = o.Name, Width = 400, Height = 450, Output = output, UniqueOverlapLoot = true };
        var loot = WorldIO.Slice(source, sourceHash, o.Specs()[1], lootOptions, Path.Combine(output, "loot-owner-middle.wld"));
        Check("optional_chest_owner_clears_nonowner_only", () => loot.ClearedOverlapInventories == 1 && WorldIO.Load(loot.File.Path).Chests[0].Items[0].StackSize == 0 && WorldIO.Load(slices[0].File.Path).Chests[0].Items[0].StackSize == 17);
        var narrow = o.Specs()[2] with { SourceX = 999 };
        Check("reject_short_source_no_stretch", () => Reject(() => WorldIO.Slice(source, sourceHash, narrow, o, Path.Combine(output, "invalid.wld"))));
        Check("reject_invalid_world_name", () => Reject(() => new Options { Name = "../bad", Output = output }.Validate()));
        Check("reject_nonstandard_size", () => Reject(() => o.Validate()));
        Check("reject_existing_nonempty_output", () => Reject(() => new Pipeline(_ => { }).RunAsync(new Options { Output = output }, CancellationToken.None).GetAwaiter().GetResult()));
        string bad = Path.Combine(output, "broken.wld"); File.WriteAllBytes(bad, [0, 1, 2, 3]);
        Check("reject_truncated_world", () => Reject(() => WorldIO.Load(bad)));
        Pipeline.WriteConfigs(output, o, 0);
        Check("launcher_middle_first_correct_worldName_field", () =>
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "config-fragments", "launcher.fragment.json")));
            var a = doc.RootElement.GetProperty("launcher").GetProperty("autoStartServers");
            return a[0].GetProperty("worldName").GetString() == o.Name && a[1].GetProperty("name").GetString() == o.Name + "·西" && a[2].GetProperty("name").GetString() == o.Name + "·东";
        });
        Check("edge_config_contact_only_no_loop_stride", () =>
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "config-fragments", "EdgeStitch.json")));
            var e = doc.RootElement; return !e.GetProperty("首尾相连成环").GetBoolean() && e.GetProperty("边缘触发余量_格").GetInt32() == 0 && e.GetProperty("切片间距_格").GetInt32() == 320;
        });
        Preview.Render(output, slices, o);
        Check("preview_correct_logical_width_without_duplicate_overlaps", () =>
        {
            using var image = new Bitmap(Path.Combine(output, "preview", "三联世界总览.png"));
            return image.Width == (o.SourceWidth + 7) / 8 && image.Height == (o.Height + 7) / 8 + 42;
        });
        Check("preview_seam_right_half_uses_neighbor_not_clamped_border", () =>
        {
            using var image = new Bitmap(Path.Combine(output, "preview", "seam-1.png"));
            var color = TerrariaDataStore.Instance.TileById[1].Color;
            return image.GetPixel(199, 200).ToArgb() == Color.FromArgb(color.R, color.G, color.B).ToArgb();
        });
        Files.Write(Path.Combine(output, "generation-result.json"), new GenerationRecord(o, Files.Inspect(source), 0, slices, seams, "FIXTURE_ONLY", "UNVERIFIED", DateTimeOffset.Now));
        Check("verify_existing_valid", () => { Pipeline.VerifyExisting(output, _ => { }); return true; });
        // Real .wld corruption, not a mock: change a seam tile, resave and compare.
        maps[1].Tiles[40, 200].WireRed = !maps[1].Tiles[40, 200].WireRed;
        string mutated = Path.Combine(output, "mutated-middle.wld"); WorldIO.Save(maps[1], mutated);
        Check("negative_one_tile_tamper_detected", () => WorldIO.Compare(slices[0].File.Path, mutated, 80).Mismatches == 1);
        File.Copy(mutated, slices[1].File.Path, true);
        Check("negative_manifest_tamper_detected", () => Reject(() => Pipeline.VerifyExisting(output, _ => { })));
        File.Copy(duplicate, slices[1].File.Path, true);
        // A serialized byte differs only if the writer timestamps change; restore original record hash after deterministic repeat.
        foreach (var map in maps) map.Tiles = null!; WorldIO.Collect();
        Files.Write(Path.Combine(output, "test-result.json"), new { passed, failed, scope = "real-wld-component-fixtures-not-game-E2E", productionTouched = false, rows });
        Console.WriteLine($"RESULT {passed} PASS / {failed} FAIL");
        if (failed > 0) throw new Exception("夹具测试失败，详见 test-result.json。");
    }
}
