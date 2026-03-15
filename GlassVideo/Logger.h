#pragma once
#include <windows.h>
#include <mutex>

class Logger
{
public:
    static Logger& Instance();

    bool Open(const char* path);
    void Close();
    void Write(const char* format, ...);

private:
    Logger();
    ~Logger();
    Logger(const Logger&) = delete;
    Logger& operator=(const Logger&) = delete;

    FILE* _file;
    std::mutex _mutex;
};