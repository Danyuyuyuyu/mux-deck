using NetArchTest.Rules;
using Xunit;

namespace MuxDeck.Architecture.Tests
{
    public sealed class PurePathMembersRuleTests
    {
        [Theory]
        [InlineData("MuxDeck.Application")]
        [InlineData("MuxDeck.Contracts")]
        public void Inner_layers_only_use_the_authorized_pure_path_members(string layer)
        {
            var types = ArchitectureScope.ProductionTypes().That().ResideInNamespace(layer);
            Assert.NotEmpty(types.GetTypes());
            ArchitectureScope.AssertSuccess(types.Should().MeetCustomRule(new PurePathMembersRule()).GetResult());
        }

        // 只扫描 IL；不执行 fixture 的文件读写、随机命名或路径环境查询。
        [Theory]
        [InlineData(typeof(Fixtures.PathMembers.GuardCombine), true)]
        [InlineData(typeof(Fixtures.PathMembers.GuardJoin), true)]
        [InlineData(typeof(Fixtures.PathMembers.GuardFileName), true)]
        [InlineData(typeof(Fixtures.PathMembers.GuardFileStem), true)]
        [InlineData(typeof(Fixtures.PathMembers.GuardExtension), true)]
        [InlineData(typeof(Fixtures.PathMembers.GuardParentName), true)]
        [InlineData(typeof(Fixtures.PathMembers.GuardChangeSuffix), true)]
        [InlineData(typeof(Fixtures.PathMembers.GuardHasSuffix), true)]
        [InlineData(typeof(Fixtures.PathMembers.GuardSeparator), true)]
        [InlineData(typeof(Fixtures.PathMembers.GuardAlternateSeparator), true)]
        [InlineData(typeof(Fixtures.PathMembers.GuardFullPath), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardTempPath), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardTempFile), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardRandomFile), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardUnlistedPath), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardFileReader), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardDirectoryReader), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardFileMetadata), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardDirectoryMetadata), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardStreamSignature), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardStreamGeneric), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardMemoryStreamCreator), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardInvalidData), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardLambdaPath), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardAsyncPath), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardLambdaFile), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardAsyncFile), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardCatchIo), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardTypeToken), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardFileDelegate), false)]
        [InlineData(typeof(Fixtures.PathMembers.GuardPureLambdaPath), true)]
        [InlineData(typeof(Fixtures.PathMembers.GuardPureAsyncPath), true)]
        public void Member_guard_rejects_io_and_allows_the_exact_path_whitelist(Type type, bool expected)
        {
            var types = Types.InAssembly(typeof(PurePathMembersRuleTests).Assembly).That().HaveName(type.Name);
            Assert.Single(types.GetTypes());
            var result = types.Should().MeetCustomRule(new PurePathMembersRule()).GetResult();
            Assert.Equal(expected, result.IsSuccessful);
            if (!expected) Assert.Contains(type.FullName!, result.FailingTypeNames);
        }

        [Fact]
        public void Domain_keeps_the_original_whole_io_namespace_ban()
        {
            var types = Types.InAssembly(typeof(PurePathMembersRuleTests).Assembly).That()
                .HaveName(nameof(Fixtures.PathMembers.GuardCombine));
            Assert.False(types.ShouldNot().HaveDependencyOn("System.IO").GetResult().IsSuccessful);
        }
    }
}

namespace MuxDeck.Architecture.Tests.Fixtures.PathMembers
{
    internal sealed class GuardCombine { public string Read() => Path.Combine("a", "b"); }
    internal sealed class GuardJoin { public string Read() => Path.Join("a", "b"); }
    internal sealed class GuardFileName { public string Read(string path) => Path.GetFileName(path); }
    internal sealed class GuardFileStem { public string? Read(string path) => Path.GetFileNameWithoutExtension(path); }
    internal sealed class GuardExtension { public string? Read(string path) => Path.GetExtension(path); }
    internal sealed class GuardParentName { public string? Read(string path) => Path.GetDirectoryName(path); }
    internal sealed class GuardChangeSuffix { public string? Read(string path) => Path.ChangeExtension(path, ".mkv"); }
    internal sealed class GuardHasSuffix { public bool Read(string path) => Path.HasExtension(path); }
    internal sealed class GuardSeparator { public char Read() => Path.DirectorySeparatorChar; }
    internal sealed class GuardAlternateSeparator { public char Read() => Path.AltDirectorySeparatorChar; }
    internal sealed class GuardFullPath { public string Read(string path) => Path.GetFullPath(path); }
    internal sealed class GuardTempPath { public string Read() => Path.GetTempPath(); }
    internal sealed class GuardTempFile { public string Read() => Path.GetTempFileName(); }
    internal sealed class GuardRandomFile { public string Read() => Path.GetRandomFileName(); }
    internal sealed class GuardUnlistedPath { public bool Read(string path) => Path.IsPathRooted(path); }
    internal sealed class GuardFileReader { public string Read(string path) => File.ReadAllText(path); }
    internal sealed class GuardDirectoryReader { public bool Read(string path) => Directory.Exists(path); }
    internal sealed class GuardFileMetadata { public object Read(string path) => new FileInfo(path); }
    internal sealed class GuardDirectoryMetadata { public object Read(string path) => new DirectoryInfo(path); }
    internal sealed class GuardStreamSignature { public Stream? Value { get; init; } }
    internal sealed class GuardStreamGeneric { public List<Stream> Values { get; } = []; }
    internal sealed class GuardMemoryStreamCreator { public object Create() => new MemoryStream(); }
    internal sealed class GuardInvalidData { public void Fail() => throw new InvalidDataException("fixture"); }
    internal sealed class GuardLambdaPath { public Func<string> Read() => () => Path.GetTempPath(); }
    internal sealed class GuardAsyncPath { public async Task<string> Read(string path) { await Task.Yield(); return Path.GetFullPath(path); } }
    internal sealed class GuardLambdaFile { public Func<string> Read(string path) => () => File.ReadAllText(path); }
    internal sealed class GuardAsyncFile { public async Task<string> Read(string path) { await Task.Yield(); return await File.ReadAllTextAsync(path); } }
    internal sealed class GuardCatchIo { public void Read(Action action) { try { action(); } catch (IOException) { } } }
    internal sealed class GuardTypeToken { public Type Read() => typeof(FileInfo); }
    internal sealed class GuardFileDelegate { public Func<string, string> Read() => File.ReadAllText; }
    internal sealed class GuardPureLambdaPath { public Func<string> Read(string path) => () => Path.GetFileName(path); }
    internal sealed class GuardPureAsyncPath { public async Task<string> Read(string path) { await Task.Yield(); return Path.GetFileName(path); } }
}
