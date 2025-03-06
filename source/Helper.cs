// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)
using System;
using System.Collections.Generic;
static class Helper
{
    // Converts an integer array to a bytes array where each unique value is assigned a byte index
    // Also returns the number of unique values found
    public static (byte[], int) Ords(this int[] data, List<int> uniques = null)
    {
        byte[] result = new byte[data.Length];
        if (uniques == null) uniques = new List<int>();
        for (int i = 0; i < data.Length; i++)
        {
            int d = data[i];
            int ord = uniques.IndexOf(d);  // Check if we've seen this value before
            if (ord == -1)  // New unique value
            {
                ord = uniques.Count;
                uniques.Add(d);
            }
            result[i] = (byte)ord;  // Store the index instead of the value
        }
        return (result, uniques.Count);
    }

    // Splits a string on two different separators, creating a 2D string array
    // Example: "a,b,c;d,e;f,g" split by ';' and ',' becomes [["a","b","c"],["d","e"],["f","g"]]
    public static string[][] Split(this string s, char S1, char S2)
    {
        string[] split = s.Split(S1);
        string[][] result = new string[split.Length][];
        for (int k = 0; k < result.Length; k++) result[k] = split[k].Split(S2);
        return result;
    }

    // Calculates a^n (a raised to the power of n)
    public static int Power(int a, int n)
    {
        int product = 1;
        for (int i = 0; i < n; i++) product *= a;
        return product;
    }

    // Converts a boolean array to an integer by treating it as a binary number
    // Example: [true, false, true] becomes 5 (binary 101)
    public static int Index(this bool[] array)
    {
        int result = 0, power = 1;
        for (int i = 0; i < array.Length; i++, power *= 2) if (array[i]) result += power;
        return result;
    }

    // Converts a byte array to a long by treating it as a base-C number
    // Used to create unique indices for patterns
    public static long Index(this byte[] p, int C)
    {
        long result = 0, power = 1;
        for (int i = 0; i < p.Length; i++, power *= C) result += p[p.Length - 1 - i] * power;
        return result;
    }

    // Finds positions of 1 bits in a binary number and returns them as a byte array
    // Example: 10 (binary 1010) returns [1,3] (zero-indexed positions of set bits)
    public static byte[] NonZeroPositions(int w)
    {
        // Count how many bits are set to 1
        int amount = 0, wcopy = w;
        for (byte p = 0; p < 32; p++, w >>= 1) if ((w & 1) == 1) amount++;

        // Create array and fill with positions of set bits
        byte[] result = new byte[amount];
        amount = 0;
        for (byte p = 0; p < 32; p++, wcopy >>= 1) if ((wcopy & 1) == 1)
            {
                result[amount] = p;
                amount++;
            }
        return result;
    }

    // Finds the index of the largest positive value in an array
    // Returns -1 if no positive values exist
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

    // Creates an NxN pattern array by applying a function to each (x,y) coordinate
    // Useful for pattern generation and transformation
    public static T[] Pattern<T>(Func<int, int, T> f, int N)
    {
        T[] result = new T[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) result[x + y * N] = f(x, y);
        return result;
    }

    // Rotates a pattern 90 degrees clockwise
    public static T[] Rotated<T>(T[] p, int N) => Pattern((x, y) => p[N - 1 - y + x * N], N);

    // Reflects a pattern horizontally (flips left to right)
    public static T[] Reflected<T>(T[] p, int N) => Pattern((x, y) => p[N - 1 - x + y * N], N);
}

static class RandomHelper
{
    // Picks a random element from a list
    public static T Random<T>(this List<T> list, Random random) => list[random.Next(list.Count)];

    // Selects an index based on weighted probabilities
    // The probability of selecting index i is weights[i]/sum(weights)
    public static int Random(this double[] weights, double r)
    {
        // Calculate sum of all weights
        double sum = 0;
        for (int i = 0; i < weights.Length; i++) sum += weights[i];

        // Generate threshold based on random value r (0 to 1)
        double threshold = r * sum;

        // Find the index where cumulative sum exceeds the threshold
        double partialSum = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            partialSum += weights[i];
            if (partialSum >= threshold) return i;
        }
        return 0;  // Fallback (should not reach here unless all weights are 0)
    }

    // Fisher-Yates shuffle algorithm to randomly permute an array
    // This implementation creates the identity permutation [0,1,2,...] and shuffles it
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

/*
=== SUMMARY ===

These helper classes provide utility functions for the Wave Function Collapse algorithm, making common operations more concise and readable.

The Helper class is like a Swiss Army knife for data transformation, offering tools to:

1. Convert between different data types and representations:
   - Mapping arbitrary values to compact byte indices (Ords)
   - Converting patterns to unique numerical IDs (Index methods)
   - Extracting bit positions from integers (NonZeroPositions)

2. Manipulate patterns and arrays:
   - Creating 2D patterns using custom functions (Pattern)
   - Transforming patterns through rotation and reflection
   - Finding maximum values in arrays (MaxPositiveIndex)

3. Handle strings and math:
   - Split strings on multiple delimiters
   - Calculate powers of numbers

The RandomHelper class provides probability and randomization tools:

1. Making weighted random selections:
   - Picking items from lists with uniform probability
   - Selecting indices based on weighted probabilities

2. Creating random permutations:
   - Shuffling arrays using the Fisher-Yates algorithm

These utilities handle the "plumbing" needed for the WFC algorithm to efficiently work with patterns, probabilities, and transformations - similar to how a graphics library provides basic drawing functions that more complex visualizations can build upon.
*/