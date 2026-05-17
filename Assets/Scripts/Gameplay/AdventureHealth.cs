using MirrorLite;
using UnityEngine;

namespace MirrorLite.Gameplay
{
    public class AdventureHealth : NetworkBehaviour
    {
        public int maxHealth = 5;
        public SyncVar<int> health = new SyncVar<int>(5);
        public bool destroyOnDeath;

        void Start()
        {
            if (health.Value <= 0)
                health.Value = maxHealth;
        }

        public void TakeDamage(int amount)
        {
            if (!CanApplyGameplay())
                return;

            health.Value = Mathf.Max(0, health.Value - Mathf.Max(0, amount));
            health.Dirty = true;

            if (health.Value == 0 && destroyOnDeath)
                Destroy(gameObject);
        }

        public void Heal(int amount)
        {
            if (!CanApplyGameplay())
                return;

            health.Value = Mathf.Min(maxHealth, health.Value + Mathf.Max(0, amount));
            health.Dirty = true;
        }

        public override void OnSerializeAll(NetworkWriter writer)
        {
            writer.WriteInt(maxHealth);
            writer.WriteInt(health.Value);
            health.Dirty = false;
        }

        public override void OnDeserializeAll(NetworkReader reader)
        {
            maxHealth = reader.ReadInt();
            health.Value = reader.ReadInt();
        }

        bool CanApplyGameplay()
        {
            return netIdentity == null || netIdentity.isServerOwned;
        }
    }
}
