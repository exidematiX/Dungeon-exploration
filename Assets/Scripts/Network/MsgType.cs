namespace MirrorLite
{
    public enum MsgType : ushort
    {
        Spawn = 1,//生成
        RpcCall = 2,//远程过程调用
        SyncVars = 3,//同步变量
        Welcome = 4,//欢迎
        ClientHello = 5,//客户端你好
        PlayerInput = 10,//玩家输入
        FrameSnapshot = 11,//帧快照
        Custom = 100,//自定义
    }
}
