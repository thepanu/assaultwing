﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using AW2.Core;
using AW2.Game;
using AW2.Helpers;
using AW2.Helpers.Serialization;
using AW2.Net.ManagementMessages;
using AW2.Net.Messages;

namespace AW2.Net.MessageHandling
{
    public static class MessageHandlers
    {
        public static void ActivateHandlers(IEnumerable<IMessageHandler> handlers)
        {
            AssaultWingCore.Instance.NetworkEngine.MessageHandlers.AddRange(handlers);
        }

        public static void DeactivateHandlers(IEnumerable<IMessageHandler> handlers)
        {
            var net = AssaultWingCore.Instance.NetworkEngine;
            var handlerTypesToRemove = handlers.Select(handler => handler.GetType());
            foreach (var handler in net.MessageHandlers)
                if (handlerTypesToRemove.Contains(handler.GetType())) handler.Dispose();
        }

        public static IEnumerable<IMessageHandler> GetStandaloneMenuHandlers(Action<GameServerListReply> handleGameServerListReply, Action<JoinGameServerReply> handleJoinGameServerReply)
        {
            yield return new MessageHandler<GameServerListReply>(false, IMessageHandler.SourceType.Management, handleGameServerListReply);
            yield return new MessageHandler<JoinGameServerReply>(false, IMessageHandler.SourceType.Management, handleJoinGameServerReply);

            // These messages only game servers receive
            yield return new MessageHandler<ClientJoinMessage>(false, IMessageHandler.SourceType.Management, HandleClientJoinMessage);
            yield return new MessageHandler<PingMessage>(false, IMessageHandler.SourceType.Management, HandlePingMessage);
        }

        public static IEnumerable<IMessageHandler> GetClientMenuHandlers(Action joinGameReplyAction, Action<StartGameMessage> handleStartGameMessage, Action<ConnectionClosingMessage> handleConnectionClosingMessage)
        {
            yield return new MessageHandler<ConnectionClosingMessage>(true, IMessageHandler.SourceType.Server, handleConnectionClosingMessage);
            yield return new MessageHandler<StartGameMessage>(false, IMessageHandler.SourceType.Server, handleStartGameMessage);
            yield return new MessageHandler<PlayerSettingsReply>(false, IMessageHandler.SourceType.Server, HandlePlayerSettingsReply);
            yield return new MessageHandler<PlayerSettingsRequest>(false, IMessageHandler.SourceType.Server, HandlePlayerSettingsRequestOnClient);
            yield return new MessageHandler<PlayerDeletionMessage>(false, IMessageHandler.SourceType.Server, HandlePlayerDeletionMessage);
            yield return new MessageHandler<GameSettingsRequest>(false, IMessageHandler.SourceType.Server, HandleGameSettingsRequest);
            yield return new MessageHandler<JoinGameReply>(true, IMessageHandler.SourceType.Server, mess => joinGameReplyAction());
        }

        public static IEnumerable<IMessageHandler> GetClientGameplayHandlers(Action<ConnectionClosingMessage> handleConnectionClosingMessage, GameplayMessageHandler<GobCreationMessage>.GameplayMessageAction handleGobCreationMessage)
        {
            yield return new MessageHandler<ArenaStartRequest>(false, IMessageHandler.SourceType.Server, m => HandleArenaStartRequest(m, handleGobCreationMessage));
            yield return new MessageHandler<ArenaFinishMessage>(false, IMessageHandler.SourceType.Server, HandleArenaFinishMessage);
            yield return new MessageHandler<PlayerMessageMessage>(false, IMessageHandler.SourceType.Server, HandlePlayerMessageMessageOnClient);
            yield return new MessageHandler<PlayerUpdateMessage>(false, IMessageHandler.SourceType.Server, HandlePlayerUpdateMessage);
            yield return new MessageHandler<PlayerDeletionMessage>(false, IMessageHandler.SourceType.Server, HandlePlayerDeletionMessage);
            yield return new GameplayMessageHandler<GobCreationMessage>(false, IMessageHandler.SourceType.Server, handleGobCreationMessage);
        }

        public static IEnumerable<IMessageHandler> GetClientArenaActionHandlers(GameplayMessageHandler<GobCreationMessage>.GameplayMessageAction handleGobCreationMessage)
        {
            yield return new GameplayMessageHandler<GobCreationMessage>(false, IMessageHandler.SourceType.Server, handleGobCreationMessage);
        }

        public static IEnumerable<IMessageHandler> GetServerMenuHandlers()
        {
            yield return new MessageHandler<JoinGameRequest>(false, IMessageHandler.SourceType.Client, HandleJoinGameRequest);
            yield return new MessageHandler<PlayerSettingsRequest>(false, IMessageHandler.SourceType.Client, HandlePlayerSettingsRequestOnServer);
        }

        public static IEnumerable<IMessageHandler> GetServerGameplayHandlers()
        {
            yield return new MessageHandler<PlayerControlsMessage>(false, IMessageHandler.SourceType.Client, AW2.UI.UIEngineImpl.HandlePlayerControlsMessage);
            yield return new MessageHandler<PlayerMessageMessage>(false, IMessageHandler.SourceType.Client, HandlePlayerMessageMessageOnServer);
        }

        public static void IncomingConnectionHandlerOnServer(Result<AW2.Net.Connections.Connection> result, Func<bool> allowNewConnection)
        {
            if (!result.Successful)
                Log.Write("Some client failed to connect: " + result.Error);
            else
            {
                Log.Write("Server obtained connection from " + result.Value.RemoteTCPEndPoint);
                if (!allowNewConnection())
                {
                    var mess = new ConnectionClosingMessage { Info = "Game server doesn't allow joining right now" };
                    result.Value.Send(mess);
                    AssaultWingCore.Instance.NetworkEngine.DropClient(result.Value.ID, false);
                }
            }
        }

        #region Handler implementations

        private static void HandleClientJoinMessage(ClientJoinMessage mess)
        {
            AssaultWingCore.Instance.NetworkEngine.ClientUDPEndPointPool.Add(mess.ClientUDPEndPoints);
        }

        private static void HandlePingMessage(PingMessage mess)
        {
            var pong = new PongMessage();
            AssaultWingCore.Instance.NetworkEngine.ManagementServerConnection.Send(pong);
        }

        private static void HandlePlayerSettingsRequestOnClient(PlayerSettingsRequest mess)
        {
            var spectator = AssaultWingCore.Instance.DataEngine.Spectators.FirstOrDefault(
                spec => spec.ID == mess.PlayerID && spec.ServerRegistration != Spectator.ServerRegistrationType.No);
            if (spectator == null)
            {
                var newPlayer = CreateAndAddNewPlayer(mess);
                newPlayer.ID = mess.PlayerID;
                newPlayer.ServerRegistration = Spectator.ServerRegistrationType.Yes;
            }
            else if (spectator.IsRemote)
            {
                mess.Read(spectator, SerializationModeFlags.ConstantData, 0);
            }
            else
            {
                // Be careful not to overwrite our most recent name and equipment choices
                // with something older from the server.
                var tempPlayer = GetTempPlayer();
                mess.Read(tempPlayer, SerializationModeFlags.ConstantData, 0);
                if (spectator is Player) ((Player)spectator).PlayerColor = tempPlayer.PlayerColor;
            }
        }

        private static void HandlePlayerSettingsReply(PlayerSettingsReply mess)
        {
            var player = AssaultWingCore.Instance.DataEngine.Spectators.FirstOrDefault(plr => ClientPlayerCriteria(plr, mess.OldPlayerID));
            if (player == null) throw new ApplicationException("Cannot find unregistered local player with ID " + mess.OldPlayerID);
            player.ServerRegistration = Spectator.ServerRegistrationType.Yes;
            player.ID = mess.NewPlayerID;
        }

        private static void HandlePlayerDeletionMessage(PlayerDeletionMessage mess)
        {
            AssaultWingCore.Instance.DataEngine.Spectators.Remove(spec => spec.ID == mess.PlayerID);
        }

        private static void HandleGameSettingsRequest(GameSettingsRequest mess)
        {
            ((AssaultWing)AssaultWing.Instance).SelectedArenaName = mess.ArenaToPlay;
        }

        private static void HandleArenaStartRequest(ArenaStartRequest mess, GameplayMessageHandler<GobCreationMessage>.GameplayMessageAction handleGobCreationMessage)
        {
            MessageHandlers.ActivateHandlers(MessageHandlers.GetClientArenaActionHandlers(handleGobCreationMessage));
            AssaultWingCore.Instance.StartArena(mess.StartDelay);
        }

        private static void HandleArenaFinishMessage(ArenaFinishMessage mess)
        {
            AssaultWingCore.Instance.FinishArena();
        }

        private static void HandlePlayerMessageMessageOnServer(PlayerMessageMessage mess)
        {
            if (mess.AllPlayers)
            {
                var otherPlayers = AssaultWingCore.Instance.DataEngine.Players
                    .Where(plr => plr.ConnectionID != mess.ConnectionID);
                foreach (var player in otherPlayers)
                    player.SendMessage(mess.Text, Player.PLAYER_MESSAGE_COLOR);
            }
            else
            {
                var player = AssaultWingCore.Instance.DataEngine.Players.First(plr => plr.ID == mess.PlayerID);
                if (player.IsRemote)
                    AssaultWingCore.Instance.NetworkEngine.GetGameClientConnection(player.ConnectionID).Send(mess);
                else
                    HandlePlayerMessageMessageOnClient(mess);
            }
        }

        private static void HandlePlayerMessageMessageOnClient(PlayerMessageMessage mess)
        {
            if (mess.AllPlayers) throw new NotImplementedException("Client cannot broadcast player text messages");
            AssaultWingCore.Instance.DataEngine.Players.First(plr => plr.ID == mess.PlayerID).SendMessage(mess.Text, mess.Color);
        }

        private static void HandlePlayerUpdateMessage(PlayerUpdateMessage mess)
        {
            var framesAgo = AssaultWingCore.Instance.NetworkEngine.GetMessageAge(mess);
            var player = AssaultWingCore.Instance.DataEngine.Spectators.FirstOrDefault(plr => plr.ID == mess.PlayerID);
            if (player == null) throw new NetworkException("Update for unknown player ID " + mess.PlayerID);
            mess.Read(player, SerializationModeFlags.VaryingData, framesAgo);
        }

        private static void HandleJoinGameRequest(JoinGameRequest mess)
        {
            string clientDiff, serverDiff;
            bool differ = MiscHelper.FirstDifference(mess.CanonicalStrings, CanonicalString.CanonicalForms, out clientDiff, out serverDiff);
            if (differ)
            {
                string mismatchInfo = string.Format("First mismatch is client: {0}, server: {1}",
                    clientDiff ?? "<missing>", serverDiff ?? "<missing>");
                Log.Write("Client's CanonicalStrings don't match ours. " + mismatchInfo);
                var reply = new ConnectionClosingMessage
                {
                    Info = "Cannot join server due to mismatching canonical strings!\n" + mismatchInfo
                };
                AssaultWingCore.Instance.NetworkEngine.GetGameClientConnection(mess.ConnectionID).Send(reply);
                AssaultWingCore.Instance.NetworkEngine.DropClient(mess.ConnectionID, false);
            }
            else
            {
                var reply = new JoinGameReply();
                AssaultWingCore.Instance.NetworkEngine.GetGameClientConnection(mess.ConnectionID).Send(reply);
            }
        }

        private static void HandlePlayerSettingsRequestOnServer(PlayerSettingsRequest mess)
        {
            if (!mess.IsRegisteredToServer)
            {
                var newPlayer = CreateAndAddNewPlayer(mess);
                var reply = new PlayerSettingsReply
                {
                    OldPlayerID = mess.PlayerID,
                    NewPlayerID = newPlayer.ID
                };
                AssaultWingCore.Instance.NetworkEngine.GetGameClientConnection(mess.ConnectionID).Send(reply);
            }
            else
            {
                var player = AssaultWingCore.Instance.DataEngine.Spectators.FirstOrDefault(plr => plr.ID == mess.PlayerID);
                if (player == null) throw new NetworkException("Settings update for unknown player ID " + mess.PlayerID);
                if (player.ConnectionID != mess.ConnectionID)
                {
                    // Silently ignoring update of a player that doesn't live on the client who sent the update.
                }
                else
                {
                    // Be careful not to overwrite the player's color with something silly from the client.
                    var oldColor = player is Player ? (Color?)((Player)player).PlayerColor : null;
                    mess.Read(player, SerializationModeFlags.ConstantData, 0);
                    if (oldColor.HasValue) ((Player)player).PlayerColor = oldColor.Value;
                }
            }
        }

        #endregion

        private static Player GetTempPlayer()
        {
            return new Player(null, "dummy", CanonicalString.Null, CanonicalString.Null, CanonicalString.Null, new AW2.UI.PlayerControls());
        }

        private static bool ClientPlayerCriteria(Spectator spectator, int oldPlayerID)
        {
            return spectator.ServerRegistration == Spectator.ServerRegistrationType.Requested &&
                spectator.ID == oldPlayerID;
        }

        private static Player CreateAndAddNewPlayer(PlayerSettingsRequest mess)
        {
            var newPlayer = new Player(null, "<uninitialised>", CanonicalString.Null, CanonicalString.Null, CanonicalString.Null, mess.ConnectionID);
            mess.Read(newPlayer, SerializationModeFlags.ConstantData, 0);
            AssaultWingCore.Instance.DataEngine.Spectators.Add(newPlayer);
            return newPlayer;
        }
    }
}