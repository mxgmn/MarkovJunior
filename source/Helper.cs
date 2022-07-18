// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Collections.Generic;

static class Helper
{
    public static (byte[], int) Ords(this int[] data, List<int> uniques = null)
    {
        byte[] result = new byte[data.Length];
        if (uniques == null) uniques = new List<int>();
        for (int i = 0; i < data.Length; i++)
        {
            int d = data[i];
            int ord = uniques.IndexOf(d);
            if (ord == -1)
            {
                ord = uniques.Count;
                uniques.Add(d);
            }
            result[i] = (byte)ord;
        }
        return (result, uniques.Count);
    }

    public static string[][] Split(this string s, char S1, char S2)
    {
        string[] split = s.Split(S1);
        string[][] result = new string[split.Length][];
        for (int k = 0; k < result.Length; k++) result[k] = split[k].Split(S2);
        return result;
    }

    public static int Power(int a, int n)
    {
        int product = 1;
        for (int i = 0; i < n; i++) product *= a;
        return product;
    }

    public static int Index(this bool[] array)
    {
        int result = 0, power = 1;
        for (int i = 0; i < array.Length; i++, power *= 2) if (array[i]) result += power;
        return result;
    }
    public static long Index(this byte[] p, int C)
    {
        long result = 0, power = 1;
        for (int i = 0; i < p.Length; i++, power *= C) result += p[p.Length - 1 - i] * power;
        return result;
    }

    public static byte[] NonZeroPositions(int w)
    {
        int amount = 0, wcopy = w;
        for (byte p = 0; p < 32; p++, w >>= 1) if ((w & 1) == 1) amount++;
        byte[] result = new byte[amount];
        amount = 0;
        for (byte p = 0; p < 32; p++, wcopy >>= 1) if ((wcopy & 1) == 1)
            {
                result[amount] = p;
                amount++;
            }
        return result;
    }

    public static int MaxPositiveIndex(this int[] amounts)
    {
        int max = -1, argmax = -1;
        for (int i = 0; i < amounts.Length; i++)
        {
            int amount = amounts[i];
            if (amount > 0 && amount > max)
            {
                max = amount;
                argmax = i;
            }
        }
        return argmax;
    }

    public static T[] Pattern<T>(Func<int, int, T> f, int N)
    {
        T[] result = new T[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) result[x + y * N] = f(x, y);
        return result;
    }
    public static T[] Rotated<T>(T[] p, int N) => Pattern((x, y) => p[N - 1 - y + x * N], N);
    public static T[] Reflected<T>(T[] p, int N) => Pattern((x, y) => p[N - 1 - x + y * N], N);
}

static class RandomHelper
{
    public static T Random<T>(this List<T> list, Random random) => list[random.Next(list.Count)];
    
    public static int Random(this double[] weights, double r)
    {
        double sum = 0;
        for (int i = 0; i < weights.Length; i++) sum += weights[i];
        double threshold = r * sum;

        double partialSum = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            partialSum += weights[i];
            if (partialSum >= threshold) return i;
        }
        return 0;
    }

    public static void Shuffle(this int[] array, Random random)
    {
        for (int i = 0; i < array.Length; i++)
        {
            int j = random.Next(i + 1);
            array[i] = array[j];
            array[j] = i;
        }
    }
}
