using Dalamud.Game.Text;
using Dalamud.Game.Chat;
using System.Linq;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.ClientState.Objects.SubKinds;
using System;

namespace ChatBubbles
{
    public unsafe partial class ChatBubbles
    {
        private readonly object _chatLock = new();
        private string _speakerResult = "No eligible message yet";
        private void ProcessSpeaker(IHandleableChatMessage message, XivChatType kind)
        {
            bool party = kind == XivChatType.Party || kind == XivChatType.CrossParty;
            if (message.IsHandled || !_channels.Contains(kind) || !_order.Contains(kind) || kind == XivChatType.None)
            {
                _speakerResult = message.IsHandled ? "Skipped: already handled" :
                    !_order.Contains(kind) || kind == XivChatType.None ? "Skipped: unsupported channel" : "Skipped: channel disabled";
                if (party && _debug) Services.PluginLog.Information($"Chat Bubbles party: {kind}, handled={message.IsHandled}, enabled={_channels.Contains(kind)}");
                return;
            }
            try
            {
                var local = Services.ObjectTable.LocalPlayer;
                var payload = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
                if (kind == XivChatType.StandardEmote)
                    payload ??= message.Message.Payloads.OfType<PlayerPayload>().FirstOrDefault();
                var name = payload?.PlayerName ?? new string(message.Sender.ToString().Where(c => c < '\uE08F' || c > '\uE0AE').ToArray()).Trim();
                if (kind == XivChatType.Echo) name = local?.Name.TextValue ?? string.Empty;
                if (kind == XivChatType.TellOutgoing) name = local?.Name.TextValue ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) { _speakerResult = "No sender name"; return; }
                if (party && payload == null)
                {
                    name = new string(name.Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.PrivateUse && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray()).Trim();
                    name = System.Text.RegularExpressions.Regex.Replace(name, @"^\s*(?:[\[\(]?[1-8][\]\)]?[:.\-]?\s+)", "").Trim();
                }
                var matches = Services.ObjectTable.OfType<IPlayerCharacter>().Where(p => p.Name.TextValue == name).ToArray();
                if (party && _debug) Services.PluginLog.Information($"Chat Bubbles party: {kind}, payload={payload != null}, visible matches={matches.Length}");
                if (matches.Length != 1) { _speakerResult = $"Visible player matches: {matches.Length}"; return; }
                var actor = matches[0];
                var isSelf = local != null && actor.EntityId == local.EntityId;
                if (local == null || !PassNativeSocialFilters(actor, local))
                { _speakerResult = "Speaker matched | blocked by social filter"; return; }
                if (_switch && !(isSelf && _hide))
                {
                    var text = message.Message.ToString();
                    if (kind == XivChatType.CustomEmote && !text.StartsWith(actor.Name.TextValue, StringComparison.Ordinal))
                        text = actor.Name.TextValue + " " + text.TrimStart();
                    QueueNativeChat(actor.EntityId, actor.Name.TextValue, text, kind);
                    _speakerResult = $"{(isSelf ? "Self" : "Other player")} matched | {(payload != null ? "player payload" : "plain name")} | {(string.IsNullOrWhiteSpace(text) ? "empty text" : "passed to native queue")}";
                }
                else _speakerResult = !_switch ? "Skipped: bubbles disabled" : "Skipped: Hide Your Chat";
            }
            catch (Exception e) { _speakerResult = "Speaker processing error: " + e.GetType().Name; }
        }
        private void OnChatMessage(IHandleableChatMessage message)
        {
            var kind = (XivChatType)message.LogKind;
            lock (_chatLock)
            {
                ProcessSpeaker(message, kind);
                if (_debug)
                {
                    Services.PluginLog.Debug($"Chat Bubbles: {kind} ({(int)kind}) | handled={message.IsHandled} | enabled={_channels.Contains(kind)} | {_speakerResult}");
                }
            }
        }
    }
}
