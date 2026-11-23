namespace KaraokeToolkit.Core.Audio;

public enum AudioGainMode { Loudness, Peak, Offset }

public sealed record AudioGainOptions
{
    public AudioGainMode Mode { get; init; } = AudioGainMode.Loudness;
    public double TargetLufs { get; init; } = -16;
    public double PeakCeilingDbtp { get; init; } = -1;
    public double OffsetDb { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentException("Choose a gain mode.");
        if (!double.IsFinite(TargetLufs) || TargetLufs is < -70 or > -5)
            throw new ArgumentException("Loudness target must be between -70 and -5 LUFS.");
        if (!double.IsFinite(PeakCeilingDbtp) || PeakCeilingDbtp is < -30 or > 0)
            throw new ArgumentException("True peak must be between -30 and 0 dBTP.");
        if (!double.IsFinite(OffsetDb) || OffsetDb is < -60 or > 60)
            throw new ArgumentException("Gain adjustment must be between -60 and +60 dB.");
    }
}

public sealed record AudioKeyOptions
{
    public int Semitones { get; init; }

    public void Validate()
    {
        if (Semitones is < -12 or > 12) throw new ArgumentException("Choose between −12 and +12 semitones.");
    }
}

public enum AudioGainStatus { Ready, Limited, Skipped, Blocked }

public sealed record AudioGainRow(string Source, string? Output, string? GraphicsOutput, double? CurrentLufs,
    double? CurrentPeakDbtp, double? EstimatedLufs, double? EstimatedPeakDbtp, double? GainDb,
    AudioGainStatus Status, string Message)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? Semitones { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TimeSpan? SourceDuration { get; init; }
}
