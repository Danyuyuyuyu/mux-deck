namespace MuxDeck.Domain;

public static class OutputQcRules
{
    public static TrackQcReport CheckFacts(MediaQcFacts source, MediaQcFacts output,
        EquatableArray<SubtitleQcFacts> subtitleInputs, EquatableArray<AudioQcFacts> audioInputs,
        EquatableArray<MediaQcFacts> audioSources)
    {
        var checks = new List<TrackQcCheck>();
        var failure = ValidateTracks(source, output, subtitleInputs, audioInputs, audioSources, checks);
        return new(new(checks), failure);
    }

    private static TrackQcFailure? ValidateTracks(MediaQcFacts source, MediaQcFacts output,
        EquatableArray<SubtitleQcFacts> subtitleInputs, EquatableArray<AudioQcFacts> audioInputs,
        EquatableArray<MediaQcFacts> audioSources, List<TrackQcCheck> checks)
    {
        foreach (var (kind, type) in new[] { (TrackKind.Video, "video"), (TrackKind.Audio, "audio") })
        {
            var before = source.Tracks.Where(x => x.Kind == kind).ToArray();
            var after = output.Tracks.Where(x => x.Kind == kind).ToArray();
            if (before.Length + (kind == TrackKind.Audio ? audioInputs.Count : 0) != after.Length) return new(TrackQcFailureKind.SourceCount, kind);
            for (var i = 0; i < before.Length; i++)
                // MP4 等容器不提供 Matroska codec_id/旗标；已知源字段严格比较，允许输出补齐缺失字段。
                if (before[i].Codec != after[i].Codec || (before[i].CodecId is not null && before[i].CodecId != after[i].CodecId) ||
                    (before[i].PixelDimensions is not null && before[i].PixelDimensions != after[i].PixelDimensions) ||
                    (before[i].AudioChannels is not null && before[i].AudioChannels != after[i].AudioChannels) ||
                    (before[i].AudioSamplingFrequency is not null && before[i].AudioSamplingFrequency != after[i].AudioSamplingFrequency) ||
                    before[i].Name != after[i].Name || before[i].Language != after[i].Language ||
                    (before[i].LanguageIetf is not null && !string.Equals(before[i].LanguageIetf?.Value, after[i].LanguageIetf?.Value, StringComparison.OrdinalIgnoreCase)) ||
                    (before[i].HasDefaultFlag && before[i].Flags.IsDefault != after[i].Flags.IsDefault) ||
                    (before[i].HasForcedFlag && before[i].Flags.IsForced != after[i].Flags.IsForced))
                    return new(TrackQcFailureKind.SourceMetadata, kind, i + 1);
            checks.Add(new(TrackQcCheckKind.Retained, kind, 0, before.Length, null));
        }
        // 只有所有 A/V 输入声明时长时才能预测总时长；不能拿字幕尾时间或未知 MP4 时长代替源证据。
        long? expectedDuration = source.DurationNanoseconds;
        if (audioSources.Count > 0)
            expectedDuration = source.DurationNanoseconds is not null && audioSources.All(p => p.DurationNanoseconds is not null)
                ? new[] { source.DurationNanoseconds.Value }.Concat(audioSources.Select(p => p.DurationNanoseconds!.Value)).Max() : null;
        if (expectedDuration is { } duration && output.DurationNanoseconds is { } outputDuration &&
            Math.Abs((double)duration - outputDuration) > Math.Max(1_000_000_000, duration * 0.01))
            return new(TrackQcFailureKind.Duration);
        var subtitles = output.Tracks.Where(x => x.Kind == TrackKind.Subtitle).ToArray();
        if (subtitles.Length != subtitleInputs.Count) return new(TrackQcFailureKind.SubtitleCount);
        for (var i = 0; i < subtitles.Length; i++)
        {
            var actual = subtitles[i];
            var expected = subtitleInputs[i];
            if (actual.CodecId != (expected.IsAss ? "S_TEXT/ASS" : "S_TEXT/UTF8") || actual.Name != expected.Name ||
                !LanguageRules.Matches(actual.Language.Value, actual.LanguageIetf?.Value, expected.Language) || actual.Flags.IsDefault != expected.IsDefault || actual.Flags.IsForced != expected.IsForced)
                return new(TrackQcFailureKind.SubtitleMetadata, TrackKind.Subtitle, i + 1);
            checks.Add(new(TrackQcCheckKind.Subtitle, TrackKind.Subtitle, i + 1, 1, actual));
        }
        return CheckExternalAudio(source, output, audioInputs, audioSources, checks);
    }

    private static TrackQcFailure? CheckExternalAudio(MediaQcFacts source, MediaQcFacts output,
        EquatableArray<AudioQcFacts> audioInputs, EquatableArray<MediaQcFacts> audioSources, List<TrackQcCheck> checks)
    {
        var added = output.Tracks.Where(t => t.Kind == TrackKind.Audio).Skip(source.Tracks.Count(t => t.Kind == TrackKind.Audio)).ToArray();
        for (var i = 0; i < audioInputs.Count; i++)
        {
            var input = audioInputs[i]; var before = audioSources[i].Tracks.Single(t => t.Kind == TrackKind.Audio && t.Id.Value == input.TrackId); var actual = added[i];
            if (before.Codec != actual.Codec || (before.CodecId is not null && before.CodecId != actual.CodecId) ||
                (before.AudioChannels is not null && before.AudioChannels != actual.AudioChannels) ||
                (before.AudioSamplingFrequency is not null && before.AudioSamplingFrequency != actual.AudioSamplingFrequency) ||
                actual.Name != input.Name || !LanguageRules.Matches(actual.Language.Value, actual.LanguageIetf?.Value, input.Language) || actual.Flags.IsDefault != input.IsDefault || actual.Flags.IsForced != input.IsForced)
                return new(TrackQcFailureKind.AudioMetadata, TrackKind.Audio, i + 1);
            checks.Add(new(TrackQcCheckKind.AddedAudio, TrackKind.Audio, i + 1, 1, actual));
        }
        return null;
    }

    // 只统一运输层换行；Dialogue 内容、顺序及边缘空格都是正文，不能被丢弃。
    public static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");
    public static bool SubtitleContentEquals(string reference, string actual) =>
        NormalizeLineEndings(reference) == NormalizeLineEndings(actual);
}
