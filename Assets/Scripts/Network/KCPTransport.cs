// MirrorLite - 精简版 (UDP)
// 单文件示例，放入 Unity 项目 Assets/Scripts/MirrorLite.cs
// 用法：
// 1) 在空物体上添加 NetworkManager 组件，设置 playerPrefab。
// 2) 运行时调用 StartServer() / StartClient(address).
// 3) Player prefab 必须包含 NetworkIdentity 和 NetworkBehaviour 派生脚本。
// 注意：这是教学示例，省略了很多容错、加密、包分片、可靠传输等功能。

using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace MirrorLite
{
    #region Transport (UDP)
    // Reliable UDP transport (sequence, ack, retransmit, timeout).
    // Teaching-focused: single-threaded Tick() drives retransmits/timeouts.
    public class KCPTransport 
    {
        const int MaxPayloadBytes = 1200; // keep under typical MTU (avoid fragmentation)
        const float ResendIntervalSec = 0.20f; // initial RTO
        const float DisconnectTimeoutSec = 10f;
        const int MaxResendAttempts = 30;
        const int ReorderWindow = 128; // max gap tolerated for in-order delivery

        enum PacketType : byte
        {
            DataReliable = 1,
            DataUnreliable = 2,
            Ping = 3
        }

        class SentPacket
        {
            public uint seq;//序列号
            public byte[] bytes; // full encoded packet bytes (including header)//完整编码的包字节（包括头）
            public float lastSentTime;//最后发送时间
            public int attempts;//尝试次数
        }

        class Connection
        {
            public readonly object sync = new object();
            public int id;//连接ID
            public IPEndPoint endpoint;

            // receive side
            public uint recvNext; // next in-order seq expected//下一个有序序列号期望
            public Dictionary<uint, byte[]> recvBuffer = new Dictionary<uint, byte[]>();

            // ack state (for packets we received from remote)
            public uint lastReceivedSeq;//最后一个接收序列号
            public uint ackBits; // bit0 => lastReceivedSeq-1 ... bit31 => lastReceivedSeq-32

            // send side
            public uint sendNext; // next seq to use
            public Dictionary<uint, SentPacket> sent = new Dictionary<uint, SentPacket>();//发送的包

            public float lastHeardTime;//最后听到时间
            public bool connectedEventFired;//连接事件已触发
        }

        readonly object connLock = new object();

        UdpClient server;
        UdpClient client;
        IPEndPoint serverRemoteEP;//服务器远程端点
        Thread recvThread;//接收线程
        volatile bool running;//运行状态

        // server: endpoint -> connection
        readonly Dictionary<string, Connection> serverConnsByEP = new Dictionary<string, Connection>();//服务器端点 -> 连接
        readonly Dictionary<int, Connection> serverConnsById = new Dictionary<int, Connection>();//服务器ID -> 连接
        int nextConnId = 1;

        // client: single connection to server
        Connection clientConn;//客户端连接

        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static float Now() => (float)Clock.Elapsed.TotalSeconds;//当前时间

        public Action<int, byte[]> OnData; // connectionId (we'll use port hash) -> data//连接ID（我们将使用端口哈希）-> 数据
        public Action<int> OnConnected;//连接事件
        public Action<int> OnDisconnected;//断开连接事件

        public int StartServer(int listenPort)//启动服务器
        {
            server = new UdpClient(listenPort);
            serverRemoteEP = new IPEndPoint(IPAddress.Any, 0);//服务器远程端点
            running = true;//运行状态
            recvThread = new Thread(ServerReceiveLoop)
             { IsBackground = true };//接收线程
            recvThread.Start();//启动接收线程
            Debug.Log($"[UdpTransport] Server started at port {listenPort}");
            return listenPort;//返回端口
        }

        void ServerReceiveLoop()//服务器接收循环
        {
            try
            {
                while (running)//运行状态
                {
                    var data = server.Receive(ref serverRemoteEP);//接收数据
                    HandleIncomingServer(serverRemoteEP, data);//处理接收数据
                }
            }
            catch (Exception e)//异常处理
            {
                Debug.LogWarning("[UdpTransport] Server receive stopped: " + e.Message);//日志警告
            }
        }

        public void SendTo(int connectionId, string ip, int port, byte[] data)//发送数据
        {
            using (var udp = new UdpClient())
            {
                udp.Send(data, data.Length, ip, port);//发送数据
            }
        }

        // Client side
        public void StartClient(string serverIp, int serverPort)//启动客户端
        {
            client = new UdpClient();
            serverRemoteEP = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);//服务器远程端点
            clientConn = new Connection
            {
                id = 0,
                endpoint = serverRemoteEP,
                recvNext = 1,
                lastReceivedSeq = 0,
                ackBits = 0,
                sendNext = 1,
                lastHeardTime = Now()
            };
            running = true;
            recvThread = new Thread(ClientReceiveLoop) { IsBackground = true };
            recvThread.Start();
            Debug.Log($"[UdpTransport] Client started, server {serverIp}:{serverPort}");
        }

        void ClientReceiveLoop()
        {
            try
            {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                while (running)
                {
                    var data = client.Receive(ref ep);
                    HandleIncomingClient(ep, data);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UdpTransport] Client receive stopped: " + e.Message);
            }
        }

        // Reliable send to server (client side).
        public void ClientSend(byte[] payload)
        {
            ClientSendReliable(payload);
        }

        public void Stop()
        {
            running = false;
            try { server?.Close(); } catch { }
            ;
            try { client?.Close(); } catch { }
            ;
            try { recvThread?.Abort(); } catch { }
            ;
        }

        // Call this from Unity main thread periodically (e.g., NetworkManager.Update).
        public void Tick()
        {
            float now = Now();

            // client tick
            if (clientConn != null)
            {
                TickConnection(client, clientConn, now, isServer: false);
                float lastHeard;
                lock (clientConn.sync) lastHeard = clientConn.lastHeardTime;
                if (now - lastHeard > DisconnectTimeoutSec)
                {
                    OnDisconnected?.Invoke(0);
                    clientConn = null;
                }
            }

            // server tick
            if (server != null)
            {
                List<int> toDrop = null;//要丢弃的连接ID
                lock (connLock)
                {
                    foreach (var kv in serverConnsById)
                    {
                        var c = kv.Value;
                        TickConnection(server, c, now, isServer: true);
                        float lastHeard;
                        lock (c.sync) lastHeard = c.lastHeardTime;
                        if (now - lastHeard > DisconnectTimeoutSec)
                        {
                            (toDrop ??= new List<int>()).Add(c.id);
                        }
                    }
                }

                if (toDrop != null)
                {
                    foreach (var id in toDrop)
                        DropServerConnection(id);
                }
            }
        }

        // ---- Public sends ----
        public void ServerSendReliable(int connectionId, byte[] payload)//服务器发送可靠数据
        {
            if (server == null) return;
            Connection c;
            lock (connLock) serverConnsById.TryGetValue(connectionId, out c);
            if (c == null) return;
            SendReliable(server, c, payload);
        }

        public void ServerBroadcastReliable(byte[] payload)
        {
            if (server == null) return;
            List<Connection> targets = new List<Connection>();
            lock (connLock)
            {
                foreach (var kv in serverConnsById)
                    targets.Add(kv.Value);
            }
            foreach (var c in targets)
                SendReliable(server, c, payload);
        }

        public void ClientSendReliable(byte[] payload)
        {
            if (client == null || clientConn == null) return;
            SendReliable(client, clientConn, payload);
        }

        public void ClientSendUnreliable(byte[] payload)
        {
            if (client == null || clientConn == null) return;
            var packet = EncodePacket(PacketType.DataUnreliable, 0, clientConn.lastReceivedSeq, clientConn.ackBits, payload);
            client.Send(packet, packet.Length, clientConn.endpoint);
        }

        // ---- Receive handling ----
        void HandleIncomingClient(IPEndPoint from, byte[] bytes)
        {
            if (clientConn == null) return;
            if (!EndpointsMatch(from, clientConn.endpoint)) return;

            lock (clientConn.sync) clientConn.lastHeardTime = Now();
            if (!TryDecodePacket(bytes, out var type, out var seq, out var ack, out var ackBits, out var payload))
                return;

            lock (clientConn.sync)
            {
                ProcessAcks(clientConn, ack, ackBits);
            }

            if (type == PacketType.DataReliable)
            {
                if (seq != 0)
                {
                    lock (clientConn.sync)
                    {
                        ProcessReliableIncoming(clientConn, 0, seq, payload);
                    }
                }
            }
            else if (type == PacketType.DataUnreliable)
            {
                OnData?.Invoke(0, payload);
            }
        }

        void HandleIncomingServer(IPEndPoint from, byte[] bytes)
        {
            Connection c = null;
            string key = EndpointKey(from);
            lock (connLock)
            {
                if (!serverConnsByEP.TryGetValue(key, out c))
                {
                    c = new Connection
                    {
                        id = nextConnId++,
                        endpoint = new IPEndPoint(from.Address, from.Port),
                        recvNext = 1,
                        lastReceivedSeq = 0,
                        ackBits = 0,
                        sendNext = 1,
                        lastHeardTime = Now()
                    };
                    serverConnsByEP[key] = c;
                    serverConnsById[c.id] = c;
                }
            }

            lock (c.sync) c.lastHeardTime = Now();

            if (!TryDecodePacket(bytes, out var type, out var seq, out var ack, out var ackBits, out var payload))
                return;

            bool fireConnected = false;
            lock (c.sync)
            {
                ProcessAcks(c, ack, ackBits);
                if (!c.connectedEventFired && type != 0)
                {
                    c.connectedEventFired = true;
                    fireConnected = true;
                }
            }
            if (fireConnected) OnConnected?.Invoke(c.id);

            if (type == PacketType.DataReliable)
            {
                if (seq != 0)
                {
                    lock (c.sync)
                    {
                        ProcessReliableIncoming(c, c.id, seq, payload);
                    }
                }
            }
            else if (type == PacketType.DataUnreliable)
            {
                OnData?.Invoke(c.id, payload);
            }
        }

        void DropServerConnection(int id)
        {
            Connection c;
            lock (connLock)
            {
                if (!serverConnsById.TryGetValue(id, out c)) return;
                serverConnsById.Remove(id);
                serverConnsByEP.Remove(EndpointKey(c.endpoint));
            }
            OnDisconnected?.Invoke(id);
        }

        // ---- Reliable incoming/outgoing ----
        void SendReliable(UdpClient udp, Connection c, byte[] payload)
        {
            if (payload == null) payload = Array.Empty<byte>();
            if (payload.Length > MaxPayloadBytes)
            {
                Debug.LogWarning($"[UdpTransport] Payload too large ({payload.Length} bytes). Split it or lower payload size.");
                return;
            }

            uint seq;
            uint ack;
            uint ackBits;
            lock (c.sync)
            {
                seq = c.sendNext++;
                ack = c.lastReceivedSeq;
                ackBits = c.ackBits;
            }
            var packetBytes = EncodePacket(PacketType.DataReliable, seq, ack, ackBits, payload);
            var sp = new SentPacket
            {
                seq = seq,
                bytes = packetBytes,
                lastSentTime = Now(),
                attempts = 1
            };
            lock (c.sync) c.sent[seq] = sp;
            udp.Send(packetBytes, packetBytes.Length, c.endpoint);
        }

        void TickConnection(UdpClient udp, Connection c, float now, bool isServer)
        {
            if (udp == null || c == null) return;

            // resend timed-out packets
            List<SentPacket> resend = null;
            bool hardFail = false;
            lock (c.sync)
            {
                if (c.sent.Count > 0)
                {
                    List<uint> toDrop = null;
                    foreach (var kv in c.sent)
                    {
                        var sp = kv.Value;
                        float rto = ResendIntervalSec * Mathf.Pow(1.25f, Mathf.Max(0, sp.attempts - 1));
                        if (now - sp.lastSentTime >= rto)
                        {
                            sp.attempts++;
                            if (sp.attempts > MaxResendAttempts)
                            {
                                (toDrop ??= new List<uint>()).Add(sp.seq);
                                continue;
                            }
                            sp.lastSentTime = now;
                            (resend ??= new List<SentPacket>()).Add(sp);
                        }
                    }
                    if (toDrop != null)
                    {
                        foreach (var s in toDrop)
                            c.sent.Remove(s);

                        if (toDrop.Count > 0 && now - c.lastHeardTime > DisconnectTimeoutSec * 0.5f)
                            hardFail = true;
                    }
                }
            }
            if (resend != null)
            {
                foreach (var sp in resend)
                    udp.Send(sp.bytes, sp.bytes.Length, c.endpoint);
            }
            if (hardFail)
            {
                if (isServer) DropServerConnection(c.id);
                else OnDisconnected?.Invoke(0);
            }

            // opportunistic ack-only ping when idle: keep NAT open and flush ACKs
            // (send a tiny unreliable packet with ack fields)
            float lastHeard;
            uint ack;
            uint ackBits;
            lock (c.sync)
            {
                lastHeard = c.lastHeardTime;
                ack = c.lastReceivedSeq;
                ackBits = c.ackBits;
            }
            if (now - lastHeard < DisconnectTimeoutSec && now - lastHeard > 1.0f)
            {
                var ping = EncodePacket(PacketType.Ping, 0, ack, ackBits, Array.Empty<byte>());
                udp.Send(ping, ping.Length, c.endpoint);
            }
        }

        void ProcessReliableIncoming(Connection c, int connId, uint seq, byte[] payload)//
        {
            // update ack state for what we've received
            UpdateReceiveAcks(c, seq);

            // in-order delivery with buffering
            if (seq < c.recvNext)
            {
                // duplicate/old
                return;
            }

            if (seq >= c.recvNext + ReorderWindow)
            {
                // too far ahead; drop to avoid memory blow
                return;
            }

            if (seq == c.recvNext)
            {
                OnData?.Invoke(connId, payload);
                c.recvNext++;

                // flush buffered consecutive packets
                while (c.recvBuffer.TryGetValue(c.recvNext, out var nextPayload))
                {
                    c.recvBuffer.Remove(c.recvNext);
                    OnData?.Invoke(connId, nextPayload);
                    c.recvNext++;
                }
            }
            else
            {
                // out-of-order; buffer if not already
                if (!c.recvBuffer.ContainsKey(seq))
                    c.recvBuffer[seq] = payload;
            }
        }

        void ProcessAcks(Connection c, uint ack, uint ackBits)
        {
            if (c == null) return;

            // ack == latest received by remote (from our perspective)
            if (ack != 0)
                c.sent.Remove(ack);

            // ackBits: bit0 => ack-1 ... bit31 => ack-32
            for (int i = 0; i < 32; i++)
            {
                if (((ackBits >> i) & 1u) != 0)
                {
                    uint s = ack - (uint)(i + 1);
                    if (s != 0)
                        c.sent.Remove(s);
                }
            }
        }

        void UpdateReceiveAcks(Connection c, uint seq)
        {
            if (seq == 0) return;

            if (seq > c.lastReceivedSeq)
            {
                uint shift = seq - c.lastReceivedSeq;
                if (shift >= 32) c.ackBits = 0;
                else c.ackBits = (c.ackBits << (int)shift);

                // mark previous lastReceivedSeq as received (it becomes ack-1 => bit0)
                if (c.lastReceivedSeq != 0)
                    c.ackBits |= 1u << (int)(shift - 1);

                c.lastReceivedSeq = seq;
            }
            else
            {
                uint diff = c.lastReceivedSeq - seq;
                if (diff >= 1 && diff <= 32)
                {
                    int bit = (int)(diff - 1);
                    c.ackBits |= 1u << bit;
                }
            }
        }

        // ---- Packet encoding/decoding ----
        // Header: [type:1][seq:4][ack:4][ackBits:4][len:2][payload:len]
        static byte[] EncodePacket(PacketType type, uint seq, uint ack, uint ackBits, byte[] payload)
        {
            int len = payload?.Length ?? 0;
            byte[] bytes = new byte[1 + 4 + 4 + 4 + 2 + len];
            int p = 0;
            bytes[p++] = (byte)type;
            WriteU32(bytes, ref p, seq);
            WriteU32(bytes, ref p, ack);
            WriteU32(bytes, ref p, ackBits);
            WriteU16(bytes, ref p, (ushort)len);
            if (len > 0) Buffer.BlockCopy(payload, 0, bytes, p, len);
            return bytes;
        }

        static bool TryDecodePacket(byte[] bytes, out PacketType type, out uint seq, out uint ack, out uint ackBits, out byte[] payload)
        {
            type = 0; seq = 0; ack = 0; ackBits = 0; payload = null;
            if (bytes == null || bytes.Length < 1 + 4 + 4 + 4 + 2) return false;

            int p = 0;
            type = (PacketType)bytes[p++];
            seq = ReadU32(bytes, ref p);
            ack = ReadU32(bytes, ref p);
            ackBits = ReadU32(bytes, ref p);
            ushort len = ReadU16(bytes, ref p);
            if (p + len > bytes.Length) return false;
            payload = new byte[len];
            if (len > 0) Buffer.BlockCopy(bytes, p, payload, 0, len);
            return true;
        }

        static void WriteU16(byte[] b, ref int p, ushort v)
        {
            b[p++] = (byte)(v & 0xFF);
            b[p++] = (byte)((v >> 8) & 0xFF);
        }

        static ushort ReadU16(byte[] b, ref int p)
        {
            ushort v = (ushort)(b[p] | (b[p + 1] << 8));
            p += 2;
            return v;
        }

        static void WriteU32(byte[] b, ref int p, uint v)
        {
            b[p++] = (byte)(v & 0xFF);
            b[p++] = (byte)((v >> 8) & 0xFF);
            b[p++] = (byte)((v >> 16) & 0xFF);
            b[p++] = (byte)((v >> 24) & 0xFF);
        }

        static uint ReadU32(byte[] b, ref int p)
        {
            uint v = (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));
            p += 4;
            return v;
        }

        static string EndpointKey(IPEndPoint ep) => ep.Address + ":" + ep.Port;
        static bool EndpointsMatch(IPEndPoint a, IPEndPoint b) => a.Address.Equals(b.Address) && a.Port == b.Port;
    }
    #endregion
}