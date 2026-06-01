using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace STS2_MCP;

public static partial class McpMod
{
    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.OnMapPointSelectedLocally))]
    private static class NMapScreenOnMapPointSelectedLocallyReplayPatch
    {
        private static void Prefix(NMapScreen __instance, NMapPoint point)
        {
            MaybeEnsureReplayFileForCurrentRun();
        }
    }
}
