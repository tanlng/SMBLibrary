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
    internal class APMReceiveMsg
    {
        SMBServer _smbServer;
        public APMReceiveMsg(SMBServer smbServer)
        {
            _smbServer = smbServer;
        }
        internal void InitAndStartReceive(ConnectionState state)
        {
            Socket clientSocket = state.ClientSocket;
            clientSocket.BeginReceive(state.ReceiveBuffer.Buffer, state.ReceiveBuffer.WriteOffset, state.ReceiveBuffer.AvailableLength, 0, ReceiveCallback, state);
        }
        private void ReceiveCallback(IAsyncResult result)
        {
            ConnectionState state = (ConnectionState)result.AsyncState;
            Socket clientSocket = state.ClientSocket;

            if (!_smbServer.m_listening)
            {
                clientSocket.Close();
                return;
            }

            int numberOfBytesReceived;
            try
            {
                numberOfBytesReceived = clientSocket.EndReceive(result);
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
                    state.LogToServer(Severity.Warning, "The connection was forcibly closed by the remote host");
                }
                else
                {
                    state.LogToServer(Severity.Warning, $"The connection was terminated, Socket error code: {ex.ErrorCode} {ex.Message}", );
                }
                _smbServer.m_connectionManager.ReleaseConnection(state);
                return;
            }

            if (numberOfBytesReceived == 0)
            {
                state.LogToServer(Severity.Debug, "The client closed the connection");
                _smbServer.m_connectionManager.ReleaseConnection(state);
                return;
            }

            state.UpdateLastReceiveDT();

            lock (state.ReceiveBuffer)
            {
                NBTConnectionReceiveBuffer receiveBuffer = state.ReceiveBuffer;
                receiveBuffer.SetNumberOfBytesReceived(numberOfBytesReceived);
                _smbServer.ProcessConnectionBuffer(ref state);

                if (clientSocket.Connected)
                {
                    try
                    {
                        clientSocket.BeginReceive(state.ReceiveBuffer.Buffer, state.ReceiveBuffer.WriteOffset, state.ReceiveBuffer.AvailableLength, 0, ReceiveCallback, state);
                    }
                    catch (ObjectDisposedException)
                    {
                        _smbServer.m_connectionManager.ReleaseConnection(state);
                    }
                    catch (SocketException)
                    {
                        _smbServer.m_connectionManager.ReleaseConnection(state);
                    }
                }
            }
        }
    }
}
