// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)
using System.Xml.Linq;
class ParallelNode : RuleNode
{
    byte[] newstate;    // Buffer for storing new cell values before applying them

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        // Call parent class's Load method first
        if (!base.Load(xelem, parentSymmetry, grid)) return false;

        // Create buffer for new state with same size as grid
        newstate = new byte[grid.state.Length];
        return true;
    }

    // Processes a rule match and buffers changes (does not apply immediately)
    override protected void Add(int r, int x, int y, int z, bool[] maskr)
    {
        Rule rule = rules[r];

        // Random chance to skip applying this rule (based on probability)
        if (ip.random.NextDouble() > rule.p) return;

        // Mark this rule as matched in this iteration
        last[r] = true;

        int MX = grid.MX, MY = grid.MY;

        // Process each cell in the output pattern
        for (int dz = 0; dz < rule.OMZ; dz++) for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++)
                {
                    // Get new value from rule output
                    byte newvalue = rule.output[dx + dy * rule.OMX + dz * rule.OMX * rule.OMY];

                    // Calculate target cell index
                    int idi = x + dx + (y + dy) * MX + (z + dz) * MX * MY;

                    // Apply change if the new value is defined (not 0xff) and different from current
                    if (newvalue != 0xff && newvalue != grid.state[idi])
                    {
                        // Buffer the change (don't apply immediately)
                        newstate[idi] = newvalue;

                        // Record the change for tracking
                        ip.changes.Add((x + dx, y + dy, z + dz));
                    }
                }

        // Increment match counter
        matchCount++;
    }

    // Execute one step of parallel rule application
    public override bool Go()
    {
        // Process all rule matches first (via parent class's Go method)
        if (!base.Go()) return false;

        // Apply all buffered changes at once
        // Only process changes from current step (using the first index tracker)
        for (int n = ip.first[ip.counter]; n < ip.changes.Count; n++)
        {
            var (x, y, z) = ip.changes[n];
            int i = x + y * grid.MX + z * grid.MX * grid.MY;

            // Apply the buffered change to the actual grid
            grid.state[i] = newstate[i];
        }

        // Increment step counter
        counter++;

        // Return true if any rules matched (continue execution)
        return matchCount > 0;
    }
}

/*
=== SUMMARY ===

The ParallelNode class implements simultaneous rule application in the WFC algorithm. It's like processing a grid transformation where all changes happen at once rather than one after another.

Think of it like Conway's Game of Life or other cellular automata where:

1. First, you look at the current state and decide all the changes that need to happen
2. Then, you apply all those changes at the same time

This parallel approach is important because:

- It prevents earlier rule applications from affecting what later rules match (all rules see the same initial state)
- It avoids order-dependency issues where the sequence of rule application would change the outcome
- It models systems where all parts evolve simultaneously rather than sequentially

The key mechanics:
- The node buffers all changes in a separate "newstate" array
- It tracks which cells are modified but doesn't apply changes immediately
- Only after all rules have been checked does it apply all the buffered changes at once
- Rules have a probability factor (p) determining if they apply when matched

This parallel update model is essential for simulating many natural and artificial systems like physical simulations, cellular automata, and certain types of pattern generation where simultaneous updates are more realistic than sequential ones.
*/