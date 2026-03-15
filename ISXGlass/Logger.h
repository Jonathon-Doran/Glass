#pragma once
#include <windows.h>
#include <mutex>

class Logger
{
public:
    static Logger& Instance();
    bool Open(const char* path);
    void Close();

    // Writes unconditionally — for high priority messages that always log.
    void Write(const char* format, ...);

    // Writes only if the specified feature flag is true.
    void WriteIf(bool flag, const char* format, ...);

    // Sets a feature flag by name. Returns false if not recognized.
    bool SetFlag(const char* feature, bool enabled);

    // Feature flags — default states.
    bool Log_Pipes = false;
    bool Log_Video = false;
    bool Log_Sessions = true;
    bool Log_Input = false;

private:
    Logger();
    ~Logger();
    Logger(const Logger&) = delete;
    Logger& operator=(const Logger&) = delete;

    void WriteV(const char* format, va_list args);

    FILE* _file;
    std::mutex  _mutex;
};