using MuxDeck.Desktop.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MuxDeck.Core;
using MuxDeck.Core.Components;
using MuxDeck.Desktop;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;

internal static class EnvironmentUiChecks
{
    public static int Run()
    {
        var root = Path.Combine(Workspace.Discover().Root, "desktop", "artifacts", "environment-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? failure = null;
        var count = 0;
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var model = AppComposition.CreateMain(new Workspace(root));
                model.SelectedPage = 3;
                var env = model.Environment;
                // 只替换展示集合，不启动工具，不写真实用户设置。
                env.CoreComponents.Clear();
                env.ExtensionComponents.Clear();
                var longPath = Path.Combine(root, new string('a', 100), "mkvmerge.exe");
                env.CoreComponents.Add(new("MKVToolNix", "MKV 封装、轨道读取与元数据修补", "核心组件", [
                    new("mkvmerge", "", longPath) { Source = "系统" },
                    new("mkvextract", "", longPath) { Source = "系统" },
                    new("mkvpropedit", "", longPath) { Source = "系统" }]));
                env.CoreComponents.Add(new("AssFontSubset", "ASS 字体子集化工具", "核心组件", [new("AssFontSubset", "", longPath) { Source = "Mux Deck" }]));
                env.ExtensionComponents.Add(new("FFmpeg", "字幕渲染、媒体信息读取与预览", "扩展组件", [new("ffmpeg", "", longPath) { Source = "PATH" }]));
                var missing = new EnvironmentComponentViewModel("assfonts", "字体子集化备用后端", "扩展组件", [new("assfonts", "", null)]);
                env.ExtensionComponents.Add(missing);
                foreach (var row in env.CoreComponents.Concat(env.ExtensionComponents).Where(r => !r.IsMissing))
                    row.ApplyInspection(new(ComponentState.Available, row.Parts.ToArray(), "1.0"));
                env.ToolsDirectoryInput = longPath;
                env.WorkDirectoryInput = longPath;
                var window = new MainWindow { DataContext = model };
                window.Show();
                var content = window.FindControl<Grid>("EnvironmentContent")!;
                var scroll = window.FindControl<ScrollViewer>("EnvironmentScroll")!;
                var toggle = window.FindControl<Button>("EnvironmentManagementToggle")!;
                var management = window.FindControl<Border>("EnvironmentManagement")!;
                void Check(bool ok, string name)
                {
                    if (!ok) throw new InvalidOperationException(name);
                    count++;
                    Console.WriteLine("PASS: " + name);
                }
                void Render(string name)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("截图失败");
                    frame.Save(Path.Combine(root, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                foreach (var size in new[] { (1280, 900), (1040, 760) })
                {
                    window.Width = size.Item1; window.Height = size.Item2;
                    Render("collapsed-" + size.Item1);
                    Check(!management.IsEffectivelyVisible, "管理默认收起 " + size.Item1);
                    Check(content.Bounds.Width <= 960 && content.Bounds.Right <= scroll.Bounds.Width, "内容限宽且无横向溢出 " + size.Item1);
                    Check(Math.Abs(toggle.Bounds.Width - content.Bounds.Width) < 1, "管理入口铺满内容区 " + size.Item1);
                    var states = content.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Classes.Contains("env-status")).ToArray();
                    var rightEdges = states.Select(t => t.TranslatePoint(default, window)!.Value.X + t.Bounds.Width).ToArray();
                    Check(states.Length == 4 && rightEdges.Max() - rightEdges.Min() < 1, "四个组件状态列右侧一致 " + size.Item1);
                    var missingTexts = content.GetVisualDescendants().OfType<TextBlock>().Where(t => ReferenceEquals(t.DataContext, missing) && t.IsEffectivelyVisible).Select(t => t.Text).ToArray();
                    Check(missingTexts.Contains("未检测到可执行文件。") && missingTexts.All(t => t is null || !t.Contains("版本") && !t.Contains("来源")), "未安装不显示无意义元信息 " + size.Item1);
                    var pathText = content.GetVisualDescendants().OfType<TextBlock>().First(t => ReferenceEquals(t.DataContext, env.CoreComponents[0]) && t.Text == env.CoreComponents[0].PathSummary);
                    Check(Equals(ToolTip.GetTip(pathText), longPath) && pathText.Bounds.Width < content.Bounds.Width, "路径省略但悬停保留完整值 " + size.Item1);
                    var entryPoint = toggle.TranslatePoint(new Point(toggle.Bounds.Width - 40, toggle.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(entryPoint);
                    Render("hover-" + size.Item1);
                    window.MouseDown(entryPoint, MouseButton.Left);
                    window.MouseUp(entryPoint, MouseButton.Left);
                    Render("expanded-" + size.Item1);
                    Check(management.IsEffectivelyVisible && env.SettingsToggleText == "组件管理", "整行打开后仍保留区域名称 " + size.Item1);
                    var toolsInput = window.FindControl<TextBox>("EnvironmentToolsInput")!;
                    var workInput = window.FindControl<TextBox>("EnvironmentWorkInput")!;
                    Check(Math.Abs(toolsInput.Bounds.Width - workInput.Bounds.Width) < 1 && toolsInput.Text == longPath && workInput.Text == longPath, "长路径不改变输入框对齐或内容 " + size.Item1);
                    Check(scroll.Extent.Width <= scroll.Viewport.Width + 1, "展开后无横向滚动 " + size.Item1);
                    scroll.Offset = new Vector(0, 5000);
                    Render("scrolled-" + size.Item1);
                    Check(scroll.Offset.Y > 0, "展开后可滚动到底部 " + size.Item1);
                    scroll.Offset = default;
                    UiInputTestSupport.Click(window, toggle);
                    Render("restored-" + size.Item1);
                    Check(!management.IsEffectivelyVisible && !env.IsSettingsOpen, "整行再次点击收起恢复列表 " + size.Item1);
                }
                model.SelectedPage = 1;
                Dispatcher.UIThread.RunJobs();
                Check(window.GetVisualDescendants().OfType<TextBlock>().All(t => !t.IsEffectivelyVisible || t.Text != model.PageTitle), "批量页不恢复重复标题");
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            catch (Exception ex) { failure = ex; }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Console.WriteLine($"Environment UI: {count} PASS; Screenshots: {root}");
        if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
    }
}
