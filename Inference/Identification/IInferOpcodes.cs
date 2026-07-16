using Glass.Network.Protocol;
using Inference.Core;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferenceProposal
//
// One handler's proposed identity for an opcode: the wire value it believes carries the
// opcode, a confidence label, and a human-readable evidence summary. 
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferenceProposal
{
    public PatchOpcode Opcode;
    public double Confidence;
    public string Evidence = string.Empty;
    public string Label = string.Empty;
}

///////////////////////////////////////////////////////////////////////////////////////////////
// IInferOpcodes
//
// A cold-path identification strategy for a single opcode version.  Given the session's
// packet catalog, the handler examines observed traffic and proposes the wire value that
// now carries its opcode, or null if it cannot.
//
// Implementations are stateless: all input arrives through Infer, and nothing is retained
// between calls.  The runtime OpcodeDispatch handlers are unrelated. Those decode a known
// opcode's payload; these argue for an unknown opcode's identity.
///////////////////////////////////////////////////////////////////////////////////////////////
public interface IInferOpcodes
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeName
    //
    // The logical name of the opcode this handler identifies (e.g. "OP_NpcMoveUpdate").
    ///////////////////////////////////////////////////////////////////////////////////////////
    string OpcodeName { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Label
    //
    // A printable identifier for the opcode version this handler identifies, used to group
    // and head its proposals in the display (e.g. "OP_NpcMoveUpdate v1").
    ///////////////////////////////////////////////////////////////////////////////////////////
    string Label { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Infer
    //
    // Examines the catalog and proposes the wire value carrying this handler's opcode.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns: proposals ordered best-first, empty when no wire value matches
    ///////////////////////////////////////////////////////////////////////////////////////////
    List<InferenceProposal> Infer(PacketCatalog catalog);
}
