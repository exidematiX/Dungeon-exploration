using UnityEngine;

namespace MirrorLite
{
    public class NetworkIdentity : MonoBehaviour
    {
        public uint netId;
        public int ownerConnectionId = -1;
        public bool isLocalPlayer;
        public bool isServerOwned = false;
        public NetworkBehaviour[] behaviours;

        void Awake() => behaviours = GetComponents<NetworkBehaviour>();
    }
}
