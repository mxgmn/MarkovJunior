// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)
using System;

// AH (Array Helper) class provides utility methods for creating and manipulating arrays
static class AH
{
    // Creates a 3D jagged array with the specified dimensions and fills it with the given value
    public static T[][][] Array3D<T>(int MX, int MY, int MZ, T value)
    {
        T[][][] result = new T[MX][][];
        for (int x = 0; x < result.Length; x++)
        {
            // Create the second dimension array
            result[x] = new T[MY][];
            T[][] resultx = result[x];
            for (int y = 0; y < resultx.Length; y++)
            {
                // Create the third dimension array
                resultx[y] = new T[MZ];
                // Fill it with the specified value
                resultx[y].AsSpan().Fill(value);
            }
        }
        return result;
    }

    // Creates a 2D jagged array with the specified dimensions and fills it with the given value
    public static T[][] Array2D<T>(int MX, int MY, T value)
    {
        T[][] result = new T[MX][];
        for (int x = 0; x < result.Length; x++)
        {
            // Create the second dimension array
            result[x] = new T[MY];
            // Fill it with the specified value
            result[x].AsSpan().Fill(value);
        }
        return result;
    }

    // Creates a 1D array and initializes each element using the provided function
    public static T[] Array1D<T>(int length, Func<int, T> f)
    {
        T[] result = new T[length];
        for (int i = 0; i < result.Length; i++) result[i] = f(i);
        return result;
    }

    // Creates a 1D array and fills it with the given value
    public static T[] Array1D<T>(int length, T value)
    {
        T[] result = new T[length];
        result.AsSpan().Fill(value);
        return result;
    }

    // Creates a flat 1D array representing 3D data, using the provided function to generate values
    public static T[] FlatArray3D<T>(int MX, int MY, int MZ, Func<int, int, int, T> f)
    {
        T[] result = new T[MX * MY * MZ];
        for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++) result[z * MX * MY + y * MX + x] = f(x, y, z);
        return result;
    }

    // Extension method that fills a 2D array with a specific value
    public static void Set2D<T>(this T[][] a, T value) { for (int y = 0; y < a.Length; y++) a[y].AsSpan().Fill(value); }

    // Checks if two byte arrays contain the same elements
    public static bool Same(byte[] t1, byte[] t2) => t1.AsSpan().SequenceEqual(t2);
}

/*
SUMMARY:
This code is a helper class (AH = Array Helper) that makes it easier to work with arrays of different sizes.

Imagine you're building with LEGO bricks. This helper lets you:
1. Quickly create empty boxes (arrays) of different shapes (1D, 2D, or 3D)
2. Fill those boxes with the same brick (value) everywhere
3. Create boxes where each position has a special brick based on its location
4. Check if two rows of bricks are exactly the same

These functions save time when you need to create and fill many arrays in a program. They're especially useful for working with grids, maps, or 3D spaces where you need to track information at different positions.

The class uses generic types (<T>), which means it works with any type of data (numbers, text, custom objects), similar to how a container can hold different types of items.
*/
