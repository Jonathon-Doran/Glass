using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Glass.Data.Models;
using Glass.Data.Repositories;

namespace Glass.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// ProfileManager
//
// Manages the active profile lifecycle: launching characters through ISXGlass,
// sending layout and region definitions to GlassVideo, and tracking which
// slots have been defined. Used by both Glass and Inference.
///////////////////////////////////////////////////////////////////////////////////////////////
public class ProfileManager
{
    private ProfileRepository? _activeProfile;
    private readonly HashSet<int> _definedSlots = new();

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ActiveProfile
    //
    // The currently active profile repository, or null if no profile is active.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ProfileRepository? ActiveProfile => _activeProfile;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasActiveProfile
    //
    // Returns true if a profile is currently active.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool HasActiveProfile => _activeProfile != null;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ClearActiveProfile
    //
    // Clears the active profile and resets the defined slots. Called when all
    // sessions have disconnected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void ClearActiveProfile()
    {
        DebugLog.Write("ProfileManager.ClearActiveProfile: clearing active profile");
        _activeProfile = null;
        _definedSlots.Clear();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LaunchProfile
    //
    // Launches all characters in the specified profile through ISXGlass.
    // Sends the layout to GlassVideo, then launches characters with small
    // random delays between them to avoid overwhelming the login server.
    //
    // profileName:  The name of the profile to launch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public async Task LaunchProfile(string profileName)
    {
        if (_activeProfile != null)
        {
            DebugLog.Write("ProfileManager.LaunchProfile: a profile is already active, refusing launch.");
            return;
        }

        ProfileRepository repo = new ProfileRepository(profileName);
        IReadOnlyList<SlotAssignment> slots = repo.GetSlots();
        CharacterRepository charRepo = new CharacterRepository();

        _activeProfile = repo;
        _definedSlots.Clear();

        DebugLog.Write("ProfileManager.LaunchProfile: launching profile '" + profileName
            + "' with " + slots.Count + " characters");

        GlassContext.FocusTracker.Start();
        GlassContext.ISXGlassPipe.Send("new_profile");

        int layoutId = repo.GetLayoutId() ?? 0;
        if (layoutId != 0)
        {
            SendGlassVideoLayout(layoutId);
        }
        else
        {
            DebugLog.Write("ProfileManager.LaunchProfile: no layout assigned to profile, skipping GlassVideo layout.");
        }

        Random rng = new Random();
        foreach (SlotAssignment slot in slots)
        {
            Character? character = charRepo.GetById(slot.CharacterId);
            if (character == null)
            {
                DebugLog.Write("ProfileManager.LaunchProfile: no character found for id="
                    + slot.CharacterId + ", skipping");
                continue;
            }

            DebugLog.Write("ProfileManager.LaunchProfile: launching " + character.Name
                + " accountId=" + character.AccountId
                + " server=" + character.Server
                + " id=" + character.Id);

            GlassContext.ISXGlassPipe.Send("launch " + character.AccountId
                + " " + character.Name
                + " " + character.Server
                + " " + character.Id);

            int delay = rng.Next(4000, 7000);
            await Task.Delay(delay);
        }

        DebugLog.Write("ProfileManager.LaunchProfile: all characters launched");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SendGlassVideoLayout
    //
    // Sends slot definitions, slot assignments, video source regions, and video
    // destination regions to GlassVideo for the active profile's layout.
    //
    // layoutId:  The layout to send.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void SendGlassVideoLayout(int layoutId)
    {
        if (_activeProfile == null)
        {
            DebugLog.Write("ProfileManager.SendGlassVideoLayout: no active profile.");
            return;
        }

        WindowLayoutRepository layoutRepo = new WindowLayoutRepository();
        IReadOnlyList<SlotPlacement> placements = layoutRepo.GetSlotPlacements(layoutId);
        IReadOnlyList<SlotAssignment> slots = _activeProfile.GetSlots();
        CharacterRepository charRepo = new CharacterRepository();

        foreach (SlotPlacement placement in placements)
        {
            if (!_definedSlots.Contains(placement.SlotNumber))
            {
                string cmd = "slot_define " + placement.SlotNumber
                    + " " + placement.X
                    + " " + placement.Y
                    + " " + placement.Width
                    + " " + placement.Height;
                DebugLog.Write(DebugLog.Log_Sessions, "ProfileManager.SendGlassVideoLayout: sending " + cmd);
                GlassContext.GlassVideoPipe.Send(cmd);
                _definedSlots.Add(placement.SlotNumber);
            }
        }

        WindowLayout? layout = layoutRepo.GetLayoutById(layoutId);
        if (layout?.UISkinId.HasValue == true)
        {
            VideoSourceRepository sourceRepo = new VideoSourceRepository();
            IReadOnlyList<VideoSource> sources = sourceRepo.GetByUISkin(layout.UISkinId.Value);
            foreach (VideoSource source in sources)
            {
                string cmd = "region_source " + source.Name
                    + " " + source.X
                    + " " + source.Y
                    + " " + source.Width
                    + " " + source.Height;
                DebugLog.Write(DebugLog.Log_Sessions, "ProfileManager.SendGlassVideoLayout: sending " + cmd);
                GlassContext.GlassVideoPipe.Send(cmd);
            }
            DebugLog.Write(DebugLog.Log_Sessions, "ProfileManager.SendGlassVideoLayout: sent " + sources.Count + " video sources.");
        }
        else
        {
            DebugLog.Write(DebugLog.Log_Sessions, "ProfileManager.SendGlassVideoLayout: no UI skin assigned to layout, skipping video sources.");
        }

        VideoDestinationRepository destRepo = new VideoDestinationRepository();
        IReadOnlyList<VideoDestination> destinations = destRepo.GetAll();
        foreach (VideoDestination dest in destinations)
        {
            string cmd = "region_dest " + dest.Name
                + " " + dest.X
                + " " + dest.Y
                + " " + dest.Width
                + " " + dest.Height;
            DebugLog.Write(DebugLog.Log_Sessions, "ProfileManager.SendGlassVideoLayout: sending " + cmd);
            GlassContext.GlassVideoPipe.Send(cmd);
        }
        DebugLog.Write(DebugLog.Log_Sessions, "ProfileManager.SendGlassVideoLayout: sent " + destinations.Count + " video destinations.");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetCharacterNameByAccountId
    //
    // Searches the active profile's slot assignments for a character whose
    // account ID matches the given value. Returns the character name, or
    // empty string if no match is found or no profile is active.
    // Multiple characters may share an account, but only one can be in a profile.
    //
    // accountId:  The account ID to search for.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string GetCharacterNameByAccountId(uint accountId)
    {
        if (_activeProfile == null)
        {
            DebugLog.Write("ProfileManager.GetCharacterNameByAccountId: no active profile");
            return string.Empty;
        }

        CharacterRepository charRepo = new CharacterRepository();
        foreach (SlotAssignment slot in _activeProfile.GetSlots())
        {
            Character? character = charRepo.GetById(slot.CharacterId);
            if (character != null && character.AccountId == accountId)
            {
                DebugLog.Write("ProfileManager.GetCharacterNameByAccountId: accountId=" + accountId
                    + " character=" + character.Name);
                return character.Name;
            }
        }

        DebugLog.Write("ProfileManager.GetCharacterNameByAccountId: no match for accountId=" + accountId);
        return string.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetSlotForCharacter
    //
    // Returns the slot number assigned to the named character in the active profile,
    // or -1 if no assignment exists or no profile is active.
    //
    // characterName:  The character name to look up.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int GetSlotForCharacter(string characterName)
    {
        if (_activeProfile == null)
        {
            DebugLog.Write("ProfileManager.GetSlotForCharacter: no active profile");
            return -1;
        }

        CharacterRepository charRepo = new CharacterRepository();
        SlotAssignment? assignment = _activeProfile.GetSlots()
            .FirstOrDefault(s => charRepo.GetById(s.CharacterId)?.Name == characterName);

        if (assignment == null)
        {
            DebugLog.Write("ProfileManager.GetSlotForCharacter: no slot for character '" + characterName + "'");
            return -1;
        }

        DebugLog.Write("ProfileManager.GetSlotForCharacter: character='" + characterName + "' slot=" + assignment.SlotNumber);
        return assignment.SlotNumber;
    }
}
