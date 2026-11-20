using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline.Core;

/// <summary>Streaming animated GIF writer. WIC builds an optimal 256-colour palette per frame; we write the container + LZW.</summary>
public sealed class GifWriter : IDisposable
{
    private readonly Stream _s;
    private readonly int _w, _h;
    private bool _disposed;

    public GifWriter(string path, int width, int height)
    {
        _w = width; _h = height;
        _s = new BufferedStream(File.Create(path), 1 << 16);
        Write("GIF89a"u8);
        WriteU16(width); WriteU16(height);
        _s.WriteByte(0x70); // no global colour table, 8-bit colour resolution
        _s.WriteByte(0); _s.WriteByte(0);
        // NETSCAPE2.0 loop forever
        _s.Write(new byte[] { 0x21, 0xFF, 0x0B });
        Write("NETSCAPE2.0"u8);
        _s.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
    }

    public void AddFrame(byte[] bgra, int delayCentiseconds)
    {
        var src = BitmapSource.Create(_w, _h, 96, 96, PixelFormats.Bgra32, null, bgra, _w * 4);
        var palette = new BitmapPalette(src, 256);
        var indexed = new FormatConvertedBitmap(src, PixelFormats.Indexed8, palette, 0);
        var idx = new byte[_w * _h];
        indexed.CopyPixels(idx, _w, 0);

        // Graphic control extension
        _s.WriteByte(0x21); _s.WriteByte(0xF9); _s.WriteByte(4);
        _s.WriteByte(0x04); // disposal: do not dispose
        WriteU16(Math.Clamp(delayCentiseconds, 2, 65535));
        _s.WriteByte(0); _s.WriteByte(0);

        // Image descriptor with 256-entry local colour table
        _s.WriteByte(0x2C);
        WriteU16(0); WriteU16(0); WriteU16(_w); WriteU16(_h);
        _s.WriteByte(0x87);
        var colors = palette.Colors;
        for (int i = 0; i < 256; i++)
        {
            var c = i < colors.Count ? colors[i] : Colors.Black;
            _s.WriteByte(c.R); _s.WriteByte(c.G); _s.WriteByte(c.B);
        }
        WriteLzw(idx);
    }

    private void WriteLzw(byte[] idx)
    {
        const int clear = 256, eoi = 257;
        _s.WriteByte(8);
        var bits = new SubBlockBitWriter(_s);
        var dict = new Dictionary<int, int>(4096);
        int codeSize = 9, next = 258;

        void Emit(int code)
        {
            bits.Write(code, codeSize);
            if (code == clear) { codeSize = 9; next = 258; dict.Clear(); }
            else if (next > (1 << codeSize) - 1 && codeSize < 12) codeSize++;
        }

        Emit(clear);
        int prefix = idx[0];
        for (int i = 1; i < idx.Length; i++)
        {
            int k = idx[i];
            int key = (prefix << 8) | k;
            if (dict.TryGetValue(key, out int code)) { prefix = code; continue; }
            Emit(prefix);
            if (next < 4096) dict[key] = next++;
            else Emit(clear);
            prefix = k;
        }
        Emit(prefix);
        Emit(eoi);
        bits.Flush();
        _s.WriteByte(0);
    }

    private void Write(ReadOnlySpan<byte> b) => _s.Write(b);
    private void WriteU16(int v) { _s.WriteByte((byte)(v & 0xFF)); _s.WriteByte((byte)(v >> 8)); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _s.WriteByte(0x3B);
        _s.Dispose();
    }

    private sealed class SubBlockBitWriter
    {
        private readonly Stream _s;
        private readonly byte[] _block = new byte[255];
        private int _len, _acc, _nbits;

        public SubBlockBitWriter(Stream s) { _s = s; }

        public void Write(int code, int size)
        {
            _acc |= code << _nbits;
            _nbits += size;
            while (_nbits >= 8) { Put((byte)(_acc & 0xFF)); _acc >>= 8; _nbits -= 8; }
        }

        private void Put(byte b)
        {
            _block[_len++] = b;
            if (_len == 255) FlushBlock();
        }

        private void FlushBlock()
        {
            if (_len == 0) return;
            _s.WriteByte((byte)_len);
            _s.Write(_block, 0, _len);
            _len = 0;
        }

        public void Flush()
        {
            if (_nbits > 0) { Put((byte)(_acc & 0xFF)); _acc = 0; _nbits = 0; }
            FlushBlock();
        }
    }
}
