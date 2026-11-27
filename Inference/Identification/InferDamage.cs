using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Repositories;
using Glass.Network.Client;
using Glass.Network.Protocol;
using Glass.World;
using Inference.Core;
using Inference.Models;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferDamage
//
// Identifies OP_Animation and OP_Damage together.  A damage message of DamageLength bytes,
// carrying the attacker's spawn id at DamageAttackerOffset, is preceded on the same session by
// an animation message of AnimationLength bytes that begins with the attacker's spawn id.  A
// zone-to-client message of AnimationLength bytes, of a wire value not
// yet accepted, whose leading 16-bit value is a spawn id placed in the session's zone, is a
// candidate animation.  The next zone-to-client message on that session, of a wire value not
// yet accepted, is tested: it votes for the pair of the two wire values, and the vote is a hit
// when it is DamageLength bytes long and its 16-bit value at DamageAttackerOffset equals the
// candidate's spawn id.  A pair's share is its hits divided by the number of tests made after
// candidates of its animation wire value.
///////////////////////////////////////////////////////////////////////////////////////////////
public class InferDamage : IInferOpcodes
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private const string GroupLabelConstant = "Damage";
    private const string AnimationOpcodeName = "OP_Animation";
    private const string DamageOpcodeName = "OP_Damage";

    private const int AnimationLength = 4;               // payload length of an animation
    private const int AnimationSpawnIdOffset = 0;        // offset of the 16-bit attacker spawn id in an animation
    private const int DamageLength = 48;                 // payload length of a damage message
    private const int DamageAttackerOffset = 2;          // offset of the 16-bit attacker spawn id in a damage message
    private const int MinHits = 2;                       // hits a pair needs before it can be chosen
    private const double ReportThreshold = 0.40;         // minimum share to emit a proposal

    ///////////////////////////////////////////////////////////////////////////////////////////
    // WirePair
    //
    // An animation wire value and the wire value of the message tested after it.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly record struct WirePair(OpcodeValue Animation, OpcodeValue Damage);

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PendingAnimation
    //
    // A candidate animation waiting for the next message on its session.  PacketIndex is its
    // message number, WireValue its wire value and SpawnId the spawn id it begins with.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class PendingAnimation
    {
        public uint PacketIndex;
        public OpcodeValue WireValue;
        public ushort SpawnId;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Infer
    //
    // Tallies the tests and hits, then chooses the pair with the most hits among those with at
    // least MinHits.  When two or more pairs share the most hits, none is chosen.  The chosen
    // pair's share is its hits divided by the tests made after candidates of its animation wire
    // value, and the pair is proposed when the share reaches ReportThreshold.  The counts at each
    // stage are added to the result's messages.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The result for OP_Animation and OP_Damage.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IdentificationResult Infer(PacketCatalog catalog)
    {
        DebugLog.Write(LogChannel.InferenceDebug, "InferDamage.Infer: starting", defaultLoglevel);

        IdentificationResult result = new IdentificationResult();
        result.OpcodeNames.Add(AnimationOpcodeName);
        result.OpcodeNames.Add(DamageOpcodeName);
        result.GroupLabel = GroupLabelConstant;

        Dictionary<OpcodeValue, int> tests = new Dictionary<OpcodeValue, int>();
        Dictionary<WirePair, int> hits = new Dictionary<WirePair, int>();
        int candidates = TallyVotes(catalog, tests, hits);

        result.Messages.Add(candidates + " candidate animations");
        if (candidates == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferDamage.Infer: no candidate animations, no proposal", LogLevel.Warn);
            return result;
        }

        WirePair winner = default;
        int winnerHits = 0;
        List<WirePair> best = new List<WirePair>();
        foreach (KeyValuePair<WirePair, int> entry in hits)
        {
            if (entry.Value < MinHits)
            {
                continue;
            }
            if (entry.Value > winnerHits)
            {
                winner = entry.Key;
                winnerHits = entry.Value;
                best.Clear();
                best.Add(entry.Key);
            }
            else if (entry.Value == winnerHits)
            {
                best.Add(entry.Key);
            }
        }

        if (best.Count == 0)
        {
            result.Messages.Add("no pair with " + MinHits + " hits");
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferDamage.Infer: no pair with " + MinHits + " hits, no proposal", LogLevel.Warn);
            return result;
        }
        if (best.Count > 1)
        {
            string tied = string.Join(", ", best);
            result.Messages.Add("tie at " + winnerHits + " hits between " + tied);
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferDamage.Infer: tie at " + winnerHits + " hits between " + tied + ", no proposal",
                LogLevel.Warn);
            return result;
        }

        int winnerTests = tests.GetValueOrDefault(winner.Animation);
        double share = (double)winnerHits / winnerTests;
        string evidence = winnerHits + "/" + winnerTests + " animations of " + winner.Animation
            + " followed by a " + DamageLength + "-byte " + winner.Damage + " naming the same attacker";

        if (share < ReportThreshold)
        {
            result.Messages.Add(winner.Animation + " and " + winner.Damage + " share " + share.ToString("F2")
                + " below threshold");
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferDamage.Infer: " + evidence + ", share " + share.ToString("F2") + " below threshold, no proposal",
                defaultLoglevel);
            return result;
        }

        InferenceProposal proposal = new InferenceProposal();
        proposal.Assignments.Add(MakeAssignment(AnimationOpcodeName, winner.Animation, share, evidence));
        proposal.Assignments.Add(MakeAssignment(DamageOpcodeName, winner.Damage, share, evidence));
        proposal.Confidence = share;
        proposal.Evidence = evidence;
        result.Proposals.Add(proposal);

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferDamage.Infer: proposed animation " + winner.Animation + " and damage " + winner.Damage + ", "
            + evidence + ", share " + share.ToString("F2"), defaultLoglevel);
        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MakeAssignment
    //
    // Builds one opcode's assignment, carrying the opcode name as name and label.
    //
    // opcodeName:  The opcode identified.
    // wireValue:   The wire value proposed to carry it.
    // confidence:  The share for the identification.
    // evidence:    The evidence for the identification.
    //
    // Returns:  The new assignment.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static OpcodeAssignment MakeAssignment(string opcodeName, OpcodeValue wireValue, double confidence,
        string evidence)
    {
        OpcodeAssignment assignment = new OpcodeAssignment();
        assignment.OpcodeName = opcodeName;
        assignment.Label = opcodeName;
        assignment.Opcode = new PatchOpcode(GlassContext.CurrentPatchLevel, wireValue);
        assignment.Confidence = confidence;
        assignment.Evidence = evidence;
        return assignment;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TallyVotes
    //
    // Walks the capture once in arrival order, on zone sessions only, looking only at
    // zone-to-client messages of wire values not yet accepted.  When the session has a pending
    // candidate, the message is its test: the candidate's wire value gains a test, and the pair
    // gains a hit when the message is DamageLength bytes long and its 16-bit value at
    // DamageAttackerOffset equals the candidate's spawn id.  A message of AnimationLength bytes
    // whose leading 16-bit value is a spawn id placed in the session's zone then becomes the
    // session's pending candidate.
    //
    // catalog:  The session's cataloged packets.
    // tests:    Receives the tests made after candidates of each animation wire value.
    // hits:     Receives the hits per pair.
    //
    // Returns:  The number of candidate animations seen.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static int TallyVotes(PacketCatalog catalog, Dictionary<OpcodeValue, int> tests,
        Dictionary<WirePair, int> hits)
    {
        Dictionary<Session, PendingAnimation> pending = new Dictionary<Session, PendingAnimation>();
        int candidates = 0;
        int testCount = 0;
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint index = 1; index <= totalCount; index++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(index);
            if (packetOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferDamage.TallyVotes: no packet at index " + index, LogLevel.Error);
                continue;
            }
            CatalogedPacket packet = packetOrNull.Value;

            Session session = packet.Metadata.Session;
            if (session == null || session.ServerType != ServerType.Zone || packet.Metadata.ToServer)
            {
                continue;
            }

            OpcodeValue wireValue = packet.Metadata.WireValue;
            if (AcceptedOpcodes.Instance.IsAccepted(wireValue))
            {
                continue;
            }

            ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();

            PendingAnimation? waiting;
            if (pending.TryGetValue(session, out waiting))
            {
                pending.Remove(session);
                testCount++;
                tests[waiting.WireValue] = tests.GetValueOrDefault(waiting.WireValue) + 1;

                if (payload.Length == DamageLength
                    && BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(DamageAttackerOffset)) == waiting.SpawnId)
                {
                    WirePair pair = new WirePair(waiting.WireValue, wireValue);
                    hits[pair] = hits.GetValueOrDefault(pair) + 1;
                }
            }

            if (payload.Length != AnimationLength)
            {
                continue;
            }

            ushort spawnId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(AnimationSpawnIdOffset));
            if (!ZoneRepository.Instance.ResolveEntityId(session.ZoneId, (SpawnId)(uint)spawnId).Exists)
            {
                continue;
            }

            PendingAnimation animation = new PendingAnimation();
            animation.PacketIndex = packet.PacketIndex;
            animation.WireValue = wireValue;
            animation.SpawnId = spawnId;
            pending[session] = animation;
            candidates++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferDamage.TallyVotes: " + candidates + " candidate animations, " + testCount + " tested, "
            + pending.Count + " untested at the end, " + hits.Count + " pairs with hits", defaultLoglevel);
        return candidates;
    }
}