using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private const int MaxPendingReplayCommands = 64;
    private static readonly object _replayRecorderLock = new();
    private static readonly List<string> _pendingReplayCommands = [];
    private static string? _activeReplayRunKey;
    private static string? _activeReplayPath;

    private static readonly JsonSerializerOptions _replayJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static void RecordReplayCommandIfSuccessful(
        Dictionary<string, JsonElement> command,
        Dictionary<string, object?> result)
    {
        if (!ShouldRecordReplayCommand(command, result))
            return;

        try
        {
            var line = JsonSerializer.Serialize(command, _replayJsonOptions);
            var runInfo = TryGetReplayRunInfo();

            lock (_replayRecorderLock)
            {
                if (runInfo == null)
                {
                    BufferPendingReplayCommand(line);
                    return;
                }

                if (_activeReplayRunKey != runInfo.RunKey)
                {
                    _activeReplayRunKey = runInfo.RunKey;
                    _activeReplayPath = runInfo.Path;

                    Directory.CreateDirectory(Path.GetDirectoryName(runInfo.Path)!);
                    if (!File.Exists(runInfo.Path))
                        File.WriteAllText(runInfo.Path, "");

                    foreach (var pendingLine in _pendingReplayCommands)
                        File.AppendAllText(runInfo.Path, pendingLine + System.Environment.NewLine);
                    _pendingReplayCommands.Clear();
                }

                File.AppendAllText(runInfo.Path, line + System.Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to record replay command: {ex.Message}");
        }
    }

    private static bool ShouldRecordReplayCommand(
        Dictionary<string, JsonElement> command,
        Dictionary<string, object?> result)
    {
        if (!command.TryGetValue("action", out var actionElement))
            return false;

        var action = actionElement.GetString();
        if (string.IsNullOrWhiteSpace(action))
            return false;

        if (action is "get_replays" or "get_replay_status" or "start_replay")
            return false;

        if (result.ContainsKey("error"))
            return false;

        if (result.TryGetValue("status", out var status)
            && string.Equals(Convert.ToString(status, CultureInfo.InvariantCulture), "error", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static void BufferPendingReplayCommand(string line)
    {
        _pendingReplayCommands.Add(line);
        if (_pendingReplayCommands.Count > MaxPendingReplayCommands)
            _pendingReplayCommands.RemoveAt(0);
    }

    private static ReplayRunInfo? TryGetReplayRunInfo()
    {
        if (RunManager.Instance?.IsInProgress != true)
        {
            GD.Print("[STS2 MCP] ReplayRecorder: run not in progress, skipping");
            return null;
        }

        var saveManager = SaveManager.Instance;
        if (saveManager == null)
        {
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
            GD.PrintErr($"[STS2 MCP] ReplayRecorder: current_run.save not found (progressPath={progressPath}, profileRoot={profileRoot})");
            return null;
        }

        try
        {
            using var stream = new FileStream(currentRunPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
            var runKey = $"{saveScope}:profile{profileId}:{startTime?.ToString(CultureInfo.InvariantCulture) ?? timestamp}:{seed}";

            return new ReplayRunInfo(runKey, path);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] ReplayRecorder: failed to read current_run.save: {ex.Message}");
            return null;
        }
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

    private sealed record ReplayRunInfo(string RunKey, string Path);
}
