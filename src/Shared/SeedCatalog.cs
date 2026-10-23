using System.Text.Json;
namespace ThreeWorlds.Live;

public sealed record NativeSeed(string Id, string? Title, string? Description, string[] Aliases, int[] NumericAliases);
public sealed record SeedTrait(string Id, string Description, string Localization);
public sealed record NativeSeedCatalog(string EngineVersion, string EngineSha256, NativeSeed[] NativeSeeds, SeedTrait[] Traits);
public sealed record SeedRecipe(string Name, string[] NativeSeeds, string[] SeedTraits, string Description);

public static class SeedCatalog
{
    static NativeSeedCatalog Load()
    {
        using var s = typeof(SeedCatalog).Assembly.GetManifestResourceStream("MapStudio.NativeSeedCatalog.json") ?? throw new InvalidDataException("原生种子清单缺失。");
        return JsonSerializer.Deserialize<NativeSeedCatalog>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("原生种子清单损坏。");
    }
    public static NativeSeedCatalog Current { get; } = Load();
    static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal) {
        ["drunk"]="醉酒世界", ["notthebees"]="蜂蜜世界", ["fortheworthy"]="受难 / For the worthy", ["celebration"]="十周年庆典", ["theconstant"]="永恒领域 / 饥荒", ["remix"]="颠倒 / Don't dig up", ["notraps"]="机关 / No traps（更多陷阱）", ["zenith"]="天顶 / Get fixed boi", ["skyblock"]="空岛 / Skyblock",
        ["paintEverythingGray"]="灰色世界", ["paintEverythingNegative"]="反色世界", ["coatEverythingEcho"]="回声涂层", ["coatEverythingIlluminant"]="发光涂层", ["noSurface"]="无地表", ["extraLivingTrees"]="更多巨型生命树", ["extraFloatingIslands"]="更多浮岛", ["errorWorld"]="错误世界", ["graveyardBloodmoonStart"]="墓地与血月开局", ["surfaceIsInSpace"]="高空地表", ["rainsForAYear"]="无尽雨季", ["biggerAbandonedHouses"]="大型地下小屋", ["randomSpawn"]="原生随机出生点", ["addTeleporters"]="散布传送器", ["startInHardmode"]="困难模式开局", ["noInfection"]="开局无腐化猩红", ["hallowOnTheSurface"]="神圣地表", ["worldIsInfected"]="全境腐化 / 猩红", ["surfaceIsMushrooms"]="蘑菇地表", ["surfaceIsDesert"]="沙漠地表", ["pooEverywhere"]="粪便世界", ["noSpiderCaves"]="无蜘蛛洞", ["actuallyNoTraps"]="真正无陷阱", ["rainbowStuff"]="彩虹方块", ["digExtraHoles"]="更多裂缝深坑", ["roundLandmasses"]="小型星体陆块", ["extraLiquid"]="淹水世界", ["portalGunInChests"]="宝箱传送枪", ["worldIsFrozen"]="冰雪世界", ["halloweenGen"]="南瓜开局", ["endlessHalloween"]="永恒万圣节", ["endlessChristmas"]="永恒圣诞节", ["vampirism"]="吸血鬼玩家", ["teamBasedSpawns"]="按队伍出生", ["dualDungeons"]="双地牢", ["moreLightning"]="更多雷击", ["noLightning"]="禁用雷击"
    };
    public static string Label(string id) => Names.TryGetValue(id, out var name) ? name : id;
    public static string Group(string id) => id switch {
        "paintEverythingGray" or "paintEverythingNegative" or "coatEverythingEcho" or "coatEverythingIlluminant" or "rainbowStuff" => "外观与色彩",
        "graveyardBloodmoonStart" or "startInHardmode" or "errorWorld" or "vampirism" or "pooEverywhere" => "挑战与开局",
        "biggerAbandonedHouses" or "dualDungeons" or "portalGunInChests" or "addTeleporters" or "randomSpawn" or "teamBasedSpawns" => "建筑、战利品与出生",
        "rainsForAYear" or "endlessHalloween" or "endlessChristmas" or "halloweenGen" or "moreLightning" or "noLightning" => "天气与节日",
        _ => "地形与生态"
    };
    public static readonly SeedRecipe[] Recipes = [
        new("纯原版（清空附加项）", [], [], "不增加额外选项；仍保留主界面的地图样式和输入种子。"),
        new("空岛探索", ["skyblock"], ["extraFloatingIslands"], "原生空岛加更多浮岛；可在基础环境补全中保留地牢、神庙与微光。"),
        new("星体水世界 · 实验", [], ["roundLandmasses","extraLiquid"], "小型星体陆块与淹水世界组合；不是独立官方种子名。"),
        new("巨树与浮岛", [], ["extraLivingTrees","extraFloatingIslands"], "更多巨型生命树与浮岛。"),
        new("冰雪与极光", [], ["worldIsFrozen","coatEverythingIlluminant"], "冰雪世界加发光涂层；名称是本工具组合预设。"),
        new("蘑菇净土", [], ["surfaceIsMushrooms","noInfection"], "蘑菇地表、开局没有腐化或猩红。"),
        new("沙漠深坑", [], ["surfaceIsDesert","digExtraHoles"], "沙漠地表加额外裂缝与深坑。"),
        new("双地牢探险", [], ["dualDungeons","biggerAbandonedHouses","portalGunInChests"], "双地牢、大型地下小屋与宝箱传送枪。"),
        new("地下受难", ["remix","fortheworthy"], [], "颠倒世界加受难规则；保留特殊出生层，不强制普通地表。"),
        new("天顶挑战", ["zenith"], [], "由原生天顶选项展开其依赖；不等同于逐组合世界已测试。"),
        new("节庆彩虹", ["celebration"], ["rainbowStuff","endlessChristmas"], "庆典、彩虹方块与永恒圣诞节。")
    ];
    public static void Validate(string[] seeds, string[] traits)
    {
        ArgumentNullException.ThrowIfNull(seeds); ArgumentNullException.ThrowIfNull(traits);
        foreach (string id in seeds) if (!Current.NativeSeeds.Any(s => s.Id == id)) throw new ArgumentException("未知原生种子选项：" + id);
        foreach (string id in traits) if (!Current.Traits.Any(s => s.Id == id)) throw new ArgumentException("未知原生种子特性：" + id);
        if (traits.Contains("moreLightning") && traits.Contains("noLightning")) throw new ArgumentException("更多雷击和禁用雷击不能同时选择。");
        if (traits.Contains("noInfection") && traits.Contains("worldIsInfected")) throw new ArgumentException("开局无侵蚀与全境侵蚀不能同时选择。");
        if (seeds.Contains("notraps") && traits.Contains("actuallyNoTraps")) throw new ArgumentException("更多陷阱和真正无陷阱不能同时选择。");
    }
    public static void ValidateForStyle(string style, string[] seeds, string[] traits)
    {
        string? primary = style switch { "bees" => "notthebees", "worthy" => "fortheworthy", "dontstarve" => "theconstant", "classic" or "archipelago" => null, _ => style };
        Validate(primary == null ? seeds : seeds.Concat([primary]).Distinct().ToArray(), traits);
    }
}
