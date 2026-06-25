using Glass.Core.Logging;
using Glass.Network.Protocol.Fields;
using Glass.UI;
using Inference.Models;
using System;

namespace Inference.Core;

public partial class OpcodeTracePresenter
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // SearchCursor
    //
    // Names where find navigation currently sits.  Holds one of two states:
    //
    //   OnMatch  The cursor sits on a resolved match.  The match index is a valid subscript into
    //            the owning presenter's _matches; the row index is unset.
    //   OnRow    The cursor names a row, with no match resolved yet.  The rowPacketIndex IDs that
    //            row; the match index is unset.  The first advance resolves this to a concrete
    //            match by direction.
    //
    // A cursor is valid only against the match list it was advanced over and only until that
    // list is rebuilt by the next search.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private sealed class SearchCursor
    {
        ///////////////////////////////////////////////////////////////////////////////////////
        // CursorState
        //
        // The cursor's two mutually exclusive conditions: sitting on a resolved match, or naming
        // a row whose match has not been resolved yet.
        ///////////////////////////////////////////////////////////////////////////////////////
        public enum CursorState
        {
            OnMatch,
            OnRow
        }

        private readonly OpcodeTracePresenter _owner;
        private CursorState _state;
        private uint _matchIndex;
        private uint _rowPacketIndex;       // this is a durable ID unlike a row index
        private bool _wrap;
        private readonly ArgbColor _cursorColor;

        ///////////////////////////////////////////////////////////////////////////////////////
        // SearchCursor (constructor)
        //
        // Builds a cursor owned by the given presenter, starting OnRow at row zero with no match
        // resolved.
        //
        // owner:  The presenter whose match list, rows, and wrap flag this cursor navigates.
        ///////////////////////////////////////////////////////////////////////////////////////
        public SearchCursor(OpcodeTracePresenter owner)
        {
            _owner = owner;
            _state = CursorState.OnRow;
            _matchIndex = 0u;
            _rowPacketIndex = 0u;
            _wrap = false;
            _cursorColor = new ArgbColor(0x800000FFu);
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // State
        //
        // The cursor's current condition: OnMatch when it sits on a resolved match, OnRow when
        // it names a row awaiting resolution.
        ///////////////////////////////////////////////////////////////////////////////////////
        public CursorState State
        {
            get { return _state; }
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // MatchIndex
        //
        // The resolved match's subscript into the owner's _matches.  Valid only when OnMatch;
        // reading it OnRow is an integrity violation.
        ///////////////////////////////////////////////////////////////////////////////////////
        public uint MatchIndex
        {
            get
            {
                if (_state != CursorState.OnMatch)
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.MatchIndex read while state is " + _state, LogLevel.Error);
                    Environment.FailFast("SearchCursor.MatchIndex read while not OnMatch");
                }

                return _matchIndex;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // RowPacketIndex
        //
        // The PacketIndex of the row the cursor names.  Valid only when OnRow; reading it
        // OnMatch is an integrity violation.
        ///////////////////////////////////////////////////////////////////////////////////////
        public uint RowPacketIndex
        {
            get
            {
                if (_state != CursorState.OnRow)
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.RowPacketIndex read while state is " + _state, LogLevel.Error);
                    Environment.FailFast("SearchCursor.RowPacketIndex read while not OnRow");
                }

                return _rowPacketIndex;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // SetWrap
        //
        // Sets whether advancing past the end of the match list wraps to the other end and
        // continues, or stops.
        //
        // wrap:  True to wrap, false to stop at the ends.
        ///////////////////////////////////////////////////////////////////////////////////////
        public void SetWrap(bool wrap)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.SetWrap: " + _wrap + " -> " + wrap, LogLevel.Trace);

            _wrap = wrap;
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // MoveToPacket
        //
        // Transitions the cursor to the OnRow state, naming the row by its PacketIndex.  The
        // match index is cleared; OnRow makes it meaningless.
        //
        // rowPacketIndex:  The PacketIndex of the row the cursor names.
        ///////////////////////////////////////////////////////////////////////////////////////
        public void MoveToPacket(uint rowPacketIndex)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.MoveToPacket: " + _state + " -> OnRow(rowPacketIndex=" + rowPacketIndex + ")",
                LogLevel.Info);

            _state = CursorState.OnRow;
            _rowPacketIndex = rowPacketIndex;
            _matchIndex = 0u;
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // AdvanceForward
        //
        // Moves this cursor to the next live match, removing stale matches as they are
        // encountered.  OnMatch starts after the current match; OnRow starts at the first match
        // whose PacketIndex is at or after the cursor row's PacketIndex.  With wrap set, the end
        // wraps to index zero and the scan continues; with wrap off, reaching the end ends the
        // scan.  The cursor is left unchanged when no live match is found.
        //
        // On landing, the outgoing match's element is captured before the cursor index moves, the
        // match's row is scrolled into view, and Repaint is run as the scroll's settled action so
        // the cursor color lands after the viewport has stopped moving.  Scrolling before painting
        // cuts the color-flash artifact that painting first would leave during the scroll.
        ///////////////////////////////////////////////////////////////////////////////////////
        public void AdvanceForward()
        {
            if (_owner._matches.Count == 0)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.AdvanceForward: empty match list, cursor unchanged", LogLevel.Trace);
                return;
            }

            FieldDisplayNode? outgoingElement = null;
            if (_state == CursorState.OnMatch)
            {
                outgoingElement = _owner._matches[(int)_matchIndex].Element;
            }

            uint index;
            if (_state == CursorState.OnMatch)
            {
                index = _matchIndex + 1u;
            }
            else
            {
                index = StartIndexForRow();
            }

            uint examined = 0u;
            uint limit = (uint)_owner._matches.Count;

            while (examined < limit)
            {
                if (index >= (uint)_owner._matches.Count)
                {
                    if (_wrap == false)
                    {
                        DebugLog.Write(LogChannel.Opcodes,
                            "SearchCursor.AdvanceForward: end reached, wrap off, cursor unchanged",
                            LogLevel.Trace);
                        return;
                    }

                    index = 0u;
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.AdvanceForward: wrapped to index 0", LogLevel.Trace);
                }

                if (_owner.RemoveMatchIfStale(index) == true)
                {
                    examined = examined + 1u;
                    continue;
                }

                _state = CursorState.OnMatch;
                _matchIndex = index;
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.AdvanceForward: cursor moved to match index " + index, LogLevel.Trace);

                LandOnCurrentMatch(outgoingElement);
                return;
            }

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.AdvanceForward: cursor unchanged, no live match found", LogLevel.Trace);
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // AdvanceBackward
        //
        // Moves this cursor to the previous live match, removing stale matches as they are
        // encountered.  OnMatch starts before the current match; OnRow starts at the last match
        // whose PacketIndex is at or before the cursor row's PacketIndex.  With wrap set, passing
        // index zero wraps to the last index and the scan continues; with wrap off, passing the
        // start ends the scan.  The cursor is left unchanged when no live match is found.
        //
        // On landing, the outgoing match's element is captured before the cursor index moves, the
        // match's row is scrolled into view, and Repaint is run as the scroll's settled action so
        // the cursor color lands after the viewport has stopped moving.  Scrolling before painting
        // cuts the color-flash artifact that painting first would leave during the scroll.
        ///////////////////////////////////////////////////////////////////////////////////////
        public void AdvanceBackward()
        {
            if (_owner._matches.Count == 0)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.AdvanceBackward: empty match list, cursor unchanged", LogLevel.Trace);
                return;
            }

            FieldDisplayNode? outgoingElement = null;
            if (_state == CursorState.OnMatch)
            {
                outgoingElement = _owner._matches[(int)_matchIndex].Element;
            }

            uint index;
            bool started;
            if (_state == CursorState.OnMatch)
            {
                if (_matchIndex == 0u)
                {
                    if (_wrap == false)
                    {
                        DebugLog.Write(LogChannel.Opcodes,
                            "SearchCursor.AdvanceBackward: at start, wrap off, cursor unchanged",
                            LogLevel.Trace);
                        return;
                    }

                    index = (uint)_owner._matches.Count - 1u;
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.AdvanceBackward: wrapped to last index " + index, LogLevel.Trace);
                }
                else
                {
                    index = _matchIndex - 1u;
                }
                started = true;
            }
            else
            {
                index = StartIndexForRowBackward(out started);
            }

            if (started == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.AdvanceBackward: no match at or before row, cursor unchanged",
                    LogLevel.Trace);
                return;
            }

            uint examined = 0u;
            uint limit = (uint)_owner._matches.Count;

            while (examined < limit)
            {
                if (_owner.RemoveMatchIfStale(index) == true)
                {
                    examined = examined + 1u;

                    if (_owner._matches.Count == 0)
                    {
                        DebugLog.Write(LogChannel.Opcodes,
                            "SearchCursor.AdvanceBackward: list emptied by pruning, cursor unchanged",
                            LogLevel.Trace);
                        return;
                    }

                    if (index >= (uint)_owner._matches.Count)
                    {
                        index = (uint)_owner._matches.Count - 1u;
                    }
                    else if (index == 0u)
                    {
                        if (_wrap == false)
                        {
                            DebugLog.Write(LogChannel.Opcodes,
                                "SearchCursor.AdvanceBackward: start reached after prune, wrap off, cursor unchanged",
                                LogLevel.Trace);
                            return;
                        }

                        index = (uint)_owner._matches.Count - 1u;
                        DebugLog.Write(LogChannel.Opcodes,
                            "SearchCursor.AdvanceBackward: wrapped to last index " + index
                            + " after prune", LogLevel.Trace);
                    }
                    else
                    {
                        index = index - 1u;
                    }

                    continue;
                }

                _state = CursorState.OnMatch;
                _matchIndex = index;
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.AdvanceBackward: cursor moved to match index " + index, LogLevel.Trace);

                LandOnCurrentMatch(outgoingElement);
                return;
            }

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.AdvanceBackward: cursor unchanged, no live match found", LogLevel.Trace);
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // StartIndexForRowBackward
        //
        // Resolves the OnRow cursor to the index in the owner's _matches where a backward scan
        // begins: the last match whose PacketIndex is at or before the named row's PacketIndex.
        // The match list is in ascending PacketIndex order, so the scan runs from the high end
        // down and returns the first match it finds at or below the row.  When no match is at or
        // before the row, found is set false and zero is returned, signaling the backward scan
        // to leave the cursor unchanged.
        //
        // found:    Set true when a match at or before the row's PacketIndex exists, false when
        //           none does.
        //
        // returns:  The subscript into _matches of the last match at or before the row's
        //           PacketIndex when found is true; zero when found is false.
        ///////////////////////////////////////////////////////////////////////////////////////
        private uint StartIndexForRowBackward(out bool found)
        {
            uint count = (uint)_owner._matches.Count;
            for (uint i = count; i > 0u; i = i - 1u)
            {
                uint candidateIndex = i - 1u;
                if (_owner._matches[(int)candidateIndex].PacketIndex <= _rowPacketIndex)
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.StartIndexForRowBackward: row packetIndex " + _rowPacketIndex
                        + " resolved to match index " + candidateIndex, LogLevel.Trace);
                    found = true;
                    return candidateIndex;
                }
            }

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.StartIndexForRowBackward: no match at or before row packetIndex "
                + _rowPacketIndex + ", cursor will be left unchanged", LogLevel.Trace);
            found = false;
            return 0u;
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // LandOnCurrentMatch
        //
        // Brings the match the cursor now sits on into view and paints it in the cursor color.
        // Called from an advance once the cursor is OnMatch at its new index.  The match's row is
        // resolved through the owner and scrolled into view; Repaint is handed to the scroll as
        // its settled action so the cursor color lands only after the viewport has stopped moving,
        // cutting the color-flash a paint-first order would leave.  The outgoing element is passed
        // through to Repaint so the prior cursor paint clears even when it sat on a different
        // element.
        //
        // When the match's row cannot be resolved the scroll is skipped and Repaint runs directly,
        // so the cursor color is still applied even though the row could not be brought into view.
        //
        // outgoingElement:  The element the cursor painted on its prior position, or null when the
        //                   cursor had no prior paint.
        ///////////////////////////////////////////////////////////////////////////////////////
        private void LandOnCurrentMatch(FieldDisplayNode? outgoingElement)
        {
            SearchMatch match = _owner._matches[(int)_matchIndex];
            uint matchPacketIndex = match.PacketIndex;

            OpcodeTraceRow? row;
            if (_owner._rowByPacketIndex.TryGetValue(matchPacketIndex, out row))
            {
                // Repaint is not called here.  It is wrapped as the scroll's settled action and
                // runs at the end of the scroll's final layout phase, after the viewport settles.

                _owner.ScrollMatchIntoView(row, match, () => Repaint(outgoingElement));
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.LandOnCurrentMatch: scrolling match at packetIndex " + matchPacketIndex
                    + " into view, paint deferred to settled", LogLevel.Trace);
            }
            else
            {
                // No row to scroll, so nothing defers the paint; Repaint runs now.

                Repaint(outgoingElement);
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.LandOnCurrentMatch: match packetIndex " + matchPacketIndex
                    + " has no row in map, painted without scroll", LogLevel.Warn);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // StartIndexForRow
        //
        // Resolves the OnRow cursor to the index in the owner's _matches where a forward scan
        // begins: the first match whose PacketIndex is at or after the named row's PacketIndex.
        // Returns the match count when no match is at or after the row, so the forward scan sees
        // a past-the-end start and applies its end-of-list handling.
        //
        // returns:  The subscript into _matches of the first match at or after the row's
        //           PacketIndex, or the match count when none qualifies.
        ///////////////////////////////////////////////////////////////////////////////////////
        private uint StartIndexForRow()
        {
            for (uint i = 0u; i < (uint)_owner._matches.Count; i = i + 1u)
            {
                if (_owner._matches[(int)i].PacketIndex >= _rowPacketIndex)
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.StartIndexForRow: row packetIndex " + _rowPacketIndex
                        + " resolved to match index " + i, LogLevel.Trace);
                    return i;
                }
            }

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.StartIndexForRow: no match at or after row packetIndex "
                + _rowPacketIndex + ", returning end index " + _owner._matches.Count, LogLevel.Trace);
            return (uint)_owner._matches.Count;
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // Repaint
        //
        // Paints the match the cursor sits on in the cursor color, clearing the previous cursor
        // paint everywhere in one step.  The cursor color is its own generation lane in the
        // owner's highlight generation map: bumping that lane makes every span previously stamped
        // in the cursor color stale at once, restoring those matches to their underlying highlight
        // color with no per-row bookkeeping.  After the bump a fresh cursor-color span is stamped
        // over every span of the current match, so the whole hit lights in the cursor color.  The
        // new element is notified so it re-renders; the outgoing element, when it differs from the
        // new one, is notified too so its now-stale cursor span clears.
        //
        // Bumping before stamping is required: stamping first and bumping after would make the
        // just-stamped spans stale.
        //
        // outgoingElement:  The element the cursor painted on its prior position, or null when the
        //                   cursor had no prior paint.  Notified after the bump when it differs
        //                   from the current match's element.
        ///////////////////////////////////////////////////////////////////////////////////////
        private void Repaint(FieldDisplayNode? outgoingElement)
        {
            SearchMatch match = _owner._matches[(int)_matchIndex];
            FieldDisplayNode element = match.Element;

            _owner._highlightGenerationMap.Bump(_cursorColor);
            uint cursorGeneration = _owner._highlightGenerationMap.CurrentGeneration(_cursorColor);

            int stamped = 0;
            for (int i = 0; i < match.Spans.Count; i++)
            {
                HighlightSpan source = match.Spans[i];
                HighlightSpan cursorSpan = new HighlightSpan(
                    source.Start, source.Length, _cursorColor, cursorGeneration);
                element.AddSpan(cursorSpan, _owner._highlightGenerationMap.CurrentGeneration);
                stamped++;
            }

            element.NotifySpansChanged();

            if (outgoingElement != null && object.ReferenceEquals(outgoingElement, element) == false)
            {
                outgoingElement.NotifySpansChanged();
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.Repaint: notified outgoing element to clear stale cursor span",
                    LogLevel.Trace);
            }

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.Repaint: stamped " + stamped + " cursor span(s) at match index "
                + _matchIndex, LogLevel.Trace);
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // ToString
        //
        // Renders the cursor as its state and the field that state makes meaningful.
        ///////////////////////////////////////////////////////////////////////////////////////
        public override string ToString()
        {
            if (_state == CursorState.OnRow)
            {
                return "OnRow(rowPacketIndex=" + _rowPacketIndex + ")";
            }

            return "OnMatch(match=" + _matchIndex + ")";
        }
    }
}