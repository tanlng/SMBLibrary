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
    internal class TAPReceiveMsg
    {
        SMBServer _smbServer;
        internal TAPReceiveMsg(SMBServer smbServer)
        {
            _smbServer = smbServer;
        }
        internal async void InitAndStartReceive(ConnectionState state)
        {
            while (true)
            {
                Socket clientSocket = state.ClientSocket;

                int numberOfBytesReceived;
                try
                {
                    numberOfBytesReceived = await clientSocket.ReceiveAsync(
                        new ArraySegment<byte>(state.ReceiveBuffer.Buffer, state.ReceiveBuffer.WriteOffset, state.ReceiveBuffer.AvailableLength));
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
                        state.LogToServer(Severity.Warning, $"The connection was terminated, Socket error code: {ex.ErrorCode} {ex.Message}");
                    }
                    _smbServer.m_connectionManager.ReleaseConnection(state);
                    return;
                }

                if (!_smbServer.m_listening)
                {
                    clientSocket.Close();
                    return;
                }

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
            }
        }

    }
}
