using System;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void EnableMainMenuStartup(NGame game)
    {
        try
        {
            game.StartOnMainMenu = true;
            GD.Print("[STS2 MCP] Skipping startup logo animation; starting on main menu");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to enable main-menu startup: {ex}");
        }
    }

    [HarmonyPatch(typeof(NGame), "GameStartup")]
    static class GameStartupPatch
    {
        static void Prefix(NGame __instance) => EnableMainMenuStartup(__instance);
    }

    [HarmonyPatch(typeof(NLogoAnimation), nameof(NLogoAnimation.PlayAnimation))]
    static class LogoAnimationPatch
    {
        static bool Prefix(ref Task __result)
        {
            __result = Task.CompletedTask;
            GD.Print("[STS2 MCP] Skipped logo animation task");
            return false;
        }
    }
}
