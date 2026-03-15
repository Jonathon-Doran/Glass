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
// SlotManager::RenderAll
//
// Renders all slots into their destination rectangles using viewport scissoring.
// Caller is responsible for clearing and presenting the render target.
//
// context:       D3D11 device context for the window
// rtv:           The backbuffer we are drawing into
// quadRenderer:  A renderer for the destination area (renders to the current viewport)
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
void SlotManager::RenderAll(ID3D11DeviceContext* context, ID3D11RenderTargetView* rtv, QuadRenderer& quadRenderer)
{
    std::lock_guard<std::mutex> lock(_mutex);

    context->OMSetRenderTargets(1, &rtv, nullptr);

    for (std::pair<const SlotID, std::unique_ptr<SlotInfo>>& pair : _slots)
    {
        SlotInfo* slot = pair.second.get();
        ID3D11ShaderResourceView* srv = slot->capture.GetShaderResourceView();

        if (!srv)
        {
            Logger::Instance().Write("SlotManager::RenderAll: slotId=%u has no SRV, skipping.", slot->slotId);
            continue;
        }

        D3D11_VIEWPORT vp = {};
        vp.TopLeftX = (float)slot->x;
        vp.TopLeftY = (float)slot->y;
        vp.Width = (float)slot->width;
        vp.Height = (float)slot->height;
        vp.MinDepth = 0.0f;
        vp.MaxDepth = 1.0f;
        context->RSSetViewports(1, &vp);

        Logger::Instance().Write("SlotManager::RenderAll: rendering slotId=%u session=%s vp=%.0f,%.0f %.0fx%.0f",
            slot->slotId, slot->sessionName.c_str(),
            vp.TopLeftX, vp.TopLeftY, vp.Width, vp.Height);

        float topCropUV = 100.0f / (float)slot->capture.GetHeight();
        quadRenderer.Render(context, srv, topCropUV);
    }
}