using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.Core.Settings;

/// <summary>Saved encoding bitrates for bitrate-controlled output codecs.</summary>
public sealed record AudioBitratePreferences(int Mp3 = 128, int Ogg = 128, int Wma = 128, int Aac = 128)
{
    public int For(AudioEncoder encoder) => encoder switch
    {
        AudioEncoder.MP3 => Mp3,
        AudioEncoder.OGG => Ogg,
        AudioEncoder.WMA => Wma,
        AudioEncoder.AAC => Aac,
        _ => throw new ArgumentOutOfRangeException(nameof(encoder), "This codec does not use a bitrate setting.")
    };

    public AudioBitratePreferences With(AudioEncoder encoder, int bitrate) => encoder switch
    {
        AudioEncoder.MP3 => this with { Mp3 = bitrate },
        AudioEncoder.OGG => this with { Ogg = bitrate },
        AudioEncoder.WMA => this with { Wma = bitrate },
        AudioEncoder.AAC => this with { Aac = bitrate },
        _ => this
    };

    public void Validate()
    {
        if (!AudioEncoderService.SupportedBitrates.Contains(Mp3) ||
            !AudioEncoderService.SupportedBitrates.Contains(Ogg) ||
            !AudioEncoderService.SupportedBitrates.Contains(Wma) ||
            !AudioEncoderService.SupportedBitrates.Contains(Aac))
            throw new ArgumentOutOfRangeException(nameof(AudioBitratePreferences), "Select supported codec bitrates.");
    }
}
