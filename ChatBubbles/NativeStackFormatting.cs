using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace ChatBubbles
{
    public unsafe partial class ChatBubbles
    {
        private static string NativeNodePlain(AtkTextNode* node)
        {
            var bytes = node->NodeText.AsSpan();
            return bytes.IndexOf((byte)2) < 0 ? node->NodeText.ToString() : SeString.Parse(bytes).TextValue;
        }
        private static bool MixedNativeStyle(NativeMessage message) =>
            message.StackParts is { } parts && message.StackItalics is { } flags &&
            parts.Length == flags.Length && flags.Any(x => x) && flags.Any(x => !x) &&
            string.Join("\n", parts) == message.Text;
        private static byte[] EncodeMixedNativeText(NativeMessage message)
        {
            var payloads = new List<Payload>();
            for (int i = 0; i < message.StackParts!.Length; i++)
            {
                if (i > 0) payloads.Add(new TextPayload("\n"));
                payloads.Add(new EmphasisItalicPayload(message.StackItalics![i]));
                payloads.Add(new TextPayload(message.StackParts[i]));
                payloads.Add(EmphasisItalicPayload.ItalicsOff);
            }
            return new SeString(payloads).Encode();
        }
        private static void SetNativeStackText(AtkTextNode* text, NativeMessage message)
        {
            bool italic = message.StackItalics is { Length: > 0 } flags ? flags.All(x => x) : IsNativeEmote(message.Channel);
            text->TextFlags = italic ? text->TextFlags | TextFlags.Italic : text->TextFlags & ~TextFlags.Italic;
            if (MixedNativeStyle(message)) text->SetText(EncodeMixedNativeText(message));
            else text->SetText(message.Text);
        }
        private static void EnsureMixedNativeText(AtkTextNode* text, NativeMessage message)
        {
            if (!MixedNativeStyle(message)) return;
            var bytes = EncodeMixedNativeText(message);
            if (!text->NodeText.AsSpan().SequenceEqual(bytes)) text->SetText(bytes);
        }
    }
}
