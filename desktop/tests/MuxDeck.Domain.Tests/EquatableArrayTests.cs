using System.Collections.Immutable;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Domain.Tests;

public sealed class EquatableArrayTests
{
    [Fact]
    public void Separate_arrays_with_equal_elements_have_value_equality_and_equal_hashes()
    {
        EquatableArray<string?> first = ["a", null, "b"];
        EquatableArray<string?> second = ["a", null, "b"];
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.True(first.Equals((object)second));
    }

    [Fact]
    public void Default_is_empty_for_reading_enumeration_equality_and_hash()
    {
        var value = default(EquatableArray<int>);
        Assert.Equal(EquatableArray<int>.Empty, value);
        Assert.Equal(new EquatableArray<int>(default(ImmutableArray<int>)), value);
        Assert.True(value.Count == value.Length);
        Assert.True(value.IsEmpty);
        Assert.Empty(value);
        Assert.Equal(EquatableArray<int>.Empty.GetHashCode(), value.GetHashCode());
        Assert.Throws<IndexOutOfRangeException>(() => value[0]);
    }

    [Theory]
    [InlineData(new[] { 1, 2 }, new[] { 2, 1 })]
    [InlineData(new[] { 1, 2 }, new[] { 1 })]
    [InlineData(new[] { 1, 2 }, new[] { 1, 3 })]
    public void Different_order_length_or_element_is_not_equal(int[] first, int[] second)
    {
        Assert.NotEqual(new EquatableArray<int>(first), new EquatableArray<int>(second));
    }

    [Fact]
    public void Construction_copies_mutable_input()
    {
        var source = new[] { 1, 2 };
        var value = new EquatableArray<int>(source);
        source[0] = 9;
        Assert.Equal(new[] { 1, 2 }, value.ToArray());
        Assert.False(value.Equals("other"));
    }
}
