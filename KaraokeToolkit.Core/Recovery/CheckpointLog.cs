using System.Text;
using System.Text.Json;

namespace KaraokeToolkit.Core.Recovery;

/// <summary>Checksummed, newline-terminated checkpoints with a durable flush per record.</summary>
internal sealed class CheckpointLog<T> : IDisposable where T : class
{
    private const int ChecksumLength = 64;
    private readonly FileStream stream;
    private bool faulted;

    private CheckpointLog(FileStream stream) => this.stream = stream;

    internal static CheckpointLog<T> Create(string path)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        try { stream.Flush(flushToDisk: true); return new(stream); }
        catch { stream.Dispose(); throw; }
    }

    internal static CheckpointLog<T> Open(string path, int committedLength)
    {
        RunSession.CheckPlainPath(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
        try
        {
            if (stream.Length < committedLength) throw InvalidRecord();
            if (stream.Length > committedLength) stream.SetLength(committedLength);
            stream.Position = committedLength;
            return new(stream);
        }
        catch { stream.Dispose(); throw; }
    }

    internal void Append(T record)
    {
        ThrowIfFaulted();
        try
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(record, DurableJson.Settings);
            stream.Write(Encoding.ASCII.GetBytes(RecoveryFingerprint.HashBytes(payload)));
            stream.WriteByte((byte)' ');
            stream.Write(payload);
            stream.WriteByte((byte)'\n');
            stream.Flush(flushToDisk: true);
        }
        catch { faulted = true; throw; }
    }

    internal void ThrowIfFaulted()
    {
        if (faulted) throw new IOException("A checkpoint write failed. Reopen recovery before saving more progress.");
    }

    internal static int Replay(string path, Action<T> apply)
    {
        RunSession.CheckPlainPath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        int offset = 0;
        while (offset < bytes.Length)
        {
            // A missing terminator leaves an uncommitted tail to truncate on resume.
            int end = bytes.AsSpan(offset).IndexOf((byte)'\n');
            if (end < 0) break;
            var line = bytes.AsSpan(offset, end);
            if (line.Length <= ChecksumLength || line[ChecksumLength] != (byte)' ') throw InvalidRecord();
            var payload = line[(ChecksumLength + 1)..];
            if (Encoding.ASCII.GetString(line[..ChecksumLength]) != RecoveryFingerprint.HashBytes(payload)) throw InvalidRecord();
            apply(JsonSerializer.Deserialize<T>(payload, DurableJson.Settings) ?? throw InvalidRecord());
            offset += end + 1;
        }
        return offset;
    }

    private static IOException InvalidRecord() => new("Invalid recovery checkpoint record. Keep the journal for manual review.");

    public void Dispose() => stream.Dispose();
}
