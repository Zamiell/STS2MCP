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

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.TryManualPlay))]
    private static class CardModelTryManualPlayReplayPatch
    {
        private static void Prefix(CardModel __instance, Creature? target)
        {
            TryRecordObservedManualPlayCard(__instance, target);
        }
    }

    [HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.EndTurn))]
    private static class PlayerCmdEndTurnReplayPatch
    {
        private static void Prefix(Player player)
        {
            TryRecordObservedEndTurn(player);
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

    private static void TryRecordQueuedCombatAction(GameAction action)
    {
        if (action is PlayCardAction playCardAction)
            TryRecordObservedPlayCard(playCardAction);
        else if (action is EndPlayerTurnAction endTurnAction)
            TryRecordObservedEndPlayerTurnAction(endTurnAction);
    }

    private static void TryRecordObservedManualPlayCard(CardModel card, Creature? target)
    {
        if (!TryGetLocalReplayPlayer(out var player)
            || !ReferenceEquals(card.Owner, player)
            || !card.CanPlayTargeting(target)
            || !TryGetCardIndexInHand(player, card, out var cardIndex))
        {
            return;
        }

        RecordObservedPlayCard(card, cardIndex, target);
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

    private static void TryRecordObservedEndTurn(Player player)
    {
        if (!TryGetLocalReplayPlayer(out var localPlayer) || !ReferenceEquals(player, localPlayer))
            return;

        if (WasObservedCombatCommandJustRecorded("end_turn", null, null))
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
            if (!enemy.IsAlive)
                continue;

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
