//#define DEBUG_SENT_BYTE_COUNT // dumps to log an itemised count of sent bytes every second
//#define DEBUG_MESSAGE_DELAY // delays message sending to simulate lag
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using AW2.Helpers;
using AW2.Helpers.Collections;
using AW2.Net.ConnectionUtils;
using AW2.Net.Messages;

namespace AW2.Net.Connections
{
    /// <summary>
    /// A connection to a remote host over a network. Communication between 
    /// the local and remote host is done by messages. 
    /// </summary>
    /// Connection operates asynchronously. Both creation of connections and 
    /// sending messages via connections are done asynchronously. Therefore 
    /// their result is not known by the time the corresponding method call returns. 
    /// When results of such actions eventually arrive (as either success or 
    /// failure), they are added to corresponding queues.
    /// It is up to the client program to read the results from the queues.
    /// This can be done handily in the client program main loop. If such 
    /// a loop is not available, or for other reasons, the client program 
    /// can hook up events that notify of finished asynchronous operations.
    /// Such queues exist for connection attempts (static), received messages 
    /// (for each connection) and general error conditions (for each connection).
    /// 
    /// This class is thread safe.
    public abstract class Connection
    {
        #region Fields

        /// <summary>
        /// A meta-value for <see cref="ID"/> denoting an invalid value.
        /// </summary>
        public const int INVALID_ID = -1;

        /// <summary>
        /// Least int that is known not to have been used as a connection identifier.
        /// </summary>
        /// <see cref="Connection.Id"/>
        private static int g_leastUnusedID = 0;

        private static List<ConnectAsyncState> g_connectAsyncStates = new List<ConnectAsyncState>();

        /// <summary>
        /// If greater than zero, then the connection is disposed and thus no longer usable.
        /// </summary>
        private int _isDisposed;

        /// <summary>
        /// TCP socket to the connected remote host.
        /// </summary>
        private AWTCPSocket _tcpSocket;

        private IPEndPoint _remoteUDPEndPoint;

        /// <summary>
        /// Received messages that are waiting for consumption by the client program.
        /// </summary>
        private TypedQueue<Message> _messages;

#if DEBUG_SENT_BYTE_COUNT
        private static TimeSpan _lastPrintTime = new TimeSpan(-1);
        private static Dictionary<Type, int> _messageSizes = new Dictionary<Type, int>();
#endif

#if DEBUG_MESSAGE_DELAY
        // TimeSpan is the time to send the message
        private Queue<AW2.Helpers.Pair<Message, TimeSpan>> _messagesToSend = new Queue<AW2.Helpers.Pair<Message, TimeSpan>>();
#endif

        #endregion Fields

        #region Properties

        /// <summary>
        /// Unique identifier of the connection. Nonnegative.
        /// </summary>
        public int ID { get; private set; }

        /// <summary>
        /// Short, human-readable name of the connection.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Has the connection handshake been completed. Sending UDP messages is not possible before the
        /// handshaking is complete.
        /// </summary>
        public bool IsHandshaked { get; protected set; }

        public bool IsDisposed { get { return _isDisposed > 0; } }

        /// <summary>
        /// The remote TCP end point of the connection.
        /// </summary>
        /// <see cref="System.Net.Sockets.Socket.RemoteEndPoint"/>
        public IPEndPoint RemoteTCPEndPoint
        {
            get
            {
                if (IsDisposed) throw new InvalidOperationException("This connection has been disposed");
                return _tcpSocket.RemoteEndPoint;
            }
        }

        /// <summary>
        /// The remote UDP end point of the connection.
        /// </summary>
        /// <see cref="System.Net.Sockets.Socket.RemoteEndPoint"/>
        public IPEndPoint RemoteUDPEndPoint
        {
            get
            {
                if (IsDisposed) throw new InvalidOperationException("This connection has been disposed");
                return _remoteUDPEndPoint;
            }
            set { _remoteUDPEndPoint = value; }
        }

        /// <summary>
        /// The remote IP address of the connection.
        /// </summary>
        public IPAddress RemoteIPAddress
        {
            get
            {
                if (IsDisposed) throw new InvalidOperationException("This connection has been disposed");
                return _remoteUDPEndPoint != null ? _remoteUDPEndPoint.Address : _tcpSocket.RemoteEndPoint.Address;
            }
        }

        /// <summary>
        /// Received messages that are waiting for consumption by the client program.
        /// </summary>
        public ITypedQueue<Message> Messages { get { return _messages; } }

        /// <summary>
        /// Results of connection attempts.
        /// </summary>
        public static ThreadSafeWrapper<Queue<Result<Connection>>> ConnectionResults { get; private set; }

        /// <summary>
        /// Information about general error situations in background threads.
        /// </summary>
        public ThreadSafeWrapper<Queue<Exception>> Errors
        {
            get
            {
                if (_tcpSocket == null) return null;
                return _tcpSocket.Errors;
            }
        }

        public PingInfo PingInfo { get; private set; }

        #endregion Properties

        #region Public interface

        /// <summary>
        /// Starts opening a connection to a remote host.
        /// </summary>
        /// <param name="remoteEndPoints">Alternative end points to connect to.</param>
        public static void Connect(AWEndPoint[] remoteEndPoints)
        {
            var sockets = new Socket[remoteEndPoints.Length];
            var asyncState = new ConnectAsyncState(sockets, remoteEndPoints);
            g_connectAsyncStates.Add(asyncState);
            for (int i = 0; i < remoteEndPoints.Length; i++)
            {
                int index = i;
                sockets[i] = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sockets[i].BeginConnect(remoteEndPoints[i].TCPEndPoint, result => ConnectCallback(result, index), asyncState);
            }
        }

        /// <summary>
        /// Cancels all current connection attempts to a remote host.
        /// </summary>
        public static void CancelConnect()
        {
            foreach (var state in g_connectAsyncStates) state.Cancel();
        }

        /// <summary>
        /// Updates the connection's ping information. Call this every frame.
        /// </summary>
        public void Update()
        {
            if (IsHandshaked) PingInfo.Update();
        }

        /// <summary>
        /// Sends a message to the remote host. The message is sent asynchronously,
        /// so there is no guarantee when the transmission will be finished.
        /// </summary>
        /// <param name="message">The message to send.</param>
        public virtual void Send(Message message)
        {
            if (IsDisposed) return;
            try
            {
#if DEBUG_MESSAGE_DELAY
                // Store message and check if there are old messages to send.
                TimeSpan sendTime = AssaultWing.Instance.GameTime.TotalRealTime + TimeSpan.FromMilliseconds(100);
                messagesToSend.Enqueue(new AW2.Helpers.Pair<Message, TimeSpan>(message, sendTime));
                while (messagesToSend.Peek().Second <= AssaultWing.Instance.GameTime.TotalRealTime)
                {
                    var sendMessage = messagesToSend.Dequeue().First;
                    Send(sendMessage.Serialize());
                }
#else
                var data = message.Serialize();
                switch (message.SendType)
                {
                    case MessageSendType.TCP: SendViaTCP(data); break;
                    case MessageSendType.UDP: SendViaUDP(data); break;
                    default: throw new MessageException("Unknown send type " + message.SendType);
                }
#endif
#if DEBUG_SENT_BYTE_COUNT
                if (_lastPrintTime + TimeSpan.FromSeconds(1) < AssaultWing.Instance.GameTime.TotalRealTime)
                {
                    _lastPrintTime = AssaultWing.Instance.GameTime.TotalRealTime;
                    AW2.Helpers.Log.Write("------ SENT_BYTE_COUNT dump");
                    foreach (var pair in _messageSizes)
                        AW2.Helpers.Log.Write(pair.Key.Name + ": " + pair.Value + " bytes");
                    AW2.Helpers.Log.Write("Total " + _messageSizes.Sum(pair => pair.Value) + " bytes");
                    _messageSizes.Clear();
                }
                if (!_messageSizes.ContainsKey(message.GetType()))
                    _messageSizes.Add(message.GetType(), data.Length);
                else
                    _messageSizes[message.GetType()] += data.Length;
#endif
            }
            catch (SocketException e)
            {
                Errors.Do(queue => queue.Enqueue(e));
            }

        }

        /// <summary>
        /// Closes the connection and frees resources it has allocated.
        /// </summary>
        public void Dispose()
        {
            Dispose(false);
        }

        /// <summary>
        /// Reacts to errors that may have occurred during the connection's
        /// operation in background threads.
        /// </summary>
        public void HandleErrors()
        {
            if (IsDisposed) return;
            if (Errors == null) return;
            bool errorsFound = false;
            Errors.Do(queue =>
            {
                while (queue.Count > 0)
                {
                    errorsFound = true;
                    Exception e = queue.Dequeue();
                    AW2.Helpers.Log.Write("Error occurred with " + Name + ": " + e.Message);
                }
            });
            if (errorsFound)
            {
                AW2.Helpers.Log.Write("Closing " + Name + " due to errors");
                Dispose(true);
            }
        }

        /// <summary>
        /// Returns the number of bytes waiting to be sent through this connection.
        /// </summary>
        public int GetSendQueueSize()
        {
            if (IsDisposed) return 0;
            return _tcpSocket.GetSendQueueSize();
        }

        /// <summary>
        /// Interprets a received buffer as a message from the network.
        /// </summary>
        public void HandleMessage(NetBuffer messageHeaderAndBody)
        {
            var message = Message.Deserialize(messageHeaderAndBody.Buffer, ID);
            if (!TryHandleMessageInternally(message, messageHeaderAndBody.EndPoint))
                _messages.Enqueue(message);
        }

        public static void HandleNewConnection(Result<Connection> result)
        {
            if (result == null) return;
            ReportResult(result);
        }

        #endregion Public interface

        #region Non-public methods

        static Connection()
        {
            ConnectionResults = new ThreadSafeWrapper<Queue<Result<Connection>>>(new Queue<Result<Connection>>());
        }

        /// <summary>
        /// Closes the connection and frees resources it has allocated.
        /// </summary>
        /// <param name="error">If <c>true</c> then an internal error has occurred.</param>
        protected void Dispose(bool error)
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) > 0) return;
            DisposeImpl(error);
        }

        /// <summary>
        /// Performs the actual disposing.
        /// </summary>
        /// <param name="error">If <c>true</c> then an internal error has occurred.</param>
        protected virtual void DisposeImpl(bool error)
        {
            _tcpSocket.Dispose(error);
        }

        /// <summary>
        /// Creates a new connection to a remote host.
        /// </summary>
        /// <param name="tcpSocket">An opened TCP socket to the remote host. The
        /// created connection owns the socket and will dispose of it.</param>
        /// The client program can create <see cref="Connection">Connections</see>
        /// via the static methods <see cref="StartListening(int, string)"/> and 
        /// <see cref="Connect(IPAddress, int, string)"/>.
        protected Connection(Socket tcpSocket)
            : this()
        {
            if (tcpSocket == null) throw new ArgumentNullException("tcpSocket", "Null socket argument");
            if (!tcpSocket.Connected) throw new ArgumentException("Socket not connected", "tcpSocket");
            _tcpSocket = new AWTCPSocket(tcpSocket, HandleMessage);
            _tcpSocket.StartThreads();
        }

        /// <summary>
        /// Creates a UDP-only connection.
        /// </summary>
        protected Connection()
        {
            ID = g_leastUnusedID++;
            Name = "Connection " + ID;
            _messages = new TypedQueue<Message>();
            PingInfo = new PingInfo(this);
        }

        /// <summary>
        /// Sends raw byte data to the remote host via TCP. The data is sent asynchronously,
        /// so there is no guarantee when the transmission will be finished.
        /// </summary>
        private void SendViaTCP(byte[] data)
        {
            if (_tcpSocket == null) throw new InvalidOperationException("Connection has no TCP socket for sending a message");
            _tcpSocket.Send(data, null);
        }

        /// <summary>
        /// Sends raw byte data to the remote host via UDP. The data is sent asynchronously,
        /// so there is no guarantee when the transmission will be finished.
        /// </summary>
        private void SendViaUDP(byte[] data)
        {
            if (!IsHandshaked) throw new InvalidOperationException("Cannot send messages via UDP before connection is handshaked");
            AssaultWing.Instance.NetworkEngine.UDPSocket.Send(data, RemoteUDPEndPoint);
        }

        /// <summary>
        /// Returns true if message was interpreted internally by the <see cref="Connection"/>
        /// and needs not be added to the public message queue.
        /// </summary>
        private bool TryHandleMessageInternally(Message mess, IPEndPoint remoteEndPoint)
        {
            // HACK: All we want is, on the game server, to read the sender's (which is a game client)
            // UDP end point from the message. We happen to know that PingRequestMessage is sent via UDP.
            if (!IsHandshaked && mess is PingRequestMessage)
            {
                RemoteUDPEndPoint = new IPEndPoint(RemoteTCPEndPoint.Address, remoteEndPoint.Port);
                IsHandshaked = true;
            }
            return false;
        }

        public static void ReportResult(Result<Connection> result)
        {
            ConnectionResults.Do(queue => queue.Enqueue(result));
        }

        #endregion Non-public methods

        #region Private callback implementations

        /// <summary>
        /// Callback implementation for finishing an outgoing connection attempt.
        /// </summary>
        /// <param name="asyncResult">The result of the asynchronous operation.</param>
        /// <param name="index">Index of the connection attempt in <c>asyncResult.AsyncState</c></param>
        private static void ConnectCallback(IAsyncResult asyncResult, int index)
        {
            g_connectAsyncStates.Remove((ConnectAsyncState)asyncResult.AsyncState);
            var result = ConnectAsyncState.ConnectionAttemptCallback(asyncResult, () => CreateServerConnection(asyncResult, index));
            HandleNewConnection(result);
        }

        private static Connection CreateServerConnection(IAsyncResult asyncResult, int index)
        {
            var state = (ConnectAsyncState)asyncResult.AsyncState;
            state.Sockets[index].EndConnect(asyncResult);
            var connection = new GameServerConnection(state.Sockets[index], state.RemoteEndPoints[index].UDPEndPoint);
            connection.RemoteUDPEndPoint = state.RemoteEndPoints[index].UDPEndPoint;
            return connection;
        }

        #endregion Private callback implementations
    }
}