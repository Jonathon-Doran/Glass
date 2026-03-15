#pragma once
#include "resource.h"
#include <queue>
#include <mutex>
#include <string>
#include "SlotManager.h"

extern std::queue<std::string>  g_commandQueue;
extern std::mutex               g_commandMutex;
extern SlotManager              g_slotManager;