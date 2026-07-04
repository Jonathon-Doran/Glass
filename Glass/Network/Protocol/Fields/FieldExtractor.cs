using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Memory;
using Glass.Data;
using Glass.Network.Protocol.Fields;
using Microsoft.Data.Sqlite;
using PacketDotNet;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldExtractor
//
// Decodes a packet payload into a tree of collection instances.  Owns two stores for the
// extraction in progress: a Gate forest (_gates), whose nodes link the collection instances
// and reference their decoded bags, and a bag store (_bags), whose records hold each
// instance's slots and arena.  Gates are addressed by GateHandle and bags by BagHandle,
// each handle being an index into its store.
//
// Lifetime and threading: the extractor holds per-extraction state, so it decodes one tree
// at a time and is not shared across threads.  Concurrent extraction is achieved by
// constructing one FieldExtractor per thread.  One instance is constructed in
// ProtocolStackBootstrap.Initialize and reached through GlassContext.
///////////////////////////////////////////////////////////////////////////////////////////////
public class FieldExtractor
{
    // debugging flag for deep inspection of item collections
    private static bool _traceCollection1 = true;

    // The Gate forest for the extraction in progress, indexed by GateHandle.  Reset between
    // extractions.
    private readonly List<Gate> _gates;

    // The bag store for the extraction in progress, indexed by BagHandle.  Reset between
    // extractions.
    private readonly List<FieldBag> _bags;

    // Idle FieldBag objects available for reuse.
    private readonly Stack<FieldBag> _freeBags;

    // The absolute payload bit position where the current collection instance begins.  Read
    // as the relocation base when resolving each field's absolute position. Reset to zero at the start of each extraction.
    private uint _bitCursor;

    // The transient child-index for the gate most recently entered.  EnterGate walks that
    // gate's instance bags into this array; ChildBag reads instance i from it.  Grown to
    // the largest gate seen and reused across EnterGate calls; the live extent is the first
    // _indexCount entries.
    private BagHandle[] _childIndex;

    // The bag selected by the most recent EnterGate call — instance bagIndex of the gate
    // entered.  The handle-keyed reads resolve a SlotId against this bag.  Set only by
    // EnterGate; BagHandle.None until the first EnterGate of an extraction.
    private BagHandle _activeBag;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FieldExtractor (constructor)
    //
    // Constructs the Gate forest and bag store the extractor fills during a single
    // extraction.  Both are empty until an extraction populates them and are reset between
    // extractions.  One FieldExtractor is constructed in ProtocolStackBootstrap.Initialize
    // and reached through GlassContext; it extracts one tree at a time.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldExtractor()
    {
        _gates = new List<Gate>();
        _bags = new List<FieldBag>();
        _freeBags = new Stack<FieldBag>();
        _childIndex = Array.Empty<BagHandle>();
        _activeBag = BagHandle.None;
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetCursor
    //
    // Sets the bit cursor to the given absolute bit position.
    //
    // absoluteBit:  The absolute bit position to set the cursor to.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void SetCursor(uint absoluteBit)
    {
        _bitCursor = absoluteBit;
   }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AlignCursor
    //
    // Rounds the bit cursor up to the next byte boundary.  Called once at the end of each
    // collection instance after all fields have been extracted.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AlignCursor()
    {
        uint aligned = (_bitCursor + 7u) & ~7u;
        _bitCursor = aligned;
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // Extract
    //
    // Decodes a packet payload into a tree of collection instances, beginning at the given
    // top-level gate definition.  Resets the bit cursor, then expands the top-level gate —
    // which creates its gate and instance bags and recurses through any nested gates — and
    // returns the handle of the gate created for that top-level definition.  The returned
    // handle is the root the consumer navigates from.
    //
    // gateDefinition:  The top-level gate definition to expand.
    // payload:         The raw payload bytes to decode.
    //
    // Returns:  The handle of the root gate.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public GateHandle Extract(GateDefinitionHandle gateDefinition,
        ReadOnlySpan<byte> payload)
    {
        _bitCursor = 0u;

        DebugLog.Write(LogChannel.Fields, "FieldExtractor.Extract: top-level gate " + gateDefinition
            + ", payload " + payload.Length + " bytes");

        if (payload.Length == 0)
        {
            PatchLevel patchLevel = GlassContext.CurrentPatchLevel;
            GateDefinition definition = GlassContext.PatchRegistry.GetGateDefinition(patchLevel, gateDefinition);

            DebugLog.Write(LogChannel.Fields, "0 length payload passed to extract for gate " +
                definition);
            return GateHandle.None;
        }
        if (gateDefinition.Exists == false)
        {
            return GateHandle.None;
        }

        GateHandle rootGate = ExpandMultiplicity(gateDefinition, BagHandle.None, payload);
        _activeBag = _gates[(int)(uint)rootGate].Head;

        DebugLog.Write(LogChannel.Fields, "FieldExtractor.Extract: descent complete, root gate "
            + rootGate + ", " + _bags.Count + " bag(s) filled");

        return rootGate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExpandMultiplicity
    //
    // Creates the gate for the given definition within the spawning bag, then decodes its
    // child collection according to its multiplicity kind.  Each instance gets its own bag:
    // the bag is created, linked back to the gate through ParentGate to weave the resolution
    // spine, appended to the gate's instance chain, and filled.
    //
    // gateDefinitionHandle:  The gate definition to instantiate and run.
    // spawningBag:           The bag being filled when the gate was reached, or BagHandle.None
    //                        at the top level.  Becomes the gate's ParentBag.
    // payload:               The raw payload bytes the instances decode from.
    //
    // Returns:  The handle of the gate created and run.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private GateHandle ExpandMultiplicity(GateDefinitionHandle gateDefinitionHandle,
     BagHandle spawningBag, ReadOnlySpan<byte> payload)
    {
        GateHandle gateHandle = CreateGate(gateDefinitionHandle, spawningBag);
        Gate gate = _gates[(int)(uint)gateHandle];

        DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExpandMultiplicity: gate " + gateHandle
            + " kind " + gate.Kind + " child " + gate.ChildCollection);

        switch (gate.Kind)
        {
            case MultiplicityKind.Once:
                {
                    BagHandle bagHandle = CreateBag(gate.ChildCollection);
                    _bags[(int)(uint)bagHandle].ParentGate = gateHandle;
                    AppendInstanceBag(gateHandle, bagHandle);

                    uint cursorBefore = _bitCursor;
                    bool extracted = ExtractCollection(gate.ChildCollection, payload, bagHandle);

                    if (extracted == false)
                    {
                        string failure = "FieldExtractor.ExpandMultiplicity: gate " + gateHandle
                            + " Once instance bag " + bagHandle + " failed extraction at cursor "
                            + cursorBefore + "; a required single instance ran off the payload";
                        DebugLog.WriteMultiline(LogChannel.Fields, failure + Environment.NewLine
                            + Environment.StackTrace);
                        Environment.FailFast(failure);
                    }

                    gate = _gates[(int)(uint)gateHandle];
                    gate.BitsConsumed += _bitCursor - cursorBefore;
                    _gates[(int)(uint)gateHandle] = gate;

                    DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExpandMultiplicity: gate "
                        + gateHandle + " Once bag " + bagHandle + " consumed "
                        + (_bitCursor - cursorBefore) + " bits, total " + gate.BitsConsumed);
                    break;
                }

            case MultiplicityKind.Times:
                {
                    GateDefinition definition = GlassContext.PatchRegistry.GetGateDefinition(
                        GlassContext.CurrentPatchLevel, gateDefinitionHandle);
                    uint resolvedCount = ResolveCount(spawningBag, definition);
                    DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExpandMultiplicity: gate "
                        + gateHandle + " Times count resolved to " + resolvedCount);

                    for (uint instanceIndex = 0; instanceIndex < resolvedCount; instanceIndex++)
                    {
                        BagHandle bagHandle = CreateBag(gate.ChildCollection);
                        _bags[(int)(uint)bagHandle].ParentGate = gateHandle;
                        AppendInstanceBag(gateHandle, bagHandle);

                        uint cursorBefore = _bitCursor;
                        bool extracted = ExtractCollection(gate.ChildCollection, payload, bagHandle);

                        if (extracted == false)
                        {
                            string failure = "FieldExtractor.ExpandMultiplicity: gate " + gateHandle
                                + " Times instance " + instanceIndex + " of " + resolvedCount
                                + " bag " + bagHandle + " failed extraction at cursor " + cursorBefore
                                + "; a counted instance ran off the payload";
                            DebugLog.WriteMultiline(LogChannel.Fields, failure + Environment.NewLine
                                + Environment.StackTrace);
                            Environment.FailFast(failure);
                        }

                        gate = _gates[(int)(uint)gateHandle];
                        gate.BitsConsumed += _bitCursor - cursorBefore;
                        _gates[(int)(uint)gateHandle] = gate;

                        DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExpandMultiplicity: gate "
                            + gateHandle + " Times instance " + instanceIndex + " of " + resolvedCount
                            + " bag " + bagHandle + " consumed " + (_bitCursor - cursorBefore)
                            + " bits, total " + gate.BitsConsumed);
                    }
                    break;
                }

            case MultiplicityKind.UntilEnd:
                {
                    uint payloadBits = (uint)payload.Length * 8u;
                    while (_bitCursor < payloadBits)
                    {
                        BagHandle bagHandle = CreateBag(gate.ChildCollection);
                        _bags[(int)(uint)bagHandle].ParentGate = gateHandle;
                        AppendInstanceBag(gateHandle, bagHandle);

                        uint cursorBefore = _bitCursor;
                        bool extracted = ExtractCollection(gate.ChildCollection, payload, bagHandle);

                        if (extracted == false)
                        {
                            RemoveInstanceBag(gateHandle, bagHandle);
                            _bags[(int)(uint)bagHandle].Release();

                            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExpandMultiplicity: gate "
                                + gateHandle + " UntilEnd terminated at cursor " + _bitCursor + " of "
                                + payloadBits + "; discarded partial bag " + bagHandle);
                            break;
                        }

                        uint consumed = _bitCursor - cursorBefore;

                        gate = _gates[(int)(uint)gateHandle];
                        gate.BitsConsumed += consumed;
                        _gates[(int)(uint)gateHandle] = gate;

                        DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExpandMultiplicity: gate "
                            + gateHandle + " UntilEnd instance bag " + bagHandle + " consumed "
                            + consumed + " bits, cursor " + _bitCursor + " of " + payloadBits);
                    }
                    break;
                }

            default:
                {
                    string failure = "FieldExtractor.ExpandMultiplicity: gate " + gateHandle
                        + " has unhandled multiplicity kind " + gate.Kind;
                    DebugLog.WriteMultiline(LogChannel.Fields, failure + Environment.NewLine
                        + Environment.StackTrace);
                    Environment.FailFast(failure);
                    break;
                }
        }

        DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExpandMultiplicity: gate " + gateHandle
            + " complete; head " + gate.Head + " tail " + gate.Tail);

        return gateHandle;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExtractCollection
    //
    // Walks the field definitions for the given collection and writes one FieldSlot per
    // definition into the bag addressed by bagHandle, preserving definition-to-slot index
    // alignment. Fills the bag for one instance: the instance begins at the current bit cursor,
    // captured as collectionStartBit, and each field's absolute payload position is computed as
    // collectionStartBit plus the field's collection-local start bit.  The cursor is advanced
    // after each field and rounded to the next byte boundary at the end of the instance, and
    // true is returned.
    //
    // A field carrying a predicate is decoded only when its predicate holds; when it does not
    // the slot is left Empty and no payload is consumed.  This is a successful skip and the
    // walk continues.  A field carrying a gate decodes its child collection; the gate field's
    // own slot is added for alignment and left Empty.
    //
    // A field whose decode does not succeed for a reason other than payload length (Unknown
    // encoding, unsupported bit width) produces a slot that is added but left Empty, and the
    // walk continues.
    //
    // The instance fails when a non-predicate-skipped field fails the HasBits bounds check.
    // The walk stops at once, the bit cursor is left where the instance began -- the final
    // advance is never reached -- and false is returned.  The bag holds the partial slots
    // walked before the failure; this method neither clears the bag nor unlinks it.
    //
    // A collection with no field definitions for the current patch is treated as a failed
    // instance: false is returned and no slots are written.
    //
    // collection:  The collection whose field definitions to walk.
    // payload:     The raw payload bytes.  Field offsets are relocated past the bit cursor
    //              into this span.
    // bagHandle:   The handle of the bag to fill.  Resolved locally; the bag is mutated in
    //              place.
    //
    // Returns:  true when every definition was walked and the cursor advanced past the
    //           instance; false when a field failed the bounds check, or the collection has no
    //           field definitions, and the instance was abandoned with the cursor untouched.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private bool ExtractCollection(CollectionHandle collection, ReadOnlySpan<byte> payload,
            BagHandle bagHandle)
    {
        PatchLevel patchLevel = GlassContext.CurrentPatchLevel;
        FieldBag bag = _bags[(int)(uint)bagHandle];

        FieldDefinition[]? definitions = GlassContext.PatchRegistry.GetFields(collection);
        if (definitions == null)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractCollection: collection "
                + collection + " has no field definitions for this patch, nothing extracted", LogLevel.Warn);
            return false;
        }

        Span<uint> resolvedEndBits = stackalloc uint[definitions.Length];
        uint collectionStartBit = _bitCursor;

        for (uint definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            FieldDefinition definition = definitions[definitionIndex];

            ref FieldSlot slot = ref bag.AddSlot();
            slot.SetName(bag, definition.Name);
            slot.Sequence = (ushort)definition.Sequence;

            uint localStartBit = ResolveFieldStartBit(definitions, resolvedEndBits, definitionIndex);
            uint effectiveBitOffset = collectionStartBit + localStartBit;
            slot.WireBitOffset = effectiveBitOffset;
            slot.WireBitLength = 0;


            if (_traceCollection1 && (((uint)collection == 1u) || (uint)collection == 6u))
            {
                DebugLog.Write(LogChannel.Fields, "Inventory field '"
                    + definition.Name + "' bitCursor=0x" + _bitCursor.ToString("X4") + "=" + _bitCursor.ToString()
                    + " byteCursor=" + (_bitCursor / 8u).ToString("X4")
                    + " localStart=" + localStartBit
                    + " effectiveOffset=" + effectiveBitOffset
                    + " effectiveByte=" + (effectiveBitOffset / 8u)
                    + " bitLength=" + definition.BitLength
                    + " byteLength=" + (definition.BitLength / 8u), LogLevel.Info);
            }

            if (definition.Predicate.Op != PredicateOp.None)
            {
                if (EvaluatePredicate(bag, definition.Predicate) == false)
                {
                    resolvedEndBits[(int)definitionIndex] = localStartBit;
                    DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractCollection: field '"
                        + definition.Name + "' predicate false, skipped", LogLevel.Trace);
                    continue;
                }
            }

            if (HasBits(payload, effectiveBitOffset, definition.BitLength) == false)
            {
                DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractCollection: field '"
                    + definition.Name + "' ran off payload at offset " + effectiveBitOffset
                    + " for length " + definition.BitLength + "; abandoning instance, cursor left at "
                    + _bitCursor, LogLevel.Warn);
                return false;
            }

            switch (definition.Encoding)
            {
                case FieldEncoding.Unknown:
                    break;

                case FieldEncoding.UInt:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, effectiveBitOffset, definition.BitLength, out raw) == true)
                        {
                            slot.SetUInt((uint)raw);
                        }
                        slot.WireBitLength = (ushort)definition.BitLength;
                        break;
                    }

                case FieldEncoding.Int:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, effectiveBitOffset, definition.BitLength, out raw) == true)
                        {
                            int signed = SignExtend(raw, definition.BitLength);
                            slot.SetInt(signed);
                        }
                        slot.WireBitLength = (ushort)definition.BitLength;
                        break;
                    }

                case FieldEncoding.UIntMsb:
                    ExtractUIntMsb(payload, effectiveBitOffset, definition.BitLength, ref slot);
                    slot.WireBitLength = (ushort)definition.BitLength;
                    break;

                case FieldEncoding.Float:
                    ExtractFloatLE(payload, effectiveBitOffset, definition.BitLength, ref slot);
                    slot.WireBitLength = (ushort)definition.BitLength;
                    break;

                case FieldEncoding.UIntMasked:
                    {
                        ulong raw;
                        if (TryReadBitsLE(payload, effectiveBitOffset, definition.BitLength, out raw) == true)
                        {
                            slot.SetUInt((uint)raw);
                        }
                        slot.WireBitLength = (ushort)definition.BitLength;
                        break;
                    }

                case FieldEncoding.SignMagnitudeLsb:
                    ExtractSignMagnitudeLsb(payload, effectiveBitOffset, definition.BitLength, definition.Divisor, ref slot);
                    slot.WireBitLength = (ushort)definition.BitLength;
                    break;

                case FieldEncoding.SignMagnitudeMsb:
                    ExtractSignMagnitudeMsb(payload, effectiveBitOffset, definition.BitLength, definition.Divisor, ref slot);
                    slot.WireBitLength = (ushort)definition.BitLength;
                    break;

                case FieldEncoding.OptSignMagnitudeMsb:
                    break;

                case FieldEncoding.CsvToken:
                    break;

                case FieldEncoding.StringNullTerminated:
                    slot.WireBitLength = (ushort)(ExtractNullTerminatedString(payload, effectiveBitOffset, bag, ref slot) * 8u);
                    break;

                case FieldEncoding.StringLengthPrefixed:
                    slot.WireBitLength = (ushort)(ExtractLengthPrefixedString(payload, effectiveBitOffset, bag, ref slot) * 8u);
                    break;
                case FieldEncoding.Blob:
                    {
                        uint bytesConsumed = ExtractBlob(payload, effectiveBitOffset,
                            definition.BlobByteCount, bag, ref slot);
                        slot.WireBitLength = (ushort)(bytesConsumed * 8u);
                        break;
                    }
                case FieldEncoding.Gate:
                    {
                        GateHandle gateHandle = ExpandMultiplicity(definition.Gate, bagHandle, payload);
                        slot.SetGate(gateHandle);
                        slot.WireBitLength = (ushort)_gates[(int)(uint)gateHandle].BitsConsumed;
                        AppendChildGate(bagHandle, gateHandle);

                        DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractCollection: gate field '"
                            + definition.Name + "' stored child gate " + gateHandle + " in bag "
                            + bagHandle + ", consumed " + slot.WireBitLength + " bits", LogLevel.Trace);
                        break;
                    }

                default:
                    {
                        string failure = "FieldExtractor.ExtractCollection: unhandled encoding "
                            + definition.Encoding + " for field '" + definition.Name + "'";
                        DebugLog.Write(LogChannel.Fields, failure, LogLevel.Error);
                        Environment.FailFast(failure);
                        break;
                    }
            }

            if (_traceCollection1 && (((uint)collection == 1u) || (uint)collection == 6u))
            {
                DebugLog.Write(LogChannel.Fields, "Inventory field '"
                    + definition.Name + "' value=" + slot.AsString(bag)
                    + " wireBitLength=" + slot.WireBitLength
                    + " wireByteLength=" + (slot.WireBitLength / 8u) + "\n\n", LogLevel.Info);
            }

            uint localEndBit = localStartBit + slot.WireBitLength;
            resolvedEndBits[(int)definitionIndex] = localEndBit;
            if (definition.Encoding != FieldEncoding.Gate)
            {
                SetCursor(collectionStartBit + localEndBit);
            }
        }

        AlignCursor();
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // EnumerateBags
    //
    // Yields the BagHandle of each bag in the current extraction, in creation order, for the
    // first count bags of the bag store.  Separated from Extract so the eager decode runs to
    // completion before any handle is yielded: Extract performs the full descent, then returns
    // this enumerable over the bags that descent produced.  A yield method cannot take the
    // payload span, so the span is consumed entirely in Extract and never reaches here.
    //
    // count:  The number of bags to yield, captured as _bags.Count at the end of the descent.
    //
    // Returns:  An enumerable yielding count BagHandles, values 0 through count - 1.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private IEnumerable<BagHandle> EnumerateBags(int count)
    {
        DebugLog.Write(LogChannel.Fields, "FieldExtractor.EnumerateBags: yielding " + count
            + " bag handle(s)");

        for (uint bagIndex = 0; bagIndex < (uint)count; bagIndex++)
        {
            yield return (BagHandle)bagIndex;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CreateBag
    //
    // Creates one bag for the given collection: computes the collection's slot and arena
    // capacities from the registry, rents a lease large enough to hold both regions, takes a
    // FieldBag from the free list or constructs one, initializes it over the lease, appends
    // it to the bag store, and returns the handle that addresses it.  The returned handle's
    // value is the bag's index in the store.
    //
    // collection:   The collection this bag decodes.
    //
    // Returns:  A BagHandle addressing the newly created bag.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private BagHandle CreateBag(CollectionHandle collection)
    {
        PatchLevel patchLevel = GlassContext.CurrentPatchLevel;
        ushort slotCapacity = GlassContext.PatchRegistry.GetSlotCount(collection);
        uint arenaCapacity = GlassContext.PatchRegistry.GetArenaCapacity(patchLevel, collection);
        uint required = FieldBag.BytesNeeded(slotCapacity, arenaCapacity);

        BufferLease lease = GlassContext.BufferPool.Rent(required);

        FieldBag bag;
        if (_freeBags.Count > 0)
        {
            bag = _freeBags.Pop();
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.CreateBag: reused bag from free list, "
                + (_freeBags.Count) + " remaining");
        }
        else
        {
            bag = new FieldBag();
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.CreateBag: free list empty, constructed new bag");
        }

        bag.Initialize(lease, collection, slotCapacity, arenaCapacity);

        BagHandle handle = (BagHandle)(uint)_bags.Count;
        _bags.Add(bag);

        DebugLog.Write(LogChannel.Fields, "FieldExtractor.CreateBag: bag " + handle
            + " for collection " + collection + ", slotCapacity " + slotCapacity
            + ", arenaCapacity " + arenaCapacity + ", lease length " + lease.Length);

        return handle;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CreateGate
    //
    // Creates a gate node for the given definition within the spawning bag, lifting the
    // multiplicity policy from the definition.  The gate's ParentBag is set to the spawning
    // bag, establishing the gate's upward step on the resolution spine.  The new gate is
    // appended to the extractor's gate list and its handle returned.
    //
    // definitionHandle:  The static gate definition to instantiate.
    // parent:            The bag this gate is created in, and which contains the definitionHandle.
    //                    Will be BagHandle.None at the root.
    //
    // Returns:  The handle of the newly-created gate.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private GateHandle CreateGate(GateDefinitionHandle definitionHandle, BagHandle parent)
    {
        PatchLevel patchLevel = GlassContext.CurrentPatchLevel;
        GateDefinition definition = GlassContext.PatchRegistry.GetGateDefinition(patchLevel, definitionHandle);
        GateHandle selfHandle = (GateHandle)(uint)_gates.Count;

        Gate gate = new Gate();
        gate.Definition = definitionHandle;
        gate.Kind = definition.Kind;
        gate.ChildCollection = definition.ChildCollection;
        gate.ParentBag = parent;
        gate.NextSibling = GateHandle.None;
        gate.Head = BagHandle.None;
        gate.Tail = BagHandle.None;
        gate.BagCount = 0;
        gate.Self = selfHandle;
        gate.BitsConsumed = 0u;
        _gates.Add(gate);

        DebugLog.Write(LogChannel.Fields, "FieldExtractor.CreateGate: gate '" + definition.Name
            + "' created as " + selfHandle + " kind=" + gate.Kind + " child=" + gate.ChildCollection
            + " parentBag=" + parent);
        return selfHandle;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AppendChildGate
    //
    // Appends a gate to the tail of a bag's child-gate chain.  When the bag has no child
    // gates the gate becomes both the bag's FirstChildGate and LastChildGate; otherwise the
    // current last child gate's NextSibling is set to the gate and the bag's LastChildGate
    // advances to it.  Threads the forward link that lives on the gate store and the head and
    // tail that live on the bag store, which is why the append is performed here, where both
    // stores are in scope, rather than on either type alone.
    //
    // bag:   The handle of the bag whose child-gate chain receives the gate.
    // gate:  The handle of the gate to append.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AppendChildGate(BagHandle bag, GateHandle gate)
    {
        FieldBag bagObject = _bags[(int)(uint)bag];

        if (bagObject.LastChildGate.Exists == false)
        {
            bagObject.FirstChildGate = gate;
            bagObject.LastChildGate = gate;

            DebugLog.Write(LogChannel.Fields, "FieldExtractor.AppendChildGate: bag " + bag
                + " first child gate " + gate);
        }
        else
        {
            GateHandle priorTail = bagObject.LastChildGate;
            Gate priorTailGate = _gates[(int)(uint)priorTail];
            priorTailGate.NextSibling = gate;
            _gates[(int)(uint)priorTail] = priorTailGate;

            bagObject.LastChildGate = gate;

            DebugLog.Write(LogChannel.Fields, "FieldExtractor.AppendChildGate: bag " + bag
                + " appended child gate " + gate + " after " + priorTail);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AppendInstanceBag
    //
    // Appends a bag to the tail of a gate's instance chain.  When the gate has no instances
    // the bag becomes both the gate's Head and Tail; otherwise the current tail bag's NextBag
    // is set to the bag and the gate's Tail advances to it.  Bumps the gate's BagCount in
    // either case.  Threads the forward link that lives on the bag store and the head, tail,
    // and count that live on the gate store, which is why the append is performed here, where
    // both stores are in scope, rather than on either type alone.
    //
    // gate:  The handle of the gate whose instance chain receives the bag.
    // bag:   The handle of the bag to append.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AppendInstanceBag(GateHandle gate, BagHandle bag)
    {
        Gate gateObject = _gates[(int)(uint)gate];

        if (gateObject.Head.Exists == false)
        {
            gateObject.Head = bag;
            gateObject.Tail = bag;

            DebugLog.Write(LogChannel.Fields, "FieldExtractor.AppendInstanceBag: gate " + gate
                + " first instance bag " + bag);
        }
        else
        {
            BagHandle priorTail = gateObject.Tail;
            _bags[(int)(uint)priorTail].NextBag = bag;

            gateObject.Tail = bag;

            DebugLog.Write(LogChannel.Fields, "FieldExtractor.AppendInstanceBag: gate " + gate
                + " appended instance bag " + bag + " after " + priorTail);
        }

        gateObject.BagCount = gateObject.BagCount + 1u;
        _gates[(int)(uint)gate] = gateObject;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RemoveInstanceBag
    //
    // Removes a bag from a gate's instance chain, the inverse of AppendInstanceBag.  When the
    // bag is the gate's sole instance both Head and Tail are reset to None; otherwise the chain
    // is walked from Head to the bag's predecessor, the predecessor's NextBag is unlinked past
    // the removed bag, and the gate's Tail is retreated to the predecessor when the removed bag
    // was the tail.  Decrements the gate's BagCount in either case.  Threads the forward link
    // that lives on the bag store and the head, tail, and count that live on the gate store,
    // which is why the removal is performed here, where both stores are in scope, rather than on
    // either type alone.
    //
    // The removed bag's storage is not touched: unlinking it from the chain is this method's
    // whole responsibility, and the caller releases the bag.
    //
    // gate:  The handle of the gate whose instance chain gives up the bag.
    // bag:   The handle of the bag to remove.  Must be present in the gate's instance chain.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void RemoveInstanceBag(GateHandle gate, BagHandle bag)
    {
        Gate gateObject = _gates[(int)(uint)gate];

        if (gateObject.Head.Equals(bag) == true && gateObject.Tail.Equals(bag) == true)
        {
            gateObject.Head = BagHandle.None;
            gateObject.Tail = BagHandle.None;

            DebugLog.Write(LogChannel.Fields, "FieldExtractor.RemoveInstanceBag: gate " + gate
                + " removed sole instance bag " + bag);
        }
        else
        {
            BagHandle walk = gateObject.Head;
            BagHandle predecessor = BagHandle.None;
            while (walk.Exists == true)
            {
                BagHandle next = _bags[(int)(uint)walk].NextBag;
                if (next.Equals(bag) == true)
                {
                    predecessor = walk;
                    break;
                }
                walk = next;
            }

            if (predecessor.Exists == false)
            {
                string failure = "FieldExtractor.RemoveInstanceBag: bag " + bag
                    + " not found in gate " + gate + " instance chain";
                DebugLog.WriteMultiline(LogChannel.Fields, failure + Environment.NewLine
                    + Environment.StackTrace);
                Environment.FailFast(failure);
            }

            _bags[(int)(uint)predecessor].NextBag = _bags[(int)(uint)bag].NextBag;

            if (gateObject.Tail.Equals(bag) == true)
            {
                gateObject.Tail = predecessor;
            }

            DebugLog.Write(LogChannel.Fields, "FieldExtractor.RemoveInstanceBag: gate " + gate
                + " removed instance bag " + bag + " after predecessor " + predecessor);
        }

        gateObject.BagCount = gateObject.BagCount - 1u;
        _gates[(int)(uint)gate] = gateObject;
        _bags[(int)(uint)bag].Invalidate();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResolveCount
    //
    // Resolves the instance count for a Times gate.  Called only on the Times arm of
    // ExpandMultiplicity.
    //
    // When the definition carries no FieldSlot and no CountFieldName the count is the constant
    // on the definition and is returned directly.
    //
    // For a local gate (FieldSlotLocal true): when FieldSlot exists it is read directly from
    // the spawning bag.  When FieldSlot is None the count field name is resolved dynamically
    // against the spawning bag's collection via IndexOfField, then read from the spawning bag.
    // A dynamic lookup that fails to find the field is an integrity failure and halts the
    // process via FailFast.
    //
    // For a non-local gate (FieldSlotLocal false): the spine is walked from the spawning bag
    // upward until a bag whose collection matches the slot's collection is found, and the count
    // is read from there.  A spine walk that exhausts without matching is an integrity failure
    // and halts the process via FailFast.
    //
    // startBag:    The bag being filled when the gate field was reached.
    // definition:  The gate definition carrying the count policy.
    //
    // Returns:  The resolved instance count.  Does not return on any resolution failure.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private uint ResolveCount(BagHandle startBag, GateDefinition definition)
    {
        if (definition.FieldSlot.Exists == false && string.IsNullOrEmpty(definition.CountFieldName) == true)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ResolveCount: Times gate '"
                + definition.Name + "' uses constant count " + definition.Count);
            return definition.Count;
        }

        if (definition.FieldSlotLocal == true)
        {
            SlotId slot = definition.FieldSlot;

            if (slot.Exists == false)
            {
                FieldBag spawningBag = _bags[(int)(uint)startBag];
                CollectionHandle spawningCollection = spawningBag.Collection;

                slot = GlassContext.PatchRegistry.IndexOfField(spawningCollection,
                    definition.CountFieldName);

                if (slot.Exists == false)
                {
                    string failure = "FieldExtractor.ResolveCount: Times gate '" + definition.Name
                        + "' dynamic lookup of count field '" + definition.CountFieldName
                        + "' failed in spawning collection " + spawningCollection;
                    DebugLog.WriteMultiline(LogChannel.Fields, failure + Environment.NewLine
                        + Environment.StackTrace);
                    Environment.FailFast(failure);
                }

                DebugLog.Write(LogChannel.Fields, "FieldExtractor.ResolveCount: Times gate '"
                    + definition.Name + "' dynamically resolved '" + definition.CountFieldName
                    + "' to slot " + slot + " in spawning collection " + spawningCollection);
            }

            uint localCount = _bags[(int)(uint)startBag].GetUIntAt(slot);
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ResolveCount: Times gate '"
                + definition.Name + "' local count from slot " + slot
                + " in bag " + startBag + " = " + localCount);
            return localCount;
        }

        // Non-local: walk the spine to find the ancestor bag whose collection matches.
        BagHandle walkBag = startBag;
        while (walkBag.Exists == true)
        {
            FieldBag bag = _bags[(int)(uint)walkBag];
            if (bag.Collection == definition.FieldSlot.Collection)
            {
                uint spineCount = bag.GetUIntAt(definition.FieldSlot);
                DebugLog.Write(LogChannel.Fields, "FieldExtractor.ResolveCount: Times gate '"
                    + definition.Name + "' non-local count from slot " + definition.FieldSlot
                    + " in ancestor bag " + walkBag + " = " + spineCount);
                return spineCount;
            }

            GateHandle parentGate = bag.ParentGate;
            if (parentGate.Exists == false)
            {
                break;
            }
            walkBag = _gates[(int)(uint)parentGate].ParentBag;
        }

        string nonLocalFailure = "FieldExtractor.ResolveCount: Times gate '" + definition.Name
            + "' non-local count field " + definition.FieldSlot
            + " names collection not found on the spine from bag " + startBag;
        DebugLog.WriteMultiline(LogChannel.Fields, nonLocalFailure + Environment.NewLine
            + Environment.StackTrace);
        Environment.FailFast(nonLocalFailure);
        return 0u;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResolveFieldStartBit
    //
    // Computes the collection-local bit position where the field at definitionIndex begins.
    // An absolute field starts at its schema BitOffset.  A relative field starts at its
    // anchor field's recorded end bit plus its schema BitOffset; the anchor's end is read
    // from resolvedEndBits, where it was stored after the anchor was decoded.
    //
    // The returned position is collection-local; the caller relocates it past the bit cursor
    // to an absolute payload position before reading.  This method does not record the
    // field's own end bit — that is known only after the field is decoded and is stored by
    // the caller.
    //
    // definitions:      The field definitions for the collection.
    // resolvedEndBits:  Per-instance end bits of already-decoded fields, indexed by
    //                   definition index.  Read for the anchor of a relative field.
    // definitionIndex:  Index of the field whose start bit to resolve.
    //
    // Returns:  The collection-local start bit of the field.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static uint ResolveFieldStartBit(
        FieldDefinition[] definitions,
        Span<uint> resolvedEndBits,
        uint definitionIndex)
    {
        FieldDefinition definition = definitions[definitionIndex];
        uint startBit;

        if (definition.RelativeToSlot == null)
        {
            startBit = definition.BitOffset;
        }
        else
        {
            uint anchorIndex = definition.RelativeToSlot.Value;
            uint anchorEndBit = resolvedEndBits[(int)anchorIndex];
            startBit = anchorEndBit + definition.BitOffset;
        }

        return startBit;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HasBits
    //
    // Verifies that the payload contains at least bitLength bits starting at bitOffset.
    // Called from ExtractCollection before each per-encoding read to guard against truncated payloads
    // or field definitions that don't match the actual packet shape.
    //
    // The check converts the bit range to a byte range covering it (which may span a
    // partial byte at each end) and compares against the payload's byte length.
    //
    // On failure this method is silent; the caller leaves the slot in its default Empty
    // state, which consumers detect via the slot's type tag.  Per-field bounds failures
    // are an expected condition during Inference (every handler is tried against every
    // packet) and logging them would flood the log with non-anomalies.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset at which the read would start.  Taken from the field
    //                definition unchanged.
    //   bitLength  - The number of bits the read needs.  Taken from the field definition
    //                unchanged.
    //
    // Returns:
    //   true  - The payload has enough bits; the caller may proceed with the read.
    //   false - The payload is too short; the caller must skip the decode and leave the
    //           slot in its default state.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool HasBits(ReadOnlySpan<byte> payload, uint bitOffset, uint bitLength)
    {
        uint lastBitExclusive = bitOffset + bitLength;
        uint requiredBytes = (lastBitExclusive + 7u) / 8u;
        if (requiredBytes > (uint)payload.Length)
        {
            return false;
        }
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // TryReadBitsLE
    //
    // Reads bitLength bits from the payload starting at bitOffset, treating the payload
    // bytes as a little-endian bit stream — that is, byte 0 holds the lowest-addressed
    // bits, with bit 0 of each byte being the lowest bit of that byte's contribution.
    //
    // On success, the extracted bits are written to value, right-justified in a ulong with
    // all higher bits zero.  Callers interpret those bits according to the field's encoding
    // (signed, unsigned, fixed-point, IEEE float, etc.).  On failure, value is set to 0 and
    // the caller must treat the result as unread; the slot stays Empty.
    //
    // The caller must have already verified the read fits in the payload via HasBits.  This
    // method does not bounds-check the payload; out-of-range reads will throw at the
    // underlying payload access.
    //
    // Width support: this implementation reads at most 8 source bytes.  That allows up to
    // 64 bits at a byte-aligned offset, or up to 56 bits at a non-byte-aligned offset (the
    // worst case being bit offset 7 plus 56 bits = bit 63, the last bit of byte 7).  Reads
    // that would exceed those limits return false rather than silently truncating.  All
    // current field definitions are 32 bits or smaller, well within both limits.  If a
    // wider non-aligned read is ever needed, this method must be extended to read a 9th
    // source byte.
    //
    // Parameters:
    //   payload    - The packet payload.  Treated as a contiguous bit stream.
    //   bitOffset  - The starting bit offset within the payload.  May be non-byte-aligned.
    //   bitLength  - The number of bits to read.  Must be in [1, 64] for byte-aligned reads
    //                or [1, 56] for non-byte-aligned reads.
    //   value      - On success, receives the extracted bits right-justified in a ulong.
    //                On failure, set to 0 and must not be used.
    //
    // Returns:
    //   true   - The read succeeded; value holds the extracted bits.
    //   false  - bitLength is out of range (zero, or wider than this method supports at the
    //            given bit offset).  value is set to 0.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool TryReadBitsLE(ReadOnlySpan<byte> payload, uint bitOffset, uint bitLength,
        out ulong value)
    {
        value = 0ul;

        if (bitLength == 0u)
        {
            return false;
        }

        if (bitLength > 64u)
        {
            return false;
        }

        uint bitShift = bitOffset % 8u;
        if (bitShift > 0u && bitLength > 56u)
        {
            return false;
        }

        uint byteOffset = bitOffset / 8u;
        uint lastBitExclusive = bitShift + bitLength;
        uint byteCount = (lastBitExclusive + 7u) / 8u;

        ulong assembled = 0ul;
        for (uint byteIndex = 0u; byteIndex < byteCount; byteIndex++)
        {
            ulong sourceByte = payload[(int)(byteOffset + byteIndex)];
            assembled = assembled | (sourceByte << (int)(byteIndex * 8u));
        }

        ulong shifted = assembled >> (int)bitShift;

        ulong mask;
        if (bitLength == 64u)
        {
            mask = ulong.MaxValue;
        }
        else
        {
            mask = (1ul << (int)bitLength) - 1ul;
        }

        value = shifted & mask;
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SignExtend
    //
    // Treats the low bitLength bits of raw as a signed integer in two's-complement form and
    // returns the value sign-extended to a 32-bit signed int.  If the top bit of the bit
    // range (bit at position bitLength - 1) is set, the result is negative; otherwise
    // positive.
    //
    // Used by the signed integer encodings (Int16LE, Int32LE) to convert the raw bits
    // returned by TryReadBitsLE — which are right-justified and zero-extended — into a
    // properly-signed C# int.
    //
    // Parameters:
    //   raw        - The bits to interpret, right-justified in a ulong with all higher bits
    //                zero (the form returned by TryReadBitsLE on success).
    //   bitLength  - The width of the signed value in bits.  Must be in [1, 32]; widths
    //                outside that range fall through with the raw low bits cast to int,
    //                which is acceptable because ExtractCollection has already validated the field
    //                via TryReadBitsLE before calling this.
    //
    // Returns:
    //   The sign-extended 32-bit signed integer.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private int SignExtend(ulong raw, uint bitLength)
    {
        if (bitLength == 0u || bitLength >= 32u)
        {
            return (int)raw;
        }

        ulong signBit = 1ul << (int)(bitLength - 1u);
        if ((raw & signBit) == 0ul)
        {
            return (int)raw;
        }

        ulong fillMask = ~((1ul << (int)bitLength) - 1ul);
        ulong extended = raw | fillMask;
        return (int)extended;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractFloatLE
    //
    // Reads a 32-bit IEEE single-precision float in little-endian bit order from the payload
    // at the given bit offset and stores it in the slot.  The bits are read via
    // TryReadBitsLE and reinterpreted as a float without any numeric conversion — the bit
    // pattern is the float.
    //
    // On any read failure the slot is left in its default Empty state.  HasBits has already
    // confirmed the payload has enough bytes; failures here would mean the bit length is
    // out of range for TryReadBitsLE, which for a properly defined float field (BitLength
    // of 32) cannot happen.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.  Caller-computed: definition.BitOffset
    //                for required fields, the running offset for optional fields.
    //   bitLength  - The number of bits to read.  Should be 32 for a standard IEEE single;
    //                other widths are read but the result is undefined.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractFloatLE(ReadOnlySpan<byte> payload, uint bitOffset, uint bitLength,
        ref FieldSlot slot)
    {
        ulong raw;
        if (TryReadBitsLE(payload, bitOffset, bitLength, out raw) == false)
        {
            return;
        }

        int bits = (int)raw;
        float value = BitConverter.Int32BitsToSingle(bits);
        slot.SetFloat(value);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractNullTerminatedString
    //
    // Decodes a null-terminated ASCII string beginning at the given bit offset, which must
    // be byte-aligned.  The wire bytes up to but not including the terminator are stored
    // in the slot.  If the offset lies past the payload, or no terminator exists in the
    // remaining payload, the slot is left unset.
    //
    // payload:    The application payload being decoded.
    // bitOffset:  Bit offset into the payload where the string begins.
    // bag:        The bag that owns the slot; receives the string bytes into its arena.
    // slot:       The slot to receive the decoded string.
    //
    // Returns:    Number of bytes consumed including the terminator, or 0 if no string
    //             was decoded.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private uint ExtractNullTerminatedString(ReadOnlySpan<byte> payload, uint bitOffset,
        FieldBag bag, ref FieldSlot slot)
    {
        uint byteOffset = bitOffset / 8u;
        if (byteOffset >= (uint)payload.Length)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractNullTerminatedString: field '"
                + slot.GetName(bag) + "' byteOffset " + byteOffset + " is at or past payload length "
                + payload.Length + ", slot left empty");
            return 0;
        }
        ReadOnlySpan<byte> tail = payload.Slice((int)byteOffset);
        int nullIndex = tail.IndexOf((byte)0x00);
        if (nullIndex < 0)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractNullTerminatedString: field '"
                + slot.GetName(bag) + "' at byteOffset " + byteOffset
                + " has no null terminator within remaining " + tail.Length
                + " bytes, slot left empty");
            return 0;
        }
        ReadOnlySpan<byte> stringBytes = tail.Slice(0, nullIndex);
        slot.SetAsciiString(bag, stringBytes);
        if (stringBytes.Length > 0)
        {
            int logLength = stringBytes.Length > 100 ? 100 : stringBytes.Length;
            string logString = System.Text.Encoding.ASCII.GetString(stringBytes.Slice(0, logLength));
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractNullTerminatedString: field '"
                + slot.GetName(bag) + "' at byteOffset " + byteOffset
                + " value='" + logString + "' length=" + stringBytes.Length, LogLevel.Info);
        }

        return (uint)stringBytes.Length + 1;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractLengthPrefixedString
    //
    // Reads a uint32 little-endian length prefix at the given byte offset (derived from
    // the bit offset), then reads that many bytes immediately following as an ASCII
    // string.  BitLength is not used by this encoding; the length prefix determines the
    // string length.
    //
    // On any failure the slot is left in its default Empty state.  Failures here are
    // routine during Inference (every handler reads every packet, length prefixes
    // interpreted at random offsets often produce nonsense values), so all paths are
    // silent.  Handlers that care about a specific failure can detect it by checking
    // the slot's type tag after extraction.
    //
    // A length prefix of zero is a successful decode producing a zero-length string;
    // the slot's type becomes AsciiString and its payload length is zero.  This is
    // distinguishable from a failure (which leaves the slot Empty).
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.  Caller-computed: definition.BitOffset
    //                for required fields, the running offset for optional fields.  Divided
    //                by 8 to produce the byte offset; non-byte-aligned length-prefixed
    //                strings are not supported and would mis-locate the prefix.
    //   bag:       - The bag that owns the slot; receives the string bytes into its arena.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    //
    // Returns:
    //   The wire length of the extracted string (in bytes) including the prefix.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private uint ExtractLengthPrefixedString(ReadOnlySpan<byte> payload, uint bitOffset,
        FieldBag bag, ref FieldSlot slot)
    {
        uint byteOffset = bitOffset / 8u;
        if (byteOffset + 4u > (uint)payload.Length)
        {
            DebugLog.Write(LogChannel.Fields,
                $"ExtractLengthPrefixedString: length prefix at byte {byteOffset} " +
                $"exceeds payload length {(uint)payload.Length}, slot not set");
            return 0;
        }
        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice((int)byteOffset));
        uint stringStart = byteOffset + 4u;
        uint available = (uint)payload.Length - stringStart;
        if (declaredLength > available)
        {
            DebugLog.Write(LogChannel.Fields,
                $"ExtractLengthPrefixedString: declared length {declaredLength} at byte {byteOffset} " +
                $"exceeds available {available}, slot not set");
            return 0;
        }

        if (declaredLength > 63)
        {
            declaredLength = 63;
            DebugLog.Write(LogChannel.Fields, "truncating long string:");
            DebugLog.Write(LogChannel.Fields, 
                SoeHexDump.Format(payload.Slice((int)stringStart, (int)declaredLength)));
        }
        ReadOnlySpan<byte> stringBytes = payload.Slice((int)stringStart, (int)declaredLength);
        slot.SetAsciiString(bag, stringBytes);
        return (uint)stringBytes.Length + 4;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractUIntMsb
    //
    // Reads an unsigned integer value packed MSB-first within each byte.  No sign bit;
    // all bits contribute to the unsigned magnitude.
    //
    // On any read failure the slot is left in its default Empty state.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.
    //   bitLength  - The total number of bits.  Must be at least 1 and at most 32.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractUIntMsb(ReadOnlySpan<byte> payload, uint bitOffset,
        uint bitLength, ref FieldSlot slot)
    {
        if (bitLength < 1u || bitLength > 32u)
        {
            return;
        }

        uint bitPosition = bitOffset;
        uint bitsLeft = bitLength;
        uint result = 0u;

        while (bitsLeft > 0u)
        {
            uint byteIndex = bitPosition / 8u;
            uint bitInByte = bitPosition % 8u;
            uint bitsInThisByte = 8u - bitInByte;

            uint bitsToTake;
            if (bitsLeft < bitsInThisByte)
            {
                bitsToTake = bitsLeft;
            }
            else
            {
                bitsToTake = bitsInThisByte;
            }

            uint shift = bitsInThisByte - bitsToTake;
            uint mask = (1u << (int)bitsToTake) - 1u;
            uint chunk = ((uint)payload[(int)byteIndex] >> (int)shift) & mask;

            result = (result << (int)bitsToTake) | chunk;
            bitPosition = bitPosition + bitsToTake;
            bitsLeft = bitsLeft - bitsToTake;
        }

        slot.SetUInt(result);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractSignMagnitudeMsb
    //
    // Reads a signed coordinate value packed MSB-first within each byte, sign-magnitude
    // encoded (first bit is the sign, 1 = negative; remaining bits are the unsigned
    // magnitude), then divides by Divisor.
    //
    // This is the bit-reading convention of the EQ client shared with BitReader.
    // It is distinct from the LSB-first /
    // two's-complement encoding used by ExtractSignExtendedDiv8 — they must not be
    // confused; values decoded with the wrong convention will be wrong, sometimes
    // wildly so.
    //
    // On any read failure the slot is left in its default Empty state.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.
    //   bitLength  - The total number of bits including the sign bit.  Must be at least 2
    //                (one sign bit + one magnitude bit) and at most 32.
    //   divisor    - Divisor used to create a floating point value.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractSignMagnitudeMsb(ReadOnlySpan<byte> payload, uint bitOffset,
        uint bitLength, float divisor, ref FieldSlot slot)
    {
        if (bitLength < 2u || bitLength > 32u)
        {
            return;
        }

        uint bitPosition = bitOffset;
        uint bitsLeft = bitLength;
        uint result = 0u;

        while (bitsLeft > 0u)
        {
            uint byteIndex = bitPosition / 8u;
            uint bitInByte = bitPosition % 8u;
            uint bitsInThisByte = 8u - bitInByte;

            uint bitsToTake;
            if (bitsLeft < bitsInThisByte)
            {
                bitsToTake = bitsLeft;
            }
            else
            {
                bitsToTake = bitsInThisByte;
            }

            uint shift = bitsInThisByte - bitsToTake;
            uint mask = (1u << (int)bitsToTake) - 1u;
            uint chunk = ((uint)payload[(int)byteIndex] >> (int)shift) & mask;

            result = (result << (int)bitsToTake) | chunk;
            bitPosition = bitPosition + bitsToTake;
            bitsLeft = bitsLeft - bitsToTake;
        }

        uint signBit = result >> (int)(bitLength - 1u);
        uint magnitudeMask = (1u << (int)(bitLength - 1u)) - 1u;
        uint magnitude = result & magnitudeMask;

        int signedValue;
        if (signBit != 0u)
        {
            signedValue = -(int)magnitude;
        }
        else
        {
            signedValue = (int)magnitude;
        }

        float fValue = signedValue / divisor;
        slot.SetFloat(fValue);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractSignMagnitudeLsb
    //
    // Reads a sign-extended fixed-point coordinate value at the given bit offset and bit
    // length, then divides by 8.0 to convert to a floating-point world coordinate.  Used
    // by MobUpdate / NpcMoveUpdate position fields, which pack each coordinate as a
    // signed integer (commonly 19 bits) in units of 1/8 world unit.
    //
    // On any read failure the slot is left in its default Empty state.
    //
    // Parameters:
    //   payload    - The packet payload being decoded.
    //   bitOffset  - The bit offset to read from.  Caller-computed: definition.BitOffset
    //                for required fields, the running offset for optional fields.
    //   bitLength  - The number of bits in the packed signed integer; the divide by 8 is
    //                implicit in the encoding.
    //   slot       - The slot to fill.  Already has its name set.  Stays Empty on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ExtractSignMagnitudeLsb(ReadOnlySpan<byte> payload, uint bitOffset,
        uint bitLength, float divisor, ref FieldSlot slot)
    {
        ulong raw;
        if (TryReadBitsLE(payload, bitOffset, bitLength, out raw) == false)
        {
            return;
        }

        int signedValue = SignExtend(raw, bitLength);
        float fValue = signedValue / divisor;
        slot.SetFloat(fValue);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ExtractBlob
    //
    // Reads byteCount raw bytes from the payload at the given bit offset, which must be
    // byte-aligned, and stores them in the slot via SetBlob.  On any failure the slot is
    // left in its default Empty state.
    //
    // payload:     The packet payload being decoded.
    // bitOffset:   Bit offset into the payload where the blob begins.  Must be byte-aligned.
    // byteCount:   Number of bytes to read.
    // bag:         The bag that owns the slot; receives the blob bytes into its arena.
    // slot:        The slot to fill.
    //
    // Returns:     The number of bytes consumed, or 0 on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private uint ExtractBlob(ReadOnlySpan<byte> payload, uint bitOffset, uint byteCount,
        FieldBag bag, ref FieldSlot slot)
    {
        uint byteOffset = bitOffset / 8u;

        DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractBlob: byteOffset=" + byteOffset
            + " byteCount=" + byteCount, LogLevel.Trace);

        if (byteOffset + byteCount > (uint)payload.Length)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractBlob: ran off payload at byteOffset="
                + byteOffset + " byteCount=" + byteCount + " payloadLength=" + payload.Length,
                LogLevel.Warn);
            return 0u;
        }

        slot.SetBlob(bag, payload.Slice((int)byteOffset, (int)byteCount));

        DebugLog.Write(LogChannel.Fields, "FieldExtractor.ExtractBlob: stored " + byteCount
            + " bytes", LogLevel.Trace);

        return byteCount;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // EvaluatePredicate
    //
    // Evaluates the predicate against its source slot in the given bag.  The source slot
    // must hold an integer type; any other type means the gating field is absent or
    // mistyped, which is a schema or extraction integrity violation and halts the process
    // via FailFast with the failure details preserved in the Fields log channel.
    //
    // bag:        The bag containing the predicate's source slot.
    // predicate:  The predicate to evaluate.
    //
    // Returns:    The predicate result.  Does not return if the source slot is not an
    //             integer type.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool EvaluatePredicate(FieldBag bag, FieldPredicate predicate)
    {
        FieldType sourceType = bag.GetTypeAt(predicate.SourceSlot);
        if (sourceType == FieldType.UInt)
        {
            uint sourceValue = bag.GetUIntAt(predicate.SourceSlot);
            return EvaluateUnsigned(sourceValue, predicate);
        }
        if (sourceType == FieldType.Int)
        {
            int sourceValue = bag.GetIntAt(predicate.SourceSlot);
            return EvaluateSigned(sourceValue, predicate);
        }
        string message = "FieldExtractor.EvaluatePredicate: source slot " + predicate.SourceSlot
            + " has type " + sourceType + ", which is not an integer the predicate can test; "
            + "the gating field is absent or mistyped, packet or schema is broken -- aborting";
        DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
            + Environment.StackTrace);
        Environment.FailFast(message);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // EvaluateUnsigned
    //
    // Applies the predicate's operator to an unsigned source value, comparing against the
    // unsigned operand.  BitmaskNonZero tests the raw bits; the ordered operators compare
    // unsigned magnitude.
    //
    // Parameters:
    //   sourceValue  - The source field's value, read as unsigned.
    //   predicate    - The resolved predicate carrying the operator and unsigned operand.
    //
    // Returns:
    //   true when the comparison holds, false otherwise.  Fail-fasts on an unhandled operator.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool EvaluateUnsigned(uint sourceValue, FieldPredicate predicate)
    {
        bool result;
        switch (predicate.Op)
        {
            case PredicateOp.BitmaskNonZero:
                result = (sourceValue & predicate.Operand) != 0u;
                break;

            case PredicateOp.Equal:
                result = sourceValue == predicate.Operand;
                break;

            case PredicateOp.NotEqual:
                result = sourceValue != predicate.Operand;
                break;

            case PredicateOp.Greater:
                result = sourceValue > predicate.Operand;
                break;

            case PredicateOp.GreaterOrEqual:
                result = sourceValue >= predicate.Operand;
                break;

            case PredicateOp.Less:
                result = sourceValue < predicate.Operand;
                break;

            case PredicateOp.LessOrEqual:
                result = sourceValue <= predicate.Operand;
                break;

            default:
                {
                    string message = "FieldExtractor.EvaluateUnsigned: unhandled predicate op "
                        + predicate.Op + " -- broken predicate definition, aborting";

                    DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                        + Environment.StackTrace);

                    Environment.FailFast(message);
                    result = false;
                    break;
                }
        }

        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // EvaluateSigned
    //
    // Applies the predicate's operator to a signed source value, comparing against the signed
    // operand.  BitmaskNonZero tests the raw bits, reinterpreted as unsigned for the mask, so
    // a sign-extended source does not turn a narrow mask into a match on the extension bits;
    // the ordered operators compare signed magnitude.
    //
    // Parameters:
    //   sourceValue  - The source field's value, read as signed.
    //   predicate    - The resolved predicate carrying the operator and both operand views.
    //
    // Returns:
    //   true when the comparison holds, false otherwise.  Fail-fasts on an unhandled operator.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool EvaluateSigned(int sourceValue, FieldPredicate predicate)
    {
        bool result;
        switch (predicate.Op)
        {
            case PredicateOp.BitmaskNonZero:
                {
                    string message = "FieldExtractor.EvaluateSigned: BitmaskNonZero applied to a "
                        + "signed source field; a bitmask test requires an unsigned field -- "
                        + "broken schema definition, aborting";

                    DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                        + Environment.StackTrace);

                    Environment.FailFast(message);
                    result = false;
                    break;
                }

            case PredicateOp.Equal:
                result = sourceValue == predicate.SignedOperand;
                break;

            case PredicateOp.NotEqual:
                result = sourceValue != predicate.SignedOperand;
                break;

            case PredicateOp.Greater:
                result = sourceValue > predicate.SignedOperand;
                break;

            case PredicateOp.GreaterOrEqual:
                result = sourceValue >= predicate.SignedOperand;
                break;

            case PredicateOp.Less:
                result = sourceValue < predicate.SignedOperand;
                break;

            case PredicateOp.LessOrEqual:
                result = sourceValue <= predicate.SignedOperand;
                break;

            default:
                {
                    string message = "FieldExtractor.EvaluateSigned: unhandled predicate op "
                        + predicate.Op + " -- broken predicate definition, aborting";

                    DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                        + Environment.StackTrace);

                    Environment.FailFast(message);
                    result = false;
                    break;
                }
        }

        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CollectionOf
    //
    // Returns the collection handle of the active bag, letting a consumer discriminate which
    // collection the active bag decodes and read its slots accordingly.  The active bag is the
    // one selected by the most recent EnterGate, or the root bag set when the extraction
    // completed.
    //
    // Returns:  The collection handle the active bag decodes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public CollectionHandle CollectionOf()
    {
        return _bags[(int)(uint)_activeBag].Collection;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetIntAt
    //
    // Reads the slot as a 32-bit signed integer from the active bag.  Forwards to the bag's
    // own accessor, which FailFasts on a read failure.  The active bag is the one selected by
    // the most recent EnterGate, or the root bag set when the extraction completed.
    //
    // slot:  The slot to read.
    //
    // Returns:  The slot's signed integer value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int GetIntAt(SlotId slot)
    {
        return _bags[(int)(uint)_activeBag].GetIntAt(slot);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetUIntAt
    //
    // Reads the slot as a 32-bit unsigned integer from the active bag.  Forwards to the bag's
    // own accessor, which FailFasts on a read failure.  The active bag is the one selected by
    // the most recent EnterGate, or the root bag set when the extraction completed.
    //
    // slot:  The slot to read.
    //
    // Returns:  The slot's unsigned integer value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint GetUIntAt(SlotId slot)
    {
        return _bags[(int)(uint)_activeBag].GetUIntAt(slot);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetFloatAt
    //
    // Reads the slot as a 32-bit float from the active bag.  Forwards to the bag's own
    // accessor, which FailFasts on a read failure.  The active bag is the one selected by the
    // most recent EnterGate, or the root bag set when the extraction completed.
    //
    // slot:  The slot to read.
    //
    // Returns:  The slot's float value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public float GetFloatAt(SlotId slot)
    {
        return _bags[(int)(uint)_activeBag].GetFloatAt(slot);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetBytesAt
    //
    // Reads the slot as a span over its ASCII string bytes from the active bag.  Forwards to
    // the bag's own accessor, which FailFasts on a read failure.  The active bag is the one
    // selected by the most recent EnterGate, or the root bag set when the extraction
    // completed.
    //
    // The returned span is valid only until the bag is cleared or released; after that the
    // arena bytes may belong to a new tenant.
    //
    // slot:  The slot to read.
    //
    // Returns:  The span over the slot's string bytes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ReadOnlySpan<byte> GetBytesAt(SlotId slot)
    {
        return _bags[(int)(uint)_activeBag].GetBytesAt(slot);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetStringAt
    //
    // Reads the slot as an ASCII string from the active bag, decoding the slot's arena bytes
    // into a newly-allocated string.  Forwards to the bag's GetBytesAt accessor for the
    // bytes, which FailFasts on a read failure, then decodes them as ASCII.  The active bag
    // is the one selected by the most recent EnterGate, or the root bag set when the
    // extraction completed.
    //
    // The returned string is an independent copy with no reference back to the arena, so it
    // stays valid after the bag is cleared or released — unlike the span GetBytesAt returns.
    // Callers in a hot path that must avoid the allocation read GetBytesAt instead.
    //
    // slot:  The slot to read.
    //
    // Returns:  The slot's decoded ASCII string.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string GetStringAt(SlotId slot)
    {
        ReadOnlySpan<byte> bytes = _bags[(int)(uint)_activeBag].GetBytesAt(slot);
        string value = System.Text.Encoding.ASCII.GetString(bytes);

        return value;
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetGateAt
    //
    // Reads the gate-encoded slot from the active bag and returns the handle of the child gate
    // it decoded through.  Forwards to the bag's own accessor, which FailFasts on a read
    // failure.  The active bag is the one selected by the most recent EnterGate, or the root
    // bag set when the extraction completed.  The returned handle selects an instance of the
    // child gate through EnterGate.
    //
    // slot:  The gate-encoded slot to read.
    //
    // Returns:  The handle of the child gate.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public GateHandle GetGateAt(SlotId slot)
    {
        return _bags[(int)(uint)_activeBag].GetGateAt(slot);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // EnterGate
    //
    // Selects an instance bag of the given gate as the active bag, the bag the handle-keyed
    // reads resolve a SlotId against.  Walks the gate's instance chain from Head through each
    // bag's NextBag into the child index in instance order, then sets the active bag to the
    // entry at bagIndex.  The walk runs on every call: each call may select a bag in an
    // unrelated part of the tree, so no index is carried between calls.
    //
    // A walked count that disagrees with the gate's BagCount, or a bagIndex outside the walked
    // range, is an integrity failure and halts the process via FailFast.
    //
    // gate:      The handle of the gate whose instance bag to select.
    // bagIndex:  The instance index within the gate, in [0, BagCount).
    //
    // Returns:  The gate's instance bag count.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint EnterGate(GateHandle gate, uint bagIndex)
    {
        Gate gateObject = _gates[(int)(uint)gate];
        uint count = gateObject.BagCount;

        if ((uint)_childIndex.Length < count)
        {
            DebugLog.Write(LogChannel.Fields, "FieldExtractor.EnterGate: growing child index from "
                + _childIndex.Length + " to " + count);
            Array.Resize(ref _childIndex, (int)count);
        }

        uint filled = 0u;
        BagHandle walk = gateObject.Head;
        while (walk.Exists == true)
        {
            _childIndex[(int)filled] = walk;
            filled = filled + 1u;
            walk = _bags[(int)(uint)walk].NextBag;
        }

        if (filled != count)
        {
            string failure = "FieldExtractor.EnterGate: gate " + gate + " walked " + filled
                + " instance bags but BagCount is " + count + "; instance chain and count disagree";
            DebugLog.WriteMultiline(LogChannel.Fields, failure + Environment.NewLine
                + Environment.StackTrace);
            Environment.FailFast(failure);
        }

        if (bagIndex >= count)
        {
            string failure = "FieldExtractor.EnterGate: gate " + gate + " bagIndex " + bagIndex
                + " out of range [0, " + count + ")";
            DebugLog.WriteMultiline(LogChannel.Fields, failure + Environment.NewLine
                + Environment.StackTrace);
            Environment.FailFast(failure);
        }

        _activeBag = _childIndex[(int)bagIndex];

        DebugLog.Write(LogChannel.Fields, "FieldExtractor.EnterGate: gate " + gate
            + " selected instance " + bagIndex + " of " + count + ", active bag " + _activeBag);

        return count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IsPresent
    //
    // Returns whether the slot holds a value in the active bag, letting a handler distinguish
    // an absent field from one present with a zero value.  Forwards to the bag's own accessor.
    // The active bag is the one selected by the most recent EnterGate, or the root bag set when
    // the extraction completed.
    //
    // slot:  The slot to test.
    //
    // Returns:  true if the slot holds a value; false if it is Empty or the index is out of
    //           range.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool IsPresent(SlotId slot)
    {
        return _bags[(int)(uint)_activeBag].IsPresent(slot);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetByteRangeFor
    //
    // Returns the ByteRange for the slot indicated by SlotId.  This is relative to the start
    // of the packet payload.
    //
    // slot:  The slot to query.
    //
    // Returns:  The byte range covering the slot's content.  Does not return on failure.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ByteRange GetByteRangeFor(SlotId slot)
    {
        return _bags[(int)(uint)_activeBag].GetByteRangeFor(slot);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WalkBag
    //
    // Returns a walker over the filled slots of the active bag, letting a consumer enumerate
    // the bag's fields in sequence order.  The active bag is the one selected by the most
    // recent EnterGate, or the root bag set when the extraction completed.
    //
    // Returns:  A walker positioned at the start of the active bag.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public BagWalker WalkBag()
    {
        return _bags[(int)(uint)_activeBag].Walk();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BagCount
    //
    // Returns the number of instance bags the given gate decoded, letting a consumer bound an
    // instance loop before selecting a bag with EnterGate.  Reads the count the gate carries,
    // maintained as each instance bag was appended; no instance chain is walked.
    //
    // gate:  The handle of the gate to query.
    //
    // Returns:  The gate's instance bag count.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint BagCount(GateHandle gate)
    {
        if (gate.Exists == false)
        {
            return 0;
        }
        return _gates[(int)(uint)gate].BagCount;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Release
    //
    // Ends the current extraction: releases each active bag's lease back to the pool, returns
    // each bag object to the free list for reuse, and empties the bag and gate stores.  The
    // consumer calls this when done reading an extraction's results; handles from that
    // extraction must not be used afterward.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Release()
    {
        DebugLog.Write(LogChannel.Fields, "FieldExtractor.Release: releasing " + _bags.Count
            + " bags, " + _gates.Count + " gates; free list before " + _freeBags.Count);

        for (int bagIndex = 0; bagIndex < _bags.Count; bagIndex++)
        {
            FieldBag bag = _bags[bagIndex];
            bag.Release();
            _freeBags.Push(bag);
        }

        _bags.Clear();
        _gates.Clear();
        _activeBag = BagHandle.None;

        DebugLog.Write(LogChannel.Fields, "FieldExtractor.Release: free list after " + _freeBags.Count);
    }
}