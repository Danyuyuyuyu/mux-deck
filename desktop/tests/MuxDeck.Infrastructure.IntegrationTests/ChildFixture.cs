using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MuxDeck.Infrastructure.Muxing;

namespace MuxDeck.Infrastructure.IntegrationTests;

internal static class ChildFixture
{
    internal const string ProbeJson = """
        {"tracks":[{"id":7,"type":"audio","codec":"AAC","properties":{"codec_id":"A_AAC","language":"chi","language_ietf":"zh-Hans","track_name":"中文","default_track":true,"forced_track":false,"audio_channels":2,"audio_sampling_frequency":48000}}],
        "attachments":[{"id":4,"file_name":"字.ttf","content_type":"font/ttf","size":3}],"container":{"recognized":true,"properties":{"duration":12345}}}
        """;
    internal const string Ass = """
        [Script Info]
        ; Font Subset: TEST - Test
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic
        Style: Default,Test,20,&HFFFFFF,&HFFFFFF,&H0,&H0,0,0
        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,A
        """;
    internal const string FontXml = """
        <ttFont><name><namerecord nameID="1">Test</namerecord><namerecord nameID="4">Test Regular</namerecord></name>
        <cmap><cmap_format_4 platformID="3" platEncID="1"><map code="0x41" name="A"/><map code="0x42" name=".notdef"/></cmap_format_4>
        <cmap_format_4 platformID="1" platEncID="0"><map code="0x43" name="C"/></cmap_format_4></cmap>
        <OS_2><fsSelection value="00000000 01000000"/><usWeightClass value="400"/></OS_2></ttFont>
        """;

    internal static ProcessRequest Request(string mode, IEnumerable<string>? args = null) =>
        new("dotnet", [typeof(ChildFixture).Assembly.Location, "--infra-child", mode, .. args ?? []]);

    // 在 xUnit 入口前分流；测试程序集本身充当真实可取消子进程，CI 不依赖媒体工具。
    [ModuleInitializer]
    internal static void Dispatch()
    {
        var command = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(command, "--infra-child");
        if (index < 0) return;
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            var mode = command[index + 1];
            var args = command.Skip(index + 2).ToArray();
            Console.Error.WriteLine("中文 stderr");
            if (mode == "echo") Console.WriteLine(JsonSerializer.Serialize(args));
            else if (mode == "large") { Console.Write(new string('x', 4096)); Console.Error.Write(new string('y', 4096)); }
            else if (mode == "exit") { Console.Error.WriteLine("工具失败详情"); Environment.Exit(7); }
            else if (mode == "wait") { Console.Error.WriteLine("READY"); Thread.Sleep(60000); }
            else if (mode == "tree")
            {
                var request = Request("wait");
                var start = new ProcessStartInfo(request.Executable) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in request.Arguments) start.ArgumentList.Add(arg);
                using var child = Process.Start(start)!;
                Console.WriteLine("CHILD:" + child.Id);
                Thread.Sleep(60000);
            }
            else
            {
                var executable = args[0]; args = args[1..];
                if (mode == "fatal") { Console.Error.WriteLine("2026-10-13 00:00:00|ERR|Missing font"); Environment.Exit(0); }
                if (mode == "invalid") Console.WriteLine("{broken");
                else if (executable == "probe") Console.WriteLine(ProbeJson);
                else if (executable == "afs")
                {
                    var directory = args[Array.IndexOf(args, "--output") + 1];
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, Path.GetFileName(args[0])), Ass, new UTF8Encoding(false));
                    File.WriteAllBytes(Path.Combine(directory, "Test.ttf"), [1, 2, 3]);
                }
                else if (executable == "ttx")
                    File.WriteAllText(args[Array.IndexOf(args, "-o") + 1], mode == "xml-invalid" ? "<!DOCTYPE ttFont SYSTEM 'external'><ttFont/>" : FontXml);
                else if (executable == "mux")
                    File.WriteAllBytes(args[Array.IndexOf(args, "-o") + 1], [1, 2, 3]);
                else if (executable == "extract")
                    foreach (var arg in args.Skip(2).Where(arg => arg != "--fullraw"))
                        File.WriteAllBytes(arg[(arg.IndexOf(':') + 1)..], [1, 2, 3]);
                if (executable != "probe") Console.WriteLine("中文 stdout");
            }
            Environment.Exit(0);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.Exit(99); }
    }
}

internal sealed class ChildToolRunner(string mode = "success", Action<ProcessLine>? observe = null) : IMuxToolRunner
{
    internal List<ProcessRequest> Requests { get; } = [];
    public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var executable = Path.GetFileNameWithoutExtension(request.Executable) switch
        {
            "mkvmerge" => request.Arguments.Contains("-J") ? "probe" : "mux",
            "mkvextract" => "extract", "AssFontSubset.Console" => "afs", "ttx" => "ttx",
            _ => "unknown"
        };
        var child = ChildFixture.Request(mode, [executable, .. request.Arguments]) with
        { WorkingDirectory = request.WorkingDirectory, Environment = request.Environment, MaxCapturedCharacters = request.MaxCapturedCharacters };
        return new ProcessRunner().RunAsync(child, new Sink(line => { observe?.Invoke(line); progress?.Report(line); }), cancellationToken);
    }
}
internal sealed class Sink(Action<ProcessLine> action) : IProgress<ProcessLine>
{
    public void Report(ProcessLine value) => action(value);
}
