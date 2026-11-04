namespace KaraokeToolkit.Tests.Support;

/// <summary>Runs progress callbacks inline so cancellation tests do not depend on dispatch timing.</summary>
internal sealed class SynchronousProgress<T>(System.Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
