using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Execution.OperationId;
namespace KaraokeToolkit.Tests.Conversion;

public class RequestTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void RejectsUnknownAction(int action) => Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionRequest((Action)action, ".", new()).Validate());

    [Theory]
    [InlineData(Action.PlayAudioG)]
    [InlineData(Action.CheckForUpdates)]
    public void HostActionsAreNotSilentlyReportedAsBatchSuccess(Action action) => Assert.Throws<NotSupportedException>(() => new ConversionRequest(action, ".", new()).Validate());

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void RejectsInvalidCompression(int level) => Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionRequest(Action.AudioGToZip, ".", new() { ZipCompression = (ZipCompression)level }).Validate());

    [Fact]
    public void RequiresSourceFolder() => Assert.Throws<ArgumentException>(() => new ConversionRequest(Action.AudioGToBin, "", new()).Validate());

    [Theory]
    [InlineData(AudioEncoder.WAV)]
    [InlineData(AudioEncoder.FLAC)]
    public void LosslessBinOutputDoesNotRequireBitrate(AudioEncoder encoder) =>
        new ConversionRequest(Action.BinToZip, ".", new() { AudioEncoder = encoder, AudioBitrate = 0 }).Validate();

    [Fact]
    public void DefaultsMatchCurrentEncodingPreferences()
    {
        var o = new ConversionOptions();
        Assert.Equal(320, o.AudioBitrate); Assert.Equal(AudioEncoder.MP3, o.AudioEncoder);
        Assert.True(o.DeleteOldFiles); Assert.True(o.RecurseFolders); Assert.True(o.AutoCreateCueText);
        Assert.True(o.AutoRenameZip); Assert.True(o.RenameBins); Assert.Equal(ZipCompression.Balanced, o.ZipCompression);
        Assert.Equal("DISC-TRACK ARTIST - TITLE", o.NamingFormat);
        Assert.Equal(VideoContainer.MP4, o.VideoContainer);
        Assert.Equal(VideoResolution.HD, o.VideoResolution);
        Assert.Equal(30, o.VideoFrameRate);
    }

    [Fact]
    public void VideoOptionsAreValidatedForVideoConversion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionRequest(Action.KaraokeToVideo, ".", new() { VideoFrameRate = 24 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionRequest(Action.KaraokeToVideo, ".", new() { VideoContainer = (VideoContainer)99 }).Validate());
    }
}
