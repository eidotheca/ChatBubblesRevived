using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Enums;
namespace ChatBubbles { public unsafe partial class ChatBubbles {
        private static string LimitNativeText(string text)
        {
            if (text.Length <= 500) return text;
            int end = 499;
            if (char.IsHighSurrogate(text[end - 1])) end--;
            return text[..end] + "…";
        }
        private bool PassNativeSocialFilters(IPlayerCharacter actor, IPlayerCharacter local)
        {
            if (actor.EntityId == local.EntityId || Services.DutyState.IsDutyStarted) return true;
            if (_friendsOnly && !actor.StatusFlags.HasFlag(StatusFlags.Friend)) return false;
            if (_partyOnly && !actor.StatusFlags.HasFlag(StatusFlags.PartyMember)) return false;
            if (_fcOnly && (string.IsNullOrEmpty(local.CompanyTag.TextValue) ||
                !string.Equals(local.CompanyTag.TextValue, actor.CompanyTag.TextValue, StringComparison.Ordinal))) return false;
            return true;
        }

}}
