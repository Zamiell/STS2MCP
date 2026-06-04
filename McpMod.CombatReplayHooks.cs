using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private static string? _lastObservedCombatCommandSignature;
    private static DateTimeOffset _lastObservedCombatCommandAt = DateTimeOffset.MinValue;

    [HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue))]
    private static class ActionQueueSynchronizerRequestEnqueueReplayPatch
    {
        private static void Prefix(GameAction action)
        {
            TryRecordQueuedCombatAction(action);
        }
    }

    [HarmonyPatch(typeof(ActionExecutor), "AfterActionFinished")]
    private static class ActionExecutorAfterActionFinishedReplayPlaybackPatch
    {
        private static void Postfix(GameAction action)
        {
            RememberCompletedReplayAction(action);
        }
    }

    [HarmonyPatch(typeof(PotionModel), nameof(PotionModel.EnqueueManualUse))]
    private static class PotionModelEnqueueManualUseReplayPatch
    {
        private static void Prefix(PotionModel __instance, Creature target)
        {
            TryRecordObservedPotionUse(__instance, target);
        }
    }

    [HarmonyPatch(typeof(PotionModel), nameof(PotionModel.Discard))]
    private static class PotionModelDiscardReplayPatch
    {
        private static void Prefix(PotionModel __instance)
        {
            TryRecordObservedPotionDiscard(__instance);
        }
    }

    [HarmonyPatch(typeof(NChooseACardSelectionScreen), "SelectHolder")]
    private static class NChooseACardSelectionScreenSelectHolderReplayPatch
    {
        private static void Prefix(NCardHolder cardHolder)
        {
            TryRecordObservedChooseCard(cardHolder);
        }
    }

    [HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.ChooseLocalOption))]
    private static class RestSiteSynchronizerChooseLocalOptionReplayPatch
    {
        private static void Prefix(RestSiteSynchronizer __instance, int index)
        {
            TryRecordObservedRestSiteOption(__instance, index);
        }
    }

    private static void TryRecordQueuedCombatAction(GameAction action)
    {
        if (action is PlayCardAction playCardAction)
            TryRecordObservedPlayCard(playCardAction);
        else if (action is EndPlayerTurnAction endTurnAction)
            TryRecordObservedEndPlayerTurnAction(endTurnAction);
    }

    private static void TryRecordObservedPlayCard(PlayCardAction action)
    {
        if (!TryGetLocalReplayPlayer(out var player) || !ReferenceEquals(action.Player, player))
            return;

        var card = TryGetPlayCardActionCard(action);
        if (card == null || !TryGetCardIndexInHand(player, card, out var cardIndex))
            return;

        if (WasObservedCombatCommandJustRecorded("play_card", card, action.Target))
            return;

        RecordObservedPlayCard(card, cardIndex, action.Target);
    }

    private static void RecordObservedPlayCard(CardModel card, int cardIndex, Creature? target)
    {
        RememberObservedCombatCommand("play_card", card, target);
        var command = new Dictionary<string, object?>
        {
            ["action"] = "play_card",
            ["card_index"] = cardIndex,
            ["card_id"] = card.Id.Entry
        };

        if (target != null && TryGetCreatureEntityId(target, out var targetId))
            command["target"] = targetId;

        RecordObservedCombatReplayCommand(command);
    }

    private static void TryRecordObservedEndPlayerTurnAction(EndPlayerTurnAction action)
    {
        if (!TryGetLocalReplayPlayer(out var player) || !TryGetEndPlayerTurnActionPlayer(action, out var actionPlayer))
            return;

        if (!ReferenceEquals(player, actionPlayer) || WasObservedCombatCommandJustRecorded("end_turn", null, null))
            return;

        RememberObservedCombatCommand("end_turn", null, null);
        RecordObservedCombatReplayCommand(new Dictionary<string, object?>
        {
            ["action"] = "end_turn"
        });
    }

    private static bool WasObservedCombatCommandJustRecorded(string action, CardModel? card, Creature? target)
    {
        if (_lastObservedCombatCommandSignature != BuildObservedCombatCommandSignature(action, card, target))
            return false;

        return DateTimeOffset.UtcNow - _lastObservedCombatCommandAt < TimeSpan.FromMilliseconds(250);
    }

    private static void RememberObservedCombatCommand(string action, CardModel? card, Creature? target)
    {
        _lastObservedCombatCommandSignature = BuildObservedCombatCommandSignature(action, card, target);
        _lastObservedCombatCommandAt = DateTimeOffset.UtcNow;
    }

    private static string BuildObservedCombatCommandSignature(string action, CardModel? card, Creature? target)
    {
        var cardId = card == null ? "" : card.Id.ToString();
        var targetId = target?.CombatId?.ToString() ?? "";
        return $"{action}|{cardId}|{targetId}";
    }

    private static void TryRecordObservedPotionUse(PotionModel potion, Creature target)
    {
        if (!TryGetLocalReplayPlayer(out var player)
            || potion.Owner == null
            || !ReferenceEquals(potion.Owner, player)
            || !TryGetPotionSlot(player, potion, out var slot))
        {
            return;
        }

        var command = new Dictionary<string, object?>
        {
            ["action"] = "use_potion",
            ["slot"] = slot
        };

        if (target != null && TryGetCreatureEntityId(target, out var targetId))
            command["target"] = targetId;

        RecordObservedCombatReplayCommand(command);
    }

    private static void TryRecordObservedPotionDiscard(PotionModel potion)
    {
        if (!TryGetLocalReplayPlayer(out var player)
            || potion.Owner == null
            || !ReferenceEquals(potion.Owner, player)
            || !TryGetPotionSlot(player, potion, out var slot))
        {
            return;
        }

        RecordObservedCombatReplayCommand(new Dictionary<string, object?>
        {
            ["action"] = "discard_potion",
            ["slot"] = slot
        });
    }

    private static void TryRecordObservedChooseCard(NCardHolder cardHolder)
    {
        if (!TryGetLocalReplayPlayer(out _))
            return;

        var card = cardHolder.CardModel;
        if (card == null)
            return;

        RecordObservedCombatReplayCommand(new Dictionary<string, object?>
        {
            ["action"] = "select_card",
            ["card_id"] = card.Id.Entry
        });
    }

    private static void TryRecordObservedRestSiteOption(RestSiteSynchronizer synchronizer, int index)
    {
        if (_suppressReplayRecording || RunManager.Instance?.IsInProgress != true)
            return;

        var options = synchronizer.GetLocalOptions();
        if (index < 0 || index >= options.Count)
            return;

        var optionId = options[index].OptionId;
        if (string.IsNullOrWhiteSpace(optionId))
            return;

        RecordObservedReplayCommand(new Dictionary<string, object?>
        {
            ["action"] = "choose_rest_option",
            ["option_id"] = optionId
        });
    }

    private static bool TryGetLocalReplayPlayer(out Player player)
    {
        player = null!;

        if (RunManager.Instance?.IsInProgress != true || CombatManager.Instance?.IsInProgress != true)
            return false;

        var runState = RunManager.Instance.DebugOnlyGetState();
        var localPlayer = runState == null ? null : LocalContext.GetMe(runState);
        if (localPlayer == null)
            return false;

        player = localPlayer;
        return true;
    }

    private static CardModel? TryGetPlayCardActionCard(PlayCardAction action)
    {
        try
        {
            return action.NetCombatCard.ToCardModelOrNull()
                ?? typeof(PlayCardAction)
                    .GetField("_card", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(action) as CardModel;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetEndPlayerTurnActionPlayer(EndPlayerTurnAction action, out Player player)
    {
        player = null!;
        try
        {
            player = typeof(EndPlayerTurnAction)
                .GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(action) as Player ?? null!;
            return player != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetCardIndexInHand(Player player, CardModel card, out int cardIndex)
    {
        cardIndex = -1;
        var hand = player.PlayerCombatState?.Hand;
        if (hand == null)
            return false;

        for (var i = 0; i < hand.Cards.Count; i++)
        {
            if (ReferenceEquals(hand.Cards[i], card))
            {
                cardIndex = i;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPotionSlot(Player player, PotionModel potion, out int slot)
    {
        for (slot = 0; slot < player.PotionSlots.Count; slot++)
        {
            if (ReferenceEquals(player.GetPotionAtSlotIndex(slot), potion))
                return true;
        }

        slot = -1;
        return false;
    }

    private static bool TryGetCreatureEntityId(Creature creature, out string entityId)
    {
        entityId = "";

        var combatState = creature.CombatState;
        if (combatState == null)
            return false;

        var entityCounts = new Dictionary<string, int>();
        foreach (var enemy in combatState.Enemies)
        {
            var baseId = enemy.Monster?.Id.Entry ?? "unknown";
            if (!entityCounts.TryGetValue(baseId, out var count))
                count = 0;
            entityCounts[baseId] = count + 1;

            if (ReferenceEquals(enemy, creature))
            {
                entityId = $"{baseId}_{count}";
                return true;
            }
        }

        return false;
    }
}
