using System;
using System.Collections.Generic;
using UnityEngine;

namespace MirrorLite
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        public GameObject playerPrefab;
        public int serverPort = 7777;
        public string serverIp = "127.0.0.1";
        public Transform[] spawnPoints;
        public bool autoSpawnPlayerOnConnect = true;
        public int snapshotSendRate = 15;
        public int syncVarSendRate = 10;

        KCPTransport transport = new();
        Dictionary<uint, GameObject> spawned = new();
        Dictionary<int, uint> playerByConnection = new();
        Queue<Action> mainThreadQueue = new Queue<Action>();
        uint nextNetId = 1;
        int localConnectionId = -1;
        float snapshotTimer;
        float syncVarTimer;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            MessageDispatcher.Register(MsgType.Spawn, OnMsgSpawn);
            MessageDispatcher.Register(MsgType.RpcCall, OnMsgRpc);
            MessageDispatcher.Register(MsgType.SyncVars, OnMsgSync);
            MessageDispatcher.Register(MsgType.Welcome, OnMsgWelcome);
            MessageDispatcher.Register(MsgType.ClientHello, OnMsgClientHello);
            MessageDispatcher.Register(MsgType.PlayerInput, OnMsgPlayerInput);
            MessageDispatcher.Register(MsgType.FrameSnapshot, OnMsgFrameSnapshot);
        }

        public void StartServer()
        {
            transport.OnData += OnTransportData;
            transport.OnConnected += OnTransportConnected;
            transport.OnDisconnected += OnTransportDisconnected;
            transport.StartServer(serverPort);
            EnsureFrameWorld(serverMode: true);
            Debug.Log("[NM] Server started");
        }

        public uint Spawn(GameObject prefab, Vector3 pos, Quaternion rot, int ownerConnectionId = -1)
        {
            var id = nextNetId++;
            var go = Instantiate(prefab, pos, rot);
            var nid = go.GetComponent<NetworkIdentity>() ?? go.AddComponent<NetworkIdentity>();
            nid.netId = id;
            nid.ownerConnectionId = ownerConnectionId;
            nid.isServerOwned = true;
            nid.isLocalPlayer = false;
            spawned[id] = go;

            var player = go.GetComponent<PlayerNet>();
            if (player != null)
                player.RefreshNetworkIdentity();

            var packet = MessageDispatcher.Pack(MsgType.Spawn, w =>
            {
                w.WriteUInt(id);
                w.WriteInt(ownerConnectionId);
                w.WriteString(prefab.name);
                w.WriteFloat(pos.x); w.WriteFloat(pos.y); w.WriteFloat(pos.z);
                w.WriteFloat(rot.x); w.WriteFloat(rot.y); w.WriteFloat(rot.z); w.WriteFloat(rot.w);
            });

            transport.ServerBroadcastReliable(packet);
            return id;
        }

        public void StartClient()
        {
            transport.OnData += OnTransportData;
            transport.OnDisconnected += OnTransportDisconnected;
            transport.StartClient(serverIp, serverPort);
            EnsureFrameWorld(serverMode: false);
            ClientSendReliable(MsgType.ClientHello, null);
            Debug.Log("[NM] Client started");
        }

        void Update()
        {
            transport.Tick();
            DrainMainThreadQueue();
            SendServerSnapshots();
            SendDirtySyncVars();
        }

        void OnTransportData(int conn, byte[] data) =>
            EnqueueMainThread(() => MessageDispatcher.Handle(conn, data));

        void OnTransportConnected(int connId)
        {
            EnqueueMainThread(() =>
            {
                var welcome = MessageDispatcher.Pack(MsgType.Welcome, w => w.WriteInt(connId));
                transport.ServerSendReliable(connId, welcome);
            });
        }

        void OnTransportDisconnected(int connId)
        {
            EnqueueMainThread(() =>
            {
                if (!playerByConnection.TryGetValue(connId, out var netId))
                    return;

                playerByConnection.Remove(connId);
                if (!spawned.TryGetValue(netId, out var go))
                    return;

                spawned.Remove(netId);
                Destroy(go);
            });
        }

        void OnMsgSpawn(int connId, NetworkReader reader)
        {
            var id = reader.ReadUInt();
            var ownerConnectionId = reader.ReadInt();
            var prefabName = reader.ReadString();
            var px = reader.ReadFloat(); var py = reader.ReadFloat(); var pz = reader.ReadFloat();
            var rx = reader.ReadFloat(); var ry = reader.ReadFloat(); var rz = reader.ReadFloat(); var rw = reader.ReadFloat();
            Debug.Log($"[NM] Spawn recv: {prefabName} id={id}");

            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Debug.LogWarning("Prefab not found in Resources: " + prefabName);
                return;
            }

            var go = Instantiate(prefab, new Vector3(px, py, pz), new Quaternion(rx, ry, rz, rw));
            var nid = go.GetComponent<NetworkIdentity>() ?? go.AddComponent<NetworkIdentity>();
            nid.netId = id;
            nid.ownerConnectionId = ownerConnectionId;
            nid.isServerOwned = false;
            nid.isLocalPlayer = ownerConnectionId == localConnectionId;
            spawned[id] = go;

            var player = go.GetComponent<PlayerNet>();
            if (player != null)
                player.RefreshNetworkIdentity();
        }

        void OnMsgRpc(int connId, NetworkReader reader)
        {
            var netId = reader.ReadUInt();
            var method = reader.ReadString();
            if (!RpcRegistry.InvokeClientRpc(netId, method, reader) &&
                !RpcRegistry.InvokeServerRpc(netId, method, reader))
            {
                Debug.LogWarning("[NM] Unknown RPC target: " + netId + ":" + method);
            }
        }

        void OnMsgSync(int connId, NetworkReader reader)
        {
            var netId = reader.ReadUInt();
            if (!spawned.TryGetValue(netId, out var go)) return;
            var behaviours = go.GetComponents<NetworkBehaviour>();
            foreach (var b in behaviours)
                b.OnDeserializeAll(reader);
        }

        void OnMsgWelcome(int connId, NetworkReader reader)
        {
            localConnectionId = reader.ReadInt();
            Debug.Log("[NM] Welcome, local connection id=" + localConnectionId);
        }

        void OnMsgClientHello(int connId, NetworkReader reader)
        {
            if (!autoSpawnPlayerOnConnect || playerPrefab == null || playerByConnection.ContainsKey(connId))
                return;

            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                var p = spawnPoints[playerByConnection.Count % spawnPoints.Length];
                if (p != null)
                {
                    pos = p.position;
                    rot = p.rotation;
                }
            }

            var netId = Spawn(playerPrefab, pos, rot, connId);
            playerByConnection[connId] = netId;
        }

        void OnMsgPlayerInput(int connId, NetworkReader reader)
        {
            var count = reader.ReadByte();
            for (int i = 0; i < count; i++)
            {
                var input = FrameInput.Read(reader);
                if (!playerByConnection.TryGetValue(connId, out var ownedNetId) || ownedNetId != input.netId)
                    continue;

                FrameSyncWorld.Instance?.QueueInput(input);
            }
        }

        void OnMsgFrameSnapshot(int connId, NetworkReader reader)
        {
            var snapshotFrame = reader.ReadUShort();
            var count = reader.ReadUShort();
            var snapshots = new List<PlayerSnapshot>(count);
            for (int i = 0; i < count; i++)
                snapshots.Add(PlayerSnapshot.Read(reader));

            FrameSyncWorld.Instance?.ApplySnapshots(snapshotFrame, snapshots);
        }

        public void ServerInvokeClientRpc(uint netId, string method, Action<NetworkWriter> writeArgs)
        {
            var packet = MessageDispatcher.Pack(MsgType.RpcCall, w =>
            {
                w.WriteUInt(netId);
                w.WriteString(method);
                writeArgs?.Invoke(w);
            });
            transport.ServerBroadcastReliable(packet);
        }

        public void ClientSendInput(List<FrameInput> inputs)
        {
            if (inputs == null || inputs.Count == 0)
                return;

            var cappedCount = Mathf.Min(inputs.Count, byte.MaxValue);
            ClientSendUnreliable(MsgType.PlayerInput, w =>
            {
                w.WriteByte((byte)cappedCount);
                for (int i = 0; i < cappedCount; i++)
                    inputs[i].Write(w);
            });
        }

        public void ClientSendRpc(string method, Action<NetworkWriter> writeArgs)
        {
            var packet = MessageDispatcher.Pack(MsgType.RpcCall, w =>
            {
                w.WriteUInt(1);
                w.WriteString(method);
                writeArgs?.Invoke(w);
            });
            transport.ClientSendReliable(packet);
        }

        public bool TryGetSpawned(uint netId, out GameObject go) => spawned.TryGetValue(netId, out go);

        public void ClientSendReliable(MsgType type, Action<NetworkWriter> fill)
        {
            var packet = MessageDispatcher.Pack(type, w => fill?.Invoke(w));
            transport.ClientSendReliable(packet);
        }

        public void ClientSendUnreliable(MsgType type, Action<NetworkWriter> fill)
        {
            var packet = MessageDispatcher.Pack(type, w => fill?.Invoke(w));
            transport.ClientSendUnreliable(packet);
        }

        void SendServerSnapshots()
        {
            var world = FrameSyncWorld.Instance;
            if (world == null || !world.serverAuthoritative || snapshotSendRate <= 0)
                return;

            snapshotTimer += Time.deltaTime;
            float interval = 1f / snapshotSendRate;
            if (snapshotTimer < interval)
                return;

            snapshotTimer -= interval;
            var snapshots = world.BuildSnapshots();
            if (snapshots.Count == 0)
                return;

            var packet = MessageDispatcher.Pack(MsgType.FrameSnapshot, w =>
            {
                w.WriteUShort(world.CurrentFrame);
                w.WriteUShort((ushort)snapshots.Count);
                foreach (var snapshot in snapshots)
                    snapshot.Write(w);
            });
            transport.ServerBroadcastReliable(packet);
        }

        void SendDirtySyncVars()
        {
            var world = FrameSyncWorld.Instance;
            if (world == null || !world.serverAuthoritative || syncVarSendRate <= 0)
                return;

            syncVarTimer += Time.deltaTime;
            float interval = 1f / syncVarSendRate;
            if (syncVarTimer < interval)
                return;

            syncVarTimer -= interval;
            foreach (var kv in spawned)
            {
                var go = kv.Value;
                if (go == null)
                    continue;

                var behaviours = go.GetComponents<NetworkBehaviour>();
                if (behaviours == null || behaviours.Length == 0)
                    continue;

                var packet = MessageDispatcher.Pack(MsgType.SyncVars, w =>
                {
                    w.WriteUInt(kv.Key);
                    foreach (var behaviour in behaviours)
                        behaviour.OnSerializeAll(w);
                });
                transport.ServerBroadcastReliable(packet);
            }
        }

        void EnsureFrameWorld(bool serverMode)
        {
            var world = FrameSyncWorld.Instance;
            if (world == null)
                world = gameObject.AddComponent<FrameSyncWorld>();

            world.serverAuthoritative = serverMode;
        }

        void EnqueueMainThread(Action action)//将动作添加到主线程队列
        {
            lock (mainThreadQueue)
                mainThreadQueue.Enqueue(action);
        }

        void DrainMainThreadQueue()//清空主线程队列
        {
            while (true)
            {
                Action action = null;
                lock (mainThreadQueue)
                {
                    if (mainThreadQueue.Count > 0)
                        action = mainThreadQueue.Dequeue();
                }

                if (action == null)
                    break;

                action();
            }
        }

        void OnApplicationQuit() => transport.Stop();
    }
}
