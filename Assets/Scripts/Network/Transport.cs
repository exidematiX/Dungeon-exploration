using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Mirror
{
    public abstract class Transport
    {
        public abstract void onServerStart();
        public abstract void onClintConnect(string address);
        public abstract void stop();
        public abstract void Send(int connectionId, byte[] data);
        public Action<int, byte[]> OnDataReceived;
        public Action<int> OnClientConnected;
        public Action<int> OnClientDisconnected;
    }
}

