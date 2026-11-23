# IsoRim Sprite Fix

[English](README.md) | [简体中文](README.zh-CN.md)

A fix mod for RimWorld 1.7 (Waylith engine): keeps the **weapons, apparel and other mods' extra parts** of
IsoRim's billboard pawns attached to the card, and folds the **contents of card buildings** onto their cards.
Everything turns with the camera instead of staying flat on the ground.

> This mod fixes 2D only. If you run a mod that draws 3D pawns (anything using SYS), 3D pawns still have their own
> problems — a paper pawn should carry a paper weapon.

## Features

- **Weapon sprites**: an idle weapon's facing is recomputed with the vanilla `DrawCarriedWeapon` rules, fixing
  "the gun slides around while walking" and "the weapon is not hidden when seen from behind".
- **Extra parts**: detects drawing calls on a card pawn that IsoRim does not transform (halos, shields, decorations)
  and lists them in the settings; tick one and it is drawn as part of the card.
  Each method has its own **layer (renderQueue) / plane left / plane up / depth**, and the numbers can be clicked and
  typed in directly.
- **Card buildings**: things inside a building that IsoRim stands up as a card (a body, a corpse, blood, the spike on
  an impaling stake) follow the card. Also per-method, with measured defaults for the impaling stake.
- **In-game entry**: one more icon in the button row at the right edge of the map view opens this mod's settings —
  draggable, resizable, does not pause the game, and remembers where you left it. The options-menu entry still works
  and shows the same pages.
- **Built-in model pack**: the previously separate MyModelPack is merged in, so it is no longer needed as its own mod.
  Re-exporting with IsoRim's model binder syncs the new models in on the next start.

## Requirements

- **Harmony** (brrainz.harmony)
- **[IsoRim Waylith](https://github.com/peren717/IsoRim-1.7)** (Peren.IsoRim17)

Load this mod after IsoRim.

## Known issues

- With Show Me Your Hands, the hands are misplaced after starting to aim and turning the camera (not fixed yet;
  mostly harmless).
- The staff of 更多绮罗勇者 is held in a rather comical way.
- IsoRimZ upper floors: extra parts on the second floor and above are still being fixed.

## Compatibility

Confirmed to work with: Show Me Your Hands, NewRatkinPlus, Epona Dynastic Rise, Miho, SQUISHY Ranged,
Isekai RPG Leveling (ascension halo), Isekai Dungeons (godhood halo), CeleTech, VEF shield bubbles and more.

Another mod's sprite still stuck to the ground? Turn on **"auto-detect other mods' extra parts"** in the settings,
equip the item once (or stand there), then tick the newly listed method back in the settings. You can turn detection
off afterwards to save performance.

## Credits

- **[IsoRim Waylith](https://github.com/peren717/IsoRim-1.7)** (Peren) — card rendering and API
- **Visceral Anatomy - Gore & Regeneration** (TanyaVonHart) — reference for the impaling-stake adaptation
- Everyone who reported problems

## License

Code and documentation: see `LICENSE`. Models under `Models/` are made by the author with IsoRim's model binder and
are not distributed with this repository.
