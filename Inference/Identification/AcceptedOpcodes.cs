using Glass.Core.Logging;
using Glass.Network.Protocol;
using System.Collections.Generic;
using System.Text;

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
    private const LogLevel defaultLoglevel = LogLevel.Trace;

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
            "AcceptedOpcodes.Accept: wireValue=" + wireValue + " name=" + opcodeName, defaultLoglevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Remove
    //
    // Removes the identification recorded for a wire value, so the value is no longer
    // treated as identified this session.
    //
    // wireValue:  The wire opcode value whose identification is removed.
    //
    // Returns:  True when an identification was recorded for the value and has been removed,
    //           false when none was recorded.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Remove(OpcodeValue wireValue)
    {
        string? name;
        if (!_accepted.TryGetValue(wireValue, out name))
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "AcceptedOpcodes.Remove: wireValue=" + wireValue + " was not accepted, nothing removed",
                LogLevel.Warn);
            return false;
        }

        _accepted.Remove(wireValue);

        DebugLog.Write(LogChannel.InferenceDebug,
            "AcceptedOpcodes.Remove: wireValue=" + wireValue + " name=" + name + " removed", defaultLoglevel);
        return true;
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
            "AcceptedOpcodes.WireValueFor: no accepted entry named " + opcodeName, defaultLoglevel);
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

        DebugLog.Write(LogChannel.InferenceDebug, "AcceptedOpcodes.Clear: cleared", defaultLoglevel);
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////
// IdentificationSummaryLine
//
// One line of the identification summary: a proposal from a handler run, or the record of a
// handler run that produced no proposal, in which case WireValue is None.  Properties rather
// than fields because WPF bindings require properties.  Checked is true when the line's wire
// value is accepted under the line's opcode name.
///////////////////////////////////////////////////////////////////////////////////////////////
public class IdentificationSummaryLine
{
    public string OpcodeName { get; }
    public OpcodeValue WireValue { get; }
    public double Confidence { get; }
    public string Evidence { get; }
    public IdentificationOutcome Outcome { get; }
    public string Messages { get; }
    public uint RunNumber { get; }

    public string OpcodeHex
    {
        get
        {
            if (WireValue.Exists)
            {
                return WireValue.ToString();
            }
            return string.Empty;
        }
    }

    public bool Checked
    {
        get
        {
            return WireValue.Exists && AcceptedOpcodes.Instance.GetName(WireValue) == OpcodeName;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IdentificationSummaryLine (constructor)
    //
    // opcodeName:  The opcode name the line is about.
    // wireValue:   The proposed wire value, or None when the run produced no proposal.
    // confidence:  The proposal's posterior, 0 when there is no proposal.
    // evidence:    The proposal's evidence ledger, empty when there is no proposal.
    // outcome:     How the handler run ended.
    // messages:    The status lines from the handler run, joined for display.
    // runNumber:   The identification run that produced the line.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IdentificationSummaryLine(string opcodeName, OpcodeValue wireValue, double confidence,
        string evidence, IdentificationOutcome outcome, string messages, uint runNumber)
    {
        OpcodeName = opcodeName;
        WireValue = wireValue;
        Confidence = confidence;
        Evidence = evidence;
        Outcome = outcome;
        Messages = messages;
        RunNumber = runNumber;
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////
// IdentificationSummary
//
// Session-lived summary of identification: the current lines for each opcode name across the
// identification runs of this session.  A single session-wide instance is reached through
// Instance.  Held in memory and emptied by Clear.  Not persisted.
///////////////////////////////////////////////////////////////////////////////////////////////
public class IdentificationSummary
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private static IdentificationSummary? _instance;
    private readonly List<IdentificationSummaryLine> _lines;
    private uint _runCount;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // The single session-wide summary, created on first access.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static IdentificationSummary Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new IdentificationSummary();
            }
            return _instance;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Lines
    //
    // The summary's current lines, in the order they were added.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<IdentificationSummaryLine> Lines
    {
        get { return _lines; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IdentificationSummary (constructor)
    //
    // Creates an empty summary with no runs recorded.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private IdentificationSummary()
    {
        _lines = new List<IdentificationSummaryLine>();
        _runCount = 0;

        DebugLog.Write(LogChannel.InferenceDebug, "IdentificationSummary: created empty", defaultLoglevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddResults
    //
    // Records one identification run.  For each result, the unchecked lines already held for
    // its opcode name are removed, then one line is added per proposal, or a single line with
    // no wire value when the result has no proposals.  Checked lines are kept.
    //
    // results:  The handler results of the run, in handler order.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void AddResults(List<IdentificationResult> results)
    {
        _runCount++;

        for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
        {
            IdentificationResult result = results[resultIndex];
            string messages = string.Join("; ", result.Messages);

            int removed = _lines.RemoveAll(line => line.OpcodeName == result.OpcodeName && !line.Checked);
            if (removed > 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "IdentificationSummary.AddResults: " + result.OpcodeName + " replaced " + removed
                    + " unchecked lines", defaultLoglevel);
            }

            if (result.Proposals.Count == 0)
            {
                _lines.Add(new IdentificationSummaryLine(result.OpcodeName, OpcodeValue.None, 0.0,
                    string.Empty, result.Outcome, messages, _runCount));
                DebugLog.Write(LogChannel.InferenceDebug,
                    "IdentificationSummary.AddResults: " + result.OpcodeName + " " + result.Outcome
                    + ", no proposal line added", defaultLoglevel);
                continue;
            }

            for (int proposalIndex = 0; proposalIndex < result.Proposals.Count; proposalIndex++)
            {
                InferenceProposal proposal = result.Proposals[proposalIndex];
                _lines.Add(new IdentificationSummaryLine(result.OpcodeName, proposal.Opcode.Value,
                    proposal.Confidence, proposal.Evidence, result.Outcome, messages, _runCount));
            }

            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.AddResults: " + result.OpcodeName + " added "
                + result.Proposals.Count + " proposal lines", defaultLoglevel);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "IdentificationSummary.AddResults: run " + _runCount + ", " + results.Count + " results, "
            + _lines.Count + " lines held", defaultLoglevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindAcceptanceConflict
    //
    // Tests whether accepting a wire value under an opcode name would conflict with an
    // existing acceptance: the wire value already accepted under any name, or the opcode name
    // already accepted with any wire value.
    //
    // wireValue:   The wire value to be accepted.
    // opcodeName:  The opcode name it would be accepted under.
    // conflict:    Receives a description of the existing acceptance, empty when none.
    //
    // Returns:  True when a conflict exists, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private bool FindAcceptanceConflict(OpcodeValue wireValue, string opcodeName, out string conflict)
    {
        conflict = string.Empty;

        if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
        {
            conflict = wireValue + " is already accepted as " + AcceptedOpcodes.Instance.GetName(wireValue);
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.FindAcceptanceConflict: " + conflict, defaultLoglevel);
            return true;
        }

        OpcodeValue acceptedValue = AcceptedOpcodes.Instance.WireValueFor(opcodeName);
        if (acceptedValue.Exists)
        {
            conflict = opcodeName + " is already accepted as " + acceptedValue;
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.FindAcceptanceConflict: " + conflict, defaultLoglevel);
            return true;
        }

        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TrySetChecked
    //
    // Checks or unchecks a line.  Checking accepts the line's wire value under its opcode
    // name; unchecking removes that acceptance.  A check is refused when the line has no wire
    // value, when the wire value is already accepted under any name, or when the opcode name
    // is already accepted with any wire value.
    //
    // line:      The line to change.
    // check:     True to check the line, false to uncheck it.
    // conflict:  Receives a description of why a check was refused, empty otherwise.
    //
    // Returns:  True when the line is in the requested state afterward, false when refused.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool TrySetChecked(IdentificationSummaryLine line, bool check, out string conflict)
    {
        conflict = string.Empty;

        if (!line.WireValue.Exists)
        {
            conflict = line.OpcodeName + " has no proposed wire value";
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.TrySetChecked: " + conflict + ", refused", LogLevel.Warn);
            return false;
        }

        if (line.Checked == check)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.TrySetChecked: " + line.OpcodeName + " " + line.OpcodeHex
                + " already " + (check ? "checked" : "unchecked"), defaultLoglevel);
            return true;
        }

        if (!check)
        {
            AcceptedOpcodes.Instance.Remove(line.WireValue);
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.TrySetChecked: unchecked " + line.OpcodeName + " "
                + line.OpcodeHex, defaultLoglevel);
            return true;
        }

        if (FindAcceptanceConflict(line.WireValue, line.OpcodeName, out conflict))
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.TrySetChecked: " + conflict + ", refused", LogLevel.Warn);
            return false;
        }

        AcceptedOpcodes.Instance.Accept(line.WireValue, line.OpcodeName);
        DebugLog.Write(LogChannel.InferenceDebug,
            "IdentificationSummary.TrySetChecked: checked " + line.OpcodeName + " " + line.OpcodeHex,
            defaultLoglevel);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AutoAccept
    //
    // Accepts a handler result's proposal when the result holds exactly one proposal and its
    // posterior is at or above the floor, unless the acceptance would conflict with an
    // existing one.  An acceptance, or a conflict that prevented one, is added to the result's
    // messages.
    //
    // result:  The handler result.
    // floor:   The minimum posterior for automatic acceptance.
    //
    // Returns:  True when the proposal was accepted, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool AutoAccept(IdentificationResult result, double floor)
    {
        if (result.Proposals.Count != 1)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.AutoAccept: " + result.OpcodeName + " has "
                + result.Proposals.Count + " proposals, not auto-accepted", defaultLoglevel);
            return false;
        }

        InferenceProposal proposal = result.Proposals[0];
        OpcodeValue wireValue = proposal.Opcode.Value;

        if (proposal.Confidence < floor)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.AutoAccept: " + result.OpcodeName + " " + wireValue
                + " posterior " + proposal.Confidence.ToString("F3") + " below floor "
                + floor.ToString("F2") + ", not auto-accepted", defaultLoglevel);
            return false;
        }

        string conflict;
        if (FindAcceptanceConflict(wireValue, result.OpcodeName, out conflict))
        {
            result.Messages.Add("not auto-accepted: " + conflict);
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.AutoAccept: " + result.OpcodeName + " " + wireValue
                + " not auto-accepted, " + conflict, LogLevel.Warn);
            return false;
        }

        AcceptedOpcodes.Instance.Accept(wireValue, result.OpcodeName);
        result.Messages.Add("auto-accepted at floor " + floor.ToString("F2"));
        DebugLog.Write(LogChannel.InferenceDebug,
            "IdentificationSummary.AutoAccept: " + result.OpcodeName + " " + wireValue
            + " accepted, posterior " + proposal.Confidence.ToString("F3"), defaultLoglevel);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildCopyText
    //
    // Builds the summary as plain text: a count line, then one entry per line in the order
    // held.  Each entry gives the opcode name, wire value, whether it is accepted, outcome,
    // confidence and run number, followed by its evidence and messages on separate indented
    // lines when present.
    //
    // Returns:  The summary text.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string BuildCopyText()
    {
        StringBuilder text = new StringBuilder();
        text.AppendLine("Identification summary: " + _lines.Count + " lines, " + _runCount + " runs");

        for (int lineIndex = 0; lineIndex < _lines.Count; lineIndex++)
        {
            IdentificationSummaryLine line = _lines[lineIndex];

            string wireText = "none";
            if (line.WireValue.Exists)
            {
                wireText = line.OpcodeHex;
            }

            text.AppendLine(line.OpcodeName
                + "  wire " + wireText
                + "  accepted " + (line.Checked ? "yes" : "no")
                + "  outcome " + line.Outcome
                + "  confidence " + line.Confidence.ToString("F2")
                + "  run " + line.RunNumber);

            if (line.Evidence.Length > 0)
            {
                text.AppendLine("    evidence: " + line.Evidence);
            }
            if (line.Messages.Length > 0)
            {
                text.AppendLine("    messages: " + line.Messages);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "IdentificationSummary.BuildCopyText: " + _lines.Count + " lines", defaultLoglevel);
        return text.ToString();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Removes every line and resets the run count.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        int removed = _lines.Count;
        _lines.Clear();
        _runCount = 0;

        DebugLog.Write(LogChannel.InferenceDebug,
            "IdentificationSummary.Clear: removed " + removed + " lines", defaultLoglevel);
    }
}