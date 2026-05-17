using System;
using System.Text;

namespace MirrorLite
{
    public class NetworkReader
    {
        byte[] buf; int pos;
        public NetworkReader(byte[] data) { buf = data; pos = 0; }
        public byte ReadByte() => buf[pos++];
        public bool ReadBool() => ReadByte() != 0;
        public byte[] ReadBytes(int len) { var a = new byte[len]; Array.Copy(buf, pos, a, 0, len); pos += len; return a; }
        public int ReadInt() { var v = BitConverter.ToInt32(buf, pos); pos += 4; return v; }
        public uint ReadUInt() { var v = BitConverter.ToUInt32(buf, pos); pos += 4; return v; }
        public ushort ReadUShort() { var v = BitConverter.ToUInt16(buf, pos); pos += 2; return v; }
        public float ReadFloat() { var v = BitConverter.ToSingle(buf, pos); pos += 4; return v; }
        public string ReadString() { var len = ReadInt(); var b = ReadBytes(len); return Encoding.UTF8.GetString(b); }
        public bool End => pos >= buf.Length;
    }
}
