using Glass.Core.Logging;
using Glass.Network.Protocol;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// AcceptedOpcodes
//
// Session-lived record of wire opcode values the operator has identified during this
// identification session.  Kept separate from the patch level's opcode table because the
// target level is duplicated from a prior patch and therefore already carries inherited
// names on many wire values; those inherited names do not indicate an identification made
// this session.  Only values recorded here are treated as identified for the purpose of
// excluding them from further inference.
//
// A single session-wide instance is reached through Instance.  Held in memory and reset by
// Clear when a new capture or identification run begins.  Not persisted.
///////////////////////////////////////////////////////////////////////////////////////////////
public class AcceptedOpcodes
{
    private readonly Dictionary<OpcodeValue, string> _accepted;
    private static AcceptedOpcodes? _instance;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // The single session-wide registry of accepted opcodes, created on first access.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static AcceptedOpcodes Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new AcceptedOpcodes();
            }
            return _instance;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AcceptedOpcodes (constructor)
    //
    // Creates an empty registry.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private AcceptedOpcodes()
    {
        _accepted = new Dictionary<OpcodeValue, string>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Accept
    //
    // Records a wire value as identified under the given opcode name, overwriting any prior
    // name recorded for that value.
    //
    // wireValue:   The wire opcode value being identified.
    // opcodeName:  The name identified for that value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Accept(OpcodeValue wireValue, string opcodeName)
    {
        _accepted[wireValue] = opcodeName;

        DebugLog.Write(LogChannel.InferenceDebug,
            "AcceptedOpcodes.Accept: wireValue=" + wireValue + " name=" + opcodeName, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IsAccepted
    //
    // Returns true when the given wire value has been recorded as identified this session.
    //
    // wireValue:  The wire opcode value to test.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool IsAccepted(OpcodeValue wireValue)
    {
        return _accepted.ContainsKey(wireValue);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetName
    //
    // Returns the name recorded for the given wire value, or the empty string when the value
    // has not been accepted this session.
    //
    // wireValue:  The wire opcode value to look up.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string GetName(OpcodeValue wireValue)
    {
        string? name;
        if (_accepted.TryGetValue(wireValue, out name))
        {
            return name;
        }
        return string.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WireValueFor
    //
    // Returns the wire value recorded this session under the given opcode name, or
    // OpcodeValue.None when no accepted entry carries that name.  When more than one wire
    // value was accepted under the same name, the first found is returned.
    //
    // opcodeName:  The opcode name to look up.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeValue WireValueFor(string opcodeName)
    {
        foreach (KeyValuePair<OpcodeValue, string> entry in _accepted)
        {
            if (entry.Value == opcodeName)
            {
                return entry.Key;
            }
        }

        DebugLog.Write(LogChannel.Inference,
            "AcceptedOpcodes.WireValueFor: no accepted entry named " + opcodeName, LogLevel.Trace);
        return OpcodeValue.None;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Removes every recorded identification.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        _accepted.Clear();

        DebugLog.Write(LogChannel.InferenceDebug, "AcceptedOpcodes.Clear: cleared", LogLevel.Trace);
    }
}