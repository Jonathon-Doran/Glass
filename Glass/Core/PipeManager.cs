using System.IO.Pipes;
using System.Text;

namespace Glass.Core;

// Manages both named pipe connections between Glass and another process.
// Glass is always the client on both pipes.
// Reconnection is handled internally. Callers receive only
// Connected and Disconnected events.
public class PipeManager : IDisposable
{
    private const int RetryDelayMs = 500;

    private NamedPipeClientStream? _commandPipe;
    private NamedPipeClientStream? _notifyPipe;

    // CancellationTokenSource for PipeManager lifetime — cancelled only on StopAsync().
    private CancellationTokenSource? _lifetimeCts;

    // CancellationTokenSource for current connection — cancelled by Reset() to unblock both threads.
    private CancellationTokenSource? _connectionCts;

    private Task? _readerTask;
    private Task? _writerTask;

    private readonly Queue<string> _sendQueue = new();
    private readonly SemaphoreSlim _sendSignal = new(0);
    private readonly string _name;
    private readonly string _commandPipeName;
    private readonly string _notifyPipeName;

    private bool _disposed = false;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<string>? MessageReceived;

    private bool _connected = false;
    public bool IsConnected => _connected;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PipeManager Constructor
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public PipeManager(string name, string commandPipeName, string notifyPipeName)
    {
        _name = name;
        _commandPipeName = commandPipeName;
        _notifyPipeName = notifyPipeName;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Log
    //
    // Writes a prefixed log message using this PipeManager's name.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void Log(string message)
    {
        DebugLog.Write(DebugLog.Log_Pipes, $"[{_name}] {message}");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Start
    //
    // Start both pipe threads.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Start()
    {
        _lifetimeCts = new CancellationTokenSource();
        _connectionCts = new CancellationTokenSource();

        _readerTask = Task.Run(() => ReaderLoopAsync(_lifetimeCts.Token));
        _writerTask = Task.Run(() => WriterLoopAsync(_lifetimeCts.Token));

        Log("started.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // StopAsync
    //
    // Stop both pipe threads and wait for them to exit.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  
    public async Task StopAsync()
    {
        Log("stopping.");

        _lifetimeCts?.Cancel();
        _connectionCts?.Cancel();
        _sendSignal.Release();

        if (_readerTask != null)
        {
            await _readerTask;
        }
        if (_writerTask != null)
        {
            await _writerTask;
        }

        _connected = false;

        Log("stopped.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Send
    //
    // Queue a message to be sent to the other process.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Send(string message)
    {
        if (!_connected)
        {
            Log($"send skipped, not connected: {message}");
            return;
        }

        lock (_sendQueue)
        {
            _sendQueue.Enqueue(message);
        }
        _sendSignal.Release();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Reset
    //
    // Cancels the connection token to unblock both threads, then
    // issues a new connection token for the next connection attempt.  _sendSignal is releasing the semaphore that the writer
    // waits on.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Reset()
    {
        Log($"resetting. caller={new System.Diagnostics.StackTrace().GetFrame(1)?.GetMethod()?.Name ?? "unknown"}");
        lock (_sendQueue)
        {
            _sendQueue.Clear();
        }
        _connectionCts?.Cancel();
        _connectionCts = new CancellationTokenSource();
        _sendSignal.Release();
        _connected = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ConnectBothAsync
    //
    // Connects to both pipe servers using a linked token that
    // respects both lifetime and connection cancellation.
    // Retries until successful or cancelled.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private async Task<bool> ConnectBothAsync(CancellationToken lifetimeToken)
    {
        while (!lifetimeToken.IsCancellationRequested)
        {
            try
            {
                _commandPipe = new NamedPipeClientStream(".", _commandPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                _notifyPipe = new NamedPipeClientStream(".", _notifyPipeName, PipeDirection.In, PipeOptions.Asynchronous);

                await _commandPipe.ConnectAsync(lifetimeToken);
                await _notifyPipe.ConnectAsync(lifetimeToken);

                Log("connected.");
                Connected?.Invoke();
                _connected = true;
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Log($"connect failed: {ex.Message}, retrying...");
                _commandPipe?.Close();
                _commandPipe = null;
                _notifyPipe?.Close();
                _notifyPipe = null;
                await Task.Delay(RetryDelayMs, lifetimeToken).ContinueWith(_ => { });
            }
        }
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReaderLoopAsync
    //
    // Reads notifications from the notify pipe. Drives reconnection for both pipes.
    // On breakage, fires Disconnected, calls Reset(), and reconnects both pipes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private async Task ReaderLoopAsync(CancellationToken lifetimeToken)
    {
        Log("reader started.");

        while (!lifetimeToken.IsCancellationRequested)
        {
            if (!await ConnectBothAsync(lifetimeToken))
            {
                break;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeToken, _connectionCts!.Token);

            try
            {
                var lengthBuffer = new byte[4];
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    int bytesRead = await ReadExactAsync(_notifyPipe!, lengthBuffer, 4, linkedCts.Token);
                    if (bytesRead != 4)
                    {
                        Log($"reader short read on length, got {bytesRead}");
                        break;
                    }

                    int length = BitConverter.ToInt32(lengthBuffer, 0);
                    if ((length <= 0) || (length > 65536))
                    {
                        Log($"reader invalid length {length}");
                        break;
                    }

                    var messageBuffer = new byte[length];
                    bytesRead = await ReadExactAsync(_notifyPipe!, messageBuffer, length, linkedCts.Token);
                    if (bytesRead != length)
                    {
                        Log($"reader short read on body, got {bytesRead}");
                        break;
                    }

                    var message = Encoding.UTF8.GetString(messageBuffer);
                    Log($"received: {message}");
                    MessageReceived?.Invoke(message);
                }
            }
            catch (OperationCanceledException)
            {
                if (lifetimeToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                Log($"reader error: {ex.Message}");
            }

            if (!lifetimeToken.IsCancellationRequested)
            {
                Log("reader disconnected, resetting.");
                Reset();
                Disconnected?.Invoke();

            }
        }

        Log("reader exiting.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriterLoopAsync
    //
    // Writes commands to the command pipe. Waits for messages in the send queue.
    // Uses the connection token to detect resets and skip stale sends.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private async Task WriterLoopAsync(CancellationToken lifetimeToken)
    {
        Log("writer started.");

        while (!lifetimeToken.IsCancellationRequested)
        {
            await _sendSignal.WaitAsync(lifetimeToken).ContinueWith(_ => { });

            if (lifetimeToken.IsCancellationRequested)
            {
                break;
            }

            if ((_commandPipe == null) || (!_commandPipe.IsConnected))
            {
                continue;
            }

            string? message = null;
            lock (_sendQueue)
            {
                if (_sendQueue.Count > 0)
                {
                    message = _sendQueue.Dequeue();
                }
            }

            if (message == null)
            {
                continue;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeToken, _connectionCts!.Token);

            try
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                var length = BitConverter.GetBytes(bytes.Length);
                await _commandPipe.WriteAsync(length, linkedCts.Token);
                await _commandPipe.WriteAsync(bytes, linkedCts.Token);
                await _commandPipe.FlushAsync(linkedCts.Token);
                Log($"sent: {message}");
            }
            catch (OperationCanceledException)
            {
                if (lifetimeToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                Log($"writer error: {ex.Message}");
                if (!lifetimeToken.IsCancellationRequested)
                {
                    Log("writer disconnected, resetting.");
                    Disconnected?.Invoke();
                    Reset();
                }
            }
        }

        Log("writer exiting.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReadExactAsync
    //
    // Read exactly count bytes from stream. Returns total bytes read.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private static async Task<int> ReadExactAsync(PipeStream stream, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }
            totalRead += bytesRead;
        }
        return totalRead;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
        if (!_disposed)
        {
            Reset();
            _lifetimeCts?.Dispose();
            _connectionCts?.Dispose();
            _sendSignal.Dispose();
            _disposed = true;
        }
    }
}