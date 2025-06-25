/* Copyright (C) 2014-2020 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using SMBLibrary.Authentication.GSSAPI;
using SMBLibrary.NetBios;
using Utilities;

namespace SMBLibrary.Server
{
    internal delegate void LogDelegate(Severity severity, string message);

    internal class ConnectionState
    {
        private Socket m_clientSocket;
        private IPEndPoint m_clientEndPoint;
        private NBTConnectionReceiveBuffer m_receiveBuffer;
        private BlockingQueue<SessionPacket> m_sendQueue;
        private DateTime m_creationDT;
        private DateTime m_lastReceiveDT;
        private Reference<DateTime> m_lastSendDTRef; // We must use a reference because the sender thread will keep using the original ConnectionState object
        private LogDelegate LogToServerHandler;
        public SMBDialect Dialect;
        public GSSContext AuthenticationContext;

        public SocketAsyncEventArgs SendEventArgs { get; set; }

        public ConnectionState(Socket clientSocket, IPEndPoint clientEndPoint, LogDelegate logToServerHandler)
        {
            m_clientSocket = clientSocket;
            m_clientEndPoint = clientEndPoint;
            m_receiveBuffer = new NBTConnectionReceiveBuffer();
            m_sendQueue = new BlockingQueue<SessionPacket>();
            m_creationDT = DateTime.UtcNow;
            m_lastReceiveDT = DateTime.UtcNow;
            m_lastSendDTRef = DateTime.UtcNow;
            LogToServerHandler = logToServerHandler;
            Dialect = SMBDialect.NotSet;
        }

        public ConnectionState(ConnectionState state)
        {
            m_clientSocket = state.ClientSocket;
            m_clientEndPoint = state.ClientEndPoint;
            m_receiveBuffer = state.ReceiveBuffer;
            m_sendQueue = state.SendQueue;
            m_creationDT = state.CreationDT;
            m_lastReceiveDT = state.LastReceiveDT;
            m_lastSendDTRef = state.LastSendDTRef;
            LogToServerHandler = state.LogToServerHandler;
            Dialect = state.Dialect;
        }

        /// <summary>
        /// Free all resources used by the active sessions in this connection
        /// </summary>
        public virtual void CloseSessions()
        {
        }

        public virtual List<SessionInformation> GetSessionsInformation()
        {
            return new List<SessionInformation>();
        }

        public void LogToServer(Severity severity, string message)
        {
            message = String.Format("[{0}] {1}", ConnectionIdentifier, message);
            if (LogToServerHandler != null)
            {
                LogToServerHandler(severity, message);
            }
        }

        public void LogToServer(Severity severity, string message, params object[] args)
        {
            LogToServer(severity, String.Format(message, args));
        }

        public Socket ClientSocket
        {
            get
            {
                return m_clientSocket;
            }
        }

        public IPEndPoint ClientEndPoint
        {
            get
            {
                return m_clientEndPoint;
            }
        }

        public NBTConnectionReceiveBuffer ReceiveBuffer
        {
            get
            {
                return m_receiveBuffer;
            }
        }

        public BlockingQueue<SessionPacket> SendQueue
        {
            get
            {
                return m_sendQueue;
            }
        }

        public DateTime CreationDT
        {
            get
            {
                return m_creationDT;
            }
        }

        public DateTime LastReceiveDT
        {
            get
            {
                return m_lastReceiveDT;
            }
        }

        public DateTime LastSendDT
        {
            get
            {
                return LastSendDTRef.Value;
            }
        }

        internal Reference<DateTime> LastSendDTRef
        {
            get
            {
                return m_lastSendDTRef;
            }
        }

        public void UpdateLastReceiveDT()
        {
            m_lastReceiveDT = DateTime.UtcNow;
        }

        public void UpdateLastSendDT()
        {
            m_lastSendDTRef.Value = DateTime.UtcNow;
        }

        public string ConnectionIdentifier
        {
            get
            {
                if (ClientEndPoint != null)
                {
                    return ClientEndPoint.Address + ":" + ClientEndPoint.Port;
                }
                return String.Empty;
            }
        }

        public int SendAttempts { get; internal set; }
        public void Send(SessionPacket response) {
            Socket clientSocket = ClientSocket;
            try
            {
                // 开始测量发送耗时
                //Stopwatch sendStopwatch = Stopwatch.StartNew();
                byte[] responseBytes = response.GetPoolBytes();
                try
                {
                    clientSocket.Send(responseBytes, 0, response.ActualByteLength, SocketFlags.None);
                }
                finally
                {
                    response.ReturnBuffer(responseBytes); // 必须归还内存池
                }
                //sendStopwatch.Stop();
                //if (responseBytes.Length > 1024)
                //{
                // 计算发送速度（单位：字节/秒）
                //double sendSpeed = (double)responseBytes.Length / 1024 / 1024 / (sendStopwatch.Elapsed.TotalSeconds);
                //    PrintWithInterval(state, $"send {response.Type} {responseBytes.Length}/{sendStopwatch.Elapsed.TotalSeconds} 速度: {sendSpeed:F2} MB/秒 | 队列剩余{state.SendQueue.Count} | activeConnections 数量 {m_connectionManager.ActiveConnectionsCount}");
                //}
            }
            catch (SocketException ex)
            {
                LogToServer(Severity.Warning, "Failed to send packet. SocketException: {0}", ex.Message);
                // Note: m_connectionManager contains SMB1ConnectionState or SMB2ConnectionState instances that were constructed from the initial
                // ConnectionState instance given to this method. for this reason, we must use state.ClientEndPoint to find and release the connection.
                //m_connectionManager.ReleaseConnection(ClientEndPoint);
                return;
            }
            catch (ObjectDisposedException)
            {
                LogToServer(Severity.Warning, "Failed to send packet. ObjectDisposedException.");
                //m_connectionManager.ReleaseConnection(ClientEndPoint);
                return;
            }

            UpdateLastSendDT();
        }
    }
}
