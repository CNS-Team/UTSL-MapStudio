using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ThreeWorlds.Live;
namespace MapStudio;

public sealed partial class StudioWindow
{
    static TextBlock Label(string text, double size = 12, bool muted = false) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse(muted ? "#96A7C1" : "#E6EDF8") };
    Window Dialog(string title, double width, double height, Control content) => new() {
        Title = title, Width = width, Height = height, MinWidth = Math.Min(width, 680), MinHeight = Math.Min(height, 380),
        Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush.Parse("#151C27"), Content = content
    };
    Window CreateAboutWindow()
    {
        var stack = new StackPanel { Margin = new Thickness(30), Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        stack.Children.Add(new Image { Width = 100, Height = 100, Source = new Bitmap(AssetLoader.Open(new Uri("avares://UTSL.MapStudio/Assets/MapStudio.png"))) });
        stack.Children.Add(Label("UTSL地图生成器", 22));
        stack.Children.Add(Label("开发团队：流光工作室", 13));
        stack.Children.Add(Label("版权署名：流光-Pigeon-X", 13));
        stack.Children.Add(Label("2.0.0", 11, true));
        stack.Children.Add(Label("第三方组件保留各自版权与许可证。", 10, true));
        var w = Dialog("关于", 440, 390, stack); w.CanResize = false; return w;
    }
    async void AboutClick(object? sender, RoutedEventArgs e) => await CreateAboutWindow().ShowDialog(this);
    static readonly (string Title, string Text)[] ParameterDescriptions = [
        ("种子与样式", "输入普通种子、原版特殊种子名或完整复制码。地图样式是快捷选择；种子特性合集允许叠加原生特殊种子与细分特性。勾选项与输入种子会合并，不会代替用户种子。"),
        ("种子特性合集", "清单直接采集自随附引擎 1.4.5.8：9种特殊种子、37项特性。中文描述来自该引擎本地化。组合预设是工具配方，不是新的官方种子名。原生选项调用已逐项验证；并非所有组合都已生成地图或通过游戏测试。"),
        ("生成方式", "连续母图：原生生成超宽地图后切为西、中、东，支持特殊种子。独立标准三世界：分别原生生成三张标准地图，再处理同Y海平面和边界；只用于普通地形。"),
        ("尺寸与难度", "小4200×1200、中6400×1500、大8400×2400；难度为普通、专家、大师或旅行。输入种子码携带尺寸时，仍以本面板的尺寸和难度为准。"),
        ("基础环境补全", "默认收起。空岛自动补全只对实际空岛世界生效；自定义补全按勾选项补入原生环境；保持纯原生不添加。地牢/神庙/丛林/猩红/腐化/微光/雪原/沙漠/蘑菇可选。补全会影响无侵蚀等种子意图，想保持特性原貌请选择保持纯原生。"),
        ("边界与出生点", "1西—2中（默认进入）—3东，单侧40格、总重叠80格。边界插件候选使用原浮点Y、对侧内缩1–2格、保留速度，允许卡墙，不找地面或出生点。中间世界默认采用中心安全出生点；随机/队伍出生类原生选项不等于本工具会放弃中心默认规则。"),
        ("极端组合", "无地表、淹水、错误世界等可能不满足中心安全出生点或接缝对象保护。生成器会明确失败而不是伪造成功；可换种子/组合重试。更多雷击与无雷击、全境侵蚀与无侵蚀、更多陷阱与真正无陷阱不可同时选择。"),
        ("预览与输出", "实时图格采样可选0.5、1、2或5秒；不是进度动画。地图在根目录的地图文件夹，成功附带去重拼接长图。只有有COMPLETE.json的批次可以使用。长图是图格配色视图，不是游戏客户端贴图。"),
        ("安全边界", "本工具只在本地生成，不自动连接游戏服务器或安装插件；取消只影响此任务自己的worker。地图生成/离线读图验证不等于真人边界换乘或渲染通过。")
    ];
    Window CreateParametersWindow()
    {
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 18 };
        foreach (var item in ParameterDescriptions) { var section = new StackPanel { Spacing = 7 }; section.Children.Add(Label(item.Title, 16)); section.Children.Add(Label(item.Text, 12, true)); content.Children.Add(section); }
        return Dialog("功能与参数说明", 760, 700, new ScrollViewer { Content = content });
    }
    async void ParametersClick(object? sender, RoutedEventArgs e) => await CreateParametersWindow().ShowDialog(this);
    void ConfigureParameterTips()
    {
        ToolTip.SetTip(WorldName, "中间世界名称；左右地图自动添加·西、·东，中间为默认进入世界。");
        ToolTip.SetTip(Seed, "可输入普通种子、特殊种子名或完整复制码；与种子特性面板勾选项叠加。");
        ToolTip.SetTip(SizeChoice, "每一片的实际图格尺寸，连续母图去重后总宽为每片宽×3−160。");
        ToolTip.SetTip(DifficultyChoice, "普通 / 专家 / 大师 / 旅行；不把困难模式开局和大师难度混为一谈。");
        ToolTip.SetTip(LayoutChoice, ParameterDescriptions[2].Text);
        ToolTip.SetTip(FoundationChoice, ParameterDescriptions[4].Text);
        ToolTip.SetTip(RefreshChoice, "只改变采样间隔，不跳过原生生成；过快可能增加开销。");
        StyleChoice.SelectionChanged += (_, _) => { if (StyleChoice.SelectedIndex >= 0) ToolTip.SetTip(StyleChoice, StyleCatalog.All[StyleChoice.SelectedIndex].Description); };
        ToolTip.SetTip(StyleChoice, StyleCatalog.All[0].Description);
    }
    void UpdateSeedSummary() => SeedsButton.Content = $"种子特性合集…  已选 {nativeSeeds.Length + seedTraits.Length}";
    async void SeedsClick(object? sender, RoutedEventArgs e) { if (runner == null) await CreateSeedsWindow().ShowDialog(this); }
    Window CreateSeedsWindow()
    {
        var seeds = new Dictionary<string, CheckBox>(); var traits = new Dictionary<string, CheckBox>();
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(24), RowSpacing = 14 };
        var intro = new StackPanel { Spacing = 6 }; intro.Children.Add(Label("种子特性合集", 22));
        intro.Children.Add(Label($"随附泰拉引擎 {SeedCatalog.Current.EngineVersion}  ·  {SeedCatalog.Current.NativeSeeds.Length}种特殊种子 / {SeedCatalog.Current.Traits.Length}项特性", 12, true));
        intro.Children.Add(Label("保持输入种子，叠加勾选特性；逐项原生开关已验证，不代表所有组合均完成整图或游戏测试。", 11, true)); grid.Children.Add(intro);
        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("280,Auto,*"), ColumnSpacing = 10 }; Grid.SetRow(tools, 1); grid.Children.Add(tools);
        var recipe = new ComboBox { Name = "RecipeChoice", ItemsSource = SeedCatalog.Recipes.Select(r => r.Name).ToArray(), SelectedIndex = 0 };
        var applyRecipe = new Button { Name = "ApplyRecipe", Content = "套用配方" }; var search = new TextBox { Name = "SeedSearch", PlaceholderText = "搜索特性 / 英文代号" }; Grid.SetColumn(applyRecipe, 1); Grid.SetColumn(search, 2); tools.Children.Add(recipe); tools.Children.Add(applyRecipe); tools.Children.Add(search);
        var options = new StackPanel { Spacing = 16 }; var scroll = new ScrollViewer { Content = options }; Grid.SetRow(scroll, 2); grid.Children.Add(scroll);
        void Group(string title, IEnumerable<(string Id, string Text)> items, Dictionary<string, CheckBox> dict, string[] selection)
        {
            var section = new StackPanel { Spacing = 8 }; section.Children.Add(Label(title, 15)); var wrap = new WrapPanel(); section.Children.Add(wrap); options.Children.Add(section);
            foreach (var item in items) {
                var b = new CheckBox { Content = SeedCatalog.Label(item.Id), IsChecked = selection.Contains(item.Id), Width = 270, Margin = new Thickness(0, 0, 8, 5) };
                ToolTip.SetTip(b, item.Text + "\n原生代号：" + item.Id); dict.Add(item.Id, b); wrap.Children.Add(b);
            }
        }
        Group("原生特殊种子（可叠加）", SeedCatalog.Current.NativeSeeds.Select(s => (s.Id, (s.Description ?? "原生特殊种子选项。") + "\n种子别名：" + string.Join(" / ", s.Aliases.Concat(s.NumericAliases.Select(v => v.ToString()))))), seeds, nativeSeeds);
        foreach (var group in SeedCatalog.Current.Traits.GroupBy(s => SeedCatalog.Group(s.Id))) Group(group.Key, group.Select(s => (s.Id, s.Description)), traits, seedTraits);
        var foot = new StackPanel { Spacing = 10 }; Grid.SetRow(foot, 3); grid.Children.Add(foot);
        var message = Label("鼠标悬停查看每项原生说明。配方只替换本面板的勾选项，不改种子、尺寸与难度。", 11, true); foot.Children.Add(message);
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var clear = new Button { Name = "ClearSeeds", Content = "全部清空" }; var cancel = new Button { Name = "CancelSeeds", Content = "取消" }; var save = new Button { Name = "SaveSeeds", Content = "应用特性" }; save.Classes.Add("primary"); actions.Children.Add(clear); actions.Children.Add(cancel); actions.Children.Add(save); foot.Children.Add(actions);
        var window = Dialog("种子特性合集", 940, 790, grid);
        void Select(string[] ns, string[] ts) { foreach (var p in seeds) p.Value.IsChecked = ns.Contains(p.Key); foreach (var p in traits) p.Value.IsChecked = ts.Contains(p.Key); }
        applyRecipe.Click += (_, _) => { var r = SeedCatalog.Recipes[recipe.SelectedIndex]; Select(r.NativeSeeds, r.SeedTraits); message.Text = r.Description; };
        clear.Click += (_, _) => Select([], []); cancel.Click += (_, _) => window.Close();
        save.Click += (_, _) => {
            var ns = seeds.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToArray(); var ts = traits.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToArray();
            try { SeedCatalog.ValidateForStyle(StyleCatalog.All[StyleChoice.SelectedIndex].Id, ns, ts); nativeSeeds = ns; seedTraits = ts; UpdateSeedSummary(); window.Close(); }
            catch (ArgumentException ex) { message.Text = ex.Message; }
        };
        search.TextChanged += (_, _) => { string q = search.Text?.Trim() ?? ""; foreach (var p in seeds.Concat(traits)) p.Value.IsVisible = q.Length == 0 || p.Key.Contains(q, StringComparison.OrdinalIgnoreCase) || SeedCatalog.Label(p.Key).Contains(q, StringComparison.OrdinalIgnoreCase) || (SeedCatalog.Current.Traits.FirstOrDefault(s => s.Id == p.Key)?.Description ?? "").Contains(q, StringComparison.OrdinalIgnoreCase); };
        return window;
    }
}
