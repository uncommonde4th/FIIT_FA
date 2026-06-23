﻿using System;
using System.Collections.Generic;
using Arithmetic.BigInt.Interfaces;

namespace Arithmetic.BigInt.MultiplyStrategy;

internal sealed class FftMultiplier : IMultiplier
{
    private const int minimalSize = 32;
    private const int halfWordBase = 1 << 16;

    private const uint MOD1 = 998244353;   // 2^23 * 7 * 17 + 1
    private const uint MOD2 = 1004535809;  // 2^21 * 479 + 1
    private const uint MOD3 = 469762049;   // 2^26 + 7 * something + 1

    private const uint G = 3;

    public BetterBigInteger Multiply(BetterBigInteger a, BetterBigInteger b)
    {
        if (a is null)
            throw new ArgumentNullException(nameof(a));

        if (b is null)
            throw new ArgumentNullException(nameof(b));

        bool isNegative = a.IsNegative ^ b.IsNegative;

        uint[] res = MultiplyNttCrt(a.GetDigits(), b.GetDigits());

        return BetterBigInteger.FromMagnitude(res, isNegative);
    }

    private static uint[] MultiplyNttCrt(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        uint[] a = BetterBigInteger.NormalizeCopy(left);
        uint[] b = BetterBigInteger.NormalizeCopy(right);

        int nA = BetterBigInteger.GetRealLength(a);
        int nB = BetterBigInteger.GetRealLength(b);

        if (nA == 0 || nB == 0)
            return new uint[] { 0 };

        if (Math.Max(nA, nB) <= minimalSize)
            return BetterBigInteger.MultiplyClassic(a, b);

        int[] A = SplitWords(a, nA);
        int[] B = SplitWords(b, nB);

        int need = A.Length + B.Length - 1;

        int n = 1;
        while (n < need) n <<= 1;

        long[] c1 = Convolution(A, B, MOD1, n);
        long[] c2 = Convolution(A, B, MOD2, n);
        long[] c3 = Convolution(A, B, MOD3, n);

        // CRT recombination
        long m1 = MOD1;
        long m2 = MOD2;
        long m3 = MOD3;

        long m12 = m1 * m2;

        long inv_m1_mod_m2 = ModInverse(m1 % m2, m2);
        long inv_m12_mod_m3 = ModInverse(m12 % m3, m3);

        long[] result = new long[need];

        for (int i = 0; i < need; i++)
        {
            long x1 = c1[i];
            long x2 = c2[i];
            long x3 = c3[i];

            long t2 = (x2 - x1) % m2;
            if (t2 < 0) t2 += m2;
            t2 = (t2 * inv_m1_mod_m2) % m2;

            long x12 = x1 + m1 * t2;

            long t3 = (x3 - (x12 % m3)) % m3;
            if (t3 < 0) t3 += m3;
            t3 = (t3 * inv_m12_mod_m3) % m3;

            long x = x12 + m12 * t3;

            result[i] = x;
        }

        long[] normalized = NormalizeBase(result);

        return PackWords(normalized);
    }

    private static long[] Convolution(int[] a, int[] b, uint mod, int n)
    {
        long[] fa = new long[n];
        long[] fb = new long[n];

        for (int i = 0; i < a.Length; i++) fa[i] = a[i];
        for (int i = 0; i < b.Length; i++) fb[i] = b[i];

        Ntt(fa, mod, false);
        Ntt(fb, mod, false);

        for (int i = 0; i < n; i++)
            fa[i] = fa[i] * fb[i] % mod;

        Ntt(fa, mod, true);

        return fa;
    }

    private static void Ntt(long[] a, uint mod, bool invert)
    {
        int n = a.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;

            if (i < j)
                (a[i], a[j]) = (a[j], a[i]);
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            long wlen = PowMod(G, (mod - 1) / (uint)len, mod);

            if (invert)
                wlen = ModInverse(wlen, mod);

            for (int i = 0; i < n; i += len)
            {
                long w = 1;

                for (int j = 0; j < len / 2; j++)
                {
                    long u = a[i + j];
                    long v = a[i + j + len / 2] * w % mod;

                    a[i + j] = (u + v) % mod;
                    a[i + j + len / 2] = (u - v + mod) % mod;

                    w = w * wlen % mod;
                }
            }
        }

        if (invert)
        {
            long inv_n = ModInverse(n, mod);
            for (int i = 0; i < n; i++)
                a[i] = a[i] * inv_n % mod;
        }
    }

    private static int[] SplitWords(uint[] words, int length)
    {
        int[] coeffs = new int[length * 2];

        for (int i = 0; i < length; i++)
        {
            coeffs[2 * i] = (int)(words[i] & 0xFFFF);
            coeffs[2 * i + 1] = (int)(words[i] >> 16);
        }

        return coeffs;
    }

    private static long[] NormalizeBase(long[] coeffs)
    {
        List<long> res = new(coeffs.Length + 4);

        long carry = 0;
        int i = 0;

        while (i < coeffs.Length || carry != 0)
        {
            long cur = carry;

            if (i < coeffs.Length)
                cur += coeffs[i];

            carry = cur >> 16;
            cur &= 0xFFFF;

            res.Add(cur);
            i++;
        }

        while (res.Count > 1 && res[^1] == 0)
            res.RemoveAt(res.Count - 1);

        return res.ToArray();
    }

    private static uint[] PackWords(long[] coeffs)
    {
        if (coeffs.Length == 0)
            return new uint[] { 0 };

        int n = (coeffs.Length + 1) / 2;
        uint[] res = new uint[n];

        for (int i = 0; i < n; i++)
        {
            uint lo = (uint)coeffs[2 * i];
            uint hi = 0;

            if (2 * i + 1 < coeffs.Length)
                hi = (uint)coeffs[2 * i + 1];

            res[i] = lo | (hi << 16);
        }

        return BetterBigInteger.NormalizeCopy(res);
    }

    private static long PowMod(long a, long e, long mod)
    {
        long r = 1;

        while (e > 0)
        {
            if ((e & 1) != 0)
                r = r * a % mod;

            a = a * a % mod;
            e >>= 1;
        }

        return r;
    }

    private static long ModInverse(long a, long mod)
        => PowMod((a % mod + mod) % mod, mod - 2, mod);
}