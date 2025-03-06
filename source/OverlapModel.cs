// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class OverlapNode : WFCNode
{
    byte[][] patterns;    // Array of all patterns extracted from the sample image

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        // Currently only supports 2D grids
        if (grid.MZ != 1)
        {
            Interpreter.WriteLine("overlapping model currently works only for 2d");
            return false;
        }

        // Get pattern size (NxN) - default is 3x3
        N = xelem.Get("n", 3);

        // Process symmetry settings
        string symmetryString = xelem.Get<string>("symmetry", null);
        bool[] symmetry = SymmetryHelper.GetSymmetry(true, symmetryString, parentSymmetry);
        if (symmetry == null)
        {
            Interpreter.WriteLine($"unknown symmetry {symmetryString} at line {xelem.LineNumber()}");
            return false;
        }

        // Whether to wrap around when extracting patterns from input
        bool periodicInput = xelem.Get("periodicInput", true);

        // Create the output grid with same dimensions as input
        newgrid = Grid.Load(xelem, grid.MX, grid.MY, grid.MZ);
        if (newgrid == null) return false;
        periodic = true;  // Output grid is periodic (wraps around)

        // Load the sample image
        name = xelem.Get<string>("sample");
        (int[] bitmap, int SMX, int SMY, _) = Graphics.LoadBitmap($"resources/samples/{name}.png");
        if (bitmap == null)
        {
            Interpreter.WriteLine($"couldn't read sample {name}");
            return false;
        }

        // Convert bitmap to byte array and get number of colors
        (byte[] sample, int C) = bitmap.Ords();
        if (C > newgrid.C)
        {
            Interpreter.WriteLine($"there were more than {newgrid.C} colors in the sample");
            return false;
        }

        // Calculate total possible patterns (C^(N*N))
        long W = Helper.Power(C, N * N);

        // Function to convert a pattern index to its actual pattern representation
        byte[] patternFromIndex(long ind)
        {
            long residue = ind, power = W;
            byte[] result = new byte[N * N];
            for (int i = 0; i < result.Length; i++)
            {
                power /= C;
                int count = 0;
                while (residue >= power)
                {
                    residue -= power;
                    count++;
                }
                result[i] = (byte)count;
            }
            return result;
        };

        // Dictionary to count pattern occurrences
        Dictionary<long, int> weights = new();
        List<long> ordering = new();

        // Extract all patterns from the sample image
        int ymax = periodicInput ? grid.MY : grid.MY - N + 1;
        int xmax = periodicInput ? grid.MX : grid.MX - N + 1;
        for (int y = 0; y < ymax; y++) for (int x = 0; x < xmax; x++)
            {
                // Extract an NxN pattern starting at (x,y)
                byte[] pattern = Helper.Pattern((dx, dy) => sample[(x + dx) % SMX + ((y + dy) % SMY) * SMX], N);

                // Generate all symmetrical variants of this pattern based on symmetry settings
                var symmetries = SymmetryHelper.SquareSymmetries(pattern, q => Helper.Rotated(q, N), q => Helper.Reflected(q, N), (q1, q2) => false, symmetry);

                // Add each symmetrical variant to the weights dictionary
                foreach (byte[] p in symmetries)
                {
                    long ind = p.Index(C);  // Convert pattern to a unique index
                    if (weights.ContainsKey(ind)) weights[ind]++;  // Increment weight if already seen
                    else
                    {
                        weights.Add(ind, 1);  // Add new pattern with weight 1
                        ordering.Add(ind);     // Keep track of order
                    }
                }
            }

        // Total number of unique patterns
        P = weights.Count;
        Console.WriteLine($"number of patterns P = {P}");

        // Initialize patterns and weights arrays
        patterns = new byte[P][];
        base.weights = new double[P];
        int counter = 0;
        foreach (long w in ordering)
        {
            patterns[counter] = patternFromIndex(w);  // Convert index back to actual pattern
            base.weights[counter] = weights[w];       // Set pattern weight
            counter++;
        }

        // Function to check if two patterns can be adjacent
        bool agrees(byte[] p1, byte[] p2, int dx, int dy)
        {
            // Calculate overlap area
            int xmin = dx < 0 ? 0 : dx, xmax = dx < 0 ? dx + N : N, ymin = dy < 0 ? 0 : dy, ymax = dy < 0 ? dy + N : N;

            // Check if patterns match in overlap area
            for (int y = ymin; y < ymax; y++) for (int x = xmin; x < xmax; x++)
                    if (p1[x + N * y] != p2[x - dx + N * (y - dy)]) return false;
            return true;
        };

        // Initialize propagator - stores which patterns can be adjacent in each direction
        propagator = new int[4][][];
        for (int d = 0; d < 4; d++)
        {
            propagator[d] = new int[P][];
            for (int t = 0; t < P; t++)
            {
                // Find all patterns that can be adjacent to pattern t in direction d
                List<int> list = new();
                for (int t2 = 0; t2 < P; t2++)
                    if (agrees(patterns[t], patterns[t2], DX[d], DY[d])) list.Add(t2);

                // Store compatible patterns
                propagator[d][t] = new int[list.Count];
                for (int c = 0; c < list.Count; c++) propagator[d][t][c] = list[c];
            }
        }

        // Process rules that constrain which patterns can appear at specific input values
        map = new Dictionary<byte, bool[]>();
        foreach (XElement xrule in xelem.Elements("rule"))
        {
            char input = xrule.Get<char>("in");
            byte[] outputs = xrule.Get<string>("out").Split('|').Select(s => newgrid.values[s[0]]).ToArray();

            // Create a mask of valid patterns (true if the pattern starts with one of the allowed outputs)
            bool[] position = Enumerable.Range(0, P).Select(t => outputs.Contains(patterns[t][0])).ToArray();
            map.Add(grid.values[input], position);
        }

        // Default rule for empty cells (value 0) - allow all patterns
        if (!map.ContainsKey(0)) map.Add(0, Enumerable.Repeat(true, P).ToArray());

        // Complete initialization via parent class
        return base.Load(xelem, parentSymmetry, grid);
    }

    // Convert the wave function to a final grid state
    protected override void UpdateState()
    {
        int MX = newgrid.MX, MY = newgrid.MY;

        // Create a voting array for each cell and color
        int[][] votes = AH.Array2D(newgrid.state.Length, newgrid.C, 0);

        // Count "votes" for each color in each cell based on possible patterns
        for (int i = 0; i < wave.data.Length; i++)
        {
            bool[] w = wave.data[i];
            int x = i % MX, y = i / MX;

            // For each possible pattern at this cell
            for (int p = 0; p < P; p++) if (w[p])
                {
                    byte[] pattern = patterns[p];

                    // Add votes for each position in the pattern
                    for (int dy = 0; dy < N; dy++)
                    {
                        int ydy = y + dy;
                        if (ydy >= MY) ydy -= MY;  // Wrap around

                        for (int dx = 0; dx < N; dx++)
                        {
                            int xdx = x + dx;
                            if (xdx >= MX) xdx -= MX;  // Wrap around

                            byte value = pattern[dx + dy * N];  // Color at this position
                            votes[xdx + ydy * MX][value]++;     // Increment vote count
                        }
                    }
                }
        }

        // Choose the color with the most votes for each cell
        // Add small random value to break ties
        Random r = new();
        for (int i = 0; i < votes.Length; i++)
        {
            double max = -1.0;
            byte argmax = 0xff;
            int[] v = votes[i];

            for (byte c = 0; c < v.Length; c++)
            {
                double value = v[c] + 0.1 * r.NextDouble();  // Add randomness
                if (value > max)
                {
                    argmax = c;
                    max = value;
                }
            }

            newgrid.state[i] = argmax;  // Set final cell value
        }
    }
}

/*
=== SUMMARY ===

The OverlapNode class implements the "Overlapping" version of the Wave Function Collapse algorithm, which works by analyzing patterns in a sample image and generating new images with similar local patterns.

Think of it like learning a visual language from an example:

1. Pattern Extraction:
   - The code divides a sample image into small overlapping patterns (typically 3×3 squares)
   - It identifies all unique patterns and counts how often each appears
   - It also generates variations of these patterns based on symmetry settings (rotations, reflections)

2. Pattern Adjacency Rules:
   - For each pattern and direction, it determines which other patterns can be placed adjacent
   - Patterns can be adjacent if they overlap perfectly in the shared area
   - This builds a set of "legal moves" for the WFC algorithm

3. Generation Process:
   - The algorithm starts with all patterns possible at each location
   - It iteratively collapses cells to specific patterns, propagating constraints
   - When complete, it uses a "voting" system to determine final colors

This approach is powerful because:
- It doesn't need explicit rules - it learns them from the sample
- It can capture complex visual patterns and styles
- It produces outputs that look locally similar to the sample but with novel global arrangements

It's like how a child might learn to draw in the style of an artist by studying small parts of their work and learning which shapes can go next to each other, then creating new compositions following those same local rules.
*/