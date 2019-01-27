using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Telepathy.Server
{
    class SocketManager
    {
        private readonly int _maxConnectNum;
        private readonly int _revBufferSize;
        private readonly BufferManager _bufferManager;
        private const int OpsToAlloc = 2;
        private Socket _listenSocket;
        private readonly SocketEventPool _pool;
        private int _clientCount;
        private readonly Semaphore _maxNumberAcceptedClients;

        public EventHandler<EventArgs<AsyncUserToken, int>> ClientNumberChange;

        public EventHandler<EventArgs<AsyncUserToken, byte[]>> ReceiveClientData;

        public List<AsyncUserToken> ClientList { get; set; }

        public SocketManager(int numConnections, int receiveBufferSize)
        {
            _clientCount = 0;
            _maxConnectNum = numConnections;
            _revBufferSize = receiveBufferSize;

            // allocate buffers such that the maximum number of sockets can have one outstanding read and
            //write posted to the socket simultaneously
            _bufferManager = new BufferManager(receiveBufferSize * numConnections * OpsToAlloc, receiveBufferSize);

            _pool = new SocketEventPool(numConnections);
            _maxNumberAcceptedClients = new Semaphore(numConnections, numConnections);
        }

        public void Init()
        {
            // Allocates one large byte buffer which all I/O operations use a piece of.  This guards
            // against memory fragmentation
            _bufferManager.InitBuffer();
            ClientList = new List<AsyncUserToken>();

            // preallocate pool of SocketAsyncEventArgs objects
            for (int i = 0; i < _maxConnectNum; i++)
            {
                var readWriteEventArg = new SocketAsyncEventArgs();
                readWriteEventArg.Completed += IO_Completed;
                readWriteEventArg.UserToken = new AsyncUserToken();

                // assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
                _bufferManager.SetBuffer(readWriteEventArg);

                // add SocketAsyncEventArg to the pool
                _pool.Push(readWriteEventArg);
            }
        }

        public bool Start(IPEndPoint localEndPoint)
        {
            try
            {
                ClientList.Clear();
                _listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _listenSocket.Bind(localEndPoint);

                // start the server with a listen backlog of 100 connections
                _listenSocket.Listen(_maxConnectNum);

                // post accepts on the listening socket
                StartAccept(null);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Stop()
        {
            foreach (AsyncUserToken token in ClientList)
            {
                try
                {
                    token.Socket.Shutdown(SocketShutdown.Both);
                }
                catch (Exception) { }
            }
            try
            {
                _listenSocket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception) { }

            _listenSocket.Close();
            int cCount = ClientList.Count;
            lock (ClientList) { ClientList.Clear(); }

            ClientNumberChange?.Invoke(-cCount, null);
            OnClientNumChanged(ClientNumberChange.CreateArgs(null, -cCount));
        }

        public void CloseClient(AsyncUserToken token)
        {
            try
            {
                token.Socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception) { }
        }

        // Begins an operation to accept a connection request from the client
        //
        // <param name="acceptEventArg">The context object to use when issuing
        // the accept operation on the server's listening socket</param>
        public void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += AcceptEventArg_Completed;
            }
            else
            {
                // socket must be cleared since the context object is being reused
                acceptEventArg.AcceptSocket = null;
            }

            _maxNumberAcceptedClients.WaitOne();
            if (!_listenSocket.AcceptAsync(acceptEventArg))
            {
                ProcessAccept(acceptEventArg);
            }
        }

        // This method is the callback method associated with Socket.AcceptAsync
        // operations and is invoked when an accept operation is complete
        //
        void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        protected virtual void OnClientNumChanged(EventArgs<AsyncUserToken, int> e)
        {
            ClientNumberChange?.Invoke(this, e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            try
            {
                Interlocked.Increment(ref _clientCount);

                // Get the socket for the accepted client connection and put it into the
                //ReadEventArg object user token
                SocketAsyncEventArgs readEventArgs = _pool.Pop();
                var userToken = (AsyncUserToken)readEventArgs.UserToken;
                userToken.Socket = e.AcceptSocket;
                userToken.ConnectTime = DateTime.Now;
                userToken.Remote = e.AcceptSocket.RemoteEndPoint;
                userToken.IpAddress = ((IPEndPoint)(e.AcceptSocket.RemoteEndPoint)).Address;

                lock (ClientList) { ClientList.Add(userToken); }

                OnClientNumChanged(ClientNumberChange.CreateArgs(userToken, 1));

                if (!e.AcceptSocket.ReceiveAsync(readEventArgs))
                {
                    ProcessReceive(readEventArgs);
                }
            }
            catch (Exception)
            {
                // log
            }

            // Accept the next connection request
            if (e.SocketError == SocketError.OperationAborted) return;
            StartAccept(e);
        }

        void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;

                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;

                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }
        }

        // This method is invoked when an asynchronous receive operation completes.
        // If the remote host closed the connection, then the socket is closed.
        // If data was received then the data is echoed back to the client.
        //
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            try
            {
                // check if the remote host closed the connection
                var token = (AsyncUserToken)e.UserToken;
                if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                {
                    byte[] data = new byte[e.BytesTransferred];
                    Array.Copy(e.Buffer, e.Offset, data, 0, e.BytesTransferred);
                    lock (token.Buffer)
                    {
                        token.Buffer.AddRange(data);
                    }

                    do
                    {
                        byte[] lenBytes = token.Buffer.GetRange(0, 4).ToArray();
                        int packageLen = BitConverter.ToInt32(lenBytes, 0);
                        if (packageLen > token.Buffer.Count - 4)
                        {
                            break;
                        }

                        byte[] rev = token.Buffer.GetRange(4, packageLen).ToArray();

                        lock (token.Buffer)
                        {
                            token.Buffer.RemoveRange(0, packageLen + 4);
                        }

                        var e1 = ReceiveClientData.CreateArgs(token, rev);
                        OnReceiveClientData(e1);

                    } while (token.Buffer.Count > 4);

                    if (!token.Socket.ReceiveAsync(e))
                        ProcessReceive(e);
                }
                else
                {
                    CloseClientSocket(e);
                }
            }
            catch (Exception)
            {
                //log;
            }
        }

        protected virtual void OnReceiveClientData(EventArgs<AsyncUserToken, byte[]> arg)
        {
            ReceiveClientData?.Invoke(this, arg);
        }

        // This method is invoked when an asynchronous send operation completes.
        // The method issues another receive on the socket to read any additional
        // data sent from the client
        //
        // <param name="e"></param>
        private void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                // done echoing data back to the client
                var token = (AsyncUserToken)e.UserToken;
                // read the next block of data send from the client
                bool willRaiseEvent = token.Socket.ReceiveAsync(e);
                if (!willRaiseEvent)
                {
                    ProcessReceive(e);
                }
            }
            else
            {
                CloseClientSocket(e);
            }
        }

        //关闭客户端
        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            var token = e.UserToken as AsyncUserToken;

            lock (ClientList) { ClientList.Remove(token); }

            // close the socket associated with the client
            try
            {
                token?.Socket.Shutdown(SocketShutdown.Send);
            }
            catch (Exception) { }
            token?.Socket.Close();

            // decrement the counter keeping track of the total number of clients connected to the server
            Interlocked.Decrement(ref _clientCount);
            _maxNumberAcceptedClients.Release();

            // Free the SocketAsyncEventArg so they can be reused by another client
            e.UserToken = new AsyncUserToken();
            _pool.Push(e);

            OnClientNumChanged(ClientNumberChange.CreateArgs(token, 1));
        }

        public void SendMessage(AsyncUserToken token, byte[] message)
        {
            if (token?.Socket == null || !token.Socket.Connected)
                return;

            try
            {
                var buff = new byte[message.Length + 4];
                byte[] len = BitConverter.GetBytes(message.Length);
                Array.Copy(len, buff, 4);
                Array.Copy(message, 0, buff, 4, message.Length);

                //token.Socket.Send(buff);  //这句也可以发送, 可根据自己的需要来选择

                var sendArg = new SocketAsyncEventArgs { UserToken = token };
                sendArg.SetBuffer(buff, 0, buff.Length);
                token.Socket.SendAsync(sendArg);
            }
            catch (Exception)
            {
                // log
            }
        }
    }
}