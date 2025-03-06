// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Collections.Generic;

static class Search
{
    // Main search algorithm to find a sequence of rule applications that transforms the present state to match the future state
    // Returns a sequence of states (trajectory) from present to future, or null if no solution is found
    public static byte[][] Run(byte[] present, int[] future, Rule[] rules, int MX, int MY, int MZ, int C, bool all, int limit, double depthCoefficient, int seed)
    {
        // Initialize potential arrays for heuristic calculations
        int[][] bpotentials = AH.Array2D(C, present.Length, -1);  // Backward potentials (distance from future)
        int[][] fpotentials = AH.Array2D(C, present.Length, -1);  // Forward potentials (distance from present)

        // Calculate initial heuristic estimates
        Observation.ComputeBackwardPotentials(bpotentials, future, MX, MY, MZ, rules);
        int rootBackwardEstimate = Observation.BackwardPointwise(bpotentials, present);
        Observation.ComputeForwardPotentials(fpotentials, present, MX, MY, MZ, rules);
        int rootForwardEstimate = Observation.ForwardPointwise(fpotentials, future);

        // Check if the problem is solvable
        if (rootBackwardEstimate < 0 || rootForwardEstimate < 0)
        {
            Console.WriteLine("INCORRECT PROBLEM");
            return null;
        }
        Console.WriteLine($"root estimate = ({rootBackwardEstimate}, {rootForwardEstimate})");

        // If present state already matches future, return empty trajectory
        if (rootBackwardEstimate == 0) return Array.Empty<byte[]>();

        // Create the initial search node (root board)
        Board rootBoard = new(present, -1, 0, rootBackwardEstimate, rootForwardEstimate);

        // Data structures for the A* search algorithm
        List<Board> database = new();  // Stores all visited boards
        database.Add(rootBoard);
        Dictionary<byte[], int> visited = new(new StateComparer());  // Tracks visited states to avoid cycles
        visited.Add(present, 0);

        // Priority queue for the search frontier, ordered by heuristic rank
        PriorityQueue<int, double> frontier = new();
        Random random = new(seed);
        frontier.Enqueue(0, rootBoard.Rank(random, depthCoefficient));
        int frontierLength = 1;

        // For tracking best solution so far
        int record = rootBackwardEstimate + rootForwardEstimate;

        // Main search loop
        while (frontierLength > 0 && (limit < 0 || database.Count < limit))
        {
            // Get the most promising board from the frontier
            int parentIndex = frontier.Dequeue();
            frontierLength--;
            Board parentBoard = database[parentIndex];

            // Generate child states by applying rules
            // "all" mode finds all non-overlapping rule applications, "one" mode applies one rule at a time
            var children = all ? parentBoard.state.AllChildStates(MX, MY, rules) : parentBoard.state.OneChildStates(MX, MY, rules);

            // Evaluate each child state
            foreach (var childState in children)
            {
                // Check if we've seen this state before
                bool success = visited.TryGetValue(childState, out int childIndex);
                if (success)
                {
                    // If we found a shorter path to an existing state
                    Board oldBoard = database[childIndex];
                    if (parentBoard.depth + 1 < oldBoard.depth)
                    {
                        // Update the existing board with the shorter path
                        oldBoard.depth = parentBoard.depth + 1;
                        oldBoard.parentIndex = parentIndex;

                        // Add it back to the frontier if it's still a valid board
                        if (oldBoard.backwardEstimate >= 0 && oldBoard.forwardEstimate >= 0)
                        {
                            frontier.Enqueue(childIndex, oldBoard.Rank(random, depthCoefficient));
                            frontierLength++;
                        }
                    }
                }
                else  // New state we haven't seen before
                {
                    // Calculate heuristic estimates for the new state
                    int childBackwardEstimate = Observation.BackwardPointwise(bpotentials, childState);
                    Observation.ComputeForwardPotentials(fpotentials, childState, MX, MY, MZ, rules);
                    int childForwardEstimate = Observation.ForwardPointwise(fpotentials, future);

                    // Skip invalid states
                    if (childBackwardEstimate < 0 || childForwardEstimate < 0) continue;

                    // Create a new board for this state
                    Board childBoard = new(childState, parentIndex, parentBoard.depth + 1, childBackwardEstimate, childForwardEstimate);
                    database.Add(childBoard);
                    childIndex = database.Count - 1;
                    visited.Add(childBoard.state, childIndex);

                    // If we reached the goal (forward estimate = 0), reconstruct and return the solution path
                    if (childBoard.forwardEstimate == 0)
                    {
                        Console.WriteLine($"found a trajectory of length {parentBoard.depth + 1}, visited {visited.Count} states");
                        List<Board> trajectory = Board.Trajectory(childIndex, database);
                        trajectory.Reverse();
                        return trajectory.Select(b => b.state).ToArray();
                    }
                    else
                    {
                        // Track the best partial solution so far (for debugging)
                        if (limit < 0 && childBackwardEstimate + childForwardEstimate <= record)
                        {
                            record = childBackwardEstimate + childForwardEstimate;
                            Console.WriteLine($"found a state of record estimate {record} = {childBackwardEstimate} + {childForwardEstimate}");
                            childState.Print(MX, MY);
                        }
                        // Add the new state to the frontier
                        frontier.Enqueue(childIndex, childBoard.Rank(random, depthCoefficient));
                        frontierLength++;
                    }
                }
            }
        }

        // No solution found within the search limit
        return null;
    }

    // Generates all possible child states by applying one rule at a time
    static List<byte[]> OneChildStates(this byte[] state, int MX, int MY, Rule[] rules)
    {
        List<byte[]> result = new();
        foreach (Rule rule in rules)
            for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                    if (Matches(rule, x, y, state, MX, MY)) result.Add(Applied(rule, x, y, state, MX));
        return result;
    }

    // Checks if a rule matches at a specific position in the grid
    static bool Matches(this Rule rule, int x, int y, byte[] state, int MX, int MY)
    {
        // Check if rule pattern fits within grid boundaries
        if (x + rule.IMX > MX || y + rule.IMY > MY) return false;

        // Check if each cell in the pattern matches the corresponding grid cell
        int dy = 0, dx = 0;
        for (int di = 0; di < rule.input.Length; di++)
        {
            // If rule input bitmask doesn't allow the state value at this position, rule doesn't match
            if ((rule.input[di] & (1 << state[x + dx + (y + dy) * MX])) == 0) return false;
            dx++;
            if (dx == rule.IMX) { dx = 0; dy++; }
        }
        return true;
    }

    // Creates a new state by applying a rule at the specified position
    static byte[] Applied(Rule rule, int x, int y, byte[] state, int MX)
    {
        byte[] result = new byte[state.Length];
        Array.Copy(state, result, state.Length);
        for (int dz = 0; dz < rule.OMZ; dz++) for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++)
                {
                    byte newValue = rule.output[dx + dy * rule.OMX + dz * rule.OMX * rule.OMY];
                    if (newValue != 0xff) result[x + dx + (y + dy) * MX] = newValue;  // 0xff means "don't change"
                }
        return result;
    }

    // Prints a 2D grid state for debugging
    static void Print(this byte[] state, int MX, int MY)
    {
        char[] characters = new[] { '.', 'R', 'W', '#', 'a', '!', '?', '%', '0', '1', '2', '3', '4', '5' };
        for (int y = 0; y < MY; y++)
        {
            for (int x = 0; x < MX; x++) Console.Write($"{characters[state[x + y * MX]]} ");
            Console.WriteLine();
        }
    }

    // Checks if a point is inside a rule's pattern area
    public static bool IsInside(this (int x, int y) p, Rule rule, int x, int y) =>
        x <= p.x && p.x < x + rule.IMX && y <= p.y && p.y < y + rule.IMY;

    // Checks if two rule patterns overlap when placed at specific positions
    public static bool Overlap(Rule rule0, int x0, int y0, Rule rule1, int x1, int y1)
    {
        for (int dy = 0; dy < rule0.IMY; dy++) for (int dx = 0; dx < rule0.IMX; dx++)
                if ((x0 + dx, y0 + dy).IsInside(rule1, x1, y1)) return true;
        return false;
    }

    // More sophisticated child state generation that applies multiple non-overlapping rules at once
    public static List<byte[]> AllChildStates(this byte[] state, int MX, int MY, Rule[] rules)
    {
        // Find all possible rule applications
        var list = new List<(Rule, int)>();
        int[] amounts = new int[state.Length];  // Counts how many rules cover each cell
        for (int i = 0; i < state.Length; i++)
        {
            int x = i % MX, y = i / MX;
            for (int r = 0; r < rules.Length; r++)
            {
                Rule rule = rules[r];
                if (rule.Matches(x, y, state, MX, MY))
                {
                    list.Add((rule, i));
                    // Increment counts for all cells covered by this rule
                    for (int dy = 0; dy < rule.IMY; dy++) for (int dx = 0; dx < rule.IMX; dx++) amounts[x + dx + (y + dy) * MX]++;
                }
            }
        }
        (Rule, int)[] tiles = list.ToArray();
        bool[] mask = AH.Array1D(tiles.Length, true);  // Tracks which rule applications are still available
        List<(Rule, int)> solution = new();  // Collects the set of non-overlapping rule applications

        List<byte[]> result = new();
        Enumerate(result, solution, tiles, amounts, mask, state, MX);
        return result;
    }

    // Recursive backtracking algorithm to find all possible combinations of non-overlapping rule applications
    static void Enumerate(List<byte[]> children, List<(Rule, int)> solution, (Rule, int)[] tiles, int[] amounts, bool[] mask, byte[] state, int MX)
    {
        // Find the cell with the most rule applications (greedy heuristic)
        int I = amounts.MaxPositiveIndex();
        int X = I % MX, Y = I / MX;

        // If no cells have rules covering them, we've found a complete solution
        if (I < 0)
        {
            children.Add(state.Apply(solution, MX));
            return;
        }

        // Find all rule applications that cover the selected cell
        List<(Rule, int)> cover = new();
        for (int l = 0; l < tiles.Length; l++)
        {
            var (rule, i) = tiles[l];
            if (mask[l] && (X, Y).IsInside(rule, i % MX, i / MX)) cover.Add((rule, i));
        }

        // Try each rule application
        foreach (var (rule, i) in cover)
        {
            solution.Add((rule, i));

            // Find all rule applications that overlap with the current one
            List<int> intersecting = new();
            for (int l = 0; l < tiles.Length; l++) if (mask[l])
                {
                    var (rule1, i1) = tiles[l];
                    if (Overlap(rule, i % MX, i / MX, rule1, i1 % MX, i1 / MX)) intersecting.Add(l);
                }

            // Temporarily hide overlapping rule applications
            foreach (int l in intersecting) Hide(l, false, tiles, amounts, mask, MX);
            // Recursively find more non-overlapping rule applications
            Enumerate(children, solution, tiles, amounts, mask, state, MX);
            // Restore overlapping rule applications
            foreach (int l in intersecting) Hide(l, true, tiles, amounts, mask, MX);

            solution.RemoveAt(solution.Count - 1);
        }
    }

    // Helper to hide/unhide a rule application and update cell coverage counts
    static void Hide(int l, bool unhide, (Rule, int)[] tiles, int[] amounts, bool[] mask, int MX)
    {
        mask[l] = unhide;
        var (rule, i) = tiles[l];
        int x = i % MX, y = i / MX;
        int incr = unhide ? 1 : -1;
        for (int dy = 0; dy < rule.IMY; dy++) for (int dx = 0; dx < rule.IMX; dx++) amounts[x + dx + (y + dy) * MX] += incr;
    }

    // Applies a rule to a state, modifying it in place
    static void Apply(this Rule rule, int x, int y, byte[] state, int MX)
    {
        for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++) state[x + dx + (y + dy) * MX] = rule.output[dx + dy * rule.OMX];
    }

    // Creates a new state by applying a list of rule applications
    static byte[] Apply(this byte[] state, List<(Rule, int)> solution, int MX)
    {
        byte[] result = new byte[state.Length];
        Array.Copy(state, result, state.Length);
        foreach (var (rule, i) in solution) Apply(rule, i % MX, i / MX, result, MX);
        return result;
    }
}

// Represents a state in the search space
class Board
{
    public byte[] state;               // The current grid state
    public int parentIndex;            // Index of the parent board in the database
    public int depth;                  // Number of steps from the root
    public int backwardEstimate;       // Estimated distance from the target state
    public int forwardEstimate;        // Estimated distance from the initial state

    public Board(byte[] state, int parentIndex, int depth, int backwardEstimate, int forwardEstimate)
    {
        this.state = state;
        this.parentIndex = parentIndex;
        this.depth = depth;
        this.backwardEstimate = backwardEstimate;
        this.forwardEstimate = forwardEstimate;
    }

    // Computes the heuristic rank for the A* search
    // Lower rank = higher priority in the frontier
    public double Rank(Random random, double depthCoefficient)
    {
        // Different ranking strategies based on depthCoefficient
        double result = depthCoefficient < 0.0 ? 1000 - depth : forwardEstimate + backwardEstimate + 2.0 * depthCoefficient * depth;
        // Add small random factor to break ties
        return result + 0.0001 * random.NextDouble();
    }

    // Reconstructs the path from a board back to the root
    public static List<Board> Trajectory(int index, List<Board> database)
    {
        List<Board> result = new();
        for (Board board = database[index]; board.parentIndex >= 0; board = database[board.parentIndex]) result.Add(board);
        return result;
    }
}

// Custom comparer for byte arrays to use in Dictionary
class StateComparer : IEqualityComparer<byte[]>
{
    // Checks if two states are identical
    public bool Equals(byte[] a, byte[] b)
    {
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // Computes a hash code for a state
    public int GetHashCode(byte[] a)
    {
        int result = 17;
        for (int i = 0; i < a.Length; i++) unchecked { result = result * 29 + a[i]; }
        return result;
    }
}

/*
========== SUMMARY ==========

This code implements a path-finding algorithm for transforming one grid state into another using a set of pattern-matching rules. Think of it like finding the best sequence of "moves" to solve a puzzle.

Imagine you have a puzzle game (like a sliding tile puzzle or Rubik's cube) where you start with one configuration and want to reach a target configuration. This algorithm finds the optimal sequence of moves to get there.

Here's what the Search class does in simple terms:

1. A* Search Algorithm: The main "Run" method uses a specialized version of the A* algorithm to find the shortest path from the present state to a state that satisfies the future constraints.
   - It uses two heuristics: backward (distance to goal) and forward (distance from start)
   - It maintains a priority queue of states to explore, always choosing the most promising next

2. State Generation: The code can generate next states in two different ways:
   - "OneChildStates": Applies a single rule at a time, creating many simple successor states
   - "AllChildStates": Uses a more complex algorithm to find combinations of non-overlapping rules that can be applied simultaneously

3. Pattern Matching: For each potential rule application, it checks if the rule's pattern matches at that position in the grid

4. Optimization Techniques:
   - Visited state tracking to avoid cycles
   - Priority queue to explore most promising states first
   - Heuristic estimates to guide the search toward the goal
   - Backtracking to find optimal combinations of non-overlapping rules

The Board class represents a state in the search, tracking its grid configuration, parent state, depth in the search tree, and heuristic estimates. The StateComparer helps efficiently detect duplicate states.

In practical terms, this algorithm could be used for procedural level generation, puzzle solving, or transforming one pattern into another while following specific rules - all through a smart, efficient search process.
*/