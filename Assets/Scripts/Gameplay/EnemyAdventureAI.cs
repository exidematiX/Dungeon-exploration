using MirrorLite;
using UnityEngine;
using UnityEngine.AI;

namespace MirrorLite.Gameplay
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyAdventureAI : NetworkBehaviour
    {
        public float sightRadius = 12f;
        public float attackRadius = 1.8f;
        public float attackCooldown = 1.2f;
        public float patrolRadius = 8f;
        public int damage = 1;

        NavMeshAgent agent;
        BehaviourNode tree;
        Transform target;
        Vector3 spawnPosition;
        Vector3 patrolPoint;
        float nextAttackTime;
        float nextTargetScanTime;

        protected override void Awake()
        {
            base.Awake();
            agent = GetComponent<NavMeshAgent>();
            spawnPosition = transform.position;
            patrolPoint = spawnPosition;
            BuildTree();
        }

        void Update()
        {
            if (!ShouldDriveAI())
                return;

            tree.Tick();
        }

        public override void OnSerializeAll(NetworkWriter writer)
        {
            writer.WriteFloat(transform.position.x);
            writer.WriteFloat(transform.position.y);
            writer.WriteFloat(transform.position.z);
            writer.WriteFloat(transform.rotation.x);
            writer.WriteFloat(transform.rotation.y);
            writer.WriteFloat(transform.rotation.z);
            writer.WriteFloat(transform.rotation.w);
        }

        public override void OnDeserializeAll(NetworkReader reader)
        {
            var position = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
            var rotation = new Quaternion(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
            transform.position = Vector3.Lerp(transform.position, position, Time.deltaTime * 12f);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, Time.deltaTime * 12f);
        }

        void BuildTree()
        {
            tree = new BehaviourSelector(
                new BehaviourSequence(
                    new BehaviourAction(FindTarget),
                    new BehaviourAction(AttackIfClose),
                    new BehaviourAction(ChaseTarget)
                ),
                new BehaviourAction(Patrol)
            );
        }

        BehaviourStatus FindTarget()
        {
            if (target != null && Vector3.Distance(transform.position, target.position) <= sightRadius)
                return BehaviourStatus.Success;

            if (Time.time < nextTargetScanTime)
                return BehaviourStatus.Failure;

            nextTargetScanTime = Time.time + 0.25f;
            target = null;
            float bestDistance = float.MaxValue;
            var players = FindObjectsOfType<PlayerNet>();
            foreach (var player in players)
            {
                if (player == null || player.netIdentity == null)
                    continue;

                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance <= sightRadius && distance < bestDistance)
                {
                    bestDistance = distance;
                    target = player.transform;
                }
            }

            return target != null ? BehaviourStatus.Success : BehaviourStatus.Failure;
        }

        BehaviourStatus AttackIfClose()
        {
            if (target == null)
                return BehaviourStatus.Failure;

            float distance = Vector3.Distance(transform.position, target.position);
            if (distance > attackRadius)
                return BehaviourStatus.Success;

            agent.ResetPath();
            Face(target.position);
            if (Time.time >= nextAttackTime)
            {
                nextAttackTime = Time.time + attackCooldown;
                var health = target.GetComponent<AdventureHealth>();
                if (health != null)
                    health.TakeDamage(damage);
            }

            return BehaviourStatus.Running;
        }

        BehaviourStatus ChaseTarget()
        {
            if (target == null)
                return BehaviourStatus.Failure;

            agent.SetDestination(target.position);
            return BehaviourStatus.Running;
        }

        BehaviourStatus Patrol()
        {
            if (!agent.hasPath || agent.remainingDistance <= 0.5f)
            {
                patrolPoint = FindPatrolPoint();
                agent.SetDestination(patrolPoint);
            }

            return BehaviourStatus.Running;
        }

        Vector3 FindPatrolPoint()
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 circle = Random.insideUnitCircle * patrolRadius;
                var candidate = spawnPosition + new Vector3(circle.x, 0f, circle.y);
                if (NavMesh.SamplePosition(candidate, out var hit, 2f, NavMesh.AllAreas))
                    return hit.position;
            }

            return spawnPosition;
        }

        void Face(Vector3 worldPosition)
        {
            var direction = worldPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        bool ShouldDriveAI()
        {
            return netIdentity == null || netIdentity.isServerOwned;
        }
    }
}
