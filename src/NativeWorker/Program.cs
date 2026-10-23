using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text;
using Microsoft.Xna.Framework;
using ReLogic.Utilities;
using Terraria.GameContent.Biomes.Desert;
using Terraria.WorldBuilding;
using ThreeWorlds.Live;

internal static class Program
{
    static string engine = "";
    static WorkerRequest request = new();
    public static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 6) throw new ArgumentException("worker <engine> <new-workdir> <width> <height> <seed> <difficulty> [load-only.wld] [--request=path]");
            engine = Path.GetFullPath(args[0]);
            string work = Path.GetFullPath(args[1]);
            string? requestFile = args.Skip(6).FirstOrDefault(a => a.StartsWith("--request="))?[10..];
            if (requestFile != null) request = JsonSerializer.Deserialize<WorkerRequest>(File.ReadAllText(requestFile)) ?? throw new InvalidDataException("worker request missing");
            _ = StyleCatalog.Get(request.Style);
            if (Directory.Exists(work) && Directory.EnumerateFileSystemEntries(work).Any())
                throw new IOException("生成工作目录必须为空。");
            Directory.CreateDirectory(work);
            Directory.SetCurrentDirectory(work);
            Environment.SetEnvironmentVariable("UTSL_WORLD_PATH", Path.Combine(work, "Worlds"));
            Environment.SetEnvironmentVariable("UTSL_SAVE_PATH", Path.Combine(work, "Saves"));
            Environment.SetEnvironmentVariable("UTSL_CONSOLE_MODE", "headless");
            Environment.SetEnvironmentVariable("UTSL_CUSTOM_HEIGHT", args[3]);
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string candidate = Path.Combine(engine, name.Name + ".dll");
                return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
            };
            Directory.CreateDirectory(Path.Combine(work, "config"));
            Directory.CreateDirectory(Path.Combine(work, "plugins"));
            string? loadFile = args.Skip(6).FirstOrDefault(a => !a.StartsWith("--"));
            if (loadFile != null)
            {
                Directory.CreateDirectory(Path.Combine(work, "Worlds"));
                File.Copy(Path.GetFullPath(loadFile), Path.Combine(work, "Worlds", "SeamSource.wld"));
            }
            File.WriteAllText(Path.Combine(work, "config", "config.json"),
                """{"logging":{"mode":"txt"},"launcher":{"listenPort":29991,"joinServer":"none","colorfulConsoleStatus":false,"autoStartServers":[]}}""");
            return Generate(int.Parse(args[2]), int.Parse(args[3]), args[4], int.Parse(args[5]));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Generate(int width, int height, string seed, int difficulty)
    {
        if (width < 4200 || width > 25200 || height < 1200 || height > 2400 || height % 150 != 0)
            throw new ArgumentException("不支持的世界尺寸。");
        if (difficulty is < 0 or > 3) throw new ArgumentException("难度必须为 0..3。");
        var api = typeof(UnifierTSL.UnifierApi);
        static void Call(Type api, string name, params object[] parameters) =>
            (api.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic) ?? throw new MissingMethodException(name))
                .Invoke(null, parameters);
        void Stage(string message) => File.AppendAllText("worker-stage.log", $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        Stage("bootstrap-begin");
        UnifierTSL.Initializer.InitializeResolver();
        Directory.CreateDirectory(Environment.GetEnvironmentVariable("UTSL_SAVE_PATH")!);
        Terraria.Program.SavePath = Environment.GetEnvironmentVariable("UTSL_SAVE_PATH");
        // Prevent ApplySavePathOverride from importing unrelated personal worlds.
        typeof(Terraria.Main).GetField("WorldPath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(null, Environment.GetEnvironmentVariable("UTSL_WORLD_PATH"));
        var surface = api.Assembly.GetType("UnifierTSL.Surface.Hosting.LauncherSurfaceConsole", true)!;
        surface.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [new UnifierTSL.Surface.Adapter.Cli.Terminal.TerminalLauncherSurfaceHost()]);
        Call(api, "HandleCommandLinePreRun", (object)new[] { "-lang", "7" });
        Call(api, "PrepareRuntime", (object)Array.Empty<string>());
        UnifierTSL.Initializer.Initialize();
        Call(api, "InitializeCore");
        Stage("core-initialized-offline-no-coordinator-launch");
        // Process-local, order-equivalent explicit DFS. The native recursive cave
        // counter overflowed the thread-pool stack in Buried Chests on an actual
        // ultra-wide mother world. No shared/core assembly is modified.
        if (!request.FloodProbe) On.Terraria.WorldGenSystemContext.nextCount += (_, ctx, x, y, jungle, lavaOk) =>
            NativeFloodCount.Count(ctx, x, y, jungle, lavaOk);
        On.Terraria.WorldGenSystemContext.GenerateWorld += (orig, ctx, p, controller) =>
        {
            ApplySeedOptions((UnifierTSL.Servers.ServerContext)ctx.root, seed, request.Style, width, height, difficulty);
            return orig(ctx, p, controller);
        };
        LiveSampler? sampler = null;
        On.Terraria.WorldBuilding.GenPass.Apply += (orig, pass, root, p, config) =>
        {
            sampler?.Enter(pass.Name, p);
            orig(pass, root, p, config);
        };
        // Vanilla scales desert depth by total world width. A 25k-wide mother world
        // would make a >2400-tile-tall hive. Bound that *biome scale*, not world data.
        // This hook is process-local, used only for oversized offline generation.
        On.Terraria.GameContent.Biomes.Desert.DesertDescription.CreateFromPlacement += (orig, root, origin) =>
        {
            double nativeScale = root.Main.maxTilesX / 4200d;
            double scale = Math.Min(nativeScale, root.Main.maxTilesY / 1200d);
            if (scale >= nativeScale) return orig(root, origin);
            int columns = (int)(80 * scale);
            int rows = (int)((Terraria.WorldGen.genRand.NextDouble() * 0.5 + 1.5) * 170 * scale);
            if (root.WorldGen.remixWorldGen) rows = (int)(340 * scale);
            var block = DesertDescription.DefaultBlockScale;
            int biomeWidth = (int)(block.X * columns), biomeHeight = (int)(block.Y * rows);
            origin.X -= biomeWidth / 2;
            var surfaceMap = SurfaceMap.FromArea(root, origin.X - 5, biomeWidth + 10);
            if (DesertDescription.RowHasInvalidTiles(root, origin.X, surfaceMap.Bottom, biomeWidth)) return DesertDescription.Invalid;
            int surfaceY = (int)(surfaceMap.Average + surfaceMap.Bottom) / 2;
            origin.Y = surfaceY + Terraria.WorldGen.genRand.Next(40, 60);
            int anniversary = root.Main.tenthAnniversaryWorld ? (int)(20 * scale) : 0;
            Stage($"bounded-desert nativeScale={nativeScale:F3} effectiveScale={scale:F3} hive={biomeWidth}x{biomeHeight}");
            return new DesertDescription
            {
                CombinedArea = new Rectangle(origin.X, surfaceY, biomeWidth, origin.Y + biomeHeight - surfaceY),
                Hive = new Rectangle(origin.X, origin.Y + anniversary, biomeWidth, biomeHeight - anniversary),
                Desert = new Rectangle(origin.X, surfaceY, biomeWidth, origin.Y + biomeHeight / 2 - surfaceY + anniversary),
                BlockScale = block, BlockColumnCount = columns, BlockRowCount = rows, Surface = surfaceMap, IsValid = true
            };
        };
        // The offline generator stops at StartServer entry, BEFORE native networking.
        // It never calls UnifiedServerCoordinator.Launch or loads server plugins.
        On.Terraria.NetplaySystemContext.StartServer += (orig, ctx) =>
        {
            var server = (UnifierTSL.Servers.ServerContext)ctx.root;
            Stage("startserver-intercepted-before-network");
            server.WorldFile.SaveWorld(resetTime: false, useTemps: false);
            sampler?.Finish();
            string file = server.Main.worldPathName;
            File.WriteAllText("native-result.json", JsonSerializer.Serialize(new
            {
                completed = true, path = file, width = server.Main.maxTilesX, height = server.Main.maxTilesY,
                difficulty = server.Main.GameMode, seed = server.Main.ActiveWorldFileData.SeedText,
                spawnX = server.Main.spawnTileX, spawnY = server.Main.spawnTileY,
                style = request.Style, inputSeed = seed, server.Main.skyblockWorld, server.Main.zenithWorld,
                server.Main.remixWorld, server.Main.drunkWorld, server.Main.notTheBeesWorld,
                networkStarted = false, pluginsLoaded = false, pid = Environment.ProcessId,
                generatorPolicy = "native-generation; height-bounded-desert; order-equivalent-iterative-nextCount; offline-process-only",
                at = DateTimeOffset.Now
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("NATIVE_WORLD_SAVED " + file);
            Environment.Exit(0);
        };
        var provider = UnifierTSL.Servers.IWorldDataProvider.GenerateOrLoadExisting("SeamSource", width, difficulty, request.Evil, seed);
        var world = new UnifierTSL.Servers.ServerContext("SeamSource", provider);
        world.Netplay.UseUPNP = false;
        if (request.FloodProbe) return NativeFloodTests.Run(world);
        if (request.ProbeOnly)
        {
            provider.ApplyMetadata(world);
            ApplySeedOptions(world, seed, request.Style, width, height, difficulty);
            NativeSeeds.Export(world);
            if (request.SeedTests) NativeSeeds.Test(world);
            File.WriteAllText("seed-probe.json", JsonSerializer.Serialize(new {
                request.Style, seed, world.Main.maxTilesX, world.Main.maxTilesY, world.Main.GameMode,
                flags = world.WorldFileData.seedOptionsInOrder.Select(v => new { v.ServerConfigName, v.Enabled }),
                roundLandmasses = world.WorldGen.SecretSeed.roundLandmasses.Enabled,
                extraLiquid = world.WorldGen.SecretSeed.extraLiquid.Enabled, networkStarted = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        using var live = sampler = new LiveSampler(world, request);
        Stage("native-generation-or-load-begin");
        world.RunBlocking(["-lang", "7"]);
        throw new InvalidOperationException("原生生成器未提交完成结果。");
    }

    static void ApplySeedOptions(UnifierTSL.Servers.ServerContext world, string seed, string style, int width, int height, int difficulty)
    {
        SeedCatalog.ValidateForStyle(style, request.NativeSeeds ?? [], request.SeedTraits ?? []);
        // Let the real native parser handle full copied codes and custom secret tokens.
        bool copied = world.WorldFileData.TryApplyingCopiedSeed(seed, false, out string processed, out _, out _);
        string plain = copied ? processed : seed;
        string normalized = System.Text.RegularExpressions.Regex.Replace(plain.ToLowerInvariant(), "[^a-z0-9]", "");
        foreach (var option in world.WorldFileData.seedOptionsInOrder)
            if (option.SpecialSeedNames.Any(s => s == normalized) ||
                (int.TryParse(plain, out int number) && option.SpecialSeedValues.Contains(number))) option.Enabled = true;
        if (world.WorldGen.SecretSeed.CheckInputForSecretSeed(plain, out var secret)) world.WorldGen.SecretSeed.Enable(secret, false);
        NativeSeeds.Apply(world, request.NativeSeeds ?? [], request.SeedTraits ?? []);
        switch (style)
        {
            case "skyblock": world.WorldGenerationOptions.Get<WorldSeedOption_Skyblock>().Enabled = true; break;
            case "zenith": world.WorldGenerationOptions.Get<WorldSeedOption_Everything>().Enabled = true; break;
            case "worthy": world.WorldGenerationOptions.Get<WorldSeedOption_ForTheWorthy>().Enabled = true; break;
            case "bees": world.WorldGenerationOptions.Get<WorldSeedOption_NotTheBees>().Enabled = true; break;
            case "drunk": world.WorldGenerationOptions.Get<WorldSeedOption_Drunk>().Enabled = true; break;
            case "remix": world.WorldGenerationOptions.Get<WorldSeedOption_Remix>().Enabled = true; break;
            case "celebration": world.WorldGenerationOptions.Get<WorldSeedOption_Anniversary>().Enabled = true; break;
            case "dontstarve": world.WorldGenerationOptions.Get<WorldSeedOption_DontStarve>().Enabled = true; break;
            case "notraps": world.WorldGenerationOptions.Get<WorldSeedOption_NoTraps>().Enabled = true; break;
            case "archipelago":
                world.WorldGen.SecretSeed.Enable(world.WorldGen.SecretSeed.roundLandmasses, false);
                world.WorldGen.SecretSeed.Enable(world.WorldGen.SecretSeed.extraLiquid, false); break;
        }
        // A copied seed embeds a STANDARD size/difficulty; UI geometry remains authoritative.
        world.Main.maxTilesX = width; world.Main.maxTilesY = height; world.Main.GameMode = difficulty;
        world.Main.ActiveWorldFileData.SetSeed(plain);
        File.WriteAllText("seed-options.json", JsonSerializer.Serialize(new { input = seed, processed = plain, style, copied,
            width, height, difficulty, flags = world.WorldFileData.seedOptionsInOrder.Select(v => new { v.ServerConfigName, v.Enabled }),
            requestedNativeSeeds = request.NativeSeeds ?? [], requestedSeedTraits = request.SeedTraits ?? [],
            traits = world.WorldGen.SecretSeed.AllSecretSeeds.Select(s => new { id = s.Localization.Split('.').Last(), s.Enabled }),
            roundLandmasses = world.WorldGen.SecretSeed.roundLandmasses.Enabled, extraLiquid = world.WorldGen.SecretSeed.extraLiquid.Enabled },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
