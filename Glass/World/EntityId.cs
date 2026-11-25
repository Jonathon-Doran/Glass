using Glass.Core.Logging;

namespace Glass.World;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// EntityId
//
// Identifier of one entity, unique across zones and across runs of Glass.  The 64 bits are packed as:
//
//   bits 56 to 63   EntityType, which selects the repository that holds the entity
//   bits 24 to 55   run number, distinct for each run of Glass
//   bits 0 to 23    counter, distinct for each entity issued within a run
//
// The identifier carries no zone or spawn id, so an entity keeps its identifier when it moves between zones.  The
// all-zero value has type None and is the "no identifier" value; None exposes it and Exists tests against it.  An
// identifier that was never assigned is None.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct EntityId
{
    public const uint MaxCounter = 0x00FFFFFFu;

    private const int TypeShift = 56;
    private const int RunShift = 24;
    private const ulong RunMask = 0xFFFFFFFFul;

    private readonly ulong _value;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // EntityId (constructor)
    //
    // Creates an identifier from its three parts.  A type of None or a counter above MaxCounter cannot form a valid
    // identifier; either is a broken caller, logged with a stack trace, and the process is terminated.
    //
    // type:     The kind of entity identified.  Must not be None.
    // run:      The run number of the current run of Glass.
    // counter:  The entity's counter within the run.  Must not exceed MaxCounter.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public EntityId(EntityType type, uint run, uint counter)
    {
        if (type == EntityType.None)
        {
            string message = "EntityId: cannot create an identifier of type None (run=0x" + run.ToString("X8")
                + " counter=" + counter + ") -- broken caller, aborting";
            DebugLog.WriteMultiline(LogChannel.General, message + Environment.NewLine + Environment.StackTrace,
                LogLevel.Error);
            Environment.FailFast(message);
        }

        if (counter > MaxCounter)
        {
            string message = "EntityId: counter " + counter + " exceeds the maximum " + MaxCounter + " for type "
                + type + " run=0x" + run.ToString("X8") + " -- broken caller, aborting";
            DebugLog.WriteMultiline(LogChannel.General, message + Environment.NewLine + Environment.StackTrace,
                LogLevel.Error);
            Environment.FailFast(message);
        }

        _value = ((ulong)type << TypeShift) | ((ulong)run << RunShift) | counter;
    }

    public static EntityId None => default;

    public bool Exists => Type != EntityType.None;

    public EntityType Type => (EntityType)(_value >> TypeShift);

    public uint Run => (uint)((_value >> RunShift) & RunMask);

    public uint Counter => (uint)(_value & MaxCounter);

    public ulong Value => _value;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Formats the identifier as its type, run number in hex, and counter, separated by slashes, or "None" for the
    // no-identifier value, for use in log messages.
    //
    // Returns the formatted string.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        if (!Exists)
        {
            return "None";
        }
        return Type + "/" + Run.ToString("X8") + "/" + Counter;
    }
}