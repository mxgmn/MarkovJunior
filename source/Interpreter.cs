// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)
using System;
using System.Xml.Linq;
using System.Collections.Generic;
class Interpreter
{
    // Core components of the WFC system
    public Branch root, current;    // Hierarchical execution tree structure
    public Grid grid;               // The current working grid being filled
    Grid startgrid;                 // Original empty grid state for resets
    bool origin;                    // Whether to start from center of grid
    public Random random;           // Random number generator with fixed seed
    public List<(int, int, int)> changes;  // Tracks cell changes (coordinates)
    public List<int> first;         // Tracks first change indices for each step
    public int counter;             // Counts execution steps

    public bool gif;                // Whether to generate animation frames
    Interpreter() { }               // Private constructor - use Load instead

    // Factory method to create and initialize an Interpreter
    public static Interpreter Load(XElement xelem, int MX, int MY, int MZ)
    {
        Interpreter ip = new();

        // Load configuration settings
        ip.origin = xelem.Get("origin", false);  // Start from center if true

        // Create the appropriate grid based on XML config
        ip.grid = Grid.Load(xelem, MX, MY, MZ);
        if (ip.grid == null)
        {
            Console.WriteLine("failed to load grid");
            return null;  // Exit if grid loading fails
        }
        ip.startgrid = ip.grid;  // Save initial grid state

        // Parse symmetry settings for pattern generation
        string symmetryString = xelem.Get<string>("symmetry", null);
        bool[] symmetry = SymmetryHelper.GetSymmetry(ip.grid.MZ == 1, symmetryString, AH.Array1D(ip.grid.MZ == 1 ? 8 : 48, true));
        if (symmetry == null)
        {
            WriteLine($"unknown symmetry {symmetryString} at line {xelem.LineNumber()}");
            return null;  // Exit if symmetry is invalid
        }

        // Create the top-level execution node
        Node topnode = Node.Factory(xelem, symmetry, ip, ip.grid);
        if (topnode == null) return null;  // Exit if node creation fails

        // Ensure root is a Branch (wrapping in MarkovNode if needed)
        ip.root = topnode is Branch ? topnode as Branch : new MarkovNode(topnode, ip);

        // Initialize tracking collections
        ip.changes = new List<(int, int, int)>();
        ip.first = new List<int>();

        return ip;
    }

    // Main execution loop that runs the WFC algorithm
    public IEnumerable<(byte[], char[], int, int, int)> Run(int seed, int steps, bool gif)
    {
        // Initialize for a new run
        random = new Random(seed);
        grid = startgrid;
        grid.Clear();

        // If origin=true, set the center cell as determined
        if (origin) grid.state[grid.MX / 2 + (grid.MY / 2) * grid.MX + (grid.MZ / 2) * grid.MX * grid.MY] = 1;

        // Reset tracking variables
        changes.Clear();
        first.Clear();
        first.Add(0);

        // Reset execution state
        root.Reset();
        current = root;
        this.gif = gif;
        counter = 0;

        // Keep executing steps until completion or step limit reached
        while (current != null && (steps <= 0 || counter < steps))
        {
            // For GIF mode, output intermediate state after each step
            if (gif)
            {
                Console.WriteLine($"[{counter}]");
                yield return (grid.state, grid.characters, grid.MX, grid.MY, grid.MZ);
            }

            // Execute the next step
            current.Go();
            counter++;

            // Record the change boundary for this step
            first.Add(changes.Count);
        }

        // Return the final state
        yield return (grid.state, grid.characters, grid.MX, grid.MY, grid.MZ);
    }

    // Helper methods for console output
    public static void WriteLine(string s) => Console.WriteLine(s);
    public static void Write(string s) => Console.Write(s);
}

/*
=== SUMMARY ===

This code is the core "Interpreter" class for the Wave Function Collapse (WFC) algorithm. Think of it as the conductor of an orchestra, coordinating all the parts needed to generate patterns.

Imagine you're filling in a grid of cells one by one, but each cell's value must follow rules about what can be placed next to what (like puzzle pieces that need to fit together). This class manages that process:

1. The "Load" method sets everything up:
   - Creates a grid (like a blank canvas)
   - Sets up rules for how patterns can repeat (symmetry)
   - Builds a decision tree (nodes) that will guide the filling process

2. The "Run" method actually generates the pattern:
   - Starts with a specific random seed (so results can be reproduced)
   - Optionally starts from the center of the grid
   - Steps through the decision tree, gradually filling in cells
   - Can output intermediate states for animation (GIF mode)
   - Continues until complete or maximum steps reached

This interpreter is like an automated artist that follows specific rules (defined in XML) to create complex patterns that maintain local consistency - each part connects properly with its neighbors according to the defined rules.

It's used for procedural generation of textures, levels, or other patterns where you want randomness but with structure and rules.
*/