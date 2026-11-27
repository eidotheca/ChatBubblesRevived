using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace ChatBubbles
{
    public unsafe partial class ChatBubbles
    {
        // Attachment IDs used by the original ChatBubbles implementation.
        private static readonly byte[] NativeChaosAttachments = { 1, 6, 7, 8, 9, 10, 11, 25, 28, 30, 32, 33, 34, 35, 43, 44, 45, 48, 49, 50, 51, 52, 53, 54, 55, 62, 63, 64 };
        private sealed record NativeMessage(uint Id, string Name, string Text, XivChatType Channel, long Time)
        {
            public string[]? StackParts { get; init; }
            public bool[]? StackItalics { get; init; }
        }
        private const int MaxStackMessages = 18;
        private const int MaxStackCharacters = 500;
        private const int MaxStackVisualLines = 18;
        private sealed class NativeTrack
        {
            public Queue<NativeMessage> Pending = new();
            public NativeMessage? Active;
            public NativeMessage? Replacement;
            public bool Closing;
            public bool InPlaceStack;
            public bool GrownInPlace;
            public bool FirstAfterModeChange;
            public bool LayoutDone;
            public long OpenedAt;
            public long ClosedAt;
            public long IdleSince;
            public bool ObservedActive;
            public HashSet<nint> PreexistingNodes = new();
            public nint LastLayoutNode;
            public nint LastLayoutAddon;
            public long LastLayoutAt;
        }
        private readonly Dictionary<uint, NativeTrack> _nativeTracks = new();
        private int _nativeMode = -1;
        // Keep recent confirmed ownership across track removal/recreation.
        private readonly Dictionary<uint, (nint Addon, nint Node, long At)> _recentNativeNodes = new();
        private void ClearNativeChat() { _nativeTracks.Clear(); _recentNativeNodes.Clear(); }
        // Keep ownership of the outgoing native node until it has actually retired.
        // Only discard work queued under the old mode, not the visible bubble.
        private void SyncNativeMode()
        {
            if (_nativeMode == _bubbleFunctionality) return;
            foreach (var track in _nativeTracks.Values)
            {
                track.Pending.Clear();
                track.Replacement = null;
                track.InPlaceStack = false;
                track.FirstAfterModeChange = true;
            }
            _nativeMode = _bubbleFunctionality;
        }
        private void QueueNativeChat(uint id, string name, string text, XivChatType channel)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            SyncNativeMode();
            if (!_nativeTracks.TryGetValue(id, out var track))
            {
                if (_nativeTracks.Count >= 32) return;
                _nativeTracks[id] = track = new NativeTrack();
            }
            var item = new NativeMessage(id, name, LimitNativeText(text.Replace("\0", "").Trim()), channel, Environment.TickCount64);
            var current = track.Replacement ?? track.Active;
            bool firstAfterChange = track.FirstAfterModeChange;
            track.FirstAfterModeChange = false;
            // The first eligible message bypasses the old timer. Stack (and
            // same-channel Sensible) still combine with a live outgoing message.
            if (firstAfterChange && (track.Closing || current == null ||
                (_bubbleFunctionality != 1 && !(_bubbleFunctionality == 3 && current.Channel == channel))))
            {
                track.InPlaceStack = false;
                track.Replacement = item;
                return;
            }
            if (_bubbleFunctionality == 2)
            { track.Pending.Clear(); track.InPlaceStack = false; track.Replacement = item; }
            else if (current != null && (_bubbleFunctionality == 1 || (_bubbleFunctionality == 3 && current.Channel == channel && track.Pending.Count == 0)))
            {
                var parts = new List<string>(current.StackParts ?? new[] { current.Text });
                var styles = new List<bool>(current.StackItalics ?? new[] { IsNativeEmote(current.Channel) });
                parts.Add(item.Text); styles.Add(IsNativeEmote(item.Channel));
                // The message-count cap is separate from the visual line cap.
                // Defer character/line trimming until the native font can measure the actual wrapping.
                while (parts.Count > MaxStackMessages) { parts.RemoveAt(0); styles.RemoveAt(0); }
                track.Replacement = current with { Text = string.Join("\n", parts), Time = item.Time, StackParts = parts.ToArray(), StackItalics = styles.ToArray() };
                track.InPlaceStack = true;
            }
            else if (track.Pending.Count < Math.Clamp(_queue, 1, 20)) track.Pending.Enqueue(item);
        }
        private void ProcessNativeChat()
        {
            lock (_chatLock)
            {
                if (!_switch)
                { ClearNativeChat(); return; }
                var local = Services.ObjectTable.LocalPlayer;
                if (local == null) { ClearNativeChat(); return; }
                SyncNativeMode();
                var now = Environment.TickCount64;
                foreach (var old in _recentNativeNodes.Where(x => now - x.Value.At > 10000).Select(x => x.Key).ToArray()) _recentNativeNodes.Remove(old);
                foreach (var pair in _nativeTracks.ToArray())
                {
                    var track = pair.Value;
                    var next = track.Replacement ?? (track.Pending.Count > 0 ? track.Pending.Peek() : track.Active);
                    if (next == null) { _nativeTracks.Remove(pair.Key); continue; }
                    var actor = Services.ObjectTable.OfType<IPlayerCharacter>().FirstOrDefault(p => p.EntityId == next.Id && p.Name.TextValue == next.Name);
                    if (actor == null || actor.Address == nint.Zero || (_hide && actor.EntityId == local.EntityId) ||
                        Vector3.Distance(local.Position, actor.Position) > Math.Max(1, _yalmCap) || !PassNativeSocialFilters(actor, local))
                    { _nativeTracks.Remove(pair.Key); continue; }
                    var character = (Character*)actor.Address;
                    if (character->YellBalloon.Character != character) { _nativeTracks.Remove(pair.Key); continue; }
                    bool idle = character->YellBalloon.State == FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloonState.Inactive;
                    if (!idle)
                    {
                        track.IdleSince = 0;
                        if (character->YellBalloon.State == FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloonState.Active) track.ObservedActive = true;
                        // Repeated identical messages can change channel/color without allocating a new game slot.
                        // Preserve the existing validated layout association and refresh the active duration.
                        if (track.Replacement is { } repeated && track.Active is { } active && !track.Closing &&
                            string.Equals(repeated.Text, active.Text, StringComparison.Ordinal) &&
                            MatchesNativeText(character->YellBalloon.Text.ToString(), active.Text) &&
                            character->YellBalloon.State == FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloonState.Active)
                        {
                            track.Active = repeated;
                            track.InPlaceStack = false;
                            track.Replacement = null;
                            character->YellBalloon.PlayTimer = (float)Math.Clamp(_timer, 1, 60);
                            _nativeCaptureStatus = $"Updated existing native balloon: {repeated.Channel}.";
                        }
                        if (track.InPlaceStack && track.Replacement is { } stacked && track.Active != null && !track.Closing &&
                            character->YellBalloon.State == FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloonState.Active &&
                            TryGrowNativeBubble(track, character, stacked, now))
                        {
                            track.InPlaceStack = false;
                            track.Replacement = null;
                            _nativeCaptureStatus = "Stacked in existing native balloon.";
                        }
                        // Keep a pending stack in place rather than repeatedly closing the balloon.
                        if (track.InPlaceStack && track.Replacement != null && !track.Closing)
                        {
                            // Do not force-close a live stack just because a measurement or
                            // ownership check failed. Retry while it is visible; pending text
                            // can open normally after the native balloon expires.
                            if (track.Active != null && now - track.Replacement.Time < 750 &&
                                character->YellBalloon.State == FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloonState.Active &&
                                MatchesNativeText(character->YellBalloon.Text.ToString(), track.Active.Text))
                                character->YellBalloon.PlayTimer = Math.Max(character->YellBalloon.PlayTimer, 0.75f);
                            _nativeCaptureStatus = $"Stack update pending ({now - track.Replacement.Time}ms); retaining existing balloon. Reason: {_stackWaitReason}";
                            ApplyActiveNativeSpacing(track, character, now);
                            continue;
                        }
                        // Only close a balloon whose text matches our own last request. Never close an unrelated game balloon.
                        if (track.Replacement != null && track.Active != null && !track.Closing &&
                            MatchesNativeText(character->YellBalloon.Text.ToString(), track.Active.Text))
                        {
                            character->YellBalloon.CloseBalloon();
                            track.Closing = true;
                            track.InPlaceStack = false;
                            track.ClosedAt = now;
                            _nativeCaptureStatus = "Replacing native balloon; waiting for idle.";
                        }
                        // Native layout is also reapplied before drawing.
                        if (!track.Closing) ApplyActiveNativeSpacing(track, character, now);
                        continue;
                    }
                    // Inactive is not proof that the old UI slot has finished fading out.
                    // Allow the native pool to retire the old slot before requesting a new one.
                    if (track.Active != null && !track.ObservedActive && !track.Closing && now - track.OpenedAt < 1000)
                        continue;
                    if (track.Closing || (track.Active != null && track.ObservedActive))
                    {
                        if (track.IdleSince == 0) track.IdleSince = now;
                        if (now - track.IdleSince < 120) continue;
                        if (now - track.IdleSince < 500 && ExistingMatchingNodes(track.Active?.Text ?? "").Count > 0)
                            continue;
                    }
                    track.Active = null; track.Closing = false;
                    track.IdleSince = 0;
                    NativeMessage? item = track.Replacement;
                    track.Replacement = null;
                    track.InPlaceStack = false;
                    while (item == null && track.Pending.TryDequeue(out var queued))
                        if (now - queued.Time <= 120000 && _channels.Contains(queued.Channel)) item = queued;
                    if (item == null || !_channels.Contains(item.Channel) || now - item.Time > 120000)
                    { if (track.Pending.Count == 0) _nativeTracks.Remove(pair.Key); continue; }
                    // A pending stack can outlive its native node. Bound its text even
                    // when no live text node is available to measure wrapped lines.
                    if (item.StackParts != null) item = BoundExpiredStack(item);
                    track.PreexistingNodes = ExistingMatchingNodes(item.Text);
                    // The native pool can reuse the same visible slot when replacing identical text.
                    // Only exempt a recently matched slot from this same speaker's track.
                    var layoutAddon = (nint)Services.GameGui.GetAddonByName("_MiniTalk", 1);
                    if (layoutAddon != 0 && layoutAddon == track.LastLayoutAddon &&
                        now - track.LastLayoutAt <= 3000 && track.LastLayoutNode != 0)
                        track.PreexistingNodes.Remove(track.LastLayoutNode);
                    if (_recentNativeNodes.TryGetValue(item.Id, out var owned) && owned.Addon == layoutAddon && now - owned.At <= 10000)
                        track.PreexistingNodes.Remove(owned.Node);
                    track.LayoutDone = false;
                    track.GrownInPlace = false;
                    track.ObservedActive = false;
                    track.OpenedAt = now;
                    byte attachment = _assBubbles ? (byte)63 : _chaosMode ? NativeChaosAttachments[Random.Shared.Next(NativeChaosAttachments.Length)] : (byte)25;
                    character->YellBalloon.OpenBalloon(item.Text, (float)Math.Clamp(_timer, 1, 60), false, 0f, false, false, false, attachment);
                    track.Active = item;
                    _nativeCaptureStatus = "Native chat balloon requested.";
                }
            }
        }
        private string _nativeCaptureStatus = "Ready.";
        private long _nextDebugStatus;
        private void NativeCaptureUpdate(IFramework framework)
        {
            try { ProcessNativeChat(); }
            catch (Exception e) { _nativeCaptureStatus = "Native chat failed: " + e.GetType().Name; lock (_chatLock) ClearNativeChat(); }
            if (_debug && Environment.TickCount64 >= _nextDebugStatus)
            {
                _nextDebugStatus = Environment.TickCount64 + 5000;
                Services.PluginLog.Debug($"Chat Bubbles: {_speakerResult} | {_nativeCaptureStatus} | {_nativeLayoutStatus} | {_nativeColorStatus} | {_nativeScaleStatus} | Last layout failure: {_nativePersistentFailure}");
            }
        }
    }
}
