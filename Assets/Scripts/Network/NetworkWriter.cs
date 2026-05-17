using System;
using System.Collections.Generic;
using System.Text;

namespace MirrorLite
{
    public class NetworkWriter
    {
        List<byte> buf = new List<byte>();
        public void WriteByte(byte v) => buf.Add(v);
        public void WriteBool(bool v) => buf.Add(v ? (byte)1 : (byte)0);
        public void WriteBytes(byte[] v) => buf.AddRange(v);
        public void WriteInt(int v) => WriteBytes(BitConverter.GetBytes(v));
        public void WriteUInt(uint v) => WriteBytes(BitConverter.GetBytes(v));
        public void WriteUShort(ushort v) => WriteBytes(BitConverter.GetBytes(v));
        public void WriteFloat(float v) => WriteBytes(BitConverter.GetBytes(v));
        public void WriteString(string s)
        {
            if (s == null) s = string.Empty;
            var b = Encoding.UTF8.GetBytes(s);
            WriteInt(b.Length);
            WriteBytes(b);
        }

        public byte[] ToArray() => buf.ToArray();
    }
}
