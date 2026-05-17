using System.Collections.Generic;
using UnityEngine;

namespace MirrorLite
{
    // Attach to the player prefab.
    public class PlayerNet : NetworkBehaviour
    {
        public SyncVar<Vector3> position = new SyncVar<Vector3>(Vector3.zero);
        public float moveSpeed = 4.5f;
        public float predictionCorrectionSpeed = 18f;
        public float remoteInterpolationSpeed = 14f;

        Vector3 velocity;
        Vector3 serverPosition;
        Vector3 serverVelocity;
        ushort lastInputFrame;
        float inputAccumulator;
        bool registered;
        readonly List<FrameInput> recentInputs = new List<FrameInput>();

        public bool IsLocalPlayer => netIdentity != null && netIdentity.isLocalPlayer;

        void OnEnable() => TryRegister();

        void OnDisable()
        {
            if (registered && FrameSyncWorld.Instance != null)
                FrameSyncWorld.Instance.UnregisterPlayer(this);
            registered = false;
        }

        void Update()
        {
            TryRegister();

            if (!Application.isPlaying || netIdentity == null)
                return;

            if (IsLocalPlayer)
            {
                SendPredictedInput();
                SmoothPredictionError();
            }
            else if (!netIdentity.isServerOwned)
            {
                transform.position = Vector3.Lerp(transform.position, serverPosition, Time.deltaTime * remoteInterpolationSpeed);
            }
        }

        public void RefreshNetworkIdentity()
        {
            netIdentity = GetComponent<NetworkIdentity>();
            position.Value = transform.position;
            serverPosition = transform.position;
            TryRegister();
        }

        public void Simulate(FrameInput input, float dt, bool applyTransform)
        {
            var move = Vector2.ClampMagnitude(input.move, 1f);
            velocity = new Vector3(move.x, 0f, move.y) * moveSpeed;

            if (applyTransform)
            {
                transform.position += velocity * dt;
                if (velocity.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);

                position.Value = transform.position;
            }
        }

        public PlayerSnapshot MakeSnapshot(ushort frame)
        {
            return new PlayerSnapshot
            {
                netId = netIdentity != null ? netIdentity.netId : 0,
                frame = frame,
                position = transform.position,
                velocity = velocity
            };
        }

        public void ApplyServerSnapshot(PlayerSnapshot snapshot)
        {
            serverPosition = snapshot.position;
            serverVelocity = snapshot.velocity;

            if (IsLocalPlayer)
            {
                var error = snapshot.position - transform.position;
                if (error.sqrMagnitude > 4f)
                    transform.position = snapshot.position;
            }
            else
            {
                position.Value = snapshot.position;
            }
        }

        public override void OnSerializeAll(NetworkWriter writer)
        {
            writer.WriteFloat(position.Value.x);
            writer.WriteFloat(position.Value.y);
            writer.WriteFloat(position.Value.z);
            position.Dirty = false;
        }

        public override void OnDeserializeAll(NetworkReader reader)
        {
            var x = reader.ReadFloat();
            var y = reader.ReadFloat();
            var z = reader.ReadFloat();
            position.Value = new Vector3(x, y, z);
            transform.position = position.Value;
        }

        void SendPredictedInput()
        {
            var world = FrameSyncWorld.Instance;
            if (world == null || NetworkManager.Instance == null || netIdentity.netId == 0)
                return;

            inputAccumulator += Time.deltaTime;
            if (inputAccumulator < world.DeltaTime)
                return;

            inputAccumulator -= world.DeltaTime;
            var input = new FrameInput
            {
                netId = netIdentity.netId,
                frame = ++lastInputFrame,
                move = ReadMoveInput(),
                interact = Input.GetKey(KeyCode.E)
            };

            Simulate(input, world.DeltaTime, true);
            world.QueueInput(input);
            recentInputs.Add(input);

            int redundancy = Mathf.Max(1, world.inputRedundancy);
            while (recentInputs.Count > redundancy)
                recentInputs.RemoveAt(0);

            NetworkManager.Instance.ClientSendInput(recentInputs);
        }

        void SmoothPredictionError()
        {
            if (serverPosition == Vector3.zero && position.Value == Vector3.zero)
                return;

            var error = serverPosition - transform.position;
            if (error.sqrMagnitude < 0.0004f)
                return;

            transform.position += error * Mathf.Clamp01(Time.deltaTime * predictionCorrectionSpeed);
            position.Value = transform.position;
        }

        Vector2 ReadMoveInput()
        {
            Vector2 move = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move.y -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move.x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move.x -= 1f;
            return Vector2.ClampMagnitude(move, 1f);
        }

        void TryRegister()
        {
            if (registered || FrameSyncWorld.Instance == null)
                return;

            if (netIdentity == null)
                netIdentity = GetComponent<NetworkIdentity>();

            if (netIdentity == null || netIdentity.netId == 0)
                return;

            FrameSyncWorld.Instance.RegisterPlayer(this);
            registered = true;
        }
    }
}
