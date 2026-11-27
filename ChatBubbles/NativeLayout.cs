using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MiniTalk = FFXIVClientStructs.FFXIV.Client.UI.AddonMiniTalk;

namespace ChatBubbles
{
    public unsafe partial class ChatBubbles
    {
        private const int _compactMinWidth = 44;
        private const int _compactHorizontalPad = 13;
        private const int _compactTopPad = 4;
        private const int _compactBottomPad = 9;
        private static string NodeGeometry(AtkResNode* node)
        {
            if (node == null) return "missing";
            return $"id={node->NodeId}, {node->Width}x{node->Height}, xy={node->X:F1},{node->Y:F1}, scale={node->ScaleX:F2},{node->ScaleY:F2}";
        }
        private static string ApplyCompactLayout(AtkTextNode* text, AtkResNode* bg, AtkResNode* tail, bool grownInPlace = false)
        {
            ushort width = 0, height = 0;
            text->GetTextDrawSize(&width, &height);
            var tn = (AtkResNode*)text;
            if (bg == null || bg->NodeId != 5 || tn->NodeId != 3 || width == 0 || height == 0 || width > 500 || height > 600)
                return $"Unexpected geometry; measured={width}x{height}, text node={tn->Width}x{tn->Height}; no changes made.";
            string before = NodeGeometry(bg);
            ushort targetW = (ushort)Math.Max(_compactMinWidth, (height <= 21 ? (int)width : Math.Max((int)width, (int)tn->Width)) + 2 * _compactHorizontalPad);
            // Tall native text nodes contain internal vertical whitespace. Preserve the
            // text node size and wrapping; trim only the background around its glyphs.
            int trimTop = !grownInPlace && tn->Height >= 120 ? 13 : 0;
            int trimBottom = !grownInPlace && tn->Height >= 120 ? 10 : 0;
            ushort targetH = (ushort)Math.Max(36, (int)tn->Height + _compactTopPad + _compactBottomPad - trimTop - trimBottom);
            float bottom = bg->Y + bg->Height;
            if (bg->Width != targetW) bg->SetWidth(targetW);
            if (bg->Height != targetH) bg->SetHeight(targetH);
            float bx = -targetW / 2f, by = bottom - targetH;
            float tx = -(height <= 21 ? width : tn->Width) / 2f - (height <= 21 ? 2f : 0f);
            float ty = by + _compactTopPad - trimTop;
            if (Math.Abs(bg->X - bx) > 0.01f || Math.Abs(bg->Y - by) > 0.01f) bg->SetPositionFloat(bx, by);
            if (Math.Abs(tn->X - tx) > 0.01f || Math.Abs(tn->Y - ty) > 0.01f) tn->SetPositionFloat(tx, ty);
            return $"Measured text={width}x{height}\nBefore: {before}\nAfter: {NodeGeometry(bg)}\nText node: {NodeGeometry(tn)}\nTail: {NodeGeometry(tail)}";
        }
        private HashSet<nint> ExistingMatchingNodes(string message)
        {
            var result = new HashSet<nint>();
            var address = (nint)Services.GameGui.GetAddonByName("_MiniTalk", 1);
            if (address == 0) return result;
            var addon = (MiniTalk*)address;
            string expected = NormalizeSpacing(message);
            foreach (var entry in addon->TalkBubbles)
            {
                var text = entry.BubbleTextNode;
                var root = entry.BubbleResNode;
                if (text != null && root != null && root->IsVisible() && text->IsVisible() && MatchesLayoutText(NativeNodePlain(text), message))
                    result.Add((nint)text);
            }
            return result;
        }
        private void ApplyActiveNativeSpacing(NativeTrack track, Character* character, long now)
        {
            if (track.Active == null || track.Closing) return;
            if (_nativeColorDrawPass) _nativeColorStatus = $"Waiting for layout: {track.Active.Channel}";
            if (!MatchesLayoutText(character->YellBalloon.Text.ToString(), track.Active.Text))
            { RecordLayoutWait(track, now, $"Actor text mismatch (expected {track.Active.Text.Length}, actual {character->YellBalloon.Text.ToString().Length} chars)"); return; }
            string expected = NormalizeSpacing(track.Active.Text);
            // Identical active messages are ambiguous; leave their native layout untouched.
            if (_nativeTracks.Values.Count(t => t.Active != null && MatchesLayoutText(t.Active.Text, track.Active.Text)) != 1)
            { RecordLayoutWait(track, now, "Ambiguous active chat text"); return; }
            var address = (nint)Services.GameGui.GetAddonByName("_MiniTalk", 1);
            if (address == 0) return;
            var addon = (MiniTalk*)address;
            int matches = 0;
            AtkTextNode* target = null;
            AtkResNode* scaleRoot = null;
            AtkResNode* background = null;
            AtkResNode* tail = null;
            foreach (var entry in addon->TalkBubbles)
            {
                var text = entry.BubbleTextNode;
                var root = entry.BubbleResNode;
                if (text == null || root == null) continue;
                if (!root->IsVisible() || !text->IsVisible() || !MatchesLayoutText(NativeNodePlain(text), track.Active.Text))
                { track.PreexistingNodes.Remove((nint)text); continue; }
                matches++;
                if (track.PreexistingNodes.Contains((nint)text)) continue;
                target = text;
                scaleRoot = root;
                background = (AtkResNode*)entry.BubbleNineGridNode;
                tail = (AtkResNode*)entry.BubbleImageNode;
            }
            if (matches != 1 || target == null)
            { RecordLayoutWait(track, now, $"Matching nodes={matches}; eligible={target != null}; excluded={track.PreexistingNodes.Count}; expected chars={track.Active.Text.Length}"); return; }
            RestoreScaleForLayout(scaleRoot, target, background, tail, address);
            if (_nativeColorDrawPass)
            {
                ApplyNativeEmoteStyle(target, track.Active);
                EnsureMixedNativeText(target, track.Active);
            }
            var result = ApplyCompactLayout(target, background, tail, track.GrownInPlace);
            track.LayoutDone = !result.StartsWith("Unexpected", StringComparison.Ordinal);
            if (track.LayoutDone)
            {
                ApplyNativeScale(scaleRoot, target, background, tail, address);
                track.LastLayoutNode = (nint)target;
                track.LastLayoutAddon = address;
                track.LastLayoutAt = now;
                _recentNativeNodes[track.Active.Id] = (address, (nint)target, now);
                if (_nativeColorDrawPass)
                {
                    try { ApplyNativeColors(target, background, tail, track.Active.Channel); }
                    catch (Exception e) { _nativeColorStatus = "Color failed: " + e.GetType().Name; }
                }
            }
            if (track.LayoutDone) _nativeLayoutStatus = $"Applied ({track.Active.Channel}): {background->Width}x{background->Height}; text {target->Width}x{target->Height}; text Y={((AtkResNode*)target)->Y:F1}; background Y={background->Y:F1}; tall trim={(!track.GrownInPlace && target->Height >= 120 ? 23 : 0)}";
            else _nativeLayoutStatus = result;
        }
        // Measure candidate text without writing it into the visible node.
        private static void MeasureCandidate(AtkTextNode* node, string value, ushort* width, ushort* height)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");
            fixed (byte* ptr = bytes) node->GetTextDrawSize(width, height, ptr);
        }
        // Find the first displayed line using the native font and wrap width.
        private static int FirstWrappedLineEnd(AtkTextNode* node, string value, int singleLineHeight)
        {
            int newline = value.IndexOf('\n');
            string paragraph = newline < 0 ? value : value[..newline];
            if (paragraph.Length == 0) return Math.Min(value.Length, 1);
            var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(paragraph);
            int lo = 1, hi = boundaries.Length, fit = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                int end = mid == boundaries.Length ? paragraph.Length : boundaries[mid];
                ushort w = 0, h = 0;
                MeasureCandidate(node, paragraph[..end], &w, &h);
                if (h > 0 && h <= singleLineHeight && w <= node->Width)
                { fit = end; lo = mid + 1; }
                else hi = mid - 1;
            }
            if (fit >= paragraph.Length) return newline < 0 ? paragraph.Length : newline + 1;
            if (fit == 0) fit = boundaries.Length > 1 ? boundaries[1] : paragraph.Length;
            // Prefer a word boundary, as the native word wrapper does. For an
            // unbroken word, retain the measured grapheme boundary instead.
            if (fit < paragraph.Length && !char.IsWhiteSpace(paragraph[fit]))
            {
                int space = fit - 1;
                while (space > 0 && !char.IsWhiteSpace(paragraph[space])) space--;
                if (space > 0) fit = space + 1;
            }
            while (fit < paragraph.Length && char.IsWhiteSpace(paragraph[fit])) fit++;
            return fit;
        }
        private static NativeMessage BoundExpiredStack(NativeMessage item)
        {
            var parts = new List<string>(item.StackParts ?? new[] { item.Text });
            var styles = new List<bool>(item.StackItalics ?? new[] { IsNativeEmote(item.Channel) });
            // No live native measurement is available after retirement. Keep the
            // newest suffix, cutting on grapheme/word boundaries rather than
            // evicting a whole long message merely because of the character cap.
            while (parts.Count > 1 && string.Join("\n", parts).Length > MaxStackCharacters)
            {
                int excess = string.Join("\n", parts).Length - MaxStackCharacters;
                var first = parts[0];
                if (excess >= first.Length) { parts.RemoveAt(0); styles.RemoveAt(0); continue; }
                var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(first);
                int cut = boundaries.FirstOrDefault(b => b >= excess);
                if (cut == 0) { parts.RemoveAt(0); styles.RemoveAt(0); continue; }
                int space = first.IndexOf(' ', cut);
                if (space >= 0 && space - cut <= 24) cut = space + 1;
                parts[0] = first[cut..].TrimStart();
                if (parts[0].Length == 0) { parts.RemoveAt(0); styles.RemoveAt(0); }
            }
            return item with { Text = LimitNativeText(string.Join("\n", parts)), StackParts = parts.ToArray(), StackItalics = styles.ToArray() };
        }
        // Measure one visual line at a time. Candidate GetTextDrawSize does not
        // guarantee the same wrapping as SetText/ApplyTextFlow on the live node.
        private static NativeMessage FitRollingStack(AtkTextNode* node, NativeMessage next)
        {
            var parts = new List<string>();
            var styles = new List<bool>();
            var sources = next.StackParts ?? new[] { next.Text };
            for (int partIndex = 0; partIndex < sources.Length; partIndex++)
            {
                var lines = new List<string>();
                string remaining = sources[partIndex];
                while (remaining.Length > 0)
                {
                    int end = FirstWrappedLineEnd(node, remaining, 21);
                    if (end <= 0) break;
                    lines.Add(remaining[..end].TrimEnd());
                    remaining = remaining[end..].TrimStart(' ', '\t');
                }
                if (lines.Count > 0) { parts.Add(string.Join("\n", lines)); styles.Add(next.StackItalics is { } flags && partIndex < flags.Length ? flags[partIndex] : IsNativeEmote(next.Channel)); }
            }
            while (parts.Count > 0)
            {
                string joined = string.Join("\n", parts);
                if (joined.Length <= MaxStackCharacters && joined.Count(c => c == '\n') < MaxStackVisualLines)
                    return next with { Text = joined, StackParts = parts.ToArray(), StackItalics = styles.ToArray() };
                int split = parts[0].IndexOf('\n');
                if (split >= 0) parts[0] = parts[0][(split + 1)..];
                else if (parts.Count > 1) { parts.RemoveAt(0); styles.RemoveAt(0); }
                else return BoundExpiredStack(next);
            }
            return BoundExpiredStack(next);
        }
        private string _stackWaitReason = "No stack update yet.";
        private bool StackWait(string reason) { _stackWaitReason = reason; return false; }
        // Only mutate an already confirmed, uniquely matched, visible slot. Never
        // allocate a second bubble or change the actor attachment during a stack.
        private bool TryGrowNativeBubble(NativeTrack track, Character* character, NativeMessage next, long now)
        {
            if (track.Active == null || !track.LayoutDone || track.LastLayoutNode == 0) return StackWait("No confirmed active layout");
            if (!MatchesLayoutText(character->YellBalloon.Text.ToString(), track.Active.Text)) return StackWait("Actor text mismatch");
            if (ExistingMatchingNodes(track.Active.Text).Count != 1) return StackWait("Active UI text not uniquely matched");
            if (ExistingMatchingNodes(next.Text).Any(n => n != track.LastLayoutNode)) return StackWait("New text matches another visible node");
            if (_nativeTracks.Values.Count(t => t.Active != null && MatchesLayoutText(t.Active.Text, track.Active.Text)) != 1) return StackWait("Ambiguous active tracks");
            var address = (nint)Services.GameGui.GetAddonByName("_MiniTalk", 1);
            if (address == 0 || address != track.LastLayoutAddon) return StackWait("Addon changed");
            foreach (var entry in ((MiniTalk*)address)->TalkBubbles)
            {
                var text = entry.BubbleTextNode;
                var root = entry.BubbleResNode;
                var bg = (AtkResNode*)entry.BubbleNineGridNode;
                if (text == null || (nint)text != track.LastLayoutNode || root == null || bg == null ||
                    !root->IsVisible() || !text->IsVisible() || bg->NodeId != 5 || text->NodeId != 3 ||
                    !MatchesLayoutText(NativeNodePlain(text), track.Active.Text)) continue;
                RestoreScaleForLayout(root, text, bg, (AtkResNode*)entry.BubbleImageNode, address);
                var oldW = text->Width; var oldH = text->Height;
                var oldText = text->NodeText.AsSpan().ToArray();
                var oldFlags = text->TextFlags;
                // Measure candidates without modifying visible text. Use a
                // normal native wrapping width before fitting the compact geometry.
                const ushort wrapWidth = 192;
                text->SetWidth(wrapWidth);
                text->SetHeight(600);
                next = FitRollingStack(text, next);
                ushort w = 0, h = 0;
                SetNativeStackText(text, next);
                text->GetTextDrawSize(&w, &h);
                if (w == 0 || h == 0 || w > wrapWidth + 2 || h > 570)
                {
                    text->SetWidth(oldW); text->SetHeight(oldH); text->SetText(oldText); text->TextFlags = oldFlags;
                    return StackWait($"Live text measurement {w}x{h}; chars={next.Text.Length}; lines={next.Text.Count(c => c == '\n') + 1}");
                }
                // Do not narrow a multiline wrap box if doing so changes the line count.
                text->SetWidth((ushort)Math.Clamp((int)w + 2, 12, (int)wrapWidth));
                ushort fittedW = 0, fittedH = 0;
                text->GetTextDrawSize(&fittedW, &fittedH);
                if (fittedH != h || fittedW > text->Width) text->SetWidth(wrapWidth);
                // Explicit line breaks must fit even if native measurement underreports.
                int lineFloor = 17 + 14 * next.Text.Count(c => c == '\n');
                int safeHeight = Math.Max((int)h, lineFloor) + (h <= 21 ? 4 : 10);
                if (safeHeight > 590)
                {
                    text->SetWidth(oldW); text->SetHeight(oldH); text->SetText(oldText); text->TextFlags = oldFlags;
                    return StackWait($"Safe height exceeded: {safeHeight}");
                }
                text->SetHeight((ushort)safeHeight);
                character->YellBalloon.Text.SetString(next.Text);
                character->YellBalloon.PlayTimer = (float)Math.Clamp(_timer, 1, 60);
                track.Active = next;
                track.GrownInPlace = true;
                track.OpenedAt = now;
                track.PreexistingNodes.Remove((nint)text);
                ApplyActiveNativeSpacing(track, character, now);
                _stackWaitReason = "In-place update committed";
                return true;
            }
            return StackWait("Previously owned text node not visible or changed");
        }
        private string _nativeLayoutStatus = "Waiting for native chat.";
        private string _nativePersistentFailure = "None recorded.";
        private void RecordLayoutWait(NativeTrack track, long now, string reason)
        {
            _nativeLayoutStatus = "Waiting: " + reason;
            if (now - track.OpenedAt >= 1000)
                _nativePersistentFailure = $"{track.Active?.Channel}: {reason}; age={now - track.OpenedAt}ms; previously applied={track.LayoutDone}";
        }
        private static string CompactMatchKey(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c) && c != '\0' && c != '\u200b' && c != '\u00ad'));
        private static bool MatchesNativeText(string actual, string expected)
        {
            var a = CompactMatchKey(actual);
            var b = CompactMatchKey(expected);
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            // Game may truncate long strings; only accept a substantial matching prefix.
            a = a.TrimEnd('…');
            if (a.EndsWith("...", StringComparison.Ordinal)) a = a[..^3];
            return a.Length >= 48 && a.Length < b.Length && b.StartsWith(a, StringComparison.Ordinal);
        }
        // Layout matching only: retain strict ownership checks for closing balloons.
        private static string LayoutMatchKey(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && c != '\u200b' && c != '\u00ad' && c != '\ufeff')
            .Select(c => c switch { '\u2018' or '\u2019' => '\'', '\u201c' or '\u201d' => '"', _ => c }));
        private static bool MatchesLayoutText(string actual, string expected)
        {
            if (MatchesNativeText(actual, expected)) return true;
            var a = LayoutMatchKey(actual);
            var b = LayoutMatchKey(expected);
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            // Require a substantial shared prefix, with uniqueness checks at both call sites.
            // A long game-rendered message may be truncated or transformed after the prefix.
            const int prefixLength = 96;
            return a.Length >= prefixLength && b.Length >= prefixLength &&
                a.AsSpan(0, prefixLength).SequenceEqual(b.AsSpan(0, prefixLength));
        }
        // During retirement, the actor's balloon text may already be cleared.
        // Only style the previously confirmed, still-visible UI node, never a new match.
        private void ApplyRetiringNativeStyle(NativeTrack track)
        {
            if (track.Active == null || !track.LayoutDone || track.LastLayoutNode == 0) return;
            var address = (nint)Services.GameGui.GetAddonByName("_MiniTalk", 1);
            if (address == 0 || address != track.LastLayoutAddon) return;
            if (_nativeTracks.Values.Count(t => t.Active != null && MatchesLayoutText(t.Active.Text, track.Active.Text)) != 1) return;
            var addon = (MiniTalk*)address;
            if (ExistingMatchingNodes(track.Active.Text).Count != 1) return;
            foreach (var entry in addon->TalkBubbles)
            {
                var text = entry.BubbleTextNode;
                var root = entry.BubbleResNode;
                if (text == null || (nint)text != track.LastLayoutNode || root == null ||
                    !root->IsVisible() || !text->IsVisible() ||
                    !MatchesLayoutText(NativeNodePlain(text), track.Active.Text)) continue;
                var background = (AtkResNode*)entry.BubbleNineGridNode;
                var tail = (AtkResNode*)entry.BubbleImageNode;
                RestoreScaleForLayout(root, text, background, tail, address);
                ApplyNativeEmoteStyle(text, track.Active);
                EnsureMixedNativeText(text, track.Active);
                var result = ApplyCompactLayout(text, background, tail, track.GrownInPlace);
                if (!result.StartsWith("Unexpected", StringComparison.Ordinal))
                {
                    ApplyNativeScale(root, text, background, tail, address);
                    ApplyNativeColors(text, background, tail, track.Active.Channel);
                }
                return;
            }
        }
        private void NativeLayoutPreDraw(AddonEvent type, AddonArgs args)
        {
            RestoreNativeColors();
            if (!_switch)
            { RestoreNativeScales(); return; }
            _nativeColorDrawPass = true;
            try
            {
                lock (_chatLock)
                {
                    MaintainNativeScales();
                    foreach (var track in _nativeTracks.Values)
                    {
                        if (track.Active == null) continue;
                        var item = track.Active;
                        var actor = Services.ObjectTable.OfType<IPlayerCharacter>().FirstOrDefault(p => p.EntityId == item.Id && p.Name.TextValue == item.Name);
                        if (actor == null || actor.Address == nint.Zero) continue;
                        var character = (Character*)actor.Address;
                        if (character->YellBalloon.Character != character) continue;
                        if (track.Closing || character->YellBalloon.State == FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloonState.Inactive ||
                            !MatchesLayoutText(character->YellBalloon.Text.ToString(), item.Text))
                        {
                            ApplyRetiringNativeStyle(track);
                            continue;
                        }
                        ApplyActiveNativeSpacing(track, character, Environment.TickCount64);
                    }
                }
            }
            catch (Exception e) { _nativeLayoutStatus = "Layout failed: " + e.GetType().Name; }
            finally { _nativeColorDrawPass = false; }
        }
        private static string NormalizeSpacing(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }
}
