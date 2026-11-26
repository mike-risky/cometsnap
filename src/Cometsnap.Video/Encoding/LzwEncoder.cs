namespace Cometsnap.Video.Encoding;

/// <summary>GIF flavoured LZW (variable code size up to 12 bits, LSB first, 255-byte sub-blocks).</summary>
internal sealed class LzwEncoder
{
    private const int MaxCodes = 4096;
    private const int HashSize = 5003; // prime, ~80% load at 4096 entries

    private readonly int[] _hashKeys = new int[HashSize];
    private readonly short[] _hashCodes = new short[HashSize];

    /// <summary>Writes min code size, the LZW data sub-blocks and the block terminator.</summary>
    public void Encode(ReadOnlySpan<byte> indices, int minCodeSize, Stream output)
    {
        output.WriteByte((byte)minCodeSize);
        var bits = new SubBlockWriter(output);
        var clear = 1 << minCodeSize;
        var end = clear + 1;

        int size, next;
        void Reset()
        {
            Array.Fill(_hashKeys, -1);
            size = minCodeSize + 1;
            next = clear + 2;
        }

        Reset();
        bits.Write(clear, size);
        if (indices.IsEmpty)
        {
            bits.Write(end, size);
            bits.Flush();
            output.WriteByte(0);
            return;
        }

        int prefix = indices[0];
        for (var i = 1; i < indices.Length; i++)
        {
            var k = indices[i];
            var key = (prefix << 8) | k;
            var h = Hash(key);
            while (_hashKeys[h] != -1 && _hashKeys[h] != key)
                if (++h == HashSize) h = 0;

            if (_hashKeys[h] == key)
            {
                prefix = _hashCodes[h];
                continue;
            }

            bits.Write(prefix, size);
            var code = next++;
            _hashKeys[h] = key;
            _hashCodes[h] = (short)code;
            if (code == (1 << size) && size < 12) size++;
            if (next == MaxCodes)
            {
                bits.Write(clear, size);
                Reset();
            }
            prefix = k;
        }

        bits.Write(prefix, size);
        bits.Write(end, size);
        bits.Flush();
        output.WriteByte(0);
    }

    private static int Hash(int key) => (int)((uint)(key * 2654435761u) % HashSize);

    /// <summary>Packs codes LSB first and emits them as length-prefixed sub-blocks of ≤255 bytes.</summary>
    private sealed class SubBlockWriter
    {
        private readonly Stream _out;
        private readonly byte[] _block = new byte[256];
        private int _count;
        private uint _acc;
        private int _accBits;

        public SubBlockWriter(Stream output) => _out = output;

        public void Write(int code, int size)
        {
            _acc |= (uint)code << _accBits;
            _accBits += size;
            while (_accBits >= 8)
            {
                Put((byte)_acc);
                _acc >>= 8;
                _accBits -= 8;
            }
        }

        public void Flush()
        {
            if (_accBits > 0) Put((byte)_acc);
            _acc = 0;
            _accBits = 0;
            if (_count > 0) EmitBlock();
        }

        private void Put(byte b)
        {
            _block[1 + _count++] = b;
            if (_count == 255) EmitBlock();
        }

        private void EmitBlock()
        {
            _block[0] = (byte)_count;
            _out.Write(_block, 0, _count + 1);
            _count = 0;
        }
    }
}
