using System;
using System.Collections.Generic;

namespace MirrorLite
{
    public static class RpcRegistry
    {
        static Dictionary<string, Action<NetworkReader>> serverRpcHandlers = new();
        static Dictionary<string, Action<NetworkReader>> clientRpcHandlers = new();

        public static void RegisterServerRpc(uint netId, string methodName, Action<NetworkReader> handler)
        {
            serverRpcHandlers[$"{netId}:{methodName}"] = handler;
        }

        public static bool InvokeServerRpc(uint netId, string methodName, NetworkReader reader)
        {
            var key = $"{netId}:{methodName}";
            if (serverRpcHandlers.TryGetValue(key, out var h)) { h(reader); return true; }
            return false;
        }

        public static void RegisterClientRpc(uint netId, string methodName, Action<NetworkReader> handler)
        {
            clientRpcHandlers[$"{netId}:{methodName}"] = handler;
        }

        public static bool InvokeClientRpc(uint netId, string methodName, NetworkReader reader)
        {
            var key = $"{netId}:{methodName}";
            if (clientRpcHandlers.TryGetValue(key, out var h)) { h(reader); return true; }
            return false;
        }
    }
}