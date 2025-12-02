/* Copyright (C) 2017-2020 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Serialization;
using SMBLibrary.NetBios;
using SMBLibrary.Server.SMB2;
using SMBLibrary.SMB2;
using Utilities;

namespace SMBLibrary.Server
{
    public partial class SMBServer
    {
        private void ProcessSMB2RequestChain(List<SMB2Command> requestChain, ref ConnectionState state)
        {
#if DEBUG
            //foreach (var item in requestChain)
            //{
            //    state.LogToServer(Severity.Trace, $"Request {item.GetType().Name} \r\n" + Utilities.JsonConvertHelper.Serialize(item));
            //}
#endif
            FileID? fileID = null;
            NTStatus? fileIDStatus = null;
            foreach (SMB2Command request in requestChain)
            {
                try
                {
                //if (request is NegotiateRequest)
                //{
                    List<SMB2Command> responseChain = [];
                    ProcessResponse(ref state, responseChain, ref fileID, ref fileIDStatus, request);
                    if (responseChain.Count > 0)
                    {
                        EnqueueResponseChain(state, responseChain);
                    }
                //}
                //else
                //{
                //    var currentState = state;
                //    Task.Run(() =>
                //    {
                //        List<SMB2Command> responseChain = [];
                //        ProcessResponse(ref currentState, responseChain, ref fileID, ref fileIDStatus, request);
                //        if (responseChain.Count > 0)
                //        {
                //            EnqueueResponseChain(currentState, responseChain);
                //        }
                //    });
                //}
            }
                catch (Exception ex)
                {
                    state.LogToServer(Severity.Error, $"{ex.Message} \r\n  {ex.Source} \r\n  {ex.StackTrace} \r\n InnerException:{ex.InnerException}");
        }
            }
        }

        private void ProcessResponse(ref ConnectionState state, List<SMB2Command> responseChain, ref FileID? fileID, ref NTStatus? fileIDStatus, SMB2Command request)
        {
            SMB2Command response;
            if (request.Header.IsRelatedOperations && RequestContainsFileID(request))
            {
                if (fileIDStatus != null && fileIDStatus != NTStatus.STATUS_SUCCESS && fileIDStatus != NTStatus.STATUS_BUFFER_OVERFLOW)
                {
                    // [MS-SMB2] When the current request requires a FileId and the previous request either contains
                    // or generates a FileId, if the previous request fails with an error, the server SHOULD fail the
                    // current request with the same error code returned by the previous request.
                    state.LogToServer(Severity.Verbose, "Compunded related request {0} failed because FileId generation failed.", request.CommandName);
                    response = new ErrorResponse(request.CommandName, fileIDStatus.Value);
                }
                else if (fileID.HasValue)
                {
                    SetRequestFileID(request, fileID.Value);
                    response = ProcessSMB2Command(request, ref state);
                }
                else
                {
                    // [MS-SMB2] When the current request requires a FileId, and if the previous request neither contains
                    // nor generates a FileId, the server MUST fail the compounded request with STATUS_INVALID_PARAMETER.
                    state.LogToServer(Severity.Verbose, "Compunded related request {0} failed, the previous request neither contains nor generates a FileId.", request.CommandName);
                    response = new ErrorResponse(request.CommandName, NTStatus.STATUS_INVALID_PARAMETER);
                }
            }
            else
            {
                fileID = GetRequestFileID(request);
                response = ProcessSMB2Command(request, ref state);
            }

            if (response != null)
            {
                UpdateSMB2Header(response, request, state);
                responseChain.Add(response);
                if (GeneratesFileID(response))
                {
                    fileID = GetResponseFileID(response);
                    fileIDStatus = response.Header.Status;
                }
                else if (RequestContainsFileID(request))
                {
                    fileIDStatus = response.Header.Status;
                }
            }
        }

        /// <summary>
        /// May return null
        /// </summary>
        private SMB2Command ProcessSMB2Command(SMB2Command command, ref ConnectionState state)
        {
            if (state.Dialect == SMBDialect.NotSet)
            {
                if (command is NegotiateRequest)
                {
                    NegotiateRequest request = (NegotiateRequest)command;
                    bool supportsLeasing = (m_leaseConfig != null);
                    SMB2Command response = NegotiateHelper.GetNegotiateResponse(request, m_securityProvider, state, m_transport, m_serverGuid, m_serverStartTime, m_enableSMB3, supportsLeasing);
                    if (state.Dialect != SMBDialect.NotSet)
                    {
                        // Pass the global LeaseManager to the connection state
                        state = new SMB2ConnectionState(state, m_leaseConfig, m_leaseManager);
                        m_connectionManager.AddConnection(state);
                    }
                    return response;
                }
                else
                {
                    // [MS-SMB2] If the request being received is not an SMB2 NEGOTIATE Request [..]
                    // and Connection.NegotiateDialect is 0xFFFF or 0x02FF, the server MUST
                    // disconnect the connection.
                    state.LogToServer(Severity.Information, "Invalid Connection State for command {0}", command.CommandName.ToString());
                    state.ClientSocket.Close();
                    return null;
                }
            }
            else if (command is NegotiateRequest)
            {
                // [MS-SMB2] If Connection.NegotiateDialect is 0x0202, 0x0210, 0x0300, 0x0302, or 0x0311,
                // the server MUST disconnect the connection.
                state.LogToServer(Severity.Information, "Rejecting NegotiateRequest. NegotiateDialect is already set");
                state.ClientSocket.Close();
                return null;
            }
            else
            {
                // 使用高精度计时器记录命令处理的开始时间
                // System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // 处理命令
                SMB2Command result = ProcessSMB2Command(command, (SMB2ConnectionState)state);

                // 停止计时器并记录处理时间
                // stopwatch.Stop();
                //state.LogToServer(Severity.Information, "[性能统计] 处理 {0} 命令耗时: {1} 微秒", 
                //    command.CommandName, stopwatch.ElapsedTicks * 1000000.0 / System.Diagnostics.Stopwatch.Frequency);
                // 同时以毫秒为单位记录处理时间
                // double milliseconds = stopwatch.ElapsedMilliseconds;
                // state.LogToServer(Severity.Information, "[性能统计] 处理 {0} 命令耗时: {1} 毫秒", command.CommandName, milliseconds);
                return result;
                //return ProcessSMB2Command(command, (SMB2ConnectionState)state);
            }
        }

        private SMB2Command ProcessSMB2Command(SMB2Command command, SMB2ConnectionState state)
        {
            if (command is SessionSetupRequest)
            {
                return SessionSetupHelper.GetSessionSetupResponse((SessionSetupRequest)command, m_securityProvider, state);
            }
            else if (command is EchoRequest)
            {
                return new EchoResponse();
            }
            else
            {
                SMB2Session session = state.GetSession(command.Header.SessionID);
                if (session == null)
                {
                    return new ErrorResponse(command.CommandName, NTStatus.STATUS_USER_SESSION_DELETED);
                }

                if (command is TreeConnectRequest)
                {
                    return TreeConnectHelper.GetTreeConnectResponse((TreeConnectRequest)command, state, m_services, m_shares);
                }
                else if (command is LogoffRequest)
                {
                    state.LogToServer(Severity.Information, "Logoff: User '{0}' logged off. (SessionID: {1})", session.UserName, command.Header.SessionID);
                    m_securityProvider.DeleteSecurityContext(ref session.SecurityContext.AuthenticationContext);
                    state.RemoveSession(command.Header.SessionID);
                    return new LogoffResponse();
                }
                else if (command is LeaseBreakResponse)
                {
                    return session.ProcessLeaseBreakResponse((LeaseBreakResponse)command);
                }
                else if (command.Header.IsAsync)
                {
                    // TreeID will not be present in an ASYNC header
                    if (command is CancelRequest)
                    {
                        return CancelHelper.GetCancelResponse((CancelRequest)command, state);
                    }
                }
                else
                {
                    ISMBShare share = session.GetConnectedTree(command.Header.TreeID);
                    if (share == null)
                    {
                        state.LogToServer(Severity.Verbose, "{0} failed. Invalid TreeID (SessionID: {1}, TreeID: {2}).", command.CommandName, command.Header.SessionID, command.Header.TreeID);
                        return new ErrorResponse(command.CommandName, NTStatus.STATUS_NETWORK_NAME_DELETED);
                    }

                    if (command is TreeDisconnectRequest)
                    {
                        return TreeConnectHelper.GetTreeDisconnectResponse((TreeDisconnectRequest)command, share, state);
                    }
                    else if (command is CreateRequest)
                    {
                        return CreateHelper.GetCreateResponse((CreateRequest)command, share, state);
                    }
                    else if (command is QueryInfoRequest)
                    {
                        return QueryInfoHelper.GetQueryInfoResponse((QueryInfoRequest)command, share, state);
                    }
                    else if (command is SetInfoRequest)
                    {
                        return SetInfoHelper.GetSetInfoResponse((SetInfoRequest)command, share, state);
                    }
                    else if (command is QueryDirectoryRequest)
                    {
                        return QueryDirectoryHelper.GetQueryDirectoryResponse((QueryDirectoryRequest)command, share, state);
                    }
                    else if (command is ReadRequest)
                    {
                        return ReadWriteResponseHelper.GetReadResponse((ReadRequest)command, share, state);
                    }
                    else if (command is WriteRequest)
                    {
                        return ReadWriteResponseHelper.GetWriteResponse((WriteRequest)command, share, state);
                    }
                    else if (command is LockRequest)
                    {
                        return LockHelper.GetLockResponse((LockRequest)command, share, state);
                    }
                    else if (command is FlushRequest)
                    {
                        return ReadWriteResponseHelper.GetFlushResponse((FlushRequest)command, share, state);
                    }
                    else if (command is CloseRequest)
                    {
                        return CloseHelper.GetCloseResponse((CloseRequest)command, share, state);
                    }
                    else if (command is IOCtlRequest)
                    {
                        return IOCtlHelper.GetIOCtlResponse((IOCtlRequest)command, share, state);
                    }
                    else if (command is CancelRequest)
                    {
                        return CancelHelper.GetCancelResponse((CancelRequest)command, state);
                    }
                    else if (command is ChangeNotifyRequest)
                    {
                        return ChangeNotifyHelper.GetChangeNotifyInterimResponse((ChangeNotifyRequest)command, share, state);
                    }
                }
            }

            return new ErrorResponse(command.CommandName, NTStatus.STATUS_NOT_SUPPORTED);
        }

        internal static void EnqueueResponse(ConnectionState state, SMB2Command response)
        {
            List<SMB2Command> responseChain = new List<SMB2Command>();
            responseChain.Add(response);
            EnqueueResponseChain(state, responseChain);
        }

        private static void EnqueueResponseChain(ConnectionState state, List<SMB2Command> responseChain)
        {
#if DEBUG
            // 打印日志会影响响应时间，一般不打
            //foreach (var item in responseChain)
            //{
            //    state.LogToServer(Severity.Trace, $"Response  {item.GetType().Name} \r\n" + Utilities.JsonConvertHelper.Serialize(item));
            //}
#endif
            byte[] signingKey = null;
            if (state is SMB2ConnectionState)
            {
                // Note: multiple sessions MAY be multiplexed on the same connection, so theoretically
                // we could have compounding unrelated requests from different sessions.
                // In practice however this is not a real problem.
                ulong sessionID = responseChain[0].Header.SessionID;
                if (sessionID != 0)
                {
                    SMB2Session session = ((SMB2ConnectionState)state).GetSession(sessionID);
                    if (session != null)
                    {
                        signingKey = session.SigningKey;
                    }
                }
            }

            SessionMessagePacket packet = new SessionMessagePacket();
            SMB2Dialect smb2Dialect = (signingKey != null) ? ToSMB2Dialect(state.Dialect) : SMB2Dialect.SMB2xx;
            packet.Trailer = SMB2Command.GetCommandChainBytes(responseChain, signingKey, smb2Dialect);
            state.Send(packet);
            state.LogToServer(Severity.Verbose, "SMB2 response chain queued: Response count: {0}, First response: {1}, Packet length: {2}", responseChain.Count, responseChain[0].CommandName.ToString(), packet.Length);
        }

        internal static SMB2Dialect ToSMB2Dialect(SMBDialect smbDialect)
        {
            switch (smbDialect)
            {
                case SMBDialect.SMB202:
                    return SMB2Dialect.SMB202;
                case SMBDialect.SMB210:
                    return SMB2Dialect.SMB210;
                case SMBDialect.SMB300:
                    return SMB2Dialect.SMB300;
                case SMBDialect.SMB302:
                    return SMB2Dialect.SMB302;
                case SMBDialect.SMB311:
                    return SMB2Dialect.SMB311;
                default:
                    throw new ArgumentException("Unsupported SMB2 Dialect: " + smbDialect.ToString());
            }
        }

        private static void UpdateSMB2Header(SMB2Command response, SMB2Command request, ConnectionState state)
        {
            response.Header.MessageID = request.Header.MessageID;
            response.Header.CreditCharge = request.Header.CreditCharge;
            response.Header.Credits = Math.Max((ushort)1, request.Header.Credits);
            response.Header.IsRelatedOperations = request.Header.IsRelatedOperations;
            response.Header.Reserved = request.Header.Reserved;
            if (response.Header.SessionID == 0)
            {
                response.Header.SessionID = request.Header.SessionID;
            }
            if (response.Header.TreeID == 0)
            {
                response.Header.TreeID = request.Header.TreeID;
            }
            bool signingRequired = false;
            if (state is SMB2ConnectionState)
            {
                SMB2Session session = ((SMB2ConnectionState)state).GetSession(response.Header.SessionID);
                if (session != null && session.SigningRequired)
                {
                    signingRequired = true;
                }
            }
            // [MS-SMB2] The server SHOULD sign the message [..] if the request was signed by the client,
            // and the response is not an interim response to an asynchronously processed request.
            bool isInterimResponse = (response.Header.IsAsync && response.Header.Status == NTStatus.STATUS_PENDING);
            response.Header.IsSigned = (request.Header.IsSigned || signingRequired) && !isInterimResponse;
        }

        private static bool RequestContainsFileID(SMB2Command command)
        {
            return (command is ChangeNotifyRequest ||
                    command is CloseRequest ||
                    command is FlushRequest ||
                    command is IOCtlRequest ||
                    command is LockRequest ||
                    command is QueryDirectoryRequest ||
                    command is QueryInfoRequest ||
                    command is ReadRequest ||
                    command is SetInfoRequest ||
                    command is WriteRequest);
        }

        private static FileID? GetRequestFileID(SMB2Command command)
        {
            if (command is ChangeNotifyRequest)
            {
                return ((ChangeNotifyRequest)command).FileId;
            }
            else if (command is CloseRequest)
            {
                return ((CloseRequest)command).FileId;
            }
            else if (command is FlushRequest)
            {
                return ((FlushRequest)command).FileId;
            }
            else if (command is IOCtlRequest)
            {
                return ((IOCtlRequest)command).FileId;
            }
            else if (command is LockRequest)
            {
                return ((LockRequest)command).FileId;
            }
            else if (command is QueryDirectoryRequest)
            {
                return ((QueryDirectoryRequest)command).FileId;
            }
            else if (command is QueryInfoRequest)
            {
                return ((QueryInfoRequest)command).FileId;
            }
            else if (command is ReadRequest)
            {
                return ((ReadRequest)command).FileId;
            }
            else if (command is SetInfoRequest)
            {
                return ((SetInfoRequest)command).FileId;
            }
            else if (command is WriteRequest)
            {
                return ((WriteRequest)command).FileId;
            }
            return null;
        }

        private static void SetRequestFileID(SMB2Command command, FileID fileID)
        {
            if (command is ChangeNotifyRequest)
            {
                ((ChangeNotifyRequest)command).FileId = fileID;
            }
            else if (command is CloseRequest)
            {
                ((CloseRequest)command).FileId = fileID;
            }
            else if (command is FlushRequest)
            {
                ((FlushRequest)command).FileId = fileID;
            }
            else if (command is IOCtlRequest)
            {
                ((IOCtlRequest)command).FileId = fileID;
            }
            else if (command is LockRequest)
            {
                ((LockRequest)command).FileId = fileID;
            }
            else if (command is QueryDirectoryRequest)
            {
                ((QueryDirectoryRequest)command).FileId = fileID;
            }
            else if (command is QueryInfoRequest)
            {
                ((QueryInfoRequest)command).FileId = fileID;
            }
            else if (command is ReadRequest)
            {
                ((ReadRequest)command).FileId = fileID;
            }
            else if (command is SetInfoRequest)
            {
                ((SetInfoRequest)command).FileId = fileID;
            }
            else if (command is WriteRequest)
            {
                ((WriteRequest)command).FileId = fileID;
            }
        }

        private static bool GeneratesFileID(SMB2Command command)
        {
            return (command.CommandName == SMB2CommandName.Create ||
                    command.CommandName == SMB2CommandName.IOCtl);
        }

        private static FileID? GetResponseFileID(SMB2Command command)
        {
            if (command is CreateResponse)
            {
                return ((CreateResponse)command).FileId;
            }
            else if (command is IOCtlResponse)
            {
                return ((IOCtlResponse)command).FileId;
            }
            return null;
        }
    }
}
