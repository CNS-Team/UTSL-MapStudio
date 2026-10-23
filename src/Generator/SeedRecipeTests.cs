using System.Text.Json;
using ThreeWorlds.Live;
namespace ThreeWorlds;
internal static class SeedRecipeTests
{
    internal static void Run(string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("不覆盖旧测试证据。");
        Directory.CreateDirectory(output); var checks = new List<object>(); int passed = 0, failed = 0;
        void Check(string name, Action body) { try { body(); checks.Add(new { name, pass = true }); passed++; } catch (Exception ex) { checks.Add(new { name, pass = false, error = ex.ToString() }); failed++; } }
        void Assert(bool b) { if (!b) throw new Exception("Assertion failed"); }
        bool Reject(Action body) { try { body(); return false; } catch (ArgumentException) { return true; } }
        Check("catalog_real_engine9_and37", () => Assert(SeedCatalog.Current.NativeSeeds.Length == 9 && SeedCatalog.Current.Traits.Length == 37));
        Check("catalog_matches_actual_bundled_engine", () => Assert(Files.Inspect(Path.Combine(Program.ToolRoot, "engine", "OTAPI.dll")).Sha256 == SeedCatalog.Current.EngineSha256));
        foreach (var s in SeedCatalog.Current.NativeSeeds) Check("native_name_" + s.Id, () => new Options { NativeSeeds = [s.Id], Output = output }.Validate());
        foreach (var s in SeedCatalog.Current.Traits) Check("trait_name_" + s.Id, () => new Options { SeedTraits = [s.Id], Output = output }.Validate());
        foreach (var r in SeedCatalog.Recipes) Check("recipe_" + r.Name, () => new Options { NativeSeeds = r.NativeSeeds, SeedTraits = r.SeedTraits, Output = output }.Validate());
        Check("recipe_arrays_roundtrip_actual_options", () => {
            var o = new Options { Name = "配方", Seed = "seed123", NativeSeeds = ["drunk"], SeedTraits = ["extraLivingTrees", "dualDungeons"], Output = output };
            var reloaded = JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(o, Files.Json), Files.Json)!;
            Assert(reloaded.NativeSeeds.SequenceEqual(o.NativeSeeds) && reloaded.SeedTraits.SequenceEqual(o.SeedTraits) && reloaded.Seed == o.Seed && reloaded.Difficulty == o.Difficulty);
        });
        Check("unknown_native_rejected", () => Assert(Reject(() => new Options { NativeSeeds = ["fake"], Output = output }.Validate())));
        Check("unknown_trait_rejected", () => Assert(Reject(() => new Options { SeedTraits = ["fake"], Output = output }.Validate())));
        foreach (string[] pair in new[] { new[] { "moreLightning", "noLightning" }, new[] { "noInfection", "worldIsInfected" } })
            Check("conflict_" + pair[0], () => Assert(Reject(() => new Options { SeedTraits = pair, Output = output }.Validate())));
        Check("style_moretraps_vs_actual_notraps_rejected", () => Assert(Reject(() => new Options { Style = "notraps", SeedTraits = ["actuallyNoTraps"], Output = output }.Validate())));
        Check("native_moretraps_vs_actual_notraps_rejected", () => Assert(Reject(() => new Options { NativeSeeds = ["notraps"], SeedTraits = ["actuallyNoTraps"], Output = output }.Validate())));
        Check("normal_stitch_rejects_special_traits", () => Assert(Reject(() => new Options { WorldLayout = "normal-stitch", SeedTraits = ["extraLiquid"], Output = output }.Validate())));
        Check("normal_stitch_rejects_special_native", () => Assert(Reject(() => new Options { WorldLayout = "normal-stitch", NativeSeeds = ["remix"], Output = output }.Validate())));
        Files.Write(Path.Combine(output, "result.json"), new { passed, failed, checks, product = Files.Inspect(Path.Combine(AppContext.BaseDirectory, "UTSL.ThreeWorlds.dll")), scope = "catalog and serialized request validation; not native generation or gameplay", productionTouched = false });
        if (failed != 0) throw new InvalidDataException("种子配方测试失败。");
    }
}
