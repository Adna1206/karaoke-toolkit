namespace KaraokeToolkit.Core.DiscRipping;

public sealed record DiscTrack(int Number, long AudioBytes)
{
    public long BinBytes => checked(AudioBytes / 2352 * 2448);
    public override string ToString() => $"Track {Number:00} ({TimeSpan.FromSeconds(AudioBytes / 176400d):mm\\:ss})";
}
public sealed record OpticalDisc(int Drive, string Name, bool SupportsCdg, string Identity, DiscTrack[] Tracks)
{
    /// <summary>Stable cdrdao device address; Drive remains the display/list index.</summary>
    public string? Device { get; init; }
    public override string ToString() => Name + (!SupportsCdg ? " — CD+G unsupported" : Tracks.Length == 0 ? " — no audio disc" : "");
}

/// <summary>Owns a disposable disc image shared by the selected tracks of one run.</summary>
internal interface IDiscBatchReader
{
    IAsyncDisposable BeginBatch(OpticalDisc disc);
}

/// <summary>The hardware boundary: reads audio plus subchannel sectors, never publishes user files.</summary>
public interface IDiscReader
{
    Task<IReadOnlyList<OpticalDisc>> ScanAsync(CancellationToken token);
    Task ReadTrackAsync(OpticalDisc disc, DiscTrack track, string stagedBin, IProgress<int>? progress, CancellationToken token);
}
public enum RipFormat { Bin, AudioG, Zip }
