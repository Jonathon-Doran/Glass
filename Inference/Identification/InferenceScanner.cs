using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using Inference.Core;
using SQLitePCL;
using System;
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
    private readonly Action<SignalInferenceStatus> _statusHandler;
    private IdentificationResult? _currentResult = null;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // InferenceScanner (constructor)
    //
    // Builds the handler list in identification order and binds the status collector.
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
        _handlers.Add(new InferTargetRequest());
        _handlers.Add(new InferHpUpdate());


        _statusHandler = CollectStatus;

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferenceScanner.ctor: " + _handlers.Count + " handlers", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Scan
    //
    // Runs each handler against the catalog in order and records one result per handler run.
    // A handler whose opcode already has an accepted wire value is skipped and has no result.
    // Status lines published while a handler runs are added to that handler's result.  A
    // result with proposals is offered for automatic acceptance before the next handler runs.
    // A handler that throws or returns null is recorded as Failed with the reason in its
    // messages, and the scan continues with the remaining handlers.
    //
    // catalog:          The session's cataloged packets.
    // autoAcceptFloor:  The minimum posterior for automatic acceptance.
    //
    // Returns:  One result per handler run, in handler order.  Empty when the catalog is null
    //           or every handler was skipped.
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

        GlassContext.SignalBus.Subscribe(_statusHandler);
        try
        {
            for (int handlerIndex = 0; handlerIndex < _handlers.Count; handlerIndex++)
            {
                IInferOpcodes handler = _handlers[handlerIndex];

                if (AcceptedOpcodes.Instance.WireValueFor(handler.OpcodeName).Exists)
                {
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "InferenceScanner.Scan: " + handler.Label + " already accepted, skipped",
                        LogLevel.Trace);
                    continue;
                }

                IdentificationResult result = new IdentificationResult();
                result.OpcodeName = handler.OpcodeName;
                result.Label = handler.Label;
                _currentResult = result;

                List<InferenceProposal>? handlerProposals = null;
                try
                {
                    handlerProposals = handler.Infer(catalog);
                }
                catch (System.Exception ex)
                {
                    result.Outcome = IdentificationOutcome.Failed;
                    result.Messages.Add("threw: " + ex.Message);
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "InferenceScanner.Scan: " + handler.Label + " threw: " + ex.Message,
                        LogLevel.Error);
                }
                finally
                {
                    _currentResult = null;
                }

                if (result.Outcome == IdentificationOutcome.Failed)
                {
                    results.Add(result);
                    continue;
                }

                if (handlerProposals == null)
                {
                    result.Outcome = IdentificationOutcome.Failed;
                    result.Messages.Add("returned null");
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "InferenceScanner.Scan: " + handler.Label + " returned null", LogLevel.Warn);
                    results.Add(result);
                    continue;
                }

                result.Proposals = handlerProposals;
                if (handlerProposals.Count > 0)
                {
                    result.Outcome = IdentificationOutcome.Proposed;
                    IdentificationSummary.Instance.AutoAccept(result, autoAcceptFloor);
                }
                else
                {
                    result.Outcome = IdentificationOutcome.NoProposal;
                }
                results.Add(result);

                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferenceScanner.Scan: " + handler.Label + " -> "
                    + handlerProposals.Count + " proposals", LogLevel.Info);
            }
        }
        finally
        {
            GlassContext.SignalBus.Unsubscribe(_statusHandler);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferenceScanner.Scan: done, " + results.Count + " handler results", LogLevel.Info);
        return results;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CollectStatus
    //
    // Adds a status line to the result of the handler currently running.  A status line
    // published while no handler is running is logged and discarded.
    //
    // signal:  The status carrying the line.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void CollectStatus(SignalInferenceStatus signal)
    {
        if (_currentResult == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferenceScanner.CollectStatus: no handler running, discarded '" + signal.Text + "'",
                LogLevel.Warn);
            return;
        }

        _currentResult.Messages.Add(signal.Text);
        DebugLog.Write(LogChannel.InferenceDebug,
            "InferenceScanner.CollectStatus: " + _currentResult.Label + " '" + signal.Text + "'",
            LogLevel.Trace);
    }
}