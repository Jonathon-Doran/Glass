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
// One line of the identification summary: one opcode assignment from a proposal, or the
// record of a handler run that produced no proposal for an opcode, in which case WireValue
// is None.  Lines from the same proposal share a GroupKey and are checked and unchecked
// together.  Properties rather than fields because WPF bindings require properties.
// Checked is true when the line's wire value is accepted under the line's opcode name.
///////////////////////////////////////////////////////////////////////////////////////////////
public class IdentificationSummaryLine
{
    public string OpcodeName { get; }
    public OpcodeValue WireValue { get; }
    public double Confidence { get; }
    public string Evidence { get; }
    public uint GroupKey { get; }
    public string GroupLabel { get; }
    public double GroupConfidence { get; }
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
    // opcodeName:       The opcode name the line is about.
    // wireValue:        The proposed wire value, or None when there is none.
    // confidence:       This assignment's own confidence, 0 when there is no proposal.
    // evidence:         This assignment's own evidence, empty when there is no proposal.
    // groupKey:         The key shared by every line of the same proposal.
    // groupLabel:       The label the line is grouped under for display.
    // groupConfidence:  The proposal's joint confidence, 0 when there is no proposal.
    // outcome:          How the handler run ended.
    // messages:         The status lines from the handler run, joined for display.
    // runNumber:        The identification run that produced the line.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IdentificationSummaryLine(string opcodeName, OpcodeValue wireValue, double confidence,
        string evidence, uint groupKey, string groupLabel, double groupConfidence,
        IdentificationOutcome outcome, string messages, uint runNumber)
    {
        OpcodeName = opcodeName;
        WireValue = wireValue;
        Confidence = confidence;
        Evidence = evidence;
        GroupKey = groupKey;
        GroupLabel = groupLabel;
        GroupConfidence = groupConfidence;
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
    private uint _groupCount = 0;

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
    // Records one identification run.  A result marked AlreadyAccepted changes nothing.  For
    // every other result, the unchecked lines already held for any of its opcode names are
    // removed, then each proposal is added as a group of lines sharing one group key, one
    // line per assignment.  A result with no proposals is added as one group with a line per
    // opcode name and no wire value.  Checked lines are kept.
    //
    // results:  The handler results of the run, in handler order.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void AddResults(List<IdentificationResult> results)
    {
        _runCount++;

        for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
        {
            IdentificationResult result = results[resultIndex];

            if (result.Outcome == IdentificationOutcome.AlreadyAccepted)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "IdentificationSummary.AddResults: " + result.GroupLabel + " already accepted, no lines added",
                    defaultLoglevel);
                continue;
            }

            string messages = string.Join("; ", result.Messages);

            int removed = _lines.RemoveAll(line => result.OpcodeNames.Contains(line.OpcodeName) && !line.Checked);
            if (removed > 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "IdentificationSummary.AddResults: " + result.GroupLabel + " replaced " + removed
                    + " unchecked lines", defaultLoglevel);
            }

            if (result.Proposals.Count == 0)
            {
                _groupCount++;
                for (int nameIndex = 0; nameIndex < result.OpcodeNames.Count; nameIndex++)
                {
                    _lines.Add(new IdentificationSummaryLine(result.OpcodeNames[nameIndex], OpcodeValue.None,
                        0.0, string.Empty, _groupCount, result.GroupLabel, 0.0, result.Outcome, messages,
                        _runCount));
                }
                DebugLog.Write(LogChannel.InferenceDebug,
                    "IdentificationSummary.AddResults: " + result.GroupLabel + " " + result.Outcome
                    + ", " + result.OpcodeNames.Count + " lines with no wire value added", defaultLoglevel);
                continue;
            }

            for (int proposalIndex = 0; proposalIndex < result.Proposals.Count; proposalIndex++)
            {
                InferenceProposal proposal = result.Proposals[proposalIndex];
                _groupCount++;

                for (int assignmentIndex = 0; assignmentIndex < proposal.Assignments.Count; assignmentIndex++)
                {
                    OpcodeAssignment assignment = proposal.Assignments[assignmentIndex];
                    _lines.Add(new IdentificationSummaryLine(assignment.OpcodeName, assignment.Opcode.Value,
                        assignment.Confidence, assignment.Evidence, _groupCount, result.GroupLabel,
                        proposal.Confidence, result.Outcome, messages, _runCount));
                }
            }

            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.AddResults: " + result.GroupLabel + " added "
                + result.Proposals.Count + " proposals", defaultLoglevel);
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
    // Checks or unchecks a line together with every line sharing its group key.  Unchecking
    // removes the acceptance of every member that is accepted.  Checking is refused when any
    // member has no wire value, when two members share a wire value or an opcode name, or
    // when any member not yet accepted would conflict with an existing acceptance; otherwise
    // every member not yet accepted is accepted.  A refused check accepts nothing.
    //
    // line:      The line that was clicked.
    // check:     True to check the line's group, false to uncheck it.
    // conflict:  Receives a description of why a check was refused, empty otherwise.
    //
    // Returns:  True when the group is in the requested state afterward, false when refused.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool TrySetChecked(IdentificationSummaryLine line, bool check, out string conflict)
    {
        conflict = string.Empty;
        List<IdentificationSummaryLine> members = _lines.FindAll(member => member.GroupKey == line.GroupKey);

        if (!check)
        {
            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                if (members[memberIndex].Checked)
                {
                    AcceptedOpcodes.Instance.Remove(members[memberIndex].WireValue);
                }
            }
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.TrySetChecked: unchecked " + line.GroupLabel + " group "
                + line.GroupKey + ", " + members.Count + " members", defaultLoglevel);
            return true;
        }

        for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
        {
            IdentificationSummaryLine member = members[memberIndex];

            if (!member.WireValue.Exists)
            {
                conflict = member.OpcodeName + " has no proposed wire value";
                DebugLog.Write(LogChannel.InferenceDebug,
                    "IdentificationSummary.TrySetChecked: " + conflict + ", refused", LogLevel.Warn);
                return false;
            }

            for (int otherIndex = memberIndex + 1; otherIndex < members.Count; otherIndex++)
            {
                IdentificationSummaryLine other = members[otherIndex];
                if (other.WireValue == member.WireValue || other.OpcodeName == member.OpcodeName)
                {
                    conflict = member.OpcodeName + " " + member.OpcodeHex + " and " + other.OpcodeName + " "
                        + other.OpcodeHex + " share a wire value or name";
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "IdentificationSummary.TrySetChecked: " + conflict + ", refused", LogLevel.Warn);
                    return false;
                }
            }

            if (member.Checked)
            {
                continue;
            }

            if (FindAcceptanceConflict(member.WireValue, member.OpcodeName, out conflict))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "IdentificationSummary.TrySetChecked: " + conflict + ", refused", LogLevel.Warn);
                return false;
            }
        }

        for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
        {
            IdentificationSummaryLine member = members[memberIndex];
            if (!member.Checked)
            {
                AcceptedOpcodes.Instance.Accept(member.WireValue, member.OpcodeName);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "IdentificationSummary.TrySetChecked: checked " + line.GroupLabel + " group " + line.GroupKey
            + ", " + members.Count + " members", defaultLoglevel);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AutoAccept
    //
    // Accepts every assignment of a handler result's proposal when the result holds exactly
    // one proposal, its joint posterior is at or above the floor, every assignment has a wire
    // value, no two assignments share a wire value or opcode name, and no assignment would
    // conflict with an existing acceptance.  Otherwise nothing is accepted.  An acceptance,
    // or the reason one was prevented after the floor test passed, is added to the result's
    // messages.
    //
    // result:  The handler result.
    // floor:   The minimum joint posterior for automatic acceptance.
    //
    // Returns:  True when the proposal was accepted, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool AutoAccept(IdentificationResult result, double floor)
    {
        if (result.Proposals.Count != 1)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.AutoAccept: " + result.GroupLabel + " has "
                + result.Proposals.Count + " proposals, not auto-accepted", defaultLoglevel);
            return false;
        }

        InferenceProposal proposal = result.Proposals[0];

        if (proposal.Confidence < floor)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "IdentificationSummary.AutoAccept: " + result.GroupLabel + " posterior "
                + proposal.Confidence.ToString("F3") + " below floor " + floor.ToString("F2")
                + ", not auto-accepted", defaultLoglevel);
            return false;
        }

        for (int assignmentIndex = 0; assignmentIndex < proposal.Assignments.Count; assignmentIndex++)
        {
            OpcodeAssignment assignment = proposal.Assignments[assignmentIndex];
            OpcodeValue wireValue = assignment.Opcode.Value;
            string conflict;

            if (!wireValue.Exists)
            {
                conflict = assignment.OpcodeName + " has no proposed wire value";
                result.Messages.Add("not auto-accepted: " + conflict);
                DebugLog.Write(LogChannel.InferenceDebug,
                    "IdentificationSummary.AutoAccept: " + result.GroupLabel + " not auto-accepted, "
                    + conflict, LogLevel.Warn);
                return false;
            }

            for (int otherIndex = assignmentIndex + 1; otherIndex < proposal.Assignments.Count; otherIndex++)
            {
                OpcodeAssignment other = proposal.Assignments[otherIndex];
                if (other.Opcode.Value == wireValue || other.OpcodeName == assignment.OpcodeName)
                {
                    conflict = assignment.OpcodeName + " and " + other.OpcodeName + " share a wire value or name";
                    result.Messages.Add("not auto-accepted: " + conflict);
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "IdentificationSummary.AutoAccept: " + result.GroupLabel + " not auto-accepted, "
                        + conflict, LogLevel.Warn);
                    return false;
                }
            }

            if (FindAcceptanceConflict(wireValue, assignment.OpcodeName, out conflict))
            {
                result.Messages.Add("not auto-accepted: " + conflict);
                DebugLog.Write(LogChannel.InferenceDebug,
                    "IdentificationSummary.AutoAccept: " + result.GroupLabel + " not auto-accepted, "
                    + conflict, LogLevel.Warn);
                return false;
            }
        }

        for (int assignmentIndex = 0; assignmentIndex < proposal.Assignments.Count; assignmentIndex++)
        {
            OpcodeAssignment assignment = proposal.Assignments[assignmentIndex];
            AcceptedOpcodes.Instance.Accept(assignment.Opcode.Value, assignment.OpcodeName);
        }

        result.Messages.Add("auto-accepted at floor " + floor.ToString("F2"));
        DebugLog.Write(LogChannel.InferenceDebug,
            "IdentificationSummary.AutoAccept: " + result.GroupLabel + " accepted "
            + proposal.Assignments.Count + " assignments, posterior " + proposal.Confidence.ToString("F3"),
            defaultLoglevel);
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