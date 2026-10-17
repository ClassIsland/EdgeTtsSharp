using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace Edge_tts_sharp;

/// <summary>
///     Sec-MS-GEC v2 token 计算
/// </summary>
public partial class EdgeTts
{
    private const double WinEpoch = 11644473600;
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";

    /// <summary>
    ///     本机时钟相对服务器的偏移量（秒）。
    /// </summary>
    private static double _clockSkewSeconds;

    private const uint Sha256Adjustment = 0xC9DC06A7;
    private const uint Sha256Sigma0Adjustment = 0x059C0B80;
    private const uint Sha256Sigma1Adjustment = 0x000B92C0;

    private const uint Cs64Factor = 0x82893933;
    private const uint Cs64Adjustment0 = 0xF2F90B1A;
    private const uint Cs64Adjustment1 = 0x0B1A0000;

    private static readonly uint[] Sha256Initial =
    {
        0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
        0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19
    };

    private static readonly uint[] Sha256StateSeed =
    {
        0xFD9FBEA2, 0x91243CE7, 0xBE499094, 0xCD2E1E0A,
        0x6C006F49, 0x04BD35AB, 0xFEB0A7E4, 0x03EFFBD2
    };

    private static readonly uint[] Sha256Schedule =
    {
        0xEB24BF9E, 0x1558691F, 0xA0C09EE2, 0x0CFA32BA,
        0xA622EE8C, 0xD1DD9491, 0xC0DD25A0, 0xF9E70AB6,
        0xF0C31573, 0x7058CE4E, 0x7188E606, 0x00E5AB33,
        0xD258B060, 0x72766564
    };

    private static readonly uint[] Sha256RoundConstants =
    {
        0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5,
        0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
        0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3,
        0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
        0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC,
        0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
        0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7,
        0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
        0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13,
        0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
        0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3,
        0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
        0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5,
        0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
        0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208,
        0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2
    };

    private static readonly uint[] Sha256BlockConstants = BuildSha256BlockConstants();

    private static readonly uint[,] Cs64Coefficients =
    {
        { 0xE82E91B1, 0xB32700D0 },
        { 0x44DE24C6, 0x88DFF51D },
        { 0x3BEB65BC, 0x29514038 },
        { 0xC89C5933, 0xB328CBDF },
        { 0x8D1EC211, 0xD405B4D5 },
        { 0x314EF5D5, 0x26E55F3C },
        { 0xE92491B1, 0x607801C6 },
        { 0xF0A5B9D5, 0x82B2C1FB },
        { 0x06064CF3, 0xFA51A3DA },
        { 0xDA79FA60, 0x207CC464 },
        { 0xBED20560, 0x3C3C74F3 },
        { 0x89F9C9F8, 0xD2D14B61 },
        { 0x35FA38C6, 0xBB04A2C1 },
        { 0xCB49E3C6, 0xC5626167 }
    };

    private static uint[] BuildSha256BlockConstants()
    {
        var constants = new uint[64];
        Array.Copy(Sha256RoundConstants, constants, 64);
        constants[14] = Sha256Adjustment;
        return constants;
    }

    /// <summary>
    ///     取非负余数，与 Python 的 <c>%</c> 运算符行为一致。
    /// </summary>
    /// <param name="value">被除数。</param>
    /// <param name="divisor">正除数。</param>
    /// <returns>向下取整后的余数。</returns>
    private static int Mod(int value, int divisor)
    {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }

    /// <summary>
    ///     将 32 位数值循环左移 16 位。
    /// </summary>
    /// <param name="value">待循环移位的数值。</param>
    /// <returns>循环移位后的数值。</returns>
    private static uint Rol16(uint value)
    {
        return (value << 16) | (value >> 16);
    }

    /// <summary>
    ///     将数据切分为小端序 32 位字，并补零至偶数个。
    /// </summary>
    /// <param name="data">待切分的数据。</param>
    /// <returns>小端序字序列。</returns>
    private static List<uint> WordsLe4Even(byte[] data)
    {
        var words = new List<uint>();
        if (data.Length == 0)
        {
            return words;
        }

        var padded = new byte[data.Length + Mod(-data.Length, 4)];
        Array.Copy(data, padded, data.Length);
        for (var offset = 0; offset < padded.Length; offset += 4)
        {
            words.Add((uint)(padded[offset]
                             | (padded[offset + 1] << 8)
                             | (padded[offset + 2] << 16)
                             | (padded[offset + 3] << 24)));
        }

        if (words.Count % 2 != 0)
        {
            words.Add(0);
        }

        return words;
    }

    /// <summary>
    ///     SHA-256 小 sigma 零函数，对合并后的循环移位只截断一次。
    /// </summary>
    /// <param name="value">待混合的字。</param>
    /// <returns>混合后的字。</returns>
    private static uint Sigma0(uint value)
    {
        var rotations = (value >> 7) ^ (value << 25) ^ (value >> 18) ^ (value << 14) ^ (value >> 3);
        return rotations & 0xFFFFFFFF;
    }

    /// <summary>
    ///     SHA-256 小 sigma 一函数，对合并后的循环移位只截断一次。
    /// </summary>
    /// <param name="value">待混合的字。</param>
    /// <returns>混合后的字。</returns>
    private static uint Sigma1(uint value)
    {
        var rotations = (value >> 17) ^ (value << 15) ^ (value >> 19) ^ (value << 13) ^ (value >> 10);
        return rotations & 0xFFFFFFFF;
    }

    /// <summary>
    ///     执行 SHA-256 轮运算，可选择从第一个分组的中途继续。
    /// </summary>
    /// <param name="state">传入的工作状态。</param>
    /// <param name="words">消息扩展字序列。</param>
    /// <param name="firstRound">起始轮次。</param>
    /// <returns>返回的工作状态。</returns>
    private static uint[] Sha256Rounds(uint[] state, List<uint> words, int firstRound = 0)
    {
        var r = new[]
        {
            state[0], state[1], state[2], state[3],
            state[4], state[5], state[6], state[7]
        };
        var constants = firstRound == 14 ? Sha256BlockConstants : Sha256RoundConstants;

        for (var index = firstRound; index < 64; index++)
        {
            var rotE = r[4] * 0x100000001UL;
            var rotA = r[0] * 0x100000001UL;
            var t1 = r[7]
                     + (uint)((rotE >> 6) ^ (rotE >> 11) ^ (rotE >> 25))
                     + (r[6] ^ (r[4] & (r[5] ^ r[6])))
                     + constants[index]
                     + words[index];
            var t2 = (uint)((rotA >> 2) ^ (rotA >> 13) ^ (rotA >> 22))
                     + ((r[0] & r[1]) | (r[2] & (r[0] | r[1])));
            var nextE = r[3] + t1;
            r[3] = r[2];
            r[2] = r[1];
            r[1] = r[0];
            r[0] = t1 + t2;
            r[7] = r[6];
            r[6] = r[5];
            r[5] = r[4];
            r[4] = nextE;
        }

        return r;
    }

    /// <summary>
    ///     扩展 SHA-256 消息字序列，在模加之前先合并循环移位。
    /// </summary>
    /// <param name="words">原地扩展的消息字序列。</param>
    /// <param name="firstWord">起始计算的字下标。</param>
    private static void ExpandSha256Schedule(List<uint> words, int firstWord = 16)
    {
        for (var index = firstWord; index < 64; index++)
        {
            var x = words[index - 2];
            var y = words[index - 15];
            var sigma1 = (x >> 17) ^ (x << 15) ^ (x >> 19) ^ (x << 13) ^ (x >> 10);
            var sigma0 = (y >> 7) ^ (y << 25) ^ (y >> 18) ^ (y << 14) ^ (y >> 3);
            words.Add(sigma1 + words[index - 7] + sigma0 + words[index - 16]);
        }
    }

    /// <summary>
    ///     计算令牌数据对应的 SHA-256 初始状态。
    /// </summary>
    /// <param name="data">盐值的前七个字节。</param>
    /// <returns>初始状态。</returns>
    private static uint[] Sha256Init(byte[] data)
    {
        var words = new List<uint>();
        for (var index = 0; index < 14; index++)
        {
            words.Add(0);
        }

        words.Add((uint)((data[0] << 16) | (data[1] << 8) | data[2]));
        words.Add((uint)((data[3] << 24) | (data[4] << 16) | (data[5] << 8) | data[6]));
        for (var index = 16; index < 30; index++)
        {
            var value = Sha256Schedule[index - 16];
            if (index - 2 >= 14)
            {
                value += index == 16
                             ? Sigma1(words[index - 2]) ^ Sha256Sigma1Adjustment
                             : Sigma1(words[index - 2]);
            }

            if (index - 7 >= 14)
            {
                value += words[index - 7];
            }

            if (index - 15 >= 14)
            {
                value += index == 29
                             ? Sigma0(words[index - 15]) ^ Sha256Sigma0Adjustment
                             : Sigma0(words[index - 15]);
            }

            words.Add(value);
        }

        words.Add(
            Sigma1(words[28])
            + words[23]
            + Sigma0(words[15])
            + words[14]
            + Sha256Adjustment
            - Sha256RoundConstants[14]);
        ExpandSha256Schedule(words, 31);
        var working = Sha256Rounds(Sha256StateSeed, words, 14);
        var state = new uint[8];
        for (var index = 0; index < 8; index++)
        {
            state[index] = Sha256Initial[index] + working[index];
        }

        return state;
    }

    /// <summary>
    ///     计算令牌数据对应的 SHA-256 摘要。
    /// </summary>
    /// <param name="data">令牌盐值。</param>
    /// <returns>32 字节摘要。</returns>
    private static byte[] Sha256Digest(byte[] data)
    {
        if (data.Length < 7)
        {
            throw new ArgumentException("Token salt must contain at least seven bytes", nameof(data));
        }

        var length = 57 + data.Length;
        var zeroPadding = Mod(55 - length, 64);
        var padded = new byte[data.Length - 7 + 1 + zeroPadding + 8];
        Array.Copy(data, 7, padded, 0, data.Length - 7);
        padded[data.Length - 7] = 0x80;

        var lengthBits = (ulong)(length * 8);
        for (var index = 0; index < 8; index++)
        {
            padded[padded.Length - 8 + index] = (byte)(lengthBits >> (56 - index * 8));
        }

        var state = Sha256Init(new[] { data[0], data[1], data[2], data[3], data[4], data[5], data[6] });
        for (var offset = 0; offset < padded.Length; offset += 64)
        {
            var words = new List<uint>(16);
            for (var index = 0; index < 16; index++)
            {
                var at = offset + index * 4;
                words.Add((uint)((padded[at] << 24)
                                 | (padded[at + 1] << 16)
                                 | (padded[at + 2] << 8)
                                 | padded[at + 3]));
            }

            ExpandSha256Schedule(words);
            var working = Sha256Rounds(state, words);
            for (var index = 0; index < 8; index++)
            {
                state[index] += working[index];
            }
        }

        var digest = new byte[32];
        for (var index = 0; index < 8; index++)
        {
            var value = state[index];
            digest[index * 4] = (byte)(value >> 24);
            digest[index * 4 + 1] = (byte)(value >> 16);
            digest[index * 4 + 2] = (byte)(value >> 8);
            digest[index * 4 + 3] = (byte)value;
        }

        return digest;
    }

    /// <summary>
    ///     计算令牌数据对应的 CS64 摘要字。
    /// </summary>
    /// <param name="data">令牌盐值。</param>
    /// <param name="digest">令牌盐值的 SHA-256 摘要。</param>
    /// <returns>两个 CS64 摘要字。</returns>
    private static uint[] Cs64Digest(byte[] data, byte[] digest)
    {
        var keyA = (uint)(digest[0]
                          | (digest[1] << 8)
                          | (digest[2] << 16)
                          | (digest[3] << 24)) | 1u;
        var keyB = (uint)(digest[4]
                          | (digest[5] << 8)
                          | (digest[6] << 16)
                          | (digest[7] << 24)) | 1u;
        var factors = new[]
        {
            keyA * Cs64Factor,
            keyB * Cs64Factor
        };
        var terms = new List<uint>(Cs64Coefficients.GetLength(0));
        var offsets = new List<uint>(Cs64Coefficients.GetLength(0));
        for (var index = 0; index < Cs64Coefficients.GetLength(0); index++)
        {
            terms.Add(Cs64Coefficients[index, 0] * factors[index % 2]);
            offsets.Add(Cs64Coefficients[index, 1]);
        }

        var salt = new byte[data.Length + 1];
        Array.Copy(data, 0, salt, 1, data.Length);
        var words = WordsLe4Even(salt);
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var term = (ulong)word * (index % 2 == 0 ? keyA : keyB);
            var offset = Rol16(word) * (index % 2 == 0 ? 0xEF0569FBUL : 0xC31713DBUL);
            if (index == 0)
            {
                term += (ulong)Cs64Adjustment0 * factors[0];
                offset += Cs64Adjustment1;
            }

            terms.Add((uint)term);
            offsets.Add((uint)offset);
        }

        ulong swapAcc = 0;
        ulong swapLast = 0;
        ulong state = 0;
        ulong acc = 0;
        for (var index = 0; index < terms.Count; index += 2)
        {
            var x = terms[index];
            var y = terms[index + 1];
            var t = Rol16(offsets[index] + x);
            var q = t * 0x76F50F34u;
            swapAcc += (ulong)Rol16(q) * 0xC3EFEA97u + q;
            t = Rol16(offsets[index + 1] + y);
            q = t * 0x1881CE3Cu;
            swapLast = (ulong)Rol16(q) * 0x35BD1EC9u + q;
            swapAcc = (swapAcc + swapLast) & 0xFFFFFFFF;

            state = (state * keyA + x) & 0xFFFFFFFF;
            state = ((ulong)Rol16((uint)state) * 0xCF98B111u) & 0xFFFFFFFF;
            state = ((ulong)Rol16((uint)state) * 0x87085B9Fu) & 0xFFFFFFFF;
            state = ((ulong)Rol16((uint)state) * 0x12CEB96Du) & 0xFFFFFFFF;
            state = ((ulong)Rol16((uint)state) * 0x257E1D83u) & 0xFFFFFFFF;
            acc += state;
            t = (uint)(state * keyB + y);
            t = Rol16(t) * 0xA27416F5u;
            t = Rol16(t) * 0xD38396FFu;
            state = Rol16(Rol16(t) * 0x7C932B89u) * 0xBFA49F69u;
            acc = (acc + state) & 0xFFFFFFFF;
        }

        return new[] { (uint)(swapLast ^ state), (uint)(swapAcc ^ acc) };
    }

    /// <summary>
    ///     计算令牌哈希的两个组成部分。
    /// </summary>
    /// <param name="data">令牌盐值。</param>
    /// <returns>CS64 文本与 SHA-256 文本。</returns>
    private static (string Cs64, string Sha256) TokenHash(byte[] data)
    {
        var digest = Sha256Digest(data);
        var cs64Words = Cs64Digest(data, digest);
        var cs64Bytes = new byte[8];
        for (var index = 0; index < cs64Words.Length; index++)
        {
            cs64Bytes[index * 4] = (byte)cs64Words[index];
            cs64Bytes[index * 4 + 1] = (byte)(cs64Words[index] >> 8);
            cs64Bytes[index * 4 + 2] = (byte)(cs64Words[index] >> 16);
            cs64Bytes[index * 4 + 3] = (byte)(cs64Words[index] >> 24);
        }

        var cs64 = Convert.ToHexString(cs64Bytes);
        var sha256 = new StringBuilder(64);
        foreach (var value in digest)
        {
            sha256.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        return (cs64, sha256.ToString());
    }

    private static string GenerateSecMsGecV2(double? unixTimestamp = null)
    {
        var ticks = unixTimestamp ?? GetUnixTimestamp();
        ticks += WinEpoch;
        ticks -= ticks - 300 * Math.Floor(ticks / 300);
        ticks *= 1e9 / 100;
        var salt = Encoding.ASCII.GetBytes(
            Convert.ToInt64(ticks).ToString(CultureInfo.InvariantCulture) + TrustedClientToken);
        var hash = TokenHash(salt);
        return hash.Cs64 + "-" + hash.Sha256;
    }

    private static double GetUnixTimestamp()
    {
        return GetRawUnixTimestamp() + _clockSkewSeconds;
    }

    private static double GetRawUnixTimestamp()
    {
        return (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
    }

    /// <summary>
    ///     根据 403 响应的服务器日期校正本机时钟偏移，
    ///     校正量会累积到后续的 token 计算中。
    /// </summary>
    /// <param name="statusCode">WebSocket 握手返回的状态码。</param>
    /// <param name="serverDate">响应的 Date 头。</param>
    /// <returns>完成校正时返回 <see langword="true" />。</returns>
    private static bool TryAdjustClockSkew(HttpStatusCode? statusCode, string serverDate)
    {
        if (statusCode != HttpStatusCode.Forbidden || string.IsNullOrWhiteSpace(serverDate))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                serverDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        _clockSkewSeconds += parsed.ToUnixTimeSeconds() - GetRawUnixTimestamp();
        return true;
    }
}