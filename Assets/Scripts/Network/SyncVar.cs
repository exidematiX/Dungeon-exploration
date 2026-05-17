using System;
using System.Collections.Generic;

namespace MirrorLite
{
    [Serializable]
    public class SyncVar<T>
    {
        public T Value;
        public bool Dirty;

        public SyncVar(T v = default) { Value = v; Dirty = true; }

        public void Set(T v)
        {
            if (!EqualityComparer<T>.Default.Equals(Value, v))
            {
                Value = v;
                Dirty = true;
            }
        }
    }
}