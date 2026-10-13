using NetArchTest.Rules;
using Xunit;

namespace MuxDeck.Architecture.Tests
{
    public sealed class DependencyScannerTests
    {
        // 反例只存在测试程序集，验证 IL 扫描能发现字段、泛型、构造和异步方法中的依赖。
        [Theory]
        [InlineData(typeof(Fixtures.FieldConsumer))]
        [InlineData(typeof(Fixtures.GenericConsumer))]
        [InlineData(typeof(Fixtures.ConstructorConsumer))]
        [InlineData(typeof(Fixtures.AsyncConsumer))]
        public void Scanner_detects_forbidden_dependencies(Type consumer)
        {
            var result = Types.InAssembly(typeof(DependencyScannerTests).Assembly).That()
                .HaveName(consumer.Name).ShouldNot()
                .HaveDependencyOn("MuxDeck.Architecture.Tests.Fixtures.Infrastructure").GetResult();
            Assert.False(result.IsSuccessful);
            Assert.Contains(consumer.FullName!, result.FailingTypeNames);
        }

        [Fact]
        public void Scanner_accepts_a_consumer_without_forbidden_dependencies()
        {
            ArchitectureScope.AssertSuccess(Types.InAssembly(typeof(DependencyScannerTests).Assembly).That()
                .HaveName(nameof(Fixtures.CleanConsumer)).ShouldNot()
                .HaveDependencyOn("MuxDeck.Architecture.Tests.Fixtures.Infrastructure").GetResult());
        }
    }
}

namespace MuxDeck.Architecture.Tests.Fixtures.Infrastructure
{
    internal sealed class Dependency;
}

namespace MuxDeck.Architecture.Tests.Fixtures
{
    internal sealed class FieldConsumer
    {
        public Infrastructure.Dependency? Value = null;
    }

    internal sealed class GenericConsumer
    {
        public List<Infrastructure.Dependency> Values { get; } = [];
    }

    internal sealed class ConstructorConsumer
    {
        public object Create() => new Infrastructure.Dependency();
    }

    internal sealed class AsyncConsumer
    {
        public async Task<object> CreateAsync()
        {
            await Task.Yield();
            return new Infrastructure.Dependency();
        }
    }

    internal sealed class CleanConsumer
    {
        public string Value => "clean";
    }
}
