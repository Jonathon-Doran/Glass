#include "Logger.h"
#include <stdio.h>

Logger& Logger::Instance()
{
    static Logger instance;
    return instance;
}

Logger::Logger()
    : _file(nullptr)
{
}

Logger::~Logger()
{
    Close();
}

// Opens the log file at the given path for writing. Returns false if it cannot be opened.
bool Logger::Open(const char* path)
{
    std::lock_guard<std::mutex> lock(_mutex);
    _file = _fsopen(path, "w", _SH_DENYWR);
    return _file != nullptr;
}

// Closes the log file.
void Logger::Close()
{
    std::lock_guard<std::mutex> lock(_mutex);
    if (_file)
    {
        fclose(_file);
        _file = nullptr;
    }
}

// Internal: writes a timestamped formatted message to the log file.
void Logger::WriteV(const char* format, va_list args)
{
    std::lock_guard<std::mutex> lock(_mutex);
    if (!_file)
    {
        return;
    }
    SYSTEMTIME st;
    GetLocalTime(&st);
    fprintf(_file, "[%02d:%02d:%02d.%03d] ", st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
    vfprintf(_file, format, args);
    fprintf(_file, "\n");
    fflush(_file);
}

// Writes a timestamped formatted message to the log file.
void Logger::Write(const char* format, ...)
{
    va_list args;
    va_start(args, format);
    WriteV(format, args);
    va_end(args);
}

// Writes a timestamped message only if flag is true.
void Logger::WriteIf(bool flag, const char* format, ...)
{
    if (!flag)
    {
        return;
    }
    va_list args;
    va_start(args, format);
    WriteV(format, args);
    va_end(args);
}

// Sets a feature flag by name. Returns false if not recognized.
bool Logger::SetFlag(const char* feature, bool enabled)
{
    if (_stricmp(feature, "pipes") == 0)
    {
        Log_Pipes = enabled;
        return true;
    }
    if (_stricmp(feature, "video") == 0)
    {
        Log_Video = enabled;
        return true;
    }
    if (_stricmp(feature, "sessions") == 0)
    {
        Log_Sessions = enabled;
        return true;
    }
    if (_stricmp(feature, "input") == 0)
    {
        Log_Input = enabled;
        return true;
    }
    return false;
}