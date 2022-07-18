// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;

static class AH
{
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
                resultx[y].AsSpan().Fill(value);
            }
        }
        return result;
    }

    public static T[][] Array2D<T>(int MX, int MY, T value)
    {
        T[][] result = new T[MX][];
        for (int x = 0; x < result.Length; x++)
        {
            result[x] = new T[MY];
            result[x].AsSpan().Fill(value);
        }
        return result;
    }

    public static T[] Array1D<T>(int length, Func<int, T> f)
    {
        T[] result = new T[length];
        for (int i = 0; i < result.Length; i++) result[i] = f(i);
        return result;
    }
    public static T[] Array1D<T>(int length, T value)
    {
        T[] result = new T[length];
        result.AsSpan().Fill(value);
        return result;
    }

    public static T[] FlatArray3D<T>(int MX, int MY, int MZ, Func<int, int, int, T> f)
    {
        T[] result = new T[MX * MY * MZ];
        for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++) result[z * MX * MY + y * MX + x] = f(x, y, z);
        return result;
    }

    public static void Set2D<T>(this T[][] a, T value) { for (int y = 0; y < a.Length; y++) a[y].AsSpan().Fill(value); }
    
    public static bool Same(byte[] t1, byte[] t2) => t1.AsSpan().SequenceEqual(t2);
}
