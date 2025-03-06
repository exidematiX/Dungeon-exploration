using UnityEngine;

namespace Unity.BossRoom.Utils
{
    /// <summary>
    /// Utility struct to linearly interpolate between two Vector3 values. Allows for flexible linear interpolations
    /// where current and target change over time.
    /// </summary>
    public struct PositionLerper
    {
        // Calculated start for the most recent interpolation
        Vector3 m_LerpStart;

        // Calculated time elapsed for the most recent interpolation
        float m_CurrentLerpTime;

        // The duration of the interpolation, in seconds
        float m_LerpTime;

        public PositionLerper(Vector3 start, float lerpTime)
        {
            m_LerpStart = start;
            m_CurrentLerpTime = 0f;
            m_LerpTime = lerpTime;
        }

        /// <summary>
        /// 在两个 Vector3 值之间进行线性插值。
        /// </summary>
        /// <param name="current"> 插值开始。 </param>
        /// <param name="target"> End of the interpolation. </param>
        /// <returns> 当前和目标之间的 Vector3 值. </returns>
        public Vector3 LerpPosition(Vector3 current, Vector3 target)
        {
            if (current != target)
            {
                m_LerpStart = current;
                m_CurrentLerpTime = 0f;
            }

            m_CurrentLerpTime += Time.deltaTime;
            if (m_CurrentLerpTime > m_LerpTime)
            {
                m_CurrentLerpTime = m_LerpTime;
            }

            var lerpPercentage = m_CurrentLerpTime / m_LerpTime;

            return Vector3.Lerp(m_LerpStart, target, lerpPercentage);
        }
    }
}
