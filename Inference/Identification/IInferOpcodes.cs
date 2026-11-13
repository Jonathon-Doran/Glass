using Glass.Network.Protocol;
using Inference.Core;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeAssignment
//
// One opcode's identification within a proposal: the opcode name and label, the wire value
// proposed to carry it, and this member's own confidence and evidence.
///////////////////////////////////////////////////////////////////////////////////////////////
public class OpcodeAssignment
{
    public string OpcodeName = string.Empty;
    public string Label = string.Empty;
    public PatchOpcode Opcode = PatchOpcode.None;
    public double Confidence;
    public string Evidence = string.Empty;
}

///////////////////////////////////////////////////////////////////////////////////////////////
// InferenceProposal
//
// One handler's proposed identification of one or more opcodes, taken together: the
// assignments, each naming an opcode and the wire value proposed to carry it, with the
// joint confidence and evidence that every assignment in the proposal is correct.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferenceProposal
{
    public List<OpcodeAssignment> Assignments = new List<OpcodeAssignment>();
    public double Confidence;
    public string Evidence = string.Empty;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ForOpcode
    //
    // Creates a proposal identifying one opcode: a single assignment carrying the opcode
    // name as name and label, the proposed wire value, the confidence and the evidence, with
    // the same confidence and evidence as the proposal's joint values.
    //
    // opcodeName:  The opcode identified.
    // opcode:      The wire value proposed to carry it, at the current patch level.
    // confidence:  The posterior for the identification.
    // evidence:    The evidence ledger for the identification.
    //
    // Returns:  The new proposal.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static InferenceProposal ForOpcode(string opcodeName, PatchOpcode opcode, double confidence,
        string evidence)
    {
        OpcodeAssignment assignment = new OpcodeAssignment();
        assignment.OpcodeName = opcodeName;
        assignment.Label = opcodeName;
        assignment.Opcode = opcode;
        assignment.Confidence = confidence;
        assignment.Evidence = evidence;

        InferenceProposal proposal = new InferenceProposal();
        proposal.Assignments.Add(assignment);
        proposal.Confidence = confidence;
        proposal.Evidence = evidence;
        return proposal;
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////
// IdentificationOutcome
//
// How one handler's identification run ended.  None is the state before an outcome is
// recorded.  Proposed: the handler returned at least one proposal.  NoProposal: the handler
// returned no proposal.  Incomplete: the handler's best proposal lacks a wire value for at
// least one of its opcodes.  AlreadyAccepted: every opcode the handler identifies was
// already accepted, so its proposals were dropped.  Failed: the handler threw or returned
// null.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum IdentificationOutcome
{
    None,
    Proposed,
    NoProposal,
    Incomplete,
    AlreadyAccepted,
    Failed
}

///////////////////////////////////////////////////////////////////////////////////////////////
// IdentificationResult
//
// The record of one handler's identification run: the opcode names the handler identifies,
// the label its lines are grouped under, how the run ended, the proposals it made, and the
// status messages produced during the run.
///////////////////////////////////////////////////////////////////////////////////////////////
public class IdentificationResult
{
    public List<string> OpcodeNames = new List<string>();
    public string GroupLabel = string.Empty;
    public IdentificationOutcome Outcome = IdentificationOutcome.None;
    public List<InferenceProposal> Proposals = new List<InferenceProposal>();
    public List<string> Messages = new List<string>();

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ForOpcode
    //
    // Creates an empty result for a handler that identifies one opcode, with that opcode's
    // name as the result's only opcode name and as its group label.
    //
    // opcodeName:  The opcode the handler identifies.
    //
    // Returns:  The new result.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static IdentificationResult ForOpcode(string opcodeName)
    {
        IdentificationResult result = new IdentificationResult();
        result.OpcodeNames.Add(opcodeName);
        result.GroupLabel = opcodeName;
        return result;
    }
}

///////////////////////////////////////////////////////////////////////////////////////////////
// IInferOpcodes
//
// A cold-path identification strategy for one opcode, or for a group of opcodes identified
// together.  Given the session's packet catalog, the handler examines observed traffic and
// returns a result naming the opcodes it identifies, its proposals, and any status messages
// explaining why it did or did not propose.
//
// Implementations are stateless: all input arrives through Infer, and nothing is retained
// between calls.  The runtime OpcodeDispatch handlers are unrelated. Those decode a known
// opcode's payload; these argue for an unknown opcode's identity.
///////////////////////////////////////////////////////////////////////////////////////////////
public interface IInferOpcodes
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // Infer
    //
    // Examines the catalog and proposes the wire values carrying this handler's opcodes.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The handler's result, with its proposals ordered best first.
    ///////////////////////////////////////////////////////////////////////////////////////////
    IdentificationResult Infer(PacketCatalog catalog);
}
