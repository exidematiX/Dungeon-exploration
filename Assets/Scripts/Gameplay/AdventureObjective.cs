using System.Collections.Generic;
using MirrorLite;
using UnityEngine;

namespace MirrorLite.Gameplay
{
    public class AdventureObjective : NetworkBehaviour
    {
        public float interactRadius = 2.5f;
        public int requiredPlayers = 1;
        public SyncVar<int> progress = new SyncVar<int>(0);
        public SyncVar<int> completed = new SyncVar<int>(0);

        readonly HashSet<uint> playersInRange = new HashSet<uint>();

        void Update()
        {
            if (!CanApplyGameplay() || completed.Value != 0)
                return;

            playersInRange.Clear();
            var players = FindObjectsOfType<PlayerNet>();
            foreach (var player in players)
            {
                if (player == null || player.netIdentity == null)
                    continue;

                if (Vector3.Distance(transform.position, player.transform.position) <= interactRadius)
                    playersInRange.Add(player.netIdentity.netId);
            }

            if (playersInRange.Count >= requiredPlayers)
            {
                progress.Value += Mathf.CeilToInt(Time.deltaTime * 25f);
                progress.Dirty = true;
                if (progress.Value >= 100)
                {
                    progress.Value = 100;
                    completed.Value = 1;
                    completed.Dirty = true;
                }
            }
        }

        public override void OnSerializeAll(NetworkWriter writer)
        {
            writer.WriteInt(progress.Value);
            writer.WriteInt(completed.Value);
            progress.Dirty = false;
            completed.Dirty = false;
        }

        public override void OnDeserializeAll(NetworkReader reader)
        {
            progress.Value = reader.ReadInt();
            completed.Value = reader.ReadInt();
        }

        bool CanApplyGameplay()
        {
            return netIdentity == null || netIdentity.isServerOwned;
        }
    }
}
