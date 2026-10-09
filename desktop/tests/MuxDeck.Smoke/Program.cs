using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using MuxDeck.Core;
using MuxDeck.Desktop;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;

// Child modes exercise real pipes, argument escaping, failures and cancellation without external media tools.
if (args.FirstOrDefault() == "--child")
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine(args[1]); Console.Error.WriteLine("stderr 字幕"); return 7;
}
if (args.FirstOrDefault() == "--wait") { await Task.Delay(TimeSpan.FromSeconds(30)); return 0; }

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}

var runner = new ProcessRunner();
var executable = Environment.ProcessPath!;
var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
    ? new[] { System.Reflection.Assembly.GetEntryAssembly()!.Location } : Array.Empty<string>();
var special = "中文 subtitle \"quoted\" & spaces";
var result = await runner.RunAsync(new ProcessRequest(executable, [.. prefix, "--child", special]));
Check(result.ExitCode == 7 && result.StandardOutput.Trim() == special && result.StandardError.Contains("stderr 字幕"),
    $"进程参数、Unicode、stderr 和非零退出码 (exit={result.ExitCode}, out={result.StandardOutput.Trim()}, err={result.StandardError.Trim()})");
using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
{
    var elapsed = Stopwatch.StartNew();
    try
    {
        await runner.RunAsync(new ProcessRequest(executable, [.. prefix, "--wait"]), cancellationToken: cancellation.Token);
        throw new InvalidOperationException("Expected cancellation");
    }
    catch (OperationCanceledException) { Check(elapsed.Elapsed < TimeSpan.FromSeconds(10), "取消终止子进程"); }
}

var root = Workspace.Discover();
Check(new ToolLocator(root).Inspect().Count == 6, "六项工具路径检测");
var fixture = Path.Combine(root.Root, "data", "tmp", "desktop-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(fixture, "bin", "mkvtoolnix"));
try
{
    var bundled = Path.Combine(fixture, "bin", "mkvtoolnix", "mkvmerge.exe");
    File.WriteAllText(bundled, "fixture");
    Check(new ToolLocator(new Workspace(fixture)).Inspect()[0].Path == bundled, "便携 bin 优先");
}
finally { Directory.Delete(fixture, recursive: true); }

// All UI operations run on one STA thread after asynchronous core checks complete.
Exception? uiError = null;
var uiThread = new Thread(() =>
{
    try
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
            { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var model = new MainWindowViewModel(root);
        var window = new MainWindow { DataContext = model };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var video = window.FindControl<TextBox>("VideoInput")!;
        video.Text = "E:\\Video\\测试片源.mkv";
        Dispatcher.UIThread.RunJobs();
        Check(model.VideoPath == video.Text, "编译绑定：输入回写 ViewModel");
        model.ShowEnvironmentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check(model.IsEnvironment && model.Tools.Count == 6, "导航到真实环境检测");
        model.ShowSingleCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check(model.IsSingle && video.Text == "E:\\Video\\测试片源.mkv", "切页保留表单状态");
        Check(window.Bounds.Width >= 900 && window.Bounds.Height >= 700, "窗口布局与最小尺寸");
        if (args.Length == 2 && args[0] == "--screenshot")
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame");
            image.Save(args[1], Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Console.WriteLine("Screenshot: " + args[1]);
        }
        window.Close();
    }
    catch (Exception ex) { uiError = ex; }
});
if (OperatingSystem.IsWindows()) uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start(); uiThread.Join();
if (uiError is not null) throw uiError;
Console.WriteLine($"{passed}/{passed} PASS");
return 0;
