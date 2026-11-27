# Chat Bubbles Revived

after nearly 1.5 years, i finally have the guts to publish this. a revival of the old [ChatBubbles](https://github.com/Haplo064/ChatBubbles) for FFXIV made by Haplo064 and Khayle. this plugin is pretty much the same thing except now I'll be the one maintaining it. 

**Version:** 2.2.0.1 (Updated for FFXIV Patch 7.58 and Dalamud API 15)

## Features

some are still buggy but they work for now.

- native chat bubbles (like you see with the npcs? yeah) with adjustable size, height and duration
- custom text and background colors for each chat channel.
- chat channel selection and social filters
- four message modes: Queue, Stack, Replace, Sensible
- chaos mode and butt bubbles

ya girl loves organization so settings are now split into General, Channels and Extras. 

## Commands

`/bub` - open/close settings window
`/bub toggle` - turn chat bubbles on/off

## Known limitations

- pvp enabled but not fully tested yet.
- chat bubbles may not appear if the plugin can't uniquely identify the character speaking.
- FFXIV/Dalamud updates may break bubbles since this plugin relies on the game's native UI.
- bubbles may jitter horizontally during certain character and camera movements. currently investigating.

## Installation

add this to your custom plugin repositories in Dalamud settings:

`https://raw.githubusercontent.com/eidotheca/ChatBubblesRevived/main/repo.json`

then search for Chat Bubbles Revived in the plugin installer.

if you have any issues, enable debug logging under Extras or contact me on discord @eidotheca. 

this plugin is not officially supported by square enix. 

## Credits

- Haplo064 and Khayle — original ChatBubbles plugin
- Eido — revival and maintenance

made for lain but i guess yall can use it too :)