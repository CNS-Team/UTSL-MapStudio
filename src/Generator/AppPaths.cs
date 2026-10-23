namespace ThreeWorlds;

internal static class AppPaths
{
    internal static string PackageRoot(string runtimeRoot)
    {
        var full = Path.GetFullPath(runtimeRoot).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(Path.GetFileName(full), "程序", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(full)!.FullName : full;
    }
    internal static string MapsRoot(string runtimeRoot) => Path.Combine(PackageRoot(runtimeRoot), "地图");
    internal static string NewMapFolder(string runtimeRoot) => Path.Combine(MapsRoot(runtimeRoot), "三联世界-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
}
