using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;

internal static class RealUiAcceptance
{
    public static int Run(string fixtureDirectory, string screenshotPath)
    {
        Exception? failure = null;
        var passed = 0;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            SingleMuxViewModel? single = null;
            try
            {
                var workspace = Workspace.Discover();
                var artifactPrefix = Path.GetFullPath(Path.Combine(workspace.Root, "desktop", "artifacts"))
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fixture = Path.GetFullPath(fixtureDirectory);
                var screenshot = Path.GetFullPath(screenshotPath);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!fixture.StartsWith(artifactPrefix, comparison) || !screenshot.StartsWith(artifactPrefix, comparison))
                    throw new ArgumentException("真实 UI 验收素材和截图必须在 desktop/artifacts/ 下。");

                var input = Path.Combine(fixture, "input");
                var video = Path.Combine(input, "phase1-source.mkv");
                var sc = Path.Combine(input, "sc.ass");
                var tc = Path.Combine(input, "tc.ass");
                var fonts = Path.Combine(fixture, "fonts-yahei");
                if (!File.Exists(video) || !File.Exists(sc) || !File.Exists(tc) || !Directory.Exists(fonts))
                    throw new FileNotFoundException("真实 UI 验收素材不完整：" + fixture);

                AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
                    { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var model = new MainWindowViewModel(workspace);
                single = model.Single;
                window = new MainWindow { DataContext = model };
                window.Show();
                single.ScSubtitle = sc;
                single.TcSubtitle = tc;
                single.FontsDirectory = fonts;
                single.OutputDirectory = Path.Combine(fixture, "outputs", "ui-" + Guid.NewGuid().ToString("N"));
                single.VideoPath = video;

                // 驱动真实路径 setter 的合并探测，不伪造轨道或服务返回值。
                PumpUntil(() => !single.IsProbing, TimeSpan.FromSeconds(45), "源视频探测");
                var sourceRows = single.Tracks.Where(row => row.Source == "源视频 · 内封").ToArray();
                Check(sourceRows.Any(row => row.Type == "视频" && row.Policy == "保留") &&
                    sourceRows.Any(row => row.Type == "音频" && row.Policy == "保留"), "真实探测视频和音轨并显示保留策略");
                Check(sourceRows.Any(row => row.Type == "字幕" && row.Policy == "移除") &&
                    sourceRows.Any(row => row.Type == "附件" && row.Policy == "移除"), "真实探测源字幕和附件并显示移除策略");
                Check(single.Tracks.Count(row => row.IsEditable && row.Policy == "新增") == 2 &&
                    single.ScTrack.Language == "zh-Hans" && single.TcTrack.Language == "zh-Hant" &&
                    single.ScTrack.IsDefault && !single.TcTrack.IsDefault, "轨表显示本次简繁新增轨和默认旗标");
                Check(single.StartCommand.CanExecute(null), "真实完整输入可通过开始命令提交");

                single.StartCommand.Execute(null);
                Check(single.IsRunning && !single.CanEdit && !single.CanEditSelectedTrack &&
                    !single.StartCommand.CanExecute(null) && single.CancelCommand.CanExecute(null), "真实运行中锁定参数并启用取消");
                var outputDirectory = single.OutputDirectory;
                single.OutputDirectory = Path.Combine(fixture, "outputs", "must-not-be-used");
                Check(single.OutputDirectory == outputDirectory, "运行中拒绝更改本次输出位置");
                PumpUntil(() => single.RunTask.IsCompleted && !single.IsRunning && single.StartCommand.CanExecute(null),
                    TimeSpan.FromMinutes(3), "真实 UI 封装任务");
                single.RunTask.GetAwaiter().GetResult();
                Check(single.Result?.Status == MuxStatus.Succeeded && single.StatusText == "成功" &&
                    File.Exists(single.OutputPath), "真实 Core 成功结果回到 UI 且成品存在");
                Check(single.CanEdit && single.CanEditSelectedTrack && !single.CancelCommand.CanExecute(null), "成功后恢复编辑并结束取消状态");
                Check(Path.GetDirectoryName(single.OutputPath) == outputDirectory && single.Result?.Checks.Count > 0 &&
                    File.Exists(single.LogPath), "成品、校验说明和完整日志可从 UI 结果定位");

                single.SelectedTrack = single.ScTrack;
                Dispatcher.UIThread.RunJobs();
                Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("无法捕获真实 UI 验收窗口。");
                image.Save(screenshot, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                Console.WriteLine("Screenshot: " + screenshot);
                Console.WriteLine("Output: " + single.OutputPath);
                Console.WriteLine("Task log: " + single.LogPath);
            }
            catch (Exception ex)
            {
                failure = ex;
                Console.Error.WriteLine("FAIL: 真实 UI 验收 — " + ex);
                if (single is not null)
                    Console.Error.WriteLine($"状态={single.StatusText}; 阶段={single.StageText}; 探测={single.ProbeText}\n{single.ResultSummary}\n{single.Log}");
            }
            finally
            {
                // 验收异常也走生产窗口的安全退出路径，不能遗留正在运行的工具。
                if (window is not null)
                {
                    try
                    {
                        var closed = false;
                        window.Closed += (_, _) => closed = true;
                        window.Close();
                        PumpUntil(() => closed, TimeSpan.FromSeconds(45), "窗口安全退出");
                        Check(true, "真实工作区取消等待路径结束并触发 Closed");
                    }
                    catch (Exception ex) { failure ??= ex; Console.Error.WriteLine("FAIL: 退出验收 — " + ex); }
                }
            }

            void Check(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException("FAIL: " + name);
                passed++;
                Console.WriteLine("PASS: " + name);
            }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Console.WriteLine($"真实 UI 验收：{passed} 项通过，{(failure is null ? "成功" : "失败")}。");
        return failure is null ? 0 : 1;
    }

    private static void PumpUntil(Func<bool> completed, TimeSpan timeout, string operation)
    {
        var elapsed = Stopwatch.StartNew();
        while (!completed() && elapsed.Elapsed < timeout)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
        if (!completed()) throw new TimeoutException(operation + "未在 " + timeout + " 内完成。");
    }
}
