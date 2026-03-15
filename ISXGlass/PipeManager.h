#pragma once
#include <windows.h>
#include <string>
#include <queue>
#include <mutex>
#include <condition_variable>
#include <functional>
#include <atomic>

// PipeManager owns both named pipes between ISXGlass and Glass.exe.
//
// ISXGlass_Commands: ISXGlass is server, Glass.exe is client. ISXGlass reads.
// ISXGlass_Notify:   Glass.exe is server, ISXGlass is client. ISXGlass writes.
//
// Reader thread: creates pipe server, blocks on connect, reads messages, loops on breakage.
// Writer thread: connects to Glass.exe pipe server, drains send queue, loops on breakage.
// Breakage on either pipe resets both. Send queue is discarded on reset.
//
// Protocol: [LENGTH int32][BODY bytes]

#define PIPE_CMD            "\\\\.\\pipe\\ISXGlass_Commands"
#define PIPE_NOTIFY         "\\\\.\\pipe\\ISXGlass_Notify"
#define PIPE_BUFFER_SIZE    4096

class PipeManager
{
public:
    using CommandCallback = std::function<void(const std::string&)>;

    PipeManager();
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

    bool ReadExact(HANDLE hPipe, void* buf, DWORD len);
    bool WriteExact(HANDLE hPipe, const void* buf, DWORD len);

    HANDLE      _hReaderPipe;
    HANDLE      _hWriterPipe;
    HANDLE      _hReaderThread;
    HANDLE      _hWriterThread;
    HANDLE      _hStopEvent;

    std::queue<std::string>     _sendQueue;
    std::mutex                  _queueMutex;
    std::condition_variable     _queueCV;

    std::mutex                  _resetMutex;
    std::atomic<bool>           _resetting;
    std::atomic<bool>           _needsReconnect;
    bool                        _running;

    CommandCallback             _callback;
};

extern PipeManager g_PipeManager;