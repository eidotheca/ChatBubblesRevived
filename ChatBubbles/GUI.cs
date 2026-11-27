using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Plugin;
using Dalamud.Bindings.ImGui;
using System;
using System.Diagnostics;
using Num = System.Numerics;


namespace ChatBubbles
{
    public unsafe partial class ChatBubbles : IDalamudPlugin
    {
        
        private bool DrawChannelResetButton(int index)
        {
            float size = ImGui.GetFrameHeight();
            bool clicked = ImGui.Button($"##ResetChannelColors{index}", new Num.Vector2(size, size));
            var min = ImGui.GetItemRectMin();
            var center = min + new Num.Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.24f;
            float thickness = Math.Max(1f, size * 0.07f);
            uint color = ImGui.GetColorU32(ImGuiCol.Text);
            var draw = ImGui.GetWindowDrawList();
            // Counterclockwise undo arrow, drawn independently of the UI font.
            var previous = center + new Num.Vector2(-radius, -radius * 0.55f);
            var tip = previous;
            for (int segment = 0; segment <= 16; segment++)
            {
                float angle = (-150f + segment * 300f / 16f) * MathF.PI / 180f;
                var point = center + radius * new Num.Vector2(MathF.Cos(angle), MathF.Sin(angle));
                if (segment == 0) tip = point;
                else draw.AddLine(previous, point, color, thickness);
                previous = point;
            }
            draw.AddLine(tip, tip + new Num.Vector2(0f, -size * 0.19f), color, thickness);
            draw.AddLine(tip, tip + new Num.Vector2(size * 0.19f, 0f), color, thickness);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reset this channel's colors to game defaults.");
            return clicked;
        }

        private void BubbleConfigUi()
        {
            if (_config)
            {
                ImGui.SetNextWindowSizeConstraints(new Num.Vector2(600, 420), new Num.Vector2(1920, 1080));
                if (ImGui.Begin("Chat Bubbles Revived", ref _config))
                {
                    bool extrasActive = false;
                    if (ImGui.BeginTabBar("##BubbleSettingsTabs"))
                    {
                        if (ImGui.BeginTabItem("General"))
                        {
                            ImGui.Text("Visibility and filters");
                ImGui.Checkbox("Show Bubbles", ref _switch);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        "This is here in case you used '/bub toggle' and forgot about doing it.");
                }
                ImGui.Checkbox("Hide Your Chat", ref _hide);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Hides your own character's bubbles.");
                }
                ImGui.Checkbox("Display friends only", ref _friendsOnly);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Hides the bubbles from every character that isn't in your friend list. Bypassed during active duties.");
				}
				ImGui.Checkbox("Display FC only", ref _fcOnly);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Matches your nonempty Free Company tag (approximate; tags are not unique). Bypassed during active duties.");
				}
				ImGui.Checkbox("Display party only", ref _partyOnly);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Hides the bubbles from every character that isn't in your current party. Bypassed during active duties.");
                }
                            ImGui.Separator();
                            ImGui.Text("Message behavior");
                ImGui.InputInt("Bubble Timer", ref _timer);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("How long the bubble will last on screen.");
                }
                ImGui.InputInt("Yalm distance", ref _yalmCap);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Anything over this distance in Yalms will not be shown");
                }

                ImGui.RadioButton("Queue", ref _bubbleFunctionality, 0);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        "Queue the messages to appear one after the other.");
                }
                ImGui.SameLine();
                ImGui.RadioButton("Stack", ref _bubbleFunctionality, 1);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        "Stack messages into one bubble.");
                }
                ImGui.SameLine();
                ImGui.RadioButton("Replace", ref _bubbleFunctionality, 2);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        "Instantly replace bubbles when new input occurs.");
                }
                ImGui.SameLine();
                ImGui.RadioButton("Sensible", ref _bubbleFunctionality, 3);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        "Stack for the same chat channel, but queue if a new chat channel occurs.");
                }


                if (_bubbleFunctionality == 0 || _bubbleFunctionality == 3)
                {
                    ImGui.InputInt("Maximum Bubble Queue", ref _queue);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("How many bubbles can be queued to be seen per person.");
                    }
                }
                
                            ImGui.Separator();
                            ImGui.Text("Appearance");
                float nativeScale = NormalizeNativeScale(_bubbleSize);
                if (ImGui.SliderFloat("Bubble size", ref nativeScale, 0.5f, 2.0f, "%.2fx"))
                { _bubbleSize = nativeScale; SaveConfig(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Scales the complete native player bubble without changing wrapping.");
                ImGui.SameLine();
                if (ImGui.Button("Reset size")) { _bubbleSize = 1f; SaveConfig(); }
                            ImGui.TextWrapped("Emotes use native italic formatting automatically.");
                            ImGui.EndTabItem();
                        }
                        if (ImGui.BeginTabItem("Channels"))
                        {
                            ImGui.TextWrapped("Color squares: Text / Tint / Background. Enable the channels you want to display.");
                var i = 0;
                ImGui.Text("Enabled channels:");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Which chat channels to show bubbles for.");
                }

                ImGui.Columns(2);
                foreach (var e in _order)
                {
                    if (e != XivChatType.None)
                    {
                        var txtclr = BitConverter.GetBytes(_textColour[i].Choice);
                        var textPreview = _textColour[i].Choice == 0 && _textColour[i].Option == 0
                            ? new Num.Vector4(0.035f, 0.03f, 0.025f, 1f)
                            : new Num.Vector4(txtclr[3] / 255f, txtclr[2] / 255f, txtclr[1] / 255f, 1f);
                        if (ImGui.ColorButton($"Text Colour##{i}", textPreview))
                        {
                            _chooser = _textColour[i];
                            _picker = true;
                        }
                        ImGui.SameLine();
                        var temp2 = _bubbleColours2[i];
                        var previous_temp2 = temp2;
                        // An inactive tint is not a black color. Show a white preview only.
                        if (temp2.W == 0f) temp2 = new Num.Vector4(1f, 1f, 1f, 0f);
                        var preview_temp2 = temp2;
                        if (ImGui.ColorEdit4($"Bubble tint##{i}", ref temp2, ImGuiColorEditFlags.NoInputs|ImGuiColorEditFlags.NoLabel))
                        {
                            // RGB edits on an inactive color should become visible immediately.
                            // An alpha-only change to zero still explicitly disables the override.
                            if (previous_temp2.W == 0f && temp2.W == 0f &&
                                (preview_temp2.X != temp2.X || preview_temp2.Y != temp2.Y || preview_temp2.Z != temp2.Z))
                                temp2.W = 0.5f;
                        }
                        else temp2 = previous_temp2;
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(
                                temp2.W == 0f ? "Bubble tint: OFF (game default). Alpha zero disables tint." : "Bubble color tint. Alpha controls tint strength; zero disables it.");
                        }
                        ImGui.SameLine();
                        var temp = _bubbleColours[i];
                        var previous_temp = temp;
                        if (ImGui.ColorEdit4($"Bubble background##{i}", ref temp, ImGuiColorEditFlags.NoInputs|ImGuiColorEditFlags.NoLabel))
                        {
                            // RGB edits on an inactive color should become visible immediately.
                            // An alpha-only change to zero still explicitly disables the override.
                            if (previous_temp.W == 0f && temp.W == 0f &&
                                (previous_temp.X != temp.X || previous_temp.Y != temp.Y || previous_temp.Z != temp.Z))
                                temp.W = 1f;
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(
                                "Bubble background color. Alpha controls override strength; zero restores classic ivory.");
                        }
                        ImGui.SameLine();

                        if (DrawChannelResetButton(i))
                        {
                            _textColour[i].Choice = 0;
                            _textColour[i].Option = 0;
                            temp = new Num.Vector4(1f, 1f, 1f, 0f);
                            temp2 = new Num.Vector4(0f, 0f, 0f, 0f);
                            if (ReferenceEquals(_chooser, _textColour[i])) _picker = false;
                            _bubbleColours[i] = temp;
                            _bubbleColours2[i] = temp2;
                            SaveConfig();
                        }
                        ImGui.SameLine();

                        var enabled = _channels.Contains(e);
                        if (ImGui.Checkbox($"{e}", ref enabled))
                        {
                            if (enabled) _channels.Add(e);
                            else _channels.Remove(e);
                        }

                        _bubbleColours[i] = temp;
                        _bubbleColours2[i] = temp2;
                        
                        ImGui.NextColumn();
                    }
                    i++;
                }
                                

                ImGui.Columns(1);


                            ImGui.EndTabItem();
                        }
                        if (ImGui.BeginTabItem("Extras"))
                        {
                            extrasActive = true;
                            ImGui.Text("Novelty attachment modes");
                            ImGui.BeginDisabled(_assBubbles);
                            if (ImGui.Checkbox("Chaos Mode", ref _chaosMode))
                            {
                                if (_chaosMode) _assBubbles = false;
                                SaveConfig();
                            }
                            ImGui.EndDisabled();
                            ImGui.TextWrapped("Random attachment points for new bubbles.");
                            ImGui.BeginDisabled(_chaosMode);
                            if (ImGui.Checkbox("Butt Bubbles", ref _assBubbles))
                            {
                                if (_assBubbles) _chaosMode = false;
                                SaveConfig();
                            }
                            ImGui.EndDisabled();
                            ImGui.TextWrapped("Attach new bubbles to the rear. Disable the active mode to select the other.");
                            ImGui.Separator();
                            if (ImGui.Checkbox("Debug Logging", ref _debug)) SaveConfig();
                            ImGui.TextWrapped("Writes diagnostic status to the Dalamud log while enabled.");
                            ImGui.EndTabItem();
                        }
                        ImGui.EndTabBar();
                    }
                    ImGui.Separator();
                    if (ImGui.Button("Save and Close")) _config = false;
                    if (extrasActive)
                    {
                        ImGui.SameLine();
                        if (ImGui.Button("Support original author"))
                            Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/khayle", UseShellExecute = true });
                    }
                }
                ImGui.End();
                SaveConfig();
            }

            if (_picker)
            {
                // Size to the full palette rather than stretching columns to the window.
                var pickerFlags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize
                    | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse;
                if (ImGui.Begin("UIColor Picker##FixedPalette", ref _picker, pickerFlags))
                {
                    if (ImGui.Button("Restore default text color") && _chooser != null)
                    {
                        _chooser.Choice = 0;
                        _chooser.Option = 0;
                        _picker = false;
                        SaveConfig();
                    }
                    ImGui.Spacing();
                    const int columns = 10;
                    float swatch = ImGui.GetFrameHeight();
                    int index = 0;
                    foreach (var z in _uiColours)
                    {
                        if (index % columns != 0)
                            ImGui.SameLine();
                        var temp = BitConverter.GetBytes(z.Dark);
                        if (ImGui.ColorButton(z.RowId.ToString(), new Num.Vector4(
                            (float)temp[3] / 255,
                            (float)temp[2] / 255,
                            (float)temp[1] / 255,
                            (float)temp[0] / 255), ImGuiColorEditFlags.None,
                            new Num.Vector2(swatch, swatch)) && _chooser != null)
                        {
                            _chooser.Choice = z.Dark;
                            _chooser.Option = z.RowId;
                            _picker = false;
                        SaveConfig();
                        }
                        index++;
                    }
                }
                ImGui.End();
            }

        }
    }
}
