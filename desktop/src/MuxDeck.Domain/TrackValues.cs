namespace MuxDeck.Domain;

public enum TrackKind { Unknown, Video, Audio, Subtitle, Attachment }

public readonly record struct TrackId(int Value);

public readonly record struct TrackFlags(bool IsDefault, bool IsForced);

/// <summary>保留调用方提供的原始语言标签；规范化与匹配规则在后续 Domain 迁移中实现。</summary>
public readonly record struct LanguageTag
{
    private readonly string? value;

    public LanguageTag(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        this.value = value;
    }

    public string Value => value ?? string.Empty;
    public bool Equals(LanguageTag other) => StringComparer.Ordinal.Equals(Value, other.Value);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;
}
