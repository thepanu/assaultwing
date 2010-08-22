﻿using System.Net;
using System.Net.Sockets;
using AW2.Net.ConnectionUtils;
using AW2.Net.Messages;

namespace AW2.Net.Connections
{
    /// <summary>
    /// A network connection to a game server.
    /// </summary>
    public class GameServerConnection : Connection
    {
        /// <summary>
        /// Creates a new connection to a game server.
        /// </summary>
        /// <param name="tcpSocket">An opened TCP socket to the remote host. The
        /// created connection owns the socket and will dispose of it.</param>
        public GameServerConnection(Socket tcpSocket, IPEndPoint remoteUDPEndPoint)
            : base(tcpSocket)
        {
            Name = "Game Server Connection " + ID;
            RemoteUDPEndPoint = remoteUDPEndPoint;
            IsHandshaked = true;
        }

        protected override void DisposeImpl(bool error)
        {
            if (error)
            {
                AssaultWing.Instance.StopClient();
                var dialogData = new AW2.Graphics.OverlayComponents.CustomOverlayDialogData(
                    "Connection to server lost!\nPress Enter to return to Main Menu",
                    new AW2.UI.TriggeredCallback(AW2.UI.TriggeredCallback.GetProceedControl(),
                        AssaultWing.Instance.ShowMenu));
                AssaultWing.Instance.ShowDialog(dialogData);
            }
            base.DisposeImpl(error);
        }
    }
}