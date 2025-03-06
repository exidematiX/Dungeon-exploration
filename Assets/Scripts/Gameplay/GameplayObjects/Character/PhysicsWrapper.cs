using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character
{
    /// <summary>
    /// 用于直接引用与物理相关的组件的包装类。
    /// PhysicsWrapper 的每个实例都注册到一个静态字典，由 NetworkObject 的 ID 索引。
    /// </summary>
    /// <remarks>
    /// The root GameObject of PCs & NPCs is not the object which will move through the world, so other classes will
    /// PC 和 NPC 的根游戏对象不是会在世界中移动的对象，因此其他类需要快速引用 PC/NPC 在游戏中的位置。
    /// </remarks>
    public class PhysicsWrapper : NetworkBehaviour
    {
        static Dictionary<ulong, PhysicsWrapper> m_PhysicsWrappers = new Dictionary<ulong, PhysicsWrapper>();

        [SerializeField]
        Transform m_Transform;

        public Transform Transform => m_Transform;

        [SerializeField]
        Collider m_DamageCollider;

        public Collider DamageCollider => m_DamageCollider;

        ulong m_NetworkObjectID;

        public override void OnNetworkSpawn()
        {
            m_PhysicsWrappers.Add(NetworkObjectId, this);

            m_NetworkObjectID = NetworkObjectId;
        }

        public override void OnNetworkDespawn()
        {
            RemovePhysicsWrapper();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            RemovePhysicsWrapper();
        }

        void RemovePhysicsWrapper()
        {
            m_PhysicsWrappers.Remove(m_NetworkObjectID);
        }

        public static bool TryGetPhysicsWrapper(ulong networkObjectID, out PhysicsWrapper physicsWrapper)
        {
            return m_PhysicsWrappers.TryGetValue(networkObjectID, out physicsWrapper);
        }
    }
}
