using UnityEngine;

namespace MirrorLite
{
    public struct FrameInput
    {
        public uint netId;
        public ushort frame;
        public Vector2 move;
        public bool interact;

        public void Write(NetworkWriter writer)
        {
            writer.WriteUInt(netId);
            writer.WriteUShort(frame);
            writer.WriteFloat(move.x);
            writer.WriteFloat(move.y);
            writer.WriteBool(interact);
        }

        public static FrameInput Read(NetworkReader reader)
        {
            return new FrameInput
            {
                netId = reader.ReadUInt(),
                frame = reader.ReadUShort(),
                move = new Vector2(reader.ReadFloat(), reader.ReadFloat()),
                interact = reader.ReadBool()
            };
        }
    }

    public struct PlayerSnapshot
    {
        public uint netId;
        public ushort frame;
        public Vector3 position;
        public Vector3 velocity;

        public void Write(NetworkWriter writer)
        {
            writer.WriteUInt(netId);
            writer.WriteUShort(frame);
            writer.WriteFloat(position.x);
            writer.WriteFloat(position.y);
            writer.WriteFloat(position.z);
            writer.WriteFloat(velocity.x);
            writer.WriteFloat(velocity.y);
            writer.WriteFloat(velocity.z);
        }

        public static PlayerSnapshot Read(NetworkReader reader)
        {
            return new PlayerSnapshot
            {
                netId = reader.ReadUInt(),
                frame = reader.ReadUShort(),
                position = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()),
                velocity = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat())
            };
        }
    }
}
