# Art Credits

## Character & enemy sprites — Dungeon Crawl Stone Soup tiles (CC0 / public domain)

The 32×32 pixel sprites in `assets/sprites/` come from the
[Dungeon Crawl Stone Soup](https://github.com/crawl/crawl) tile set
(`crawl-ref/source/rltiles/mon/`), released under CC0. Mapping:

Act I:

| Game use | Source tile |
| --- | --- |
| Player avatar | `humanoids/humans/hell_knight.png` |
| Cultist | `humanoids/humans/occultist.png` |
| Acid Slime | `amorphous/slime_creature.png` |
| Rot Hound | `animals/hound.png` |
| Ward Acolyte | `vault/cloud_mage.png` |
| Bog Troll | `humanoids/troll.png` |
| Possessed Armor | `undead/ancient_champion.png` |
| Hollow King | `undead/ancient_lich.png` |
| The Gaol Warden | `humanoids/oni_incarcerator.png` |

Act II:

| Game use | Source tile |
| --- | --- |
| Ember Wisp | `nonliving/fire_vortex1.png` |
| Slag Brute | `nonliving/iron_golem.png` |
| Cinder Cultist | `vault/hellbinder.png` |
| Ash Stalker | `demons/smoke_demon.png` |
| Molten Sentinel | `nonliving/blazeheart_golem.png` |
| Pyre Warden | `demons/efreet.png` |
| Emberlord Vashk | `unique/asmodeus.png` |
| The Slag Maw | `amorphous/rockslime.png` |

Act III:

| Game use | Source tile |
| --- | --- |
| Hollow Shade | `undead/shadow_wraith.png` |
| Bone Choir | `undead/revenant.png` |
| Throne Sentry | `statues/obsidian_statue.png` |
| Void Leech | `aberrations/tentacled_monstrosity.png` |
| Crown Reaver | `vault/antique_champion.png` |
| The Silent Judge | `holy/daeva.png` |
| The Hollow Throne | `vault/zot_statue.png` |
| The Nameless Regent | `undead/ancient_lich.png` |

The tiled screen backgrounds in `assets/backgrounds/` are from the same set
(`crawl-ref/source/rltiles/dngn/floor/`, CC0, upscaled 2×): `crypt0`,
`black_cobalt01`, `dirt0`, `etched0`, `cobble_blood1`, `floor_gulch0`,
`demonic_red1`.

## Icons — game-icons.net (CC BY 3.0)

All SVG icons in `assets/icons/` are from [game-icons.net](https://game-icons.net),
licensed under [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).
They were recolored (white on transparent) but otherwise unmodified. Icons by author:

- **Lorc** (https://lorcblog.blogspot.com): anchor, axe-swing, barbed-spear,
  bordered-shield, bottle-vapors, bottled-bolt, brainstorm, broken-bottle, campfire,
  cracked-saber, cracked-shield,
  crossed-swords, crowned-skull, evil-book, fanged-skull, fire-bottle, fizzing-flask,
  flying-flag, gears, ghost, hammer-drop, heart-bottle, heart-drop, hourglass,
  lightning-arc, lightning-mask, magic-shield, meditation, microscope-lens,
  piercing-sword, poison-bottle, poison-gas, potion-ball, punch, punch-blast,
  quick-slash, round-bottom-flask, sacrificial-dagger, serrated-slash, shoulder-scales,
  snake-bite, sonic-shout, spiked-armor, spiral-bottle, square-bottle, surrounded-shield,
  swap-bag, sword-slice, thunder-struck, uncertainty, wave-strike, whirlwind
- **Delapouite** (https://delapouite.com): biceps, bracer, charging-bull, chest-armor,
  coins-pile, dart, drum, health-potion, snake-spiral, stone-wall, totem, two-coins,
  warhammer
- **Skoll** (https://game-icons.net): blood, fangs, open-treasure-chest
- **sbed** (https://opengameart.org/content/95-game-icons): death-skull, poison, shield
- **Willdabeast** (https://wjbstories.blogspot.com): black-book, round-shield
- **Andy Meneely** (https://www.se.rit.edu/~andy/): riposte
- **Caro Asercion** (https://game-icons.net): coinflip

If this game is distributed, this attribution must ship with it (e.g. in the
credits/settings screen or an included file).

## Typography — Google Fonts (SIL Open Font License 1.1)

Fonts in `assets/fonts/` are used under the
[SIL OFL 1.1](https://openfontlicense.org/), which requires the license text
to ship alongside the font files (see the `*-OFL.txt` files next to each
font).

| Font | Copyright | Use |
| --- | --- | --- |
| [Silkscreen](https://fonts.google.com/specimen/Silkscreen) | © 2001 The Silkscreen Project Authors | Display face — titles, buttons, card/enemy names, HP/energy numbers, floating damage text |
| [Jersey 15](https://fonts.google.com/specimen/Jersey+15) | © 2023 The Soft Type Project Authors | Body face — card descriptions and general UI text |

Both are bitmap/pixel faces, imported with antialiasing, hinting and subpixel
positioning disabled so glyphs stay on the pixel grid — see
`docs/ART_SPEC.md` §7.

### Retired

Cinzel (Natanael Gama) and IM Fell English (Igino Marini) were the previous
display/body pair. They were dropped when the project committed to pixel art
as a single medium: high-res serif faces are anti-aliased sub-pixel curves,
so keeping them would have moved the art/text seam rather than closed it.
Their files have been removed from `assets/fonts/`.

Pixelify Sans was also trialled as the body face and rejected: at 16px its
digits `2`, `3`, `5` and `8` are mutually ambiguous, which is disqualifying
for a game whose text is mostly numbers. Its files were removed too.

## UI chrome — procedural, no sourced material

All chrome (button bezels, HP-bar frames, card frames, badges, panels) is
built procedurally in `scripts/ui/ChromeStyles.cs` from the shared palette.
No attribution is required.

### Retired

The End Turn and main-menu buttons previously used ornate wooden frame
textures from [Fantasy UI Box](https://opengameart.org/content/fantasy-ui-box)
by **StumpyStrust** (CC0). They were removed when the project committed to
pixel art: the frames are smooth, anti-aliased fantasy art, and placing them
beside bitmap type and 32×32 sprites was exactly the mixed-media seam the
commitment exists to close.

## Audio — procedurally generated, no sourced material

Every sound in the game (SFX and music) is synthesized in-engine at runtime
from raw oscillators/envelopes/filters (`scripts/audio/AudioSynth.cs`,
`AudioCues.cs`, `AudioMusic.cs`) - no samples, recordings, or third-party
audio assets were used. No attribution is required.
