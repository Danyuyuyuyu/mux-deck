namespace MuxDeck.Domain;

public static class TrackPolicy
{
    // 旧草稿的空槽位先展示 SC 默认；输入组合发生变化后才按实际槽位重算。
    public const bool InitialScDefault = true;
    public const bool InitialTcDefault = false;

    public static (bool Sc, bool Tc) SubtitleDefaults(bool hasSc, bool hasTc) =>
        (hasSc, !hasSc && hasTc);

    public static bool RetainsSource(TrackKind kind) =>
        kind is TrackKind.Video or TrackKind.Audio;

    // 源行按固定保留策略投影，不能读外加轨道的可编辑纳入值。
    public static bool IsIncluded(bool isExternal, bool externalIncluded, bool sourceRetained) =>
        isExternal ? externalIncluded : sourceRetained;
}
