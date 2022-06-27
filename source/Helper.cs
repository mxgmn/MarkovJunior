// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Collections.Generic;

/// <summary>
/// Miscellaneous helper functions.
/// </summary>
static class Helper
{
    /// <summary>
    /// Maps this array to an array of ordinals, i.e. each value <c>x</c> maps
    /// to an ordinal <c>o</c> where <c>x</c> is the <c>o</c>'th distinct value
    /// in the array.
    /// </summary>
    /// <param name="data">The array of values</param>
    /// <param name="uniques">If supplied, a list of already-seen values whose indices in the list are their ordinals.</param>
    /// <returns>A pair <c>(ords, count)</c>, where <c>ords</c> are the mapped ordinals, and <c>count</c> is the number of distinct ordinals.</returns>
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

    /// <summary>
    /// Splits this string into a 2D array of strings. The string is first
    /// split on the delimiter <c>S1</c>, then each part is split on the
    /// delimiter <c>S2</c>.
    /// </summary>
    public static string[][] Split(this string s, char S1, char S2)
    {
        string[] split = s.Split(S1);
        string[][] result = new string[split.Length][];
        for (int k = 0; k < result.Length; k++) result[k] = split[k].Split(S2);
        return result;
    }

    /// <summary>
    /// Computes the integer power <c>a ** n</c>, for a non-negative exponent <c>n</c>.
    /// </summary>
    public static int Power(int a, int n)
    {
        int product = 1;
        for (int i = 0; i < n; i++) product *= a;
        return product;
    }
    
    /// <summary>
    /// Converts a <c>bool</c> array to an integer, by interpreting it as the
    /// bits of a binary number.
    /// </summary>
    public static int Index(this bool[] array)
    {
        int result = 0, power = 1;
        for (int i = 0; i < array.Length; i++, power *= 2) if (array[i]) result += power;
        return result;
    }
    
    /// <summary>
    /// Converts a <c>byte</c> array to a <c>long</c>, by interpreting it as an
    /// array of digits in base <c>C</c>.
    /// </summary>
    /// <param name="p">The array of digits.</param>
    /// <param name="C">The radix.</param>
    public static long Index(this byte[] p, int C)
    {
        long result = 0, power = 1;
        for (int i = 0; i < p.Length; i++, power *= C) result += p[p.Length - 1 - i] * power;
        return result;
    }

    /// <summary>
    /// Returns the position of the first 1 bit in the given <c>int</c> value,
    /// or <c>0xff</c> if there are no 1 bits.
    /// </summary>
    public static byte FirstNonZeroPosition(int w)
    {
        for (byte p = 0; p < 32; p++, w >>= 1) if ((w & 1) == 1) return p;
        return 0xff;
    }
    
    /// <summary>
    /// Returns an array of the positions of the 1 bits in the given <c>int</c>
    /// value, in ascending order.
    /// </summary>
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

    /// <summary>
    /// Returns the index of the maximum positive value in this array, or -1 if
    /// the array contains no positive values.
    /// </summary>
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

    /// <summary>
    /// Creates a square pattern from <c>f(x, y)</c>, as a flat array.
    /// </summary>
    public static T[] Pattern<T>(Func<int, int, T> f, int N)
    {
        T[] result = new T[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) result[x + y * N] = f(x, y);
        return result;
    }
    
    /// <summary>
    /// Rotates a square pattern by 90 degrees, returning a new flat array.
    /// </summary>
    public static T[] Rotated<T>(T[] p, int N) => Pattern((x, y) => p[N - 1 - y + x * N], N);
    
    /// <summary>
    /// Reflects a square pattern vertically, returning a new flat array.
    /// </summary>
    public static T[] Reflected<T>(T[] p, int N) => Pattern((x, y) => p[N - 1 - x + y * N], N);
}

/// <summary>
/// Helper functions for pseudorandom sampling. 
/// </summary>
static class RandomHelper
{
    /// <summary>
    /// Returns a pseudorandom element from this list.
    /// </summary>
    public static T Random<T>(this List<T> list, Random random) => list[random.Next(list.Count)];
    
    /// <summary>
    /// Given a pseudorandom number between 0 and 1, returns the index of a
    /// weighted sample from this array of weights.
    /// </summary>
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

    /// <summary>
    /// Replaces this array's contents with a pseudorandom permutation of the
    /// numbers from 0 to <c>array.Length - 1</c>.
    /// </summary>
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
