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

| `state_type`             | Screen                                                  | Available Actions                                                            | Description                                                                                                      |
| ------------------------ | ------------------------------------------------------- | ---------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `menu`                   | Main menu, submenu, character select, or blocking popup | `menu_select`                                                                | No active run is required. Options are advertised by state and accepted case-insensitively.                      |
| `unknown`                | Unrecognized room or null state                         | none                                                                         | The mod cannot identify an actionable screen.                                                                    |
| `monster / elite / boss` | Combat                                                  | `play_card, use_potion, discard_potion, end_turn`                            | Combat state includes player energy, hand, piles, enemies, intents, powers, and combat-only character resources. |
| `hand_select`            | In-combat hand card selection                           | `combat_select_card, combat_confirm_selection`                               | Used for prompts that exhaust, discard, upgrade, or otherwise choose cards from hand.                            |
| `rewards`                | Rewards screen                                          | `claim_reward, proceed`                                                      | Post-combat and event reward screens. Card rewards open a separate card_reward state.                            |
| `card_reward`            | Card reward selection                                   | `select_card_reward, skip_card_reward`                                       | Pick one offered card or skip when allowed.                                                                      |
| `map`                    | Map navigation                                          | `choose_map_node`                                                            | Choose from next_options. Boss metadata is included when exposed by the run map.                                 |
| `event`                  | Event or Ancient encounter                              | `choose_event_option, advance_dialogue`                                      | Event options use the indexes advertised by state; dialogue-only events use advance_dialogue.                    |
| `rest_site`              | Rest site                                               | `choose_rest_option, proceed`                                                | Choose rest, smith, or other rest-site options.                                                                  |
| `shop`                   | Shop                                                    | `shop_purchase, proceed`                                                     | The shop inventory is flattened into indexed purchasable items.                                                  |
| `fake_merchant`          | Fake Merchant event shop                                | `shop_purchase, proceed`                                                     | Relic-only shop disguised as an event; if combat starts, the merchant disappears.                                |
| `treasure`               | Treasure room                                           | `claim_treasure_relic, proceed`                                              | Open chest and claim indexed relics.                                                                             |
| `card_select`            | Deck card selection overlay                             | `select_card, confirm_selection, cancel_selection`                           | Grid screens toggle selection; choose-a-card screens pick immediately.                                           |
| `bundle_select`          | Card bundle choice overlay                              | `select_bundle, confirm_bundle_selection, cancel_bundle_selection`           | Open a bundle preview, confirm it, or cancel it.                                                                 |
| `relic_select`           | Relic choice overlay                                    | `select_relic, skip_relic_selection`                                         | Boss relic and similar relic choice screens.                                                                     |
| `crystal_sphere`         | Crystal Sphere minigame                                 | `crystal_sphere_set_tool, crystal_sphere_click_cell, crystal_sphere_proceed` | Select a tool, reveal cells, then finish the minigame.                                                           |
| `game_over`              | Run ended                                               | `menu_select`                                                                | Use menu_select with main_menu. Continue is intentionally not actionable.                                        |
| `overlay`                | Unhandled overlay catch-all                             | none                                                                         | Manual interaction may be required.                                                                              |

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

STS2MCP records successful singleplayer API commands to a `.replay` file for the active run. Replay files are newline-delimited JSON: each line is the original command payload accepted by the HTTP API. Files are written under the game user-data directory at `STS2MCP/replays/` with names in the form `{seed}_{yyyyMMdd_HHmmss}.replay`, where the timestamp comes from the run's saved `start_time`. Commands issued before the run save exists, such as the `menu_select` sequence that starts a seeded run, are buffered and flushed once STS2MCP can read the run seed and start time.

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

### Legacy RunReplays

#### `get_replay_status`

Read the current external RunReplays playback status.

No fields.

Notes:

- Legacy/external interop: requires RunReplays to be installed and enabled.

#### `get_replays`

List replay floors discoverable through the external RunReplays mod.

No fields.

Notes:

- Legacy/external interop: requires RunReplays to be installed and enabled. Prefer STS2MCP .replay recording for new replay data.

#### `start_replay`

Start playback through the external RunReplays mod.

| Field         | Type     | Required | Description                                                                      |
| ------------- | -------- | -------- | -------------------------------------------------------------------------------- |
| `target`      | `string` | no       | Compact replay target, such as SEED or SEED:floor_N.                             |
| `seed`        | `string` | no       | Replay seed to launch when target is omitted.                                    |
| `floor`       | `int`    | no       | Optional floor number to replay to for seed.                                     |
| `start_floor` | `int`    | no       | Optional saved floor to load before replaying to floor; requires seed and floor. |

Notes:

- Legacy/external interop: requires RunReplays to be installed and enabled. Prefer STS2MCP .replay recording for new replay data.

### Map

#### `choose_map_node`

Travel to an available map node.

| Field   | Type  | Required | Description                                      |
| ------- | ----- | -------- | ------------------------------------------------ |
| `index` | `int` | yes      | 0-based map node option index from next_options. |

### Menu

#### `menu_select`

Select a menu, popup, character-select, or FTUE option.

| Field    | Type     | Required | Description                                |
| -------- | -------- | -------- | ------------------------------------------ |
| `option` | `string` | yes      | Option id or advertised menu option.       |
| `seed`   | `string` | no       | Optional custom seed for character select. |

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

### Rest

#### `choose_rest_option`

Choose a rest site option.

| Field   | Type  | Required | Description                                  |
| ------- | ----- | -------- | -------------------------------------------- |
| `index` | `int` | yes      | 0-based rest option index from next_options. |

### Reward

#### `claim_reward`

Claim a reward from the rewards screen.

| Field   | Type  | Required | Description                     |
| ------- | ----- | -------- | ------------------------------- |
| `index` | `int` | yes      | 0-based claimable reward index. |

#### `select_card_reward`

Select a card from the card reward screen.

| Field        | Type  | Required | Description                |
| ------------ | ----- | -------- | -------------------------- |
| `card_index` | `int` | yes      | 0-based card reward index. |

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

| Field   | Type  | Required | Description                                        |
| ------- | ----- | -------- | -------------------------------------------------- |
| `index` | `int` | yes      | 0-based card index in the active selection screen. |

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

| Field   | Type  | Required | Description                   |
| ------- | ----- | -------- | ----------------------------- |
| `index` | `int` | yes      | 0-based treasure relic index. |
