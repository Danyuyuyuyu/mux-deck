using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace MuxDeck.Architecture.Tests;

public sealed class CompositionEntryRuleTests
{
    [Theory]
    [InlineData("MuxDeck.Desktop", "Program", "Program.cs", true)]
    [InlineData("MuxDeck.Desktop.Composition", "Factory", "Composition/Factory.cs", true)]
    [InlineData("MuxDeck.Desktop.Composition.Nested", "Factory", "Composition/Nested/Factory.cs", true)]
    [InlineData("MuxDeck.Desktop", "Program", "Views/Program.cs", false)]
    [InlineData("MuxDeck.Desktop.Composition", "Factory", "Views/Factory.cs", false)]
    [InlineData("MuxDeck.Desktop.Views", "Factory", "Composition/Factory.cs", false)]
    [InlineData("MuxDeck.Desktop", "Other", "Program.cs", false)]
    [InlineData("MuxDeck.Desktop.Composition", "Factory", "CompositionOther/Factory.cs", false)]
    public void Composition_requires_both_allowed_type_and_real_source_path(string ns, string name, string path, bool expected)
    {
        using var fixture = new SymbolFixture(ns, name, path, true);
        var rule = new CompositionEntryRule(fixture.Root);
        Assert.Equal(expected, rule.MeetsRule(fixture.Type));
        if (!expected) Assert.False(string.IsNullOrWhiteSpace(rule.FailureReason));
    }

    [Fact]
    public void Missing_pdb_reports_a_clear_failure_instead_of_throwing()
    {
        using var fixture = new SymbolFixture("MuxDeck.Desktop", "Program", "Program.cs", false);
        var rule = new CompositionEntryRule(fixture.Root);
        Assert.False(rule.MeetsRule(fixture.Type));
        Assert.Contains("缺少 PDB", rule.FailureReason!);
    }

    [Fact]
    public void Mapped_source_path_reports_a_clear_failure_instead_of_throwing()
    {
        using var fixture = new SymbolFixture("MuxDeck.Desktop", "Program", "/_/src/MuxDeck.Desktop/Program.cs", true);
        var rule = new CompositionEntryRule(fixture.Root);
        Assert.False(rule.MeetsRule(fixture.Type));
        Assert.Contains("源码路径已映射为 /_/", rule.FailureReason!);
    }

    [Fact]
    public void A_partial_type_cannot_hide_an_extra_forbidden_source_file()
    {
        using var fixture = new SymbolFixture("MuxDeck.Desktop.Composition", "Factory", "Composition/Factory.cs",
            true, "Views/Factory.Partial.cs");
        var rule = new CompositionEntryRule(fixture.Root);
        Assert.False(rule.MeetsRule(fixture.Type));
        Assert.Contains("Views", rule.FailureReason!);
    }

    // 反例程序集和便携 PDB 仅在本测试拥有的唯一 artifacts 子目录内生成。
    private sealed class SymbolFixture : IDisposable
    {
        private readonly ModuleDefinition module;
        internal string Root { get; } = Path.Combine(ArchitectureScope.Root, "artifacts", "arch-corrections",
            "symbol-fixtures", Guid.NewGuid().ToString("N"));
        internal TypeDefinition Type { get; }

        internal SymbolFixture(string ns, string name, string path, bool pdb, string? extraPath = null)
        {
            Directory.CreateDirectory(Root);
            var file = Path.Combine(Root, "Fixture.dll");
            using (var source = ModuleDefinition.CreateModule("Fixture", ModuleKind.Dll))
            {
                var type = new TypeDefinition(ns, name, TypeAttributes.Public | TypeAttributes.Class, source.TypeSystem.Object);
                source.Types.Add(type);
                AddMethod(source, type, "Create", DocumentPath(path));
                if (extraPath is not null) AddMethod(source, type, "Extra", DocumentPath(extraPath));
                source.Write(file, new WriterParameters { WriteSymbols = pdb,
                    SymbolWriterProvider = pdb ? new PortablePdbWriterProvider() : null });
            }
            module = ModuleDefinition.ReadModule(file);
            Type = module.GetType(ns + "." + name);
        }

        private string DocumentPath(string path) => path.StartsWith("/_/", StringComparison.Ordinal) ? path :
            Path.GetFullPath(Path.Combine(Root, "src", "MuxDeck.Desktop", path));

        private static void AddMethod(ModuleDefinition module, TypeDefinition type, string name, string document)
        {
            var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            type.Methods.Add(method);
            var first = Instruction.Create(OpCodes.Nop);
            method.Body.Instructions.Add(first);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            method.DebugInformation.SequencePoints.Add(new SequencePoint(first, new Document(document))
                { StartLine = 1, StartColumn = 1, EndLine = 1, EndColumn = 2 });
        }

        public void Dispose()
        {
            module.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }
}
