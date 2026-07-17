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

    ///////////////////////////////////////////////////////////////////////////////////////////
    public InferenceScanner()
    {
        _handlers = new List<IInferOpcodes>();
        _handlers.Add(new InferNpcMoveUpdate());
        _handlers.Add(new InferMobUpdate());
        _handlers.Add(new InferMovementHistory());
        _handlers.Add(new InferPlayerProfile());
        _handlers.Add(new InferZoneEntry_Z2C());

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferenceScanner.ctor: " + _handlers.Count + " handlers", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Scan
    //
    // Runs each handler against the catalog in order and concatenates their proposals.
    // A handler that throws or returns null is logged and skipped; the scan continues
    // with the remaining handlers.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  All proposals, in handler order.  Empty when the catalog is null or no
    //           handler produced a proposal.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<InferenceProposal> Scan(PacketCatalog catalog)
    {
        List<InferenceProposal> proposals = new List<InferenceProposal>();

        if (catalog == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferenceScanner.Scan: null catalog, nothing to scan", LogLevel.Error);
            return proposals;
        }

        DebugLog.Write(LogChannel.InferenceDebug, "InferenceScanner.Scan: starting", LogLevel.Trace);

        for (int handlerIndex = 0; handlerIndex < _handlers.Count; handlerIndex++)
        {
            IInferOpcodes handler = _handlers[handlerIndex];

            List<InferenceProposal> handlerProposals;
            try
            {
                handlerProposals = handler.Infer(catalog);
            }
            catch (System.Exception ex)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferenceScanner.Scan: " + handler.Label + " threw: " + ex.Message,
                    LogLevel.Error);
                continue;
            }

            if (handlerProposals == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferenceScanner.Scan: " + handler.Label + " returned null", LogLevel.Warn);
                continue;
            }

            proposals.AddRange(handlerProposals);
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferenceScanner.Scan: " + handler.Label + " -> "
                + handlerProposals.Count + " proposals", LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferenceScanner.Scan: done, " + proposals.Count + " total proposals", LogLevel.Trace);
        return proposals;
    }
}