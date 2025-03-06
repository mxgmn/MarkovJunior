// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

// Implements cellular automata-like rules using convolution kernels
class ConvolutionNode : Node
{
    ConvolutionRule[] rules;       // Rules to apply during generation
    int[] kernel;                  // Defines the neighborhood pattern
    bool periodic;                 // Whether grid wraps around edges
    public int counter, steps;     // Current step count and maximum steps

    int[][] sumfield;              // Stores neighborhood counts for each cell

    // Predefined 2D neighborhood patterns (3x3 grids)
    static readonly Dictionary<string, int[]> kernels2d = new()
    {
        // Von Neumann neighborhood: up, right, down, left cells (plus signs)
        ["VonNeumann"] = new int[9] { 0, 1, 0, 1, 0, 1, 0, 1, 0 },
        // Moore neighborhood: all 8 surrounding cells
        ["Moore"] = new int[9] { 1, 1, 1, 1, 0, 1, 1, 1, 1 },
    };
    // Predefined 3D neighborhood patterns (3x3x3 grids)
    static readonly Dictionary<string, int[]> kernels3d = new()
    {
        // 3D Von Neumann neighborhood: 6 adjacent cells
        ["VonNeumann"] = new int[27] { 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0 },
        // All faces and edges but no corners (18 cells)
        ["NoCorners"] = new int[27] { 0, 1, 0, 1, 1, 1, 0, 1, 0, 1, 1, 1, 1, 0, 1, 1, 1, 1, 0, 1, 0, 1, 1, 1, 0, 1, 0 },
    };

    // Initialize from XML configuration
    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        // Get rule elements, use the main element as a rule if none defined
        XElement[] xrules = xelem.Elements("rule").ToArray();
        if (xrules.Length == 0) xrules = new[] { xelem };
        rules = new ConvolutionRule[xrules.Length];
        for (int k = 0; k < rules.Length; k++)
        {
            // Create and load each rule
            rules[k] = new();
            if (!rules[k].Load(xrules[k], grid)) return false;
        }

        // Get other parameters
        steps = xelem.Get("steps", -1);                     // Max steps (-1 = unlimited)
        periodic = xelem.Get("periodic", false);            // Whether the grid wraps
        string neighborhood = xelem.Get<string>("neighborhood");  // Neighborhood type
        // Select kernel based on grid dimension
        kernel = grid.MZ == 1 ? kernels2d[neighborhood] : kernels3d[neighborhood];

        // Create sum storage array
        sumfield = AH.Array2D(grid.state.Length, grid.C, 0);
        return true;
    }

    // Reset the node
    override public void Reset()
    {
        counter = 0;
    }

    // Main process method - runs one step of the convolution
    override public bool Go()
    {
        // Stop if maximum steps reached
        if (steps > 0 && counter >= steps) return false;
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;

        // Reset sum counts
        sumfield.Set2D(0);

        // For 2D grid
        if (MZ == 1)
        {
            // For each cell in the grid
            for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                {
                    // Get the sum array for this cell
                    int[] sums = sumfield[x + y * MX];
                    // Look at each neighbor in the 3x3 grid around this cell
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        {
                            // Calculate neighbor coordinates
                            int sx = x + dx;
                            int sy = y + dy;

                            // Handle boundary conditions
                            if (periodic)
                            {
                                // Wrap around edges if periodic
                                if (sx < 0) sx += MX;
                                else if (sx >= MX) sx -= MX;
                                if (sy < 0) sy += MY;
                                else if (sy >= MY) sy -= MY;
                            }
                            else if (sx < 0 || sy < 0 || sx >= MX || sy >= MY) continue;  // Skip out-of-bounds

                            // Add the kernel value to the count for this neighbor's state
                            sums[grid.state[sx + sy * MX]] += kernel[dx + 1 + (dy + 1) * 3];
                        }
                }
        }
        // For 3D grid
        else
        {
            // Same process but with 3 dimensions
            for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                    {
                        int[] sums = sumfield[x + y * MX + z * MX * MY];
                        for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                                {
                                    int sx = x + dx;
                                    int sy = y + dy;
                                    int sz = z + dz;

                                    if (periodic)
                                    {
                                        if (sx < 0) sx += MX;
                                        else if (sx >= MX) sx -= MX;
                                        if (sy < 0) sy += MY;
                                        else if (sy >= MY) sy -= MY;
                                        if (sz < 0) sz += MZ;
                                        else if (sz >= MZ) sz -= MZ;
                                    }
                                    else if (sx < 0 || sy < 0 || sz < 0 || sx >= MX || sy >= MY || sz >= MZ) continue;

                                    sums[grid.state[sx + sy * MX + sz * MX * MY]] += kernel[dx + 1 + (dy + 1) * 3 + (dz + 1) * 9];
                                }
                    }
        }

        // Apply rules based on calculated sums
        bool change = false;  // Track if any cell changed
        for (int i = 0; i < sumfield.Length; i++)
        {
            int[] sums = sumfield[i];        // Neighborhood counts for this cell
            byte input = grid.state[i];      // Current cell state

            // Try each rule
            for (int r = 0; r < rules.Length; r++)
            {
                ConvolutionRule rule = rules[r];
                // Check if rule applies to this cell's state and the probability check passes
                if (input == rule.input && rule.output != grid.state[i] && (rule.p == 1.0 || ip.random.Next() < rule.p * int.MaxValue))
                {
                    bool success = true;
                    // Check sum condition if specified
                    if (rule.sums != null)
                    {
                        int sum = 0;
                        // Calculate sum of specified values
                        for (int c = 0; c < rule.values.Length; c++) sum += sums[rule.values[c]];
                        // Check if sum is in the allowed range
                        success = rule.sums[sum];
                    }
                    if (success)
                    {
                        // Apply the rule
                        grid.state[i] = rule.output;
                        change = true;
                        break;
                    }
                }
            }
        }

        counter++;
        // Return true if any cell changed
        return change;
    }

    // Nested class to represent individual convolution rules
    class ConvolutionRule
    {
        public byte input, output;  // Input and output cell states
        public byte[] values;       // Cell states to count
        public bool[] sums;         // Valid sum values
        public double p;            // Probability of applying rule

        // Load rule from XML
        public bool Load(XElement xelem, Grid grid)
        {
            // Get basic attributes
            input = grid.values[xelem.Get<char>("in")];    // Input cell state
            output = grid.values[xelem.Get<char>("out")];  // Output cell state
            p = xelem.Get("p", 1.0);                      // Probability

            // Helper function to parse number ranges
            static int[] interval(string s)
            {
                if (s.Contains('.'))
                {
                    // Handle ranges like "3..5"
                    string[] bounds = s.Split("..");
                    int min = int.Parse(bounds[0]);
                    int max = int.Parse(bounds[1]);
                    int[] result = new int[max - min + 1];
                    for (int i = 0; i < result.Length; i++) result[i] = min + i;
                    return result;
                }
                else return new int[1] { int.Parse(s) };  // Single number
            };

            // Get values to count and valid sums
            string valueString = xelem.Get<string>("values", null);
            string sumsString = xelem.Get<string>("sum", null);

            // Validate that both or neither are provided
            if (valueString != null && sumsString == null)
            {
                Interpreter.WriteLine($"missing \"sum\" attribute at line {xelem.LineNumber()}");
                return false;
            }
            if (valueString == null && sumsString != null)
            {
                Interpreter.WriteLine($"missing \"values\" attribute at line {xelem.LineNumber()}");
                return false;
            }

            // Parse values and sums if provided
            if (valueString != null)
            {
                // Convert characters to grid values
                values = valueString.Select(c => grid.values[c]).ToArray();

                // Parse sum constraints
                sums = new bool[28];  // Array of valid sum values
                string[] intervals = sumsString.Split(',');
                foreach (string s in intervals) foreach (int i in interval(s)) sums[i] = true;
            }
            return true;
        }
    }
}

/*
SUMMARY:
This code implements a cellular automaton - a grid of cells that change based on their neighbors.

Imagine a chessboard where each square's color can change based on the colors of nearby squares.
This ConvolutionNode:

1. Counts how many neighbors of each type (color) every cell has
2. Applies rules based on these counts to change cells

For example, a rule might say "if a white cell has exactly 3 black neighbors, turn it black."
This process can create interesting patterns like:
- Conway's Game of Life (cells "live" or "die" based on neighbors)
- Growing structures (like crystals or plants)
- Simulating natural processes (fire spread, water flow)

The "kernel" defines which neighbors count (just the 4 adjacent cells, or diagonals too).
Rules can be probabilistic (sometimes apply, sometimes don't) and can check for specific
conditions like "exactly 2 neighbors" or "between 3 and 5 neighbors."

Each step, the process applies all rules simultaneously across the grid, creating evolving
patterns that can model real-world phenomena or generate interesting visuals.
*/