// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Xml.Linq;
using System.Collections.Generic;

class OneNode : RuleNode
{
    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        // Initialize using parent class's Load method
        if (!base.Load(xelem, parentSymmetry, grid)) return false;

        // Create list to store potential rule matches
        matches = new List<(int, int, int, int)>();

        // Create mask to track which rules have been checked at which positions
        matchMask = AH.Array2D(rules.Length, grid.state.Length, false);
        return true;
    }

    override public void Reset()
    {
        // Reset parent state
        base.Reset();

        // Only reset match data if we have matches
        if (matchCount != 0)
        {
            matchMask.Set2D(false);  // Clear match mask
            matchCount = 0;          // Reset match counter
        }
    }

    // Apply a rule at a specific position
    void Apply(Rule rule, int x, int y, int z)
    {
        int MX = grid.MX, MY = grid.MY;
        var changes = ip.changes;

        // Apply each cell in the output pattern
        for (int dz = 0; dz < rule.OMZ; dz++) for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++)
                {
                    // Get new value from rule output
                    byte newValue = rule.output[dx + dy * rule.OMX + dz * rule.OMX * rule.OMY];

                    // Skip cells marked as "don't change" (0xff)
                    if (newValue != 0xff)
                    {
                        // Calculate target position
                        int sx = x + dx;
                        int sy = y + dy;
                        int sz = z + dz;
                        int si = sx + sy * MX + sz * MX * MY;  // Flat index

                        byte oldValue = grid.state[si];
                        // Apply change if value is different
                        if (newValue != oldValue)
                        {
                            grid.state[si] = newValue;  // Update grid
                            changes.Add((sx, sy, sz));  // Track for rendering
                        }
                    }
                }
    }

    override public bool Go()
    {
        // Find matches using parent method
        if (!base.Go()) return false;

        // Record when this match happened
        lastMatchedTurn = ip.counter;

        // Handle trajectory-based execution (predetermined sequence)
        if (trajectory != null)
        {
            if (counter >= trajectory.Length) return false;  // End if we've used all steps

            // Copy predetermined state for this step
            Array.Copy(trajectory[counter], grid.state, grid.state.Length);
            counter++;
            return true;
        }

        // Choose a random match and apply it
        var (R, X, Y, Z) = RandomMatch(ip.random);
        if (R < 0) return false;  // No valid matches
        else
        {
            last[R] = true;  // Mark this rule as used
            Apply(rules[R], X, Y, Z);  // Apply the rule
            counter++;
            return true;
        }
    }

    // Select a random match, potentially using heuristics
    (int r, int x, int y, int z) RandomMatch(Random random)
    {
        // Use potentials-based selection (heuristic-guided)
        if (potentials != null)
        {
            // Check if we've reached the goal state (for observation-based execution)
            if (observations != null && Observation.IsGoalReached(grid.state, future))
            {
                futureComputed = false;
                return (-1, -1, -1, -1);  // Success - we're done
            }

            double max = -1000.0;
            int argmax = -1;

            // For temperature-based selection
            double firstHeuristic = 0.0;
            bool firstHeuristicComputed = false;

            // Check all potential matches
            for (int k = 0; k < matchCount; k++)
            {
                var (r, x, y, z) = matches[k];
                int i = x + y * grid.MX + z * grid.MX * grid.MY;

                // Validate match still applies (grid may have changed)
                if (!grid.Matches(rules[r], x, y, z))
                {
                    // Remove invalid match
                    matchMask[r][i] = false;
                    matches[k] = matches[matchCount - 1];
                    matchCount--;
                    k--;
                }
                else
                {
                    // Calculate heuristic value for this match
                    double? heuristic = Field.DeltaPointwise(grid.state, rules[r], x, y, z, fields, potentials, grid.MX, grid.MY);
                    if (heuristic == null) continue;  // Skip if heuristic couldn't be calculated

                    double h = (double)heuristic;

                    // Record first heuristic for temperature-based selection
                    if (!firstHeuristicComputed)
                    {
                        firstHeuristic = h;
                        firstHeuristicComputed = true;
                    }

                    double u = random.NextDouble();  // Random component

                    // Temperature-based probabilistic selection
                    // Higher temperature = more randomness, lower = more greedy
                    double key = temperature > 0 ?
                        Math.Pow(u, Math.Exp((h - firstHeuristic) / temperature)) :
                        -h + 0.001 * u;

                    // Track best match
                    if (key > max)
                    {
                        max = key;
                        argmax = k;
                    }
                }
            }

            // Return best match or (-1,-1,-1,-1) if none
            return argmax >= 0 ? matches[argmax] : (-1, -1, -1, -1);
        }
        else  // Simple uniform random selection
        {
            while (matchCount > 0)
            {
                // Pick a random match
                int matchIndex = random.Next(matchCount);

                var (r, x, y, z) = matches[matchIndex];
                int i = x + y * grid.MX + z * grid.MX * grid.MY;

                // Remove from consideration
                matchMask[r][i] = false;
                matches[matchIndex] = matches[matchCount - 1];
                matchCount--;

                // Verify match is still valid
                if (grid.Matches(rules[r], x, y, z)) return (r, x, y, z);
            }

            // No valid matches found
            return (-1, -1, -1, -1);
        }
    }
}

/*
=== SUMMARY ===

The OneNode class implements a rule-based generation approach that applies a single random rule at each step. Think of it like rolling a die and making changes based on the result.

Here's how it works in simple terms:

1. Rule Selection Process:
   - First, it collects all possible rules that match the current grid state
   - Then, it randomly selects one of these matches and applies it
   - Each rule can change multiple cells in the grid based on its output pattern

2. Two Selection Strategies:
   - Simple random: Picks any valid match with equal probability
   - Heuristic-guided: Uses "potentials" to guide selection toward a goal
     * Temperature parameter controls randomness vs. greediness
     * Higher temperature allows more exploration
     * Lower temperature focuses on best immediate improvements

3. Special Features:
   - Can follow a predetermined trajectory instead of making random choices
   - Can work toward a specific target state using observations
   - Tracks which rules were actually used

This approach is good for creating controlled randomness - like a painter who follows rules about what colors can go next to each other, but still makes random choices within those constraints. It produces results that follow local rules while maintaining global variety.
*/