using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MiniTalk = FFXIVClientStructs.FFXIV.Client.UI.AddonMiniTalk;

namespace ChatBubbles
{
    public unsafe partial class ChatBubbles
    {
        private readonly record struct ChildTransform(nint Node, float X, float Y, float SX, float SY, float AX, float AY, float ASX, float ASY);
        private sealed record NativeScaleSnapshot(nint TextNode, string Text, ChildTransform TextTransform, ChildTransform Background, ChildTransform Tail, long SeenAt);
        private readonly Dictionary<nint, NativeScaleSnapshot> _nativeScales = new();
        private nint _nativeScaleAddon;
        private string _nativeScaleStatus = "Waiting for matched chat.";
        private static float NormalizeNativeScale(float value) => float.IsFinite(value) ? Math.Clamp(value, 0.5f, 2f) : 1f;
        private static bool SameScale(float a, float b) => Math.Abs(a - b) < 0.001f;

        private static void RestoreChild(AtkResNode* node, ChildTransform old)
        {
            if (node == null || (nint)node != old.Node) return;
            float x = SameScale(node->X, old.AX) ? old.X : node->X;
            float y = SameScale(node->Y, old.AY) ? old.Y : node->Y;
            float sx = SameScale(node->ScaleX, old.ASX) ? old.SX : node->ScaleX;
            float sy = SameScale(node->ScaleY, old.ASY) ? old.SY : node->ScaleY;
            if (!SameScale(x,node->X) || !SameScale(y,node->Y)) node->SetPositionFloat(x,y);
            if (!SameScale(sx,node->ScaleX) || !SameScale(sy,node->ScaleY)) node->SetScale(sx,sy);
            node->DrawFlags |= 1u;
        }
        private static ChildTransform ScaleChild(AtkResNode* node, float factor)
        {
            float x=node->X,y=node->Y,sx=node->ScaleX,sy=node->ScaleY;
            // Scale about the parent coordinate origin, accounting for each child's own pivot.
            float ax=(x+node->OriginX)*factor-node->OriginX;
            float ay=(y+node->OriginY)*factor-node->OriginY;
            float asx=sx*factor,asy=sy*factor;
            if (!SameScale(x,ax) || !SameScale(y,ay)) node->SetPositionFloat(ax,ay);
            if (!SameScale(sx,asx) || !SameScale(sy,asy)) node->SetScale(asx,asy);
            node->DrawFlags |= 1u;
            return new ChildTransform((nint)node,x,y,sx,sy,ax,ay,asx,asy);
        }
        private void RestoreScaleForLayout(AtkResNode* root, AtkTextNode* text, AtkResNode* bg, AtkResNode* tail, nint addon)
        {
            if (addon != _nativeScaleAddon || root == null || !_nativeScales.Remove((nint)root,out var old)) return;
            RestoreChild((AtkResNode*)text,old.TextTransform);
            RestoreChild(bg,old.Background);
            RestoreChild(tail,old.Tail);
        }
        private void ApplyNativeScale(AtkResNode* root, AtkTextNode* text, AtkResNode* bg, AtkResNode* tail, nint addon)
        {
            if (root == null || text == null || bg == null || tail == null || addon == 0) return;
            if (_nativeScaleAddon != addon) { _nativeScales.Clear(); _nativeScaleAddon=addon; }
            RestoreScaleForLayout(root,text,bg,tail,addon);
            float factor=NormalizeNativeScale(_bubbleSize);
            if (factor==1f) { _nativeScaleStatus="Default 1.00x; native animation untouched."; return; }
            var tn=(AtkResNode*)text;
            // Only transform siblings directly beneath this bubble's validated root.
            // Refuse unknown hierarchies rather than scaling unrelated UI or scaling twice.
            if (tn->ParentNode != root || bg->ParentNode != root || tail->ParentNode != root || tn==bg || tn==tail || bg==tail)
            { _nativeScaleStatus=$"Unsupported hierarchy: root {root->NodeId}; parents text={(tn->ParentNode==root)}, bg={(bg->ParentNode==root)}, tail={(tail->ParentNode==root)}"; return; }
            var t=ScaleChild(tn,factor); var b=ScaleChild(bg,factor); var p=ScaleChild(tail,factor);
            _nativeScales[(nint)root]=new NativeScaleSnapshot((nint)text,NativeNodePlain(text),t,b,p,Environment.TickCount64);
            _nativeScaleStatus=$"Child scale {factor:F2}x; text {tn->ScaleX:F2},{tn->ScaleY:F2}; parent animation {root->ScaleX:F2},{root->ScaleY:F2}";
        }
        private void MaintainNativeScales()
        {
            if (_nativeScales.Count==0) return;
            var address=(nint)Services.GameGui.GetAddonByName("_MiniTalk",1);
            if (address==0 || address!=_nativeScaleAddon) { _nativeScales.Clear(); _nativeScaleAddon=0; return; }
            var seen=new HashSet<nint>();
            foreach(var entry in ((MiniTalk*)address)->TalkBubbles)
            {
                var root=entry.BubbleResNode; var text=entry.BubbleTextNode;
                var bg=(AtkResNode*)entry.BubbleNineGridNode; var tail=(AtkResNode*)entry.BubbleImageNode;
                if(root==null || !_nativeScales.TryGetValue((nint)root,out var old)) continue;
                seen.Add((nint)root);
                // Root visibility covers the fade, even if the game's text node is hidden first.
                if(root->IsVisible() && text!=null && (nint)text==old.TextNode &&
                    string.Equals(NativeNodePlain(text),old.Text,StringComparison.Ordinal))
                { ApplyNativeScale(root,text,bg,tail,address); continue; }
                RestoreScaleForLayout(root,text,bg,tail,address);
            }
            foreach(var key in new List<nint>(_nativeScales.Keys)) if(!seen.Contains(key)) _nativeScales.Remove(key);
        }
        private void RestoreNativeScales()
        {
            try
            {
                var address=(nint)Services.GameGui.GetAddonByName("_MiniTalk",1);
                if(address==0 || address!=_nativeScaleAddon) return;
                foreach(var entry in ((MiniTalk*)address)->TalkBubbles)
                    RestoreScaleForLayout(entry.BubbleResNode,entry.BubbleTextNode,(AtkResNode*)entry.BubbleNineGridNode,(AtkResNode*)entry.BubbleImageNode,address);
            }
            finally { _nativeScales.Clear(); _nativeScaleAddon=0; }
        }
    }
}
