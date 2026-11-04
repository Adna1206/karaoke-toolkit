using KaraokeToolkit.Core.Conversion;
using System.IO.Compression;
using System.Security.Cryptography;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;
namespace KaraokeToolkit.Tests.Support;

/// <summary>Deterministic media bytes and snapshots; no reference engine or helper process is needed.</summary>
internal static class TestMediaFixture
{
    internal static string NewFolder()
    {
        var path = TestArtifacts.NewPath();
        Directory.CreateDirectory(path); return path;
    }
    internal static void Seed(string folder, Action action, string mode)
    {
        Directory.CreateDirectory(folder);
        const int sectors = 150;
        byte[] pcm = new byte[sectors * 2352], cdg = new byte[sectors * 96];
        for(int i=0; i<pcm.Length/4; i++) { short v=(short)(5000*Math.Sin(i*2*Math.PI*440/44100)); BitConverter.GetBytes(v).CopyTo(pcm,i*4); BitConverter.GetBytes(v).CopyTo(pcm,i*4+2); }
        for(int i=0; i<cdg.Length; i++) cdg[i]=(byte)(i%64);
        using var wave = new MemoryStream();
        using(var writer = new BinaryWriter(wave, System.Text.Encoding.UTF8, true)) {
            writer.Write("RIFF"u8.ToArray()); writer.Write(pcm.Length+36); writer.Write("WAVEfmt "u8.ToArray()); writer.Write(16); writer.Write((short)1); writer.Write((short)2);
            writer.Write(44100); writer.Write(176400); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8.ToArray()); writer.Write(pcm.Length); writer.Write(pcm);
        }
        if(action == Action.AudioG_To_Bin) {
            if(mode != "missing-audio") File.WriteAllBytes(Path.Combine(folder,"song.wav"), mode == "invalid-audio" ? new byte[]{1,2,3} : wave.ToArray());
            if(mode != "missing-graphics") File.WriteAllBytes(Path.Combine(folder,"song.cdg"), mode == "short-graphics" ? cdg[..100] : cdg);
            if(mode == "overwrite") File.WriteAllBytes(Path.Combine(folder,"song.bin"), new byte[(sectors+5)*2448]);
        } else if(action == Action.Zip_To_Bin) {
            using var zip=ZipFile.Open(Path.Combine(folder,"song.zip"),ZipArchiveMode.Create);
            using(var entry=zip.CreateEntry("song.wav").Open()) entry.Write(wave.ToArray());
            using(var entry=zip.CreateEntry("song.cdg").Open()) entry.Write(cdg);
            if(mode == "overwrite") File.WriteAllBytes(Path.Combine(folder,"song.bin"),new byte[(sectors+5)*2448]);
        } else {
            byte[] bin=new byte[sectors*2448];
            for(int i=0;i<sectors;i++) { Array.Copy(pcm,i*2352,bin,i*2448,2352); Array.Copy(cdg,i*96,bin,i*2448+2352,96); }
            File.WriteAllBytes(Path.Combine(folder,"song.bin"),mode == "truncated-bin" ? bin[..123] : bin);
            if(mode == "overwrite") {File.WriteAllBytes(Path.Combine(folder,"song.wav"),wave.ToArray()); File.WriteAllBytes(Path.Combine(folder,"song.cdg"),cdg);}
        }
    }
    internal static SortedDictionary<string,string> Snapshot(string folder)
    {
        var result=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories)) {
            string key=Path.GetRelativePath(folder,file);
            if(Path.GetExtension(file)==".zip") {
                using var zip=ZipFile.OpenRead(file);
                foreach(var entry in zip.Entries) {using var stream=entry.Open();result[key+"!"+entry.FullName]=Convert.ToHexString(SHA256.HashData(stream));}
            } else result[key]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        }
        return result;
    }
}

