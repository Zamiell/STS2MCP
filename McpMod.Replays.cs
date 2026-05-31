using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace STS2_MCP;

public static partial class McpMod
{
    private static Type? GetRunReplayMenuType()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType("RunReplays.RunReplayMenu", throwOnError: false))
            .FirstOrDefault(type => type != null);
    }

    [McpAction("get_replays", "Legacy RunReplays", "List replay floors discoverable through the external RunReplays mod.")]
    [McpActionNote("Legacy/external interop: requires RunReplays to be installed and enabled. Prefer STS2MCP .replay recording for new replay data.")]
    private static Dictionary<string, object?> ExecuteGetReplays()
    {
        Type? replayMenuType = GetRunReplayMenuType();
        if (replayMenuType == null)
            return Error("RunReplays mod is not loaded. Install and enable RunReplays before calling get_replays.");

        MethodInfo? method = replayMenuType.GetMethod(
            "ListReplays",
            BindingFlags.Public | BindingFlags.Static);
        if (method == null)
            return Error("RunReplays does not expose ListReplays. Rebuild/install the updated RunReplays fork.");

        object? result;
        try
        {
            result = method.Invoke(null, []);
        }
        catch (TargetInvocationException ex)
        {
            string detail = ex.InnerException?.Message ?? ex.Message;
            return Error($"RunReplays failed to list replays: {detail}");
        }

        if (result is not System.Collections.IEnumerable entries)
            return Error("RunReplays returned an invalid replay list.");

        var replayEntries = new List<Dictionary<string, object?>>();
        foreach (object? entry in entries)
        {
            if (entry == null)
                continue;

            replayEntries.Add(new Dictionary<string, object?>
            {
                ["seed"] = ReadStringProperty(entry, "Seed"),
                ["character_id"] = ReadStringProperty(entry, "CharacterId"),
                ["floor"] = ReadNullableIntProperty(entry, "Floor"),
                ["ascension"] = ReadNullableIntProperty(entry, "Ascension"),
                ["saved_at"] = ReadDateTimeProperty(entry, "SavedAt")?.ToString("O"),
                ["minimal_log_path"] = ReadStringProperty(entry, "MinimalLogPath"),
                ["save_path"] = ReadStringProperty(entry, "SavePath"),
                ["is_sample"] = ReadBoolProperty(entry, "IsSample"),
                ["target"] = ReadStringProperty(entry, "Target")
            });
        }

        var grouped = replayEntries
            .GroupBy(entry => entry["seed"] as string ?? "")
            .Select(group => new Dictionary<string, object?>
            {
                ["seed"] = group.Key,
                ["character_id"] = group.FirstOrDefault()?["character_id"],
                ["floors"] = group
                    .OrderByDescending(entry => entry["floor"] as int? ?? 0)
                    .ToList()
            })
            .OrderBy(group => group["seed"] as string)
            .ToList();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["count"] = replayEntries.Count,
            ["seed_count"] = grouped.Count,
            ["replays"] = replayEntries,
            ["groups"] = grouped
        };
    }

    [McpAction("get_replay_status", "Legacy RunReplays", "Read the current external RunReplays playback status.")]
    [McpActionNote("Legacy/external interop: requires RunReplays to be installed and enabled.")]
    private static Dictionary<string, object?> ExecuteGetReplayStatus()
    {
        Type? replayEngineType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType("RunReplays.ReplayEngine", throwOnError: false))
            .FirstOrDefault(type => type != null);
        if (replayEngineType == null)
            return Error("RunReplays mod is not loaded.");

        MethodInfo? method = replayEngineType.GetMethod(
            "GetStatus",
            BindingFlags.Public | BindingFlags.Static);
        if (method == null)
            return Error("RunReplays does not expose GetStatus. Rebuild/install the updated RunReplays fork.");

        object? result = method.Invoke(null, []);
        if (result == null)
            return Error("RunReplays returned no replay status.");

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["is_active"] = ReadBoolProperty(result, "IsActive"),
            ["is_replay_run"] = ReadBoolProperty(result, "IsReplayRun"),
            ["active_seed"] = ReadStringProperty(result, "ActiveSeed"),
            ["loaded_count"] = ReadNullableIntProperty(result, "LoadedCount"),
            ["pending_count"] = ReadNullableIntProperty(result, "PendingCount"),
            ["consumed_count"] = ReadNullableIntProperty(result, "ConsumedCount"),
            ["current_command"] = ReadStringProperty(result, "CurrentCommand"),
            ["current_state_suffix"] = ReadStringProperty(result, "CurrentStateSuffix"),
            ["next_commands"] = ReadStringEnumerableProperty(result, "NextCommands"),
            ["next_state_suffixes"] = ReadStringEnumerableProperty(result, "NextStateSuffixes"),
            ["recent_consumed"] = ReadStringEnumerableProperty(result, "RecentConsumed")
        };
    }

    [McpAction("start_replay", "Legacy RunReplays", "Start playback through the external RunReplays mod.")]
    [McpActionField("target", "string", false, "Compact replay target, such as SEED or SEED:floor_N.")]
    [McpActionField("seed", "string", false, "Replay seed to launch when target is omitted.")]
    [McpActionField("floor", "int", false, "Optional floor number to replay to for seed.")]
    [McpActionField("start_floor", "int", false, "Optional saved floor to load before replaying to floor; requires seed and floor.")]
    [McpActionNote("Legacy/external interop: requires RunReplays to be installed and enabled. Prefer STS2MCP .replay recording for new replay data.")]
    private static Dictionary<string, object?> ExecuteStartReplay(
        Dictionary<string, JsonElement> data)
    {
        string? target = data.TryGetValue("target", out var targetElem)
            ? targetElem.GetString()
            : null;
        string? seed = data.TryGetValue("seed", out var seedElem)
            ? seedElem.GetString()
            : null;
        int? floor = TryReadNullableInt(data, "floor");
        int? startFloor = TryReadNullableInt(data, "start_floor");

        if (string.IsNullOrWhiteSpace(target) && string.IsNullOrWhiteSpace(seed))
            return Error("Missing 'seed' or 'target'. Use target='SEED[:floor_N]' or seed plus optional floor.");
        if (startFloor.HasValue && (string.IsNullOrWhiteSpace(seed) || !floor.HasValue))
            return Error("start_floor requires seed and floor.");

        Type? replayMenuType = GetRunReplayMenuType();
        if (replayMenuType == null)
            return Error("RunReplays mod is not loaded. Install and enable RunReplays before calling start_replay.");

        MethodInfo? method;
        object? result;
        try
        {
            if (!string.IsNullOrWhiteSpace(target))
            {
                method = replayMenuType.GetMethod(
                    "StartReplayTarget",
                    BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                    return Error("RunReplays does not expose StartReplayTarget. Rebuild/install the updated RunReplays fork.");
                result = method.Invoke(null, [target]);
            }
            else
            {
                if (startFloor.HasValue)
                {
                    method = replayMenuType.GetMethod(
                        "StartReplayFromFloorBySeed",
                        BindingFlags.Public | BindingFlags.Static);
                    if (method == null)
                        return Error("RunReplays does not expose StartReplayFromFloorBySeed. Rebuild/install the updated RunReplays fork.");
                    result = method.Invoke(null, [seed, floor!.Value, startFloor.Value]);
                }
                else
                {
                    method = replayMenuType.GetMethod(
                        "StartReplayBySeed",
                        BindingFlags.Public | BindingFlags.Static);
                    if (method == null)
                        return Error("RunReplays does not expose StartReplayBySeed. Rebuild/install the updated RunReplays fork.");
                    result = method.Invoke(null, [seed, floor]);
                }
            }
        }
        catch (TargetInvocationException ex)
        {
            string detail = ex.InnerException?.Message ?? ex.Message;
            return Error($"RunReplays failed to start replay: {detail}");
        }

        if (result == null)
            return Error("RunReplays returned no replay start result.");

        bool success = ReadBoolProperty(result, "Success");
        var response = new Dictionary<string, object?>
        {
            ["status"] = success ? "ok" : "error",
            [success ? "message" : "error"] = ReadStringProperty(result, "Message") ?? "Replay start failed.",
            ["seed"] = ReadStringProperty(result, "Seed"),
            ["floor"] = ReadNullableIntProperty(result, "Floor"),
            ["character_id"] = ReadStringProperty(result, "CharacterId"),
            ["ascension"] = ReadNullableIntProperty(result, "Ascension"),
            ["log_path"] = ReadStringProperty(result, "LogPath")
        };
        return response;
    }

    private static int? TryReadNullableInt(Dictionary<string, JsonElement> data, string fieldName)
    {
        if (!data.TryGetValue(fieldName, out var elem))
            return null;
        if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt32(out int value))
            return value;
        if (elem.ValueKind == JsonValueKind.String && int.TryParse(elem.GetString(), out value))
            return value;
        return null;
    }

    private static string? ReadStringProperty(object source, string propertyName)
    {
        object? value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        return value as string;
    }

    private static int? ReadNullableIntProperty(object source, string propertyName)
    {
        object? value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        return value is int intValue ? intValue : null;
    }

    private static bool ReadBoolProperty(object source, string propertyName)
    {
        object? value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        return value is bool boolValue && boolValue;
    }

    private static DateTime? ReadDateTimeProperty(object source, string propertyName)
    {
        object? value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        return value is DateTime dateTime ? dateTime : null;
    }

    private static List<string> ReadStringEnumerableProperty(object source, string propertyName)
    {
        object? value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        if (value is not System.Collections.IEnumerable enumerable)
            return [];
        return enumerable
            .Cast<object?>()
            .Select(item => item?.ToString())
            .Where(item => !string.IsNullOrEmpty(item))
            .Cast<string>()
            .ToList();
    }
}
