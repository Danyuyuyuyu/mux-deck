using MuxDeck.Contracts;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Application.Tests;

public sealed class InputLocationContractTests
{
    [Theory]
    [InlineData(InputLocationKind.LocalFile)]
    [InlineData(InputLocationKind.LocalDirectory)]
    [InlineData(InputLocationKind.NonLocal)]
    [InlineData(InputLocationKind.Unknown)]
    public void Import_items_share_the_domain_location_kind(InputLocationKind kind)
    {
        var item = new InputItem("input", kind);
        Assert.Equal(kind, item.Kind);
        Assert.Equal(item, new InputItem("input", kind));
        Assert.Equal(item.GetHashCode(), new InputItem("input", kind).GetHashCode());
    }

    [Fact]
    public void Contracts_have_no_duplicate_input_kind_enum()
    {
        Assert.Equal(typeof(InputLocationKind), typeof(InputItem).GetProperty(nameof(InputItem.Kind))!.PropertyType);
        Assert.Null(typeof(InputItem).Assembly.GetType("MuxDeck.Contracts.InputItemKind"));
        Assert.NotEqual(new InputItem("input", InputLocationKind.LocalFile),
            new InputItem("input", InputLocationKind.LocalDirectory));
    }
}
