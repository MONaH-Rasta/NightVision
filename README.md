# Night Vision

Oxide plugin for Rust. Allows players to see at night! When using night vision, it will appear daytime to you while the rest of the players will see the normal time of day.

## Permissions

* `nightvision.allowed` -- Allows players to use night vision
* `nightvision.unlimitednvg` -- Allows player to use unlimited night vision goggles
* `nightvision.auto` -- Automatically enable night vision when a player joins (requires `nightvision.allowed`)

## Configuration
  
```json
{
  "chatIconID": "0",
  "date": "01/25/2024",
  "time": 12.0
}
```

* `chatIconID`: SteamID for rust chat icon
* `date`: Date to use when night vision is active (`MM/DD/YYYY` format)
* `time`: Time to use when night vision is active (`0`-`24`)

## Chat Commands

* `/nightvision help` -- Night vision help
* `/nightvision <0-24>` (`/nv`) -- Toggle night vision on/off with optional time `0`-`24`
* `/unlimitednvg (/unvg)` -- Equip/remove unlimited night vision goggles

## Localization

```json
{
  "ChatPrefix": "<color=#00ff00>[Night Vision]</color>",
  "NoPerms": "You do not have permission to use this command!",
  "TimeLocked": "Time locked to {0}",
  "TimeUnlocked": "Time unlocked",
  "HelpTitle": "<size=16><color=#00ff00>Night Vision</color> Help</size>\n",
  "Help1": "<color=#00ff00>/nightvision <0-24>(/nv)</color> - Toggle time lock night vision with optional time 0-24",
  "Help2": "<color=#00ff00>/unlimitednvg (/unvg)</color> - Equip/remove unlimited night vision goggles",
  "EquipUNVG": "Equipped unlimited night vision goggles",
  "RemoveUNVG": "Removed unlimited night vision goggles"
}
```

## For Developers

```csharp
[PluginReference]
Plugin NightVisionRef;

void LockPlayerTime(BasePlayer player, float time)
{
    NightVisionRef?.CallHook("LockPlayerTime", player, time);
}

void UnlockPlayerTime(BasePlayer player)
{
    NightVisionRef?.CallHook("UnlockPlayerTime", player);
}

bool IsPlayerTimeLocked(BasePlayer player)
{
    return (bool)NightVisionRef?.CallHook("IsPlayerTimeLocked", player);
}
```

## Credits

* **Jake_Rich** for the original plugin idea and implementation
