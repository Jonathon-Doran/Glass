using Glass.Core.Logging;
using Inference.Core;

using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferenceScanner
//
// Runs a fixed, ordered sequence of IInferOpcodes handlers against the packet catalog and
// collects their proposals.  The handler list is supplied at construction; its order is the
// identification order.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferenceScanner
{
    private readonly List<IInferOpcodes> _handlers;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // InferenceScanner (constructor)
    //
    // Builds the handler list in identification order.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public InferenceScanner()
    {
        _handlers = new List<IInferOpcodes>();
        _handlers.Add(new InferPlayerProfile());
        _handlers.Add(new InferNpcMoveUpdate());
        _handlers.Add(new InferMobUpdate());
        _handlers.Add(new InferMovementHistory());
        _handlers.Add(new InferZoneEntry_Z2C());
        _handlers.Add(new InferNewZone());
        _handlers.Add(new InferInventory());
        _handlers.Add(new InferSendAA());
        _handlers.Add(new InferCommonMessage());
        _handlers.Add(new InferDeath());
        _handlers.Add(new InferClientUpdate());
        _handlers.Add(new InferManaUpdate());
        _handlers.Add(new InferCastRequest());
        _handlers.Add(new InferCastResolution());
       // _handlers.Add(new InferTargetRequest());
        _handlers.Add(new InferHpUpdate());

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferenceScanner.ctor: " + _handlers.Count + " handlers", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Scan
    //
    // Runs each handler against the catalog in order and records one result per handler.  A
    // result whose opcode names are all already accepted is marked AlreadyAccepted and its
    // proposals are dropped.  A result whose outcome the handler left as None is marked
    // Proposed or NoProposal by its proposal count, and a Proposed result is offered for
    // automatic acceptance before the next handler runs.  A handler that throws or returns
    // null is recorded as a Failed result labeled with the handler's class name, with the
    // reason in its messages, and the scan continues with the remaining handlers.
    //
    // catalog:          The session's cataloged packets.
    // autoAcceptFloor:  The minimum posterior for automatic acceptance.
    //
    // Returns:  One result per handler, in handler order.  Empty when the catalog is null.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<IdentificationResult> Scan(PacketCatalog catalog, double autoAcceptFloor)
    {
        List<IdentificationResult> results = new List<IdentificationResult>();

        if (catalog == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferenceScanner.Scan: null catalog, nothing to scan", LogLevel.Error);
            return results;
        }

        DebugLog.Write(LogChannel.InferenceDebug, "InferenceScanner.Scan: starting", LogLevel.Info);

        for (int handlerIndex = 0; handlerIndex < _handlers.Count; handlerIndex++)
        {
            IInferOpcodes handler = _handlers[handlerIndex];
            string handlerName = handler.GetType().Name;

            IdentificationResult? result = null;
            string failure = string.Empty;
            try
            {
                result = handler.Infer(catalog);
                if (result == null)
                {
                    failure = "returned null";
                }
            }
            catch (System.Exception ex)
            {
                failure = "threw: " + ex.Message;
            }

            if (result == null)
            {
                result = new IdentificationResult();
                result.OpcodeNames.Add(handlerName);
                result.GroupLabel = handlerName;
                result.Outcome = IdentificationOutcome.Failed;
                result.Messages.Add(failure);
                results.Add(result);
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferenceScanner.Scan: " + handlerName + " " + failure, LogLevel.Error);
                continue;
            }

            if (AllAccepted(result.OpcodeNames))
            {
                result.Outcome = IdentificationOutcome.AlreadyAccepted;
                result.Proposals.Clear();
                results.Add(result);
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferenceScanner.Scan: " + result.GroupLabel + " already accepted, proposals dropped",
                    LogLevel.Trace);
                continue;
            }

            if (result.Outcome == IdentificationOutcome.None)
            {
                if (result.Proposals.Count > 0)
                {
                    result.Outcome = IdentificationOutcome.Proposed;
                }
                else
                {
                    result.Outcome = IdentificationOutcome.NoProposal;
                }
            }

            if (result.Outcome == IdentificationOutcome.Proposed)
            {
                IdentificationSummary.Instance.AutoAccept(result, autoAcceptFloor);
            }
            results.Add(result);

            DebugLog.Write(LogChannel.InferenceDebug,
                "InferenceScanner.Scan: " + result.GroupLabel + " " + result.Outcome + ", "
                + result.Proposals.Count + " proposals", LogLevel.Info);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferenceScanner.Scan: done, " + results.Count + " handler results", LogLevel.Info);
        return results;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AllAccepted
    //
    // Tests whether every opcode name in a list has an accepted wire value.
    //
    // opcodeNames:  The opcode names to test.
    //
    // Returns:  True when the list is not empty and every name is accepted, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool AllAccepted(List<string> opcodeNames)
    {
        if (opcodeNames.Count == 0)
        {
            return false;
        }

        for (int nameIndex = 0; nameIndex < opcodeNames.Count; nameIndex++)
        {
            if (!AcceptedOpcodes.Instance.WireValueFor(opcodeNames[nameIndex]).Exists)
            {
                return false;
            }
        }
        return true;
    }
}