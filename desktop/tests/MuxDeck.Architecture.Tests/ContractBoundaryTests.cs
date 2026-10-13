using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace MuxDeck.Architecture.Tests;

public sealed class ContractBoundaryTests
{
    [Fact]
    public void Contracts_have_only_system_domain_and_contract_dependencies()
    {
        var types = ArchitectureScope.ProductionTypes().That().ResideInNamespace("MuxDeck.Contracts");
        Assert.NotEmpty(types.GetTypes());
        ArchitectureScope.AssertSuccess(types.Should()
            .OnlyHaveDependenciesOn("System", "MuxDeck.Domain", "MuxDeck.Contracts").GetResult());
    }

    [Fact]
    public void All_contract_types_are_public()
    {
        var types = ArchitectureScope.ProductionTypes().That().ResideInNamespace("MuxDeck.Contracts");
        Assert.NotEmpty(types.GetTypes());
        ArchitectureScope.AssertSuccess(types.Should().BePublic().GetResult());
    }

    [Fact]
    public void Contracts_are_owned_by_application_and_have_no_friend_assemblies()
    {
        var assembly = Assembly.Load("MuxDeck.Application");
        Assert.All(ArchitectureScope.ProductionTypes().GetTypes()
            .Where(type => ArchitectureScope.InNamespace(type.Namespace, "MuxDeck.Contracts")),
            type => Assert.Equal(assembly, type.Assembly));
        Assert.DoesNotContain(assembly.GetCustomAttributesData(),
            attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.InternalsVisibleToAttribute");
    }

    [Fact]
    public void Contract_data_is_record_or_enum_with_no_mutable_collections_or_setters()
    {
        var assembly = Assembly.Load("MuxDeck.Application");
        foreach (var type in assembly.GetTypes().Where(type =>
                     ArchitectureScope.InNamespace(type.Namespace, "MuxDeck.Contracts")))
            Assert.Null(ContractDataRule.Failure(type));
    }
}
