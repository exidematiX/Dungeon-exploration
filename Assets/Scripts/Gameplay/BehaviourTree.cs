using System;
using System.Collections.Generic;

namespace MirrorLite.Gameplay
{
    public enum BehaviourStatus
    {
        Success,
        Failure,
        Running
    }

    public abstract class BehaviourNode
    {
        public abstract BehaviourStatus Tick();
    }

    public sealed class BehaviourAction : BehaviourNode
    {
        readonly Func<BehaviourStatus> action;

        public BehaviourAction(Func<BehaviourStatus> action)
        {
            this.action = action;
        }

        public override BehaviourStatus Tick() => action != null ? action() : BehaviourStatus.Failure;
    }

    public sealed class BehaviourSequence : BehaviourNode
    {
        readonly List<BehaviourNode> children;
        int runningIndex;

        public BehaviourSequence(params BehaviourNode[] children)
        {
            this.children = new List<BehaviourNode>(children);
        }

        public override BehaviourStatus Tick()
        {
            while (runningIndex < children.Count)
            {
                var status = children[runningIndex].Tick();
                if (status == BehaviourStatus.Running)
                    return BehaviourStatus.Running;

                if (status == BehaviourStatus.Failure)
                {
                    runningIndex = 0;
                    return BehaviourStatus.Failure;
                }

                runningIndex++;
            }

            runningIndex = 0;
            return BehaviourStatus.Success;
        }
    }

    public sealed class BehaviourSelector : BehaviourNode
    {
        readonly List<BehaviourNode> children;
        int runningIndex;

        public BehaviourSelector(params BehaviourNode[] children)
        {
            this.children = new List<BehaviourNode>(children);
        }

        public override BehaviourStatus Tick()
        {
            while (runningIndex < children.Count)
            {
                var status = children[runningIndex].Tick();
                if (status == BehaviourStatus.Running)
                    return BehaviourStatus.Running;

                if (status == BehaviourStatus.Success)
                {
                    runningIndex = 0;
                    return BehaviourStatus.Success;
                }

                runningIndex++;
            }

            runningIndex = 0;
            return BehaviourStatus.Failure;
        }
    }
}
