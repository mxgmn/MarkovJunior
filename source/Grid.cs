// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class Grid
{
    /// <summary>The grid's state, as a flat array.</summary>
    public byte[] state;

    /// <summary>
    /// Used by <see cref="AllNode">AllNode</see> as a temporary buffer to keep
    /// track of which cells have been changed.
    /// </summary>
    public bool[] mask;

    /// <summary>The width of the grid.</summary>
    public int MX;

    /// <summary>The height of the grid.</summary>
    public int MY;

    /// <summary>The depth of the grid. If 2D grid has a depth of 1.</summary>
    public int MZ;

    /// <summary>The number of distinct colors allowed in the grid.</summary>
    public byte C;
    
    /// <summary>The alphabet used for colors.</summary>
    public char[] characters;
    
    /// <summary>Maps each alphabet symbol to its color, i.e. its index in <see cref="Grid.characters">characters</see>.</summary>
    public Dictionary<char, byte> values;

    /// <summary>
    /// Maps each character representing an alphabet symbol, union or wildcard
    /// to a bitmask of which color(s) that character represents.
    /// </summary>
    public Dictionary<char, int> waves;

    /// <summary>If not <c>null</c>, rules with file resources will be loaded from this folder.</summary>
    /// <seealso cref="Rule.Load(XElement, Grid, Grid)"/>
    public string folder;

    /// <summary>A bitmask of which colors should be rendered transparently.</summary>
    /// <remarks>Not currently used.</remarks>
    int transparent;

    /// <summary>A buffer for a temporary copy of the <c>state</c> array.</summary>
    /// <remarks>Not currently used.</remarks>
    byte[] statebuffer;

    /// <summary>
    /// Creates a new grid, whose parameters are loaded from an XML element.
    /// The loading may fail if the XML data is invalid, in which case
    /// <c>null</c> is returned.
    /// </summary>
    /// <param name="xelem">The XML element.</param>
    /// <param name="MX"><inheritdoc cref="Grid.MX" path="/summary"/></param>
    /// <param name="MY"><inheritdoc cref="Grid.MY" path="/summary"/></param>
    /// <param name="MZ"><inheritdoc cref="Grid.MZ" path="/summary"/></param>
    public static Grid Load(XElement xelem, int MX, int MY, int MZ)
    {
        Grid g = new();
        g.MX = MX;
        g.MY = MY;
        g.MZ = MZ;
        string valueString = xelem.Get<string>("values", null)?.Replace(" ", "");
        if (valueString == null)
        {
            Interpreter.WriteLine("no values specified");
            return null;
        }

        g.C = (byte)valueString.Length;
        g.values = new Dictionary<char, byte>();
        g.waves = new Dictionary<char, int>();
        g.characters = new char[g.C];
        for (byte i = 0; i < g.C; i++)
        {
            char symbol = valueString[i];
            if (g.values.ContainsKey(symbol))
            {
                Interpreter.WriteLine($"repeating value {symbol} at line {xelem.LineNumber()}");
                return null;
            }
            else
            {
                g.characters[i] = symbol;
                g.values.Add(symbol, i);
                g.waves.Add(symbol, 1 << i);
            }
        }

        string transparentString = xelem.Get<string>("transparent", null);
        if (transparentString != null) g.transparent = g.Wave(transparentString);

        var xunions = xelem.MyDescendants("markov", "sequence", "union").Where(x => x.Name == "union");
        g.waves.Add('*', (1 << g.C) - 1);
        foreach (XElement xunion in xunions)
        {
            char symbol = xunion.Get<char>("symbol");
            if (g.waves.ContainsKey(symbol))
            {
                Interpreter.WriteLine($"repeating union type {symbol} at line {xunion.LineNumber()}");
                return null;
            }
            else
            {
                int w = g.Wave(xunion.Get<string>("values"));
                g.waves.Add(symbol, w);
            }
        }

        g.state = new byte[MX * MY * MZ];
        g.statebuffer = new byte[MX * MY * MZ];
        g.mask = new bool[MX * MY * MZ];
        g.folder = xelem.Get<string>("folder", null);
        return g;
    }

    /// <summary>
    /// Resets the grid's state to all zeroes (i.e. the first color in the
    /// grid's alphabet).
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < state.Length; i++) state[i] = 0;
    }

    /// <summary>
    /// Parses a string of alphabet symbols as a bitmask of colors.
    /// </summary>
    public int Wave(string values)
    {
        int sum = 0;
        for (int k = 0; k < values.Length; k++) sum += 1 << this.values[values[k]];
        return sum;
    }

    /*static readonly int[] DX = { 1, 0, -1, 0, 0, 0 };
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

    /// <summary>
    /// Determines whether the rule's input pattern matches in this grid at the
    /// given position. The position must be such that the whole input pattern
    /// is in-bounds.
    /// </summary>
    public bool Matches(Rule rule, int x, int y, int z)
    {
        int dz = 0, dy = 0, dx = 0;
        for (int di = 0; di < rule.input.Length; di++)
        {
            if ((rule.input[di] & (1 << state[x + dx + (y + dy) * MX + (z + dz) * MX * MY])) == 0) return false;

            dx++;
            if (dx == rule.IMX)
            {
                dx = 0; dy++;
                if (dy == rule.IMY) { dy = 0; dz++; }
            }
        }
        return true;
    }
}
