using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Terraria.Localization;
using UnifierTSL.Servers;

internal static class NativeSeeds
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static void Write(string name, object value) => File.WriteAllText(name, JsonSerializer.Serialize(value, Json));
    internal static void Apply(ServerContext world, string[] seeds, string[] traits)
    {
        ThreeWorlds.Live.SeedCatalog.Validate(seeds, traits);
        // Validate the whole recipe before mutating even this isolated process's options.
        var options = world.WorldFileData.seedOptionsInOrder.Where(o => o.ServerConfigName != "normal").ToDictionary(o => o.ServerConfigName, StringComparer.Ordinal);
        var hidden = world.WorldGen.SecretSeed.AllSecretSeeds.ToDictionary(s => s.Localization.Split('.').Last(), StringComparer.Ordinal);
        foreach (string name in seeds) if (!options.ContainsKey(name)) throw new ArgumentException("当前原生引擎不支持种子选项：" + name);
        foreach (string name in traits) if (!hidden.ContainsKey(name)) throw new ArgumentException("当前原生引擎不支持种子特性：" + name);
        if (traits.Contains("moreLightning") && traits.Contains("noLightning")) throw new ArgumentException("更多闪电与无闪电不能同时选择。");
        foreach (string name in seeds) options[name].Enabled = true;
        foreach (string name in traits) world.WorldGen.SecretSeed.Enable(hidden[name], false);
    }
    internal static void Export(ServerContext world)
    {
        string engine = typeof(Terraria.Main).Assembly.Location;
        var version = FileVersionInfo.GetVersionInfo(engine);
        Write("native-seed-catalog.json", new {
            schemaVersion = 1, engineVersion = version.FileVersion, engineSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(engine))),
            capturedAt = DateTimeOffset.Now, source = "actual bundled OTAPI / world instance, not web guesses", networkStarted = false, pluginsLoaded = false,
            nativeSeeds = world.WorldFileData.seedOptionsInOrder.Select(o => new { id = o.ServerConfigName, title = o.Title?.Value ?? Language.GetTextValue("UI." + o.KeyName + "_Title"), description = o.Description?.Value ?? Language.GetTextValue("UI." + o.KeyName), aliases = o.SpecialSeedNames, numericAliases = o.SpecialSeedValues }),
            traits = world.WorldGen.SecretSeed.AllSecretSeeds.Select(s => new { id = s.Localization.Split('.').Last(), description = Language.GetTextValue(s.Localization), localization = s.Localization })
        });
    }
    internal static void Test(ServerContext world)
    {
        var checks = new List<object>(); int passed = 0, failed = 0;
        void Check(string name, Action test) { try { Reset(); test(); checks.Add(new { name, pass = true }); passed++; } catch (Exception ex) { checks.Add(new { name, pass = false, error = ex.ToString() }); failed++; } }
        void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
        void Reset() { foreach (var o in world.WorldFileData.seedOptionsInOrder) o.Enabled = false; world.WorldGen.SecretSeed.ClearAllSeeds(); }
        foreach (var option in world.WorldFileData.seedOptionsInOrder.Where(o => o.ServerConfigName != "normal"))
            Check("native_" + option.ServerConfigName, () => { Apply(world, [option.ServerConfigName], []); Assert(option.Enabled); });
        foreach (var trait in world.WorldGen.SecretSeed.AllSecretSeeds)
            Check("trait_" + trait.Localization.Split('.').Last(), () => { Apply(world, [], [trait.Localization.Split('.').Last()]); Assert(trait.Enabled && world.WorldGen.SecretSeed.activeSecretSeedCount == 1); Apply(world, [], [trait.Localization.Split('.').Last()]); Assert(world.WorldGen.SecretSeed.activeSecretSeedCount == 1); });
        Check("combined_native_and_traits", () => { var pair = world.WorldFileData.seedOptionsInOrder.Where(o => o.ServerConfigName != "normal").Take(2).ToArray(); Apply(world, pair.Select(o => o.ServerConfigName).ToArray(), ["roundLandmasses", "extraLiquid"]); Assert(pair.All(o => o.Enabled) && world.WorldGen.SecretSeed.roundLandmasses.Enabled && world.WorldGen.SecretSeed.extraLiquid.Enabled); });
        Check("unknown_native_rejected", () => { try { Apply(world, ["NOT_A_NATIVE_SEED"], []); throw new Exception("not rejected"); } catch (ArgumentException) { } });
        Check("unknown_trait_rejected_without_partial_enable", () => { try { Apply(world, [], ["extraLivingTrees", "NOT_A_SEED_TRAIT"]); throw new Exception("not rejected"); } catch (ArgumentException) { Assert(!world.WorldGen.SecretSeed.extraLivingTrees.Enabled); } });
        Check("contradictory_weather_rejected", () => { try { Apply(world, [], ["moreLightning", "noLightning"]); throw new Exception("not rejected"); } catch (ArgumentException) { Assert(!world.WorldGen.SecretSeed.moreLightning.Enabled && !world.WorldGen.SecretSeed.noLightning.Enabled); } });
        foreach (var recipe in ThreeWorlds.Live.SeedCatalog.Recipes)
            Check("recipe_" + recipe.Name, () => {
                Apply(world, recipe.NativeSeeds, recipe.SeedTraits);
                Assert(recipe.NativeSeeds.All(id => world.WorldFileData.seedOptionsInOrder.Single(s=>s.ServerConfigName==id).Enabled));
                Assert(recipe.SeedTraits.All(id => world.WorldGen.SecretSeed.AllSecretSeeds.Single(s=>s.Localization.EndsWith("."+id,StringComparison.Ordinal)).Enabled));
            });
        Reset();
        Write("seed-selection-tests.json", new { passed, failed, checks, scope = "real native seed option setters and validation, not finished worlds or gameplay", networkStarted = false, pluginsLoaded = false });
        if (failed != 0) throw new InvalidDataException("原生种子选项测试失败。");
    }
}
