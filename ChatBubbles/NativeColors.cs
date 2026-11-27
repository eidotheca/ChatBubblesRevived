using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MiniTalk = FFXIVClientStructs.FFXIV.Client.UI.AddonMiniTalk;

namespace ChatBubbles
{
    public unsafe partial class ChatBubbles
    {
        private bool _nativeColorDrawPass;
        private string _nativeColorStatus = "Waiting for matched chat.";
        private nint _nativeColorAddon;
        private readonly Dictionary<nint, bool> _nativeItalicSaved = new();
        private static bool IsNativeEmote(XivChatType channel) => channel is XivChatType.CustomEmote or XivChatType.StandardEmote;
        private void ApplyNativeEmoteStyle(AtkTextNode* text, NativeMessage message)
        {
            if (text == null || text->NodeId != 3) return;
            bool italic = message.StackItalics is { Length: > 0 } styles ? System.Array.TrueForAll(styles, x => x) : IsNativeEmote(message.Channel);
            bool original = (text->TextFlags & TextFlags.Italic) != 0;
            if (italic == original) return;
            _nativeColorAddon = (nint)Services.GameGui.GetAddonByName("_MiniTalk", 1);
            _nativeItalicSaved.TryAdd((nint)text, original);
            text->TextFlags = italic ? text->TextFlags | TextFlags.Italic : text->TextFlags & ~TextFlags.Italic;
            ((AtkResNode*)text)->DrawFlags |= 1u;
        }
        private readonly Dictionary<nint, NodeColors> _nativeColorSaved = new();
        private readonly Dictionary<nint, (byte R, byte G, byte B)> _nativeTextSaved = new();
        private readonly record struct NodeColors(byte R, byte G, byte B, short AR, short AG, short AB);
        private static byte BlendNative(byte original, float value, float strength) =>
            (byte)Math.Clamp(MathF.Round(original + (255f * value - original) * strength), 0f, 255f);
        private static short AddNative(short original, float value, float strength) =>
            (short)Math.Clamp(MathF.Round(original + 255f * value * strength), -255f, 255f);
        private void ColorNativeSurface(AtkResNode* node, Vector4 bg, Vector4 tint)
        {
            if (node == null || (bg.W <= 0 && tint.W <= 0)) return;
            var key = (nint)node;
            if (!_nativeColorSaved.TryAdd(key, new NodeColors(node->Color.R, node->Color.G, node->Color.B, node->AddRed, node->AddGreen, node->AddBlue))) return;
            var original = _nativeColorSaved[key];
            // Blend tint over the base modulation rather than adding positive RGB to
            // a nearly white texture (which clips to white and hides the tint).
            node->Color.R = BlendNative(BlendNative(original.R, bg.X, bg.W), tint.X, tint.W);
            node->Color.G = BlendNative(BlendNative(original.G, bg.Y, bg.W), tint.Y, tint.W);
            node->Color.B = BlendNative(BlendNative(original.B, bg.Z, bg.W), tint.Z, tint.W);
            node->DrawFlags |= 1u;
        }
        private void ApplyNativeColors(AtkTextNode* text, AtkResNode* bg, AtkResNode* tail, XivChatType channel)
        {
            int i = _order.IndexOf(channel);
            if (i < 0 || i >= _bubbleColours.Count || i >= _bubbleColours2.Count || i >= _textColour.Length) return;
            if (text == null || bg == null || tail == null || bg->NodeId != 5 || tail->NodeId != 4 || ((AtkResNode*)text)->NodeId != 3) return;
            _nativeColorAddon = (nint)Services.GameGui.GetAddonByName("_MiniTalk", 1);
            var custom = Vector4.Clamp(_bubbleColours[i], Vector4.Zero, Vector4.One);
            var tint = Vector4.Clamp(_bubbleColours2[i], Vector4.Zero, Vector4.One);
            ColorNativeSurface(bg, custom, tint);
            ColorNativeSurface(tail, custom, tint);
            var choice = _textColour[i];
            _nativeColorStatus = $"{channel}: tint strength={tint.W:F2}, background strength={custom.W:F2}, text override={choice != null && (choice.Choice != 0 || choice.Option != 0)}";
            if (choice != null && (choice.Choice != 0 || choice.Option != 0) &&
                _nativeTextSaved.TryAdd((nint)text, (text->TextColor.R, text->TextColor.G, text->TextColor.B)))
            {
                var c = BitConverter.GetBytes(choice.Choice);
                text->TextColor.R = c[3]; text->TextColor.G = c[2]; text->TextColor.B = c[1];
                ((AtkResNode*)text)->DrawFlags |= 1u;
            }
        }
        private void RestoreNativeSurface(AtkResNode* node)
        {
            if (node == null || !_nativeColorSaved.Remove((nint)node, out var c)) return;
            node->Color.R = c.R; node->Color.G = c.G; node->Color.B = c.B;
            node->AddRed = c.AR; node->AddGreen = c.AG; node->AddBlue = c.AB;
            node->DrawFlags |= 1u;
        }
        private void RestoreNativeColors()
        {
            if (_nativeColorSaved.Count == 0 && _nativeTextSaved.Count == 0 && _nativeItalicSaved.Count == 0) return;
            try
            {
                var address = (nint)Services.GameGui.GetAddonByName("_MiniTalk", 1);
                if (address == 0 || address != _nativeColorAddon) return;
                foreach (var entry in ((MiniTalk*)address)->TalkBubbles)
                {
                    RestoreNativeSurface((AtkResNode*)entry.BubbleNineGridNode);
                    RestoreNativeSurface((AtkResNode*)entry.BubbleImageNode);
                    var text = entry.BubbleTextNode;
                    if (text != null && _nativeItalicSaved.Remove((nint)text, out var italic))
                    { text->TextFlags = italic ? text->TextFlags | TextFlags.Italic : text->TextFlags & ~TextFlags.Italic; ((AtkResNode*)text)->DrawFlags |= 1u; }
                    if (text != null && _nativeTextSaved.Remove((nint)text, out var c))
                    { text->TextColor.R = c.R; text->TextColor.G = c.G; text->TextColor.B = c.B; ((AtkResNode*)text)->DrawFlags |= 1u; }
                }
            }
            finally { _nativeColorSaved.Clear(); _nativeTextSaved.Clear(); _nativeItalicSaved.Clear(); _nativeColorAddon = 0; }
        }
        // PostDraw can run before the renderer consumes the node colors.
        // Keep the modulation until the next layout pass instead.
        private void NativeColorsPostDraw(AddonEvent type, AddonArgs args) { }
    }
}
