//拓展方法，让哨兵节点链式绑定
public static class UnSubscribeExtension
{
    private static T GetOrAddComponent<T>(GameObject go) where T : Component
    {
        return go.GetComponent<T>() ?? go.AddComponent<T>();
    }

    /// <summary>
    /// GameObject 销毁时自动取消订阅
    /// </summary>
    public static IUnSubscribe UnSubscribeWhenGameObjectDestroyed(
        this IUnSubscribe self, GameObject go)
    {
        return GetOrAddComponent<UnSubscribeOnDestroyTrigger>(go).AddUnSubscribe(self);
    }

    public static IUnSubscribe UnSubscribeWhenGameObjectDestroyed<T>(
        this IUnSubscribe self, T component) where T : Component
    {
        return self.UnSubscribeWhenGameObjectDestroyed(component.gameObject);
    }

    public static IUnSubscribe UnSubscribeWhenDisabled(
        this IUnSubscribe self, GameObject go)
    {
        return GetOrAddComponent<UnSubscribeOnDisableTrigger>(go).AddUnSubscribe(self);
    }

    public static IUnSubscribe UnSubscribeWhenDisabled<T>(
        this IUnSubscribe self, T component) where T : Component
    {
        return self.UnSubscribeWhenDisabled(component.gameObject);
    }
}
