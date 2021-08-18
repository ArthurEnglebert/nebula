﻿using HarmonyLib;
using NebulaModel.Packets.Trash;
using NebulaWorld;
using NebulaWorld.Trash;

namespace NebulaPatcher.Patches.Dynamic
{
    [HarmonyPatch(typeof(TrashSystem))]
    class TrashSystem_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(TrashSystem.SetForNewGame))]
        public static void SetForNewGame_Postfix()
        {
            //Request trash data from the host
            if (SimulatedWorld.Instance.Initialized && !LocalPlayer.Instance.IsMasterClient)
            {
                LocalPlayer.Instance.SendPacket(new TrashSystemRequestDataPacket(GameMain.localStar == null ? -1 : GameMain.localStar.id));
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(TrashSystem.ClearAllTrash))]
        public static void ClearAllTrash_Postfix()
        {
            //Send notification, that somebody clicked on "ClearAllTrash"
            if (SimulatedWorld.Instance.Initialized && !TrashManager.ClearAllTrashFromOtherPlayers)
            {
                LocalPlayer.Instance.SendPacket(new TrashSystemClearAllTrashPacket());
            }
        }
    }
}
