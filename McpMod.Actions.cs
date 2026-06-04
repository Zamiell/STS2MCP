using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.ProfileScreen;
using Godot;

namespace STS2_MCP;

public static partial class McpMod
{
    private static Dictionary<string, object?> ExecuteAction(string action, Dictionary<string, JsonElement> data)
    {
        if (!RunManager.Instance.IsInProgress)
            return Error("No run in progress");

        var runState = RunManager.Instance.DebugOnlyGetState()!;
        var player = LocalContext.GetMe(runState);
        if (player == null)
            return Error("Could not find local player");

        var tree = (Godot.Engine.GetMainLoop()) as SceneTree;
        if (tree?.Root != null && IsAnyFtueVisible(tree.Root))
            return Error("Blocking popup active. Use menu_select with one of the advertised popup options before gameplay actions.");

        return action switch
        {
            "return_to_main_menu" => ExecuteReturnToMainMenu(),
            "debug_start_encounter" => ExecuteDebugStartEncounter(runState, data),
            "debug_force_play_phase" => ExecuteDebugForcePlayPhase(),
            "play_card" => ExecutePlayCard(player, data),
            "use_potion" => ExecuteUsePotion(player, data),
            "discard_potion" => ExecuteDiscardPotion(player, data),
            "end_turn" => ExecuteEndTurn(player),
            "choose_map_node" => ExecuteChooseMapNode(data),
            "choose_event_option" => ExecuteChooseEventOption(data),
            "choose_event_option_by_title_key" => ExecuteChooseEventOptionByTitleKey(data),
            "advance_dialogue" => ExecuteAdvanceDialogue(),
            "choose_rest_option" => ExecuteChooseRestOption(data),
            "shop_purchase" => ExecuteShopPurchase(player, data),
            "claim_reward" => ExecuteClaimReward(data),
            "claim_reward_by_match" => ExecuteClaimRewardByMatch(data),
            "select_card_reward" => ExecuteSelectCardReward(data),
            "select_card_reward_by_id" => ExecuteSelectCardRewardById(data),
            "skip_card_reward" => ExecuteSkipCardReward(),
            "proceed" => ExecuteProceed(),
            "select_card" => ExecuteSelectCard(data),
            "select_deck_card" => ExecuteSelectDeckCard(player, data),
            "confirm_selection" => ExecuteConfirmSelection(),
            "cancel_selection" => ExecuteCancelSelection(),
            "select_bundle" => ExecuteSelectBundle(data),
            "confirm_bundle_selection" => ExecuteConfirmBundleSelection(),
            "cancel_bundle_selection" => ExecuteCancelBundleSelection(),
            "combat_select_card" => ExecuteCombatSelectCard(data),
            "combat_confirm_selection" => ExecuteCombatConfirmSelection(),
            "select_relic" => ExecuteSelectRelic(data),
            "skip_relic_selection" => ExecuteSkipRelicSelection(),
            "claim_treasure_relic" => ExecuteClaimTreasureRelic(data),
            "crystal_sphere_set_tool" => ExecuteCrystalSphereSetTool(data),
            "crystal_sphere_click_cell" => ExecuteCrystalSphereClickCell(data),
            "crystal_sphere_proceed" => ExecuteCrystalSphereProceed(),
            _ => Error($"Unknown action: {action}")
        };
    }

    [McpAction("return_to_main_menu", "Debug", "Return the current game to the main menu.")]
    private static Dictionary<string, object?> ExecuteReturnToMainMenu()
    {
        var game = NGame.Instance;
        if (game == null)
            return Error("Could not access game instance");

        _ = game.ReturnToMainMenu();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Returning to main menu"
        };
    }

    [McpAction("debug_start_encounter", "Debug", "Start a specific debug encounter.")]
    [McpActionField("encounter", "string", true, "Encounter id or name, such as ChompersNormal.")]
    private static Dictionary<string, object?> ExecuteDebugStartEncounter(RunState runState, Dictionary<string, JsonElement> data)
    {
        try
        {
            if (!data.TryGetValue("encounter", out var encounterElem))
                return Error("Missing 'encounter' (e.g. 'ChompersNormal' or 'chompers-normal')");

            if (CombatManager.Instance.IsInProgress)
                CombatManager.Instance.Reset(graceful: true);

            string encounterName = encounterElem.GetString() ?? "";
            var encounter = CreateEncounterModel(encounterName);
            if (encounter == null)
                return Error($"Unknown encounter: {encounterName}");

            var setting = ApplyOptionalEncounterSetting(encounter, data);
            if (setting.Error != null)
                return Error(setting.Error);

            var room = new CombatRoom(encounter, runState);
            typeof(AbstractRoom)
                .GetProperty(nameof(AbstractRoom.Id), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(room, runState.GetAndIncrementNextRoomId());
            runState.PushRoom(room);
            _ = room.Enter(runState, false);

            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Starting debug encounter: {encounter.GetType().Name}",
                ["encounter"] = encounter.GetType().Name,
                ["setting"] = setting.Value
            };
        }
        catch (System.Exception ex)
        {
            var detail = ex.InnerException == null ? ex.Message : $"{ex.Message}: {ex.InnerException.Message}";
            return Error($"Failed to start debug encounter: {detail}");
        }
    }

    private static EncounterModel? CreateEncounterModel(string encounterName)
    {
        string normalized = NormalizeIdentifier(encounterName);
        var assembly = typeof(EncounterModel).Assembly;
        var encounterType = assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(EncounterModel).IsAssignableFrom(type))
            .FirstOrDefault(type =>
                NormalizeIdentifier(type.Name) == normalized ||
                NormalizeIdentifier(type.FullName ?? "") == normalized);

        if (encounterType == null)
            return null;

        var canonical = typeof(ModelDb)
            .GetMethod("Get", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, [typeof(Type)])
            ?.Invoke(null, [encounterType]) as EncounterModel;
        return canonical?.MutableClone() as EncounterModel;
    }

    private static (string? Value, string? Error) ApplyOptionalEncounterSetting(
        EncounterModel encounter,
        Dictionary<string, JsonElement> data)
    {
        if (!data.TryGetValue("setting", out var settingElem))
            return (null, null);

        var settingProperty = encounter.GetType()
            .GetProperty("Setting", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (settingProperty == null || !settingProperty.CanWrite)
            return (null, $"{encounter.GetType().Name} does not support a 'setting' parameter");

        string? settingText = settingElem.ValueKind switch
        {
            JsonValueKind.String => settingElem.GetString(),
            JsonValueKind.Number => settingElem.GetInt32().ToString(),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(settingText))
            return (null, "'setting' must be a string enum name or integer enum value");

        try
        {
            var settingType = settingProperty.PropertyType;
            object settingValue = settingType.IsEnum
                ? Enum.Parse(settingType, settingText, ignoreCase: true)
                : Convert.ChangeType(settingText, settingType);
            settingProperty.SetValue(encounter, settingValue);
            return (settingValue.ToString(), null);
        }
        catch (System.Exception ex)
        {
            return (null, $"Invalid setting for {encounter.GetType().Name}: {settingText} ({ex.Message})");
        }
    }

    private static string NormalizeIdentifier(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    [McpAction("debug_force_play_phase", "Debug", "Force combat back into player play phase.")]
    private static Dictionary<string, object?> ExecuteDebugForcePlayPhase()
    {
        var manager = CombatManager.Instance;
        var combatState = manager.DebugOnlyGetState();
        if (!manager.IsInProgress || combatState == null)
            return Error("Not in combat");
        if (combatState.CurrentSide != CombatSide.Player)
            return Error($"Cannot force play phase while current side is {combatState.CurrentSide}");

        SetMember(manager, "_playerActionsDisabled", false);
        SetMember(manager, nameof(CombatManager.IsEnemyTurnStarted), false);
        SetMember(manager, nameof(CombatManager.EndingPlayerTurnPhaseOne), false);
        SetMember(manager, nameof(CombatManager.EndingPlayerTurnPhaseTwo), false);

        foreach (var player in combatState.Players)
            if (player.PlayerCombatState != null)
                player.PlayerCombatState.Phase = PlayerTurnPhase.Play;

        RunManager.Instance.ActionExecutor.Unpause();
        RunManager.Instance.ActionQueueSynchronizer.SetCombatState(ActionSynchronizerCombatState.PlayPhase);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Forced combat into play phase"
        };
    }

    private static void SetMember(object target, string name, object? value)
    {
        var type = target.GetType();
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(target, value);
            return;
        }

        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null)
            property.SetValue(target, value);
    }

    [McpAction("play_card", "Combat", "Play a card from the player's hand.")]
    [McpActionField("card_index", "int", true, "0-based index in the current hand.")]
    [McpActionField("target", "string", false, "Target entity_id for single-target cards.")]
    private static Dictionary<string, object?> ExecutePlayCard(Player player, Dictionary<string, JsonElement> data)
    {
        if (!CombatManager.Instance.IsInProgress)
            return Error("Not in combat");
        if (!IsPlayPhase())
            return Error("Not in play phase - cannot act during enemy turn");
        if (CombatManager.Instance.PlayerActionsDisabled)
            return Error("Player actions are currently disabled");
        if (!player.Creature.IsAlive)
            return Error("Player creature is dead - cannot play cards");

        var combatState = player.Creature.CombatState;
        if (combatState == null)
            return Error("No combat state");

        // Get card by index in hand
        if (!data.TryGetValue("card_index", out var indexElem))
            return Error("Missing 'card_index'");

        int cardIndex = indexElem.GetInt32();
        var hand = player.PlayerCombatState?.Hand;
        if (hand == null)
            return Error("No hand available");

        var requestedCardId = data.TryGetValue("card_id", out var cardIdElem)
            ? cardIdElem.GetString()
            : null;

        CardModel? card = null;
        if (cardIndex >= 0 && cardIndex < hand.Cards.Count)
            card = hand.Cards[cardIndex];

        if (card == null
            || !ReplayCardMatches(card, requestedCardId)
            || !card.CanPlay(out _, out _))
        {
            if (!TryFindReplayCardInHand(hand, requestedCardId, out var resolvedIndex, out var resolvedCard))
            {
                if (!string.IsNullOrWhiteSpace(requestedCardId))
                    return Error($"Card '{requestedCardId}' is not playable in hand");

                if (cardIndex < 0 || cardIndex >= hand.Cards.Count)
                    return Error($"card_index {cardIndex} out of range (hand has {hand.Cards.Count} cards)");

                card = hand.Cards[cardIndex];
            }
            else
            {
                cardIndex = resolvedIndex;
                card = resolvedCard;
            }
        }

        if (!card.CanPlay(out var reason, out _))
            return Error($"Card '{card.Title}' cannot be played: {reason}");

        // Resolve target
        Creature? target = null;
        if (card.TargetType == TargetType.AnyEnemy)
        {
            if (data.TryGetValue("target", out var targetElem))
            {
                string targetId = targetElem.GetString() ?? "";
                target = ResolveTarget(combatState, targetId);
                if (target == null)
                    return Error($"Target '{targetId}' not found among alive enemies");
            }
            else if (!TryResolveOnlyAliveEnemy(combatState, out target))
            {
                return Error("Card requires a target. Provide 'target' with an entity_id.");
            }
        }

        var nextActionId = RunManager.Instance.ActionQueueSet.NextActionId;
        if (!card.TryManualPlay(target))
            return Error($"Card '{card.Title}' could not be manually played right now");

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Playing '{card.Title}'" + (target != null ? $" targeting {SafeGetText(() => target.Monster?.Title) ?? "target"}" : ""),
            ["action_id"] = nextActionId
        };
    }

    private static bool TryFindReplayCardInHand(
        CardPile hand,
        string? requestedCardId,
        out int cardIndex,
        out CardModel card)
    {
        cardIndex = -1;
        card = null!;

        if (string.IsNullOrWhiteSpace(requestedCardId))
            return false;

        for (var i = 0; i < hand.Cards.Count; i++)
        {
            var candidate = hand.Cards[i];
            if (!ReplayCardMatches(candidate, requestedCardId))
                continue;

            if (!candidate.CanPlay(out _, out _))
                continue;

            cardIndex = i;
            card = candidate;
            return true;
        }

        return false;
    }

    private static bool ReplayCardMatches(CardModel card, string? requestedCardId)
    {
        if (string.IsNullOrWhiteSpace(requestedCardId))
            return true;

        return CardIdMatches(card, requestedCardId);
    }

    [McpAction("end_turn", "Combat", "End the player's combat turn.")]
    private static Dictionary<string, object?> ExecuteEndTurn(Player player)
    {
        if (!CombatManager.Instance.IsInProgress)
            return Error("Not in combat");
        if (!IsPlayPhase())
            return Error("Not in play phase - cannot act during enemy turn");
        if (CombatManager.Instance.PlayerActionsDisabled)
            return Error("Player actions are currently disabled (turn may already be ending)");

        // Match the game's own CanTurnBeEnded guard (NEndTurnButton.cs:114-123)
        var hand = NCombatRoom.Instance?.Ui?.Hand;
        if (hand != null && (hand.InCardPlay || hand.CurrentMode != NPlayerHand.Mode.Play))
            return Error("Cannot end turn while a card is being played or hand is in selection mode");

        var nextActionId = RunManager.Instance.ActionQueueSet.NextActionId;
        PlayerCmd.EndTurn(player, canBackOut: false);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Ending turn",
            ["action_id"] = nextActionId
        };
    }

    [McpAction("use_potion", "General", "Use a potion from the player's potion slots.")]
    [McpActionField("slot", "int", true, "Potion slot index.")]
    [McpActionField("target", "string", false, "Target entity_id for targetable potions.")]
    private static Dictionary<string, object?> ExecuteUsePotion(Player player, Dictionary<string, JsonElement> data)
    {
        if (!data.TryGetValue("slot", out var slotElem))
            return Error("Missing 'slot' (potion slot index)");

        int slot = slotElem.GetInt32();
        if (slot < 0 || slot >= player.PotionSlots.Count)
            return Error($"Potion slot {slot} out of range (player has {player.PotionSlots.Count} slots)");

        var potion = player.GetPotionAtSlotIndex(slot);
        if (potion == null)
            return Error($"No potion in slot {slot}");
        if (potion.IsQueued)
            return Error($"Potion '{SafeGetText(() => potion.Title)}' is already queued for use");
        if (potion.Owner.Creature.IsDead)
            return Error("Cannot use potion - player creature is dead");
        if (!potion.PassesCustomUsabilityCheck)
            return Error($"Potion '{SafeGetText(() => potion.Title)}' cannot be used right now");

        bool inCombat = CombatManager.Instance.IsInProgress;
        if (potion.Usage == PotionUsage.CombatOnly)
        {
            if (!inCombat)
                return Error($"Potion '{SafeGetText(() => potion.Title)}' can only be used in combat");
            if (!IsPlayPhase())
                return Error("Cannot use potions outside of play phase");
        }
        else if (potion.Usage == PotionUsage.Automatic)
            return Error($"Potion '{SafeGetText(() => potion.Title)}' is automatic and cannot be manually used");

        if (inCombat && CombatManager.Instance.PlayerActionsDisabled)
            return Error("Player actions are currently disabled");

        // Resolve target
        Creature? target = null;
        var combatState = player.Creature.CombatState;

        switch (potion.TargetType)
        {
            case TargetType.AnyEnemy:
                if (!data.TryGetValue("target", out var targetElem))
                    return Error("Potion requires a target enemy. Provide 'target' with an entity_id.");
                string targetId = targetElem.GetString() ?? "";
                if (combatState == null)
                    return Error("No combat state for target resolution");
                target = ResolveTarget(combatState, targetId);
                if (target == null)
                    return Error($"Target '{targetId}' not found among alive enemies");
                break;
            case TargetType.Self:
            case TargetType.AnyAlly:
            case TargetType.AnyPlayer:
                target = player.Creature;
                break;
            default:
                target = null;
                break;
        }

        var nextActionId = RunManager.Instance.ActionQueueSet.NextActionId;
        potion.EnqueueManualUse(target);

        string targetMsg = potion.TargetType switch
        {
            TargetType.AnyEnemy => $" targeting {SafeGetText(() => target?.Monster?.Title) ?? "enemy"}",
            TargetType.Self or TargetType.AnyPlayer or TargetType.AnyAlly => " on self",
            _ => ""
        };

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Using potion '{SafeGetText(() => potion.Title)}' from slot {slot}{targetMsg}",
            ["action_id"] = nextActionId
        };
    }

    [McpAction("discard_potion", "General", "Discard a potion from the player's potion slots.")]
    [McpActionField("slot", "int", true, "Potion slot index.")]
    private static Dictionary<string, object?> ExecuteDiscardPotion(Player player, Dictionary<string, JsonElement> data)
    {
        if (!data.TryGetValue("slot", out var slotElem))
            return Error("Missing 'slot' (potion slot index)");

        int slot = slotElem.GetInt32();
        if (slot < 0 || slot >= player.PotionSlots.Count)
            return Error($"Potion slot {slot} out of range (player has {player.PotionSlots.Count} slots)");

        var potion = player.GetPotionAtSlotIndex(slot);
        if (potion == null)
            return Error($"No potion in slot {slot}");

        string potionName = SafeGetText(() => potion.Title) ?? "unknown";
        _ = PotionCmd.Discard(potion);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Discarded potion '{potionName}' from slot {slot}"
        };
    }

    [McpAction("choose_event_option", "Event", "Choose an event option.")]
    [McpActionField("index", "int", true, "0-based option index from next_options.")]
    private static Dictionary<string, object?> ExecuteChooseEventOption(Dictionary<string, JsonElement> data)
    {
        var uiRoom = NEventRoom.Instance;
        if (uiRoom == null)
            return Error("Event room is not open");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (event option index)");

        int index = indexElem.GetInt32();

        var buttons = FindAll<NEventOptionButton>(uiRoom);

        if (buttons.Count == 0)
            return Error("No event options available");
        if (index < 0 || index >= buttons.Count)
            return Error($"Event option index {index} out of range ({buttons.Count} options)");

        var button = buttons[index];
        if (button.Option.IsLocked)
            return Error($"Event option {index} is locked");
        string title = SafeGetText(() => button.Option.Title) ?? "option";
        button.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Choosing event option: {title}"
        };
    }

    [McpAction("choose_event_option_by_title_key", "Event", "Choose an event option by localization title key.")]
    [McpActionField("title_key", "string", true, "Localization key for the event option title.")]
    private static Dictionary<string, object?> ExecuteChooseEventOptionByTitleKey(Dictionary<string, JsonElement> data)
    {
        var uiRoom = NEventRoom.Instance;
        if (uiRoom == null)
            return Error("Event room is not open");

        if (!data.TryGetValue("title_key", out var titleKeyElem))
            return Error("Missing 'title_key' (event option localization title key)");

        var titleKey = titleKeyElem.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(titleKey))
            return Error("'title_key' must not be empty");

        var buttons = FindAll<NEventOptionButton>(uiRoom);
        foreach (var button in buttons)
        {
            var optionTitleKey = SafeGetLocStringKey(() => button.Option.Title);
            if (!string.Equals(optionTitleKey, titleKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (button.Option.IsLocked)
                return Error($"Event option '{titleKey}' is locked");

            button.ForceClick();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Choosing event option: {titleKey}"
            };
        }

        return Error($"No event option with title_key '{titleKey}' is available");
    }

    [McpAction("advance_dialogue", "Event", "Advance event dialogue when a dialogue-only event is waiting.")]
    private static Dictionary<string, object?> ExecuteAdvanceDialogue()
    {
        var uiRoom = NEventRoom.Instance;
        if (uiRoom == null)
            return Error("Event room is not open");

        var ancientLayout = FindFirst<NAncientEventLayout>(uiRoom);
        if (ancientLayout == null)
            return Error("No ancient dialogue active");

        var hitbox = ancientLayout.GetNodeOrNull<NClickableControl>("%DialogueHitbox");
        if (hitbox == null || !hitbox.Visible || !hitbox.IsEnabled)
            return Error("Dialogue hitbox not available - dialogue may have ended");

        hitbox.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Advancing dialogue"
        };
    }

    [McpAction("choose_rest_option", "Rest", "Choose a rest site option.")]
    [McpActionField("index", "int", false, "0-based rest option index from next_options.")]
    [McpActionField("option_id", "string", false, "Rest option id, such as HEAL or SMITH.")]
    private static Dictionary<string, object?> ExecuteChooseRestOption(Dictionary<string, JsonElement> data)
    {
        var restRoom = NRestSiteRoom.Instance;
        if (restRoom == null)
            return Error("Rest site room is not open");

        var buttons = FindAll<NRestSiteButton>(restRoom);

        if (buttons.Count == 0)
            return Error("No rest site options available");

        int index;
        if (data.TryGetValue("index", out var indexElem))
            index = indexElem.GetInt32();
        else if (data.TryGetValue("option_id", out var optionIdElem))
        {
            var optionId = optionIdElem.GetString() ?? "";
            index = buttons.FindIndex(button =>
                string.Equals(button.Option.OptionId, optionId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return Error($"Rest option '{optionId}' is not available");
        }
        else
        {
            return Error("Missing 'index' or 'option_id' (rest site option)");
        }

        if (index < 0 || index >= buttons.Count)
            return Error($"Rest option index {index} out of range ({buttons.Count} options)");

        var button = buttons[index];
        if (!button.Option.IsEnabled)
            return Error($"Rest option {index} ({button.Option.OptionId}) is disabled");
        var selectedOptionId = button.Option.OptionId;
        string optionName = SafeGetText(() => button.Option.Title) ?? button.Option.OptionId;
        button.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selecting rest site option: {optionName}",
            ["option_id"] = selectedOptionId
        };
    }

    [McpAction("shop_purchase", "Shop", "Purchase an item from the current shop.")]
    [McpActionField("index", "int", true, "0-based shop item index.")]
    private static Dictionary<string, object?> ExecuteShopPurchase(Player player, Dictionary<string, JsonElement> data)
    {
        MerchantInventory? inventory = null;

        if (player.RunState.CurrentRoom is MerchantRoom merchantRoom)
        {
            // Regular merchant - auto-open inventory if needed
            var merchUI = NMerchantRoom.Instance;
            if (merchUI?.Inventory != null && !merchUI.Inventory.IsOpen)
                merchUI.OpenInventory();
            inventory = merchantRoom.GetLocalInventory();
        }
        else if (player.RunState.CurrentRoom is EventRoom eventRoom
                 && eventRoom.CanonicalEvent is FakeMerchant
                 && (eventRoom.LocalMutableEvent ?? eventRoom.CanonicalEvent) is FakeMerchant fakeMerchant)
        {
            // Fake merchant event - auto-open via button if needed
            if (!fakeMerchant.StartedFight)
            {
                var uiRoom = NEventRoom.Instance;
                if (uiRoom != null)
                {
                    var fmNode = FindFirst<NFakeMerchant>(uiRoom);
                    if (fmNode != null)
                    {
                        var inventoryUI = FindFirst<NMerchantInventory>(fmNode);
                        if (inventoryUI != null && !inventoryUI.IsOpen)
                        {
                            var btn = fmNode.MerchantButton;
                            if (btn != null && btn.Visible && btn.IsEnabled)
                                btn.ForceClick();
                        }
                    }
                }
            }
            inventory = fakeMerchant.Inventory;
        }
        else
        {
            return Error("Not in a shop");
        }

        if (inventory == null)
            return Error("Shop inventory not ready yet; wait a moment and retry");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (shop item index)");

        int index = indexElem.GetInt32();

        var allEntries = inventory.AllEntries.ToList();
        if (index < 0 || index >= allEntries.Count)
            return Error($"Shop item index {index} out of range ({allEntries.Count} items)");

        var entry = allEntries[index];
        if (!entry.IsStocked)
            return Error("Item is sold out");
        if (!entry.EnoughGold)
            return Error($"Not enough gold (need {entry.Cost}, have {player.Gold})");

        // Fire-and-forget purchase (same path as AutoSlay)
        _ = entry.OnTryPurchaseWrapper(inventory);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Purchasing item for {entry.Cost} gold"
        };
    }

    [McpAction("choose_map_node", "Map", "Travel to an available map node.")]
    [McpActionField("index", "int", false, "0-based map node option index from next_options.")]
    [McpActionField("col", "int", false, "Target map node column. Preferred for replay stability.")]
    [McpActionField("row", "int", false, "Target map node row. Preferred for replay stability.")]
    private static Dictionary<string, object?> ExecuteChooseMapNode(Dictionary<string, JsonElement> data)
    {
        var mapScreen = NMapScreen.Instance;
        if (mapScreen == null || (!mapScreen.IsOpen && !IsNodeVisible(mapScreen)))
            return Error("Map screen is not open");

        var travelable = FindAll<NMapPoint>(mapScreen)
            .Where(mp => mp.State == MapPointState.Travelable && mp.Point != null)
            .OrderBy(mp => mp.Point!.coord.col)
            .ToList();

        if (travelable.Count == 0)
            return Error("No travelable map nodes available");

        NMapPoint target;
        if (data.TryGetValue("col", out var colElem)
            && data.TryGetValue("row", out var rowElem)
            && colElem.TryGetInt32(out var col)
            && rowElem.TryGetInt32(out var row))
        {
            target = travelable.FirstOrDefault(mp => mp.Point!.coord.col == col && mp.Point.coord.row == row)!;
            if (target == null)
                return Error($"Map node ({col},{row}) is not currently travelable");
        }
        else
        {
            if (!data.TryGetValue("index", out var indexElem))
                return Error("Missing 'index' or target 'col'/'row'");

            int index = indexElem.GetInt32();
            if (index < 0 || index >= travelable.Count)
                return Error($"Map node index {index} out of range ({travelable.Count} options available)");

            target = travelable[index];
        }

        var pt = target.Point!;
        mapScreen.OnMapPointSelectedLocally(target);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Traveling to {pt.PointType} at ({pt.coord.col},{pt.coord.row})",
            ["col"] = pt.coord.col,
            ["row"] = pt.coord.row
        };
    }

    [McpAction("claim_reward", "Reward", "Claim a reward from the rewards screen.")]
    [McpActionField("index", "int", true, "0-based claimable reward index.")]
    private static Dictionary<string, object?> ExecuteClaimReward(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NRewardsScreen rewardsScreen)
            return Error("Rewards screen is not open");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (reward index)");

        int index = indexElem.GetInt32();

        var enabledButtons = FindAll<NRewardButton>(rewardsScreen)
            .Where(b => b.IsEnabled && b.Reward != null)
            .ToList();

        if (index < 0 || index >= enabledButtons.Count)
            return Error($"Reward index {index} out of range (screen has {enabledButtons.Count} claimable rewards)");

        var button = enabledButtons[index];
        var reward = button.Reward!;
        string rewardDesc = GetRewardTypeName(reward);
        if (reward is GoldReward g)
            rewardDesc = $"gold ({g.Amount})";
        else if (reward is PotionReward p)
            rewardDesc = $"potion ({SafeGetText(() => p.Potion?.Title)})";
        else if (reward is CardReward)
            rewardDesc = "card (opens card selection)";

        button.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Claiming reward: {rewardDesc}"
        };
    }

    [McpAction("claim_reward_by_match", "Reward", "Claim a reward by stable reward identity.")]
    [McpActionField("type", "string", true, "Reward type, such as gold, potion, relic, card, or special_card.")]
    [McpActionField("gold_amount", "int", false, "Gold amount to match for gold rewards.")]
    [McpActionField("potion_id", "string", false, "Potion id to match for potion rewards.")]
    [McpActionField("relic_id", "string", false, "Relic id to match for relic rewards.")]
    private static Dictionary<string, object?> ExecuteClaimRewardByMatch(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NRewardsScreen rewardsScreen)
            return Error("Rewards screen is not open");

        if (!data.TryGetValue("type", out var typeElem))
            return Error("Missing 'type' (reward type)");

        var requestedType = typeElem.GetString() ?? "";
        var enabledButtons = FindAll<NRewardButton>(rewardsScreen)
            .Where(b => b.IsEnabled && b.Reward != null)
            .ToList();

        foreach (var button in enabledButtons)
        {
            var reward = button.Reward!;
            if (!string.Equals(GetRewardTypeName(reward), requestedType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!RewardMatches(reward, data))
                continue;

            button.ForceClick();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Claiming matching reward: {requestedType}"
            };
        }

        return Error($"No matching reward found for type '{requestedType}'");
    }

    private static bool RewardMatches(Reward reward, Dictionary<string, JsonElement> data)
    {
        if (data.TryGetValue("gold_amount", out var goldElem)
            && reward is GoldReward goldReward
            && goldElem.TryGetInt32(out var expectedGold)
            && goldReward.Amount != expectedGold)
            return false;

        if (data.TryGetValue("potion_id", out var potionElem)
            && reward is PotionReward potionReward)
        {
            var expectedPotion = NormalizeModelIdForComparison(potionElem.GetString(), "POTION.");
            if (!string.Equals(potionReward.Potion?.Id.Entry, expectedPotion, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (data.TryGetValue("relic_id", out var relicElem)
            && reward is RelicReward relicReward)
        {
            var expectedRelic = NormalizeModelIdForComparison(relicElem.GetString(), "RELIC.");
            if (!string.Equals(relicReward.Relic?.Id.Entry, expectedRelic, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string NormalizeModelIdForComparison(string? value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;
    }

    [McpAction("select_card_reward", "Reward", "Select a card from the card reward screen.")]
    [McpActionField("card_index", "int", true, "0-based card reward index.")]
    private static Dictionary<string, object?> ExecuteSelectCardReward(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCardRewardSelectionScreen cardScreen)
            return Error("Card reward selection screen is not open");

        if (!data.TryGetValue("card_index", out var indexElem))
            return Error("Missing 'card_index'");

        int cardIndex = indexElem.GetInt32();

        var cardHolders = FindAllSortedByPosition<NCardHolder>(cardScreen);
        if (cardIndex < 0 || cardIndex >= cardHolders.Count)
            return Error($"Card index {cardIndex} out of range (screen has {cardHolders.Count} cards)");

        var holder = cardHolders[cardIndex];
        string cardName = SafeGetText(() => holder.CardModel?.Title) ?? "unknown";
        holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selecting card: {cardName}"
        };
    }

    [McpAction("select_card_reward_by_id", "Reward", "Select a card from the card reward screen by id.")]
    [McpActionField("card_id", "string", true, "Card id to select from the current card reward.")]
    private static Dictionary<string, object?> ExecuteSelectCardRewardById(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCardRewardSelectionScreen cardScreen)
            return Error("Card reward selection screen is not open");

        if (!data.TryGetValue("card_id", out var cardIdElem))
            return Error("Missing 'card_id'");

        var cardId = cardIdElem.GetString() ?? "";
        foreach (var holder in FindAllSortedByPosition<NCardHolder>(cardScreen))
        {
            if (holder.CardModel == null || !CardIdMatches(holder.CardModel, cardId))
                continue;

            holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Selecting card: {cardId}"
            };
        }

        return Error($"Card reward does not contain '{cardId}'");
    }

    [McpAction("skip_card_reward", "Reward", "Skip the current card reward.")]
    private static Dictionary<string, object?> ExecuteSkipCardReward()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCardRewardSelectionScreen cardScreen)
            return Error("Card reward selection screen is not open");

        var altButtons = FindAll<NCardRewardAlternativeButton>(cardScreen);
        if (altButtons.Count == 0)
            return Error("No skip option available on this card reward");

        altButtons[0].ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Skipping card reward"
        };
    }

    [McpAction("proceed", "Navigation", "Press the active proceed button.")]
    private static Dictionary<string, object?> ExecuteProceed()
    {
        // Try rewards overlay
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is NRewardsScreen rewardsScreen)
        {
            var btn = FindFirst<NProceedButton>(rewardsScreen);
            if (btn is { IsEnabled: true })
            {
                btn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Proceeding from rewards" };
            }
        }

        // Try rest site
        if (NRestSiteRoom.Instance is { } restRoom && restRoom.ProceedButton.IsEnabled)
        {
            restRoom.ProceedButton.ForceClick();
            return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Proceeding from rest site" };
        }

        // Try merchant - close inventory first if open, then proceed
        if (NMerchantRoom.Instance is { } merchRoom)
        {
            if (merchRoom.Inventory?.IsOpen == true)
            {
                var backBtn = FindFirst<NBackButton>(merchRoom);
                if (backBtn is { IsEnabled: true })
                    backBtn.ForceClick();
            }
            if (merchRoom.ProceedButton.IsEnabled)
            {
                merchRoom.ProceedButton.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Proceeding from shop" };
            }
        }

        // Try fake merchant - close inventory first if open, then proceed
        if (NEventRoom.Instance is { } evtRoom)
        {
            var fmNode = FindFirst<NFakeMerchant>(evtRoom);
            if (fmNode != null)
            {
                var fmInventory = FindFirst<NMerchantInventory>(fmNode);
                if (fmInventory is { IsOpen: true })
                {
                    var backBtn = FindFirst<NBackButton>(fmNode);
                    if (backBtn is { IsEnabled: true })
                        backBtn.ForceClick();
                }
                var proceedBtn = FindFirst<NProceedButton>(fmNode);
                if (proceedBtn is { IsEnabled: true })
                {
                    proceedBtn.ForceClick();
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Proceeding from fake merchant" };
                }
            }

            var eventProceed = FindAll<NEventOptionButton>(evtRoom)
                .FirstOrDefault(button =>
                    button.Option.IsProceed
                    && !button.Option.IsLocked
                    && button.IsEnabled
                    && IsNodeVisible(button));
            if (eventProceed != null)
            {
                var title = SafeGetText(() => eventProceed.Option.Title) ?? "Proceed";
                eventProceed.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = $"Proceeding from event: {title}" };
            }

            if (FindAll<NEventOptionButton>(evtRoom).Any(button => button.Option.IsProceed && !button.Option.IsLocked))
                return Error("Event proceed option is not ready yet");
        }

        // Try treasure room
        var treasureUI = FindFirst<NTreasureRoom>(
            ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root);
        if (treasureUI != null && treasureUI.ProceedButton.IsEnabled)
        {
            treasureUI.ProceedButton.ForceClick();
            return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Proceeding from treasure room" };
        }

        return Error("No proceed button available or enabled");
    }

    [McpAction("select_card", "Selection", "Select or toggle a card in the active card selection screen.")]
    [McpActionField("index", "int", false, "0-based card index in the active selection screen.")]
    [McpActionField("card_id", "string", false, "Card id to select from the active selection screen.")]
    private static Dictionary<string, object?> ExecuteSelectCard(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();

        var requestedCardId = data.TryGetValue("card_id", out var cardIdElem)
            ? cardIdElem.GetString()
            : null;

        if (overlay is NCardGridSelectionScreen gridScreen)
        {
            var grid = FindFirst<NCardGrid>(gridScreen);
            if (grid == null)
                return Error("Card grid not found in selection screen");

            var holders = FindAllSortedByPosition<NGridCardHolder>(gridScreen);
            if (!TryResolveSelectionCardIndex(data, requestedCardId, holders, out var index, out var error))
                return Error(error);

            if (index < 0 || index >= holders.Count)
                return Error($"Card index {index} out of range ({holders.Count} cards available)");

            var holder = holders[index];
            string cardName = SafeGetText(() => holder.CardModel?.Title) ?? "unknown";
            grid.EmitSignal(NCardGrid.SignalName.HolderPressed, holder);

            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Toggling card selection: {cardName}"
            };
        }
        else if (overlay is NChooseACardSelectionScreen chooseScreen)
        {
            if (!IsChooseCardSelectionReady(chooseScreen))
                return Error("Card choice screen is not ready yet");

            var holders = FindAllSortedByPosition<NGridCardHolder>(chooseScreen);
            if (!TryResolveSelectionCardIndex(data, requestedCardId, holders, out var index, out var error))
                return Error(error);

            if (index < 0 || index >= holders.Count)
                return Error($"Card index {index} out of range ({holders.Count} cards available)");

            var holder = holders[index];
            string cardName = SafeGetText(() => holder.CardModel?.Title) ?? "unknown";
            holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);

            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Choosing card: {cardName}"
            };
        }

        return Error("No card selection screen is open");
    }

    private static bool IsChooseCardSelectionReady(NChooseACardSelectionScreen chooseScreen)
    {
        var openedTicks = GetInstanceFieldValue(chooseScreen, "_openedTicks") as ulong?;
        return openedTicks == null || Godot.Time.GetTicksMsec() - openedTicks > 350UL;
    }

    private static bool TryResolveSelectionCardIndex(
        Dictionary<string, JsonElement> data,
        string? requestedCardId,
        List<NGridCardHolder> holders,
        out int index,
        out string error)
    {
        index = -1;
        error = "";

        if (data.TryGetValue("index", out var indexElem))
        {
            index = indexElem.GetInt32();
            if (!string.IsNullOrWhiteSpace(requestedCardId)
                && index >= 0
                && index < holders.Count
                && !CardIdMatches(holders[index].CardModel!, requestedCardId))
            {
                error = $"Card index {index} is '{holders[index].CardModel?.Id}', not '{requestedCardId}'";
                return false;
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(requestedCardId))
        {
            error = "Missing 'index' or 'card_id' (card selection target)";
            return false;
        }

        for (var i = 0; i < holders.Count; i++)
        {
            var card = holders[i].CardModel;
            if (card != null && CardIdMatches(card, requestedCardId))
            {
                index = i;
                return true;
            }
        }

        error = $"Card '{requestedCardId}' is not selectable on the active screen";
        return false;
    }

    [McpAction("select_deck_card", "Selection", "Select or toggle a deck card in the active card selection screen.")]
    [McpActionField("deck_index", "int", false, "0-based card index in the current deck.")]
    [McpActionField("card_id", "string", false, "Card id to select, or an optional assertion for deck_index.")]
    private static Dictionary<string, object?> ExecuteSelectDeckCard(Player player, Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCardGridSelectionScreen gridScreen)
            return Error("No deck card selection screen is open");

        var requestedCardId = data.TryGetValue("card_id", out var cardIdElem)
            ? cardIdElem.GetString()
            : null;
        int deckIndex;
        if (data.TryGetValue("deck_index", out var deckIndexElem))
            deckIndex = deckIndexElem.GetInt32();
        else if (!TryFindUniqueDeckCardIndex(player, requestedCardId, out deckIndex, out var error))
            return Error(error);

        if (deckIndex < 0 || deckIndex >= player.Deck.Cards.Count)
            return Error($"deck_index {deckIndex} out of range (deck has {player.Deck.Cards.Count} cards)");

        var card = player.Deck.Cards[deckIndex];
        if (!string.IsNullOrWhiteSpace(requestedCardId)
            && !CardIdMatches(card, requestedCardId))
        {
            return Error($"deck_index {deckIndex} is '{card.Id}', not '{requestedCardId}'");
        }

        var grid = FindFirst<NCardGrid>(gridScreen);
        if (grid == null)
            return Error("Card grid not found in selection screen");

        var holder = FindAllSortedByPosition<NGridCardHolder>(gridScreen)
            .FirstOrDefault(candidate => ReferenceEquals(candidate.CardModel, card));
        if (holder == null)
            return Error($"Deck card at index {deckIndex} is not selectable on the active screen");

        string cardName = SafeGetText(() => holder.CardModel?.Title) ?? card.Id.ToString();
        grid.EmitSignal(NCardGrid.SignalName.HolderPressed, holder);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Toggling deck card selection: {cardName}"
        };
    }

    private static bool TryFindUniqueDeckCardIndex(
        Player player,
        string? requestedCardId,
        out int deckIndex,
        out string error)
    {
        deckIndex = -1;
        error = "";
        if (string.IsNullOrWhiteSpace(requestedCardId))
        {
            error = "Missing 'deck_index' or 'card_id'";
            return false;
        }

        var matches = player.Deck.Cards
            .Select((card, index) => (card, index))
            .Where(candidate => CardIdMatches(candidate.card, requestedCardId))
            .ToList();
        if (matches.Count == 0)
        {
            error = $"Card '{requestedCardId}' was not found in the current deck";
            return false;
        }

        if (matches.Count > 1)
        {
            error = $"Card '{requestedCardId}' is ambiguous in the current deck; provide deck_index";
            return false;
        }

        deckIndex = matches[0].index;
        return true;
    }

    private static bool CardIdMatches(CardModel card, string cardId)
    {
        return string.Equals(card.Id.ToString(), cardId, StringComparison.OrdinalIgnoreCase)
               || string.Equals(card.Id.Entry, cardId, StringComparison.OrdinalIgnoreCase);
    }

    [McpAction("confirm_selection", "Selection", "Confirm the active card selection screen.")]
    private static Dictionary<string, object?> ExecuteConfirmSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is NChooseACardSelectionScreen)
            return Error("Choose-a-card screen requires no confirmation - use select_card(index) to pick directly");
        if (overlay is not NCardGridSelectionScreen screen)
            return Error("No card selection screen is open");

        // Check all preview containers (upgrade uses UpgradeSinglePreviewContainer / UpgradeMultiPreviewContainer,
        // NDeckCardSelectScreen uses PreviewContainer with %PreviewConfirm)
        foreach (var containerName in new[] { "%UpgradeSinglePreviewContainer", "%UpgradeMultiPreviewContainer", "%PreviewContainer" })
        {
            var container = screen.GetNodeOrNull<Godot.Control>(containerName);
            if (container?.Visible == true)
            {
                var confirm = container.GetNodeOrNull<NConfirmButton>("Confirm")
                              ?? container.GetNodeOrNull<NConfirmButton>("%PreviewConfirm");
                if (confirm is { IsEnabled: true })
                {
                    confirm.ForceClick();
                    return new Dictionary<string, object?>
                    {
                        ["status"] = "ok",
                        ["message"] = "Confirming selection from preview"
                    };
                }
            }
        }

        // Try main confirm button
        var mainConfirm = screen.GetNodeOrNull<NConfirmButton>("Confirm")
                          ?? screen.GetNodeOrNull<NConfirmButton>("%Confirm");
        if (mainConfirm is { IsEnabled: true })
        {
            mainConfirm.ForceClick();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Confirming selection"
            };
        }

        // Fallback: find ANY enabled NConfirmButton in the screen tree.
        // Covers NCardGridSelectionScreen subclasses (like NDeckEnchantSelectScreen)
        // whose confirm button isn't in any of the known container paths above.
        var allConfirmButtons = FindAll<NConfirmButton>(screen);
        foreach (var btn in allConfirmButtons)
        {
            if (btn.IsEnabled && btn.IsVisibleInTree())
            {
                btn.ForceClick();
                return new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["message"] = "Confirming selection"
                };
            }
        }

        return Error("No confirm button is currently enabled - select more cards first");
    }

    [McpAction("cancel_selection", "Selection", "Cancel the active card selection screen.")]
    private static Dictionary<string, object?> ExecuteCancelSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();

        // Handle choose-a-card screen (skip button)
        if (overlay is NChooseACardSelectionScreen chooseScreen)
        {
            var skipButton = chooseScreen.GetNodeOrNull<NClickableControl>("SkipButton");
            if (skipButton is { IsEnabled: true })
            {
                skipButton.ForceClick();
                return new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["message"] = "Skipping card choice"
                };
            }
            return Error("No skip option available - a card must be chosen");
        }

        if (overlay is not NCardGridSelectionScreen screen)
            return Error("No card selection screen is open");

        // If preview is showing, cancel back to selection
        foreach (var containerName in new[] { "%UpgradeSinglePreviewContainer", "%UpgradeMultiPreviewContainer", "%PreviewContainer" })
        {
            var container = screen.GetNodeOrNull<Godot.Control>(containerName);
            if (container?.Visible == true)
            {
                var cancelBtn = container.GetNodeOrNull<NBackButton>("Cancel")
                                ?? container.GetNodeOrNull<NBackButton>("%PreviewCancel");
                if (cancelBtn is { IsEnabled: true })
                {
                    cancelBtn.ForceClick();
                    return new Dictionary<string, object?>
                    {
                        ["status"] = "ok",
                        ["message"] = "Cancelling preview - returning to card selection"
                    };
                }
            }
        }

        // Close the screen entirely
        var closeButton = screen.GetNodeOrNull<NBackButton>("%Close");
        if (closeButton is { IsEnabled: true })
        {
            closeButton.ForceClick();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Closing card selection screen"
            };
        }

        return Error("No cancel/close button is currently enabled - selection may be mandatory");
    }

    [McpAction("select_bundle", "Selection", "Select a bundle option.")]
    [McpActionField("index", "int", true, "0-based bundle option index.")]
    private static Dictionary<string, object?> ExecuteSelectBundle(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NChooseABundleSelectionScreen screen)
            return Error("No bundle selection screen is open");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (bundle index)");

        int index = indexElem.GetInt32();
        var previewContainer = screen.GetNodeOrNull<Godot.Control>("%BundlePreviewContainer");
        if (previewContainer?.Visible == true)
            return Error("A bundle preview is already open - confirm or cancel it first");

        var bundles = FindAll<NCardBundle>(screen);
        if (index < 0 || index >= bundles.Count)
            return Error($"Bundle index {index} out of range ({bundles.Count} bundles available)");

        bundles[index].Hitbox.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selecting bundle {index}"
        };
    }

    [McpAction("confirm_bundle_selection", "Selection", "Confirm the active bundle selection.")]
    private static Dictionary<string, object?> ExecuteConfirmBundleSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NChooseABundleSelectionScreen screen)
            return Error("No bundle selection screen is open");

        var confirmButton = screen.GetNodeOrNull<NConfirmButton>("%Confirm");
        if (confirmButton is not { IsEnabled: true })
            return Error("Bundle confirm button is not enabled");

        confirmButton.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Confirming bundle selection"
        };
    }

    [McpAction("cancel_bundle_selection", "Selection", "Cancel the active bundle selection.")]
    private static Dictionary<string, object?> ExecuteCancelBundleSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NChooseABundleSelectionScreen screen)
            return Error("No bundle selection screen is open");

        var cancelButton = screen.GetNodeOrNull<NBackButton>("%Cancel");
        if (cancelButton is not { IsEnabled: true })
            return Error("Bundle cancel button is not enabled");

        cancelButton.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Cancelling bundle selection"
        };
    }

    [McpAction("combat_select_card", "Selection", "Select a card in the active combat hand selection UI.")]
    [McpActionField("index", "int", true, "0-based card index.")]
    private static Dictionary<string, object?> ExecuteCombatSelectCard(Dictionary<string, JsonElement> data)
    {
        var hand = NPlayerHand.Instance;
        if (hand == null || !hand.IsInCardSelection)
            return Error("No in-combat card selection is active");

        if (!data.TryGetValue("card_index", out var indexElem))
            return Error("Missing 'card_index' (index of the card in hand)");

        int index = indexElem.GetInt32();
        var holders = hand.ActiveHolders;
        if (index < 0 || index >= holders.Count)
            return Error($"Card index {index} out of range ({holders.Count} selectable cards)");

        var holder = holders[index];
        string cardName = SafeGetText(() => holder.CardModel?.Title) ?? "unknown";

        // Emit the Pressed signal - same path the game UI uses
        holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selecting card from hand: {cardName}"
        };
    }

    [McpAction("combat_confirm_selection", "Selection", "Confirm the active combat hand selection UI.")]
    private static Dictionary<string, object?> ExecuteCombatConfirmSelection()
    {
        var hand = NPlayerHand.Instance;
        if (hand == null || !hand.IsInCardSelection)
            return Error("No in-combat card selection is active");

        var confirmBtn = hand.GetNodeOrNull<NConfirmButton>("%SelectModeConfirmButton");
        if (confirmBtn == null || !confirmBtn.IsEnabled)
            return Error("Confirm button is not enabled - select more cards first");

        confirmBtn.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Confirming combat card selection"
        };
    }

    [McpAction("select_relic", "Selection", "Select a relic from the active relic selection screen.")]
    [McpActionField("index", "int", true, "0-based relic index.")]
    private static Dictionary<string, object?> ExecuteSelectRelic(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NChooseARelicSelection screen)
            return Error("No relic selection screen is open");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (relic index)");

        int index = indexElem.GetInt32();

        var holders = FindAll<NRelicBasicHolder>(screen);
        if (index < 0 || index >= holders.Count)
            return Error($"Relic index {index} out of range ({holders.Count} relics available)");

        var holder = holders[index];
        string relicName = SafeGetText(() => holder.Relic?.Model?.Title) ?? "unknown";
        holder.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selecting relic: {relicName}"
        };
    }

    [McpAction("skip_relic_selection", "Selection", "Skip the active relic selection screen.")]
    private static Dictionary<string, object?> ExecuteSkipRelicSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NChooseARelicSelection screen)
            return Error("No relic selection screen is open");

        var skipButton = screen.GetNodeOrNull<NClickableControl>("SkipButton");
        if (skipButton is not { IsEnabled: true })
            return Error("No skip option available");

        skipButton.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Skipping relic selection"
        };
    }

    [McpAction("claim_treasure_relic", "Treasure", "Claim a relic from a treasure room.")]
    [McpActionField("index", "int", false, "0-based treasure relic index. Defaults to 0 when only one relic is claimable.")]
    private static Dictionary<string, object?> ExecuteClaimTreasureRelic(Dictionary<string, JsonElement> data)
    {
        var treasureUI = FindFirst<NTreasureRoom>(
            ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root);
        if (treasureUI == null)
            return Error("Treasure room is not open");

        var relicCollection = treasureUI.GetNodeOrNull<NTreasureRoomRelicCollection>("%RelicCollection");
        if (relicCollection?.Visible != true)
        {
            var chestButton = GetInstanceFieldValue(treasureUI, "_chestButton") as NButton;
            if (chestButton is { IsEnabled: true })
                chestButton.ForceClick();

            return Error("Relic collection is not visible - opening chest");
        }

        var holders = FindAll<NTreasureRoomRelicHolder>(relicCollection)
            .Where(h => h.IsEnabled && h.Visible)
            .ToList();

        int index;
        if (data.TryGetValue("index", out var indexElem))
        {
            index = indexElem.GetInt32();
        }
        else if (holders.Count == 1)
            index = 0;
        else
        {
            return Error("Missing 'index' (treasure has multiple relics)");
        }

        if (index < 0 || index >= holders.Count)
            return Error($"Relic index {index} out of range ({holders.Count} relics available)");

        var holder = holders[index];
        string relicName = SafeGetText(() => holder.Relic?.Model?.Title) ?? "unknown";
        holder.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Claiming treasure relic: {relicName}"
        };
    }

    [McpAction("crystal_sphere_set_tool", "Event", "Set the active Crystal Sphere tool.")]
    [McpActionField("tool", "int", true, "Tool id to select.")]
    private static Dictionary<string, object?> ExecuteCrystalSphereSetTool(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCrystalSphereScreen screen)
            return Error("Crystal Sphere screen is not open");

        if (!data.TryGetValue("tool", out var toolElem))
            return Error("Missing 'tool' (expected 'big' or 'small')");

        string tool = toolElem.GetString() ?? "";
        var button = tool switch
        {
            "big" => screen.GetNodeOrNull<NClickableControl>("%BigDivinationButton"),
            "small" => screen.GetNodeOrNull<NClickableControl>("%SmallDivinationButton"),
            _ => null
        };

        if (button == null)
            return Error($"Unknown Crystal Sphere tool: {tool}");
        if (!button.Visible || !button.IsEnabled)
            return Error($"Crystal Sphere tool '{tool}' is not available");

        button.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Setting Crystal Sphere tool to {tool}"
        };
    }

    [McpAction("crystal_sphere_click_cell", "Event", "Click a Crystal Sphere cell.")]
    [McpActionField("x", "int", true, "Cell X coordinate.")]
    [McpActionField("y", "int", true, "Cell Y coordinate.")]
    private static Dictionary<string, object?> ExecuteCrystalSphereClickCell(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCrystalSphereScreen screen)
            return Error("Crystal Sphere screen is not open");

        if (!data.TryGetValue("x", out var xElem))
            return Error("Missing 'x' (cell x-coordinate)");
        if (!data.TryGetValue("y", out var yElem))
            return Error("Missing 'y' (cell y-coordinate)");

        int x = xElem.GetInt32();
        int y = yElem.GetInt32();

        var cell = FindAll<NCrystalSphereCell>(screen)
            .FirstOrDefault(c => c.Entity.X == x && c.Entity.Y == y);
        if (cell == null)
            return Error($"Crystal Sphere cell ({x}, {y}) was not found");
        if (!cell.Entity.IsHidden || !cell.Visible)
            return Error($"Crystal Sphere cell ({x}, {y}) is not clickable");

        cell.EmitSignal(NClickableControl.SignalName.Released, cell);
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Clicking Crystal Sphere cell ({x}, {y})"
        };
    }

    [McpAction("crystal_sphere_proceed", "Event", "Proceed from the Crystal Sphere event.")]
    private static Dictionary<string, object?> ExecuteCrystalSphereProceed()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCrystalSphereScreen screen)
            return Error("Crystal Sphere screen is not open");

        var proceedButton = FindCrystalSphereProceedButton(screen);
        if (proceedButton == null)
            return Error("Crystal Sphere proceed button is not enabled");

        proceedButton.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Proceeding from Crystal Sphere"
        };
    }

    private static NProceedButton? FindCrystalSphereProceedButton(NCrystalSphereScreen screen)
    {
        var namedButton = screen.GetNodeOrNull<NProceedButton>("%ProceedButton");
        if (IsControlVisibleOrActionable(namedButton))
            return namedButton;

        return FindAll<NProceedButton>(screen)
            .FirstOrDefault(IsControlVisibleOrActionable);
    }

    private static Creature? ResolveTarget(ICombatState combatState, string entityId)
    {
        // Try to match by entity_id pattern: "model_entry_N"
        // First try matching by combat_id if it's a pure number
        if (uint.TryParse(entityId, out uint combatId))
            return combatState.GetCreature(combatId);

        // Match by entity_id pattern (e.g., "jaw_worm_0")
        // We rebuild the entity IDs the same way as BuildEnemyState
        var entityCounts = new Dictionary<string, int>();
        foreach (var creature in combatState.Enemies)
        {
            string baseId = creature.Monster?.Id.Entry ?? "unknown";
            if (!entityCounts.TryGetValue(baseId, out int count))
                count = 0;
            entityCounts[baseId] = count + 1;
            string generatedId = $"{baseId}_{count}";

            if (generatedId == entityId && creature.IsAlive)
                return creature;
        }

        return null;
    }

    private static bool TryResolveOnlyAliveEnemy(ICombatState combatState, out Creature target)
    {
        target = null!;
        foreach (var creature in combatState.Enemies)
        {
            if (!creature.IsAlive)
                continue;

            if (target != null)
            {
                target = null!;
                return false;
            }

            target = creature;
        }

        return target != null;
    }

    [McpAction("menu_select", "Menu", "Select a menu, popup, character-select, or FTUE option.")]
    [McpActionField("option", "string", true, "Option id or advertised menu option.")]
    [McpActionField("seed", "string", false, "Optional custom seed for character select.")]
    [McpActionField("ascension", "int", false, "Optional ascension level for character select embark.")]
    internal static Dictionary<string, object?> ExecuteMenuSelect(
        string option,
        string? seed = null,
        int? ascension = null)
    {
        option = option.Trim();

        if (string.IsNullOrEmpty(option))
            return Error("Missing menu option");

        var tree = (Engine.GetMainLoop()) as SceneTree;
        if (tree?.Root == null)
            return Error("Cannot access scene tree");

        // Game over screen. Prefer the active overlay stack entry so hidden or
        // preloaded game-over nodes cannot hijack normal menu navigation.
        var gameOver = NOverlayStack.Instance?.Peek() as NGameOverScreen;
        gameOver ??= FindAll<NGameOverScreen>(tree.Root).FirstOrDefault(IsNodeVisible);
        if (gameOver != null)
        {
            if (string.Equals(option, "continue", System.StringComparison.OrdinalIgnoreCase))
                return Error("Game-over option 'continue' is not actionable. Use: main_menu");

            if (!string.Equals(option, "main_menu", System.StringComparison.OrdinalIgnoreCase))
                return Error($"Unknown game over option: {option}. Use: main_menu");

            var result = ClickMenuButtonField(gameOver, "_mainMenuButton", "Returning to main menu");
            if ((string?)result["status"] == "ok")
                return result;

            var returnMethod = gameOver.GetType().GetMethod(
                "ReturnToMainMenu",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (returnMethod != null)
            {
                returnMethod.Invoke(gameOver, null);
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Returning to main menu" };
            }

            return Error("Game-over main_menu option is not available");
        }

        // Tutorial/FTUE popup - "Enable Tutorials?" dialog
        var tutorialFtue = FindVisibleAcceptTutorialsFtue(tree.Root);
        if (tutorialFtue != null && IsFtueNodeActive(tutorialFtue))
        {
            if (!string.Equals(option, "yes", System.StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(option, "no", System.StringComparison.OrdinalIgnoreCase))
            {
                return Error($"Unknown tutorial prompt option: {option}. Use: yes, no");
            }

            var popup = tutorialFtue.GetType().GetField("_verticalPopup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(tutorialFtue);
            if (popup != null)
            {
                var isYes = string.Equals(option, "yes", System.StringComparison.OrdinalIgnoreCase);
                var btnField = isYes ? "<YesButton>k__BackingField" : "<NoButton>k__BackingField";
                var btn = popup.GetType().GetField(btnField, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(popup);
                if (btn is NClickableControl clickable &&
                    IsPopupButtonActionable(clickable))
                {
                    clickable.ForceClick();
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = $"Tutorials: {(isYes ? "enabled" : "disabled")}" };
                }
            }
            return Error("Tutorial popup visible but buttons not accessible");
        }

        // Any other FTUE/tutorial popup with a confirm button.
        var ftue = FindVisibleGenericFtue(tree.Root);
        if (ftue != null)
        {
            if (!string.Equals(option, "advance", System.StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(option, "proceed", System.StringComparison.OrdinalIgnoreCase))
            {
                return Error($"Unknown tutorial option: {option}. Use: advance, proceed");
            }

            var ftueClickable = FindFtueAdvanceButton(ftue);
            if (ftueClickable != null)
            {
                ftueClickable.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Dismissed tutorial popup" };
            }

            return Error("Tutorial popup visible but no actionable advance/proceed control is available; retry after the next state poll");
        }

        // Generic blocking menu popups, such as the first-run warning that appears
        // over character select on fresh profiles.
        var verticalPopup = FindVisibleVerticalPopup(tree.Root);
        if (verticalPopup != null)
            return ExecutePopupOption(GetPopupOptions(verticalPopup), option);

        var popupButtonOptions = GetVisiblePopupButtonOptions(tree.Root);
        if (popupButtonOptions.Count > 0)
            return ExecutePopupOption(popupButtonOptions, option);

        // Timeline screen - advance through epoch reveals.
        var timelineScreen = FindFirst<NTimelineScreen>(tree.Root);
        if (timelineScreen != null && IsNodeVisible(timelineScreen))
        {
            if (string.Equals(option, "advance", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(option, "proceed", System.StringComparison.OrdinalIgnoreCase))
            {
                // Check for timeline tutorial screen (first-time "Proceed" button).
                var tutorial = FindFirst<NTimelineTutorial>(tree.Root);
                if (tutorial != null && IsNodeVisible(tutorial))
                {
                    var ackBtn = tutorial.GetType().GetField("_acknowledgeButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(tutorial);
                    if (ackBtn is NClickableControl ackClickable &&
                        ackClickable.IsEnabled &&
                        IsNodeVisible(ackClickable))
                    {
                        ackClickable.ForceClick();
                        return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Clicked tutorial proceed button" };
                    }
                }

                // Check for confirm button
                var confirmBtn = FindFirst<NConfirmButton>(tree.Root);
                if (confirmBtn != null && IsNodeVisible(confirmBtn) && confirmBtn.IsEnabled)
                {
                    confirmBtn.ForceClick();
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Clicked confirm button" };
                }

                // Check for any clickable proceed button
                var proceedBtn = FindFirst<NProceedButton>(tree.Root);
                if (proceedBtn != null && IsNodeVisible(proceedBtn) && proceedBtn.IsEnabled)
                {
                    proceedBtn.ForceClick();
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Clicked proceed button" };
                }

                // Check for inspect screen (epoch detail view) - close it.
                var inspectScreen = FindFirst<NEpochInspectScreen>(tree.Root);
                if (inspectScreen != null && IsNodeVisible(inspectScreen))
                {
                    var closeBtn = inspectScreen.GetType().GetField("_closeButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(inspectScreen);
                    if (closeBtn is NClickableControl closeClickable &&
                        closeClickable.IsEnabled &&
                        IsNodeVisible(closeClickable))
                    {
                        closeClickable.ForceClick();
                        return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Closed epoch inspect screen" };
                    }
                    inspectScreen.Close();
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Closed epoch inspect screen" };
                }

                // Check for queued unlock screens.
                var queuedUnlockResult = TryHandleQueuedTimelineUnlock(timelineScreen);
                if (queuedUnlockResult != null)
                    return queuedUnlockResult;

                var unrevealedEpochs = GetProgressEpochIdsByState("Obtained", "ObtainedNoSlot");
                if (unrevealedEpochs.Count > 0)
                    return new Dictionary<string, object?>
                    {
                        ["status"] = "ok",
                        ["message"] = "Epoch unlocks are obtained but not revealed; not forcing timeline reveal from automation",
                        ["pending_epoch_ids"] = unrevealedEpochs,
                        ["manual_action_required"] = true,
                        ["done"] = true
                    };

                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "No more epochs to advance", ["done"] = true };
            }
            else if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
            {
                // Try NBackButton directly on NTimelineScreen
                var backBtn = timelineScreen.GetType().GetField("_backButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(timelineScreen);
                if (backBtn is NClickableControl backClickable)
                {
                    if (backClickable.IsEnabled)
                    {
                        backClickable.ForceClick();
                        return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Going back from timeline" };
                    }
                }
                // Try the submenu stack
                var stack = timelineScreen.GetType().GetField("_stack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(timelineScreen);
                if (stack != null)
                {
                    var popMethod = stack.GetType().GetMethod("Pop");
                    popMethod?.Invoke(stack, null);
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Popped timeline from stack" };
                }
                return Error("Back button not available on timeline");
            }
            return Error($"Unknown timeline option: {option}. Use: advance, back");
        }

        // Multiplayer Join Friend screen — friend list, refresh, back, or join_<index>/<player_id>.
        var joinScreen = FindFirst<NJoinFriendScreen>(tree.Root);
        if (joinScreen != null && IsNodeVisible(joinScreen))
        {
            return ExecuteJoinScreenMenuOption(joinScreen, option);
        }

        // Multiplayer Load Game lobby — confirm/embark/unready/back to resume saved MP run.
        var loadLobby = FindFirst<NMultiplayerLoadGameScreen>(tree.Root);
        if (loadLobby != null && IsNodeVisible(loadLobby))
        {
            return ExecuteLoadLobbyMenuOption(loadLobby, option);
        }

        // Character select can outlive or be mounted separately from NMainMenu,
        // so handle it before main-menu-specific routing.
        var charSelect = FindFirst<NCharacterSelectScreen>(tree.Root);
        if (charSelect != null && IsNodeVisible(charSelect))
        {
            return ExecuteCharacterSelectMenuOption(charSelect, option, seed, ascension);
        }

        var profileScreen = FindFirst<NProfileScreen>(tree.Root);
        if (profileScreen != null && IsNodeVisible(profileScreen))
        {
            return ExecuteProfileSelectMenuOption(profileScreen, option);
        }

        // Main menu — click a menu button
        var mainMenu = FindFirst<NMainMenu>(tree.Root);
        if (mainMenu != null)
        {
            // Check if we're on singleplayer submenu
            var spSubmenu = FindFirst<NSingleplayerSubmenu>(tree.Root);
            if (spSubmenu != null && IsNodeVisible(spSubmenu))
            {
                if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
                {
                    return ClickMenuButtonField(spSubmenu, "_backButton", "Going back", "Back button is not available");
                }

                var fieldName = option.ToLowerInvariant() switch
                {
                    "standard" => "_standardButton",
                    "daily" => "_dailyButton",
                    "custom" => "_customButton",
                    _ => null
                };
                if (fieldName == null)
                    return Error($"Unknown singleplayer option: {option}. Use: standard, daily, custom, back");

                return ClickMenuButtonField(spSubmenu, fieldName, $"Selected {option}", $"Option '{option}' is not available (locked)");
            }

            // Check if we're on multiplayer host submenu
            var mpHostSubmenu = FindFirst<NMultiplayerHostSubmenu>(tree.Root);
            if (mpHostSubmenu != null && IsNodeVisible(mpHostSubmenu))
            {
                if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
                {
                    return ClickMenuButtonField(mpHostSubmenu, "_backButton", "Going back", "Back button is not available");
                }

                var fieldName = option.ToLowerInvariant() switch
                {
                    "standard" => "_standardButton",
                    "daily" => "_dailyButton",
                    "custom" => "_customButton",
                    _ => null
                };
                if (fieldName == null)
                    return Error($"Unknown multiplayer host option: {option}. Use: standard, daily, custom, back");

                return ClickMenuButtonField(mpHostSubmenu, fieldName, $"Selected {option}", $"Option '{option}' is not available (locked)");
            }

            // Check if we're on multiplayer submenu
            var mpSubmenu = FindFirst<NMultiplayerSubmenu>(tree.Root);
            if (mpSubmenu != null && IsNodeVisible(mpSubmenu))
            {
                if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
                {
                    return ClickMenuButtonField(mpSubmenu, "_backButton", "Going back", "Back button is not available");
                }

                var fieldName = option.ToLowerInvariant() switch
                {
                    "host" => "_hostButton",
                    "join" => "_joinButton",
                    "load" => "_loadButton",
                    "abandon" => "_abandonButton",
                    _ => null
                };
                if (fieldName == null)
                    return Error($"Unknown multiplayer option: {option}. Use: host, join, load, abandon, back");

                return ClickMenuButtonField(mpSubmenu, fieldName, $"Selected {option}", $"Option '{option}' is not available");
            }

            // Main menu buttons
            var normalizedMainMenuOption = option.ToLowerInvariant();
            if (normalizedMainMenuOption == "timeline")
            {
                var unrevealedEpochs = GetProgressEpochIdsByState("Obtained", "ObtainedNoSlot");
                if (unrevealedEpochs.Count > 0)
                    return TimelineUnlocksNeedManualReveal(unrevealedEpochs);
            }

            var menuFieldName = normalizedMainMenuOption switch
            {
                "singleplayer" => "_singleplayerButton",
                "multiplayer" => "_multiplayerButton",
                "compendium" => "_compendiumButton",
                "timeline" => "_timelineButton",
                "settings" => "_settingsButton",
                "continue" => "_continueButton",
                "abandon_run" => "_abandonRunButton",
                "quit" => "_quitButton",
                _ => null
            };
            if (menuFieldName == null)
                return Error($"Unknown menu option: {option}");

            return ClickMenuButtonField(mainMenu, menuFieldName, $"Selected {option}", $"Option '{option}' is not available");
        }

        return Error("Not on a menu screen");
    }

    private static Dictionary<string, object?> TimelineUnlocksNeedManualReveal(List<string> unrevealedEpochs)
    {
        return new Dictionary<string, object?>
        {
            ["status"] = "error",
            ["error"] = "Timeline has obtained epochs that still need to be revealed manually; not opening Timeline because this game state logs invalid unlock-state errors when entered through automation",
            ["pending_epoch_ids"] = unrevealedEpochs,
            ["manual_action_required"] = true
        };
    }

    private static Dictionary<string, object?> ExecutePopupOption(
        List<(string Name, NClickableControl Button)> options,
        string option)
    {
        foreach (var candidate in options)
        {
            if (!string.Equals(candidate.Name, option, System.StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsPopupButtonActionable(candidate.Button))
                return Error($"Popup option '{candidate.Name}' is disabled");

            candidate.Button.ForceClick();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Selected popup option: {candidate.Name}"
            };
        }

        return Error($"Popup option '{option}' is not actionable. Use: {string.Join(", ", options.Select(candidate => candidate.Name))}");
    }

    private static Dictionary<string, object?> ExecuteProfileSelectMenuOption(
        NProfileScreen profileScreen,
        string option)
    {
        if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
        {
            var backBtn = GetInstanceFieldValue(profileScreen, "_backButton")
                ?? FindFirst<NBackButton>(profileScreen);
            if (backBtn is NClickableControl backClickable &&
                backClickable.IsEnabled &&
                IsNodeVisible(backClickable))
            {
                backClickable.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Going back from profile select" };
            }
            return Error("Back button not available on profile select");
        }

        var normalized = option.ToLowerInvariant();
        if (normalized.StartsWith("profile_", System.StringComparison.Ordinal))
            normalized = normalized["profile_".Length..];
        else if (normalized.StartsWith("slot_", System.StringComparison.Ordinal))
            normalized = normalized["slot_".Length..];

        if (int.TryParse(normalized, out var profileId))
            return ExecuteProfileAction("switch", profileId);

        return Error($"Unknown profile select option: {option}. Use: profile_1, profile_2, profile_3, back");
    }

    private static Dictionary<string, object?> ExecuteJoinScreenMenuOption(
        NJoinFriendScreen joinScreen,
        string option)
    {
        var normalized = option.ToLowerInvariant();

        if (normalized == "back")
        {
            return ClickMenuButtonField(joinScreen, "_backButton", "Going back from join screen", "Back button is not available");
        }

        if (normalized == "refresh")
        {
            var refreshBtn = GetInstanceFieldValue(joinScreen, "_refreshButton") as NClickableControl;
            if (refreshBtn != null && refreshBtn.IsEnabled && IsNodeVisible(refreshBtn))
            {
                refreshBtn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Refreshing friend list" };
            }
            return Error("Refresh button not available");
        }

        // join_<index> or join_<player_id>
        if (normalized.StartsWith("join_", System.StringComparison.Ordinal))
        {
            var key = normalized["join_".Length..];
            var buttonContainer = GetInstanceFieldValue(joinScreen, "_buttonContainer") as Control;
            if (buttonContainer == null)
                return Error("Friend list container not available");

            // Build the in-order friend list once
            var friendButtons = new List<NJoinFriendButton>();
            foreach (var child in buttonContainer.GetChildren())
            {
                if (child is NJoinFriendButton fbtn)
                    friendButtons.Add(fbtn);
            }

            if (friendButtons.Count == 0)
                return Error("No friends with open lobbies. Use 'refresh' to retry.");

            // Try as index first
            if (int.TryParse(key, out int idx))
            {
                if (idx < 0 || idx >= friendButtons.Count)
                    return Error($"Friend index {idx} out of range (0..{friendButtons.Count - 1})");
                if (!friendButtons[idx].IsEnabled)
                    return Error($"Friend at index {idx} is not joinable right now");
                friendButtons[idx].ForceClick();
                return new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["message"] = $"Joining friend at index {idx}"
                };
            }

            // Otherwise treat as ulong player id
            if (ulong.TryParse(key, out ulong playerId))
            {
                var match = friendButtons.FirstOrDefault(b => b.PlayerId == playerId);
                if (match == null)
                    return Error($"No friend with player_id {playerId} in current list. Available indices: 0..{friendButtons.Count - 1}");
                if (!match.IsEnabled)
                    return Error($"Friend {playerId} is not joinable right now");
                match.ForceClick();
                return new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["message"] = $"Joining friend {playerId}"
                };
            }

            return Error($"Unknown join target: '{key}'. Use join_<index> (e.g. join_0) or join_<player_id>.");
        }

        return Error($"Unknown join screen option: {option}. Use: refresh, back, join_<index>, join_<player_id>");
    }

    private static Dictionary<string, object?> ExecuteLoadLobbyMenuOption(
        NMultiplayerLoadGameScreen loadLobby,
        string option)
    {
        var normalized = option.ToLowerInvariant();

        if (normalized == "confirm" || normalized == "embark" || normalized == "ready")
        {
            var confirmBtn = GetInstanceFieldValue(loadLobby, "_confirmButton") as NClickableControl;
            if (confirmBtn != null && confirmBtn.IsEnabled && IsNodeVisible(confirmBtn))
            {
                confirmBtn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Readied up to load saved MP run" };
            }
            return Error("Confirm button not available — already ready, or lobby not yet initialized");
        }

        if (normalized == "unready")
        {
            var unreadyBtn = GetInstanceFieldValue(loadLobby, "_unreadyButton") as NClickableControl;
            if (unreadyBtn != null && unreadyBtn.IsEnabled && IsNodeVisible(unreadyBtn))
            {
                unreadyBtn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Retracted ready vote" };
            }
            return Error("Unready button not available — you have not confirmed yet");
        }

        if (normalized == "back")
        {
            return ClickMenuButtonField(loadLobby, "_backButton", "Going back from load lobby", "Back button is not available");
        }

        return Error($"Unknown load lobby option: {option}. Use: confirm, embark, unready, back");
    }

    private static Dictionary<string, object?> ExecuteCharacterSelectMenuOption(
        NCharacterSelectScreen charSelect,
        string option,
        string? seed,
        int? ascension)
    {
        if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
        {
            // _backButton leaves the lobby (and disconnects in MP). _unreadyButton is a
            // separate control that only becomes enabled after the player has hit
            // confirm/embark in MP — it retracts the ready vote without leaving. Surface
            // them as distinct options so callers can pick deliberately. Fall back to
            // unready when only it is actionable so older callers don't get stuck.
            var backBtn = GetInstanceFieldValue(charSelect, "_backButton") as NClickableControl;
            if (backBtn != null && backBtn.IsEnabled && IsControlVisibleOrActionable(backBtn))
            {
                backBtn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Going back" };
            }
            var unreadyFallback = GetInstanceFieldValue(charSelect, "_unreadyButton") as NClickableControl;
            if (unreadyFallback != null && unreadyFallback.IsEnabled && IsControlVisibleOrActionable(unreadyFallback))
            {
                unreadyFallback.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Retracted ready vote (back fell through to unready)" };
            }
            return Error("Back button not available");
        }

        if (string.Equals(option, "unready", System.StringComparison.OrdinalIgnoreCase))
        {
            var unreadyBtn = GetInstanceFieldValue(charSelect, "_unreadyButton") as NClickableControl;
            if (unreadyBtn != null && unreadyBtn.IsEnabled && IsControlVisibleOrActionable(unreadyBtn))
            {
                unreadyBtn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Retracted ready vote" };
            }
            return Error("Unready button not available — you have not confirmed yet");
        }

        if (string.Equals(option, "confirm", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option, "embark", System.StringComparison.OrdinalIgnoreCase))
        {
            if (ascension.HasValue)
            {
                if (charSelect.Lobby == null)
                    return Error("Ascension embark failed before starting the run: character select lobby is unavailable.");

                if (!TrySetLobbyAscension(charSelect.Lobby, ascension.Value, out var error))
                    return Error(error);
            }

            if (!string.IsNullOrWhiteSpace(seed))
            {
                seed = seed.Trim();
                if (charSelect.Lobby == null)
                {
                    return Error("Seeded embark failed before starting the run: character select lobby is unavailable.");
                }

                if (charSelect.Lobby.NetService.Type == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Singleplayer)
                {
                    if (NGame.Instance == null)
                    {
                        return Error("Seeded embark failed before starting the run: game instance is unavailable.");
                    }

                    NGame.Instance.DebugSeedOverride = seed;
                }
                else
                {
                    try
                    {
                        charSelect.Lobby.SetSeed(seed);
                    }
                    catch (System.Exception ex)
                    {
                        return Error($"Seeded embark failed before starting the run: {ex.Message}");
                    }
                }
            }

            var embarkBtn = GetInstanceFieldValue(charSelect, "_embarkButton");
            if (embarkBtn is NClickableControl embarkClickable && embarkClickable.IsEnabled)
            {
                var msg = string.IsNullOrEmpty(seed) ? "Embarking on run" : $"Embarking on run (seed: {seed})";
                embarkClickable.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = msg };
            }
            return Error("Embark button not available — select a character first");
        }

        var buttons = FindAll<NCharacterSelectButton>(charSelect);
        foreach (var btn in buttons)
        {
            if (btn.Character != null && (
                string.Equals(btn.Character.Id.ToString(), option, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(btn.Character.Id.Entry, option, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(SafeGetText(() => btn.Character.Title), option, System.StringComparison.OrdinalIgnoreCase)))
            {
                if (btn.IsLocked)
                    return Error($"Character '{option}' is locked");
                btn.Select();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = $"Selected {SafeGetText(() => btn.Character.Title)}. Use 'confirm' to embark." };
            }
        }
        return Error($"Character '{option}' not found. Available: {string.Join(", ", buttons.Where(b => !b.IsLocked).Select(b => b.Character?.Id.Entry))}");
    }

    private static bool TrySetLobbyAscension(object lobby, int ascension, out string error)
    {
        error = "";
        if (ascension < 0)
        {
            error = "ascension must be non-negative.";
            return false;
        }

        var type = lobby.GetType();
        var setAscension = type.GetMethod(
            "SetAscension",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(int)]);
        if (setAscension != null)
        {
            setAscension.Invoke(lobby, [ascension]);
            return true;
        }

        var property = type.GetProperty(
            "Ascension",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.CanWrite == true)
        {
            property.SetValue(lobby, ascension);
            return true;
        }

        var field = type.GetField(
            "Ascension",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(lobby, ascension);
            return true;
        }

        error = "Ascension embark failed before starting the run: lobby does not expose a writable ascension control.";
        return false;
    }

    private static Dictionary<string, object?>? TryHandleQueuedTimelineUnlock(NTimelineScreen timelineScreen)
    {
        bool isQueued;
        try
        {
            isQueued = timelineScreen.IsScreenQueued();
        }
        catch (System.ObjectDisposedException)
        {
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Timeline changed while checking queued unlocks; retry after the next state poll",
                ["retry"] = true
            };
        }

        if (!isQueued)
            return null;

        var queuedScreen = TryPeekQueuedTimelineScreen(timelineScreen);
        var queuedType = queuedScreen?.GetType().Name ?? "unlock";
        var queuedEpochIds = GetQueuedTimelineEpochIds(queuedScreen);
        var alreadyRevealed = queuedEpochIds
            .Where(id => string.Equals(GetProgressEpochState(id), "Revealed", System.StringComparison.OrdinalIgnoreCase))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (alreadyRevealed.Count > 0)
        {
            var alreadyRevealedSet = new HashSet<string>(alreadyRevealed, System.StringComparer.OrdinalIgnoreCase);
            var pendingEpochIds = queuedEpochIds
                .Where(id => !alreadyRevealedSet.Contains(id))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Queued timeline unlock references already revealed epochs; not opening it to avoid an invalid unlock path",
                ["queued_unlock_type"] = queuedType,
                ["already_revealed_epoch_ids"] = alreadyRevealed,
                ["pending_epoch_ids"] = pendingEpochIds,
                ["manual_action_required"] = pendingEpochIds.Count > 0,
                ["done"] = true
            };
        }

        if (string.Equals(queuedType, "NUnlockTimelineScreen", System.StringComparison.Ordinal) &&
            IsTimelineScreenBusy(timelineScreen))
        {
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Timeline expansion is queued, but the timeline is still animating; retry after the next state poll",
                ["queued_unlock_type"] = queuedType,
                ["pending_epoch_ids"] = queuedEpochIds,
                ["retry"] = true
            };
        }

        try
        {
            timelineScreen.OpenQueuedScreen();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Opening queued {queuedType}",
                ["queued_unlock_type"] = queuedType,
                ["pending_epoch_ids"] = queuedEpochIds
            };
        }
        catch (System.ObjectDisposedException)
        {
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Timeline changed before the queued unlock could open; retry after the next state poll",
                ["queued_unlock_type"] = queuedType,
                ["retry"] = true
            };
        }
        catch (System.Exception ex)
        {
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Skipped queued {queuedType}: {ex.Message}",
                ["queued_unlock_type"] = queuedType,
                ["retry"] = true
            };
        }
    }

    private static object? TryPeekQueuedTimelineScreen(NTimelineScreen timelineScreen)
    {
        try
        {
            var queue = GetInstanceFieldValue(timelineScreen, "_unlockScreens");
            if (queue == null)
                return null;

            var count = queue.GetType().GetProperty("Count")?.GetValue(queue) as int?;
            if (count <= 0)
                return null;

            return queue.GetType().GetMethod("Peek")?.Invoke(queue, null);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTimelineScreenBusy(NTimelineScreen timelineScreen)
    {
        try
        {
            var isUiVisible = GetInstanceFieldValue(timelineScreen, "_isUiVisible") as bool?;
            if (isUiVisible == false)
                return true;

            var inputBlocker = GetInstanceFieldValue(timelineScreen, "_inputBlocker") as Control;
            if (inputBlocker != null && IsNodeVisible(inputBlocker))
                return true;
        }
        catch (System.ObjectDisposedException)
        {
            return true;
        }

        return false;
    }

    private static List<string> GetQueuedTimelineEpochIds(object? queuedScreen)
    {
        var ids = new List<string>();
        if (queuedScreen == null)
            return ids;

        AddEpochIdsFromObject(GetInstanceFieldValue(queuedScreen, "_epoch"), ids);
        AddEpochIdsFromObject(GetInstanceFieldValue(queuedScreen, "_unlockedEpochs"), ids);
        AddEpochIdsFromObject(GetInstanceFieldValue(queuedScreen, "_erasToUnlock"), ids);

        return ids.Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddEpochIdsFromObject(object? value, List<string> ids)
    {
        if (value == null)
            return;

        if (value is string id)
        {
            ids.Add(id);
            return;
        }

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
                AddEpochIdsFromObject(item, ids);
            return;
        }

        var model = value.GetType().GetProperty("Model")?.GetValue(value) ?? value;
        var modelId = model.GetType().GetProperty("Id")?.GetValue(model)?.ToString();
        if (!string.IsNullOrEmpty(modelId))
            ids.Add(modelId);
    }

    private static string? GetProgressEpochState(string epochId)
    {
        try
        {
            var progress = MegaCrit.Sts2.Core.Saves.SaveManager.Instance?.Progress;
            return progress?.Epochs
                .FirstOrDefault(epoch => string.Equals(epoch.Id, epochId, System.StringComparison.OrdinalIgnoreCase))
                ?.State
                .ToString();
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GetProgressEpochIdsByState(params string[] states)
    {
        var stateSet = new HashSet<string>(states, System.StringComparer.OrdinalIgnoreCase);
        try
        {
            var progress = MegaCrit.Sts2.Core.Saves.SaveManager.Instance?.Progress;
            if (progress == null)
                return new List<string>();

            return progress.Epochs
                .Where(epoch => stateSet.Contains(epoch.State.ToString()))
                .Select(epoch => epoch.Id)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static Dictionary<string, object?> ClickMenuButtonField(
        object owner,
        string fieldName,
        string successMessage,
        string? disabledMessage = null)
    {
        var btn = GetInstanceFieldValue(owner, fieldName);
        if (btn is NClickableControl clickable)
        {
            if (!clickable.Visible || !clickable.IsVisibleInTree())
                return Error(disabledMessage ?? $"Option '{fieldName}' is not available");

            if (!clickable.IsEnabled)
                return Error(disabledMessage ?? $"Option '{fieldName}' is not available");

            clickable.ForceClick();
            return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = successMessage };
        }

        return Error($"Could not find button '{fieldName}'");
    }
}
