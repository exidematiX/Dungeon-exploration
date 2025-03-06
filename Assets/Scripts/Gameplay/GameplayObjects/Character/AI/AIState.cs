using System;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character.AI
{

    /// <summary>
    /// 所有 AIStates 的基类
    /// </summary>
    public abstract class AIState
    {
        /// <summary>
        /// 表明该状态是否认为它可以成为/继续成为活动状态。
        /// </summary>
        /// <returns></returns>
        public abstract bool IsEligible();

        /// <summary>
        /// 每次此状态变为活动状态时调用一次。
        /// （仅当 IsEligible() 返回此状态的 true 时才会发生这种情况）
        /// </summary>
        public abstract void Initialize();

        /// <summary>
        /// 当此为活动状态时，每帧调用一次。Initialize() 将具有
        /// already been called prior to Update() being called
        /// </summary>在调用 Update() 之前已经被调用
        public abstract void Update();

    }
}
