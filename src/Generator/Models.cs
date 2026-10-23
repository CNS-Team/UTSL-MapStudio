using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ThreeWorlds;

public sealed class Options
{
    public string Name { get; set; } = "泰拉大陆";
    public string Seed { get; set; } = "20261023";
    public int Width { get; set; } = 8400;
    public int Height { get; set; } = 2400;
    public int Difficulty { get; set; } = 2;
    public int EdgeInset { get; set; } = 40;
    public string? Source { get; set; }
    public bool UniqueOverlapLoot { get; set; } = false;
    public string Style { get; set; } = "classic";
    public string WorldLayout { get; set; } = "continuous";
    public string[] NativeSeeds { get; set; } = [];
    public string[] SeedTraits { get; set; } = [];
    public FoundationOptions Foundations { get; set; } = new();
    public bool LivePreview { get; set; } = true;
    public int PreviewIntervalMs { get; set; } = 1000;
    public string Output { get; set; } = "";
    public int Overlap => EdgeInset * 2;
    public int Stride => Width - Overlap;
    public int SourceWidth => Width + Stride * 2;
    public void Validate(bool fixture = false)
    {
        _ = Live.StyleCatalog.Get(Style);
        Live.SeedCatalog.ValidateForStyle(Style, NativeSeeds, SeedTraits);
        if (WorldLayout is not ("continuous" or "normal-stitch")) throw new ArgumentException("未知三图生成方式。");
        if (WorldLayout == "normal-stitch" && (Style != "classic" || Source != null || NativeSeeds.Length > 0 || SeedTraits.Length > 0))
            throw new ArgumentException("独立标准三世界用于原版普通地形，不能导入单张母图；特殊样式/空岛请使用连续母图模式。");
        Foundations.Validate();
        if (PreviewIntervalMs is < 500 or > 5000) throw new ArgumentException("预览刷新间隔必须在500..5000ms内。");
        if (Seed.Length > 256 || Seed.Any(char.IsControl)) throw new ArgumentException("种子不能含控制字符，最多256字符。");
        if (!fixture && EdgeInset != 40) throw new ArgumentException("当前边界契约固定单侧40格/总重叠80格，不能缩入角色不可达的边缘。");
        if (string.IsNullOrWhiteSpace(Name) || Name != Name.Trim() || Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Name is "." or "..")
            throw new ArgumentException("世界名称为空或包含不合法的文件名字符。");
        if (Name.Length > 40) throw new ArgumentException("世界名最多 40 字符。");
        if (!fixture && (Width, Height) is not ((4200, 1200) or (6400, 1500) or (8400, 2400)))
            throw new ArgumentException("请使用小/中/大世界标准尺寸：4200×1200、6400×1500、8400×2400。");
        if (EdgeInset < 20 || EdgeInset > 100 || Width <= Overlap * 2)
            throw new ArgumentException("边界缓冲必须在 20..100 格内，且片宽必须大于两倍重叠宽度。");
        if (Difficulty is < 0 or > 3) throw new ArgumentException("难度应为 0普通/1专家/2大师/3旅行。");
        if (string.IsNullOrWhiteSpace(Output)) throw new ArgumentException("必须指定新的输出目录。");
        if (Source is not null && !File.Exists(Source)) throw new FileNotFoundException("源地图不存在。", Source);
    }
    public SliceSpec[] Specs(int sourceOffset = 0) => [
        new("west", Name + "·西", sourceOffset, Width, Height),
        new("middle", Name, sourceOffset + Stride, Width, Height),
        new("east", Name + "·东", sourceOffset + Stride * 2, Width, Height)
    ];
}

public record SliceSpec(string Role, string Name, int SourceX, int Width, int Height);
public record Artifact(string Path, long Bytes, string Sha256);
public record TileChange(int X, int Y, string Reason);
public record SliceResult(SliceSpec Spec, Artifact File, Guid WorldGuid, int WorldId, int SpawnX, int SpawnY,
    int Chests, int Signs, int TileEntities, int Npcs, int Mobs, int ClearedOverlapInventories, List<TileChange> Changes,
    string TileDigest, string MetadataDigest, long SourceTileMismatches, List<string> BoundaryObjectExclusions);
public record SeamResult(string Left, string Right, long ComparedTiles, long Mismatches, string LeftDigest, string RightDigest);

internal static class Files
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    public static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException(path);
    public static Artifact Inspect(string path)
    {
        using var stream = File.OpenRead(path);
        return new(Path.GetFullPath(path), stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
    }
}
