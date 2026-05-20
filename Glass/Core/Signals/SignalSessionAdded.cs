namespace Glass.Core.Signals;

///////////////////////////////////////////////////////////////////////////////////////////////
// SignalSessionAdded
//
// Raised by SessionRegistry the first time a session is identified — that is,
// when its character name transitions from unknown to known.  Not raised on
// every packet for a session, only on the identification event itself.
//
// Published on whatever thread the registry was updated on.  In the live
// capture path this is the protocol stack's capture thread; in pcap replay
// it is the replay thread.  Subscribers that need a specific thread (UI,
// for example) marshal in their handler.
//
// Carries the session id and the resolved character name.  Subscribers
// caching name-by-id can write the cache entry directly from the signal
// without re-consulting SessionRegistry.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SignalSessionAdded
{
    public int SessionId
    {
        get;
    }

    public string CharacterName
    {
        get;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SignalSessionAdded (constructor)
    //
    // sessionId:      The session identifier, monotonically increasing for the
    //                 lifetime of the capture run.
    // characterName:  The resolved character name.  Must not be null or empty;
    //                 the signal is raised only on identification, so an
    //                 empty name here would be a publisher bug.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SignalSessionAdded(int sessionId, string characterName)
    {
        SessionId = sessionId;
        CharacterName = characterName;
    }
}
