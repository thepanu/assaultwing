﻿using System;
using AW2.Core;
using AW2.UI;
using AW2.Helpers;
using AW2.Net.ManagementMessages;
using AW2.Net.MessageHandling;
using AW2.Net.Messages;
using AW2.Net;

namespace AW2.Menu
{
    /// <summary>
    /// All possible item collections for <see cref="MainMenuComponent"/>.
    /// </summary>
    public class MainMenuItemCollections
    {
        /// <summary>
        /// The very first menu when the game starts.
        /// </summary>
        public MainMenuItemCollection StartItems { get; private set; }

        /// <summary>
        /// Menu for establishing a network game.
        /// </summary>
        public MainMenuItemCollection NetworkItems { get; private set; }

        public MainMenuItemCollections(MenuEngineImpl menuEngine)
        {
            StartItems = new MainMenuItemCollection("Start Menu");
            StartItems.Add(new MainMenuItem(menuEngine)
            {
                Name = "Play Local",
                Action = component => component.MenuEngine.ActivateComponent(MenuComponentType.Equip)
            });
            StartItems.Add(new MainMenuItem(menuEngine)
            {
                Name = "Play at the Battlefront",
                Action = component =>
                {
                    InitializeNetworkItems(menuEngine);
                    component.SetItems(NetworkItems);
                    AssaultWing.Instance.SoundEngine.PlaySound("MenuChangeItem");
                    AssaultWing.Instance.NetworkEngine.ConnectToManagementServer();
                    MessageHandlers.ActivateHandlers(MessageHandlers.GetStandaloneMenuHandlers(
                        mess => HandleGameServerListReply(mess, menuEngine),
                        mess => HandleJoinGameServerReply(mess, menuEngine)));
                    AssaultWing.Instance.NetworkEngine.ManagementServerConnection.Send(new GameServerListRequest());
                }
            });
            StartItems.Add(new MainMenuItem(menuEngine)
            {
                Name = "Setup",
                Action = component => Log.Write("NOTE: Main menu item 'Setup' is not implemented")
            });
            StartItems.Add(new MainMenuItem(menuEngine)
            {
                Name = "Quit",
                Action = component => AssaultWing.Instance.Exit()
            });
        }

        private void InitializeNetworkItems(MenuEngineImpl menuEngine)
        {
            NetworkItems = new MainMenuItemCollection("Battlefront Menu");
            NetworkItems.Add(new MainMenuItem(menuEngine)
            {
                Name = "Play as Server",
                Action = component =>
                {
                    if (AssaultWing.Instance.NetworkMode != NetworkMode.Standalone) return;
                    if (!AssaultWing.Instance.StartServer(MessageHandlers.IncomingConnectionHandlerOnServer)) return;
                    component.MenuEngine.ActivateComponent(MenuComponentType.Equip);

                    // HACK: Force one local player and Amazonas as the only arena.
                    AssaultWing.Instance.DataEngine.Spectators.Remove(player => AssaultWing.Instance.DataEngine.Spectators.Count > 1);
                    AssaultWing.Instance.DataEngine.ArenaPlaylist = new AW2.Helpers.Collections.Playlist(new string[] { "Amazonas" });
                }
            });
        }

        private void HandleGameServerListReply(GameServerListReply mess, MenuEngineImpl menuEngine)
        {
            foreach (var server in mess.GameServers)
                NetworkItems.Add(new MainMenuItem(menuEngine)
                {
                    Name = string.Format("Connect to {0} [{1}/{2}]", server.Name, server.CurrentPlayers, server.MaxPlayers),
                    Action = component =>
                    {
                        if (AssaultWing.Instance.NetworkMode != NetworkMode.Standalone) return;
                        var joinRequest = new JoinGameServerRequest { GameServerManagementID = server.ManagementID };
                        AssaultWing.Instance.NetworkEngine.ManagementServerConnection.Send(joinRequest);
                    }
                });
        }

        private static void HandleJoinGameServerReply(JoinGameServerReply mess, MenuEngineImpl menuEngine)
        {
            AssaultWing.Instance.SoundEngine.PlaySound("MenuChangeItem");
            AssaultWing.Instance.StartClient(mess.GameServerEndPoint, result => ClientConnectedCallback(result, menuEngine));
        }

        private static void ClientConnectedCallback(Result<AW2.Net.Connections.Connection> result, MenuEngineImpl menuEngine)
        {
            if (!result.Successful)
            {
                Log.Write("Failed to connect to server: " + result.Error);
                AssaultWing.Instance.StopClient();
                return;
            }
            Log.Write("Client connected to " + result.Value.RemoteTCPEndPoint);

            MessageHandlers.ActivateHandlers(MessageHandlers.GetClientMenuHandlers(() => menuEngine.ActivateComponent(MenuComponentType.Equip)));

            // HACK: Force one local player.
            AssaultWing.Instance.DataEngine.Spectators.Remove(player => AssaultWing.Instance.DataEngine.Spectators.Count > 1);

            var joinRequest = new JoinGameRequest { CanonicalStrings = CanonicalString.CanonicalForms };
            AssaultWing.Instance.NetworkEngine.GameServerConnection.Send(joinRequest);
        }
    }
}
