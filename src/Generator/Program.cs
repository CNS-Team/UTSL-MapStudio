using System.Text;

namespace ThreeWorlds;

internal static class Program
{
    internal static string ToolRoot => Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
    internal static string PackageRoot => AppPaths.PackageRoot(ToolRoot);
    internal static string MapsRoot => AppPaths.MapsRoot(ToolRoot);

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length == 0 || args[0] == "gui")
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new MainForm());
                return 0;
            }
            if (args[0] == "ui-test")
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new MainForm(Files.Read<UiTestPlan>(args[1])));
                return 0;
            }
            if (args[0] == "ui-layout-test")
            {
                ApplicationConfiguration.Initialize();
                using var form = new MainForm();
                form.Shown += async (_, _) => { await Task.Delay(250); form.AuditLayout(args[1], args.Length > 2 ? args[2] : null); form.Close(); };
                Application.Run(form);
                return Files.Read<System.Text.Json.JsonElement>(Path.Combine(args[1], "result.json")).GetProperty("failed").GetInt32() == 0 ? 0 : 1;
            }
            WorldIO.Initialize();
            switch (args[0])
            {
                case "ui-protocol-probe":
                    Console.WriteLine("UI_PREVIEW " + System.Text.Json.JsonSerializer.Serialize(new { directory = args[1], mother = false }, Files.Json).Replace("\r", "").Replace("\n", ""));
                    return 0;
                case "seed-recipe-test":
                    SeedRecipeTests.Run(args[1]); return 0;
                case "live-test":
                    LiveTests.Run(args[1]); return 0;
                case "biome-inspect":
                    Files.Write(args[2], FoundationWorlds.Scan(WorldIO.Load(args[1]))); return 0;
                case "foundation-test":
                    FoundationTests.Run(args[1]); return 0;
                case "normal-stitch-test":
                    NormalWorldTests.Run(args[1]); return 0;
                case "coast-inspect":
                    var coast = WorldIO.Load(args[1]);
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { coast.Title, coast.TilesWide, coast.TilesHigh,
                        chests = coast.Chests.Where(c => c.X < 90 || c.X + 2 > coast.TilesWide - 90).Select(c => new { c.X, c.Y, c.Name, tile = coast.Tiles[c.X, c.Y].Type }),
                        signs = coast.Signs.Where(c => c.X < 90 || c.X + 2 > coast.TilesWide - 90).Select(c => new { c.X, c.Y, c.Text }),
                        entities = coast.TileEntities.Where(c => c.PosX < 90 || c.PosX > coast.TilesWide - 90).Select(c => new { c.PosX, c.PosY }),
                        plates = coast.PressurePlates.Where(c => c.PosX < 90 || c.PosX > coast.TilesWide - 90).Select(c => new { c.PosX, c.PosY }) }, Files.Json)); return 0;
                case "normal-prepare":
                    var normalOptions = Files.Read<Options>(args[1]);
                    Directory.CreateDirectory(args[2]);
                    Files.Write(Path.Combine(args[2], "result.json"), NormalWorlds.Prepare(args.Skip(3).Take(3).ToArray(), normalOptions, args[2], CancellationToken.None)); return 0;
                case "inspect":
                    var inspected = WorldIO.Load(args[1]);
                    int surfaceFloor = WorldIO.SurfaceFloor(inspected);
                    var cells = (from dx in Enumerable.Range(-1, 3) from dy in Enumerable.Range(-3, 6)
                        let x = inspected.TilesWide / 2 + dx let y = Math.Clamp(surfaceFloor + dy, 0, inspected.TilesHigh - 1)
                        let tile = inspected.Tiles[x, y]
                        select new { x, y, tile.IsActive, tile.Type, tile.LiquidAmount }).ToArray();
                    var ground = Enumerable.Range(20, inspected.TilesHigh - 40)
                        .Where(y => inspected.Tiles[inspected.TilesWide / 2, y].IsActive && inspected.Tiles[inspected.TilesWide / 2, y].Type is 0 or 1 or 2 or 53 or 59 or 147 or 161)
                        .Take(8).Select(y => new { y, type = inspected.Tiles[inspected.TilesWide / 2, y].Type }).ToArray();
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { inspected.Title, inspected.TilesWide, inspected.TilesHigh, inspected.GroundLevel,
                        inspected.RockLevel, inspected.SpawnX, inspected.SpawnY, surfaceFloor, cells, centerFirstGround = ground }, Files.Json));
                    return 0;
                case "ui-preview":
                    ApplicationConfiguration.Initialize();
                    using (var form = new MainForm())
                    using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                        form.Location = new System.Drawing.Point(-20000, -20000);
                        form.Show(); Application.DoEvents(); form.PerformLayout();
                        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                    return 0;
                case "generate":
                    if (args.Length is < 2 or > 3) throw new ArgumentException("generate <options.json> [cancel-file]");
                    CliGeneration.Run(Files.Read<Options>(args[1]), args.Length > 2 ? args[2] : null);
                    return 0;
                case "plan":
                    if (args.Length != 2) throw new ArgumentException("plan <options.json>");
                    var op = Files.Read<Options>(args[1]); op.Validate();
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        op.Name, op.Width, op.Height, op.Difficulty, op.SourceWidth, op.Stride, op.Overlap,
                        defaultWorld = op.Name, chain = op.Specs(), entryOrder = new[] { op.Name, op.Name + "·西", op.Name + "·东" },
                        westBoundary = op.EdgeInset, eastBoundary = op.Width - op.EdgeInset
                    }, Files.Json));
                    return 0;
                case "verify":
                    if (args.Length != 2) throw new ArgumentException("verify <output-directory>");
                    Pipeline.VerifyExisting(args[1], Console.WriteLine);
                    return 0;
                case "self-test":
                    SelfTests.Run(args.Length > 1 ? args[1] : Path.Combine(ToolRoot, "evidence", "self-tests"));
                    return 0;
                default:
                    Console.WriteLine("UTSL 三联世界生成器 1.0\n双击启动界面；命令：generate <options.json> / plan <options.json> / verify <输出目录> / self-test <新目录>");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
