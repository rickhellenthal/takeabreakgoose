using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace TakeABreakGoose;

/// <summary>
/// Keeps to one goose at a time. Opening the .app again from Finder or Launchpad doesn't start a
/// second copy (macOS brings the running one forward instead, see App), but running it with
/// `open -n` or from Terminal does: that copy asks the first one to open its window, and quits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string PipeName = "TakeABreakGoose";

    private readonly FileStream? _lock;
    private bool _disposed;

    /// <summary>A second copy asked this one to open its window.</summary>
    public event Action? OpenRequested;

    /// <param name="folder">Where the lock file goes (the settings folder).</param>
    public SingleInstance(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            // Locked for as long as this copy runs; macOS lets go of it when the app quits or crashes.
            _lock = new FileStream(Path.Combine(folder, "running.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            _lock = null;
        }
        if (IsFirst) Task.Run(ListenAsync);
    }

    public bool IsFirst => _lock != null;

    /// <summary>From the second copy: asks the first one to open its window. Gives up after a moment.</summary>
    public static void AskFirstToOpen()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(timeout: 2000);
            pipe.WriteByte(1);
        }
        catch (Exception)
        {
            // The first copy is just starting or stopping; it's there in the menu bar either way.
        }
    }

    private async Task ListenAsync()
    {
        while (!_disposed)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In);
                await pipe.WaitForConnectionAsync();
                if (pipe.ReadByte() == 1) Dispatcher.UIThread.Post(() => OpenRequested?.Invoke());
            }
            catch (Exception)
            {
                // Listening is a convenience: without it, a second copy just quits.
                return;
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _lock?.Dispose();
    }
}
