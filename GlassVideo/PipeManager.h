#pragma once
#include <windows.h>
#include <string>
#include <queue>
#include <mutex>
#include <condition_variable>
#include <functional>
#include <atomic>

// PipeManager owns both named pipes between two Glass processes.
//
// Reader thread: creates pipe server, blocks on connect, reads messages, loops on breakage.
// Writer thread: creates pipe server, blocks on connect, drains send queue, loops on breakage.
// Breakage on either pipe resets both. Send queue is discarded on reset.
//
// Protocol: [LENGTH int32][BODY bytes]

#define PIPE_BUFFER_SIZE 4096

class PipeManager
{
public:
    using CommandCallback = std::function<void(const std::string&)>;

    PipeManager(const std::string& name, const std::string& cmdPipe, const std::string& notifyPipe);
    ~PipeManager();

    void Start(CommandCallback callback);
    void Stop();
    void Send(const std::string& message);
    void Reset();

private:
    static DWORD WINAPI ReaderThreadProc(LPVOID lpParam);
    static DWORD WINAPI WriterThreadProc(LPVOID lpParam);

    void ReaderThread();
    void WriterThread();

    bool ReadExact(HANDLE pipe, void* buf, DWORD len);
    bool WriteExact(HANDLE pipe, const void* buf, DWORD len);
    void Log(const char* format, ...);

    const std::string   _cmdPipeName;
    const std::string   _notifyPipeName;

    HANDLE  _readerPipe;
    HANDLE  _writerPipe;
    HANDLE  _readerThread;
    HANDLE  _writerThread;
    HANDLE  _stopEvent;

    std::queue<std::string>     _sendQueue;
    std::mutex                  _queueMutex;
    std::condition_variable     _queueCV;

    std::mutex                  _resetMutex;
    std::atomic<bool>           _resetting;
    std::atomic<bool>           _needsReconnect;
    bool                        _running;

    CommandCallback             _callback;
    std::string                 _name;          // of pipe connection
};
