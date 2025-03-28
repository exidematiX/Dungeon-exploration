using System;
using Unity.BossRoom.CameraUtils;
using Unity.BossRoom.Gameplay.UserInput;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.Actions;
using Unity.BossRoom.Utils;
using Unity.Netcode;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character
{
    /// <summary>
    /// <see cref="ClientCharacter"/> 负责根据服务器发送的状态信息在客户端的屏幕上显示字符。
    /// </summary>
    public class ClientCharacter : NetworkBehaviour
    {
        [SerializeField]
        Animator m_ClientVisualsAnimator;

        [SerializeField]
        VisualizationConfiguration m_VisualizationConfiguration;

        /// <summary>
        /// Returns a reference to the active Animator for this visualization
        /// </summary>
        public Animator OurAnimator => m_ClientVisualsAnimator;

        /// <summary>
        /// Returns the targeting-reticule prefab for this character visualization
        /// </summary>
        public GameObject TargetReticulePrefab => m_VisualizationConfiguration.TargetReticule;

        /// <summary>
        /// Returns the Material to plug into the reticule when the selected entity is hostile
        /// </summary>
        public Material ReticuleHostileMat => m_VisualizationConfiguration.ReticuleHostileMat;

        /// <summary>
        /// Returns the Material to plug into the reticule when the selected entity is friendly
        /// </summary>
        public Material ReticuleFriendlyMat => m_VisualizationConfiguration.ReticuleFriendlyMat;

        CharacterSwap m_CharacterSwapper;

        public CharacterSwap CharacterSwap => m_CharacterSwapper;

        public bool CanPerformActions => m_ServerCharacter.CanPerformActions;

        ServerCharacter m_ServerCharacter;

        public ServerCharacter serverCharacter => m_ServerCharacter;

        ClientActionPlayer m_ClientActionViz;

        PositionLerper m_PositionLerper;

        RotationLerper m_RotationLerper;

        // 该值对于位置和旋转插值都足够；每个插值都可以有一个常数值
        const float k_LerpTime = 0.08f;

        Vector3 m_LerpedPosition;

        Quaternion m_LerpedRotation;

        float m_CurrentSpeed;

        /// <summary>
        /// /// 服务器到客户端 RPC 将此操作广播给所有客户端。
        /// </summary>
        /// <param name="data"> 关于要采取什么行动及其相关细节的数据 </param>
        [Rpc(SendTo.ClientsAndHost)]
        public void ClientPlayActionRpc(ActionRequestData data)
        {
            ActionRequestData data1 = data;
            m_ClientActionViz.PlayAction(ref data1);
        }

        /// <summary>
        /// This RPC is invoked on the client when the active action FXs need to be cancelled (e.g. when the character has been stunned)
        /// </summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void ClientCancelAllActionsRpc()
        {
            m_ClientActionViz.CancelAllActions();
        }

        /// <summary>
        /// This RPC is invoked on the client when active action FXs of a certain type need to be cancelled (e.g. when the Stealth action ends)
        /// </summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void ClientCancelActionsByPrototypeIDRpc(ActionID actionPrototypeID)
        {
            m_ClientActionViz.CancelAllActionsWithSamePrototypeID(actionPrototypeID);
        }

        /// <summary>
        /// Called on all clients when this character has stopped "charging up" an attack.
        /// Provides a value between 0 and 1 inclusive which indicates how "charged up" the attack ended up being.
        /// </summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void ClientStopChargingUpRpc(float percentCharged)
        {
            m_ClientActionViz.OnStoppedChargingUp(percentCharged);
        }

        void Awake()
        {
            enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsClient || transform.parent == null)
            {
                return;
            }

            enabled = true;

            m_ClientActionViz = new ClientActionPlayer(this);

            m_ServerCharacter = GetComponentInParent<ServerCharacter>();

            m_ServerCharacter.IsStealthy.OnValueChanged += OnStealthyChanged;
            m_ServerCharacter.MovementStatus.OnValueChanged += OnMovementStatusChanged;
            OnMovementStatusChanged(MovementStatus.Normal, m_ServerCharacter.MovementStatus.Value);

            // sync our visualization position & rotation to the most up to date version received from server
            transform.SetPositionAndRotation(serverCharacter.physicsWrapper.Transform.position,
                serverCharacter.physicsWrapper.Transform.rotation);
            m_LerpedPosition = transform.position;
            m_LerpedRotation = transform.rotation;

            // similarly, initialize start position and rotation for smooth lerping purposes
            m_PositionLerper = new PositionLerper(serverCharacter.physicsWrapper.Transform.position, k_LerpTime);
            m_RotationLerper = new RotationLerper(serverCharacter.physicsWrapper.Transform.rotation, k_LerpTime);

            if (!m_ServerCharacter.IsNpc)
            {
                name = "AvatarGraphics" + m_ServerCharacter.OwnerClientId;

                if (m_ServerCharacter.TryGetComponent(out ClientPlayerAvatarNetworkAnimator characterNetworkAnimator))
                {
                    m_ClientVisualsAnimator = characterNetworkAnimator.Animator;
                }

                m_CharacterSwapper = GetComponentInChildren<CharacterSwap>();

                // ...and visualize the current char-select value that we know about
                SetAppearanceSwap();

                if (m_ServerCharacter.IsOwner)
                {
                    ActionRequestData data = new ActionRequestData { ActionID = GameDataSource.Instance.GeneralTargetActionPrototype.ActionID };
                    m_ClientActionViz.PlayAction(ref data);
                    gameObject.AddComponent<CameraController>();

                    if (m_ServerCharacter.TryGetComponent(out ClientInputSender inputSender))
                    {
                        // anticipated actions will only be played on non-host, owning clients
                        if (!IsServer)
                        {
                            inputSender.ActionInputEvent += OnActionInput;
                        }
                        inputSender.ClientMoveEvent += OnMoveInput;
                    }
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (m_ServerCharacter)
            {
                m_ServerCharacter.IsStealthy.OnValueChanged -= OnStealthyChanged;

                if (m_ServerCharacter.TryGetComponent(out ClientInputSender sender))
                {
                    sender.ActionInputEvent -= OnActionInput;
                    sender.ClientMoveEvent -= OnMoveInput;
                }
            }

            enabled = false;
        }

        void OnActionInput(ActionRequestData data)
        {
            m_ClientActionViz.AnticipateAction(ref data);
        }

        void OnMoveInput(Vector3 position)
        {
            if (!IsAnimating())
            {
                OurAnimator.SetTrigger(m_VisualizationConfiguration.AnticipateMoveTriggerID);
            }
        }

        void OnStealthyChanged(bool oldValue, bool newValue)
        {
            SetAppearanceSwap();
        }

        void SetAppearanceSwap()
        {
            if (m_CharacterSwapper)
            {
                var specialMaterialMode = CharacterSwap.SpecialMaterialMode.None;
                if (m_ServerCharacter.IsStealthy.Value)
                {
                    if (m_ServerCharacter.IsOwner)
                    {
                        specialMaterialMode = CharacterSwap.SpecialMaterialMode.StealthySelf;
                    }
                    else
                    {
                        specialMaterialMode = CharacterSwap.SpecialMaterialMode.StealthyOther;
                    }
                }

                m_CharacterSwapper.SwapToModel(specialMaterialMode);
            }
        }

        /// <summary>
        /// Returns the value we should set the Animator's "Speed" variable, given current gameplay conditions.
        /// </summary>
        float GetVisualMovementSpeed(MovementStatus movementStatus)
        {
            if (m_ServerCharacter.NetLifeState.LifeState.Value != LifeState.Alive)
            {
                return m_VisualizationConfiguration.SpeedDead;
            }

            switch (movementStatus)
            {
                case MovementStatus.Idle:
                    return m_VisualizationConfiguration.SpeedIdle;
                case MovementStatus.Normal:
                    return m_VisualizationConfiguration.SpeedNormal;
                case MovementStatus.Uncontrolled:
                    return m_VisualizationConfiguration.SpeedUncontrolled;
                case MovementStatus.Slowed:
                    return m_VisualizationConfiguration.SpeedSlowed;
                case MovementStatus.Hasted:
                    return m_VisualizationConfiguration.SpeedHasted;
                case MovementStatus.Walking:
                    return m_VisualizationConfiguration.SpeedWalking;
                default:
                    throw new Exception($"Unknown MovementStatus {movementStatus}");
            }
        }

        void OnMovementStatusChanged(MovementStatus previousValue, MovementStatus newValue)
        {
            m_CurrentSpeed = GetVisualMovementSpeed(newValue);
        }

        void Update()
        {
            // On the host, Characters are translated via ServerCharacterMovement's FixedUpdate method. To ensure that
            // the game camera tracks a GameObject moving in the Update loop and therefore eliminate any camera jitter,
            // this graphics GameObject's position is smoothed over time on the host. Clients do not need to perform any
            // positional smoothing since NetworkTransform will interpolate position updates on the root GameObject.
            if (IsHost)
            {
                // Note: a cached position (m_LerpedPosition) and rotation (m_LerpedRotation) are created and used as
                // the starting point for each interpolation since the root's position and rotation are modified in
                // FixedUpdate, thus altering this transform (being a child) in the process.
                m_LerpedPosition = m_PositionLerper.LerpPosition(m_LerpedPosition,
                    serverCharacter.physicsWrapper.Transform.position);
                m_LerpedRotation = m_RotationLerper.LerpRotation(m_LerpedRotation,
                    serverCharacter.physicsWrapper.Transform.rotation);
                transform.SetPositionAndRotation(m_LerpedPosition, m_LerpedRotation);
            }

            if (m_ClientVisualsAnimator)
            {
                // set Animator variables here
                OurAnimator.SetFloat(m_VisualizationConfiguration.SpeedVariableID, m_CurrentSpeed);
            }

            m_ClientActionViz.OnUpdate();
        }

        void OnAnimEvent(string id)
        {
            //if you are trying to figure out who calls this method, it's "magic". The Unity Animation Event system takes method names as strings,
            //and calls a method of the same name on a component on the same GameObject as the Animator. See the "attack1" Animation Clip as one
            //example of where this is configured.

            m_ClientActionViz.OnAnimEvent(id);
        }

        public bool IsAnimating()
        {
            if (OurAnimator.GetFloat(m_VisualizationConfiguration.SpeedVariableID) > 0.0) { return true; }

            for (int i = 0; i < OurAnimator.layerCount; i++)
            {
                if (OurAnimator.GetCurrentAnimatorStateInfo(i).tagHash != m_VisualizationConfiguration.BaseNodeTagID)
                {
                    //we are in an active node, not the default "nothing" node.
                    return true;
                }
            }

            return false;
        }
    }
}
