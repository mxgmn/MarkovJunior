// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class Rule
{
    // Dimensions of input and output patterns
    public int IMX, IMY, IMZ, OMX, OMY, OMZ;

    // Input pattern as bitmasks and output pattern as values
    public int[] input;       // Each int is a bitmask indicating allowed values at that position
    public byte[] output;     // Each byte is a specific output value (0xff means "don't change")
    public byte[] binput;     // Simplified input for deterministic patterns (single allowed value)

    // Probability/weight for this rule
    public double p;

    // Precomputed shifts for each value in the input and output patterns
    public (int, int, int)[][] ishifts, oshifts;

    // Flag for tracking original rules vs generated symmetries
    public bool original;

    // Constructor builds a rule with all necessary data structures
    public Rule(int[] input, int IMX, int IMY, int IMZ, byte[] output, int OMX, int OMY, int OMZ, int C, double p)
    {
        this.input = input;
        this.output = output;
        this.IMX = IMX;
        this.IMY = IMY;
        this.IMZ = IMZ;
        this.OMX = OMX;
        this.OMY = OMY;
        this.OMZ = OMZ;

        this.p = p;

        // Precompute positions where each value is allowed in the input pattern
        // This makes matching more efficient during execution
        List<(int, int, int)>[] lists = new List<(int, int, int)>[C];
        for (int c = 0; c < C; c++) lists[c] = new List<(int, int, int)>();
        for (int z = 0; z < IMZ; z++) for (int y = 0; y < IMY; y++) for (int x = 0; x < IMX; x++)
                {
                    int w = input[x + y * IMX + z * IMX * IMY];
                    // Check each bit in the bitmask
                    for (int c = 0; c < C; c++, w >>= 1) if ((w & 1) == 1) lists[c].Add((x, y, z));
                }
        ishifts = new (int, int, int)[C][];
        for (int c = 0; c < C; c++) ishifts[c] = lists[c].ToArray();

        // Only precompute output shifts if input and output have same dimensions
        if (OMX == IMX && OMY == IMY && OMZ == IMZ)
        {
            for (int c = 0; c < C; c++) lists[c].Clear();
            for (int z = 0; z < OMZ; z++) for (int y = 0; y < OMY; y++) for (int x = 0; x < OMX; x++)
                    {
                        byte o = output[x + y * OMX + z * OMX * OMY];
                        // 0xff means "wildcard" - any value can stay here
                        if (o != 0xff) lists[o].Add((x, y, z));
                        else for (int c = 0; c < C; c++) lists[c].Add((x, y, z));
                    }
            oshifts = new (int, int, int)[C][];
            for (int c = 0; c < C; c++) oshifts[c] = lists[c].ToArray();
        }

        // Create a simplified representation for deterministic patterns
        // (patterns where each position allows only one value)
        int wildcard = (1 << C) - 1;  // Bitmask with all bits set (all values allowed)
        binput = new byte[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            int w = input[i];
            // If all values allowed, mark as wildcard (0xff)
            // Otherwise, find the index of the allowed value
            binput[i] = w == wildcard ? (byte)0xff : (byte)System.Numerics.BitOperations.TrailingZeroCount(w);
        }
    }

    // Creates a new rule by rotating the pattern around the Z axis (90 degrees)
    public Rule ZRotated()
    {
        int[] newinput = new int[input.Length];
        for (int z = 0; z < IMZ; z++) for (int y = 0; y < IMX; y++) for (int x = 0; x < IMY; x++)
                    newinput[x + y * IMY + z * IMX * IMY] = input[IMX - 1 - y + x * IMX + z * IMX * IMY];

        byte[] newoutput = new byte[output.Length];
        for (int z = 0; z < OMZ; z++) for (int y = 0; y < OMX; y++) for (int x = 0; x < OMY; x++)
                    newoutput[x + y * OMY + z * OMX * OMY] = output[OMX - 1 - y + x * OMX + z * OMX * OMY];

        return new Rule(newinput, IMY, IMX, IMZ, newoutput, OMY, OMX, OMZ, ishifts.Length, p);
    }

    // Creates a new rule by rotating the pattern around the Y axis (90 degrees)
    public Rule YRotated()
    {
        int[] newinput = new int[input.Length];
        for (int z = 0; z < IMX; z++) for (int y = 0; y < IMY; y++) for (int x = 0; x < IMZ; x++)
                    newinput[x + y * IMZ + z * IMZ * IMY] = input[IMX - 1 - z + y * IMX + x * IMX * IMY];

        byte[] newoutput = new byte[output.Length];
        for (int z = 0; z < OMX; z++) for (int y = 0; y < OMY; y++) for (int x = 0; x < OMZ; x++)
                    newoutput[x + y * OMZ + z * OMZ * OMY] = output[OMX - 1 - z + y * OMX + x * OMX * OMY];

        return new Rule(newinput, IMZ, IMY, IMX, newoutput, OMZ, OMY, OMX, ishifts.Length, p);
    }

    // Creates a new rule by reflecting the pattern (mirror image)
    public Rule Reflected()
    {
        int[] newinput = new int[input.Length];
        for (int z = 0; z < IMZ; z++) for (int y = 0; y < IMY; y++) for (int x = 0; x < IMX; x++)
                    newinput[x + y * IMX + z * IMX * IMY] = input[IMX - 1 - x + y * IMX + z * IMX * IMY];

        byte[] newoutput = new byte[output.Length];
        for (int z = 0; z < OMZ; z++) for (int y = 0; y < OMY; y++) for (int x = 0; x < OMX; x++)
                    newoutput[x + y * OMX + z * OMX * OMY] = output[OMX - 1 - x + y * OMX + z * OMX * OMY];

        return new Rule(newinput, IMX, IMY, IMZ, newoutput, OMX, OMY, OMZ, ishifts.Length, p);
    }

    // Checks if two rules are equivalent
    public static bool Same(Rule a1, Rule a2)
    {
        // Compare dimensions
        if (a1.IMX != a2.IMX || a1.IMY != a2.IMY || a1.IMZ != a2.IMZ ||
            a1.OMX != a2.OMX || a1.OMY != a2.OMY || a1.OMZ != a2.OMZ) return false;

        // Compare input patterns
        for (int i = 0; i < a1.IMX * a1.IMY * a1.IMZ; i++) if (a1.input[i] != a2.input[i]) return false;

        // Compare output patterns
        for (int i = 0; i < a1.OMX * a1.OMY * a1.OMZ; i++) if (a1.output[i] != a2.output[i]) return false;

        return true;
    }

    // Generates all symmetrical variants of this rule based on enabled symmetry flags
    public IEnumerable<Rule> Symmetries(bool[] symmetry, bool d2)
    {
        if (d2) return SymmetryHelper.SquareSymmetries(this, r => r.ZRotated(), r => r.Reflected(), Same, symmetry);
        else return SymmetryHelper.CubeSymmetries(this, r => r.ZRotated(), r => r.YRotated(), r => r.Reflected(), Same, symmetry);
    }

    // Loads pattern data from a resource file (image or voxel)
    public static (char[] data, int MX, int MY, int MZ) LoadResource(string filename, string legend, bool d2)
    {
        if (legend == null)
        {
            Interpreter.WriteLine($"no legend for {filename}");
            return (null, -1, -1, -1);
        }

        // Load image (2D) or voxel file (3D) based on d2 flag
        (int[] data, int MX, int MY, int MZ) = d2 ? Graphics.LoadBitmap(filename) : VoxHelper.LoadVox(filename);
        if (data == null)
        {
            Interpreter.WriteLine($"couldn't read {filename}");
            return (null, MX, MY, MZ);
        }

        // Convert color/material indices to ordinal values
        (byte[] ords, int amount) = data.Ords();
        if (amount > legend.Length)
        {
            Interpreter.WriteLine($"the amount of colors {amount} in {filename} is more than {legend.Length}");
            return (null, MX, MY, MZ);
        }

        // Map ordinal values to characters from the legend
        return (ords.Select(o => legend[o]).ToArray(), MX, MY, MZ);
    }

    // Parses a pattern from text representation
    static (char[], int, int, int) Parse(string s)
    {
        // Split into layers, rows, and columns
        string[][] lines = Helper.Split(s, ' ', '/');
        int MX = lines[0][0].Length;
        int MY = lines[0].Length;
        int MZ = lines.Length;
        char[] result = new char[MX * MY * MZ];

        // Convert text representation to 3D char array
        for (int z = 0; z < MZ; z++)
        {
            string[] linesz = lines[MZ - 1 - z];  // Z-axis is inverted in the text representation
            if (linesz.Length != MY)
            {
                Interpreter.Write("non-rectangular pattern");
                return (null, -1, -1, -1);
            }
            for (int y = 0; y < MY; y++)
            {
                string lineszy = linesz[y];
                if (lineszy.Length != MX)
                {
                    Interpreter.Write("non-rectangular pattern");
                    return (null, -1, -1, -1);
                }
                for (int x = 0; x < MX; x++) result[x + y * MX + z * MX * MY] = lineszy[x];
            }
        }

        return (result, MX, MY, MZ);
    }

    // Loads a rule from XML configuration
    public static Rule Load(XElement xelem, Grid gin, Grid gout)
    {
        int lineNumber = xelem.LineNumber();

        // Helper to construct resource file paths
        string filepath(string name)
        {
            string result = "resources/rules/";
            if (gout.folder != null) result += gout.folder + "/";
            result += name;
            result += gin.MZ == 1 ? ".png" : ".vox";
            return result;
        };

        // Get rule configuration from XML
        string inString = xelem.Get<string>("in", null);       // Input pattern as text
        string outString = xelem.Get<string>("out", null);     // Output pattern as text
        string finString = xelem.Get<string>("fin", null);     // Input pattern from file
        string foutString = xelem.Get<string>("fout", null);   // Output pattern from file
        string fileString = xelem.Get<string>("file", null);   // Both patterns from a single file
        string legend = xelem.Get<string>("legend", null);     // Character mapping for file patterns

        char[] inRect, outRect;
        int IMX = -1, IMY = -1, IMZ = -1, OMX = -1, OMY = -1, OMZ = -1;

        // Load patterns from separate sources
        if (fileString == null)
        {
            // Validate that we have both input and output patterns
            if (inString == null && finString == null)
            {
                Interpreter.WriteLine($"no input in a rule at line {lineNumber}");
                return null;
            }
            if (outString == null && foutString == null)
            {
                Interpreter.WriteLine($"no output in a rule at line {lineNumber}");
                return null;
            }

            // Load input pattern from text or file
            (inRect, IMX, IMY, IMZ) = inString != null ? Parse(inString) : LoadResource(filepath(finString), legend, gin.MZ == 1);
            if (inRect == null)
            {
                Interpreter.WriteLine($" in input at line {lineNumber}");
                return null;
            }

            // Load output pattern from text or file
            (outRect, OMX, OMY, OMZ) = outString != null ? Parse(outString) : LoadResource(filepath(foutString), legend, gin.MZ == 1);
            if (outRect == null)
            {
                Interpreter.WriteLine($" in output at line {lineNumber}");
                return null;
            }

            // For rules applied to the same grid, input and output must have same dimensions
            if (gin == gout && (OMZ != IMZ || OMY != IMY || OMX != IMX))
            {
                Interpreter.WriteLine($"non-matching pattern sizes at line {lineNumber}");
                return null;
            }
        }
        // Load both patterns from a single file (left/right halves)
        else
        {
            if (inString != null || finString != null || outString != null || foutString != null)
            {
                Interpreter.WriteLine($"rule at line {lineNumber} already contains a file attribute");
                return null;
            }
            (char[] rect, int FX, int FY, int FZ) = LoadResource(filepath(fileString), legend, gin.MZ == 1);
            if (rect == null)
            {
                Interpreter.WriteLine($" in a rule at line {lineNumber}");
                return null;
            }
            if (FX % 2 != 0)
            {
                Interpreter.WriteLine($"odd width {FX} in {fileString}");
                return null;
            }

            // Split the file in half horizontally - left side is input, right is output
            IMX = OMX = FX / 2;
            IMY = OMY = FY;
            IMZ = OMZ = FZ;

            // Extract left half (input)
            inRect = AH.FlatArray3D(FX / 2, FY, FZ, (x, y, z) => rect[x + y * FX + z * FX * FY]);
            // Extract right half (output)
            outRect = AH.FlatArray3D(FX / 2, FY, FZ, (x, y, z) => rect[x + FX / 2 + y * FX + z * FX * FY]);
        }

        // Convert input pattern from characters to bitmasks
        int[] input = new int[inRect.Length];
        for (int i = 0; i < inRect.Length; i++)
        {
            char c = inRect[i];
            bool success = gin.waves.TryGetValue(c, out int value);
            if (!success)
            {
                Interpreter.WriteLine($"input code {c} at line {lineNumber} is not found in codes");
                return null;
            }
            input[i] = value;
        }

        // Convert output pattern from characters to values
        byte[] output = new byte[outRect.Length];
        for (int o = 0; o < outRect.Length; o++)
        {
            char c = outRect[o];
            if (c == '*') output[o] = 0xff;  // '*' is a special wildcard (preserve original value)
            else
            {
                bool success = gout.values.TryGetValue(c, out byte value);
                if (!success)
                {
                    Interpreter.WriteLine($"output code {c} at line {lineNumber} is not found in codes");
                    return null;
                }
                output[o] = value;
            }
        }

        // Get rule probability/weight
        double p = xelem.Get("p", 1.0);
        return new Rule(input, IMX, IMY, IMZ, output, OMX, OMY, OMZ, gin.C, p);
    }
}

/*
========== SUMMARY ==========

This code implements a Rule system for a pattern-based generation algorithm, similar to how puzzle pieces fit together or how cellular automata evolve.

Imagine you're playing with a special kind of Lego set. Each Rule is like a building instruction that says: "If you see THIS pattern of blocks, replace it with THAT pattern." The code helps create and manage these instructions.

Here's what the Rule class does in simple terms:

1. Pattern Storage: Each rule stores two 3D patterns - an "input" (what to look for) and an "output" (what to replace it with).
   - The input pattern uses bitmasks to allow multiple possible values at each position
   - The output pattern specifies exact values or wildcards (keep whatever was there)

2. Symmetry Handling: The code can automatically generate variations of rules by rotating and reflecting them, similar to how a square puzzle piece can be placed in different orientations.
   - ZRotated(): Rotates the pattern around the Z axis (like spinning a tile on a table)
   - YRotated(): Rotates the pattern around the Y axis (like flipping a card forward)
   - Reflected(): Mirrors the pattern (like looking at a reflection)

3. Loading System: Rules can be loaded from:
   - Text descriptions in XML (using special character codes)
   - Image files (for 2D patterns)
   - Voxel files (for 3D patterns)
   - Split files (where the left half is input, right half is output)

4. Optimization: The code pre-calculates "shifts" (positions where each value appears) to make pattern matching faster during execution.

This system is likely part of a Wave Function Collapse algorithm or similar procedural generation technique, where these rules guide how patterns evolve or combine. Think of it as the "grammar" that defines how different pieces can connect together when generating levels, textures, or other content.
*/