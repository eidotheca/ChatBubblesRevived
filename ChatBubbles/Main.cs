using Dalamud.Configuration;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Dalamud.Game.Text;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Num = System.Numerics;
using Lumina.Excel.Sheets;

namespace ChatBubbles
{
    internal class UiColorComparer : IEqualityComparer<UIColor>
    {
        public bool Equals(UIColor x, UIColor y)
        {
            return x.Dark == y.Dark; // based on variable i
        }
        public int GetHashCode(UIColor obj)
        {
            return obj.Dark.GetHashCode(); // hashcode of variable to compare
        }
    }

    public unsafe partial class ChatBubbles : IDalamudPlugin
    {
        public string Name => "Chat Bubbles Revived";
        private readonly List<UIColor> _uiColours;
        private readonly Config _configuration;
        private bool _picker;

        private int _timer;
        private UiColorPick? _chooser;
        private int _queue;
        private int _bubbleFunctionality;
        private bool _hide;
        private bool _assBubbles;
        private bool _chaosMode;
        private bool _friendsOnly;
        private bool _fcOnly;
        private bool _partyOnly;

        private readonly List<Vector4> _bubbleColours;
        private readonly List<Vector4> _bubbleColours2;

        private bool _switch;
        private float _bubbleSize;

        private readonly object _configSaveLock = new();
        private string? _savedConfigSnapshot;
        private string? _pendingConfigSnapshot;
        private long _nextConfigSave;
        private bool _configSaveFailed;
        private bool _config;

        private bool _debug;

        //Distance
        private int _yalmCap;

        private readonly List<XivChatType> _channels;

        private readonly List<XivChatType> _order = new()
        {
            XivChatType.None, XivChatType.None, XivChatType.None, XivChatType.None, XivChatType.Say,
            XivChatType.Shout, XivChatType.TellOutgoing, XivChatType.TellIncoming, XivChatType.Party,
            XivChatType.Alliance,
            XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4, XivChatType.Ls5,
            XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8, XivChatType.FreeCompany, XivChatType.NoviceNetwork,
            XivChatType.CustomEmote, XivChatType.StandardEmote, XivChatType.Yell, XivChatType.CrossParty,
            XivChatType.PvPTeam,
            XivChatType.CrossLinkShell1, XivChatType.Echo, XivChatType.None, XivChatType.None, XivChatType.None,
            XivChatType.None, XivChatType.None, XivChatType.None, XivChatType.None, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3,
            XivChatType.CrossLinkShell4,
            XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7,
            XivChatType.CrossLinkShell8
        };
        
        private readonly UiColorPick[] _textColour;

        // Managed equivalent avoids changed ImGui output parameter signatures.
        private static (float H, float S, float V) RgbToHsv(float r, float g, float b)
        {
            float max = MathF.Max(r, MathF.Max(g, b));
            float min = MathF.Min(r, MathF.Min(g, b));
            float delta = max - min;
            float h = 0f;
            if (delta > 0f)
            {
                if (max == r) h = (g - b) / delta;
                else if (max == g) h = 2f + (b - r) / delta;
                else h = 4f + (r - g) / delta;
                h /= 6f;
                if (h < 0f) h += 1f;
            }
            return (h, max > 0f ? delta / max : 0f, max);
        }

        public ChatBubbles(IDalamudPluginInterface pluginInt)
        {


            pluginInt.Create<Services>();
 
            _configuration = Services.PluginInterface.GetPluginConfig() as Config ?? new Config();
            _timer = _configuration.Timer;
            _debug = _configuration.DebugLogging;
            _channels = _configuration.Channels;
            _textColour = _configuration.TextColour ?? Array.Empty<UiColorPick>();
            _queue = _configuration.Queue;
            _bubbleFunctionality = _configuration.BubbleFunctionality;
            _hide = _configuration.Hide;
            _fcOnly = _configuration.fcOnly;
            _partyOnly = _configuration.partyOnly;
            _friendsOnly = _configuration.friendsOnly;

            _bubbleColours = _configuration.BubbleColours ?? new List<Vector4>();
            _bubbleColours2 = _configuration.BubbleColours2 ?? new List<Vector4>();
            _bubbleSize = _configuration.BubbleSize;


            _assBubbles = _configuration.assBubbles;
            _chaosMode = _configuration.chaosMode && !_assBubbles;
            _configuration.chaosMode = _chaosMode;
            _switch = _configuration.Switch;
            _yalmCap = _configuration.YalmCap;


            //Added two enums in dalamud update
            if (_bubbleColours.Count == 39 && _bubbleColours2.Count >= 32 && _textColour.Length >= 39)
            {
                _bubbleColours.Insert(32, new Vector4(1, 1, 1, 0));
                _bubbleColours.Insert(32, new Vector4(1, 1, 1, 0));
                _bubbleColours2.Insert(32, new Vector4(0,0, 0, 0));
                _bubbleColours2.Insert(32, new Vector4(0, 0, 0, 0));

                var temp = new UiColorPick[_bubbleColours.Count];
                for (int i =0; i<39; i++)
                {
                    if(i >= 32)
                    {
                        temp[i+2] = _textColour[i];
                    }
                    else
                    {
                        temp[i] = _textColour[i];
                    }
                }
                temp[32] =  new() { Choice = 0, Option = 0 };
                temp[33] = new() { Choice = 0, Option = 0 };

                _textColour = temp;
            }

            while (_bubbleColours.Count < _order.Count) _bubbleColours.Add(new Vector4(1,1,1,0));
            while (_bubbleColours2.Count < _order.Count) _bubbleColours2.Add(new Vector4(0,0,0,0));
            if (_textColour.Length < _order.Count) Array.Resize(ref _textColour, _order.Count);
            for (int i = 0; i < _textColour.Length; i++)
                _textColour[i] ??= new UiColorPick { Choice = 0, Option = 0 };


            
            var list = new List<UIColor>(Services.DataManager.Excel.GetSheet<UIColor>()!.Distinct());
            list.Sort((a, b) =>
            {
                var colorA = ConvertUIColorToColor(a);
                var colorB = ConvertUIColorToColor(b);
                var (aH, aS, aV) = RgbToHsv(colorA.X, colorA.Y, colorA.Z);
                var (bH, bS, bV) = RgbToHsv(colorB.X, colorB.Y, colorB.Z);

                var hue = aH.CompareTo(bH);
                if (hue != 0) { return hue; }

                var saturation = aS.CompareTo(bS);
                if (saturation != 0) { return saturation; }

                var value = aV.CompareTo(bV);
                return value != 0 ? value : 0;
            });
            _uiColours = list;


            

            // Native chat and addon lifecycle handlers.
            Services.ChatGui.ChatMessage += OnChatMessage;
            Services.Framework.Update += NativeCaptureUpdate;
            _savedConfigSnapshot = JsonConvert.SerializeObject(_configuration);
            Services.Framework.Update += FlushConfigOnUpdate;
            Services.AddonLifecycle.RegisterListener(Dalamud.Game.Addon.Lifecycle.AddonEvent.PreDraw, "_MiniTalk", NativeLayoutPreDraw);
            Services.AddonLifecycle.RegisterListener(Dalamud.Game.Addon.Lifecycle.AddonEvent.PostDraw, "_MiniTalk", NativeColorsPostDraw);
            Services.PluginInterface.UiBuilder.Draw += BubbleConfigUi;
            Services.PluginInterface.UiBuilder.OpenConfigUi += BubbleConfig;
            Services.PluginInterface.UiBuilder.OpenMainUi += OpenMainWindow;
            Services.CommandManager.AddHandler("/bubtest", new CommandInfo(Command)
            {
                HelpMessage = "Opens Chat Bubbles Revived settings (compatibility alias)."
            });
            Services.CommandManager.AddHandler("/bub", new CommandInfo(Command) { HelpMessage = "Opens Chat Bubbles Revived settings. /bub toggle toggles bubbles." });
            Services.PluginLog.Information("Chat Bubbles Revived loaded.");
        }

        private Vector4 ConvertUIColorToColor(UIColor uiColor)
        {
            var temp = BitConverter.GetBytes(uiColor.Dark);
            return new Vector4((float)temp[3] / 255,
                (float)temp[2] / 255,
                (float)temp[1] / 255,
                (float)temp[0] / 255);
        }

        private void SaveConfig()
        {
            if (_assBubbles && _chaosMode) _chaosMode = false;
            _configuration.Timer = _timer;
            _configuration.DebugLogging = _debug;
            _configuration.Channels = _channels;
            _configuration.TextColour = _textColour;
            _configuration.Queue = _queue;
            _configuration.BubbleFunctionality = _bubbleFunctionality;
            _configuration.Hide = _hide;
            _configuration.assBubbles = _assBubbles;
            _configuration.chaosMode = _chaosMode;
            _configuration.fcOnly = _fcOnly;
            _configuration.friendsOnly = _friendsOnly;
            _configuration.partyOnly = _partyOnly;
            _configuration.BubbleColours = _bubbleColours;
            _configuration.BubbleColours2 = _bubbleColours2;

            _configuration.BubbleSize = _bubbleSize;


            _configuration.Switch = _switch;
            _configuration.YalmCap = _yalmCap;

            // Only schedule actual changes; a detached snapshot avoids saving mutable UI state.
            lock (_configSaveLock)
            {
                var snapshot = JsonConvert.SerializeObject(_configuration);
                if (snapshot == _pendingConfigSnapshot) return;
                if (snapshot == _savedConfigSnapshot)
                {
                    _pendingConfigSnapshot = null;
                    return;
                }
                _pendingConfigSnapshot = snapshot;
                if (!_configSaveFailed) _nextConfigSave = Environment.TickCount64 + 350;
            }
        }

        private void FlushConfigOnUpdate(IFramework framework) => FlushConfig(false);

        private void FlushConfig(bool force)
        {
            lock (_configSaveLock)
            {
                if (_pendingConfigSnapshot == null || (!force && Environment.TickCount64 < _nextConfigSave)) return;
                try
                {
                    var snapshot = _pendingConfigSnapshot;
                    var config = JsonConvert.DeserializeObject<Config>(snapshot);
                    if (config == null) throw new InvalidOperationException("Configuration snapshot was empty.");
                    Services.PluginInterface.SavePluginConfig(config);
                    _savedConfigSnapshot = snapshot;
                    _pendingConfigSnapshot = null;
                    if (_configSaveFailed) Services.PluginLog.Information("Configuration save recovered.");
                    _configSaveFailed = false;
                }
                catch (Exception e)
                {
                    if (!_configSaveFailed) Services.PluginLog.Warning("Configuration save failed; keeping changes in memory and retrying: " + e.Message);
                    _configSaveFailed = true;
                    _nextConfigSave = Environment.TickCount64 + 2000;
                    if (force) Services.PluginLog.Warning("Configuration could not be saved before unload; recent changes may not persist.");
                }
            }
        }


        void IDisposable.Dispose()
        {
            Services.ChatGui.ChatMessage -= OnChatMessage;
            try { RestoreNativeScales(); RestoreNativeColors(); } catch (Exception e) { Services.PluginLog.Warning("Native color cleanup: " + e.GetType().Name); }
            Services.AddonLifecycle.UnregisterListener(Dalamud.Game.Addon.Lifecycle.AddonEvent.PreDraw, "_MiniTalk", NativeLayoutPreDraw);
            Services.AddonLifecycle.UnregisterListener(Dalamud.Game.Addon.Lifecycle.AddonEvent.PostDraw, "_MiniTalk", NativeColorsPostDraw);
            Services.Framework.Update -= NativeCaptureUpdate;
            Services.Framework.Update -= FlushConfigOnUpdate;
            SaveConfig();
            FlushConfig(true);
            Services.PluginInterface.UiBuilder.Draw -= BubbleConfigUi;
            Services.PluginInterface.UiBuilder.OpenConfigUi -= BubbleConfig;
            Services.PluginInterface.UiBuilder.OpenMainUi -= OpenMainWindow;
            Services.CommandManager.RemoveHandler("/bubtest");
            Services.CommandManager.RemoveHandler("/bub");
        }

        private void OpenMainWindow() => _config = true;

        private void BubbleConfig()
        {
            if(!_config)
            {
                _config = true;
            }
            else
            {
                _config = !_config;
                SaveConfig();
            }
        } 


        // What to do when command is called
        private void Command(string command, string arguments)
        {
            if (string.Equals(arguments.Trim(), "toggle", StringComparison.OrdinalIgnoreCase))
            {
                _switch = !_switch;
                SaveConfig();
                return;
            }
            _config = !_config;
            if (!_config) SaveConfig();
        }


    }

    public class UiColorPick
    {
        public uint Choice { get; set; }
        public uint Option { get; set; }
    }

    public class Config : IPluginConfiguration
    {
        public int Version { get; set; } = 1;
        public bool DebugLogging { get; set; } = false;
        public List<XivChatType> Channels { get; set; } = new() {XivChatType.Say};
        public int Timer { get; set; } = 7;
        public int BubbleFunctionality { get; set; } = 0;
		public bool Hide { get; set; } = false;
		public bool friendsOnly { get; set; } = false;
		public bool fcOnly { get; set; } = false;
		public bool partyOnly { get; set; } = false;

        public float BubbleSize { get; set; } = 1f;

        public bool Switch { get; set; } = true;
        public int YalmCap { get; set; } = 99;

        public bool assBubbles { get; set; } = false;
        public bool chaosMode { get; set; } = false;


        public UiColorPick[] TextColour { get; set; } =
        {
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }, new () { Choice = 0, Option =0 },
            new() { Choice = 0, Option =0 }, new() { Choice = 0, Option =0 }
        };

        public List<Vector4> BubbleColours { get; set; } = new List<Num.Vector4>();
        public List<Vector4> BubbleColours2 { get; set; } = new List<Num.Vector4>();

        public int Queue { get; set; } = 3;
    }
}
