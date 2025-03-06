// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Xml.Linq;
using System.Collections.Generic;

abstract class WFCNode : Branch
{
    protected Wave wave;              // Stores the possibility space for each cell
    protected int[][][] propagator;   // Rules for how patterns propagate between neighboring cells
    protected int P, N = 1;           // P = number of patterns, N = pattern size (typically N×N)

    (int, int)[] stack;               // Stack for tracking cells that need propagation
    int stacksize;                    // Current size of the propagation stack

    protected double[] weights;        // Relative frequency of each pattern
    double[] weightLogWeights;         // Precomputed weight*log(weight) for entropy calculation
    double sumOfWeights, sumOfWeightLogWeights, startingEntropy;  // Cached values for entropy

    protected Grid newgrid;           // Output grid being constructed
    Wave startwave;                   // Initial wave state after constraints applied

    protected Dictionary<byte, bool[]> map;  // Maps grid values to allowed patterns
    protected bool periodic, shannon;         // Whether grid wraps around, whether to use Shannon entropy

    double[] distribution;            // Temporary array for pattern selection
    int tries;                        // Max attempts to find contradiction-free generation

    public string name;               // Name identifier for this node

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        // Load configuration parameters
        shannon = xelem.Get("shannon", false);  // Whether to use Shannon entropy for observation selection
        tries = xelem.Get("tries", 1000);       // Max attempts to find a valid seed

        // Initialize the wave - core data structure tracking possible patterns for each cell
        wave = new Wave(grid.state.Length, P, propagator.Length, shannon);
        startwave = new Wave(grid.state.Length, P, propagator.Length, shannon);
        stack = new (int, int)[wave.data.Length * P];  // Stack for propagation

        // Initialize entropy calculation values (when using Shannon entropy)
        sumOfWeights = sumOfWeightLogWeights = startingEntropy = 0;

        if (shannon)
        {
            weightLogWeights = new double[P];

            // Precompute values needed for Shannon entropy calculation
            for (int t = 0; t < P; t++)
            {
                weightLogWeights[t] = weights[t] * Math.Log(weights[t]);
                sumOfWeights += weights[t];
                sumOfWeightLogWeights += weightLogWeights[t];
            }

            // Formula for Shannon entropy: H = log(sum(weights)) - sum(weight*log(weight))/sum(weights)
            startingEntropy = Math.Log(sumOfWeights) - sumOfWeightLogWeights / sumOfWeights;
        }

        distribution = new double[P];  // For weighted random pattern selection
        return base.Load(xelem, parentSymmetry, newgrid);  // Continue loading in parent class
    }

    override public void Reset()
    {
        base.Reset();
        n = -1;         // Reset step counter
        firstgo = true;  // Mark as needing initialization
    }

    bool firstgo = true;  // Whether this is the first execution step
    Random random;        // Random number generator for this node
    public override bool Go()
    {
        // If n >= 0, we've completed WFC and are executing child nodes
        if (n >= 0) return base.Go();

        if (firstgo)
        {
            // Initialize the wave with propagator rules and entropy values
            wave.Init(propagator, sumOfWeights, sumOfWeightLogWeights, startingEntropy, shannon);

            // Apply constraints from existing grid values
            for (int i = 0; i < wave.data.Length; i++)
            {
                byte value = grid.state[i];
                if (map.ContainsKey(value))
                {
                    bool[] startWave = map[value];
                    for (int t = 0; t < P; t++) if (!startWave[t]) Ban(i, t);  // Disallow patterns
                }
            }

            // Check if initial constraints are contradictory
            bool firstSuccess = Propagate();
            if (!firstSuccess)
            {
                Console.WriteLine("initial conditions are contradictive");
                return false;
            }

            // Save the initial state after constraint propagation
            startwave.CopyFrom(wave, propagator.Length, shannon);

            // Try to find a seed that leads to a complete, contradiction-free generation
            int? goodseed = GoodSeed();
            if (goodseed == null) return false;  // Couldn't find a valid seed

            // Initialize with the good seed
            random = new Random((int)goodseed);
            stacksize = 0;
            wave.CopyFrom(startwave, propagator.Length, shannon);
            firstgo = false;

            // Setup the output grid
            newgrid.Clear();
            ip.grid = newgrid;
            return true;
        }
        else
        {
            // Normal WFC step: find cell with lowest entropy, observe (collapse) it, and propagate
            int node = NextUnobservedNode(random);
            if (node >= 0)
            {
                Observe(node, random);
                Propagate();
            }
            else n++;  // No more cells to observe - WFC is complete

            // Update the output grid with current state
            if (n >= 0 || ip.gif) UpdateState();
            return true;
        }
    }

    // Try multiple seeds to find one that leads to a contradiction-free completion
    int? GoodSeed()
    {
        for (int k = 0; k < tries; k++)
        {
            int observationsSoFar = 0;
            int seed = ip.random.Next();  // Get a new random seed
            random = new Random(seed);
            stacksize = 0;
            wave.CopyFrom(startwave, propagator.Length, shannon);  // Reset to initial state

            while (true)
            {
                // Find a cell to collapse and observe it
                int node = NextUnobservedNode(random);
                if (node >= 0)
                {
                    Observe(node, random);
                    observationsSoFar++;
                    bool success = Propagate();
                    if (!success)
                    {
                        // Hit a contradiction - try next seed
                        Console.WriteLine($"CONTRADICTION on try {k} with {observationsSoFar} observations");
                        break;
                    }
                }
                else
                {
                    // Successfully completed with no contradictions
                    Console.WriteLine($"wfc found a good seed {seed} on try {k} with {observationsSoFar} observations");
                    return seed;
                }
            }
        }

        Console.WriteLine($"wfc failed to find a good seed in {tries} tries");
        return null;
    }

    // Find the cell with the lowest entropy (most constrained)
    int NextUnobservedNode(Random random)
    {
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;
        double min = 1E+4;
        int argmin = -1;

        // Scan all cells
        for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                {
                    // Skip boundary cells if not using periodic boundary conditions
                    if (!periodic && (x + N > MX || y + N > MY || z + 1 > MZ)) continue;

                    int i = x + y * MX + z * MX * MY;  // Flat index
                    int remainingValues = wave.sumsOfOnes[i];  // How many patterns are still possible

                    // Use Shannon entropy or simple count based on configuration
                    double entropy = shannon ? wave.entropies[i] : remainingValues;

                    // Find cell with lowest entropy (most constrained) that isn't fully collapsed
                    if (remainingValues > 1 && entropy <= min)
                    {
                        // Add small noise to break ties randomly
                        double noise = 1E-6 * random.NextDouble();
                        if (entropy + noise < min)
                        {
                            min = entropy + noise;
                            argmin = i;
                        }
                    }
                }
        return argmin;  // Return most constrained cell (-1 if all cells are determined)
    }

    // Collapse a cell to a single pattern based on weighted probabilities
    void Observe(int node, Random random)
    {
        bool[] w = wave.data[node];

        // Create probability distribution from valid patterns and their weights
        for (int t = 0; t < P; t++) distribution[t] = w[t] ? weights[t] : 0.0;

        // Pick a random pattern based on weights
        int r = distribution.Random(random.NextDouble());

        // Ban all patterns except the chosen one
        for (int t = 0; t < P; t++) if (w[t] != (t == r)) Ban(node, t);
    }

    // Propagate constraints to neighboring cells (core WFC algorithm)
    bool Propagate()
    {
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;

        // Process stack until empty
        while (stacksize > 0)
        {
            // Pop item from stack
            (int i1, int p1) = stack[stacksize - 1];
            stacksize--;

            // Convert flat index to 3D coordinates
            int x1 = i1 % MX, y1 = (i1 % (MX * MY)) / MX, z1 = i1 / (MX * MY);

            // Check implications in all directions
            for (int d = 0; d < propagator.Length; d++)
            {
                int dx = DX[d], dy = DY[d], dz = DZ[d];
                int x2 = x1 + dx, y2 = y1 + dy, z2 = z1 + dz;

                // Handle boundaries - skip or wrap depending on periodic setting
                if (!periodic && (x2 < 0 || y2 < 0 || z2 < 0 || x2 + N > MX || y2 + N > MY || z2 + 1 > MZ)) continue;

                if (x2 < 0) x2 += MX;
                else if (x2 >= MX) x2 -= MX;
                if (y2 < 0) y2 += MY;
                else if (y2 >= MY) y2 -= MY;
                if (z2 < 0) z2 += MZ;
                else if (z2 >= MZ) z2 -= MZ;

                int i2 = x2 + y2 * MX + z2 * MX * MY;  // Neighbor's flat index
                int[] p = propagator[d][p1];  // Patterns compatible with p1 in direction d
                int[][] compat = wave.compatible[i2];  // Compatibility counters for neighbor

                // Update compatibility counters for affected patterns
                for (int l = 0; l < p.Length; l++)
                {
                    int t2 = p[l];
                    int[] comp = compat[t2];

                    comp[d]--;  // Decrement counter for compatible patterns
                    if (comp[d] == 0) Ban(i2, t2);  // If no longer supported, ban the pattern
                }
            }
        }

        // Check if we've reached a contradiction (no valid patterns for some cell)
        return wave.sumsOfOnes[0] > 0;
    }

    // Ban a pattern from a cell and update entropy
    void Ban(int i, int t)
    {
        wave.data[i][t] = false;  // Mark pattern as impossible

        // Clear compatibility counters for this cell/pattern
        int[] comp = wave.compatible[i][t];
        for (int d = 0; d < propagator.Length; d++) comp[d] = 0;

        // Add to propagation stack
        stack[stacksize] = (i, t);
        stacksize++;

        wave.sumsOfOnes[i] -= 1;  // One fewer valid pattern

        // Update entropy calculations if using Shannon entropy
        if (shannon)
        {
            // Update entropy calculations - need to adjust for removed pattern
            double sum = wave.sumsOfWeights[i];
            wave.entropies[i] += wave.sumsOfWeightLogWeights[i] / sum - Math.Log(sum);

            wave.sumsOfWeights[i] -= weights[t];
            wave.sumsOfWeightLogWeights[i] -= weightLogWeights[t];

            sum = wave.sumsOfWeights[i];
            wave.entropies[i] -= wave.sumsOfWeightLogWeights[i] / sum - Math.Log(sum);
        }
    }

    // Child classes must implement how to update output grid from wave state
    protected abstract void UpdateState();

    // Direction vectors for 3D grid neighbors (right, up, left, down, forward, back)
    protected static int[] DX = { 1, 0, -1, 0, 0, 0 };
    protected static int[] DY = { 0, 1, 0, -1, 0, 0 };
    protected static int[] DZ = { 0, 0, 0, 0, 1, -1 };
}

// Helper class that maintains the wave function state
class Wave
{
    public bool[][] data;           // For each cell, which patterns are still possible
    public int[][][] compatible;    // Counts of compatible patterns in each direction

    public int[] sumsOfOnes;        // Count of possible patterns per cell
    // Fields for Shannon entropy calculation:
    public double[] sumsOfWeights, sumsOfWeightLogWeights, entropies;

    public Wave(int length, int P, int D, bool shannon)
    {
        data = AH.Array2D(length, P, true);  // All patterns possible initially
        compatible = AH.Array3D(length, P, D, -1);  // Compatibility counters
        sumsOfOnes = new int[length];  // Count of possible patterns

        // Allocate arrays for Shannon entropy if needed
        if (shannon)
        {
            sumsOfWeights = new double[length];
            sumsOfWeightLogWeights = new double[length];
            entropies = new double[length];
        }
    }

    // Initialize the wave to starting state
    public void Init(int[][][] propagator, double sumOfWeights, double sumOfWeightLogWeights, double startingEntropy, bool shannon)
    {
        int P = data[0].Length;
        for (int i = 0; i < data.Length; i++)
        {
            // All patterns possible initially
            for (int p = 0; p < P; p++)
            {
                data[i][p] = true;
                // Initialize compatibility counters - how many patterns support this one
                for (int d = 0; d < propagator.Length; d++) compatible[i][p][d] = propagator[opposite[d]][p].Length;
            }

            sumsOfOnes[i] = P;  // All patterns possible (P total)

            // Initialize entropy calculations
            if (shannon)
            {
                sumsOfWeights[i] = sumOfWeights;
                sumsOfWeightLogWeights[i] = sumOfWeightLogWeights;
                entropies[i] = startingEntropy;
            }
        }
    }

    // Copy state from another wave
    public void CopyFrom(Wave wave, int D, bool shannon)
    {
        for (int i = 0; i < data.Length; i++)
        {
            bool[] datai = data[i], wavedatai = wave.data[i];
            for (int t = 0; t < datai.Length; t++)
            {
                datai[t] = wavedatai[t];  // Copy possibility data
                // Copy compatibility counters
                for (int d = 0; d < D; d++) compatible[i][t][d] = wave.compatible[i][t][d];
            }

            sumsOfOnes[i] = wave.sumsOfOnes[i];  // Copy pattern counts

            // Copy entropy data
            if (shannon)
            {
                sumsOfWeights[i] = wave.sumsOfWeights[i];
                sumsOfWeightLogWeights[i] = wave.sumsOfWeightLogWeights[i];
                entropies[i] = wave.entropies[i];
            }
        }
    }

    // Mapping from direction to opposite direction (right↔left, up↔down, forward↔back)
    static readonly int[] opposite = { 2, 3, 0, 1, 5, 4 };
}

/*
=== SUMMARY ===

This code implements the core algorithm of Wave Function Collapse (WFC), a procedural generation technique that creates complex patterns following specific rules.

Imagine a grid where each cell can be filled with different patterns. Initially, all patterns are possible in each cell (like Schrödinger's cat being both alive and dead). The WFC algorithm gradually "collapses" this wave of possibilities:

1. It starts by picking the cell with the lowest "entropy" (fewest remaining options) - like solving a Sudoku by focusing on the most constrained squares.

2. It randomly selects one pattern for that cell based on weighted probabilities.

3. It "propagates" the consequences of this choice to neighboring cells, removing patterns that would conflict with the chosen one - like how placing a number in Sudoku eliminates that number from related rows/columns/boxes.

4. It repeats until all cells are determined or a contradiction is reached.

The algorithm includes several advanced features:
- Shannon entropy for more intelligent cell selection
- Pattern weights to make some outcomes more likely than others
- Multiple attempts to find a seed that leads to a complete solution
- Support for both 2D and 3D grids
- Support for periodic (wrapping) boundaries

This procedural generation approach can create textures, level designs, or other patterns that follow consistent local rules while maintaining global variety - much like natural processes create complex but ordered structures.
*/