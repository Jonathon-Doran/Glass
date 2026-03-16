#include "SlotManager.h"
#include "Logger.h"
#include <d3d11.h>

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SlotManager::Define
//
// Defines a slot's position in the GlassVideo window.
// Creates the slot if it does not exist, updates position if it does.
// 
// slotID            The slot to define
// x, y              The location of the slot to render
// width, height     The size of the render window (slot)
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void SlotManager::Define(SlotID slotId, int x, int y, int width, int height)
{
    Logger::Instance().Write("SlotManager::Define: slotId=%u x=%d y=%d w=%d h=%d", slotId, x, y, width, height);

    std::lock_guard<std::mutex> lock(_mutex);

    auto it = _slots.find(slotId);
    if (it != _slots.end())
    {
        it->second -> x = x;
        it->second -> y = y;
        it->second -> width = width;
        it->second -> height = height;
        Logger::Instance().Write("SlotManager::Define: updated existing slot.");
        return;
    }

    std::unique_ptr<SlotInfo> slot = std::make_unique<SlotInfo>();
    slot->slotId = slotId;
    slot->x = x;
    slot->y = y;
    slot->width = width;
    slot->height = height;
    _slots.emplace(slotId, std::move(slot));
    Logger::Instance().Write("SlotManager::Define: slot defined. total slots=%zu", _slots.size());
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SlotManager::Assign
//
// Assigns a capture session to an existing slot.
// Initializes WGC capture for the given HWND.
// 
// device:       The device to capture
// slotId:       The slot to assign
// sessionName:  The session running on this slot
// hwnd:         The window handle to capture
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void SlotManager::Assign(ID3D11Device* device, SlotID slotId, const std::string& sessionName, HWND hwnd)
{
    Logger::Instance().Write("SlotManager::Assign: slotId=%u session=%s hwnd=%p", slotId, sessionName.c_str(), hwnd);

    std::lock_guard<std::mutex> lock(_mutex);

    auto range = _slots.equal_range(slotId);
    if (range.first == range.second)
    {
        Logger::Instance().Write("SlotManager::Assign: slotId=%u not defined.", slotId);
        return;
    }

    // For now assign to the first entry. Multiple sessions per slot is a future concern.
    SlotInfo* slot = range.first->second.get();
    slot -> sessionName = sessionName;
    slot -> hwnd = hwnd;

    if (!slot -> capture.Initialize(device, hwnd))
    {
        Logger::Instance().Write("SlotManager::Assign: capture initialization failed for session=%s", sessionName.c_str());
        slot -> sessionName.clear();
        slot -> hwnd = NULL;
        return;
    }

    Logger::Instance().Write("SlotManager::Assign: session assigned.");
}



//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SlotManager::Remove
//
// Removes all sessions assigned to a slot and removes the slot definition.
// 
// slotId:  The slot to remove
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void SlotManager::Remove(SlotID slotId)
{
    Logger::Instance().Write("SlotManager::Remove: slotId=%u", slotId);

    std::lock_guard<std::mutex> lock(_mutex);

    auto range = _slots.equal_range(slotId);
    if (range.first == range.second)
    {
        Logger::Instance().Write("SlotManager::Remove: slotId=%u not found.", slotId);
        return;
    }

    for (auto it = range.first; it != range.second; ++it)
    {
        it->second -> capture.Shutdown();
    }

    _slots.erase(range.first, range.second);
    Logger::Instance().Write("SlotManager::Remove: slot removed. total entries=%zu", _slots.size());
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SlotManager::Unassign
//
// Removes a specific session assignment from a slot by session name,
// leaving the slot definition intact for future assignment.
// 
// sessionName:  The session to remove
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void SlotManager::Unassign(const std::string& sessionName)
{
    Logger::Instance().Write("SlotManager::Unassign: session=%s", sessionName.c_str());

    std::lock_guard<std::mutex> lock(_mutex);

    for (auto it = _slots.begin(); it != _slots.end(); ++it)
    {
        if (it->second -> sessionName == sessionName)
        {
            it->second -> capture.Shutdown();
            it->second -> sessionName.clear();
            it->second -> hwnd = NULL;
            Logger::Instance().Write("SlotManager::Unassign: session unassigned from slotId=%u.", it->second -> slotId);
            return;
        }
    }

    Logger::Instance().Write("SlotManager::Unassign: session=%s not found.", sessionName.c_str());
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SlotManager::Clear
//
// Removes all slots and shuts down all captures.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void SlotManager::Clear()
{
    Logger::Instance().Write("SlotManager::Clear: clearing all slots.");

    std::lock_guard<std::mutex> lock(_mutex);

    for (auto& pair : _slots)
    {
        Logger::Instance().Write("SlotManager::Clear: shutting down slotId=%u session=%s hwnd=%p",
            pair.second->slotId,
            pair.second->sessionName.c_str(),
            pair.second->hwnd);

        pair.second -> capture.Shutdown();
    }
    _slots.clear();
    Logger::Instance().Write("SlotManager::Clear: all slots cleared.");
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SlotManager::CaptureAll
//
// Calls Capture() on all slots. Should be called from the render loop.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void SlotManager::CaptureAll()
{
    std::lock_guard<std::mutex> lock(_mutex);

    for (auto& pair : _slots)
    {
        if (pair.second -> hwnd != NULL)
        {
            pair.second -> capture.Capture();
        }
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SlotManager::GetSlots
//
// Returns a read-only reference to the slot map for use by the renderer.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
const std::multimap<SlotID, std::unique_ptr<SlotInfo>>& SlotManager::GetSlots() const
{
    return _slots;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SlotManager::GetMutex
//
// Returns the slot map mutex for external callers that need to hold it
// while iterating slots, such as the render loop.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
std::mutex& SlotManager::GetMutex()
{
    return _mutex;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// SlotManager::HitTest
//
// Returns the HWND of the slot containing the given screen point,
// or NULL if no slot contains it.
//
// x, y:  The point to test in window coordinates
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
HWND SlotManager::HitTest(int x, int y)
{
    std::lock_guard<std::mutex> lock(_mutex);

    for (auto& pair : _slots)
    {
        SlotInfo* slot = pair.second.get();
        if ((x >= slot->x) && (x < slot->x + slot->width) &&
            (y >= slot->y) && (y < slot->y + slot->height))
        {
            return slot->hwnd;
        }
    }

    return NULL;
}