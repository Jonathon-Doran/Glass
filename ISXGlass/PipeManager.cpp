#include "PipeManager.h"
#include "SessionManager.h"
#include "Logger.h"

PipeManager g_PipeManager;

PipeManager::PipeManager()
    : _hReaderPipe(INVALID_HANDLE_VALUE)
    , _hWriterPipe(INVALID_HANDLE_VALUE)
    , _hReaderThread(NULL)
    , _hWriterThread(NULL)
    , _hStopEvent(NULL)
    , _resetting(false)
    , _needsReconnect(false)
    , _running(false)
{
}

PipeManager::~PipeManager()
{
    Stop();
}

// Entry point for reader thread. Casts lpParam to PipeManager* and calls ReaderThread().
DWORD WINAPI PipeManager::ReaderThreadProc(LPVOID lpParam)
{
    ((PipeManager*)lpParam)->ReaderThread();
    return 0;
}

// Entry point for writer thread. Casts lpParam to PipeManager* and calls WriterThread().
DWORD WINAPI PipeManager::WriterThreadProc(LPVOID lpParam)
{
    ((PipeManager*)lpParam)->WriterThread();
    return 0;
}

// Start pipe manager with a callback invoked for each received command.
void PipeManager::Start(CommandCallback callback)
{
    _callback = callback;
    _running = true;
    _resetting = false;

    _hStopEvent = CreateEvent(nullptr, TRUE, FALSE, nullptr);

    _hReaderThread = CreateThread(nullptr, 0, ReaderThreadProc, this, 0, nullptr);
    _hWriterThread = CreateThread(nullptr, 0, WriterThreadProc, this, 0, nullptr);

    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: started.");
}

// Stop both pipes and wait for threads to exit.
void PipeManager::Stop()
{
    if (!_running)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: Stop ignored as PipeManager is not running.");
        return;
    }
    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: stopping.");
    _running = false;
    SetEvent(_hStopEvent);
    _queueCV.notify_all();

    if (_hReaderPipe != INVALID_HANDLE_VALUE)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: cancelling and closing reader pipe.");
        CancelIoEx(_hReaderPipe, nullptr);
        CloseHandle(_hReaderPipe);
        _hReaderPipe = INVALID_HANDLE_VALUE;
    }
    if (_hWriterPipe != INVALID_HANDLE_VALUE)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: cancelling and closing writer pipe.");
        CancelIoEx(_hWriterPipe, nullptr);
        CloseHandle(_hWriterPipe);
        _hWriterPipe = INVALID_HANDLE_VALUE;
    }

    if (_hReaderThread)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: waiting for reader thread.");
        DWORD result = WaitForSingleObject(_hReaderThread, 1000);
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader thread wait result=%d.", result);
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: closing reader thread handle.");
        CloseHandle(_hReaderThread);
        _hReaderThread = NULL;
    }
    if (_hWriterThread)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: waiting for writer thread.");
        DWORD result = WaitForSingleObject(_hWriterThread, 1000);
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer thread wait result=%d.", result);
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: closing writer thread handle.");
        CloseHandle(_hWriterThread);
        _hWriterThread = NULL;
    }

    if (_hStopEvent)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: closing stop event.");
        CloseHandle(_hStopEvent);
        _hStopEvent = NULL;
    }

    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: stopped.");
}
// Queue a message to be sent to Glass.exe.
void PipeManager::Send(const std::string& message)
{
    {
        std::lock_guard<std::mutex> lock(_queueMutex);
        _sendQueue.push(message);
    }
    _queueCV.notify_one();
}

// Close both pipe handles and discard the send queue.
// Safe to call from either thread or externally before unload.
void PipeManager::Reset()
{
    {
        std::lock_guard<std::mutex> lock(_resetMutex);
        if (_resetting || _needsReconnect)
        {
            Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: Reset already in progress, ignoring.");
            return;
        }
        _resetting = true;
    }

    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: resetting both pipes. _hReaderPipe=%p _hWriterPipe=%p", _hReaderPipe, _hWriterPipe);

    if (_hReaderPipe != INVALID_HANDLE_VALUE)
    {
        CancelIoEx(_hReaderPipe, nullptr);
        DisconnectNamedPipe(_hReaderPipe);
    }
    if (_hWriterPipe != INVALID_HANDLE_VALUE)
    {
        CancelIoEx(_hWriterPipe, nullptr);
        DisconnectNamedPipe(_hWriterPipe);
    }

    {
        std::lock_guard<std::mutex> lock(_queueMutex);
        while (!_sendQueue.empty())
        {
            _sendQueue.pop();
        }
    }

    _needsReconnect = true;

    {
        std::lock_guard<std::mutex> lock(_resetMutex);
        _resetting = false;
    }

    _queueCV.notify_all();
}
// Read exactly len bytes from hPipe. Returns false on any failure.
bool PipeManager::ReadExact(HANDLE hPipe, void* buf, DWORD len)
{
    DWORD totalRead = 0;
    while (totalRead < len)
    {
        DWORD bytesRead = 0;
        if (!ReadFile(hPipe, (char*)buf + totalRead, len - totalRead, &bytesRead, nullptr))
        {
            return false;
        }
        if (bytesRead == 0)
        {
            return false;
        }
        totalRead += bytesRead;
    }
    return true;
}

// Write exactly len bytes to hPipe. Returns false on any failure.
bool PipeManager::WriteExact(HANDLE hPipe, const void* buf, DWORD len)
{
    DWORD totalWritten = 0;
    while (totalWritten < len)
    {
        DWORD bytesWritten = 0;
        if (!WriteFile(hPipe, (const char*)buf + totalWritten, len - totalWritten, &bytesWritten, nullptr))
        {
            return false;
        }
        if (bytesWritten == 0)
        {
            return false;
        }
        totalWritten += bytesWritten;
    }
    return true;
}

// Owns the ISXGlass_Commands server pipe. Created once, reused via
// DisconnectNamedPipe/ConnectNamedPipe across connections. Blocks on
// connect and read. On breakage, calls Reset() and waits for reconnect.
void PipeManager::ReaderThread()
{
    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader thread started.");

    HANDLE hPipe = CreateNamedPipeA(
        PIPE_CMD,
        PIPE_ACCESS_INBOUND,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
        1,
        PIPE_BUFFER_SIZE,
        PIPE_BUFFER_SIZE,
        0,
        nullptr);

    if (hPipe == INVALID_HANDLE_VALUE)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader CreateNamedPipe failed: %d", GetLastError());
        return;
    }

    _hReaderPipe = hPipe;
    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader pipe created.");

    while (_running)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader waiting for connection...");

        BOOL connected = ConnectNamedPipe(_hReaderPipe, nullptr);
        DWORD err = GetLastError();
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader ConnectNamedPipe completed with err code: %d", err);

        if ((!connected) && (err != ERROR_PIPE_CONNECTED))
        {
            Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader ConnectNamedPipe failed: %d", err);
            if (_running)
            {
                Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader still running, retrying.");
                continue;
            }
            Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader not running, exiting.");
            break;
        }

        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader connected. hPipe=%p", _hReaderPipe);

        while (_running)
        {
            int length = 0;
            if (!ReadExact(_hReaderPipe, &length, sizeof(int)))
            {
                if (_running)
                {
                    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader read length failed: %d", GetLastError());
                }
                break;
            }

            if ((length <= 0) || (length > PIPE_BUFFER_SIZE))
            {
                Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: invalid length %d, disconnecting.", length);
                break;
            }

            std::string message(length, '\0');
            if (!ReadExact(_hReaderPipe, &message[0], (DWORD)length))
            {
                if (_running)
                {
                    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader read body failed: %d", GetLastError());
                }
                break;
            }

            Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: received: %s", message.c_str());

            if (_callback)
            {
                _callback(message);
            }
        }

        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader disconnected, resetting.");
        DisconnectNamedPipe(_hReaderPipe);
        Reset();
    }

    CloseHandle(_hReaderPipe);
    _hReaderPipe = INVALID_HANDLE_VALUE;
    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: reader thread exiting, closing reader pipe.");
}

// Owns the ISXGlass_Notify server pipe. Created once, reused via
// DisconnectNamedPipe/ConnectNamedPipe across connections. Drains
// the send queue. On breakage, calls Reset() and waits for reconnect.
void PipeManager::WriterThread()
{
    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer thread started.");

    HANDLE hPipe = CreateNamedPipeA(
        PIPE_NOTIFY,
        PIPE_ACCESS_OUTBOUND,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
        1,
        PIPE_BUFFER_SIZE,
        PIPE_BUFFER_SIZE,
        0,
        nullptr);

    if (hPipe == INVALID_HANDLE_VALUE)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer CreateNamedPipe failed: %d", GetLastError());
        return;
    }

    _hWriterPipe = hPipe;
    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer pipe created.");

    while (_running)
    {
        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer waiting for connection...");

        BOOL connected = ConnectNamedPipe(_hWriterPipe, nullptr);
        DWORD err = GetLastError();
        if ((!connected) && (err != ERROR_PIPE_CONNECTED))
        {
            Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer ConnectNamedPipe failed: %d", err);
            if (_running)
            {
                Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer still running, retrying.");
                continue;
            }
            Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer not running, exiting.");
            break;
        }

        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer connected. hPipe=%p", _hWriterPipe);
        _needsReconnect = false;
        g_SessionManager.EnumerateSessions(true);

        while (_running)
        {
            std::string message;
            {
                Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer entering CV wait. _running=%d _resetting=%d queueSize=%d", (int)_running, (int)_resetting.load(), (int)_sendQueue.size());
                {
                    std::unique_lock<std::mutex> lock(_queueMutex);
                    _queueCV.wait(lock, [this]
                        {
                            return ((!_sendQueue.empty()) || (!_running) || _resetting || _needsReconnect);
                        });
                    if ((!_running) || _resetting || _needsReconnect)
                    {
                        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer breaking inner loop. _running=%d _resetting=%d _needsReconnect=%d", (int)_running, (int)_resetting.load(), (int)_needsReconnect.load());
                        break;
                    }
                    if (_sendQueue.empty())
                    {
                        continue;
                    }
                    message = _sendQueue.front();
                    _sendQueue.pop();
                }
                Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer woke from CV wait. _running=%d _resetting=%d queueSize=%d", (int)_running, (int)_resetting.load(), (int)_sendQueue.size());
                if ((!_running) || _resetting)
                {
                    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer breaking inner loop. _running=%d _resetting=%d", (int)_running, (int)_resetting.load());
                    break;
                }
                Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer dequeued: %s", message.c_str());
            }

            int length = (int)message.size();
                Logger::Instance().WriteIf(Logger::Instance().Log_Pipes,"PipeManager: writer dequeueing. queueSize=%d", (int)_sendQueue.size());

            if (!WriteExact(_hWriterPipe, &length, sizeof(int)))
            {
                if (_running)
                {
                    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer write length failed: %d", GetLastError());
                }
                break;
            }
            if (!WriteExact(_hWriterPipe, message.c_str(), (DWORD)length))
            {
                if ((!_running) || _resetting)
                {
                    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer breaking inner loop. _running=%d _resetting=%d", (int)_running, (int)_resetting.load());
                    break;
                }
            }

            Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: sent: %s", message.c_str());
        }

        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer exited inner loop. _running=%d _resetting=%d", (int)_running, (int)_resetting.load());

        Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer disconnected, resetting.");
        DisconnectNamedPipe(_hWriterPipe);
        Reset();
    }

    CloseHandle(hPipe);
    _hWriterPipe = INVALID_HANDLE_VALUE;
    Logger::Instance().WriteIf(Logger::Instance().Log_Pipes, "PipeManager: writer thread exiting, closing writer pipe.");
}