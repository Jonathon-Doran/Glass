#include "GlassVideo.h"
#include "PipeManager.h"
#include "Logger.h"

PipeManager::PipeManager(const std::string& name, const std::string& cmdPipe, const std::string& notifyPipe)
    : _name(name)
    , _cmdPipeName("\\\\.\\pipe\\" + cmdPipe)
    , _notifyPipeName("\\\\.\\pipe\\" + notifyPipe)
    , _readerPipe(INVALID_HANDLE_VALUE)
    , _writerPipe(INVALID_HANDLE_VALUE)
    , _readerThread(NULL)
    , _writerThread(NULL)
    , _stopEvent(NULL)
    , _resetting(false)
    , _needsReconnect(false)
    , _running(false)
{
}

PipeManager::~PipeManager()
{
    Stop();
}

// Writes a prefixed log message using this PipeManager's name.
void PipeManager::Log(const char* format, ...)
{
    char buffer[512];
    va_list args;
    va_start(args, format);
    vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);
    Logger::Instance().Write("[%s] %s", _name.c_str(), buffer);
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

    _stopEvent = CreateEvent(nullptr, TRUE, FALSE, nullptr);

    _readerThread = CreateThread(nullptr, 0, ReaderThreadProc, this, 0, nullptr);
    _writerThread = CreateThread(nullptr, 0, WriterThreadProc, this, 0, nullptr);

    Log("started. cmd=%s notify=%s", _cmdPipeName.c_str(), _notifyPipeName.c_str());
}

// Stop both pipes and wait for threads to exit.
void PipeManager::Stop()
{
    if (!_running)
    {
        Log("Stop ignored as PipeManager is not running.");
        return;
    }

    Log("stopping.");
    _running = false;
    SetEvent(_stopEvent);
    _queueCV.notify_all();

    if (_readerPipe != INVALID_HANDLE_VALUE)
    {
        Log("cancelling and closing reader pipe.");
        CancelIoEx(_readerPipe, nullptr);
        CloseHandle(_readerPipe);
        _readerPipe = INVALID_HANDLE_VALUE;
    }
    if (_writerPipe != INVALID_HANDLE_VALUE)
    {
        Log("cancelling and closing writer pipe.");
        CancelIoEx(_writerPipe, nullptr);
        CloseHandle(_writerPipe);
        _writerPipe = INVALID_HANDLE_VALUE;
    }

    if (_readerThread)
    {
        Log("waiting for reader thread.");
        DWORD result = WaitForSingleObject(_readerThread, 1000);
        Log("reader thread wait result=%d.", result);
        CloseHandle(_readerThread);
        _readerThread = NULL;
    }
    if (_writerThread)
    {
        Log("waiting for writer thread.");
        DWORD result = WaitForSingleObject(_writerThread, 1000);
        Log("writer thread wait result=%d.", result);
        CloseHandle(_writerThread);
        _writerThread = NULL;
    }

    if (_stopEvent)
    {
        Log("closing stop event.");
        CloseHandle(_stopEvent);
        _stopEvent = NULL;
    }

    Log("stopped.");
}

// Queue a message to be sent to the other process.
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
            Log("Reset already in progress, ignoring.");
            return;
        }
        _resetting = true;
    }

    Log("resetting both pipes. readerPipe=%p writerPipe=%p", _readerPipe, _writerPipe);

    if (_readerPipe != INVALID_HANDLE_VALUE)
    {
        CancelIoEx(_readerPipe, nullptr);
        DisconnectNamedPipe(_readerPipe);
    }
    if (_writerPipe != INVALID_HANDLE_VALUE)
    {
        CancelIoEx(_writerPipe, nullptr);
        DisconnectNamedPipe(_writerPipe);
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

// Read exactly len bytes from pipe. Returns false on any failure.
bool PipeManager::ReadExact(HANDLE pipe, void* buf, DWORD len)
{
    DWORD totalRead = 0;
    while (totalRead < len)
    {
        DWORD bytesRead = 0;
        if (!ReadFile(pipe, (char*)buf + totalRead, len - totalRead, &bytesRead, nullptr))
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

// Write exactly len bytes to pipe. Returns false on any failure.
bool PipeManager::WriteExact(HANDLE pipe, const void* buf, DWORD len)
{
    DWORD totalWritten = 0;
    while (totalWritten < len)
    {
        DWORD bytesWritten = 0;
        if (!WriteFile(pipe, (const char*)buf + totalWritten, len - totalWritten, &bytesWritten, nullptr))
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

// Creates the command pipe server, blocks on connect, reads messages, loops on breakage.
void PipeManager::ReaderThread()
{
    Log("reader thread started.");

    HANDLE pipe = CreateNamedPipeA(
        _cmdPipeName.c_str(),
        PIPE_ACCESS_INBOUND,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
        1,
        PIPE_BUFFER_SIZE,
        PIPE_BUFFER_SIZE,
        0,
        nullptr);

    if (pipe == INVALID_HANDLE_VALUE)
    {
        Log("reader CreateNamedPipe failed: %d", GetLastError());
        return;
    }

    _readerPipe = pipe;
    Log("reader pipe created. pipe=%p", pipe);

    while (_running)
    {
        Log("reader waiting for connection...");

        BOOL connected = ConnectNamedPipe(_readerPipe, nullptr);
        DWORD err = GetLastError();
        Log("reader ConnectNamedPipe completed with err code: %d", err);

        if ((!connected) && (err != ERROR_PIPE_CONNECTED))
        {
            Log("reader ConnectNamedPipe failed: %d", err);
            if (_running)
            {
                Log("reader still running, retrying.");
                continue;
            }
            Log("reader not running, exiting.");
            break;
        }

        Log("reader connected. pipe=%p", _readerPipe);

        while (_running)
        {
            int length = 0;
            if (!ReadExact(_readerPipe, &length, sizeof(int)))
            {
                if (_running)
                {
                    Log("reader read length failed: %d", GetLastError());
                }
                break;
            }

            if ((length <= 0) || (length > PIPE_BUFFER_SIZE))
            {
                Log("invalid length %d, disconnecting.", length);
                break;
            }

            std::string message(length, '\0');
            if (!ReadExact(_readerPipe, &message[0], (DWORD)length))
            {
                if (_running)
                {
                    Log("reader read body failed: %d", GetLastError());
                }
                break;
            }

            Logger::Instance().Write("PipeManager: received: %s", message.c_str());
            {
                std::lock_guard<std::mutex> lock(g_commandMutex);
                g_commandQueue.push(message);
            }
        }

        Log("reader disconnected, resetting.");
        DisconnectNamedPipe(_readerPipe);
        Reset();
    }

    CloseHandle(_readerPipe);
    _readerPipe = INVALID_HANDLE_VALUE;
    Log("reader thread exiting.");
}

// Creates the notify pipe server, blocks on connect, drains send queue, loops on breakage.
void PipeManager::WriterThread()
{
    Log("writer thread started.");

    HANDLE pipe = CreateNamedPipeA(
        _notifyPipeName.c_str(),
        PIPE_ACCESS_OUTBOUND,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
        1,
        PIPE_BUFFER_SIZE,
        PIPE_BUFFER_SIZE,
        0,
        nullptr);

    if (pipe == INVALID_HANDLE_VALUE)
    {
        Log("writer CreateNamedPipe failed: %d", GetLastError());
        return;
    }

    _writerPipe = pipe;
    Log("writer pipe created. pipe=%p", pipe);

    while (_running)
    {
        Log("writer waiting for connection...");

        BOOL connected = ConnectNamedPipe(_writerPipe, nullptr);
        DWORD err = GetLastError();
        if ((!connected) && (err != ERROR_PIPE_CONNECTED))
        {
            Log("writer ConnectNamedPipe failed: %d", err);
            if (_running)
            {
                Log("writer still running, retrying.");
                continue;
            }
            Log("writer not running, exiting.");
            break;
        }

        Log("writer connected. pipe=%p", _writerPipe);
        _needsReconnect = false;

        while (_running)
        {
            std::string message;
            {
                Log("writer entering CV wait. running=%d resetting=%d queueSize=%d", (int)_running, (int)_resetting.load(), (int)_sendQueue.size());
                std::unique_lock<std::mutex> lock(_queueMutex);
                _queueCV.wait(lock, [this]
                    {
                        return ((!_sendQueue.empty()) || (!_running) || _resetting || _needsReconnect);
                    });

                if ((!_running) || _resetting || _needsReconnect)
                {
                    Log("writer breaking inner loop. running=%d resetting=%d needsReconnect=%d", (int)_running, (int)_resetting.load(), (int)_needsReconnect.load());
                    break;
                }

                if (_sendQueue.empty())
                {
                    continue;
                }

                message = _sendQueue.front();
                _sendQueue.pop();
            }

            Log("writer dequeued: %s", message.c_str());

            int length = (int)message.size();

            if (!WriteExact(_writerPipe, &length, sizeof(int)))
            {
                if (_running)
                {
                    Log("writer write length failed: %d", GetLastError());
                }
                break;
            }

            if (!WriteExact(_writerPipe, message.c_str(), (DWORD)length))
            {
                if (_running)
                {
                    Log("writer write body failed: %d", GetLastError());
                }
                break;
            }

            Log("sent: %s", message.c_str());
        }

        Log("writer disconnected, resetting.");
        DisconnectNamedPipe(_writerPipe);
        Reset();
    }

    CloseHandle(_writerPipe);
    _writerPipe = INVALID_HANDLE_VALUE;
    Log("writer thread exiting.");
}