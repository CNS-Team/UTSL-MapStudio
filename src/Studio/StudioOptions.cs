using System.Text.Json;
namespace MapStudio;
public sealed class StudioOptions
{
    public string Name { get; set; } = "泰拉大陆";
    public string Seed { get; set; } = "20261023";
    public int Width { get; set; } = 8400;
    public int Height { get; set; } = 2400;
    public int Difficulty { get; set; } = 2;
    public int EdgeInset { get; set; } = 40;
    public string Style { get; set; } = "classic";
    public string WorldLayout { get; set; } = "continuous";
    public string[] NativeSeeds { get; set; } = [];
    public string[] SeedTraits { get; set; } = [];
    public string? Source { get; set; }
    public bool LivePreview { get; set; } = true;
    public int PreviewIntervalMs { get; set; } = 1000;
    public FoundationSettings Foundations { get; set; } = new();
    public string Output { get; set; } = "";
    public int SourceWidth => Width + (Width - 80) * 2;
}
public sealed class FoundationSettings
{
    public string Mode { get; set; } = "skyblock-auto";
    public string[] Features { get; set; } = ["dungeon", "temple", "jungle", "crimson", "corruption", "shimmer"];
}
internal static class Data
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    internal static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    internal static string NewOutput() => Path.Combine(Program.MapsRoot, "三联世界-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
}
