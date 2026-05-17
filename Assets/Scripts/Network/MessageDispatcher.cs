using System;
using System.Collections.Generic;
using UnityEngine;

namespace MirrorLite
{
    public static class MessageDispatcher
    {
        static Dictionary<MsgType, Action<int, NetworkReader>> handlers = new();

        public static void Register(MsgType t, Action<int, NetworkReader> handler) => handlers[t] = handler;

        public static void Handle(int connId, byte[] data)
        {
            var r = new NetworkReader(data);
            var t = (MsgType)r.ReadUInt();
            if (handlers.TryGetValue(t, out var h)) h(connId, r);
            else Debug.LogWarning("[Dispatcher] Unknown message type: " + t);
        }

        public static byte[] Pack(MsgType t, Action<NetworkWriter> fill)
        {
            var w = new NetworkWriter();
            w.WriteUInt((uint)t);
            fill(w);
            return w.ToArray();
        }
    }
}