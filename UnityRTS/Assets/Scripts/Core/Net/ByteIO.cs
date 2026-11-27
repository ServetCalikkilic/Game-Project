using System;
using System.Collections.Generic;
using System.Text;

namespace Rts.Core.Net
{
    /// <summary>Little-endian binary writer (explicit byte order, so the wire format does not depend on the machine).</summary>
    public sealed class ByteWriter
    {
        readonly List<byte> _b = new List<byte>(256);
        public int Length => _b.Count;
        public byte[] ToArray() => _b.ToArray();

        public ByteWriter U8(int v) { _b.Add((byte)v); return this; }
        public ByteWriter Bool(bool v) { _b.Add(v ? (byte)1 : (byte)0); return this; }
        public ByteWriter U16(int v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); return this; }
        public ByteWriter I32(int v) { for (int i = 0; i < 4; i++) _b.Add((byte)(v >> (8 * i))); return this; }
        public ByteWriter F32(float v) { return I32(BitConverter.ToInt32(BitConverter.GetBytes(v), 0)); }

        public ByteWriter Str(string s, int maxBytes)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            if (bytes.Length > maxBytes) throw new ArgumentException("string too long");
            U16(bytes.Length);
            _b.AddRange(bytes);
            return this;
        }

        public ByteWriter Raw(byte[] data, int offset, int count)
        {
            for (int i = 0; i < count; i++) _b.Add(data[offset + i]);
            return this;
        }
    }

    /// <summary>Bounds-checked reader: every read past the end throws FormatException (callers convert that to a rejected message).</summary>
    public sealed class ByteReader
    {
        readonly byte[] _d; int _p; readonly int _end;
        public ByteReader(byte[] data, int offset = 0, int count = -1)
        {
            _d = data ?? throw new FormatException("null data");
            _p = offset;
            _end = count < 0 ? data.Length : offset + count;
            if (_p < 0 || _end > data.Length || _p > _end) throw new FormatException("bad range");
        }
        public int Remaining => _end - _p;

        void Need(int n) { if (n < 0 || _end - _p < n) throw new FormatException("message truncated"); }

        public int U8() { Need(1); return _d[_p++]; }
        public bool Bool() { int v = U8(); if (v > 1) throw new FormatException("bad bool"); return v == 1; }
        public int U16() { Need(2); int v = _d[_p] | (_d[_p + 1] << 8); _p += 2; return v; }
        public int I32() { Need(4); int v = _d[_p] | (_d[_p + 1] << 8) | (_d[_p + 2] << 16) | (_d[_p + 3] << 24); _p += 4; return v; }
        public float F32()
        {
            float f = BitConverter.ToSingle(BitConverter.GetBytes(I32()), 0);
            if (float.IsNaN(f) || float.IsInfinity(f)) throw new FormatException("non-finite float");
            return f;
        }
        public string Str(int maxBytes)
        {
            int n = U16();
            if (n > maxBytes) throw new FormatException("string too long");
            Need(n);
            string s = Encoding.UTF8.GetString(_d, _p, n);
            _p += n;
            return s;
        }
        public void ExpectEnd() { if (Remaining != 0) throw new FormatException("trailing bytes"); }
    }
}
