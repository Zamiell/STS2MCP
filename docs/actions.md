# STS2MCP API Reference

This file is generated from C# metadata attributes in the source.
Run `python scripts/generate_action_docs.py` after changing action metadata.

HTTP API served by the STS2_MCP mod on localhost:15526. There is no authentication; this is intended for local use only. Singleplayer and multiplayer endpoints are mutually exclusive: calling the singleplayer endpoint during a multiplayer run, or the multiplayer endpoint during a singleplayer run, returns HTTP 409.

## Endpoints

| Method | Path                   | Description                                          |
| ------ | ---------------------- | ---------------------------------------------------- |
| `GET`  | `/api/v1/singleplayer` | Read current singleplayer game state.                |
| `POST` | `/api/v1/singleplayer` | Perform a singleplayer game action.                  |
| `GET`  | `/api/v1/multiplayer`  | Read current multiplayer game state.                 |
| `POST` | `/api/v1/multiplayer`  | Perform a multiplayer action.                        |
| `GET`  | `/api/v1/profile`      | Read current profile progress.                       |
| `GET`  | `/api/v1/compendium`   | Read Compendium-shaped profile progress.             |
| `GET`  | `/api/v1/wiki`         | Fuzzy-search discovered card and relic wiki entries. |
| `GET`  | `/api/v1/profiles`     | List profile slots.                                  |
| `POST` | `/api/v1/profiles`     | Switch or delete profile slots.                      |

## Query Parameters

### `/api/v1/multiplayer`

| Parameter | Values           | Default | Description      |
| --------- | ---------------- | ------- | ---------------- |
| `format`  | `json, markdown` | `json`  | Response format. |

### `/api/v1/singleplayer`

| Parameter | Values           | Default | Description      |
| --------- | ---------------- | ------- | ---------------- |
| `format`  | `json, markdown` | `json`  | Response format. |

### `/api/v1/wiki`

| Parameter           | Values             | Default    | Description                                                         |
| ------------------- | ------------------ | ---------- | ------------------------------------------------------------------- |
| `query or q`        | `text`             | `required` | Fuzzy search text, such as ironclad perfect strike or silver spoon. |
| `item_type or type` | `all, card, relic` | `all`      | Restricts search to one wiki kind.                                  |
| `limit`             | `integer`          | `10`       | Maximum returned entries; clamped by the mod.                       |

## Game State

### Common Game State

Game-state responses include `state_type`. Run states include run metadata such as `act`, `floor`, and `ascension`, plus a `player` object with HP, block, gold, relics, potions, `max_potion_slots`, and status effects. Combat states add `energy`, `max_energy`, hand, draw/discard/exhaust pile counts and visible pile contents, enemies, intents, orbs for Defect, pets for Necrobinder, and character-specific resources such as Regent stars.

### Object Shapes

Cards in hand include `index`, `id`, `name`, `type`, `cost`, `star_cost`, `description`, `target_type`, `can_play`, `unplayable_reason`, `is_upgraded`, and `keywords`. Potions include `slot`, `target_type`, and `can_use_in_combat`. Relics and powers include ids, names, descriptions, counters or amounts, and keywords when available. Entity ids are stable uppercase identifiers with numeric suffixes when more than one copy exists, such as `JAW_WORM_0`.

### State Types

| `state_type`             | Screen                                                  | Available Actions                                                            | Description                                                                                                                            |
| ------------------------ | ------------------------------------------------------- | ---------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `menu`                   | Main menu, submenu, character select, or blocking popup | `menu_select`                                                                | No active run is required. Options are advertised by state and accepted case-insensitively.                                            |
| `unknown`                | Unrecognized room or null state                         | none                                                                         | The mod cannot identify an actionable screen.                                                                                          |
| `monster / elite / boss` | Combat                                                  | `play_card, use_potion, discard_potion, end_turn`                            | Combat state includes player energy, hand, piles, enemies, intents, powers, and combat-only character resources.                       |
| `hand_select`            | In-combat hand card selection                           | `combat_select_card, combat_confirm_selection`                               | Used for prompts that exhaust, discard, upgrade, or otherwise choose cards from hand.                                                  |
| `rewards`                | Rewards screen                                          | `claim_reward, proceed`                                                      | Post-combat and event reward screens. Card rewards open a separate card_reward state.                                                  |
| `card_reward`            | Card reward selection                                   | `select_card_reward, skip_card_reward`                                       | Pick one offered card or skip when allowed.                                                                                            |
| `map`                    | Map navigation                                          | `choose_map_node`                                                            | Choose from next_options by index, or by col/row for replay-stable map routing. Boss metadata is included when exposed by the run map. |
| `event`                  | Event or Ancient encounter                              | `choose_event_option, choose_event_option_by_title_key, advance_dialogue`    | Event options use the indexes advertised by state, or title_key for saved replay choices; dialogue-only events use advance_dialogue.   |
| `rest_site`              | Rest site                                               | `choose_rest_option, proceed`                                                | Choose rest, smith, or other rest-site options.                                                                                        |
| `shop`                   | Shop                                                    | `shop_purchase, proceed`                                                     | The shop inventory is flattened into indexed purchasable items.                                                                        |
| `fake_merchant`          | Fake Merchant event shop                                | `shop_purchase, proceed`                                                     | Relic-only shop disguised as an event; if combat starts, the merchant disappears.                                                      |
| `treasure`               | Treasure room                                           | `claim_treasure_relic, proceed`                                              | Open chest and claim indexed relics.                                                                                                   |
| `card_select`            | Deck card selection overlay                             | `select_card, confirm_selection, cancel_selection`                           | Grid screens toggle selection; choose-a-card screens pick immediately.                                                                 |
| `bundle_select`          | Card bundle choice overlay                              | `select_bundle, confirm_bundle_selection, cancel_bundle_selection`           | Open a bundle preview, confirm it, or cancel it.                                                                                       |
| `relic_select`           | Relic choice overlay                                    | `select_relic, skip_relic_selection`                                         | Boss relic and similar relic choice screens.                                                                                           |
| `crystal_sphere`         | Crystal Sphere minigame                                 | `crystal_sphere_set_tool, crystal_sphere_click_cell, crystal_sphere_proceed` | Select a tool, reveal cells, then finish the minigame.                                                                                 |
| `game_over`              | Run ended                                               | `menu_select`                                                                | Use menu_select with main_menu. Continue is intentionally not actionable.                                                              |
| `overlay`                | Unhandled overlay catch-all                             | none                                                                         | Manual interaction may be required.                                                                                                    |

### State Examples

These examples show representative JSON shapes. Fields may be omitted, null, or expanded depending on the current screen, character, and run state.

#### `menu`

Main menu, submenu, character select, and blocking popup states advertise normalized options for `menu_select`.

```json
{
  "state_type": "menu",
  "menu": {
    "screen": "main_menu",
    "options": [
      {
        "option": "singleplayer",
        "label": "Singleplayer"
      },
      {
        "option": "multiplayer",
        "label": "Multiplayer"
      }
    ]
  }
}
```

#### `unknown`

Unknown states are returned when the mod cannot map the current game screen to an actionable API state.

```json
{
  "state_type": "unknown",
  "message": "Unrecognized room or screen."
}
```

#### `monster / elite / boss`

`monster`, `elite`, and `boss` are combat states with the same response shape. Single-target cards and potions use enemy `entity_id` values.

```json
{
  "state_type": "monster",
  "battle": {
    "round": 1,
    "turn": "player",
    "is_play_phase": true,
    "enemies": [
      {
        "entity_id": "JAW_WORM_0",
        "combat_id": 1,
        "name": "Jaw Worm",
        "hp": 40,
        "max_hp": 40,
        "block": 0,
        "status": [],
        "intents": [
          {
            "type": "Attack",
            "label": "11",
            "title": "Aggressive",
            "description": "This enemy intends to Attack for 11 damage."
          }
        ]
      }
    ]
  },
  "run": {
    "act": 1,
    "floor": 2,
    "ascension": 0
  },
  "player": {
    "character": "The Ironclad",
    "hp": 80,
    "max_hp": 80,
    "block": 0,
    "energy": 3,
    "max_energy": 3,
    "hand": [
      {
        "index": 0,
        "id": "STRIKE_IRONCLAD",
        "name": "Strike",
        "type": "Attack",
        "cost": "1",
        "target_type": "AnyEnemy",
        "can_play": true,
        "unplayable_reason": null,
        "is_upgraded": false
      }
    ],
    "draw_pile_count": 5,
    "discard_pile_count": 0,
    "exhaust_pile_count": 0,
    "gold": 99,
    "status": [],
    "relics": [],
    "potions": [],
    "max_potion_slots": 3
  }
}
```

#### `hand_select`

In-combat hand selection prompts choose from the current hand and use `combat_select_card` followed by `combat_confirm_selection` when confirmation is enabled.

```json
{
  "state_type": "hand_select",
  "hand_select": {
    "prompt": "Choose a card to discard.",
    "cards": [
      {
        "index": 0,
        "id": "STRIKE_IRONCLAD",
        "name": "Strike"
      }
    ],
    "can_confirm": true,
    "can_cancel": false
  },
  "run": {
    "act": 1,
    "floor": 4,
    "ascension": 0
  },
  "player": {
    "hp": 70,
    "max_hp": 80,
    "gold": 120
  }
}
```

#### `rewards`

Reward screens store claimable rewards under `rewards.items`. Claiming rewards shifts later reward indexes; `claim_reward_by_match` can use stable fields such as `type`, `gold_amount`, `potion_id`, or `relic_id`.

```json
{
  "state_type": "rewards",
  "rewards": {
    "items": [
      {
        "index": 0,
        "type": "gold",
        "description": "17 Gold",
        "gold_amount": 17
      },
      {
        "index": 1,
        "type": "potion",
        "description": "Fire Potion",
        "potion_id": "FIRE_POTION"
      },
      {
        "index": 2,
        "type": "card",
        "description": "Add a card to your deck."
      }
    ],
    "can_proceed": true
  },
  "run": {
    "act": 1,
    "floor": 2,
    "ascension": 0
  },
  "player": {
    "hp": 74,
    "max_hp": 80,
    "gold": 99,
    "relics": [],
    "potions": []
  }
}
```

#### `card_reward`

Card rewards list offered cards under `card_reward.cards`. Use `select_card_reward` by `card_index`, `select_card_reward_by_id`, or `skip_card_reward` when `can_skip` is true.

```json
{
  "state_type": "card_reward",
  "card_reward": {
    "cards": [
      {
        "index": 0,
        "id": "POMMEL_STRIKE",
        "name": "Pommel Strike",
        "type": "Attack",
        "cost": "1",
        "description": "Deal 9 damage. Draw 1 card.",
        "rarity": "Common",
        "is_upgraded": false
      },
      {
        "index": 1,
        "id": "SHRUG_IT_OFF",
        "name": "Shrug It Off",
        "type": "Skill",
        "cost": "1",
        "description": "Gain 8 Block. Draw 1 card.",
        "rarity": "Common",
        "is_upgraded": false
      }
    ],
    "can_skip": true
  },
  "run": {
    "act": 1,
    "floor": 2,
    "ascension": 0
  },
  "player": {
    "hp": 74,
    "max_hp": 80,
    "gold": 116
  }
}
```

#### `map`

Map states expose travel choices in `map.next_options`; pass the advertised `index` to `choose_map_node`, or pass `col` and `row` for replay-stable routing.

```json
{
  "state_type": "map",
  "map": {
    "current_position": {
      "col": 3,
      "row": 0,
      "type": "Ancient"
    },
    "visited": [
      {
        "col": 3,
        "row": 0,
        "type": "Ancient"
      }
    ],
    "next_options": [
      {
        "index": 0,
        "col": 1,
        "row": 1,
        "type": "Monster",
        "leads_to": [
          {
            "col": 1,
            "row": 2,
            "type": "Unknown"
          }
        ]
      },
      {
        "index": 1,
        "col": 6,
        "row": 1,
        "type": "Monster",
        "leads_to": [
          {
            "col": 6,
            "row": 2,
            "type": "Shop"
          }
        ]
      }
    ],
    "nodes": []
  },
  "run": {
    "act": 1,
    "floor": 1,
    "ascension": 0
  },
  "player": {
    "hp": 80,
    "max_hp": 80,
    "gold": 99
  }
}
```

#### `event`

Event states expose visible event options with indexes for `choose_event_option` and localization keys for `choose_event_option_by_title_key`. A single Proceed option is still chosen through `choose_event_option`.

```json
{
  "state_type": "event",
  "event": {
    "event_id": "NEOW",
    "event_name": "Neow",
    "is_ancient": true,
    "in_dialogue": false,
    "body": null,
    "options": [
      {
        "index": 0,
        "title_key": "NEOW.options.MAX_HP.title",
        "title": "Nutritious Oyster",
        "description": "Raise your Max HP by 11.",
        "is_locked": false,
        "is_proceed": false,
        "was_chosen": false,
        "relic_name": "Nutritious Oyster"
      }
    ]
  },
  "run": {
    "act": 1,
    "floor": 1,
    "ascension": 0
  },
  "player": {
    "hp": 80,
    "max_hp": 80,
    "gold": 99
  }
}
```

#### `rest_site`

Rest site states list enabled campfire choices in `rest_site.options`. Use `choose_rest_option` with an option `index`, then `proceed` once `can_proceed` is true.

```json
{
  "state_type": "rest_site",
  "rest_site": {
    "options": [
      {
        "index": 0,
        "id": "HEAL",
        "name": "Rest",
        "description": "Heal for 30% of your Max HP (24).",
        "is_enabled": true
      },
      {
        "index": 1,
        "id": "SMITH",
        "name": "Smith",
        "description": "Upgrade a card in your Deck.",
        "is_enabled": true
      }
    ],
    "can_proceed": false
  },
  "run": {
    "act": 1,
    "floor": 9,
    "ascension": 0
  },
  "player": {
    "hp": 43,
    "max_hp": 80,
    "gold": 150
  }
}
```

#### `shop`

Shop states flatten purchasable cards, relics, potions, and services into `shop.items`; use the item `index` with `shop_purchase`.

```json
{
  "state_type": "shop",
  "shop": {
    "items": [
      {
        "index": 0,
        "type": "card",
        "name": "Pommel Strike",
        "price": 51,
        "is_enabled": true
      },
      {
        "index": 1,
        "type": "relic",
        "name": "Anchor",
        "price": 150,
        "is_enabled": true
      },
      {
        "index": 2,
        "type": "remove_card",
        "name": "Card Removal",
        "price": 75,
        "is_enabled": true
      }
    ],
    "can_proceed": true
  },
  "run": {
    "act": 1,
    "floor": 8,
    "ascension": 0
  },
  "player": {
    "hp": 65,
    "max_hp": 80,
    "gold": 190
  }
}
```

#### `fake_merchant`

Fake Merchant uses the shop-like item shape but appears as an event shop state.

```json
{
  "state_type": "fake_merchant",
  "fake_merchant": {
    "items": [
      {
        "index": 0,
        "type": "relic",
        "name": "Bag of Marbles",
        "price": 99,
        "is_enabled": true
      }
    ],
    "can_proceed": true
  },
  "run": {
    "act": 1,
    "floor": 6,
    "ascension": 0
  },
  "player": {
    "hp": 70,
    "max_hp": 80,
    "gold": 120
  }
}
```

#### `treasure`

Treasure states expose chest relics after opening. Use `claim_treasure_relic` for indexed relics, then `proceed`.

```json
{
  "state_type": "treasure",
  "treasure": {
    "is_open": true,
    "relics": [
      {
        "index": 0,
        "id": "BAG_OF_PREPARATION",
        "name": "Bag of Preparation",
        "description": "At the start of each combat, draw 2 additional cards."
      }
    ],
    "can_proceed": false
  },
  "run": {
    "act": 1,
    "floor": 10,
    "ascension": 0
  },
  "player": {
    "hp": 80,
    "max_hp": 80,
    "gold": 175
  }
}
```

#### `card_select`

Deck and pile selection overlays list cards in `card_select.cards`. `select_card` toggles grid choices but picks immediately on choose-a-card screens.

```json
{
  "state_type": "card_select",
  "card_select": {
    "screen_type": "NCombatPileCardSelectScreen",
    "prompt": "Choose a card to put into your Hand.",
    "cards": [
      {
        "index": 0,
        "id": "DEFEND_IRONCLAD",
        "name": "Defend",
        "type": "Skill",
        "cost": "1",
        "is_upgraded": false
      },
      {
        "index": 1,
        "id": "STRIKE_IRONCLAD",
        "name": "Strike",
        "type": "Attack",
        "cost": "1",
        "is_upgraded": false
      }
    ],
    "preview_showing": false,
    "can_cancel": false,
    "can_confirm": false
  },
  "run": {
    "act": 1,
    "floor": 6,
    "ascension": 0
  },
  "player": {
    "hp": 65,
    "max_hp": 80,
    "gold": 130
  }
}
```

#### `bundle_select`

Bundle selection screens expose previewable bundle choices. Open a bundle with `select_bundle`, then confirm or cancel the preview.

```json
{
  "state_type": "bundle_select",
  "bundle_select": {
    "bundles": [
      {
        "index": 0,
        "name": "Attack Bundle",
        "cards": [
          {
            "id": "STRIKE_IRONCLAD",
            "name": "Strike"
          }
        ],
        "is_selected": false
      }
    ],
    "can_confirm": false,
    "can_cancel": true
  },
  "run": {
    "act": 1,
    "floor": 1,
    "ascension": 0
  },
  "player": {
    "hp": 80,
    "max_hp": 80,
    "gold": 99
  }
}
```

#### `relic_select`

Relic selection screens list relic choices under `relic_select.relics`; use `select_relic` or `skip_relic_selection` when skipping is allowed.

```json
{
  "state_type": "relic_select",
  "relic_select": {
    "relics": [
      {
        "index": 0,
        "id": "BLACK_BLOOD",
        "name": "Black Blood",
        "description": "Replaces Burning Blood."
      },
      {
        "index": 1,
        "id": "SOZU",
        "name": "Sozu",
        "description": "Gain energy. You can no longer obtain potions."
      }
    ],
    "can_skip": false
  },
  "run": {
    "act": 1,
    "floor": 17,
    "ascension": 0
  },
  "player": {
    "hp": 20,
    "max_hp": 80,
    "gold": 200
  }
}
```

#### `crystal_sphere`

Crystal Sphere exposes the selected tool, cells, and whether the minigame can be finished.

```json
{
  "state_type": "crystal_sphere",
  "crystal_sphere": {
    "selected_tool": "reveal",
    "tools": [
      {
        "id": "reveal",
        "is_enabled": true
      }
    ],
    "cells": [
      {
        "row": 0,
        "col": 0,
        "state": "hidden",
        "value": null
      }
    ],
    "can_proceed": false
  },
  "run": {
    "act": 2,
    "floor": 25,
    "ascension": 0
  },
  "player": {
    "hp": 60,
    "max_hp": 80,
    "gold": 180
  }
}
```

#### `game_over`

Game-over states intentionally expose only menu navigation back out of the ended run.

```json
{
  "state_type": "game_over",
  "game_over": {
    "message": "Run ended.",
    "options": ["main_menu"]
  },
  "run": {
    "act": 1,
    "floor": 17,
    "ascension": 0
  },
  "player": {
    "character": "The Ironclad",
    "hp": 0,
    "max_hp": 80,
    "gold": 200,
    "relics": [],
    "potions": []
  }
}
```

#### `overlay`

Overlay is a catch-all for recognized but unsupported overlays. Manual interaction may be required.

```json
{
  "state_type": "overlay",
  "overlay": {
    "name": "UnhandledOverlay",
    "message": "Manual interaction may be required."
  }
}
```

## Menu Details

`menu_select` chooses advertised menu options case-insensitively. Submenus include `singleplayer`: `standard`, `daily`, `custom`, `back`; `multiplayer`: `host`, `join`, `load`, `abandon`, `back`; `multiplayer_host`: `standard`, `daily`, `custom`, `back`; `multiplayer_join`: `refresh`, `back`, `join_<index>`, `join_<player_id>`; `multiplayer_load_lobby`: `confirm` or `embark`, `unready`, `back`; `profile_select`: `profile_1`, `profile_2`, `profile_3`, `back`; `character_select`: character ids or names, `back`, `confirm` or `embark`, and multiplayer `unready` after readying. Blocking popups expose normalized labels such as `ignore`, `confirm`, `cancel`, or `back`. Timeline may be blocked when obtained epochs still require manual reveal; state then includes `blocked_options` and selecting `timeline` returns `manual_action_required` with `pending_epoch_ids`.

## Action Guidance

All POST bodies use an `action` field. Successful responses include `status: ok`; errors include `status: error` or an `error` field. Playing a card removes it from hand and shifts later indexes, so either re-read state between plays or play from highest hand index to lowest. Reward indexes can also shift after claiming, so claiming from highest index to lowest is usually safest. `use_potion` and `discard_potion` work whenever potions are accessible, not only in combat. Single-target cards and potions require a target `entity_id`.

## Selection Screens

`select_card` toggles cards on grid selection screens such as transform, upgrade, remove, and select prompts. On choose-a-card screens created by potions or effects, `select_card` picks immediately. `confirm_selection` confirms grid selections when a preview or confirm button is present. `cancel_selection` cancels a preview, skips choose-a-card screens when possible, or closes cancellable selection screens.

## Profiles And Compendium

`GET /api/v1/profile` returns persistent progress for the active profile, including character stats, discoveries, achievements, epochs, and global run totals. `GET /api/v1/compendium` groups profile data like the in-game Compendium. `current_run.run_id` uses `{save_scope}:profile{profile_id}:{start_time}`; use `run_id` to identify a concrete attempt and `seed` to group generated run content. Run history is resolved from the active Steam account profile save root and capped to the 20 most recent history files.

## Wiki Search

`GET /api/v1/wiki` is intentionally selective: `query` is required and results are limited to discovered cards and relics for the active profile. `item_type` can be `all`, `card`, or `relic`; `limit` defaults to 10. Card results include `base` and `upgraded` objects when the card can be upgraded.

## Profile Slots

`GET /api/v1/profiles` lists all three profile slots and the active profile. `POST /api/v1/profiles` supports action `switch` with `profile_id` 1-3 and action `delete` with `profile_id` 1-3. Switching is rejected during a run. Deleting the active profile is rejected.

## Replay Recording

STS2MCP writes `.replay` files for active singleplayer runs. Replay files are newline-delimited JSON commands that can be sent back to the HTTP API. For runs started through the in-game UI, STS2MCP observes `current_run.save` and writes the seeded startup sequence, Neow choice, and map/reward choices once those decisions are visible in the save. Combat commands are observed as the player acts, held in memory during the fight, and appended only after combat ends so closing and reopening mid-combat leaves the replay at the pre-combat state. Files are written under the game user-data directory at `STS2MCP/replays/` with names in the form `{seed}_{yyyyMMdd_HHmmss}.replay`, where the timestamp comes from the run's saved `start_time`.

## Multiplayer Additions

Multiplayer state includes `net_type`, local player identity, player ready state, lobby roster, and multiplayer-specific map, event, treasure, and combat coordination fields when available. Multiplayer `end_turn` marks the local player ready; the turn advances once all players are committed. `undo_end_turn` retracts that ready state before commitment.

## Actions

### Combat

#### `end_turn`

End the player's combat turn.

No fields.

#### `play_card`

Play a card from the player's hand.

| Field        | Type     | Required | Description                               |
| ------------ | -------- | -------- | ----------------------------------------- |
| `card_index` | `int`    | yes      | 0-based index in the current hand.        |
| `target`     | `string` | no       | Target entity_id for single-target cards. |

### Debug

#### `debug_force_play_phase`

Force combat back into player play phase.

No fields.

#### `debug_start_encounter`

Start a specific debug encounter.

| Field       | Type     | Required | Description                                   |
| ----------- | -------- | -------- | --------------------------------------------- |
| `encounter` | `string` | yes      | Encounter id or name, such as ChompersNormal. |

#### `return_to_main_menu`

Return the current game to the main menu.

No fields.

### Event

#### `advance_dialogue`

Advance event dialogue when a dialogue-only event is waiting.

No fields.

#### `choose_event_option`

Choose an event option.

| Field   | Type  | Required | Description                             |
| ------- | ----- | -------- | --------------------------------------- |
| `index` | `int` | yes      | 0-based option index from next_options. |

#### `choose_event_option_by_title_key`

Choose an event option by localization title key.

| Field       | Type     | Required | Description                                  |
| ----------- | -------- | -------- | -------------------------------------------- |
| `title_key` | `string` | yes      | Localization key for the event option title. |

#### `crystal_sphere_click_cell`

Click a Crystal Sphere cell.

| Field | Type  | Required | Description        |
| ----- | ----- | -------- | ------------------ |
| `x`   | `int` | yes      | Cell X coordinate. |
| `y`   | `int` | yes      | Cell Y coordinate. |

#### `crystal_sphere_proceed`

Proceed from the Crystal Sphere event.

No fields.

#### `crystal_sphere_set_tool`

Set the active Crystal Sphere tool.

| Field  | Type  | Required | Description        |
| ------ | ----- | -------- | ------------------ |
| `tool` | `int` | yes      | Tool id to select. |

### General

#### `discard_potion`

Discard a potion from the player's potion slots.

| Field  | Type  | Required | Description        |
| ------ | ----- | -------- | ------------------ |
| `slot` | `int` | yes      | Potion slot index. |

#### `use_potion`

Use a potion from the player's potion slots.

| Field    | Type     | Required | Description                              |
| -------- | -------- | -------- | ---------------------------------------- |
| `slot`   | `int`    | yes      | Potion slot index.                       |
| `target` | `string` | no       | Target entity_id for targetable potions. |

### Map

#### `choose_map_node`

Travel to an available map node.

| Field   | Type  | Required | Description                                             |
| ------- | ----- | -------- | ------------------------------------------------------- |
| `index` | `int` | no       | 0-based map node option index from next_options.        |
| `col`   | `int` | no       | Target map node column. Preferred for replay stability. |
| `row`   | `int` | no       | Target map node row. Preferred for replay stability.    |

### Menu

#### `menu_select`

Select a menu, popup, character-select, or FTUE option.

| Field       | Type     | Required | Description                                           |
| ----------- | -------- | -------- | ----------------------------------------------------- |
| `option`    | `string` | yes      | Option id or advertised menu option.                  |
| `seed`      | `string` | no       | Optional custom seed for character select.            |
| `ascension` | `int`    | no       | Optional ascension level for character select embark. |

### Multiplayer

#### `end_turn`

Mark the local multiplayer player ready to end turn.

No fields.

#### `undo_end_turn`

Undo the local multiplayer end-turn ready state.

No fields.

### Navigation

#### `proceed`

Press the active proceed button.

No fields.

### Replay

#### `cancel_replay`

Cancel the active replay playback.

No fields.

#### `get_replay_status`

Get the current replay playback status.

No fields.

#### `start_replay`

Start playing a newline-delimited JSON replay file.

| Field                     | Type     | Required | Description                                                                        |
| ------------------------- | -------- | -------- | ---------------------------------------------------------------------------------- |
| `path`                    | `string` | no       | Absolute replay file path, or file name under the STS2MCP replay directory.        |
| `name`                    | `string` | no       | Replay file name under the STS2MCP replay directory.                               |
| `force`                   | `bool`   | no       | Cancel any currently running replay before starting this one.                      |
| `return_to_main_menu`     | `bool`   | no       | Return to the main menu before playing the first replay command. Defaults to true. |
| `command_timeout_seconds` | `number` | no       | Maximum time to wait for each replay command to become valid. Defaults to 30.      |

### Rest

#### `choose_rest_option`

Choose a rest site option.

| Field       | Type     | Required | Description                                  |
| ----------- | -------- | -------- | -------------------------------------------- |
| `index`     | `int`    | no       | 0-based rest option index from next_options. |
| `option_id` | `string` | no       | Rest option id, such as HEAL or SMITH.       |

### Reward

#### `claim_reward`

Claim a reward from the rewards screen.

| Field   | Type  | Required | Description                     |
| ------- | ----- | -------- | ------------------------------- |
| `index` | `int` | yes      | 0-based claimable reward index. |

#### `claim_reward_by_match`

Claim a reward by stable reward identity.

| Field         | Type     | Required | Description                                                      |
| ------------- | -------- | -------- | ---------------------------------------------------------------- |
| `type`        | `string` | yes      | Reward type, such as gold, potion, relic, card, or special_card. |
| `gold_amount` | `int`    | no       | Gold amount to match for gold rewards.                           |
| `potion_id`   | `string` | no       | Potion id to match for potion rewards.                           |
| `relic_id`    | `string` | no       | Relic id to match for relic rewards.                             |

#### `select_card_reward`

Select a card from the card reward screen.

| Field        | Type  | Required | Description                |
| ------------ | ----- | -------- | -------------------------- |
| `card_index` | `int` | yes      | 0-based card reward index. |

#### `select_card_reward_by_id`

Select a card from the card reward screen by id.

| Field     | Type     | Required | Description                                     |
| --------- | -------- | -------- | ----------------------------------------------- |
| `card_id` | `string` | yes      | Card id to select from the current card reward. |

#### `skip_card_reward`

Skip the current card reward.

No fields.

### Selection

#### `cancel_bundle_selection`

Cancel the active bundle selection.

No fields.

#### `cancel_selection`

Cancel the active card selection screen.

No fields.

#### `combat_confirm_selection`

Confirm the active combat hand selection UI.

No fields.

#### `combat_select_card`

Select a card in the active combat hand selection UI.

| Field   | Type  | Required | Description         |
| ------- | ----- | -------- | ------------------- |
| `index` | `int` | yes      | 0-based card index. |

#### `confirm_bundle_selection`

Confirm the active bundle selection.

No fields.

#### `confirm_selection`

Confirm the active card selection screen.

No fields.

#### `select_bundle`

Select a bundle option.

| Field   | Type  | Required | Description                  |
| ------- | ----- | -------- | ---------------------------- |
| `index` | `int` | yes      | 0-based bundle option index. |

#### `select_card`

Select or toggle a card in the active card selection screen.

| Field     | Type     | Required | Description                                         |
| --------- | -------- | -------- | --------------------------------------------------- |
| `index`   | `int`    | no       | 0-based card index in the active selection screen.  |
| `card_id` | `string` | no       | Card id to select from the active selection screen. |

#### `select_deck_card`

Select or toggle a deck card in the active card selection screen.

| Field        | Type     | Required | Description                                                 |
| ------------ | -------- | -------- | ----------------------------------------------------------- |
| `deck_index` | `int`    | no       | 0-based card index in the current deck.                     |
| `card_id`    | `string` | no       | Card id to select, or an optional assertion for deck_index. |

#### `select_relic`

Select a relic from the active relic selection screen.

| Field   | Type  | Required | Description          |
| ------- | ----- | -------- | -------------------- |
| `index` | `int` | yes      | 0-based relic index. |

#### `skip_relic_selection`

Skip the active relic selection screen.

No fields.

### Shop

#### `shop_purchase`

Purchase an item from the current shop.

| Field   | Type  | Required | Description              |
| ------- | ----- | -------- | ------------------------ |
| `index` | `int` | yes      | 0-based shop item index. |

### Treasure

#### `claim_treasure_relic`

Claim a relic from a treasure room.

| Field   | Type  | Required | Description                                                                   |
| ------- | ----- | -------- | ----------------------------------------------------------------------------- |
| `index` | `int` | no       | 0-based treasure relic index. Defaults to 0 when only one relic is claimable. |
