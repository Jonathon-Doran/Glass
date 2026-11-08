namespace Glass.Core.Signals;

///////////////////////////////////////////////////////////////////////////////////////////////
// SignalSessionRemoved
//
// Raised by SessionRegistry when an identified session closes, so the character it belonged
// to is no longer present in the world.  The character may return later on a new session,
// or not at all.
//
// Published on whatever thread processed the disconnect.  In the live capture path this is
// the protocol stack's capture thread; in pcap replay it is the replay thread.  Subscribers
// that need a specific thread marshal in their handler.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SignalSessionRemoved
{
    public SessionId SessionId
    {
        get;
    }

    public CharacterId CharacterId
    {
        get;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SignalSessionRemoved (constructor)
    //
    // sessionId:    The Glass identifier of the session that closed.
    // characterId:  The server's persistent id of the character the session belonged to.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SignalSessionRemoved(SessionId sessionId, CharacterId characterId)
    {
        SessionId = sessionId;
        CharacterId = characterId;
    }
}
