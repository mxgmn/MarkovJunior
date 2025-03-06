// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Linq;
using System.Collections.Generic;

class Observation
{
    readonly byte from;    // Original cell value
    readonly int to;       // Target states bitmask (possible future values)

    // Constructor - maps character symbols to their internal representations
    public Observation(char from, string to, Grid grid)
    {
        this.from = grid.values[from];           // Convert symbol to byte value
        this.to = grid.Wave(to);                 // Convert string of symbols to bitmask
    }

    // Sets up the future states based on observations and validates all observations are used
    public static bool ComputeFutureSetPresent(int[] future, byte[] state, Observation[] observations)
    {
        // Track which observations are actually used
        bool[] mask = new bool[observations.Length];
        for (int k = 0; k < observations.Length; k++) if (observations[k] == null) mask[k] = true;

        // Process each cell in the grid
        for (int i = 0; i < state.Length; i++)
        {
            byte value = state[i];
            Observation obs = observations[value];
            mask[value] = true;                  // Mark this observation as present

            if (obs != null)
            {
                future[i] = obs.to;              // Set future possibilities
                state[i] = obs.from;             // Ensure state matches observation
            }
            else future[i] = 1 << value;         // If no observation, future = current
        }

        // Check if all non-null observations were used
        for (int k = 0; k < mask.Length; k++) if (!mask[k])
            {
                // Return false if some observation wasn't found in the grid
                return false;
            }
        return true;
    }

    // Calculate forward potentials - how many steps to reach each state from current
    public static void ComputeForwardPotentials(int[][] potentials, byte[] state, int MX, int MY, int MZ, Rule[] rules)
    {
        potentials.Set2D(-1);                    // Initialize all potentials as unreachable

        // Set potential 0 for current states
        for (int i = 0; i < state.Length; i++) potentials[state[i]][i] = 0;

        // Compute potentials (forward direction)
        ComputePotentials(potentials, MX, MY, MZ, rules, false);
    }

    // Calculate backward potentials - how many steps to reach desired future from each state
    public static void ComputeBackwardPotentials(int[][] potentials, int[] future, int MX, int MY, int MZ, Rule[] rules)
    {
        // Initialize potentials based on future states
        for (int c = 0; c < potentials.Length; c++)
        {
            int[] potential = potentials[c];
            // If the future allows this value, set potential to 0, otherwise unreachable
            for (int i = 0; i < future.Length; i++) potential[i] = (future[i] & (1 << c)) != 0 ? 0 : -1;
        }

        // Compute potentials (backward direction)
        ComputePotentials(potentials, MX, MY, MZ, rules, true);
    }

    // Core algorithm for computing potentials using breadth-first search
    static void ComputePotentials(int[][] potentials, int MX, int MY, int MZ, Rule[] rules, bool backwards)
    {
        // Queue for BFS, tracking cell value and position
        Queue<(byte c, int x, int y, int z)> queue = new();

        // Add all cells with potential 0 to the queue as starting points
        for (byte c = 0; c < potentials.Length; c++)
        {
            int[] potential = potentials[c];
            for (int i = 0; i < potential.Length; i++) if (potential[i] == 0) queue.Enqueue((c, i % MX, (i % (MX * MY)) / MX, i / (MX * MY)));
        }

        // Mask to avoid checking the same rule at the same position multiple times
        bool[][] matchMask = AH.Array2D(rules.Length, potentials[0].Length, false);

        // Breadth-first search to compute potentials
        while (queue.Any())
        {
            // Get next cell from queue
            (byte value, int x, int y, int z) = queue.Dequeue();
            int i = x + y * MX + z * MX * MY;    // Flat index
            int t = potentials[value][i];        // Current potential value

            // Check all rules
            for (int r = 0; r < rules.Length; r++)
            {
                bool[] maskr = matchMask[r];     // Mask for this rule
                Rule rule = rules[r];

                // Get positions where this value appears in the rule
                var shifts = backwards ? rule.oshifts[value] : rule.ishifts[value];

                // Try all possible placements of the rule
                for (int l = 0; l < shifts.Length; l++)
                {
                    var (shiftx, shifty, shiftz) = shifts[l];
                    int sx = x - shiftx;         // Compute rule origin position
                    int sy = y - shifty;
                    int sz = z - shiftz;

                    // Skip if rule would extend past grid boundaries
                    if (sx < 0 || sy < 0 || sz < 0 || sx + rule.IMX > MX || sy + rule.IMY > MY || sz + rule.IMZ > MZ) continue;

                    int si = sx + sy * MX + sz * MX * MY;  // Flat index of origin

                    // Check if rule applies at this position and hasn't been checked before
                    if (!maskr[si] && ForwardMatches(rule, sx, sy, sz, potentials, t, MX, MY, backwards))
                    {
                        maskr[si] = true;        // Mark as checked
                        // Apply rule effects
                        ApplyForward(rule, sx, sy, sz, potentials, t, MX, MY, queue, backwards);
                    }
                }
            }
        }
    }

    // Check if a rule matches at a given position based on potentials
    static bool ForwardMatches(Rule rule, int x, int y, int z, int[][] potentials, int t, int MX, int MY, bool backwards)
    {
        int dz = 0, dy = 0, dx = 0;
        byte[] a = backwards ? rule.output : rule.binput;  // Use different rule part based on direction

        // Check each cell in the rule
        for (int di = 0; di < a.Length; di++)
        {
            byte value = a[di];
            if (value != 0xff)  // 0xff means "any value"
            {
                // Get current potential for this cell
                int current = potentials[value][x + dx + (y + dy) * MX + (z + dz) * MX * MY];
                // Rule doesn't match if potential is higher than current step or unreachable
                if (current > t || current == -1) return false;
            }

            // Move to next position in rule
            dx++;
            if (dx == rule.IMX)
            {
                dx = 0; dy++;
                if (dy == rule.IMY) { dy = 0; dz++; }
            }
        }
        return true;  // All cells matched
    }

    // Apply rule effects to potentials and add affected cells to queue
    static void ApplyForward(Rule rule, int x, int y, int z, int[][] potentials, int t, int MX, int MY, Queue<(byte, int, int, int)> q, bool backwards)
    {
        // Use different rule part based on direction
        byte[] a = backwards ? rule.binput : rule.output;

        // Process each cell in the rule
        for (int dz = 0; dz < rule.IMZ; dz++)
        {
            int zdz = z + dz;
            for (int dy = 0; dy < rule.IMY; dy++)
            {
                int ydy = y + dy;
                for (int dx = 0; dx < rule.IMX; dx++)
                {
                    int xdx = x + dx;
                    int idi = xdx + ydy * MX + zdz * MX * MY;  // Flat index
                    int di = dx + dy * rule.IMX + dz * rule.IMX * rule.IMY;  // Rule index
                    byte o = a[di];

                    // Update potential for unreachable cells
                    if (o != 0xff && potentials[o][idi] == -1)
                    {
                        potentials[o][idi] = t + 1;  // Set new potential
                        q.Enqueue((o, xdx, ydy, zdz));  // Add to queue for further processing
                    }
                }
            }
        }
    }

    // Check if current state matches desired future state
    public static bool IsGoalReached(byte[] present, int[] future)
    {
        for (int i = 0; i < present.Length; i++)
            // Check if current value is in the allowed future values
            if (((1 << present[i]) & future[i]) == 0) return false;
        return true;
    }

    // Calculate total forward potential (current → future)
    public static int ForwardPointwise(int[][] potentials, int[] future)
    {
        int sum = 0;
        for (int i = 0; i < future.Length; i++)
        {
            int f = future[i];
            int min = 1000, argmin = -1;

            // Find minimum potential among allowed future values
            for (int c = 0; c < potentials.Length; c++, f >>= 1)
            {
                int potential = potentials[c][i];
                if ((f & 1) == 1 && potential >= 0 && potential < min)
                {
                    min = potential;
                    argmin = c;
                }
            }

            // If no valid potential found, goal is unreachable
            if (argmin < 0) return -1;
            sum += min;  // Add to total potential
        }
        return sum;
    }

    // Calculate total backward potential (future → current)
    public static int BackwardPointwise(int[][] potentials, byte[] present)
    {
        int sum = 0;
        for (int i = 0; i < present.Length; i++)
        {
            int potential = potentials[present[i]][i];
            // If any cell is unreachable, goal is unreachable
            if (potential < 0) return -1;
            sum += potential;  // Add to total potential
        }
        return sum;
    }
}

/*
=== SUMMARY ===

The Observation class is the pathfinding engine for the WFC algorithm. It calculates how to transform one grid state into another by applying rules.

Think of it like a GPS navigation system for grid states:

1. It maps the current state ("where you are") and desired future state ("where you want to go")

2. It calculates "potentials" - like a distance map showing how many steps are needed to reach:
   - Forward potentials: steps from current state to reach any other state
   - Backward potentials: steps from any state to reach the desired future state

3. It uses a breadth-first search algorithm (like ripples spreading from stones dropped in water):
   - Starting from known states (potential = 0)
   - Applying rules to discover reachable states
   - Assigning increasing potential values (1, 2, 3...) based on number of steps

4. It provides functions to:
   - Check if the goal has been reached
   - Calculate the minimum steps needed to reach the goal
   - Determine if a goal is reachable at all

This class helps the algorithm find the shortest path through the space of possible grid states, similar to how pathfinding works in games but operating on entire grid configurations rather than single-point locations.
*/