// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

// AllNode: A specialized RuleNode that applies multiple rules simultaneously to a grid
class AllNode : RuleNode
{
    // Initializes the node from XML configuration
    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        // Initialize the base RuleNode properties first
        if (!base.Load(xelem, parentSymmetry, grid)) return false;
        // Storage for matches: each tuple contains (rule index, x, y, z) coordinates
        matches = new List<(int, int, int, int)>();
        // 2D array tracking which rules have been matched at which positions
        matchMask = AH.Array2D(rules.Length, grid.state.Length, false);
        return true;
    }

    // Attempts to apply rule r at position (x,y,z) if it doesn't conflict with cells marked in newstate
    void Fit(int r, int x, int y, int z, bool[] newstate, int MX, int MY)
    {
        Rule rule = rules[r];
        // Check if rule output conflicts with already occupied cells
        for (int dz = 0; dz < rule.OMZ; dz++) for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++)
                {
                    // Get the output value at this relative position in the rule
                    byte value = rule.output[dx + dy * rule.OMX + dz * rule.OMX * rule.OMY];
                    // If cell is non-wildcard (0xff is wildcard) and already occupied, rule can't be applied
                    if (value != 0xff && newstate[x + dx + (y + dy) * MX + (z + dz) * MX * MY]) return;
                }
        // Mark this rule as used in the current iteration
        last[r] = true;
        // Apply the rule by copying its output pattern to the grid
        for (int dz = 0; dz < rule.OMZ; dz++) for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++)
                {
                    // Get the value to place at this position
                    byte newvalue = rule.output[dx + dy * rule.OMX + dz * rule.OMX * rule.OMY];
                    // Only process non-wildcard cells
                    if (newvalue != 0xff)
                    {
                        // Calculate actual grid coordinates
                        int sx = x + dx, sy = y + dy, sz = z + dz;
                        // Calculate the 1D index in the grid array
                        int i = sx + sy * MX + sz * MX * MY;
                        // Mark this cell as occupied
                        newstate[i] = true;
                        // Set the new cell value
                        grid.state[i] = newvalue;
                        // Record this change for future processing
                        ip.changes.Add((sx, sy, sz));
                    }
                }
    }

    // Main execution method - applies all matching rules in one step
    override public bool Go()
    {
        // Run parent class Go() first and exit if it returns false
        if (!base.Go()) return false;
        // Record which iteration we last matched on
        lastMatchedTurn = ip.counter;

        // If using a pre-calculated trajectory, just apply the next state
        if (trajectory != null)
        {
            // Stop if we've reached the end of the trajectory
            if (counter >= trajectory.Length) return false;
            // Copy the pre-calculated state to the grid
            Array.Copy(trajectory[counter], grid.state, grid.state.Length);
            counter++;
            return true;
        }

        // If no rules matched, exit
        if (matchCount == 0) return false;

        int MX = grid.MX, MY = grid.MY;
        // If using potential fields to guide the generation
        if (potentials != null)
        {
            double firstHeuristic = 0;
            bool firstHeuristicComputed = false;

            // List to store matches with their heuristic values
            List<(int, double)> list = new();
            for (int m = 0; m < matchCount; m++)
            {
                var (r, x, y, z) = matches[m];
                // Calculate how this rule would affect the potential field
                double? heuristic = Field.DeltaPointwise(grid.state, rules[r], x, y, z, fields, potentials, grid.MX, grid.MY);
                if (heuristic != null)
                {
                    double h = (double)heuristic;
                    if (!firstHeuristicComputed)
                    {
                        firstHeuristic = h;
                        firstHeuristicComputed = true;
                    }
                    // Add randomization factor
                    double u = ip.random.NextDouble();
                    // Calculate rule priority based on heuristic and temperature
                    list.Add((m, temperature > 0 ? Math.Pow(u, Math.Exp((h - firstHeuristic) / temperature)) : -h + 0.001 * u));
                }
            }
            // Sort matches by their heuristic values
            (int, double)[] ordered = list.OrderBy(pair => -pair.Item2).ToArray();
            // Apply rules in priority order
            for (int k = 0; k < ordered.Length; k++)
            {
                var (r, x, y, z) = matches[ordered[k].Item1];
                // Mark this position as processed
                matchMask[r][x + y * MX + z * MX * MY] = false;
                // Apply the rule
                Fit(r, x, y, z, grid.mask, MX, MY);
            }
        }
        else
        {
            // Without potential fields, apply rules in random order
            int[] shuffle = new int[matchCount];
            shuffle.Shuffle(ip.random);
            for (int k = 0; k < shuffle.Length; k++)
            {
                var (r, x, y, z) = matches[shuffle[k]];
                // Mark position as processed
                matchMask[r][x + y * MX + z * MX * MY] = false;
                // Apply the rule
                Fit(r, x, y, z, grid.mask, MX, MY);
            }
        }

        // Clear mask for all cells that were changed in this iteration
        for (int n = ip.first[lastMatchedTurn]; n < ip.changes.Count; n++)
        {
            var (x, y, z) = ip.changes[n];
            grid.mask[x + y * MX + z * MX * MY] = false;
        }
        // Increment iteration counter
        counter++;
        // Reset match count for next iteration
        matchCount = 0;
        return true;
    }
}

/*
SUMMARY:
This AllNode class is part of a procedural generation system. It takes rules (like "if you see pattern X, 
replace it with pattern Y") and applies many of them at once to a grid.

Think of it like a painting program where instead of placing one pixel at a time, you're placing many 
stickers (rules) all over the canvas (grid) in one step. The class:

1. Finds all places where rules can be applied
2. Makes sure rules don't conflict (try to change the same cell)
3. Can use "potential fields" (like magnets) to prioritize which rules to apply first
4. Updates the grid with all changes

This is useful for quickly generating things like game levels, terrain, or patterns by applying 
many transformation rules simultaneously.
*/
