
using System;
using System.Collections.Generic;
using UnityEngine;

public interface IUnSubscribe
{
    void UnSubscribe();
}

public struct CustomUnSubscribe : IUnSubscribe
{
    private Action _onUnSubscribe;

    public CustomUnSubscribe(Action onUnSubscribe)
    {
        _onUnSubscribe = onUnSubscribe;
    }

    public void UnSubscribe()
    {
        _onUnSubscribe?.Invoke();
        _onUnSubscribe = null; 
    }
}

public abstract class UnSubscribeTrigger : MonoBehaviour
{
    private readonly HashSet<IUnSubscribe> _unSubscribes = new HashSet<IUnSubscribe>();

    public IUnSubscribe AddUnSubscribe(IUnSubscribe unSubscribe)
    {
        _unSubscribes.Add(unSubscribe);
        return unSubscribe;
    }

    public void UnSubscribeAll()
    {
        foreach (var item in _unSubscribes)
            item.UnSubscribe();
        _unSubscribes.Clear();
    }
}

