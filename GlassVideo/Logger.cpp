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
    if (!_file)
    {
        return false;
    }
    return true;
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

// Writes a timestamped formatted message to the log file.
void Logger::Write(const char* format, ...)
{
    std::lock_guard<std::mutex> lock(_mutex);
    if (!_file)
    {
        return;
    }
    SYSTEMTIME st;
    GetLocalTime(&st);
    fprintf(_file, "[%02d:%02d:%02d.%03d] ", st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
    va_list args;
    va_start(args, format);
    vfprintf(_file, format, args);
    va_end(args);
    fprintf(_file, "\n");
    fflush(_file);
}