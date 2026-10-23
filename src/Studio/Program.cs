using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
namespace MapStudio;
internal static class Program
{
    internal static string[] Args = [];
    internal static string? EmbeddedRuntimeRoot, EmbeddedMapsRoot;
    [STAThread] public static int Main(string[] args)
    {
        Args = args;
        try { return AppBuilder.Configure<StudioApp>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), ex.ToString()); return 1; }
    }
    internal static string RuntimeRoot => EmbeddedRuntimeRoot ?? Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
    internal static string PackageRoot => Path.GetFileName(RuntimeRoot) == "程序" ? Directory.GetParent(RuntimeRoot)!.FullName : RuntimeRoot;
    internal static string MapsRoot => EmbeddedMapsRoot ?? Path.Combine(PackageRoot, "地图");
    internal static string Runner => Path.Combine(RuntimeRoot, "app", "UTSL.ThreeWorlds.exe");
}
public sealed partial class StudioApp : Application
{
    public override void Initialize() { AvaloniaXamlLoader.Load(this); RequestedThemeVariant = ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new StudioWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
