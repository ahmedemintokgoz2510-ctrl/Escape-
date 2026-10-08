using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Bağımlılıksız, küçük bir QR kod üretici (Byte modu, hata düzeltme seviyesi M, sürüm 1-10).
/// Bir URL için fazlasıyla yeterlidir (sürüm 10-M = 213 bayt). Unity API'si kullanmaz.
/// </summary>
public static class QrCodeEncoder
{
    // Hata düzeltme seviyesi M için sürüm başına tablolar (indeks = sürüm, 0 kullanılmaz).
    private static readonly int[] EccCodewordsPerBlock = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
    private static readonly int[] NumErrorCorrectionBlocks = { 0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };

    private const int MaxVersion = 10;

    /// <summary>Metni QR matrisine çevirir. [x, y] ve true = koyu modül. Sığmazsa null döner.</summary>
    public static bool[,] Encode(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        byte[] data = Encoding.UTF8.GetBytes(text);

        int version = 0;
        for (int v = 1; v <= MaxVersion; v++)
        {
            int countBits = v <= 9 ? 8 : 16;
            int needed = 4 + countBits + data.Length * 8;
            if (needed <= GetNumDataCodewords(v) * 8) { version = v; break; }
        }
        if (version == 0) return null;

        int capacityBits = GetNumDataCodewords(version) * 8;
        var bits = new List<bool>(capacityBits);
        AppendBits(bits, 0x4, 4); // Byte modu
        AppendBits(bits, data.Length, version <= 9 ? 8 : 16);
        foreach (byte b in data) AppendBits(bits, b, 8);

        AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count)); // sonlandırıcı
        AppendBits(bits, 0, (8 - bits.Count % 8) % 8);               // bayta tamamla
        for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11)
            AppendBits(bits, pad, 8);

        var dataCodewords = new byte[bits.Count / 8];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i]) dataCodewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

        byte[] allCodewords = AddEccAndInterleave(dataCodewords, version);
        return BuildMatrix(version, allCodewords);
    }

    // ---------------------------------------------------------------- Matris

    private static bool[,] BuildMatrix(int version, byte[] codewords)
    {
        int size = version * 4 + 17;
        var modules = new bool[size, size];
        var isFunction = new bool[size, size];

        DrawFunctionPatterns(modules, isFunction, version, size);
        DrawCodewords(modules, isFunction, codewords, size);

        int bestMask = 0;
        int bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(modules, isFunction, mask, size);
            DrawFormatBits(modules, isFunction, mask, size);
            int penalty = GetPenaltyScore(modules, size);
            if (penalty < bestPenalty) { bestPenalty = penalty; bestMask = mask; }
            ApplyMask(modules, isFunction, mask, size); // XOR ile geri al
        }

        ApplyMask(modules, isFunction, bestMask, size);
        DrawFormatBits(modules, isFunction, bestMask, size);
        return modules;
    }

    private static void DrawFunctionPatterns(bool[,] m, bool[,] f, int version, int size)
    {
        for (int i = 0; i < size; i++)
        {
            SetFunction(m, f, 6, i, i % 2 == 0);
            SetFunction(m, f, i, 6, i % 2 == 0);
        }

        DrawFinder(m, f, 3, 3, size);
        DrawFinder(m, f, size - 4, 3, size);
        DrawFinder(m, f, 3, size - 4, size);

        int[] align = GetAlignmentPatternPositions(version);
        int n = align.Length;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                bool corner = (i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0);
                if (!corner) DrawAlignment(m, f, align[i], align[j]);
            }
        }

        DrawFormatBits(m, f, 0, size); // yer ayırma amaçlı
        DrawVersion(m, f, version, size);
    }

    private static void DrawFinder(bool[,] m, bool[,] f, int cx, int cy, int size)
    {
        for (int dy = -4; dy <= 4; dy++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && x < size && y >= 0 && y < size)
                    SetFunction(m, f, x, y, dist != 2 && dist != 4);
            }
        }
    }

    private static void DrawAlignment(bool[,] m, bool[,] f, int cx, int cy)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                SetFunction(m, f, cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
    }

    private static void DrawFormatBits(bool[,] m, bool[,] f, int mask, int size)
    {
        int data = (0 << 3) | mask; // seviye M = 00
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = ((data << 10) | rem) ^ 0x5412;

        for (int i = 0; i <= 5; i++) SetFunction(m, f, 8, i, GetBit(bits, i));
        SetFunction(m, f, 8, 7, GetBit(bits, 6));
        SetFunction(m, f, 8, 8, GetBit(bits, 7));
        SetFunction(m, f, 7, 8, GetBit(bits, 8));
        for (int i = 9; i < 15; i++) SetFunction(m, f, 14 - i, 8, GetBit(bits, i));

        for (int i = 0; i < 8; i++) SetFunction(m, f, size - 1 - i, 8, GetBit(bits, i));
        for (int i = 8; i < 15; i++) SetFunction(m, f, 8, size - 15 + i, GetBit(bits, i));
        SetFunction(m, f, 8, size - 8, true); // her zaman koyu modül
    }

    private static void DrawVersion(bool[,] m, bool[,] f, int version, int size)
    {
        if (version < 7) return;

        int rem = version;
        for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        int bits = (version << 12) | rem;

        for (int i = 0; i < 18; i++)
        {
            bool bit = GetBit(bits, i);
            int a = size - 11 + i % 3;
            int b = i / 3;
            SetFunction(m, f, a, b, bit);
            SetFunction(m, f, b, a, bit);
        }
    }

    private static void DrawCodewords(bool[,] m, bool[,] f, byte[] data, int size)
    {
        int i = 0;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vert : vert;
                    if (!f[x, y] && i < data.Length * 8)
                    {
                        m[x, y] = GetBit(data[i >> 3], 7 - (i & 7));
                        i++;
                    }
                }
            }
        }
    }

    private static void ApplyMask(bool[,] m, bool[,] f, int mask, int size)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool invert;
                switch (mask)
                {
                    case 0: invert = (x + y) % 2 == 0; break;
                    case 1: invert = y % 2 == 0; break;
                    case 2: invert = x % 3 == 0; break;
                    case 3: invert = (x + y) % 3 == 0; break;
                    case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                    case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                    case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                    default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                }
                if (invert && !f[x, y]) m[x, y] = !m[x, y];
            }
        }
    }

    private static int GetPenaltyScore(bool[,] m, int size)
    {
        int penalty = 0;

        // N1: aynı renkten 5+ ardışık modül (satır ve sütun)
        for (int a = 0; a < size; a++)
        {
            int runX = 1, runY = 1;
            for (int b = 1; b < size; b++)
            {
                if (m[b, a] == m[b - 1, a]) runX++; else { if (runX >= 5) penalty += runX - 2; runX = 1; }
                if (m[a, b] == m[a, b - 1]) runY++; else { if (runY >= 5) penalty += runY - 2; runY = 1; }
            }
            if (runX >= 5) penalty += runX - 2;
            if (runY >= 5) penalty += runY - 2;
        }

        // N2: 2x2 aynı renk bloklar
        for (int y = 0; y < size - 1; y++)
            for (int x = 0; x < size - 1; x++)
                if (m[x, y] == m[x + 1, y] && m[x, y] == m[x, y + 1] && m[x, y] == m[x + 1, y + 1])
                    penalty += 3;

        // N3: 1011101 deseni (yanında 4 açık modül)
        bool[] pattern = { true, false, true, true, true, false, true };
        for (int a = 0; a < size; a++)
        {
            for (int b = 0; b <= size - 7; b++)
            {
                bool matchX = true, matchY = true;
                for (int k = 0; k < 7; k++)
                {
                    if (m[b + k, a] != pattern[k]) matchX = false;
                    if (m[a, b + k] != pattern[k]) matchY = false;
                }
                if (matchX && (LightRun(m, size, b - 4, a, true) || LightRun(m, size, b + 7, a, true))) penalty += 40;
                if (matchY && (LightRun(m, size, a, b - 4, false) || LightRun(m, size, a, b + 7, false))) penalty += 40;
            }
        }

        // N4: koyu/açık oran dengesi
        int dark = 0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if (m[x, y]) dark++;
        int total = size * size;
        int k4 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        penalty += Math.Max(0, k4) * 10;

        return penalty;
    }

    // (x,y)'den başlayarak 4 modülün hepsi açık (veya sınır dışı) mı?
    private static bool LightRun(bool[,] m, int size, int x, int y, bool horizontal)
    {
        for (int k = 0; k < 4; k++)
        {
            int xx = horizontal ? x + k : x;
            int yy = horizontal ? y : y + k;
            if (xx >= 0 && xx < size && yy >= 0 && yy < size && m[xx, yy]) return false;
        }
        return true;
    }

    // ------------------------------------------------- Reed-Solomon & bloklar

    private static byte[] AddEccAndInterleave(byte[] data, int version)
    {
        int numBlocks = NumErrorCorrectionBlocks[version];
        int blockEccLen = EccCodewordsPerBlock[version];
        int rawCodewords = GetNumRawDataModules(version) / 8;
        int numShortBlocks = numBlocks - rawCodewords % numBlocks;
        int shortBlockLen = rawCodewords / numBlocks;

        var blocks = new byte[numBlocks][];
        byte[] divisor = ReedSolomonComputeDivisor(blockEccLen);

        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            int datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
            var dat = new byte[datLen];
            Array.Copy(data, k, dat, 0, datLen);
            k += datLen;

            byte[] ecc = ReedSolomonComputeRemainder(dat, divisor);

            var block = new byte[shortBlockLen + 1];
            Array.Copy(dat, block, datLen);
            Array.Copy(ecc, 0, block, block.Length - blockEccLen, blockEccLen);
            blocks[i] = block;
        }

        var result = new byte[rawCodewords];
        int pos = 0;
        for (int i = 0; i < blocks[0].Length; i++)
        {
            for (int j = 0; j < blocks.Length; j++)
            {
                if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                    result[pos++] = blocks[j][i];
            }
        }
        return result;
    }

    private static byte[] ReedSolomonComputeDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = (byte)ReedSolomonMultiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = ReedSolomonMultiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonComputeRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (byte b in data)
        {
            int factor = (b ^ result[0]) & 0xFF;
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[result.Length - 1] = 0;
            for (int i = 0; i < result.Length; i++)
                result[i] ^= (byte)ReedSolomonMultiply(divisor[i], factor);
        }
        return result;
    }

    private static int ReedSolomonMultiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return z & 0xFF;
    }

    // ------------------------------------------------------------ Yardımcılar

    private static int GetNumRawDataModules(int ver)
    {
        int result = (16 * ver + 128) * ver + 64;
        if (ver >= 2)
        {
            int numAlign = ver / 7 + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
            if (ver >= 7) result -= 36;
        }
        return result;
    }

    private static int GetNumDataCodewords(int ver)
    {
        return GetNumRawDataModules(ver) / 8 - EccCodewordsPerBlock[ver] * NumErrorCorrectionBlocks[ver];
    }

    private static int[] GetAlignmentPatternPositions(int ver)
    {
        if (ver == 1) return new int[0];

        int numAlign = ver / 7 + 2;
        int size = ver * 4 + 17;
        int step = (ver * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;
        var result = new int[numAlign];
        result[0] = 6;
        for (int i = numAlign - 1, pos = size - 7; i >= 1; i--, pos -= step) result[i] = pos;
        return result;
    }

    private static void SetFunction(bool[,] m, bool[,] f, int x, int y, bool dark)
    {
        m[x, y] = dark;
        f[x, y] = true;
    }

    private static void AppendBits(List<bool> list, int value, int count)
    {
        for (int i = count - 1; i >= 0; i--) list.Add(((value >> i) & 1) != 0);
    }

    private static bool GetBit(int x, int i)
    {
        return ((x >> i) & 1) != 0;
    }
}
