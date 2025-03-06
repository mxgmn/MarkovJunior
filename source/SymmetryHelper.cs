// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Collections.Generic;

static class SymmetryHelper
{
    // Predefined symmetry subgroups for 2D (square) patterns
    // Each boolean array indicates which of the 8 possible symmetry transformations are included
    public static Dictionary<string, bool[]> squareSubgroups = new()
    {
        ["()"] = new bool[8] { true, false, false, false, false, false, false, false },   // Identity only (no symmetry)
        ["(x)"] = new bool[8] { true, true, false, false, false, false, false, false },   // Reflection across x-axis
        ["(y)"] = new bool[8] { true, false, false, false, false, true, false, false },   // Reflection across y-axis
        ["(x)(y)"] = new bool[8] { true, true, false, false, true, true, false, false },  // Reflections across both axes
        ["(xy+)"] = new bool[8] { true, false, true, false, true, false, true, false },   // 90° rotations
        ["(xy)"] = new bool[8] { true, true, true, true, true, true, true, true }         // All symmetries (D4 group)
    };

    // Generates all specified symmetry variants of a 2D pattern
    // Parameters:
    // - thing: The original pattern
    // - rotation: Function to rotate the pattern 90 degrees
    // - reflection: Function to reflect the pattern
    // - same: Function to check if two patterns are equivalent
    // - subgroup: Which symmetry transformations to include
    public static IEnumerable<T> SquareSymmetries<T>(T thing, Func<T, T> rotation, Func<T, T> reflection, Func<T, T, bool> same, bool[] subgroup = null)
    {
        T[] things = new T[8];

        // Generate all 8 possible transformations in the dihedral group D4
        things[0] = thing;                  // e (identity)
        things[1] = reflection(things[0]);  // b (reflection)
        things[2] = rotation(things[0]);    // a (90° rotation)
        things[3] = reflection(things[2]);  // ba (90° rotation + reflection)
        things[4] = rotation(things[2]);    // a2 (180° rotation)
        things[5] = reflection(things[4]);  // ba2 (180° rotation + reflection)
        things[6] = rotation(things[4]);    // a3 (270° rotation)
        things[7] = reflection(things[6]);  // ba3 (270° rotation + reflection)

        // Filter results based on the specified subgroup and eliminate duplicates
        List<T> result = new();
        for (int i = 0; i < 8; i++)
            if ((subgroup == null || subgroup[i]) && !result.Where(s => same(s, things[i])).Any())
                result.Add(things[i]);
        return result;
    }

    // Predefined symmetry subgroups for 3D (cube) patterns
    // Each boolean array indicates which of the 48 possible symmetry transformations are included
    public static Dictionary<string, bool[]> cubeSubgroups = new()
    {
        ["()"] = AH.Array1D(48, l => l == 0),             // Identity only (no symmetry)
        ["(x)"] = AH.Array1D(48, l => l == 0 || l == 1),  // Reflection across x-axis
        ["(z)"] = AH.Array1D(48, l => l == 0 || l == 17), // Reflection across z-axis
        ["(xy)"] = AH.Array1D(48, l => l < 8),            // All 2D symmetries in xy-plane
        ["(xyz+)"] = AH.Array1D(48, l => l % 2 == 0),     // All rotational symmetries
        ["(xyz)"] = AH.Array1D(48, true),                 // All 3D symmetries (cubic group)
        //["(xy)(z)"] = AH.Array1D(48, l => l < 8 || l == 17 || ...), // Not fully implemented
    };

    // Generates all specified symmetry variants of a 3D pattern
    // Parameters:
    // - thing: The original pattern
    // - a: Function to rotate around z-axis
    // - b: Function to rotate around y-axis
    // - r: Function to reflect the pattern
    // - same: Function to check if two patterns are equivalent
    // - subgroup: Which symmetry transformations to include
    public static IEnumerable<T> CubeSymmetries<T>(T thing, Func<T, T> a, Func<T, T> b, Func<T, T> r, Func<T, T, bool> same, bool[] subgroup = null)
    {
        T[] s = new T[48];

        // Generate all 48 possible transformations in the cubic symmetry group
        // This is composed of rotations around different axes and reflections
        s[0] = thing;        // e (identity)
        s[1] = r(s[0]);      // reflection
        s[2] = a(s[0]);      // a (rotation around z)
        s[3] = r(s[2]);
        s[4] = a(s[2]);      // a2 (180° z-rotation)
        s[5] = r(s[4]);
        s[6] = a(s[4]);      // a3 (270° z-rotation)
        s[7] = r(s[6]);
        s[8] = b(s[0]);      // b (rotation around y)
        s[9] = r(s[8]);
        s[10] = b(s[2]);     // b a (compound rotation)
        s[11] = r(s[10]);
        s[12] = b(s[4]);     // b a2
        s[13] = r(s[12]);
        s[14] = b(s[6]);     // b a3
        s[15] = r(s[14]);
        s[16] = b(s[8]);     // b2 (180° y-rotation)
        s[17] = r(s[16]);
        s[18] = b(s[10]);    // b2 a
        s[19] = r(s[18]);
        s[20] = b(s[12]);    // b2 a2
        s[21] = r(s[20]);
        s[22] = b(s[14]);    // b2 a3
        s[23] = r(s[22]);
        s[24] = b(s[16]);    // b3 (270° y-rotation)
        s[25] = r(s[24]);
        s[26] = b(s[18]);    // b3 a
        s[27] = r(s[26]);
        s[28] = b(s[20]);    // b3 a2
        s[29] = r(s[28]);
        s[30] = b(s[22]);    // b3 a3
        s[31] = r(s[30]);
        s[32] = a(s[8]);     // a b (rotation around z then y)
        s[33] = r(s[32]);
        s[34] = a(s[10]);    // a b a
        s[35] = r(s[34]);
        s[36] = a(s[12]);    // a b a2
        s[37] = r(s[36]);
        s[38] = a(s[14]);    // a b a3
        s[39] = r(s[38]);
        s[40] = a(s[24]);    // a3 b a2 = a b3 (alternate form)
        s[41] = r(s[40]);
        s[42] = a(s[26]);    // a3 b a3 = a b3 a
        s[43] = r(s[42]);
        s[44] = a(s[28]);    // a3 b = a b3 a2
        s[45] = r(s[44]);
        s[46] = a(s[30]);    // a3 b a = a b3 a3
        s[47] = r(s[46]);

        // Filter results based on the specified subgroup and eliminate duplicates
        List<T> result = new();
        for (int i = 0; i < 48; i++)
            if ((subgroup == null || subgroup[i]) && !result.Where(t => same(t, s[i])).Any())
                result.Add(s[i]);
        return result;
    }

    // Retrieves the appropriate symmetry subgroup based on dimension (2D or 3D) and name
    // Falls back to the default symmetry if none specified
    public static bool[] GetSymmetry(bool d2, string s, bool[] dflt)
    {
        if (s == null) return dflt;
        bool success = d2 ? squareSubgroups.TryGetValue(s, out bool[] result) : cubeSubgroups.TryGetValue(s, out result);
        return success ? result : null;
    }
}

/*
========== SUMMARY ==========

This code implements a mathematical symmetry system for generating variations of patterns in 2D and 3D space. Think of it like a sophisticated way to create all possible rotations and reflections of a shape.

In simple terms:

1. For 2D Patterns (Squares/Tiles):
   - The code can generate up to 8 different variations of a pattern through combinations of rotations and reflections
   - These correspond to the dihedral group D4 (the symmetry group of a square)
   - You can choose from predefined subgroups like "identity only", "reflections", "rotations", or "all symmetries"

2. For 3D Patterns (Cubes/Voxels):
   - The code can generate up to 48 different variations through combinations of rotations around different axes and reflections
   - These correspond to the octahedral/cubic symmetry group
   - Again, you can select from predefined subgroups to limit which symmetries are applied

3. Practical Uses:
   - This system likely helps avoid having to manually create all possible rotations and reflections of patterns
   - In procedural generation, it allows defining one pattern and automatically generating all symmetric variants
   - The flexibility to choose subgroups means you can control exactly which transformations are applied

The key insight is that this code uses group theory (a branch of mathematics) to efficiently generate all possible ways to transform a pattern while preserving its fundamental structure. This makes the pattern-based generation system much more powerful while requiring less manual input.
*/