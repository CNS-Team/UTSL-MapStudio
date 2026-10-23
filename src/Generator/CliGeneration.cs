using System.Text.Json;
namespace ThreeWorlds;
internal static class CliGeneration
{
    internal static void Run(Options options, string? cancelFile)
    {
        using var cancel = new CancellationTokenSource();
        using var watch = new System.Threading.Timer(_ => { if (cancelFile != null && File.Exists(cancelFile)) cancel.Cancel(); }, null, 0, 150);
        new Pipeline(Console.WriteLine)
        {
            Stage = (text, progress) => Console.WriteLine("UI_STAGE " + JsonSerializer.Serialize(new { text, progress }, Files.Json).ReplaceLineEndings("")),
            PreviewSource = (directory, mother) => Console.WriteLine("UI_PREVIEW " + JsonSerializer.Serialize(new { directory, mother }, Files.Json).ReplaceLineEndings(""))
        }.RunAsync(options, cancel.Token).GetAwaiter().GetResult();
    }
}
