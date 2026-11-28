using Glass.World;

namespace Glass.Core.Signals;

///////////////////////////////////////////////////////////////////////////////////////////////
// SignalPositionChanged
//
// Raised when an entity reports a position whose x, y or z differs from its last reported
// position.  A report that changes only the heading does not raise this signal.
//
// Published on the thread that decoded the position.  Subscribers that need a specific
// thread (UI, for example) marshal in their handler.
//
// Carries the entity's EntityId, and its new position.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SignalPositionChanged
{
    public EntityId EntityId
    {
        get;
    }

    public Position Position
    {
        get;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SignalPositionChanged (constructor)
    //
    // entityId:  The entity whose position changed.
    // position:  The entity's new position.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SignalPositionChanged(EntityId entityId, Position position)
    {
        EntityId = entityId;
        Position = position;
    }
}
