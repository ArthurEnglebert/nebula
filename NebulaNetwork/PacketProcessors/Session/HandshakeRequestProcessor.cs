﻿using NebulaAPI;
using NebulaModel.DataStructures;
using NebulaModel.Logger;
using NebulaModel.Networking;
using NebulaModel.Packets;
using NebulaModel.Packets.Players;
using NebulaModel.Packets.Session;
using NebulaModel.Utils;
using NebulaWorld;
using System.Collections.Generic;

namespace NebulaNetwork.PacketProcessors.Session
{
    [RegisterPacketProcessor]
    public class HandshakeRequestProcessor : PacketProcessor<HandshakeRequest>
    {
        private IPlayerManager playerManager;

        public HandshakeRequestProcessor()
        {
            playerManager = Multiplayer.Session.Network.PlayerManager;
        }

        public override void ProcessPacket(HandshakeRequest packet, NebulaConnection conn)
        {
            if (IsClient) return;

            INebulaPlayer player;
            using (playerManager.GetPendingPlayers(out var pendingPlayers))
            {
                if (!pendingPlayers.TryGetValue(conn, out player))
                {
                    conn.Disconnect(DisconnectionReason.InvalidData);
                    Log.Warn("WARNING: Player tried to handshake without being in the pending list");
                    return;
                }

                pendingPlayers.Remove(conn);
            }

            Dictionary<string, string> clientMods = new Dictionary<string, string>();

            using (BinaryUtils.Reader reader = new BinaryUtils.Reader(packet.ModsVersion))
            {
                for (int i = 0; i < packet.ModsCount; i++)
                {
                    string guid = reader.BinaryReader.ReadString();
                    string version = reader.BinaryReader.ReadString();

                    if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(guid))
                    {
                        conn.Disconnect(DisconnectionReason.ModIsMissingOnServer, guid);
                    }

                    clientMods.Add(guid, version);
                }
            }

            foreach (var pluginInfo in BepInEx.Bootstrap.Chainloader.PluginInfos)
            {
                if (pluginInfo.Value.Instance is IMultiplayerMod mod)
                {
                    if (!clientMods.ContainsKey(pluginInfo.Key))
                    {
                        conn.Disconnect(DisconnectionReason.ModIsMissing, pluginInfo.Key);
                        return;
                    }

                    string version = clientMods[pluginInfo.Key];

                    if (mod.CheckVersion(mod.Version, version)) continue;

                    conn.Disconnect(DisconnectionReason.ModVersionMismatch, $"{pluginInfo.Key};{version};{mod.Version}");
                    return;
                }
            }

            if (packet.GameVersionSig != GameConfig.gameVersion.sig)
            {
                conn.Disconnect(DisconnectionReason.GameVersionMismatch, $"{ packet.GameVersionSig };{ GameConfig.gameVersion.sig }");
                return;
            }

            Multiplayer.Session.World.OnPlayerJoining();

            bool isNewUser = false;

            //TODO: some validation of client cert / generating auth challenge for the client
            // Load old data of the client
            string clientCertHash = CryptoUtils.Hash(packet.ClientCert);
            using (playerManager.GetSavedPlayerData(out var savedPlayerData))
            {
                if (savedPlayerData.TryGetValue(clientCertHash, out var value))
                {
                    player.LoadUserData(value);
                }
                else
                {
                    savedPlayerData.Add(clientCertHash, player.Data);
                    isNewUser = true;
                }
            }

            // Add the username to the player data
            player.Data.Username = !string.IsNullOrWhiteSpace(packet.Username) ? packet.Username : $"Player {player.Id}";

            // Add the Mecha Color to the player data
            player.Data.MechaColor = packet.MechaColor;

            // Make sure that each player that is currently in the game receives that a new player as join so they can create its RemotePlayerCharacter
            PlayerJoining pdata = new PlayerJoining((PlayerData)player.Data.CreateCopyWithoutMechaData()); // Remove inventory from mecha data
            using (playerManager.GetConnectedPlayers(out var connectedPlayers))
            {
                foreach (var kvp in connectedPlayers)
                {
                    kvp.Value.SendPacket(pdata);
                }
            }

            // Add the new player to the list
            using (playerManager.GetSyncingPlayers(out var syncingPlayers))
            {
                syncingPlayers.Add(conn, player);
            }

            //Add current tech bonuses to the connecting player based on the Host's mecha
            ((MechaData)player.Data.Mecha).TechBonuses = new PlayerTechBonuses(GameMain.mainPlayer.mecha);

            using (BinaryUtils.Writer p = new BinaryUtils.Writer())
            {
                int count = 0;
                foreach (var pluginInfo in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    if (pluginInfo.Value.Instance is IMultiplayerModWithSettings mod)
                    {
                        p.BinaryWriter.Write(pluginInfo.Key);
                        mod.Export(p.BinaryWriter);
                        count++;
                    }
                }

                var gameDesc = GameMain.data.gameDesc;
                player.SendPacket(new HandshakeResponse(gameDesc.galaxyAlgo, gameDesc.galaxySeed, gameDesc.starCount, gameDesc.resourceMultiplier, isNewUser, (PlayerData)player.Data, p.CloseAndGetBytes(), count));
            }
        }
    }
}