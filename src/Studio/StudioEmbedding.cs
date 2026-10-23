using Avalonia;
using Avalonia.Controls;
namespace MapStudio;

/// <summary>Same-window embedding; the standalone application remains unchanged.</summary>
public sealed class StudioWorkspace : IDisposable {
    readonly StudioWindow controller;
    public Control View { get; }
    public bool IsBusy => controller.EmbeddedBusy;
    public StudioWorkspace(Window owner, string runtimeRoot, string mapsRoot) {
        Program.EmbeddedRuntimeRoot = Path.GetFullPath(runtimeRoot);
        Program.EmbeddedMapsRoot = Path.GetFullPath(mapsRoot);
        controller = new StudioWindow();
        View = controller.DetachForEmbedding(owner);
    }
    public void Cancel() => controller.CancelEmbedded();
    public void Dispose() {
        if (IsBusy) throw new InvalidOperationException("先取消并等待所属生成进程退出");
        controller.DisposeEmbedded();
    }
}
public sealed partial class StudioWindow {
    internal bool EmbeddedBusy => runner != null;
    internal void CancelEmbedded() => RequestCancel();
    internal Control DetachForEmbedding(Window owner) {
        embeddedOwner = owner;
        var content = (Border)Content!; Content = null;
        content.BorderThickness = new Thickness(0); content.CornerRadius = new CornerRadius(0);
        var grid = (Grid)content.Child!;
        grid.RowDefinitions[0].Height = new GridLength(0);
        grid.Children.First(c => Grid.GetRow(c) == 0).IsVisible = false;
        return content;
    }
    internal void DisposeEmbedded() { poll.Stop(); MapView.Clear(); }
}
