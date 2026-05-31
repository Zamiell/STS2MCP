using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace STS2_MCP;

public static partial class McpMod
{
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

        if (string.IsNullOrWhiteSpace(target) && string.IsNullOrWhiteSpace(seed))
            return Error("Missing 'seed' or 'target'. Use target='SEED[:floor_N]' or seed plus optional floor.");

        Type? replayMenuType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType("RunReplays.RunReplayMenu", throwOnError: false))
            .FirstOrDefault(type => type != null);

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
                method = replayMenuType.GetMethod(
                    "StartReplayBySeed",
                    BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                    return Error("RunReplays does not expose StartReplayBySeed. Rebuild/install the updated RunReplays fork.");
                result = method.Invoke(null, [seed, floor]);
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
}
