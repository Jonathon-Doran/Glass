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
        private uint _rowPacketIndex;             // this is a durable ID unlike a row index
        private FieldDisplayNode? _anchorElement; // durable node of the cursor's match, valid across a rebuild
        private int _anchorTextOffset;            // anchor span offset within _anchorElement.Text
        private bool _matchIndexValid;            // true while _matchIndex names a live position; false when the anchor must re-resolve it
        private OpcodeTraceRow? _currentRow;      // the row the cursor currently sits on; null only when the trace has no rows
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
            _anchorElement = null;
            _anchorTextOffset = 0;
            _matchIndexValid = false;
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
        // Paints the cursor's current position in the cursor color, optionally centering it in
        // the trace list first.  When centering, the paint is deferred until the scroll has
        // settled so the color does not flash mid-scroll.  When not centering, the paint runs
        // immediately and the scroll position is left where the user has it.  Does nothing when
        // the trace has no rows.
        //
        // Reads cursor state, so the cursor must already hold its new position when this is called.
        //
        // outgoingElement:  The element the cursor painted at its prior position, or null when the
        //                   cursor had no prior paint.  Used to clear that prior cursor span when
        //                   the paint runs.
        // centerInView:     True to scroll the cursor's position to center before painting, false
        //                   to paint in place without scrolling.
        ///////////////////////////////////////////////////////////////////////////////////////
        private void BringCursorIntoView(FieldDisplayNode? outgoingElement, bool centerInView)
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

                if (centerInView == false)
                {
                    Repaint(outgoingElement);
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.BringCursorIntoView: painted match at packetIndex "
                        + match.PacketIndex + " in place, no scroll", LogLevel.Trace);
                    return;
                }

                // Repaint is not called directly.  It is wrapped as the scroll's settled action and
                // runs at the end of the scroll's final layout phase, after the viewport settles, so
                // the cursor color does not flash mid-scroll.
                _owner.ScrollMatchIntoView(_currentRow, match, () => Repaint(outgoingElement));

                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.BringCursorIntoView: scrolling match at packetIndex "
                    + match.PacketIndex + " into view, paint deferred to settled", LogLevel.Trace);
            }
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
        // centerInView:  True to center the landing in the list, false to leave the scroll
        //                position unchanged.  Defaults to true.
        ///////////////////////////////////////////////////////////////////////////////////////////
        public void MoveToMessage(uint messageIndex, bool centerInView = true)
        {
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

                    BringCursorIntoView(outgoingElement, centerInView);
                    return;
                }

                SetCursorOnMatch((uint)globalIndex);

                BringCursorIntoView(outgoingElement, centerInView);
                return;
            }

            _state = CursorState.OnRow;
            _rowPacketIndex = messageIndex;
            _matchIndex = 0u;

            BringCursorIntoView(outgoingElement, centerInView);
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
                return;
            }


            // capture the outgoing match's element before the walk, while _matchIndex still names
            // the pre-move match; a mid-walk prune can shift the list past recovery afterward.
            // an invalidated or out-of-range index means the outgoing match is already gone and
            // there is no prior cursor span to clear
            FieldDisplayNode? outgoingElement = null;
            if (_state == CursorState.OnMatch && _matchIndexValid
                && _matchIndex < (uint)_owner._matches.Count)
            {
                outgoingElement = _owner._matches[(int)_matchIndex].Element;
            }

            uint globalMatchIndex;
            if (_state == CursorState.OnMatch && _matchIndexValid)
            {
                globalMatchIndex = _matchIndex + 1u;
            }
            else if (_state == CursorState.OnMatch && !_matchIndexValid)
            {
                globalMatchIndex = StartIndexForAnchorForward();
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
                            LogLevel.Trace);
                        return;
                    }

                    globalMatchIndex = 0u;
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.AdvanceForward: wrapped to index 0", LogLevel.Trace);
                }

                if (_owner.RemoveMatchIfStale(globalMatchIndex) == true)
                {
                    examined = examined + 1u;
                    continue;
                }

                SearchMatch matchTest = _owner._matches[(int) globalMatchIndex];

                SetCursorOnMatch(globalMatchIndex);

                OpcodeTraceRow? newRow;
                if (!_owner._rowByPacketIndex.TryGetValue(_rowPacketIndex, out newRow))
                {
                    newRow = null;
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.AdvanceForward: match rowPacketIndex " + _rowPacketIndex
                        + " has no row in map", LogLevel.Warn);
                }

                TransitionToRow(newRow);
                BringCursorIntoView(outgoingElement, true);
                return;
            }
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
                    "SearchCursor.AdvanceBackward: empty match list, cursor unchanged", LogLevel.Trace);
                return;
            }

            // capture the outgoing match's element before the walk, while _matchIndex still names
            // the pre-move match; a mid-walk prune can shift the list past recovery afterward.
            // an invalidated or out-of-range index means the outgoing match is already gone and
            // there is no prior cursor span to clear
            FieldDisplayNode? outgoingElement = null;
            if (_state == CursorState.OnMatch && _matchIndexValid
                && _matchIndex < (uint)_owner._matches.Count)
            {
                outgoingElement = _owner._matches[(int)_matchIndex].Element;
            }

            uint globalMatchIndex;
            bool started;
            if (_state == CursorState.OnMatch && !_matchIndexValid)
            {
                globalMatchIndex = StartIndexForAnchorBackward(out started);
            }
            else if (_state == CursorState.OnMatch)
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

                    globalMatchIndex = (uint)_owner._matches.Count - 1u;
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.AdvanceBackward: wrapped to last index " + globalMatchIndex,
                        LogLevel.Trace);
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
                    LogLevel.Trace);
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
                            LogLevel.Trace);
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
                                LogLevel.Trace);
                            return;
                        }

                        globalMatchIndex = (uint)_owner._matches.Count - 1u;
                        DebugLog.Write(LogChannel.Opcodes,
                            "SearchCursor.AdvanceBackward: wrapped to last index " + globalMatchIndex
                            + " after prune", LogLevel.Trace);
                    }
                    else
                    {
                        globalMatchIndex = globalMatchIndex - 1u;
                    }

                    continue;
                }

                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.AdvanceBackward: live match found at index " + globalMatchIndex,
                    LogLevel.Trace);

                SetCursorOnMatch(globalMatchIndex);

                OpcodeTraceRow? newRow;
                if (!_owner._rowByPacketIndex.TryGetValue(_rowPacketIndex, out newRow))
                {
                    newRow = null;
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.AdvanceBackward: match rowPacketIndex " + _rowPacketIndex
                        + " has no row in map", LogLevel.Warn);
                }

                TransitionToRow(newRow);
                BringCursorIntoView(outgoingElement, true);
                return;
            }

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.AdvanceBackward: cursor unchanged, no live match found", LogLevel.Trace);
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // NotifyMatchRemoved
        //
        // Keeps the cursor's match index synchronized with the match list when an entry is
        // removed.  A removal below the cursor's index shifts the cursor's match down one
        // position, so the index is decremented to keep naming the same match.  A removal at
        // the cursor's index means the cursor's own match is gone; the index is marked invalid
        // so the next walk re-resolves from the durable anchor.  Removals above the cursor,
        // and removals while the cursor is not on a match, need no adjustment.
        //
        // removedIndex:  The list index the match was removed from.
        ///////////////////////////////////////////////////////////////////////////////////////
        public void NotifyMatchRemoved(uint removedIndex)
        {
            if (_state != CursorState.OnMatch)
            {
                return;
            }

            if (removedIndex < _matchIndex)
            {
                _matchIndex = _matchIndex - 1u;
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.NotifyMatchRemoved: removal at " + removedIndex
                    + " below cursor, match index now " + _matchIndex, LogLevel.Trace);
                return;
            }

            if (removedIndex == _matchIndex)
            {
                _matchIndexValid = false;
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.NotifyMatchRemoved: cursor's own match removed at " + removedIndex
                    + ", index invalidated for anchor re-resolve", LogLevel.Trace);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // StartIndexForAnchorForward
        //
        // Resolves the starting index for a forward advance when the stored match index is
        // invalid after a rebuild.  Scans _matches for the first match whose element's
        // SearchOrdinal is at or after the anchor element's ordinal.  When ordinals are equal
        // (same node), the anchor offset breaks the tie: the match must start at or after
        // _anchorTextOffset.  Returns the match count when no qualifying match exists, so the
        // forward scan sees a past-the-end start and applies its end-of-list handling.
        ///////////////////////////////////////////////////////////////////////////////////////
        private uint StartIndexForAnchorForward()
        {
            if (_anchorElement == null)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.StartIndexForAnchorForward: no anchor, returning end index",
                    LogLevel.Warn);
                return (uint)_owner._matches.Count;
            }

            for (uint i = 0u; i < (uint)_owner._matches.Count; i++)
            {
                SearchMatch candidate = _owner._matches[(int)i];

                HighlightSpan anchor = candidate.Anchor;
                if (anchor.Generation < _owner._highlightGenerationMap.CurrentGeneration(anchor.OverrideColor))
                {
                    continue;
                }

                if (candidate.PacketIndex == _rowPacketIndex
                    && candidate.Element.RowTextOffset + candidate.Anchor.Start >= _anchorTextOffset)
                {
                    return i;
                }

                if (candidate.PacketIndex > _rowPacketIndex)
                {
                    return i;
                }
            }

            return (uint)_owner._matches.Count;
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // StartIndexForAnchorBackward
        //
        // Resolves the starting index for a backward advance when the stored match index is
        // invalid after a rebuild.  Scans _matches in reverse for the last match whose
        // element's SearchOrdinal is at or before the anchor element's ordinal.  When
        // ordinals are equal (same node), the anchor offset breaks the tie: the match must
        // start at or before _anchorTextOffset.  When no qualifying match exists, found is set
        // false and zero is returned, signaling the backward scan to leave the cursor
        // unchanged.
        //
        // found:    Set true when a qualifying match exists, false when none does.
        //
        // returns:  The subscript of the last qualifying match when found is true; zero when
        //           found is false.
        ///////////////////////////////////////////////////////////////////////////////////////
        private uint StartIndexForAnchorBackward(out bool found)
        {
            if (_anchorElement == null)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "SearchCursor.StartIndexForAnchorBackward: no anchor, cannot resolve",
                    LogLevel.Warn);
                found = false;
                return 0u;
            }

            uint count = (uint)_owner._matches.Count;

            for (uint i = count; i > 0u; i--)
            {
                uint candidateIndex = i - 1u;
                SearchMatch candidate = _owner._matches[(int)candidateIndex];

                HighlightSpan anchor = candidate.Anchor;
                if (anchor.Generation < _owner._highlightGenerationMap.CurrentGeneration(anchor.OverrideColor))
                {
                    continue;
                }

                if (candidate.PacketIndex == _rowPacketIndex
                    && candidate.Element.RowTextOffset + candidate.Anchor.Start <= _anchorTextOffset)
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.StartIndexForAnchorBackward: resolved to index " + candidateIndex
                        + " same packet, rowTextOffset=" + candidate.Element.RowTextOffset, LogLevel.Trace);
                    found = true;
                    return candidateIndex;
                }

                if (candidate.PacketIndex < _rowPacketIndex)
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "SearchCursor.StartIndexForAnchorBackward: resolved to index " + candidateIndex
                        + " packetIndex=" + candidate.PacketIndex, LogLevel.Trace);
                    found = true;
                    return candidateIndex;
                }
            }

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.StartIndexForAnchorBackward: no match at or before anchor packetIndex "
                + _rowPacketIndex + " rowTextOffset=" + _anchorTextOffset
                + ", cursor will be left unchanged", LogLevel.Trace);
            found = false;
            return 0u;
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // SetCursorOnMatch
        //
        // The single sink through which the cursor takes up residence on a resolved match.  Sets
        // the cursor OnMatch at the given subscript, records the match's row packet index, and
        // records the match's durable position — the FieldDisplayNode its anchor span sits on and
        // that span's character offset.  Every landing path routes through here so the durable
        // position is recorded in exactly one place.
        //
        // The durable position is what lets a relative search survive the match list being rebuilt
        // by the next query.  A match subscript is invalidated the instant the list is rebuilt; the
        // recorded node and offset are not, because FieldDisplayNode instances are permanent and
        // reference-stable across a rebuild and the offset is into that node's own Text.  The next
        // directional advance brackets from this recorded position when the subscript no longer
        // means anything.
        //
        // newIndex:  The subscript into the owner's _matches the cursor is landing on.  Must be in
        //            range; the caller resolves and bounds-checks it before landing.
        ///////////////////////////////////////////////////////////////////////////////////////
        private void SetCursorOnMatch(uint newIndex)
        {
            SearchMatch match = _owner._matches[(int)newIndex];

            _state = CursorState.OnMatch;
            _matchIndex = newIndex;
            _matchIndexValid = true;
            _rowPacketIndex = match.PacketIndex;
            _anchorElement = match.Element;
            _anchorTextOffset = (int) match.Element.RowTextOffset + match.Anchor.Start;

            DebugLog.Write(LogChannel.Opcodes,
                "SearchCursor.SetCursorOnMatch: index " + newIndex + " packetIndex "
                + _rowPacketIndex + " anchorOffset " + _anchorTextOffset, LogLevel.Trace);
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // InvalidateMatchIndex
        //
        // Marks the cursor's stored match subscript as no longer naming a live position in the
        // owner's match list.  While invalid, the next directional advance re-resolves its
        // starting position from the cursor's durable anchor — the FieldDisplayNode and character
        // offset recorded at the last landing — instead of trusting the stored subscript.  A
        // landing then assigns a fresh subscript and makes it valid again.
        ///////////////////////////////////////////////////////////////////////////////////////
        public void InvalidateMatchIndex()
        {
            _matchIndexValid = false;
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
                    found = true;
                    return candidateIndex;
                }
            }

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
                    return i;
                }
            }

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
        // Reset
        //
        // Returns the cursor to its initial state.  Clears all match and anchor state,
        // parks the cursor OnRow at packet index zero.  Called when the trace is cleared
        // so no stale match index, anchor, or row reference survives into the next capture.
        ///////////////////////////////////////////////////////////////////////////////////////
        public void Reset()
        {
            _state = CursorState.OnRow;
            _matchIndex = 0u;
            _rowPacketIndex = 0u;
            _currentRow = null;
            _wrap = false;
            _anchorElement = null;
            _anchorTextOffset = 0;
            _matchIndexValid = false;

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