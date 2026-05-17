
public class UnSubscribeOnDisableTrigger : UnSubscribeTrigger
{
    private void OnDisable() => UnSubscribeAll();
}