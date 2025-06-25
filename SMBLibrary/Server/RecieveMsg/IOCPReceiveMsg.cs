using SMBLibrary.NetBios;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Utilities;

namespace SMBLibrary.Server.RecieveMsg
{
    internal class IOCPReceiveMsg
    {
        SMBServer _smbServer;
        public IOCPReceiveMsg(SMBServer smbServer)
        {
            _smbServer = smbServer;
        }
        internal void InitAndStartReceive(ConnectionState state)
        {
            var tcs = new TaskCompletionSource<bool>();
            var args = new SocketAsyncEventArgs();
            args.Completed += (_, e) =>
            {
                if (e.SocketError != SocketError.Success)
                {
                    tcs.SetResult(false);
                    args.Dispose();

                    state.LogToServer(Severity.Warning, $"The connection was terminated。 {e.SocketError}");
                    _smbServer.m_connectionManager.ReleaseConnection(state);
                    return;
                }
                if (e.BytesTransferred == 0)
                {
                    tcs.SetResult(false);
                    args.Dispose();
                    return;
                }
                ReceiveCallback(ref state, e);
            };
            StartReceive(ref state, args);
        }

        private void StartReceive(ref ConnectionState state, SocketAsyncEventArgs args)
        {
            try
            {
                args.SetBuffer(state.ReceiveBuffer.Buffer, state.ReceiveBuffer.WriteOffset, state.ReceiveBuffer.AvailableLength);
                if (!state.ClientSocket.ReceiveAsync(args))
                {
                    ReceiveCallback(ref state, args);
                }
            }
            catch (ObjectDisposedException)
            {
                state.LogToServer(Severity.Debug, "The connection was terminated");
                _smbServer.m_connectionManager.ReleaseConnection(state);
                return;
            }
            catch (SocketException ex)
            {
                const int WSAECONNRESET = 10054;
                if (ex.ErrorCode == WSAECONNRESET)
                {
                    state.LogToServer(Severity.Debug, "The connection was forcibly closed by the remote host");
                }
                else
                {
                    state.LogToServer(Severity.Debug, "The connection was terminated, Socket error code: {0}", ex.ErrorCode);
                }
                _smbServer.m_connectionManager.ReleaseConnection(state);
                return;
            }
        }

        private void ReceiveCallback(ref ConnectionState state, SocketAsyncEventArgs args)
        {
            Socket clientSocket = state.ClientSocket;

            if (!_smbServer.m_listening)
            {
                clientSocket.Close();
                return;
            }

            int numberOfBytesReceived = args.BytesTransferred;
            if (numberOfBytesReceived == 0)
            {
                state.LogToServer(Severity.Debug, "The client closed the connection");
                _smbServer.m_connectionManager.ReleaseConnection(state);
                return;
            }

            state.UpdateLastReceiveDT();

            NBTConnectionReceiveBuffer receiveBuffer = state.ReceiveBuffer;
            receiveBuffer.SetNumberOfBytesReceived(numberOfBytesReceived);
            _smbServer.ProcessConnectionBuffer(ref state);

            if (!clientSocket.Connected)
            {
                return;
            }

            StartReceive(ref state, args);
        }

    }
}
