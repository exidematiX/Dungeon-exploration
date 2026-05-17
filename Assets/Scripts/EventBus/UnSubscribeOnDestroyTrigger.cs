
public class UnSubscribeOnDestroyTrigger : UnSubscribeTrigger
{
    private void OnDestroy() => UnSubscribeAll();
}