using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace STS2_MCP;

public static partial class McpMod
{
    private const int MaxPendingReplayCommands = 64;
    private static readonly object _replayRecorderLock = new();
    private static readonly List<string> _pendingReplayCommands = [];
    private static readonly List<string> _pendingCombatReplayCommands = [];
    private static string? _activeReplayRunKey;
    private static string? _activeReplayPath;
    private static string? _activeTracePath;
    private static DateTimeOffset _nextReplayFileEnsureAt = DateTimeOffset.MinValue;
    private static DateTimeOffset _nextReplayTraceCheckAt = DateTimeOffset.MinValue;
    private static int _recordedObservedCommandCount;
    private static int _traceSequence;
    private static string? _lastTraceStateSignature;
    private static bool _wasCombatReplayInProgress;

    private static readonly JsonSerializerOptions _replayJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static void RecordReplayCommandIfSuccessful(
        Dictionary<string, JsonElement> command,
        Dictionary<string, object?> result)
    {
        if (_suppressReplayRecording)
            return;

        if (!ShouldRecordReplayCommand(command, result))
            return;

        try
        {
            if (IsCombatReplayCommand(command) && CombatManager.Instance?.IsInProgress == true)
                return;

            var recordCommand = BuildReplayRecordedCommand(command, result);
            var line = JsonSerializer.Serialize(recordCommand, _replayJsonOptions);
            var runInfo = TryGetReplayRunInfo(logMissing: true);

            lock (_replayRecorderLock)
            {
                if (runInfo == null)
                {
                    if (_pendingCombatReplayCommands.Count > 0)
                    {
                        _pendingCombatReplayCommands.Add(line);
                        return;
                    }

                    BufferPendingReplayCommand(line);
                    return;
                }

                EnsureReplayFileForRun(runInfo);
                if (_pendingCombatReplayCommands.Count > 0)
                {
                    foreach (var pendingCombatLine in _pendingCombatReplayCommands)
                    {
                        File.AppendAllText(runInfo.Path, pendingCombatLine + System.Environment.NewLine);
                        AppendTraceSnapshot(runInfo, "after_replay_command", DeserializeReplayLine(pendingCombatLine));
                    }
                    _pendingCombatReplayCommands.Clear();
                }

                if (AppendReplayCommandLine(runInfo.Path, line, recordCommand))
                    AppendTraceSnapshot(runInfo, "after_replay_command", recordCommand);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to record replay command: {ex.Message}");
        }
    }

    private static Dictionary<string, JsonElement> BuildReplayRecordedCommand(
        Dictionary<string, JsonElement> command,
        Dictionary<string, object?> result)
    {
        if (IsReplayCommandAction(command, "choose_rest_option")
            && result.TryGetValue("option_id", out var optionId)
            && !string.IsNullOrWhiteSpace(Convert.ToString(optionId, CultureInfo.InvariantCulture)))
        {
            var stable = new Dictionary<string, object?>
            {
                ["action"] = "choose_rest_option",
                ["option_id"] = Convert.ToString(optionId, CultureInfo.InvariantCulture)
            };
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                JsonSerializer.Serialize(stable, _replayJsonOptions))!;
        }

        if (!IsReplayCommandAction(command, "choose_map_node"))
            return command;

        if (!result.TryGetValue("col", out var col)
            || !result.TryGetValue("row", out var row))
        {
            return command;
        }

        var enriched = new Dictionary<string, object?>();
        foreach (var (key, value) in command)
            enriched[key] = JsonElementToObject(value);

        enriched["col"] = Convert.ToInt32(col, CultureInfo.InvariantCulture);
        enriched["row"] = Convert.ToInt32(row, CultureInfo.InvariantCulture);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(enriched, _replayJsonOptions))!;
    }

    private static object? JsonElementToObject(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => JsonSerializer.Deserialize<object>(element.GetRawText())
        };

    private static void MaybeFlushCompletedCombatReplay()
    {
        var isCombatInProgress = CombatManager.Instance?.IsInProgress == true;

        if (!_wasCombatReplayInProgress && isCombatInProgress)
        {
            lock (_replayRecorderLock)
            {
                if (_pendingCombatReplayCommands.Count == 0)
                    _pendingCombatReplayCommands.Clear();
            }
        }

        if (_wasCombatReplayInProgress && !isCombatInProgress)
            FlushPendingCombatReplayCommands();

        _wasCombatReplayInProgress = isCombatInProgress;
    }

    private static void FlushPendingCombatReplayCommands()
    {
        try
        {
            lock (_replayRecorderLock)
            {
                if (_pendingCombatReplayCommands.Count == 0)
                    return;

                var runInfo = TryGetReplayRunInfo(logMissing: true);
                var path = runInfo?.Path ?? _activeReplayPath;
                if (string.IsNullOrWhiteSpace(path))
                    return;

                if (runInfo != null)
                    EnsureReplayFileForRun(runInfo);
                else
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                foreach (var line in _pendingCombatReplayCommands)
                {
                    File.AppendAllText(path, line + System.Environment.NewLine);
                    if (runInfo != null)
                        AppendTraceSnapshot(runInfo, "after_combat_replay_command", DeserializeReplayLine(line));
                }

                _pendingCombatReplayCommands.Clear();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to flush combat replay commands: {ex.Message}");
        }
    }

    private static void RecordObservedCombatReplayCommand(Dictionary<string, object?> command)
    {
        if (_suppressReplayRecording)
            return;

        if (CombatManager.Instance?.IsInProgress != true)
            return;

        try
        {
            var line = SerializeReplayCommand(command);
            lock (_replayRecorderLock)
                _pendingCombatReplayCommands.Add(line);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to record combat replay command: {ex.Message}");
        }
    }

    private static void RecordObservedReplayCommand(Dictionary<string, object?> command)
    {
        if (_suppressReplayRecording)
            return;

        try
        {
            var line = SerializeReplayCommand(command);
            var runInfo = TryGetReplayRunInfo(logMissing: true);

            lock (_replayRecorderLock)
            {
                if (runInfo == null)
                {
                    BufferPendingReplayCommand(line);
                    return;
                }

                EnsureReplayFileForRun(runInfo);
                if (AppendReplayLineIfLastDifferent(runInfo.Path, line))
                    AppendTraceSnapshot(runInfo, "after_observed_replay_command", command);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to record observed replay command: {ex.Message}");
        }
    }

    private static bool IsCombatReplayCommand(Dictionary<string, JsonElement> command)
    {
        if (!command.TryGetValue("action", out var actionElement))
            return false;

        return actionElement.GetString() switch
        {
            "play_card" => true,
            "end_turn" => true,
            "use_potion" => true,
            "discard_potion" => true,
            "combat_select_card" => true,
            "combat_confirm_selection" => true,
            "select_card" => true,
            "select_deck_card" => true,
            "confirm_selection" => true,
            "cancel_selection" => true,
            _ => false
        };
    }

    private static void MaybeEnsureReplayFileForCurrentRun()
    {
        if (_suppressReplayRecording)
            return;

        var now = DateTimeOffset.UtcNow;
        if (now < _nextReplayFileEnsureAt)
            return;

        _nextReplayFileEnsureAt = now.AddSeconds(1);
        EnsureReplayFileForCurrentRun();
    }

    private static void MaybeAppendReplayTraceForCurrentState()
    {
        if (_suppressReplayRecording)
            return;

        var now = DateTimeOffset.UtcNow;
        if (now < _nextReplayTraceCheckAt)
            return;

        _nextReplayTraceCheckAt = now.AddMilliseconds(250);

        try
        {
            var runInfo = TryGetReplayRunInfo(logMissing: false);
            if (runInfo == null)
                return;

            lock (_replayRecorderLock)
            {
                EnsureReplayFileForRun(runInfo);
                AppendTraceSnapshotIfStateChanged(runInfo, "state_changed");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to append replay trace state: {ex.Message}");
        }
    }

    private static void EnsureReplayFileForCurrentRun()
    {
        try
        {
            var runInfo = TryGetReplayRunInfo(logMissing: false);
            if (runInfo == null)
                return;

            lock (_replayRecorderLock)
                EnsureReplayFileForRun(runInfo);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to ensure replay file for current run: {ex.Message}");
        }
    }

    private static void EnsureReplayFileForRun(ReplayRunInfo runInfo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(runInfo.Path)!);
        Directory.CreateDirectory(Path.GetDirectoryName(runInfo.TracePath)!);

        if (_activeReplayRunKey != runInfo.RunKey)
        {
            _activeReplayRunKey = runInfo.RunKey;
            _activeReplayPath = runInfo.Path;
            _activeTracePath = runInfo.TracePath;
            _recordedObservedCommandCount = -1;
            _traceSequence = CountExistingTraceEntries(runInfo.TracePath);
            _lastTraceStateSignature = null;

            if (!File.Exists(runInfo.Path))
            {
                File.WriteAllText(runInfo.Path, "");
                GD.Print($"[STS2 MCP] ReplayRecorder: created {runInfo.Path}");
            }

            if (!File.Exists(runInfo.TracePath))
            {
                File.WriteAllText(runInfo.TracePath, "");
                GD.Print($"[STS2 MCP] ReplayRecorder: created {runInfo.TracePath}");
            }

            foreach (var pendingLine in _pendingReplayCommands)
            {
                File.AppendAllText(runInfo.Path, pendingLine + System.Environment.NewLine);
                AppendTraceSnapshot(runInfo, "after_pending_replay_command", DeserializeReplayLine(pendingLine));
            }
            _pendingReplayCommands.Clear();
        }

        if (!File.Exists(runInfo.Path))
        {
            File.WriteAllText(runInfo.Path, "");
            GD.Print($"[STS2 MCP] ReplayRecorder: recreated {runInfo.Path}");
        }

        if (!File.Exists(runInfo.TracePath))
        {
            File.WriteAllText(runInfo.TracePath, "");
            GD.Print($"[STS2 MCP] ReplayRecorder: recreated {runInfo.TracePath}");
        }

        if (_pendingCombatReplayCommands.Count == 0)
            AppendObservedCommands(runInfo);
    }

    private static void AppendObservedCommands(ReplayRunInfo runInfo)
    {
        var observedLines = FilterObservedLinesForExistingNeowSelection(
            runInfo.Path,
            BuildObservedCommandLines(runInfo).ToList());
        _recordedObservedCommandCount = CountExistingObservedSubsequence(runInfo.Path, observedLines);

        for (var i = _recordedObservedCommandCount; i < observedLines.Count; i++)
            File.AppendAllText(runInfo.Path, observedLines[i] + System.Environment.NewLine);

        _recordedObservedCommandCount = observedLines.Count;
    }

    private static List<string> FilterObservedLinesForExistingNeowSelection(string path, List<string> observedLines)
    {
        if (!File.Exists(path))
            return observedLines;

        var existingLines = File.ReadAllLines(path).ToList();
        if (!HasExplicitNeowSelection(existingLines))
            return observedLines;

        var neowChoiceIndex = FindFirstActionIndex(observedLines, "choose_event_option");
        if (neowChoiceIndex < 0)
            return observedLines;

        var neowProceedIndex = FindFirstActionIndex(observedLines, "proceed", neowChoiceIndex + 1);
        if (neowProceedIndex < 0)
            return observedLines;

        var filtered = new List<string>(observedLines.Count);
        for (var i = 0; i < observedLines.Count; i++)
        {
            if (i > neowChoiceIndex
                && i < neowProceedIndex
                && IsSelectionReplayLine(observedLines[i]))
            {
                continue;
            }

            filtered.Add(observedLines[i]);
        }

        return filtered;
    }

    private static bool HasExplicitNeowSelection(List<string> existingLines)
    {
        var neowChoiceIndex = FindFirstActionIndex(existingLines, "choose_event_option");
        if (neowChoiceIndex < 0)
            return false;

        var neowProceedIndex = FindFirstActionIndex(existingLines, "proceed", neowChoiceIndex + 1);
        if (neowProceedIndex < 0)
            neowProceedIndex = existingLines.Count;

        for (var i = neowChoiceIndex + 1; i < neowProceedIndex; i++)
        {
            if (IsSelectionReplayLine(existingLines[i]))
                return true;
        }

        return false;
    }

    private static int FindFirstActionIndex(List<string> lines, string action, int startIndex = 0)
    {
        for (var i = Math.Max(0, startIndex); i < lines.Count; i++)
        {
            if (ReplayLineHasAction(lines[i], action))
                return i;
        }

        return -1;
    }

    private static bool IsSelectionReplayLine(string line) =>
        ReplayLineHasAction(line, "select_card")
        || ReplayLineHasAction(line, "select_deck_card")
        || ReplayLineHasAction(line, "select_card_reward_by_id")
        || ReplayLineHasAction(line, "confirm_selection")
        || ReplayLineHasAction(line, "cancel_selection")
        || ReplayLineHasAction(line, "select_bundle")
        || ReplayLineHasAction(line, "confirm_bundle_selection")
        || ReplayLineHasAction(line, "cancel_bundle_selection");

    private static bool ReplayLineHasAction(string line, string action)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("action", out var actionElement)
                   && string.Equals(actionElement.GetString(), action, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static int CountExistingObservedSubsequence(string path, List<string> observedLines)
    {
        if (!File.Exists(path))
            return 0;

        var count = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (count >= observedLines.Count)
                break;

            if (ReplayLinesMatch(line, observedLines[count]))
                count++;
        }
        return count;
    }

    private static bool ReplayLinesMatch(string left, string right) =>
        NormalizeReplayLineForComparison(left) == NormalizeReplayLineForComparison(right);

    private static string NormalizeReplayLineForComparison(string line) =>
        line.Replace("\"CARD.", "\"", StringComparison.OrdinalIgnoreCase)
            .Replace("\"POTION.", "\"", StringComparison.OrdinalIgnoreCase)
            .Replace("\"RELIC.", "\"", StringComparison.OrdinalIgnoreCase)
            .Replace("\"CHARACTER.", "\"", StringComparison.OrdinalIgnoreCase);

    private static bool AppendReplayLineIfLastDifferent(string path, string line)
    {
        if (File.Exists(path))
        {
            var lastLine = File.ReadLines(path).LastOrDefault();
            if (lastLine == line)
                return false;
        }

        File.AppendAllText(path, line + System.Environment.NewLine);
        return true;
    }

    private static bool AppendReplayCommandLine(
        string path,
        string line,
        Dictionary<string, JsonElement> command)
    {
        if (IsReplayCommandAction(command, "choose_map_node")
            && File.Exists(path)
            && ReplayLinesMatch(File.ReadLines(path).LastOrDefault() ?? "", line))
        {
            return false;
        }

        File.AppendAllText(path, line + System.Environment.NewLine);
        return true;
    }

    private static void AppendTraceSnapshotIfStateChanged(ReplayRunInfo runInfo, string reason)
    {
        var state = BuildGameState();
        var signature = BuildTraceStateSignature(state);
        if (signature == _lastTraceStateSignature)
            return;

        _lastTraceStateSignature = signature;
        AppendTraceSnapshot(runInfo, reason, null, state);
    }

    private static void AppendTraceSnapshot(
        ReplayRunInfo runInfo,
        string reason,
        object? command,
        Dictionary<string, object?>? state = null)
    {
        try
        {
            state ??= BuildGameState();
            _lastTraceStateSignature = BuildTraceStateSignature(state);

            var entry = new Dictionary<string, object?>
            {
                ["step"] = _traceSequence++,
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["reason"] = reason,
                ["replay_command_count"] = CountReplayCommands(runInfo.Path),
                ["command"] = command,
                ["state_type"] = state.TryGetValue("state_type", out var stateType) ? stateType : null,
                ["floor"] = TryGetNestedTraceValue(state, "run", "floor"),
                ["state"] = state
            };

            File.AppendAllText(
                runInfo.TracePath,
                JsonSerializer.Serialize(entry, _replayJsonOptions) + System.Environment.NewLine);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to append replay trace snapshot: {ex.Message}");
        }
    }

    private static string BuildTraceStateSignature(Dictionary<string, object?> state) =>
        JsonSerializer.Serialize(state, _replayJsonOptions);

    private static object? TryGetNestedTraceValue(
        Dictionary<string, object?> state,
        string objectKey,
        string valueKey)
    {
        return state.TryGetValue(objectKey, out var nested)
               && nested is Dictionary<string, object?> nestedDictionary
               && nestedDictionary.TryGetValue(valueKey, out var value)
            ? value
            : null;
    }

    private static int CountReplayCommands(string path) =>
        File.Exists(path) ? File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line)) : 0;

    private static int CountExistingTraceEntries(string path) =>
        File.Exists(path) ? File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line)) : 0;

    private static Dictionary<string, JsonElement>? DeserializeReplayLine(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsReplayCommandAction(Dictionary<string, JsonElement> command, string action) =>
        command.TryGetValue("action", out var actionElement)
        && string.Equals(actionElement.GetString(), action, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> BuildObservedCommandLines(ReplayRunInfo runInfo)
    {
        yield return SerializeReplayCommand(new Dictionary<string, object?> { ["action"] = "menu_select", ["option"] = "singleplayer" });
        yield return SerializeReplayCommand(new Dictionary<string, object?> { ["action"] = "menu_select", ["option"] = runInfo.GameMode });
        yield return SerializeReplayCommand(new Dictionary<string, object?> { ["action"] = "menu_select", ["option"] = runInfo.CharacterId });
        yield return SerializeReplayCommand(new Dictionary<string, object?>
        {
            ["action"] = "menu_select",
            ["option"] = "confirm",
            ["seed"] = runInfo.Seed,
            ["ascension"] = runInfo.Ascension
        });

        using var stream = new FileStream(
            runInfo.CurrentRunPath,
            FileMode.Open,
            System.IO.FileAccess.Read,
            FileShare.ReadWrite);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var firstActHistory = TryGetFirstActHistory(root);
        var neowChoice = firstActHistory == null ? null : TryGetChosenNeowOptionIndex(firstActHistory.Value);
        if (neowChoice.HasValue)
        {
            yield return SerializeReplayCommand(new Dictionary<string, object?>
            {
                ["action"] = "choose_event_option",
                ["index"] = neowChoice.Value
            });

            var neowSelectionCommands = BuildObservedNeowSelectionCommands(root, firstActHistory!.Value).ToList();
            foreach (var command in neowSelectionCommands)
                yield return command;

            if (neowSelectionCommands.Count > 0)
                yield return SerializeReplayCommand(new Dictionary<string, object?> { ["action"] = "proceed" });
        }

        foreach (var command in BuildObservedMapAndRoomCommands(root, firstActHistory))
            yield return command;
    }

    private static IEnumerable<string> BuildObservedNeowSelectionCommands(
        JsonElement root,
        JsonElement firstActHistory)
    {
        if (firstActHistory.GetArrayLength() == 0
            || !TryGetFirstPlayerStats(firstActHistory[0], out var stats))
        {
            yield break;
        }

        var workingDeck = BuildInitialNeowDeck(root);
        var emittedDeckSelection = false;
        if (stats.TryGetProperty("cards_removed", out var cardsRemoved)
            && cardsRemoved.ValueKind == JsonValueKind.Array)
        {
            foreach (var card in cardsRemoved.EnumerateArray())
            {
                if (!card.TryGetProperty("id", out var cardId))
                    continue;

                var removedCard = ReadSavedCard(card);
                var deckIndex = FindCardIndex(workingDeck, removedCard);
                if (deckIndex < 0)
                    continue;

                yield return SerializeReplayCommand(new Dictionary<string, object?>
                {
                    ["action"] = "select_deck_card",
                    ["deck_index"] = deckIndex,
                    ["card_id"] = cardId.GetString()
                });

                emittedDeckSelection = true;
                workingDeck.RemoveAt(deckIndex);
            }
        }

        if (stats.TryGetProperty("upgraded_cards", out var upgradedCards)
            && upgradedCards.ValueKind == JsonValueKind.Array)
        {
            var currentDeck = ReadCurrentDeck(root);
            foreach (var card in upgradedCards.EnumerateArray())
            {
                var cardId = card.GetString();
                if (string.IsNullOrWhiteSpace(cardId))
                    continue;

                var deckIndex = FindUpgradedDeckIndex(workingDeck, currentDeck, cardId);
                if (deckIndex < 0)
                    continue;

                yield return SerializeReplayCommand(new Dictionary<string, object?>
                {
                    ["action"] = "select_deck_card",
                    ["deck_index"] = deckIndex,
                    ["card_id"] = cardId
                });

                emittedDeckSelection = true;
                workingDeck[deckIndex] = workingDeck[deckIndex] with
                {
                    UpgradeLevel = workingDeck[deckIndex].UpgradeLevel + 1
                };
            }
        }

        if (IsChosenNeowReward(stats, "SCROLL_BOXES")
            && stats.TryGetProperty("cards_gained", out var cardsGained)
            && cardsGained.ValueKind == JsonValueKind.Array)
        {
            foreach (var card in cardsGained.EnumerateArray())
            {
                if (!card.TryGetProperty("id", out var cardId))
                    continue;

                yield return SerializeReplayCommand(new Dictionary<string, object?>
                {
                    ["action"] = "select_card_reward_by_id",
                    ["card_id"] = cardId.GetString()
                });
            }
        }

        if (IsChosenNeowReward(stats, "NEOWS_BONES"))
        {
            var neowsBonesRelics = GetNeowsBonesRewardRelicIds(stats);
            foreach (var relicId in neowsBonesRelics)
            {
                yield return SerializeReplayCommand(new Dictionary<string, object?>
                {
                    ["action"] = "claim_reward_by_match",
                    ["type"] = "relic",
                    ["relic_id"] = relicId
                });
            }

            foreach (var cardId in GetNeowChosenCardIds(stats))
            {
                yield return SerializeReplayCommand(new Dictionary<string, object?>
                {
                    ["action"] = "select_card",
                    ["card_id"] = cardId
                });
            }
        }

        if (emittedDeckSelection)
            yield return SerializeReplayCommand(new Dictionary<string, object?> { ["action"] = "confirm_selection" });
    }

    private static List<string> GetNeowsBonesRewardRelicIds(JsonElement stats)
    {
        var pickedRelics = GetPickedRelicIds(stats)
            .Where(relicId => !string.Equals(relicId, "NEOWS_BONES", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (pickedRelics.Count <= 2)
            return pickedRelics;

        return [pickedRelics[0], pickedRelics[^1]];
    }

    private static List<string> GetPickedRelicIds(JsonElement stats)
    {
        var relicIds = new List<string>();
        if (!stats.TryGetProperty("relic_choices", out var relicChoices)
            || relicChoices.ValueKind != JsonValueKind.Array)
        {
            return relicIds;
        }

        foreach (var relicChoice in relicChoices.EnumerateArray())
        {
            if (!relicChoice.TryGetProperty("was_picked", out var wasPicked)
                || wasPicked.ValueKind != JsonValueKind.True
                || !relicChoice.TryGetProperty("choice", out var choice))
            {
                continue;
            }

            var relicId = StripModelPrefix(choice.GetString(), "RELIC.");
            if (!string.IsNullOrWhiteSpace(relicId))
                relicIds.Add(relicId);
        }

        return relicIds;
    }

    private static List<string> GetNeowChosenCardIds(JsonElement stats)
    {
        var cardIds = new List<string>();
        if (!stats.TryGetProperty("cards_gained", out var cardsGained)
            || cardsGained.ValueKind != JsonValueKind.Array)
        {
            return cardIds;
        }

        foreach (var card in cardsGained.EnumerateArray())
        {
            if (!card.TryGetProperty("id", out var cardIdElement))
                continue;

            var cardId = StripModelPrefix(cardIdElement.GetString(), "CARD.");
            if (string.IsNullOrWhiteSpace(cardId)
                || IsBasicStarterCardId(cardId)
                || IsCurseCardId(cardId))
            {
                continue;
            }

            cardIds.Add(cardId);
        }

        return cardIds;
    }

    private static bool IsBasicStarterCardId(string cardId) =>
        cardId.Contains("STRIKE", StringComparison.OrdinalIgnoreCase)
        || cardId.Contains("DEFEND", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurseCardId(string cardId) =>
        cardId is "ASCENDERS_BANE" or "CLUMSY" or "CURSE_OF_THE_BELL" or "DECAY" or "DOUBT" or "INJURY" or "NECRONOMICURSE" or "NORMALITY" or "PAIN" or "PARASITE" or "REGRET" or "SHAME" or "WRITHE";

    private static string StripModelPrefix(string? value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;
    }

    private static bool IsChosenNeowReward(JsonElement stats, string rewardId)
    {
        if (stats.TryGetProperty("ancient_choice", out var choices)
            && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("was_chosen", out var wasChosen)
                    || wasChosen.ValueKind != JsonValueKind.True)
                {
                    continue;
                }

                if (choice.TryGetProperty("TextKey", out var textKey)
                    && string.Equals(textKey.GetString(), rewardId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (stats.TryGetProperty("relic_choices", out var relicChoices)
            && relicChoices.ValueKind == JsonValueKind.Array)
        {
            foreach (var relicChoice in relicChoices.EnumerateArray())
            {
                if (!relicChoice.TryGetProperty("was_picked", out var wasPicked)
                    || wasPicked.ValueKind != JsonValueKind.True
                    || !relicChoice.TryGetProperty("choice", out var choice))
                {
                    continue;
                }

                if (string.Equals(choice.GetString(), $"RELIC.{rewardId}", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static List<ReplayCardSnapshot> BuildInitialNeowDeck(JsonElement root)
    {
        var currentDeck = ReadCurrentDeck(root);
        var starterDeck = TryGetStarterDeck(root);
        if (starterDeck.Count == 0)
            return currentDeck;

        var working = starterDeck.ToList();
        var unmatchedCurrentCards = currentDeck.ToList();
        foreach (var starterCard in starterDeck)
        {
            var currentIndex = FindCardIndex(unmatchedCurrentCards, starterCard);
            if (currentIndex >= 0)
                unmatchedCurrentCards.RemoveAt(currentIndex);
        }

        working.AddRange(unmatchedCurrentCards);
        return working;
    }

    private static List<ReplayCardSnapshot> TryGetStarterDeck(JsonElement root)
    {
        var characterId = TryGetCharacterId(root);
        if (string.IsNullOrWhiteSpace(characterId))
            return [];

        var character = ModelDb.AllCharacters.FirstOrDefault(candidate =>
            string.Equals(candidate.Id.ToString(), characterId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Id.Entry, characterId, StringComparison.OrdinalIgnoreCase));
        if (character == null)
            return [];

        return character.StartingDeck
            .Select(card => new ReplayCardSnapshot(card.Id.ToString(), card.CurrentUpgradeLevel))
            .ToList();
    }

    private static List<ReplayCardSnapshot> ReadCurrentDeck(JsonElement root)
    {
        if (!root.TryGetProperty("players", out var players)
            || players.ValueKind != JsonValueKind.Array
            || players.GetArrayLength() == 0
            || !players[0].TryGetProperty("deck", out var deck)
            || deck.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return deck.EnumerateArray().Select(ReadSavedCard).ToList();
    }

    private static ReplayCardSnapshot ReadSavedCard(JsonElement card)
    {
        var cardId = card.TryGetProperty("id", out var cardIdElement)
            ? cardIdElement.GetString() ?? ""
            : "";
        var upgradeLevel = card.TryGetProperty("current_upgrade_level", out var upgradeElement)
                           && upgradeElement.TryGetInt32(out var upgrades)
            ? upgrades
            : 0;

        return new ReplayCardSnapshot(cardId, upgradeLevel);
    }

    private static int FindCardIndex(List<ReplayCardSnapshot> cards, ReplayCardSnapshot target)
    {
        for (var i = 0; i < cards.Count; i++)
        {
            if (CardsMatch(cards[i], target))
                return i;
        }

        return -1;
    }

    private static bool CardsMatch(ReplayCardSnapshot left, ReplayCardSnapshot right)
    {
        return left.UpgradeLevel == right.UpgradeLevel
               && CardIdsMatch(left.CardId, right.CardId);
    }

    private static int FindUpgradedDeckIndex(
        List<ReplayCardSnapshot> beforeDeck,
        List<ReplayCardSnapshot> currentDeck,
        string upgradedCardId)
    {
        var count = Math.Min(beforeDeck.Count, currentDeck.Count);
        for (var i = 0; i < count; i++)
        {
            if (CardIdsMatch(currentDeck[i].CardId, upgradedCardId)
                && CardIdsMatch(beforeDeck[i].CardId, currentDeck[i].CardId)
                && currentDeck[i].UpgradeLevel > beforeDeck[i].UpgradeLevel)
            {
                return i;
            }
        }

        for (var i = 0; i < beforeDeck.Count; i++)
        {
            if (CardIdsMatch(beforeDeck[i].CardId, upgradedCardId))
                return i;
        }

        return -1;
    }

    private static bool CardIdsMatch(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
               || string.Equals(StripModelCategory(left), StripModelCategory(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string StripModelCategory(string modelId)
    {
        var separator = modelId.IndexOf('.');
        return separator < 0 ? modelId : modelId[(separator + 1)..];
    }

    private static IEnumerable<string> BuildObservedMapAndRoomCommands(
        JsonElement root,
        JsonElement? firstActHistory)
    {
        if (!root.TryGetProperty("visited_map_coords", out var visited)
            || visited.ValueKind != JsonValueKind.Array
            || visited.GetArrayLength() < 2)
        {
            yield break;
        }

        for (var i = 1; i < visited.GetArrayLength(); i++)
        {
            var mapChoice = TryGetMapChoice(root, visited[i - 1], visited[i]);
            if (mapChoice != null)
                yield return SerializeReplayCommand(new Dictionary<string, object?>
                {
                    ["action"] = "choose_map_node",
                    ["index"] = mapChoice.Value.Index,
                    ["col"] = mapChoice.Value.Col,
                    ["row"] = mapChoice.Value.Row
                });

            if (firstActHistory.HasValue && i < firstActHistory.Value.GetArrayLength())
            {
                foreach (var command in BuildObservedCompletedRoomCommands(firstActHistory.Value[i]))
                    yield return command;
            }
        }
    }

    private static IEnumerable<string> BuildObservedCompletedRoomCommands(JsonElement mapPointHistory)
    {
        if (!TryGetFirstPlayerStats(mapPointHistory, out var stats))
        {
            yield break;
        }

        var isTreasure = MapPointHasRoomType(mapPointHistory, "treasure");
        if (isTreasure)
        {
            foreach (var command in BuildObservedTreasureCommands(stats))
                yield return command;
        }

        foreach (var command in BuildObservedRestSiteCommands(stats))
            yield return command;

        if (!isTreasure
            && stats.TryGetProperty("gold_gained", out var goldGained)
            && goldGained.TryGetInt32(out var gold)
            && gold > 0)
        {
            yield return SerializeReplayCommand(new Dictionary<string, object?>
            {
                ["action"] = "claim_reward_by_match",
                ["type"] = "gold",
                ["gold_amount"] = gold
            });
        }

        if (stats.TryGetProperty("potion_choices", out var potionChoices)
            && potionChoices.ValueKind == JsonValueKind.Array)
        {
            foreach (var potionChoice in potionChoices.EnumerateArray())
            {
                if (!potionChoice.TryGetProperty("was_picked", out var wasPicked)
                    || wasPicked.ValueKind != JsonValueKind.True
                    || !potionChoice.TryGetProperty("choice", out var choice))
                {
                    continue;
                }

                yield return SerializeReplayCommand(new Dictionary<string, object?>
                {
                    ["action"] = "claim_reward_by_match",
                    ["type"] = "potion",
                    ["potion_id"] = choice.GetString()
                });
            }
        }

        if (!isTreasure
            && stats.TryGetProperty("relic_choices", out var relicChoices)
            && relicChoices.ValueKind == JsonValueKind.Array)
        {
            foreach (var relicChoice in relicChoices.EnumerateArray())
            {
                if (!relicChoice.TryGetProperty("was_picked", out var wasPicked)
                    || wasPicked.ValueKind != JsonValueKind.True
                    || !relicChoice.TryGetProperty("choice", out var choice))
                {
                    continue;
                }

                yield return SerializeReplayCommand(new Dictionary<string, object?>
                {
                    ["action"] = "claim_reward_by_match",
                    ["type"] = "relic",
                    ["relic_id"] = StripModelPrefix(choice.GetString(), "RELIC.")
                });
            }
        }

        if (stats.TryGetProperty("event_choices", out var eventChoices)
            && eventChoices.ValueKind == JsonValueKind.Array)
        {
            foreach (var eventChoice in eventChoices.EnumerateArray())
            {
                if (!eventChoice.TryGetProperty("title", out var title)
                    || !title.TryGetProperty("key", out var keyElement))
                {
                    continue;
                }

                var titleKey = keyElement.GetString();
                if (string.IsNullOrWhiteSpace(titleKey))
                    continue;

                yield return SerializeReplayCommand(new Dictionary<string, object?>
                {
                    ["action"] = "choose_event_option_by_title_key",
                    ["title_key"] = titleKey
                });
            }
        }

        foreach (var command in BuildObservedShopCardRemovalCommands(stats))
            yield return command;

        if (stats.TryGetProperty("card_choices", out var cardChoices)
            && cardChoices.ValueKind == JsonValueKind.Array)
        {
            foreach (var cardChoice in cardChoices.EnumerateArray())
            {
                if (!cardChoice.TryGetProperty("was_picked", out var wasPicked)
                    || wasPicked.ValueKind != JsonValueKind.True
                    || !cardChoice.TryGetProperty("card", out var card)
                    || !card.TryGetProperty("id", out var cardId))
                {
                    continue;
                }

                yield return SerializeReplayCommand(new Dictionary<string, object?>
                {
                    ["action"] = "claim_reward_by_match",
                    ["type"] = "card"
                });
                yield return SerializeReplayCommand(new Dictionary<string, object?>
                {
                    ["action"] = "select_card_reward_by_id",
                    ["card_id"] = cardId.GetString()
                });
            }
        }

        yield return SerializeReplayCommand(new Dictionary<string, object?> { ["action"] = "proceed" });
    }

    private static IEnumerable<string> BuildObservedTreasureCommands(JsonElement stats)
    {
        foreach (var _ in GetPickedRelicIds(stats))
        {
            yield return SerializeReplayCommand(new Dictionary<string, object?>
            {
                ["action"] = "claim_treasure_relic"
            });
        }
    }

    private static bool MapPointHasRoomType(JsonElement mapPointHistory, string roomType)
    {
        if (!mapPointHistory.TryGetProperty("rooms", out var rooms)
            || rooms.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var room in rooms.EnumerateArray())
        {
            if (room.TryGetProperty("room_type", out var roomTypeElement)
                && string.Equals(roomTypeElement.GetString(), roomType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> BuildObservedRestSiteCommands(JsonElement stats)
    {
        if (!stats.TryGetProperty("rest_site_choices", out var restChoices)
            || restChoices.ValueKind != JsonValueKind.Array
            || restChoices.GetArrayLength() == 0)
        {
            yield break;
        }

        foreach (var restChoice in restChoices.EnumerateArray())
        {
            var optionId = restChoice.GetString();
            if (string.IsNullOrWhiteSpace(optionId))
                continue;

            yield return SerializeReplayCommand(new Dictionary<string, object?>
            {
                ["action"] = "choose_rest_option",
                ["option_id"] = optionId
            });

            if (string.Equals(optionId, "SMITH", StringComparison.OrdinalIgnoreCase)
                && stats.TryGetProperty("upgraded_cards", out var upgradedCards)
                && upgradedCards.ValueKind == JsonValueKind.Array)
            {
                foreach (var upgradedCard in upgradedCards.EnumerateArray())
                {
                    var cardId = StripModelPrefix(upgradedCard.GetString(), "CARD.");
                    if (string.IsNullOrWhiteSpace(cardId))
                        continue;

                    yield return SerializeReplayCommand(new Dictionary<string, object?>
                    {
                        ["action"] = "select_deck_card",
                        ["card_id"] = cardId
                    });
                    yield return SerializeReplayCommand(new Dictionary<string, object?>
                    {
                        ["action"] = "confirm_selection"
                    });
                }
            }
        }
    }

    private static IEnumerable<string> BuildObservedShopCardRemovalCommands(JsonElement stats)
    {
        if (!stats.TryGetProperty("cards_removed", out var cardsRemoved)
            || cardsRemoved.ValueKind != JsonValueKind.Array
            || cardsRemoved.GetArrayLength() == 0
            || !stats.TryGetProperty("gold_spent", out var goldSpent)
            || !goldSpent.TryGetInt32(out var spent)
            || spent <= 0)
        {
            yield break;
        }

        var shopRemovalIndex = TryGetShopCardRemovalIndex(stats);
        if (shopRemovalIndex == null)
            yield break;

        foreach (var removedCard in cardsRemoved.EnumerateArray())
        {
            if (!removedCard.TryGetProperty("id", out var cardIdElement))
                continue;

            var cardId = StripModelPrefix(cardIdElement.GetString(), "CARD.");
            if (string.IsNullOrWhiteSpace(cardId))
                continue;

            yield return SerializeReplayCommand(new Dictionary<string, object?>
            {
                ["action"] = "shop_purchase",
                ["index"] = shopRemovalIndex.Value
            });
            yield return SerializeReplayCommand(new Dictionary<string, object?>
            {
                ["action"] = "select_deck_card",
                ["card_id"] = cardId
            });
            yield return SerializeReplayCommand(new Dictionary<string, object?>
            {
                ["action"] = "confirm_selection"
            });
        }
    }

    private static int? TryGetShopCardRemovalIndex(JsonElement stats)
    {
        if (!stats.TryGetProperty("card_choices", out var cardChoices)
            || cardChoices.ValueKind != JsonValueKind.Array
            || !stats.TryGetProperty("relic_choices", out var relicChoices)
            || relicChoices.ValueKind != JsonValueKind.Array
            || !stats.TryGetProperty("potion_choices", out var potionChoices)
            || potionChoices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (HasPickedChoice(cardChoices, "was_picked")
            || HasPickedChoice(relicChoices, "was_picked")
            || HasPickedChoice(potionChoices, "was_picked"))
        {
            return null;
        }

        return cardChoices.GetArrayLength() + relicChoices.GetArrayLength() + potionChoices.GetArrayLength();
    }

    private static bool HasPickedChoice(JsonElement choices, string propertyName)
    {
        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty(propertyName, out var wasPicked)
                && wasPicked.ValueKind == JsonValueKind.True)
            {
                return true;
            }
        }

        return false;
    }

    private static JsonElement? TryGetFirstActHistory(JsonElement root)
    {
        if (!root.TryGetProperty("map_point_history", out var history)
            || history.ValueKind != JsonValueKind.Array
            || history.GetArrayLength() == 0)
        {
            return null;
        }

        var firstActHistory = history[0];
        if (firstActHistory.ValueKind != JsonValueKind.Array || firstActHistory.GetArrayLength() == 0)
            return null;

        return firstActHistory;
    }

    private static int? TryGetChosenNeowOptionIndex(JsonElement firstActHistory)
    {
        var firstPoint = firstActHistory[0];
        if (firstPoint.ValueKind != JsonValueKind.Object)
            return null;

        if (!firstPoint.TryGetProperty("rooms", out var rooms)
            || rooms.ValueKind != JsonValueKind.Array
            || rooms.GetArrayLength() == 0
            || !rooms[0].TryGetProperty("model_id", out var modelId)
            || modelId.GetString() != "EVENT.NEOW")
        {
            return null;
        }

        if (!firstPoint.TryGetProperty("player_stats", out var playerStats)
            || playerStats.ValueKind != JsonValueKind.Array
            || playerStats.GetArrayLength() == 0
            || !playerStats[0].TryGetProperty("ancient_choice", out var choices)
            || choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        for (var i = 0; i < choices.GetArrayLength(); i++)
        {
            if (choices[i].TryGetProperty("was_chosen", out var chosen)
                && chosen.ValueKind == JsonValueKind.True)
            {
                return i;
            }
        }

        return null;
    }

    private static bool TryGetFirstPlayerStats(JsonElement mapPointHistory, out JsonElement stats)
    {
        stats = default;
        if (mapPointHistory.ValueKind != JsonValueKind.Object
            || !mapPointHistory.TryGetProperty("player_stats", out var playerStats)
            || playerStats.ValueKind != JsonValueKind.Array
            || playerStats.GetArrayLength() == 0)
        {
            return false;
        }

        stats = playerStats[0];
        return true;
    }

    private static MapChoice? TryGetMapChoice(JsonElement root, JsonElement previousCoord, JsonElement selectedCoord)
    {
        if (!TryGetCoord(previousCoord, out var previousCol, out var previousRow)
            || !TryGetCoord(selectedCoord, out var selectedCol, out var selectedRow))
            return null;

        if (!root.TryGetProperty("acts", out var acts)
            || acts.ValueKind != JsonValueKind.Array
            || acts.GetArrayLength() == 0
            || !acts[0].TryGetProperty("saved_map", out var savedMap)
            || !savedMap.TryGetProperty("start_coords", out var startCoords)
            || startCoords.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var options = new List<JsonElement>();
        if (previousRow == 0)
        {
            options.AddRange(startCoords.EnumerateArray());
        }
        else if (TryGetSavedMapPoint(savedMap, previousCol, previousRow, out var previousPoint)
                 && previousPoint.TryGetProperty("children", out var children)
                 && children.ValueKind == JsonValueKind.Array)
        {
            options.AddRange(children.EnumerateArray());
        }

        var sortedOptions = options
            .Where(option => TryGetCoord(option, out _, out _))
            .OrderBy(option =>
            {
                TryGetCoord(option, out var col, out _);
                return col;
            })
            .ToList();

        for (var i = 0; i < sortedOptions.Count; i++)
        {
            if (TryGetCoord(sortedOptions[i], out var col, out var row)
                && col == selectedCol
                && row == selectedRow)
            {
                return new MapChoice(i, selectedCol, selectedRow);
            }
        }

        var wingedOptions = GetSavedMapPointsInRow(savedMap, selectedRow)
            .OrderBy(point =>
            {
                TryGetCoord(point.GetProperty("coord"), out var col, out _);
                return col;
            })
            .ToList();
        for (var i = 0; i < wingedOptions.Count; i++)
        {
            if (TryGetCoord(wingedOptions[i].GetProperty("coord"), out var col, out var row)
                && col == selectedCol
                && row == selectedRow)
            {
                return new MapChoice(i, selectedCol, selectedRow);
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> GetSavedMapPointsInRow(JsonElement savedMap, int row)
    {
        if (!savedMap.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind == JsonValueKind.Object
                && point.TryGetProperty("coord", out var coord)
                && TryGetCoord(coord, out _, out var candidateRow)
                && candidateRow == row)
            {
                yield return point;
            }
        }
    }

    private static bool TryGetSavedMapPoint(
        JsonElement savedMap,
        int col,
        int row,
        out JsonElement point)
    {
        point = default;
        if (!savedMap.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var candidate in points.EnumerateArray())
        {
            if (candidate.ValueKind == JsonValueKind.Object
                && candidate.TryGetProperty("coord", out var coord)
                && TryGetCoord(coord, out var candidateCol, out var candidateRow)
                && candidateCol == col
                && candidateRow == row)
            {
                point = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetCoord(JsonElement coord, out int col, out int row)
    {
        col = 0;
        row = 0;
        return coord.ValueKind == JsonValueKind.Object
               && coord.TryGetProperty("col", out var colElement)
               && coord.TryGetProperty("row", out var rowElement)
               && colElement.TryGetInt32(out col)
               && rowElement.TryGetInt32(out row);
    }

    private readonly record struct MapChoice(int Index, int Col, int Row);

    private static string SerializeReplayCommand(Dictionary<string, object?> command) =>
        JsonSerializer.Serialize(command, _replayJsonOptions);

    private static bool ShouldRecordReplayCommand(
        Dictionary<string, JsonElement> command,
        Dictionary<string, object?> result)
    {
        if (!command.TryGetValue("action", out var actionElement))
            return false;

        var action = actionElement.GetString();
        if (string.IsNullOrWhiteSpace(action))
            return false;

        if (action == "menu_select")
        {
            if (!command.TryGetValue("option", out var optionElement))
                return false;

            var option = optionElement.GetString();
            if (!IsReplayStartMenuOption(option))
                return false;

            if (string.Equals(option, "confirm", StringComparison.OrdinalIgnoreCase)
                && !command.ContainsKey("seed"))
            {
                return false;
            }
        }

        if (action is "return_to_main_menu" or "start_replay" or "get_replay_status" or "cancel_replay")
            return false;

        if (result.ContainsKey("error"))
            return false;

        if (result.TryGetValue("status", out var status)
            && string.Equals(Convert.ToString(status, CultureInfo.InvariantCulture), "error", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool IsReplayStartMenuOption(string? option)
    {
        if (string.IsNullOrWhiteSpace(option))
            return false;

        return string.Equals(option, "singleplayer", StringComparison.OrdinalIgnoreCase)
               || string.Equals(option, "standard", StringComparison.OrdinalIgnoreCase)
               || string.Equals(option, "confirm", StringComparison.OrdinalIgnoreCase)
               || option.StartsWith("CHARACTER.", StringComparison.OrdinalIgnoreCase);
    }

    private static void BufferPendingReplayCommand(string line)
    {
        _pendingReplayCommands.Add(line);
        if (_pendingReplayCommands.Count > MaxPendingReplayCommands)
            _pendingReplayCommands.RemoveAt(0);
    }

    private static ReplayRunInfo? TryGetReplayRunInfo(bool logMissing)
    {
        if (RunManager.Instance?.IsInProgress != true)
        {
            if (logMissing)
                GD.Print("[STS2 MCP] ReplayRecorder: run not in progress, skipping");
            return null;
        }

        var saveManager = SaveManager.Instance;
        if (saveManager == null)
        {
            if (logMissing)
                GD.PrintErr("[STS2 MCP] ReplayRecorder: SaveManager.Instance is null");
            return null;
        }

        var profileId = saveManager.CurrentProfileId;
        var progressPath = GetProfileProgressPath(profileId);
        var profileRoot = GetProfileRootFromProgressPath(progressPath, profileId);
        var saveScope = GetSaveScope(profileRoot);
        var currentRunPath = TryResolveCurrentRunPathDirect(progressPath, profileId, profileRoot);

        if (string.IsNullOrWhiteSpace(currentRunPath) || !File.Exists(currentRunPath))
        {
            if (logMissing)
                GD.PrintErr($"[STS2 MCP] ReplayRecorder: current_run.save not found (progressPath={progressPath}, profileRoot={profileRoot})");
            return null;
        }

        try
        {
            using var stream = new FileStream(
                currentRunPath,
                FileMode.Open,
                System.IO.FileAccess.Read,
                FileShare.ReadWrite);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            string? seed = null;
            if (root.TryGetProperty("rng", out var rng)
                && rng.ValueKind == JsonValueKind.Object
                && rng.TryGetProperty("seed", out var seedElement))
            {
                seed = seedElement.GetString();
            }

            if (string.IsNullOrWhiteSpace(seed))
            {
                if (logMissing)
                    GD.PrintErr("[STS2 MCP] ReplayRecorder: seed missing from current_run.save");
                return null;
            }

            long? startTime = null;
            if (root.TryGetProperty("start_time", out var startElement)
                && startElement.TryGetInt64(out var parsedStartTime))
            {
                startTime = parsedStartTime;
            }

            var timestamp = FormatReplayStartTimestamp(startTime);
            var sanitizedSeed = SanitizeReplayFileNamePart(seed);
            var fileName = $"{sanitizedSeed}_{timestamp}.replay";
            var replayDirectory = GetReplayDirectory();
            var path = Path.Combine(replayDirectory, fileName);
            var tracePath = Path.ChangeExtension(path, ".trace");
            var characterId = TryGetCharacterId(root) ?? "";
            var gameMode = root.TryGetProperty("game_mode", out var gameModeElement)
                ? gameModeElement.GetString() ?? "standard"
                : "standard";
            var ascension = root.TryGetProperty("ascension", out var ascensionElement)
                            && ascensionElement.TryGetInt32(out var parsedAscension)
                ? parsedAscension
                : 0;
            var runKey = $"{saveScope}:profile{profileId}:{startTime?.ToString(CultureInfo.InvariantCulture) ?? timestamp}:{seed}";

            return new ReplayRunInfo(runKey, path, tracePath, currentRunPath, seed, gameMode, characterId, ascension);
        }
        catch (Exception ex)
        {
            if (logMissing)
                GD.PrintErr($"[STS2 MCP] ReplayRecorder: failed to read current_run.save: {ex.Message}");
            return null;
        }
    }

    private static string? TryGetCharacterId(JsonElement root)
    {
        if (!root.TryGetProperty("players", out var players)
            || players.ValueKind != JsonValueKind.Array
            || players.GetArrayLength() == 0
            || !players[0].TryGetProperty("character_id", out var characterId))
        {
            return null;
        }

        return characterId.GetString();
    }

    private static string? TryResolveCurrentRunPathDirect(string? progressPath, int profileId, string profileRoot)
    {
        var saveDirectory = GetSaveDirectoryFromProgressPath(progressPath);
        if (saveDirectory != null)
        {
            var path = Path.Combine(saveDirectory, "current_run.save");
            if (File.Exists(path))
                return path;
        }

        foreach (var saveRoot in EnumerateSaveRoots())
        {
            var path = Path.Combine(saveRoot, profileRoot, "saves", "current_run.save");
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static string GetReplayDirectory()
    {
        try
        {
            var userDataDir = Godot.OS.GetUserDataDir();
            if (!string.IsNullOrWhiteSpace(userDataDir))
                return Path.Combine(userDataDir, "STS2MCP", "replays");
        }
        catch { }

        var modDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        return Path.Combine(modDir ?? System.Environment.CurrentDirectory, "replays");
    }

    private static string FormatReplayStartTimestamp(long? startTime)
    {
        if (startTime.HasValue)
        {
            try
            {
                return DateTimeOffset
                    .FromUnixTimeSeconds(startTime.Value)
                    .ToLocalTime()
                    .ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            }
            catch { }
        }

        return DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    }

    private static string SanitizeReplayFileNamePart(string value)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            value = value.Replace(invalidChar, '_');
        return value.Trim();
    }

    private sealed record ReplayRunInfo(
        string RunKey,
        string Path,
        string TracePath,
        string CurrentRunPath,
        string Seed,
        string GameMode,
        string CharacterId,
        int Ascension);

    private sealed record ReplayCardSnapshot(string CardId, int UpgradeLevel);
}
