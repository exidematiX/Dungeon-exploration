using UnityEngine;

namespace MirrorLite
{
    public abstract class NetworkBehaviour : MonoBehaviour
    {
        public NetworkIdentity netIdentity;
        protected virtual void Awake() { netIdentity = GetComponent<NetworkIdentity>(); }

        public virtual void OnSerializeAll(NetworkWriter writer) { }
        public virtual void OnDeserializeAll(NetworkReader reader) { }
    }
}