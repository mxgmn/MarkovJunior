// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class Grid
{
    public byte[] state;       // The main grid data - stores the current state of each cell
    public bool[] mask;        // Mask indicating which cells are modifiable
    public int MX, MY, MZ;     // Grid dimensions (width, height, depth)

    public byte C;             // Number of possible cell values/symbols
    public char[] characters;  // The actual characters/symbols used in the grid
    public Dictionary<char, byte> values;  // Maps symbols to their internal byte values
    public Dictionary<char, int> waves;    // Maps symbols to their wave bitmasks
    public string folder;      // Resource folder path

    int transparent;           // Bitmask for transparent cell types
    byte[] statebuffer;        // Temporary buffer for state operations

    public static Grid Load(XElement xelem, int MX, int MY, int MZ)
    {
        Grid g = new();
        g.MX = MX;
        g.MY = MY;
        g.MZ = MZ;

        // Parse the values string (basic symbol set)
        string valueString = xelem.Get<string>("values", null)?.Replace(" ", "");
        if (valueString == null)
        {
            Interpreter.WriteLine("no values specified");
            return null;  // Exit if no values specified
        }

        // Initialize the grid's symbol system
        g.C = (byte)valueString.Length;  // Number of basic symbols
        g.values = new Dictionary<char, byte>();  // Map from symbol to index
        g.waves = new Dictionary<char, int>();    // Map from symbol to bitmask
        g.characters = new char[g.C];             // Array of symbols

        // Process each symbol
        for (byte i = 0; i < g.C; i++)
        {
            char symbol = valueString[i];
            if (g.values.ContainsKey(symbol))
            {
                // Ensure no duplicate symbols
                Interpreter.WriteLine($"repeating value {symbol} at line {xelem.LineNumber()}");
                return null;
            }
            else
            {
                // Store the symbol and create its mappings
                g.characters[i] = symbol;
                g.values.Add(symbol, i);
                g.waves.Add(symbol, 1 << i);  // Each symbol gets a unique bit in the bitmask
            }
        }

        // Process transparent cells (if specified)
        string transparentString = xelem.Get<string>("transparent", null);
        if (transparentString != null) g.transparent = g.Wave(transparentString);

        // Process symbol unions (symbol sets that represent multiple basic symbols)
        var xunions = xelem.MyDescendants("markov", "sequence", "union").Where(x => x.Name == "union");
        g.waves.Add('*', (1 << g.C) - 1);  // '*' represents "any symbol" (all bits set)

        foreach (XElement xunion in xunions)
        {
            char symbol = xunion.Get<char>("symbol");
            if (g.waves.ContainsKey(symbol))
            {
                // Ensure no duplicate union symbols
                Interpreter.WriteLine($"repeating union type {symbol} at line {xunion.LineNumber()}");
                return null;
            }
            else
            {
                // Create a bitmask representing the union of values
                int w = g.Wave(xunion.Get<string>("values"));
                g.waves.Add(symbol, w);
            }
        }

        // Initialize the grid arrays
        g.state = new byte[MX * MY * MZ];       // Main grid state
        g.statebuffer = new byte[MX * MY * MZ]; // Buffer for temporary operations
        g.mask = new bool[MX * MY * MZ];        // Cell mask
        g.folder = xelem.Get<string>("folder", null);  // Resource folder
        return g;
    }

    // Reset the grid to empty state
    public void Clear()
    {
        for (int i = 0; i < state.Length; i++) state[i] = 0;
    }

    // Convert a string of symbols to a bitmask (union of their values)
    public int Wave(string values)
    {
        int sum = 0;
        for (int k = 0; k < values.Length; k++) sum += 1 << this.values[values[k]];
        return sum;
    }

    /* Commented-out neighbor-counting code
    static readonly int[] DX = { 1, 0, -1, 0, 0, 0 };
    static readonly int[] DY = { 0, 1, 0, -1, 0, 0 };
    static readonly int[] DZ = { 0, 0, 0, 0, 1, -1 };
    public byte[] State()
    {
        int neighbors(int x, int y, int z)
        {
            int sum = 0;
            for (int d = 0; d < 6; d++)
            {
                int X = x + DX[d], Y = y + DY[d], Z = z + DZ[d];
                if (X < 0 || X >= MX || Y < 0 || Y >= MY || Z < 0 || Z >= MZ) continue;
                if (state[X + Y * MX + Z * MX * MY] != 0) sum++;
            }
            return sum;
        };

        Array.Copy(state, statebuffer, state.Length);
        for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                {
                    int i = x + y * MX + z * MX * MY;
                    byte v = state[i];
                    int n = neighbors(x, y, z);
                    if (v == 0 || ((1 << v) & transparent) != 0 || n == 0 || (n == 1 && z == 1 && (state[i - MX * MY] == 11 || state[i - MX * MY] == 4))) statebuffer[i] = 0;
                }
        return statebuffer;
    }*/

    // Check if a grid area matches a rule pattern
    public bool Matches(Rule rule, int x, int y, int z)
    {
        int dz = 0, dy = 0, dx = 0;

        // Check each cell in the rule's input pattern
        for (int di = 0; di < rule.input.Length; di++)
        {
            // Calculate the current cell position
            int cellIndex = x + dx + (y + dy) * MX + (z + dz) * MX * MY;

            // Check if the cell's value is allowed by the rule
            // The rule.input contains bitmasks of allowed values
            if ((rule.input[di] & (1 << state[cellIndex])) == 0) return false;

            // Move to the next position in the rule pattern
            dx++;
            if (dx == rule.IMX)  // End of row
            {
                dx = 0; dy++;
                if (dy == rule.IMY) { dy = 0; dz++; }  // End of layer
            }
        }
        return true;  // All cells matched the pattern
    }
}

/*
=== SUMMARY ===

The Grid class is like a data container for the Wave Function Collapse algorithm. Think of it as a 3D canvas where each cell can hold different symbols, and these symbols follow rules about how they can be arranged.

Imagine a Minecraft world or a pixel art canvas - this Grid class stores what's at each position. Here's what it does:

1. It manages a 3D grid of cells (width × height × depth)
   - Each cell contains a symbol (like 'grass', 'water', 'tree')
   - Each symbol is stored as a byte value for efficiency

2. It handles different ways to refer to symbols:
   - Individual symbols: 'A', 'B', 'C', etc.
   - Union symbols: for example, 'X' might represent "either 'A' or 'B'"
   - Wildcard (*): represents "any symbol"

3. It uses bitmasks to efficiently track possibilities:
   - Each symbol gets its own bit in a binary number
   - This makes checking multiple possibilities very fast
   - For example, if 'A'=1, 'B'=2, 'C'=4, then "A or C" = 5 (binary 101)

4. It can check if regions match pattern rules:
   - The Matches() method compares a section of the grid against a rule pattern
   - This is key for pattern-based generation algorithms

This class serves as the workspace where the WFC algorithm builds its creations, managing both the final result and the constraints that guide the generation process.
*/