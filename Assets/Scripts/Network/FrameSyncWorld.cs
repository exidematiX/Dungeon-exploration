using System;
using System.Collections.Generic;
using UnityEngine;

namespace MirrorLite
{
    public class FrameSyncWorld : MonoBehaviour
    {
        public static FrameSyncWorld Instance { get; private set; }

        [Header("Simulation")]
        public int tickRate = 30;
        public int inputRedundancy = 3;
        public bool serverAuthoritative = true;

        float accumulator;
        ushort frame;

        readonly Dictionary<uint, PlayerNet> players = new Dictionary<uint, PlayerNet>();
        readonly Dictionary<uint, SortedDictionary<ushort, FrameInput>> pendingInputs = new Dictionary<uint, SortedDictionary<ushort, FrameInput>>();

        public ushort CurrentFrame => frame;
        public float DeltaTime => 1f / Mathf.Max(1, tickRate);

        public event Action<ushort> ServerFrameAdvanced;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        void Update()
        {
            accumulator += Time.deltaTime;
            float dt = DeltaTime;
            while (accumulator >= dt)
            {
                accumulator -= dt;
                Step();
            }
        }

        public void RegisterPlayer(PlayerNet player)
        {
            if (player == null || player.netIdentity == null || player.netIdentity.netId == 0)
                return;

            players[player.netIdentity.netId] = player;
            if (!pendingInputs.ContainsKey(player.netIdentity.netId))
                pendingInputs[player.netIdentity.netId] = new SortedDictionary<ushort, FrameInput>();
        }

        public void UnregisterPlayer(PlayerNet player)
        {
            if (player == null || player.netIdentity == null)
                return;

            players.Remove(player.netIdentity.netId);
            pendingInputs.Remove(player.netIdentity.netId);
        }

        public void QueueInput(FrameInput input)
        {
            if (!pendingInputs.TryGetValue(input.netId, out var queue))
            {
                queue = new SortedDictionary<ushort, FrameInput>();
                pendingInputs[input.netId] = queue;
            }

            queue[input.frame] = input;
        }

        public List<PlayerSnapshot> BuildSnapshots()
        {
            var snapshots = new List<PlayerSnapshot>(players.Count);
            foreach (var kv in players)
            {
                snapshots.Add(kv.Value.MakeSnapshot(frame));
            }

            return snapshots;
        }

        public void ApplySnapshots(ushort snapshotFrame, List<PlayerSnapshot> snapshots)
        {
            if (snapshotFrame > frame)
                frame = snapshotFrame;

            foreach (var snapshot in snapshots)
            {
                if (players.TryGetValue(snapshot.netId, out var player))
                    player.ApplyServerSnapshot(snapshot);
            }
        }

        void Step()
        {
            frame++;

            foreach (var kv in players)
            {
                var netId = kv.Key;
                var player = kv.Value;
                FrameInput input = default;
                input.netId = netId;
                input.frame = frame;

                if (pendingInputs.TryGetValue(netId, out var queue))
                {
                    if (queue.TryGetValue(frame, out var exact))
                    {
                        input = exact;
                        queue.Remove(frame);
                    }
                    else if (queue.Count > 0)
                    {
                        foreach (var item in queue)
                        {
                            if (item.Key <= frame)
                                input = item.Value;
                        }

                        PruneOldInputs(queue, frame);
                    }
                }

                player.Simulate(input, DeltaTime, serverAuthoritative);
            }

            if (serverAuthoritative)
                ServerFrameAdvanced?.Invoke(frame);
        }

        static void PruneOldInputs(SortedDictionary<ushort, FrameInput> queue, ushort currentFrame)
        {
            List<ushort> old = null;
            foreach (var key in queue.Keys)
            {
                if (SequenceOlderOrEqual(key, currentFrame))
                    (old ??= new List<ushort>()).Add(key);
            }

            if (old == null)
                return;

            foreach (var key in old)
                queue.Remove(key);
        }

        static bool SequenceOlderOrEqual(ushort a, ushort b)
        {
            return a == b || (ushort)(b - a) < 32768;
        }
    }
}
