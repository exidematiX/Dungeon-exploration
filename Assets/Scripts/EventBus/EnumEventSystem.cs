
public class EnumEventSystem
{
    private static EnumEventSystem _instance;
    public static EnumEventSystem Instance => _instance ??= new EnumEventSystem();

    private readonly Dictionary<int, List<Action<object[]>>> _eventTable
        = new Dictionary<int, List<Action<object[]>>>();

    public IUnSubscribe Subscribe<T>(T key, Action<object[]> onEvent) where T : IConvertible
    {
        int eventId = key.ToInt32(null);

        if (!_eventTable.TryGetValue(eventId, out var list))
        {
            list = new List<Action<object[]>>();
            _eventTable[eventId] = list;
        }

        list.Add(onEvent);

        return new CustomUnSubscribe(() =>
        {
            list.Remove(onEvent);
            if (list.Count == 0)
                _eventTable.Remove(eventId);
        });
    }
 
    public void Publish<T>(T key, params object[] args) where T : IConvertible
    {
        int eventId = key.ToInt32(null);

        if (!_eventTable.TryGetValue(eventId, out var list))
            return;

        foreach (var handler in list.ToArray())
            handler?.Invoke(args);
    }

    public void Clear<T>(T key) where T : IConvertible
    {
        int eventId = key.ToInt32(null);
        _eventTable.Remove(eventId);
    }

    public void ClearAll() => _eventTable.Clear();
}