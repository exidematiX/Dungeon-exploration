using System;
using System.Collections.Generic;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.Actions;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character.AI
{
    /// <summary>
    /// 处理敌人 AI。包含处理部分细节的 AIStateLogics，
    /// 并具有由这些 AIStateLogics 调用的各种实用函数
    /// </summary>
    public class AIBrain
    {
        private enum AIStateType
        {
            ATTACK,
            //WANDER,
            IDLE,
        }

        static readonly AIStateType[] k_AIStates = (AIStateType[])Enum.GetValues(typeof(AIStateType));

        private ServerCharacter m_ServerCharacter;
        private ServerActionPlayer m_ServerActionPlayer;
        private AIStateType m_CurrentState;
        private Dictionary<AIStateType, AIState> m_Logics;
        private List<ServerCharacter> m_HatedEnemies;

        /// <summary>
        /// 如果我们是由生成器创建的，则生成器可能会覆盖我们的检测半径
        /// -1 是一个标记值，表示“无覆盖”
        /// </summary>
        private float m_DetectRangeOverride = -1;

        public AIBrain(ServerCharacter me, ServerActionPlayer myServerActionPlayer)
        {
            m_ServerCharacter = me;
            m_ServerActionPlayer = myServerActionPlayer;

            m_Logics = new Dictionary<AIStateType, AIState>
            {
                [AIStateType.IDLE] = new IdleAIState(this),
                //[ AIStateType.WANDER ] = new WanderAIState(this), // not written yet
                [AIStateType.ATTACK] = new AttackAIState(this, m_ServerActionPlayer),
            };
            m_HatedEnemies = new List<ServerCharacter>();
            m_CurrentState = AIStateType.IDLE;
        }

        /// <summary>
        /// 应该由 AIBrain 的所有者在每次 Update() 时调用
        /// </summary>
        public void Update()
        {
            AIStateType newState = FindBestEligibleAIState();
            if (m_CurrentState != newState)
            {
                m_Logics[newState].Initialize();
            }
            m_CurrentState = newState;
            m_Logics[m_CurrentState].Update();
        }

        /// <summary>
        /// 当我们获得一些生命值时会呼叫。正生命值表示治疗，负生命值表示伤害。
        /// </summary>
        /// <param name="inflicter">伤害或治愈我们的人。可能为 null。 </param>
        /// <param name="amount">受到的生命值。负数表示受到的伤害。 </param>
        public void ReceiveHP(ServerCharacter inflicter, int amount)
        {
            if (inflicter != null && amount < 0)
            {
                Hate(inflicter);
            }
        }

        private AIStateType FindBestEligibleAIState()
        {
            // 目前我们假设人工智能状态按适当性排序，
            // 当有更多州时，这可能毫无意义
            foreach (AIStateType aiStateType in k_AIStates)
            {
                if (m_Logics[aiStateType].IsEligible())
                {
                    return aiStateType;
                }
            }

            Debug.LogError("No AI states are valid!?!");
            return AIStateType.IDLE;
        }

        /// <summary>
        /// 如果从现在开始谋杀这个角色对我们来说是合适的，则返回 true！
        /// </summary>
        public bool IsAppropriateFoe(ServerCharacter potentialFoe)
        {
            if (potentialFoe == null ||
                potentialFoe.IsNpc ||
                potentialFoe.LifeState != LifeState.Alive ||
                potentialFoe.IsStealthy.Value)
            {
                return false;
            }

            // 另外，我们可以使用 NavMesh.Raycast() 来查看我们是否有敌人的视线？
            return true;
        }

        /// <summary>
        /// 通知 AIBrain 我们应该将该角色视为敌人。
        /// </summary>
        /// <param name="character"></param>
        public void Hate(ServerCharacter character)
        {
            if (!m_HatedEnemies.Contains(character))
            {
                m_HatedEnemies.Add(character);
            }
        }

        /// <summary>
        /// 返回仇恨敌人的原始列表 - 作为只读处理！
        /// </summary>
        public List<ServerCharacter> GetHatedEnemies()
        {
            // 首先我们清理列表——删除任何已经消失（变为空）、死亡等的敌人。
            for (int i = m_HatedEnemies.Count - 1; i >= 0; i--)
            {
                if (!IsAppropriateFoe(m_HatedEnemies[i]))
                {
                    m_HatedEnemies.RemoveAt(i);
                }
            }
            return m_HatedEnemies;
        }

        /// <summary>
        /// 检索关于我们的信息。视为只读！
        /// </summary>
        /// <returns></returns>
        public ServerCharacter GetMyServerCharacter()
        {
            return m_ServerCharacter;
        }

        /// <summary>
        /// 便捷的获取器，返回与该生物相关的 CharacterData。
        /// </summary>
        public CharacterClass CharacterData
        {
            get
            {
                return GameDataSource.Instance.CharacterDataByType[m_ServerCharacter.CharacterType];
            }
        }

        /// <summary>
        /// 该角色可以探测到敌人的范围，以米为单位。
        /// This is usually the same value as is indicated by our game data, but it
        /// 这通常与我们的游戏数据所指示的值相同，但可以动态覆盖。
        /// </summary>
        public float DetectRange
        {
            get
            {
                return (m_DetectRangeOverride == -1) ? CharacterData.DetectRange : m_DetectRangeOverride;
            }

            set
            {
                m_DetectRangeOverride = value;
            }
        }

    }
}
