using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private static readonly object _replayPlaybackLock = new();
    private static ReplayPlaybackState? _activeReplayPlayback;
    private static bool _suppressReplayRecording;

    [McpAction("start_replay", "Replay", "Start playing a newline-delimited JSON replay file.")]
    [McpActionField("path", "string", false, "Absolute replay file path, or file name under the STS2MCP replay directory.")]
    [McpActionField("name", "string", false, "Replay file name under the STS2MCP replay directory.")]
    [McpActionField("force", "bool", false, "Cancel any currently running replay before starting this one.")]
    [McpActionField("return_to_main_menu", "bool", false, "Return to the main menu before playing the first replay command. Defaults to true.")]
    [McpActionField("command_timeout_seconds", "number", false, "Maximum time to wait for each replay command to become valid. Defaults to 30.")]
    private static Dictionary<string, object?> ExecuteStartReplay(Dictionary<string, JsonElement> data)
    {
        var force = data.TryGetValue("force", out var forceElement)
                    && forceElement.ValueKind == JsonValueKind.True;

        lock (_replayPlaybackLock)
        {
            if (_activeReplayPlayback is { IsDone: false } && !force)
                return Error("A replay is already running. Use force=true to replace it.");
        }

        var replayPath = ResolveReplayPath(data);
        if (replayPath.Error != null)
            return Error(replayPath.Error);

        var commands = ReadReplayCommands(replayPath.Path!);
        if (commands.Error != null)
            return Error(commands.Error);

        var returnToMainMenu = !data.TryGetValue("return_to_main_menu", out var returnElement)
                               || returnElement.ValueKind != JsonValueKind.False;
        var commandTimeout = data.TryGetValue("command_timeout_seconds", out var timeoutElement)
                             && timeoutElement.TryGetDouble(out var timeoutSeconds)
            ? TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))
            : TimeSpan.FromSeconds(30);

        lock (_replayPlaybackLock)
        {
            _activeReplayPlayback = new ReplayPlaybackState(
                replayPath.Path!,
                commands.Commands!,
                commandTimeout,
                returnToMainMenu);
            _suppressReplayRecording = true;
        }

        GD.Print($"[STS2 MCP] ReplayPlayback: started {replayPath.Path}");
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Started replay: {Path.GetFileName(replayPath.Path)}",
            ["path"] = replayPath.Path,
            ["command_count"] = commands.Commands!.Count
        };
    }

    [McpAction("get_replay_status", "Replay", "Get the current replay playback status.")]
    private static Dictionary<string, object?> ExecuteGetReplayStatus()
    {
        lock (_replayPlaybackLock)
            return BuildReplayStatus(_activeReplayPlayback);
    }

    [McpAction("cancel_replay", "Replay", "Cancel the active replay playback.")]
    private static Dictionary<string, object?> ExecuteCancelReplay()
    {
        lock (_replayPlaybackLock)
        {
            if (_activeReplayPlayback is not { IsDone: false })
            {
                _suppressReplayRecording = false;
                return new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["message"] = "No replay is running"
                };
            }

            _activeReplayPlayback.Cancel("Canceled by API request");
            _suppressReplayRecording = false;
            return BuildReplayStatus(_activeReplayPlayback);
        }
    }

    private static Dictionary<string, object?> ExecuteReplayControlAction(
        string action,
        Dictionary<string, JsonElement> data)
    {
        return action switch
        {
            "start_replay" => ExecuteStartReplay(data),
            "get_replay_status" => ExecuteGetReplayStatus(),
            "cancel_replay" => ExecuteCancelReplay(),
            _ => Error($"Unknown replay action: {action}")
        };
    }

    private static void ProcessReplayPlayback()
    {
        ReplayPlaybackState? playback;
        lock (_replayPlaybackLock)
        {
            playback = _activeReplayPlayback;
            if (playback == null || playback.IsDone)
            {
                if (playback?.IsDone == true)
                {
                    if (RunManager.Instance.IsInProgress)
                        return;

                    _suppressReplayRecording = false;
                    _activeReplayPlayback = null;
                }
                return;
            }
        }

        try
        {
            if (playback.ReturnToMainMenuPending)
            {
                if (ShouldReturnToMainMenuBeforeReplay())
                    NGame.Instance?.ReturnToMainMenu();
                playback.ReturnToMainMenuPending = false;
                playback.ResetCommandTimer();
                return;
            }

            if (!PrepareReplayStart(playback))
                return;

            if (playback.AwaitingEventProceedAdvance)
            {
                ProcessPendingEventProceed(playback);
                return;
            }

            if (playback.NextIndex >= playback.Commands.Count)
            {
                playback.Complete();
                GD.Print($"[STS2 MCP] ReplayPlayback: completed {playback.Path}");
                return;
            }

            var command = playback.Commands[playback.NextIndex];
            var result = ExecuteReplayCommand(command);
            if (IsReplayCommandSuccess(result))
            {
                if (ReplayCommandIsProceed(command) && TryGetCurrentEventProceedId(out var eventId))
                {
                    playback.BeginAwaitingEventProceed(result, eventId);
                    return;
                }

                playback.Advance(result);
                return;
            }

            playback.LastResult = result;
            playback.LastError = ExtractReplayError(result);
            if (DateTimeOffset.UtcNow - playback.CommandStartedAt >= playback.CommandTimeout)
            {
                playback.Fail($"Timed out waiting for command {playback.NextIndex}: {playback.LastError}");
                GD.PrintErr($"[STS2 MCP] ReplayPlayback: {playback.Error}");
            }
        }
        catch (Exception ex)
        {
            playback.Fail(ex.Message);
            GD.PrintErr($"[STS2 MCP] ReplayPlayback failed: {ex}");
        }
    }

    private static void ProcessPendingEventProceed(ReplayPlaybackState playback)
    {
        if (!TryGetCurrentEventProceedId(out var eventId)
            || !string.Equals(eventId, playback.AwaitingEventProceedEventId, StringComparison.Ordinal))
        {
            playback.CompleteAwaitingEventProceed();
            return;
        }

        if (DateTimeOffset.UtcNow - playback.AwaitingEventProceedLastClickAt >= TimeSpan.FromMilliseconds(500))
        {
            var command = playback.Commands[playback.NextIndex];
            var result = ExecuteReplayCommand(command);
            playback.LastResult = result;
            if (IsReplayCommandSuccess(result))
            {
                playback.AwaitingEventProceedResult = result;
                playback.AwaitingEventProceedLastClickAt = DateTimeOffset.UtcNow;
                return;
            }

            playback.LastError = ExtractReplayError(result);
        }

        if (DateTimeOffset.UtcNow - playback.CommandStartedAt >= playback.CommandTimeout)
        {
            playback.Fail($"Timed out waiting for event proceed to advance: {playback.LastError}");
            GD.PrintErr($"[STS2 MCP] ReplayPlayback: {playback.Error}");
        }
    }

    private static Dictionary<string, object?> ExecuteReplayCommand(Dictionary<string, JsonElement> command)
    {
        if (!command.TryGetValue("action", out var actionElement))
            return Error("Replay command is missing 'action'");

        var action = actionElement.GetString() ?? "";
        if (action == "start_replay")
            return Error("Replay files cannot start nested replays");

        if (action == "menu_select")
        {
            var option = command.TryGetValue("option", out var optionElement)
                ? optionElement.GetString() ?? ""
                : "";
            var seed = command.TryGetValue("seed", out var seedElement)
                ? seedElement.GetString()
                : null;
            var ascension = command.TryGetValue("ascension", out var ascensionElement)
                            && ascensionElement.TryGetInt32(out var ascensionValue)
                ? ascensionValue
                : (int?)null;

            return ExecuteMenuSelect(option, seed, ascension);
        }

        return ExecuteAction(action, command);
    }

    private static bool ReplayCommandIsProceed(Dictionary<string, JsonElement> command)
    {
        return command.TryGetValue("action", out var actionElement)
               && string.Equals(actionElement.GetString(), "proceed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetCurrentEventProceedId(out string eventId)
    {
        eventId = "";

        try
        {
            var state = BuildGameState();
            if (!state.TryGetValue("state_type", out var stateType)
                || !string.Equals(Convert.ToString(stateType), "event", StringComparison.OrdinalIgnoreCase)
                || !state.TryGetValue("event", out var eventValue)
                || eventValue is not Dictionary<string, object?> eventState)
            {
                return false;
            }

            if (!EventStateHasProceedOption(eventState))
                return false;

            eventId = eventState.TryGetValue("event_id", out var id)
                ? Convert.ToString(id) ?? ""
                : "";
            return eventId.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool EventStateHasProceedOption(Dictionary<string, object?> eventState)
    {
        if (!eventState.TryGetValue("options", out var options) || options is not System.Collections.IEnumerable enumerable)
            return false;

        foreach (var option in enumerable)
        {
            if (option is not Dictionary<string, object?> optionState)
                continue;

            if (optionState.TryGetValue("is_proceed", out var isProceed) && isProceed is true)
                return true;
        }

        return false;
    }

    private static bool PrepareReplayStart(ReplayPlaybackState playback)
    {
        if (!ReplayStartsWithSingleplayer(playback))
            return true;

        var state = BuildGameState();
        if (IsMainMenuState(state) && StateHasOption(state, "singleplayer"))
        {
            playback.StartupAbandonConfirmPending = false;
            return true;
        }

        if (playback.StartupAbandonConfirmPending)
        {
            var confirmResult = ConfirmReplayStartupAbandon();
            playback.LastResult = confirmResult;
            if (IsReplayCommandSuccess(confirmResult))
            {
                playback.LastError = null;
                playback.StartupAbandonConfirmPending = false;
                playback.ResetCommandTimer();
                return false;
            }

            playback.LastError = ExtractReplayError(confirmResult);
            return ContinueWaitingForReplayStartup(playback);
        }

        if (IsMainMenuState(state) && StateHasOption(state, "abandon_run"))
        {
            var abandonResult = ExecuteMenuSelect("abandon_run");
            playback.LastResult = abandonResult;
            if (IsReplayCommandSuccess(abandonResult))
            {
                playback.LastError = null;
                playback.StartupAbandonConfirmPending = true;
                playback.ResetCommandTimer();
                return false;
            }

            playback.LastError = ExtractReplayError(abandonResult);
            return ContinueWaitingForReplayStartup(playback);
        }

        return true;
    }

    private static Dictionary<string, object?> ConfirmReplayStartupAbandon()
    {
        var confirmResult = ExecuteMenuSelect("confirm");
        if (IsReplayCommandSuccess(confirmResult))
            return confirmResult;

        var yesResult = ExecuteMenuSelect("yes");
        if (IsReplayCommandSuccess(yesResult))
            return yesResult;

        return confirmResult;
    }

    private static bool ReplayStartsWithSingleplayer(ReplayPlaybackState playback)
    {
        if (playback.NextIndex != 0 || playback.Commands.Count == 0)
            return false;

        var command = playback.Commands[0];
        var action = command.TryGetValue("action", out var actionElement)
            ? actionElement.GetString()
            : null;
        var option = command.TryGetValue("option", out var optionElement)
            ? optionElement.GetString()
            : null;

        return string.Equals(action, "menu_select", StringComparison.OrdinalIgnoreCase)
               && string.Equals(option, "singleplayer", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContinueWaitingForReplayStartup(ReplayPlaybackState playback)
    {
        if (DateTimeOffset.UtcNow - playback.CommandStartedAt < playback.CommandTimeout)
            return false;

        playback.Fail($"Timed out preparing replay start: {playback.LastError}");
        GD.PrintErr($"[STS2 MCP] ReplayPlayback: {playback.Error}");
        return false;
    }

    private static bool ShouldReturnToMainMenuBeforeReplay()
    {
        try
        {
            var state = BuildGameState();
            var stateType = state.TryGetValue("state_type", out var stateTypeValue)
                ? Convert.ToString(stateTypeValue)
                : null;
            var menuScreen = state.TryGetValue("menu_screen", out var menuScreenValue)
                ? Convert.ToString(menuScreenValue)
                : null;

            return !string.Equals(stateType, "menu", StringComparison.OrdinalIgnoreCase)
                   || !string.Equals(menuScreen, "main", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] ReplayPlayback: failed to inspect pre-replay state: {ex}");
            return true;
        }
    }

    private static bool IsMainMenuState(Dictionary<string, object?> state)
    {
        var stateType = state.TryGetValue("state_type", out var stateTypeValue)
            ? Convert.ToString(stateTypeValue)
            : null;
        var menuScreen = state.TryGetValue("menu_screen", out var menuScreenValue)
            ? Convert.ToString(menuScreenValue)
            : null;

        return string.Equals(stateType, "menu", StringComparison.OrdinalIgnoreCase)
               && string.Equals(menuScreen, "main", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StateHasOption(Dictionary<string, object?> state, string option)
    {
        if (!state.TryGetValue("options", out var options) || options == null)
            return false;

        if (options is IEnumerable<string> stringOptions)
            return stringOptions.Any(candidate => string.Equals(candidate, option, StringComparison.OrdinalIgnoreCase));

        if (options is IEnumerable<Dictionary<string, object?>> objectOptions)
        {
            return objectOptions.Any(candidate =>
                candidate.TryGetValue("name", out var name)
                && string.Equals(Convert.ToString(name), option, StringComparison.OrdinalIgnoreCase));
        }

        if (options is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (string.Equals(Convert.ToString(item), option, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static bool IsReplayCommandSuccess(Dictionary<string, object?> result)
    {
        if (result.ContainsKey("error"))
            return false;

        if (result.TryGetValue("status", out var status)
            && string.Equals(Convert.ToString(status), "error", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string ExtractReplayError(Dictionary<string, object?> result)
    {
        if (result.TryGetValue("error", out var error) && error != null)
            return Convert.ToString(error) ?? "unknown error";

        if (result.TryGetValue("message", out var message) && message != null)
            return Convert.ToString(message) ?? "unknown error";

        return JsonSerializer.Serialize(result, _jsonOptions);
    }

    private static (string? Path, string? Error) ResolveReplayPath(Dictionary<string, JsonElement> data)
    {
        string? requestedPath = null;
        if (data.TryGetValue("path", out var pathElement))
            requestedPath = pathElement.GetString();
        else if (data.TryGetValue("name", out var nameElement))
            requestedPath = nameElement.GetString();

        if (string.IsNullOrWhiteSpace(requestedPath))
            return (null, "Missing 'path' or 'name'");

        requestedPath = requestedPath.Trim();
        var fullPath = Path.IsPathRooted(requestedPath)
            ? requestedPath
            : Path.Combine(GetReplayDirectory(), requestedPath);

        fullPath = Path.GetFullPath(fullPath);
        if (!File.Exists(fullPath))
            return (null, $"Replay file not found: {fullPath}");

        return (fullPath, null);
    }

    private static (List<Dictionary<string, JsonElement>>? Commands, string? Error) ReadReplayCommands(
        string path)
    {
        var commands = new List<Dictionary<string, JsonElement>>();
        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return (null, $"Replay line {lineNumber} must be a JSON object");

                var command = new Dictionary<string, JsonElement>();
                foreach (var property in document.RootElement.EnumerateObject())
                    command[property.Name] = property.Value.Clone();

                if (!command.ContainsKey("action"))
                    return (null, $"Replay line {lineNumber} is missing 'action'");

                commands.Add(command);
            }
            catch (JsonException ex)
            {
                return (null, $"Replay line {lineNumber} is invalid JSON: {ex.Message}");
            }
        }

        if (commands.Count == 0)
            return (null, "Replay file contains no commands");

        return (commands, null);
    }

    private static Dictionary<string, object?> BuildReplayStatus(ReplayPlaybackState? playback)
    {
        if (playback == null)
            return new Dictionary<string, object?>
            {
                ["status"] = "idle",
                ["running"] = false
            };

        return new Dictionary<string, object?>
        {
            ["status"] = playback.Status,
            ["running"] = !playback.IsDone,
            ["path"] = playback.Path,
            ["next_index"] = playback.NextIndex,
            ["command_count"] = playback.Commands.Count,
            ["last_error"] = playback.LastError,
            ["error"] = playback.Error,
            ["last_result"] = playback.LastResult
        };
    }

    private sealed class ReplayPlaybackState
    {
        public ReplayPlaybackState(
            string path,
            List<Dictionary<string, JsonElement>> commands,
            TimeSpan commandTimeout,
            bool returnToMainMenu)
        {
            Path = path;
            Commands = commands;
            CommandTimeout = commandTimeout;
            ReturnToMainMenuPending = returnToMainMenu;
            StartedAt = DateTimeOffset.UtcNow;
            CommandStartedAt = StartedAt;
        }

        public string Path { get; }
        public List<Dictionary<string, JsonElement>> Commands { get; }
        public TimeSpan CommandTimeout { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset CommandStartedAt { get; private set; }
        public int NextIndex { get; private set; }
        public string Status { get; private set; } = "running";
        public string? LastError { get; set; }
        public string? Error { get; private set; }
        public Dictionary<string, object?>? LastResult { get; set; }
        public bool ReturnToMainMenuPending { get; set; }
        public bool StartupAbandonConfirmPending { get; set; }
        public bool AwaitingEventProceedAdvance { get; private set; }
        public string? AwaitingEventProceedEventId { get; private set; }
        public DateTimeOffset AwaitingEventProceedLastClickAt { get; set; }
        public Dictionary<string, object?>? AwaitingEventProceedResult { get; set; }
        public bool IsDone => Status is "completed" or "failed" or "canceled";

        public void Advance(Dictionary<string, object?> result)
        {
            LastResult = result;
            LastError = null;
            NextIndex++;
            ResetCommandTimer();
        }

        public void ResetCommandTimer()
        {
            CommandStartedAt = DateTimeOffset.UtcNow;
        }

        public void BeginAwaitingEventProceed(Dictionary<string, object?> result, string eventId)
        {
            AwaitingEventProceedAdvance = true;
            AwaitingEventProceedEventId = eventId;
            AwaitingEventProceedResult = result;
            AwaitingEventProceedLastClickAt = DateTimeOffset.UtcNow;
            LastResult = result;
            LastError = null;
        }

        public void CompleteAwaitingEventProceed()
        {
            var result = AwaitingEventProceedResult
                         ?? new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Event proceed advanced" };
            AwaitingEventProceedAdvance = false;
            AwaitingEventProceedEventId = null;
            AwaitingEventProceedResult = null;
            Advance(result);
        }

        public void Complete()
        {
            Status = "completed";
        }

        public void Fail(string error)
        {
            Status = "failed";
            Error = error;
        }

        public void Cancel(string error)
        {
            Status = "canceled";
            Error = error;
        }
    }
}
