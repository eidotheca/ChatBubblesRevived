# Chat Bubbles Revived

after nearly 1.5 years, i finally have the guts to publish this. a revival of the old [ChatBubbles](https://github.com/Haplo064/ChatBubbles) for FFXIV made by Haplo064 and Khayle. this plugin is pretty much the same thing except now I'll be the one maintaining it. a customizable native chat bubble plugin for ffxiv that has channel colors, scaling, and 4 message modes.

**Version:** 2.2.0.1 (Updated for FFXIV Patch 7.58 and Dalamud API 15)

[Latest release](https://github.com/eidotheca/ChatBubblesRevived/releases/latest)

## Features

some are still buggy but they work for now.

- native chat bubbles (like you see with the npcs? yeah) with adjustable size, height and duration
- custom text and background colors for each chat channel.
- chat channel selection and social filters
- works in dungeons and pvp
- four message modes: Queue, Stack, Replace, Sensible
- chaos mode and butt bubbles

ya girl loves organization so settings are now split into General, Channels and Extras. 
remember to disable the official ffxiv in-game chat bubbles.

## Commands

`/bub` - open/close settings window

`/bub toggle` - turn chat bubbles on/off

## Known limitations

- chat bubbles may not appear if the plugin can't uniquely identify the character speaking.
- FFXIV/Dalamud updates may break bubbles since this plugin relies on the game's native UI.
- bubbles may jitter horizontally during certain character and camera movements. currently investigating.
- auto-translate markers turn up black rather than green and red

## Installation

add this to your custom plugin repositories in Dalamud settings:

`https://raw.githubusercontent.com/eidotheca/ChatBubblesRevived/main/repo.json`

then search for Chat Bubbles Revived in the plugin installer.

## How to build from source
you need the .NET 10 SDK and dalamud 15.0.4.1 libraries. project uses the corresponding XIVLauncher Hooks directory. override `DalamudLibPath` via MSBuild if your installation differs.

from the root directory, run

```powershell
dotnet build ChatBubbles/ChatBubbles.csproj -c Release
```
you can also run the `BUILD-DIAGNOSTIC.cmd` included in the source code. run that if you want to test the build locally and the resulting DLL should be in `ChatBubbles/bin/Release/net10.0-windows/ChatBubblesRevived.dll`. enable Developer mode in /xlsettings > experimental and select the plugin DLL. click the plus sign that appears and save. the plugin will show up in /xlplugins under the Currently Testing tab. make sure any previously installed version of Chat Bubbles Revived is disabled before loading it.

## FAQ

**why arent my messages appearing?**

check your enabled chat channels/social filters/yalm distance in the settings and checkmark the channel you want. 

<br>

**why are my bubbles shaking when i move?**

known issue with native bubble positioning during certain character and camera movements, still investigating.

<br>

**my bubbles are still the in-game version even though i have the plugin enabled.**

they can't be on at the same time, disable the in-game bubbles Character Configuration > Log window settings > Chat bubble settings > uncheck "Enable chat bubbles".

<br>

**why do bubbles sometimes not appear for players who are far away?**

the plugin can only display bubbles for characters currently loaded by the game and also within your configured yalm distance

<br>

if you have any issues, enable debug logging under Extras or contact me on discord @eidotheca. 

this plugin is not officially supported by square enix. 

## Credits

- Haplo064 and Khayle — original ChatBubbles plugin
- Eido — revival and maintenance

made for lain but i guess yall can use it too :)
