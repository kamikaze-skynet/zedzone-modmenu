# Mod Menu (ZED ZONE)

One in-game menu for every mod's settings and hotkeys. A BepInEx (IL2CPP) mod for ZED ZONE, loaded by the ZED ZONE Code Mod Loader like our other mods.

Press **F11** (in the main menu or in game) to open it.

## Requires

- ZED ZONE (Windows, Steam)
- [ZED ZONE Code Mod Loader](https://steamcommunity.com/sharedfiles/filedetails/?id=3834614258) - run its install.cmd once, then start the game through Steam.

## What it does

- **Every mod in one list.** Left side: each loaded mod that has a BepInEx config (ours first, then other authors' mods). Right side: that mod's settings, grouped by section.
- **Change settings in place.** On/Off switches, `-` / `+` for numbers (Shift+click for big steps, or click the number and type), `<` / `>` for choices, click a text value to type it. **Default** puts a setting back. A blue `*` marks anything you changed.
- **All keys page.** Every mod's hotkeys on one page. Click a key, then press the new one (Esc cancels, right-click clears it). Keys used twice, or used by the game (F5 quick save, F12 Steam screenshot, Esc), show in red.
- **Clash warning.** A few seconds after the game starts, if two mods share a key, a notice shows in the corner (turn off with `WarnKeyClashes`).
- Changes are written to the mod's `.cfg` in `BepInEx\config` at once. Most apply right away; a few only after a restart (the menu can't tell which). **Re-read files** picks up edits you made to the .cfg files by hand.
- While the menu is open the game ignores your clicks and keys.

Our other mods don't need any code for this: the menu reads each plugin's BepInEx config. They work fine without Mod Menu too; it is a "Required item" on the Workshop only so everyone gets the menu.

## Settings (`BepInEx\config\girthy_mods.zedzone.modmenu.cfg`)

| Setting | Default | |
|---|---|---|
| Keys.OpenMenuKey | F11 | Opens and closes the menu |
| Keys.WarnKeyClashes | true | Corner notice at startup when two mods share a key |
| Display.UiScale | 1 | Menu size |
| Display.ShowOtherMods | true | Also list other authors' mods |
| Debug.VerboseLog | false | More detail in `BepInEx\ModMenu.log` |

## Install

Subscribe on the Steam Workshop, or copy `ModMenu.dll` and `mod.json` into `D:\SteamLibrary\steamapps\common\ZED ZONE\Mods\ModMenu` (or `%USERPROFILE%\AppData\LocalLow\LevenLiu\ZEDZONE\Mods\ModMenu`).

## Build

```
dotnet build src\ModMenu.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\ZED ZONE"
```

## Publish

```
.\publish-workshop.ps1 -SteamUser <login>                     # first upload: creates a Private item
.\publish-workshop.ps1 -SteamUser <login> -Visibility Public
.\set-required-items.ps1                                       # make Mod Menu a Required item of our other Workshop mods
```

`set-required-items.ps1` talks to the running Steam client (logged in as the items' owner) through the game's `steam_api64.dll`. Steam then asks anyone subscribing to one of those mods to subscribe to Mod Menu too. By hand instead: each item's Workshop page > Owner controls > **Add/Remove Required Items**.

Log: `BepInEx\ModMenu.log` (lists every mod's hotkeys at startup, and every setting you change).
