# STS2MCP Action Reference

This file is generated from `McpAction` and `McpActionField` attributes in the C# source.
Run `python scripts/generate_action_docs.py` after changing action metadata.

## Combat

### `end_turn`

End the player's combat turn.

No fields.

### `play_card`

Play a card from the player's hand.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `card_index` | `int` | yes | 0-based index in the current hand. |
| `target` | `string` | no | Target entity_id for single-target cards. |

## Debug

### `debug_force_play_phase`

Force combat back into player play phase.

No fields.

### `debug_start_encounter`

Start a specific debug encounter.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `encounter` | `string` | yes | Encounter id or name, such as ChompersNormal. |

### `return_to_main_menu`

Return the current game to the main menu.

No fields.

## Event

### `advance_dialogue`

Advance event dialogue when a dialogue-only event is waiting.

No fields.

### `choose_event_option`

Choose an event option.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | `int` | yes | 0-based option index from next_options. |

### `crystal_sphere_click_cell`

Click a Crystal Sphere cell.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `x` | `int` | yes | Cell X coordinate. |
| `y` | `int` | yes | Cell Y coordinate. |

### `crystal_sphere_proceed`

Proceed from the Crystal Sphere event.

No fields.

### `crystal_sphere_set_tool`

Set the active Crystal Sphere tool.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `tool` | `int` | yes | Tool id to select. |

## General

### `discard_potion`

Discard a potion from the player's potion slots.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `slot` | `int` | yes | Potion slot index. |

### `use_potion`

Use a potion from the player's potion slots.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `slot` | `int` | yes | Potion slot index. |
| `target` | `string` | no | Target entity_id for targetable potions. |

## Map

### `choose_map_node`

Travel to an available map node.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | `int` | yes | 0-based map node option index from next_options. |

## Menu

### `menu_select`

Select a menu, popup, character-select, or FTUE option.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `option` | `string` | yes | Option id or advertised menu option. |
| `seed` | `string` | no | Optional custom seed for character select. |

## Multiplayer

### `end_turn`

Mark the local multiplayer player ready to end turn.

No fields.

### `undo_end_turn`

Undo the local multiplayer end-turn ready state.

No fields.

## Navigation

### `proceed`

Press the active proceed button.

No fields.

## Rest

### `choose_rest_option`

Choose a rest site option.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | `int` | yes | 0-based rest option index from next_options. |

## Reward

### `claim_reward`

Claim a reward from the rewards screen.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | `int` | yes | 0-based claimable reward index. |

### `select_card_reward`

Select a card from the card reward screen.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `card_index` | `int` | yes | 0-based card reward index. |

### `skip_card_reward`

Skip the current card reward.

No fields.

## Selection

### `cancel_bundle_selection`

Cancel the active bundle selection.

No fields.

### `cancel_selection`

Cancel the active card selection screen.

No fields.

### `combat_confirm_selection`

Confirm the active combat hand selection UI.

No fields.

### `combat_select_card`

Select a card in the active combat hand selection UI.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | `int` | yes | 0-based card index. |

### `confirm_bundle_selection`

Confirm the active bundle selection.

No fields.

### `confirm_selection`

Confirm the active card selection screen.

No fields.

### `select_bundle`

Select a bundle option.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | `int` | yes | 0-based bundle option index. |

### `select_card`

Select or toggle a card in the active card selection screen.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | `int` | yes | 0-based card index in the active selection screen. |

### `select_relic`

Select a relic from the active relic selection screen.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | `int` | yes | 0-based relic index. |

### `skip_relic_selection`

Skip the active relic selection screen.

No fields.

## Shop

### `shop_purchase`

Purchase an item from the current shop.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | `int` | yes | 0-based shop item index. |

## Treasure

### `claim_treasure_relic`

Claim a relic from a treasure room.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | `int` | yes | 0-based treasure relic index. |
