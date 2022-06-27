// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;

/// <summary>
/// Helper functions for creating, populating and comparing multidimensional
/// arrays.
/// </summary>
static class AH
{
    /// <summary>
    /// Creates a new 3D array, filled with the specified value.
    /// </summary>
    public static T[][][] Array3D<T>(int MX, int MY, int MZ, T value)
    {
        T[][][] result = new T[MX][][];
        for (int x = 0; x < result.Length; x++)
        {
            result[x] = new T[MY][];
            T[][] resultx = result[x];
            for (int y = 0; y < resultx.Length; y++)
            {
                resultx[y] = new T[MZ];
                T[] resultxy = resultx[y];
                for (int z = 0; z < resultxy.Length; z++) resultxy[z] = value;
            }
        }
        return result;
    }

    /// <summary>
    /// Creates a new 2D array, filled using the given callback function.
    /// </summary>
    public static T[][] Array2D<T>(int MX, int MY, Func<int, int, T> f)
    {
        T[][] result = new T[MX][];
        for (int x = 0; x < result.Length; x++)
        {
            result[x] = new T[MY];
            T[] resultx = result[x];
            for (int y = 0; y < resultx.Length; y++) resultx[y] = f(x, y);
        }
        return result;
    }
    
    /// <summary>
    /// Creates a new 2D array, filled with the specified value.
    /// </summary>
    public static T[][] Array2D<T>(int MX, int MY, T value)
    {
        T[][] result = new T[MX][];
        for (int x = 0; x < result.Length; x++)
        {
            result[x] = new T[MY];
            T[] resultx = result[x];
            for (int y = 0; y < resultx.Length; y++) resultx[y] = value;
        }
        return result;
    }

    /// <summary>
    /// Creates a new 1D array, filled using the given callback function.
    /// </summary>
    public static T[] Array1D<T>(int length, Func<int, T> f)
    {
        T[] result = new T[length];
        for (int i = 0; i < result.Length; i++) result[i] = f(i);
        return result;
    }

    /// <summary>
    /// Creates a new 1D array, filled with the specified value.
    /// </summary>
    public static T[] Array1D<T>(int length, T value)
    {
        T[] result = new T[length];
        for (int i = 0; i < result.Length; i++) result[i] = value;
        return result;
    }

    /// <summary>
    /// Creates a new flat 3D array, filled using the given callback function.
    /// </summary>
    public static T[] FlatArray3D<T>(int MX, int MY, int MZ, Func<int, int, int, T> f)
    {
        T[] result = new T[MX * MY * MZ];
        for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++) result[z * MX * MY + y * MX + x] = f(x, y, z);
        return result;
    }

    /// <summary>
    /// Fills a 2D array with the given value.
    /// </summary>
    public static void Set2D<T>(this T[][] a, T value)
    {
        for (int y = 0; y < a.Length; y++)
        {
            T[] ay = a[y];
            for (int x = 0; x < ay.Length; x++) ay[x] = value;
        }
    }

    /// <summary>
    /// Copies the contents from another 2D array to this one.
    /// </summary>
    public static void CopyFrom2D<T>(this T[][] a, T[][] b) { for (int j = 0; j < a.Length; j++) Array.Copy(b[j], a[j], b[j].Length); }

    /// <summary>
    /// Determines whether two arrays are equal by value.
    /// </summary>
    public static bool Same(byte[] t1, byte[] t2)
    {
        if (t1.Length != t2.Length) return false;
        for (int i = 0; i < t1.Length; i++) if (t1[i] != t2[i]) return false;
        return true;
    }
}
