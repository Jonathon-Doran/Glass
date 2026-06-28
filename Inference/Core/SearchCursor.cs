using Glass.Core.Logging;
using Glass.Network.Protocol;
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
        private OpcodeTraceRow? _currentRow; // the row the cursor currently sits on; null only when the trace has no rows
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
            _currentRow = null;
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
        // CurrentMessage
        //
        // The message index of the row the cursor sits on, valid whether the cursor is on a match
        // or parked on a bare row.  None when the cursor holds no row, which occurs only on an
        // empty trace.
        ///////////////////////////////////////////////////////////////////////////////////////
        public MessageIndex CurrentMessage
        {
            get
            {
                if (_currentRow == null)
                {
                    return MessageIndex.None;
                }

                return (MessageIndex)_currentRow.PacketIndex;
            }
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
        // TransitionToRow
        //
        // Moves the cursor's row identity from the row it currently holds to the supplied row,
        // maintaining the cursor-row border across the change.  The border is cleared on the
        // outgoing row (the one _currentRow still names on entry), _currentRow is repointed to the
        // new row, and the border is set on the new row.  Does nothing when the new row is the one
        // already held: the border is already correct and re-flipping it would flicker.
        //
        // The cursor shows itself only through this border.  It does not write the list selection,
        // which is owned by the user for multi-row copy; the cursor's border and the user's
        // selection are independent and may sit on different rows at once.
        //
        // Owns row-level state only.  The cursor's match identity is set separately by the landing
        // path, not here.
        //
        // newRow:  The row the cursor is moving onto.  Null only when the trace has no rows, in
        //          which case the border is cleared off any outgoing row and _currentRow is left
        //          null.
        ///////////////////////////////////////////////////////////////////////////////////////
        private void TransitionToRow(OpcodeTraceRow? newRow)
        {
            if (object.ReferenceEquals(newRow, _currentRow))
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.TransitionToRow: already on target row, no change", LogLevel.Trace);
                return;
            }

            if (_currentRow != null)
            {
                _currentRow.IsCursorRow = false;
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.TransitionToRow: cleared border on packetIndex "
                    + _currentRow.PacketIndex, LogLevel.Trace);
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.TransitionToRow: no outgoing row to clear", LogLevel.Trace);
            }

            _currentRow = newRow;

            if (newRow == null)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.TransitionToRow: new row is null (empty trace), no border set",
                    LogLevel.Warn);
                return;
            }

            newRow.IsCursorRow = true;
            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.TransitionToRow: set border on packetIndex "
                + newRow.PacketIndex, LogLevel.Trace);
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // BringCursorIntoView
        //
        // Centers the cursor's current position in the trace list and paints it in the cursor
        // color.  The paint is deferred until any scroll has settled so the color does not flash
        // mid-scroll.  Does nothing when the trace has no rows.
        //
        // Reads cursor state, so the cursor must already hold its new position when this is called.
        //
        // outgoingElement:  The element the cursor painted at its prior position, or null when the
        //                   cursor had no prior paint.  Used to clear that prior cursor span when
        //                   the paint runs.
        ///////////////////////////////////////////////////////////////////////////////////////
        private void BringCursorIntoView(FieldDisplayNode? outgoingElement)
        {
            if (_currentRow == null)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.BringCursorIntoView: no current row (empty trace), nothing to center",
                    LogLevel.Warn);
                return;
            }

            if (_state == CursorState.OnMatch)
            {
                SearchMatch match = _owner._matches[(int)_matchIndex];

                // Repaint is not called directly.  It is wrapped as the scroll's settled action and
                // runs at the end of the scroll's final layout phase, after the viewport settles, so
                // the cursor color does not flash mid-scroll.
                _owner.ScrollMatchIntoView(_currentRow, match, () => Repaint(outgoingElement));

                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.BringCursorIntoView: scrolling match at packetIndex "
                    + match.PacketIndex + " into view, paint deferred to settled", LogLevel.Trace);
                return;
            }

            _owner.CenterRowInList(_currentRow);
            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.BringCursorIntoView: centered bare row at packetIndex "
                + _currentRow.PacketIndex, LogLevel.Trace);
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // MoveToMessage
        //
        // Moves the cursor to the row named by its message index.  Resolves the row through the
        // owner's row map; when that row owns matches the cursor lands on the row's first match,
        // and when it owns none the cursor parks on the row.  The cursor-row border is maintained
        // across the change and the landing is centered in the list.  When the named message has
        // no row, the cursor is left unchanged.
        //
        // messageIndex:  The message index of the row to move to.
        ///////////////////////////////////////////////////////////////////////////////////////
        public void MoveToMessage(uint messageIndex)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.MoveToMessage: message index " + messageIndex, LogLevel.Info);

            OpcodeTraceRow? row;
            if (!_owner._rowByPacketIndex.TryGetValue(messageIndex, out row))
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.MoveToMessage: message " + messageIndex
                    + " has no row in map, cursor left unchanged", LogLevel.Warn);
                return;
            }

            // capture the outgoing match's element before any state changes, for the deferred paint
            FieldDisplayNode? outgoingElement = null;
            if (_state == CursorState.OnMatch)
            {
                outgoingElement = _owner._matches[(int)_matchIndex].Element;
            }

            TransitionToRow(row);

            if (row.Matches.Count > 0)
            {
                // the same SearchMatch instance lives in both the row and the owner's list; resolve
                // its live subscript by identity rather than storing one, since pruning shifts the list
                SearchMatch firstMatch = row.Matches[0];
                int globalIndex = _owner._matches.IndexOf(firstMatch);

                if (globalIndex < 0)
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.MoveToMessage: row message " + messageIndex
                        + " owns matches but first match is not in the owner's match list, parking on row",
                        LogLevel.Warn);

                    _state = CursorState.OnRow;
                    _rowPacketIndex = messageIndex;
                    _matchIndex = 0u;

                    BringCursorIntoView(outgoingElement);
                    return;
                }

                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.MoveToMessage: row message " + messageIndex
                    + " owns matches, landing on first at global index " + globalIndex, LogLevel.Info);

                _state = CursorState.OnMatch;
                _matchIndex = (uint)globalIndex;
                _rowPacketIndex = messageIndex;

                BringCursorIntoView(outgoingElement);
                return;
            }

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.MoveToMessage: row message " + messageIndex
                + " owns no matches, parking on row", LogLevel.Info);

            _state = CursorState.OnRow;
            _rowPacketIndex = messageIndex;
            _matchIndex = 0u;

            BringCursorIntoView(outgoingElement);
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // AdvanceForward
        //
        // Moves the cursor to the next live match, pruning stale matches as they are encountered.
        // From a match the scan starts after it; from a row the scan starts at the first match at
        // or after the row's position.  With wrap set the end continues from index zero; with wrap
        // off reaching the end leaves the cursor unchanged.  The cursor is also left unchanged when
        // no live match is found.  On landing the cursor-row border is maintained and the match is
        // centered in the list.
        ///////////////////////////////////////////////////////////////////////////////////////
        public void AdvanceForward()
        {
            if (_owner._matches.Count == 0)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.AdvanceForward: empty match list, cursor unchanged", LogLevel.Info);
                return;
            }

            // capture the outgoing match's element before the walk, while _matchIndex still names
            // the pre-move match; a mid-walk prune can shift the list past recovery afterward
            FieldDisplayNode? outgoingElement = null;
            if (_state == CursorState.OnMatch)
            {
                outgoingElement = _owner._matches[(int)_matchIndex].Element;
            }

            uint globalMatchIndex;
            if (_state == CursorState.OnMatch)
            {
                globalMatchIndex = _matchIndex + 1u;
            }
            else
            {
                globalMatchIndex = StartIndexForRow();
            }

            uint examined = 0u;
            uint limit = (uint)_owner._matches.Count;

            while (examined < limit)
            {
                if (globalMatchIndex >= (uint)_owner._matches.Count)
                {
                    if (_wrap == false)
                    {
                        DebugLog.Write(LogChannel.Opcodes,
                            "SearchCursor.AdvanceForward: end reached, wrap off, cursor unchanged",
                            LogLevel.Info);
                        return;
                    }

                    globalMatchIndex = 0u;
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.AdvanceForward: wrapped to index 0", LogLevel.Info);
                }

                if (_owner.RemoveMatchIfStale(globalMatchIndex) == true)
                {
                    examined = examined + 1u;
                    continue;
                }

                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.AdvanceForward: live match found at index " + globalMatchIndex,
                    LogLevel.Info);

                _state = CursorState.OnMatch;
                _matchIndex = globalMatchIndex;
                _rowPacketIndex = _owner._matches[(int)globalMatchIndex].PacketIndex;

                OpcodeTraceRow? newRow;
                if (!_owner._rowByPacketIndex.TryGetValue(_rowPacketIndex, out newRow))
                {
                    newRow = null;
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.AdvanceForward: match rowPacketIndex " + _rowPacketIndex
                        + " has no row in map", LogLevel.Warn);
                }

                TransitionToRow(newRow);
                BringCursorIntoView(outgoingElement);
                return;
            }

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.AdvanceForward: cursor unchanged, no live match found", LogLevel.Info);
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // AdvanceBackward
        //
        // Moves the cursor to the previous live match, pruning stale matches as they are
        // encountered.  From a match the scan starts before it; from a row the scan starts at the
        // last match at or before the row's position.  With wrap set, passing index zero continues
        // from the last index; with wrap off, passing the start leaves the cursor unchanged.  The
        // cursor is also left unchanged when no live match is found.  On landing the cursor-row
        // border is maintained and the match is centered in the list.
        ///////////////////////////////////////////////////////////////////////////////////////
        public void AdvanceBackward()
        {
            if (_owner._matches.Count == 0)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.AdvanceBackward: empty match list, cursor unchanged", LogLevel.Info);
                return;
            }

            // capture the outgoing match's element before the walk, while _matchIndex still names
            // the pre-move match; a mid-walk prune can shift the list past recovery afterward
            FieldDisplayNode? outgoingElement = null;
            if (_state == CursorState.OnMatch)
            {
                outgoingElement = _owner._matches[(int)_matchIndex].Element;
            }

            uint globalMatchIndex;
            bool started;
            if (_state == CursorState.OnMatch)
            {
                if (_matchIndex == 0u)
                {
                    if (_wrap == false)
                    {
                        DebugLog.Write(LogChannel.Opcodes,
                            "SearchCursor.AdvanceBackward: at start, wrap off, cursor unchanged",
                            LogLevel.Info);
                        return;
                    }

                    globalMatchIndex = (uint)_owner._matches.Count - 1u;
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.AdvanceBackward: wrapped to last index " + globalMatchIndex,
                        LogLevel.Info);
                }
                else
                {
                    globalMatchIndex = _matchIndex - 1u;
                }
                started = true;
            }
            else
            {
                globalMatchIndex = StartIndexForRowBackward(out started);
            }

            if (started == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.AdvanceBackward: no match at or before row, cursor unchanged",
                    LogLevel.Info);
                return;
            }

            uint examined = 0u;
            uint limit = (uint)_owner._matches.Count;

            while (examined < limit)
            {
                if (_owner.RemoveMatchIfStale(globalMatchIndex) == true)
                {
                    examined = examined + 1u;

                    if (_owner._matches.Count == 0)
                    {
                        DebugLog.Write(LogChannel.Opcodes,
                            "SearchCursor.AdvanceBackward: list emptied by pruning, cursor unchanged",
                            LogLevel.Info);
                        return;
                    }

                    if (globalMatchIndex >= (uint)_owner._matches.Count)
                    {
                        globalMatchIndex = (uint)_owner._matches.Count - 1u;
                    }
                    else if (globalMatchIndex == 0u)
                    {
                        if (_wrap == false)
                        {
                            DebugLog.Write(LogChannel.Opcodes,
                                "SearchCursor.AdvanceBackward: start reached after prune, wrap off, cursor unchanged",
                                LogLevel.Info);
                            return;
                        }

                        globalMatchIndex = (uint)_owner._matches.Count - 1u;
                        DebugLog.Write(LogChannel.Opcodes,
                            "SearchCursor.AdvanceBackward: wrapped to last index " + globalMatchIndex
                            + " after prune", LogLevel.Info);
                    }
                    else
                    {
                        globalMatchIndex = globalMatchIndex - 1u;
                    }

                    continue;
                }

                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.AdvanceBackward: live match found at index " + globalMatchIndex,
                    LogLevel.Info);

                _state = CursorState.OnMatch;
                _matchIndex = globalMatchIndex;
                _rowPacketIndex = _owner._matches[(int)globalMatchIndex].PacketIndex;

                OpcodeTraceRow? newRow;
                if (!_owner._rowByPacketIndex.TryGetValue(_rowPacketIndex, out newRow))
                {
                    newRow = null;
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.AdvanceBackward: match rowPacketIndex " + _rowPacketIndex
                        + " has no row in map", LogLevel.Warn);
                }

                TransitionToRow(newRow);
                BringCursorIntoView(outgoingElement);
                return;
            }

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.AdvanceBackward: cursor unchanged, no live match found", LogLevel.Info);
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
                        + " resolved to match index " + candidateIndex, LogLevel.Info);
                    found = true;
                    return candidateIndex;
                }
            }

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.StartIndexForRowBackward: no match at or before row packetIndex "
                + _rowPacketIndex + ", cursor will be left unchanged", LogLevel.Info);
            found = false;
            return 0u;
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