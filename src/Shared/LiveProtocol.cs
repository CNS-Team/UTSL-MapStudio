using System.Text.Json;

namespace ThreeWorlds.Live;

// This is an informational view, not a coherent tile snapshot or verification input.
public sealed record LiveStatus(long Sequence, DateTimeOffset At, string Phase, string Pass,
    string Message, double NativeProgress, double PassProgress, int WorldWidth, int WorldHeight,
    int FrameWidth, int FrameHeight, double SampleMilliseconds, long Frames, string Source,
    bool Final = false, string? Warning = null);

public sealed record LiveFrame(long Sequence, int WorldWidth, int WorldHeight, int Width, int Height, int[] Pixels);

public static class LiveProtocol
{
    public const int Magic = 0x554C5631;
    public const int MaxPixels = 2_000_000;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static void Atomic(string path, Action<Stream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) write(stream);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                    break;
                }
                catch (Exception ex) when (attempt < 6 && ex is IOException or UnauthorizedAccessException)
                { Thread.Sleep(8 * (attempt + 1)); }
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void WriteStatus(string path, LiveStatus status) => Atomic(path, s => JsonSerializer.Serialize(s, status, Json));
    static FileStream OpenSnapshot(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            catch (IOException) when (attempt < 8) { Thread.Sleep(2 * (attempt + 1)); }
        }
    }
    public static LiveStatus? ReadStatus(string path)
    {
        using var s = OpenSnapshot(path);
        return JsonSerializer.Deserialize<LiveStatus>(s, Json);
    }
    public static void WriteFrame(string path, LiveFrame f) => Atomic(path, s =>
    {
        if (f.Width < 1 || f.Height < 1 || (long)f.Width * f.Height > MaxPixels || f.Pixels.Length != f.Width * f.Height)
            throw new InvalidDataException("预览尺寸超出限制。");
        using var b = new BinaryWriter(s, System.Text.Encoding.UTF8, leaveOpen: true);
        b.Write(Magic); b.Write(f.Sequence); b.Write(f.WorldWidth); b.Write(f.WorldHeight); b.Write(f.Width); b.Write(f.Height);
        foreach (int pixel in f.Pixels) b.Write(pixel);
    });
    public static LiveFrame ReadFrame(string path)
    {
        using var s = OpenSnapshot(path);
        using var b = new BinaryReader(s);
        if (b.ReadInt32() != Magic) throw new InvalidDataException("不是本版本的实时预览。");
        long sequence = b.ReadInt64(); int ww = b.ReadInt32(), wh = b.ReadInt32(), w = b.ReadInt32(), h = b.ReadInt32();
        if (ww < 1 || wh < 1 || w < 1 || h < 1 || (long)w * h > MaxPixels || s.Length != 28L + (long)w * h * 4)
            throw new InvalidDataException("实时预览尺寸或长度无效。");
        var pixels = new int[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = b.ReadInt32();
        return new(sequence, ww, wh, w, h, pixels);
    }
}

public sealed record WorldStyle(string Id, string Name, string Description);

public static class StyleCatalog
{
    public static readonly WorldStyle[] All = [
        new("classic", "原版 / 自定义特殊种子", "原生生成，保留输入种子；支持原版特殊种子和完整种子码。"),
        new("skyblock", "空岛 · Skyblock", "原生空岛选项；各片中央缺少安全落脚点时，明确添加小型起始岛。"),
        new("archipelago", "群岛 · 实验", "原生圆形陆块 + 更多液体组合，不冒称原版官方群岛种子。"),
        new("zenith", "天顶 · Get fixed boi", "原生天顶及其依赖选项；保留倒置世界出生层，不强制普通地表。"),
        new("worthy", "受难 · For the worthy", "原生受难选项。"),
        new("bees", "蜂蜜 · Not the bees", "原生蜂蜜选项。"),
        new("drunk", "醉酒 · Drunk world", "原生醉酒选项。"),
        new("remix", "颠倒 · Don't dig up", "原生颠倒世界选项；出生层遵循原生世界。"),
        new("celebration", "十周年 · Celebration", "原生十周年选项。"),
        new("dontstarve", "永恒领域 · The constant", "原生饥荒联动选项。"),
        new("notraps", "机关 · No traps", "原生机关选项。")
    ];
    public static WorldStyle Get(string id) => All.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException("未知地图样式：" + id);
}
