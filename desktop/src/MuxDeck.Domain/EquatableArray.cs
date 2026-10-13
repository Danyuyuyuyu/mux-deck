using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace MuxDeck.Domain;

/// <summary>不可变集合快照；默认值在读取、枚举、相等与哈希中均按空集合处理。</summary>
[CollectionBuilder(typeof(EquatableArray), nameof(EquatableArray.Create))]
public readonly struct EquatableArray<T> : IReadOnlyList<T>, IEquatable<EquatableArray<T>>
{
    private readonly ImmutableArray<T> values;

    public EquatableArray(ImmutableArray<T> values) => this.values = values;
    public EquatableArray(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = ImmutableArray.CreateRange(values);
    }

    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);
    // 保留初始化标志，供旧转换边界继续执行原有显式初始化校验。
    public bool IsDefault => values.IsDefault;
    public int Length => values.IsDefault ? 0 : values.Length;
    public int Count => Length;
    public bool IsEmpty => Length == 0;
    public T this[int index] => AsImmutableArray()[index];
    public ImmutableArray<T> AsImmutableArray() => values.IsDefault ? ImmutableArray<T>.Empty : values;
    public ImmutableArray<T>.Enumerator GetEnumerator() => AsImmutableArray().GetEnumerator();
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)AsImmutableArray()).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

    public bool Equals(EquatableArray<T> other)
    {
        if (Length != other.Length) return false;
        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < Length; index++)
            if (!comparer.Equals(this[index], other[index])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);
    public override int GetHashCode()
    {
        if (IsEmpty) return 0;
        var hash = new HashCode();
        foreach (var value in this) hash.Add(value, EqualityComparer<T>.Default);
        return hash.ToHashCode();
    }
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

public static class EquatableArray
{
    public static EquatableArray<T> Create<T>(ReadOnlySpan<T> values) =>
        new(ImmutableArray.CreateRange(values.ToArray()));
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> values) => new(values);
}
