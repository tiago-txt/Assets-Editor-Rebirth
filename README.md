# Assets Editor V2

Assets Editor is an open-source tool designed for modifying and managing client assets for both Tibia 12+ and Tibia 1098.

![Main interface](/Assets%20Editor/Resources/1.PNG)
![Search Window](/Assets%20Editor/Resources/2.PNG)
![OTB Editor](/Assets%20Editor/Resources/3.PNG)
![Sheet Editor](/Assets%20Editor/Resources/4.PNG)
![Import and Export](/Assets%20Editor/Resources/5.gif)
![Dark Mode](/Assets%20Editor/Resources/6.PNG)
![Lua Graphs](/Assets%20Editor/Resources/7.PNG)
![Lua Scripting](/Assets%20Editor/Resources/8.PNG)

### Features

- **Support for Tibia 12+**
- **Support for Tibia 10.98**
- **Object Modification**
- **Create/Copy/Delete Objects**
- **Import and Export**
- **Sprite Sheet Modifications**
- **New Search Window**
- **OTB Editor for Tibia 10.98**
- **Import Manager for Tibia 10.98**
- **Export to spr/dat**
- **Export to outfit/item images**
- **Large spritesheets**
- **Transparent items in spritesheets**
- **Lua support**

#### Prerequisites

- [.NET 8 Runtime] (release 2.0)
- [.NET 10 Runtime] (main branch)

#### Usage
- Download the latest release from the [Releases](https://github.com/Arch-Mina/Assets-Editor/releases) page.

:sparkles: **Supporting the Project**

If you find this project useful and want to show your appreciation or support, you're welcome to do so through [PayPal](https://paypal.me/SpiderOT?country.x=EG&locale.x=en_US). Your support is entirely optional but greatly appreciated :heart:.

### Project customizations

This fork includes additional tooling and fixes tailored for the TFS 1.4. and DAT-SPR/Canary setup:

- **Migration menu**
  - New `MigrationMenuWindow` accessible from the main window (`Migrate...` button).
  - Centralizes different migrations:
    - **Items**: TFS 1.4.2 server `items.xml` (+ optional `items.otb`) → Canary `canary-3.2.0/data/items/items.xml`.
    - **Outfits**: TFS 1.4.2 server `data/XML/outfits.xml` → Canary `canary-3.2.0/data/XML/outfits.xml`.

- **Items migration (TFS 1.4.2 → Canary)**
  - Implemented in `ItemsMigration.cs` and `MigrateItemsWindow`.
  - Supports:
    - `id` and `fromid`/`toid` ranges.
    - Optional filter by old `items.otb` (only items present in the OTB are migrated).
    - Merge of duplicated IDs (last non-empty name/article/plural wins, attributes merged with last value per key).
    - Attribute normalization to lowercase keys and safe filtering (removes attributes that Canary does not support or that only generate warnings).

- **Outfits migration (TFS 1.4.2 → Canary)**
  - Implemented in `OutfitsMigration.cs` and `MigrateOutfitsWindow`.
  - Reads old `outfits.xml` and writes a Canary-compatible `outfits.xml` preserving:
    - `type`, `looktype`, `name`, `premium`, `unlocked`, `enabled`, and optional `from`.

- **Sprite / appearances conversion adjustments**
  - `DatEditor` import pipeline updated to:
    - Normalize sprite sizes for sheets to the closest supported size (padding with transparent background when needed).
    - Keep a consistent global sprite ID counter and avoid reusing `0` as a valid sprite ID.
    - Make the remapping from legacy sprite indices to new global IDs explicit and fail-fast instead of silently writing `sprite id 0`.
  - `AssetsConverter` updated to:
    - Merge multi-tile sprites into composed bitmaps (Beast-style) and keep pattern/animation fields consistent with OTClient’s expectations.
    - Apply bounding boxes for composed sprites and remap pattern fields for correct indexing.
    - Generate deterministic sprite sheets and `catalog-content.json` compatible with OTClient 4.0.
    - **Replace any remaining `sprite id 0` in `appearance.dat` by a shared transparent sprite**:
      - A single transparent sprite bitmap is created and assigned a fresh ID.
      - All zero entries in `SpriteInfo.SpriteId` across objects/outfits/effects/missiles are rewritten to this transparent ID.
      - This prevents OTClient from logging “Failed to fetch sprite id 0 for thing …” while keeping the visuals safely invisible where data is missing.
  
  Please note that this is far from being 100% perfect; I did this to get a head start on one of my projects.
