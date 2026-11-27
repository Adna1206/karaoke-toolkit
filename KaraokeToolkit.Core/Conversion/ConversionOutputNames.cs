using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Video;

namespace KaraokeToolkit.Core.Conversion;

/// <summary>Outputs whose paths can be known without reading or encoding media.</summary>
internal static class ConversionOutputNames
{
    internal static string[] Known(string source, ConversionRequest request)
    {
        string destination = string.IsNullOrEmpty(request.OutputFolder) ? Path.GetDirectoryName(source)! : request.OutputFolder;
        string stem = Path.Combine(destination, Path.GetFileNameWithoutExtension(source));
        return request.Action switch
        {
            OperationId.AudioGToBin or OperationId.ZipToBin => [stem + ".bin"],
            OperationId.AudioGToZip or OperationId.BinToZip => [stem + ".zip"],
            OperationId.ArchiveToZip => [Path.Combine(destination, ArchiveZipConverter.OutputName(source))],
            OperationId.RepackZip => [request.Options.DeleteOldFiles ? source : stem + " (repacked).zip"],
            OperationId.BinToAudioG => [stem + AudioEncoderService.Extension(request.Options.AudioEncoder), stem + ".cdg"],
            OperationId.LosslessGToMp3G => [stem + ".mp3", string.IsNullOrEmpty(request.OutputFolder) ? source : stem + ".cdg"],
            OperationId.KmaToMp3G => [stem + ".mp3", stem + ".cdg"],
            OperationId.ZipToAudioG => [stem + ".cdg"],
            OperationId.KaraokeToVideo => [stem + VideoOptions.Extension(request.Options.VideoContainer)],
            _ => throw new NotSupportedException("No conversion plan is registered.")
        };
    }
}
