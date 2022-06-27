// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Attempts to find a trajectory of grid states, starting from the present
/// state and ending in a state that matches the future goal. Each state in the
/// trajectory is the result of applying one of the given rules to the previous
/// state.
/// </summary>
static class Search
{
    /// <summary>
    /// <inheritdoc cref="Search" path="/summary"/>
    /// </summary>
    /// <param name="present">The present state to begin searching from.</param>
    /// <param name="future">The future goal, as an array of bitmasks of colors.</param>
    /// <param name="rules">The array of rewrite rules which can be applied.</param>
    /// <param name="MX"><inheritdoc cref="Grid.MX" path="/summary"/></param>
    /// <param name="MY"><inheritdoc cref="Grid.MY" path="/summary"/></param>
    /// <param name="MZ"><inheritdoc cref="Grid.MZ" path="/summary"/></param>
    /// <param name="C"><inheritdoc cref="Grid.C" path="/summary"/></param>
    /// <param name="all">If <c>true</c>, the rules will be applied like an <see cref="AllNode">'all' node</see>; otherwise they will be applied like a <see cref="OneNode">'one' node</see>.</param>
    /// <param name="limit"><inheritdoc cref="RuleNode.limit" path="/summary"/></param>
    /// <param name="depthCoefficient"><inheritdoc cref="RuleNode.depthCoefficient" path="/summary"/></param>
    /// <param name="seed">A seed for the PRNG used by the search algorithm.</param>
    /// <returns>The trajectory as an array of grid states, or <c>null</c> if no trajectory is found. The present state is not included in the trajectory.</returns>
    public static byte[][] Run(byte[] present, int[] future, Rule[] rules, int MX, int MY, int MZ, int C, bool all, int limit, double depthCoefficient, int seed)
    {
        //Console.WriteLine("START SEARCH");
        //present.Print(MX, MY);
        
        // compute the backward and forward potentials, which will be used to compute the A* search heuristic
        int[][] bpotentials = AH.Array2D(C, present.Length, -1);
        int[][] fpotentials = AH.Array2D(C, present.Length, -1);

        Observation.ComputeBackwardPotentials(bpotentials, future, MX, MY, MZ, rules);
        int rootBackwardEstimate = Observation.BackwardPointwise(bpotentials, present);
        Observation.ComputeForwardPotentials(fpotentials, present, MX, MY, MZ, rules);
        int rootForwardEstimate = Observation.ForwardPointwise(fpotentials, future);

        // if either of these estimates are -1 then the goal is definitely not reachable
        if (rootBackwardEstimate < 0 || rootForwardEstimate < 0)
        {
            Console.WriteLine("INCORRECT PROBLEM");
            return null;
        }
        Console.WriteLine($"root estimate = ({rootBackwardEstimate}, {rootForwardEstimate})");
        
        // if the present state already matches the future, there is no need to search
        if (rootBackwardEstimate == 0) return Array.Empty<byte[]>();
        
        Board rootBoard = new(present, -1, 0, rootBackwardEstimate, rootForwardEstimate);

        // list of all grid states which have been considered so far in the search
        List<Board> database = new();
        database.Add(rootBoard);
        
        // associates each grid state with its index in the database list
        Dictionary<byte[], int> visited = new(new StateComparer());
        visited.Add(present, 0);

        PriorityQueue<int, double> frontier = new();
        Random random = new(seed);
        frontier.Enqueue(0, rootBoard.Rank(random, depthCoefficient));
        int frontierLength = 1;

        int record = rootBackwardEstimate + rootForwardEstimate;
        while (frontierLength > 0 && (limit < 0 || database.Count < limit))
        {
            int parentIndex = frontier.Dequeue();
            frontierLength--;
            Board parentBoard = database[parentIndex];
            //Console.WriteLine("-----------------------------------------------------------------------------------");
            //Console.WriteLine($"extracting board at depth {parentBoard.depth} and estimate ({parentBoard.backwardEstimate}, {parentBoard.forwardEstimate}):");
            //parentBoard.state.Print(MX, MY);

            var children = all ? parentBoard.state.AllChildStates(MX, MY, rules) : parentBoard.state.OneChildStates(MX, MY, rules);
            //Console.WriteLine($"this board has {children.Length} children");
            foreach (var childState in children)
            //for (int c = 0; c < children.Length; c++)
            {
                //byte[] childState = children[c];
                bool success = visited.TryGetValue(childState, out int childIndex);
                if (success)
                {
                    // this state has been considered before, but we might have found a shorter route to it
                    Board oldBoard = database[childIndex];
                    if (parentBoard.depth + 1 < oldBoard.depth)
                    {
                        //Console.WriteLine($"found a shorter {parentBoard.depth + 1}-route to an existing {oldBoard.depth}-board of estimate ({oldBoard.backwardEstimate}, {oldBoard.forwardEstimate})");
                        oldBoard.depth = parentBoard.depth + 1;
                        oldBoard.parentIndex = parentIndex;

                        if (oldBoard.backwardEstimate >= 0 && oldBoard.forwardEstimate >= 0)
                        {
                            frontier.Enqueue(childIndex, oldBoard.Rank(random, depthCoefficient));
                            frontierLength++;
                        }
                    }
                    //else Console.WriteLine($"found a longer {parentBoard.depth + 1}-route to an existing {oldBoard.depth}-board of estimate ({oldBoard.backwardEstimate}, {oldBoard.forwardEstimate})");
                }
                else
                {
                    // this state hasn't been considered before
                    int childBackwardEstimate = Observation.BackwardPointwise(bpotentials, childState);
                    Observation.ComputeForwardPotentials(fpotentials, childState, MX, MY, MZ, rules);
                    int childForwardEstimate = Observation.ForwardPointwise(fpotentials, future);

                    //Console.WriteLine($"child {c} has estimate ({childBackwardEstimate}, {childForwardEstimate}):");
                    //childState.Print(MX, MY);
                    if (childBackwardEstimate < 0 || childForwardEstimate < 0) continue;

                    Board childBoard = new(childState, parentIndex, parentBoard.depth + 1, childBackwardEstimate, childForwardEstimate);
                    database.Add(childBoard);
                    childIndex = database.Count - 1;
                    visited.Add(childBoard.state, childIndex);

                    if (childBoard.forwardEstimate == 0)
                    {
                        Console.WriteLine($"found a trajectory of length {parentBoard.depth + 1}, visited {visited.Count} states");
                        List<Board> trajectory = Board.Trajectory(childIndex, database);
                        trajectory.Reverse();
                        return trajectory.Select(b => b.state).ToArray();
                    }
                    else
                    {
                        if (limit < 0 && childBackwardEstimate + childForwardEstimate <= record)
                        {
                            record = childBackwardEstimate + childForwardEstimate;
                            Console.WriteLine($"found a state of record estimate {record} = {childBackwardEstimate} + {childForwardEstimate}");
                            childState.Print(MX, MY);
                        }
                        frontier.Enqueue(childIndex, childBoard.Rank(random, depthCoefficient));
                        frontierLength++;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a list of the states which can be reached from this state in
    /// one step by executing a 'one' node with the given rules.
    /// </summary>
    static List<byte[]> OneChildStates(this byte[] state, int MX, int MY, Rule[] rules)
    {
        List<byte[]> result = new();
        foreach (Rule rule in rules)
            for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                    if (Matches(rule, x, y, state, MX, MY)) result.Add(Applied(rule, x, y, state, MX));
        return result;
    }
    
    /// <summary>
    /// Determines whether this rule matches at position (x, y) in the given
    /// grid state.
    /// </summary>
    static bool Matches(this Rule rule, int x, int y, byte[] state, int MX, int MY)
    {
        if (x + rule.IMX > MX || y + rule.IMY > MY) return false;

        int dy = 0, dx = 0;
        // попробовать binput, но в этот раз здесь тоже заменить!
        // try binput, but this time replace here too!
        for (int di = 0; di < rule.input.Length; di++)
        {
            if ((rule.input[di] & (1 << state[x + dx + (y + dy) * MX])) == 0) return false;
            dx++;
            if (dx == rule.IMX) { dx = 0; dy++; }
        }
        return true;
    }
    
    /// <summary>
    /// Returns a new state in which the given rule has been applied at
    /// position (x, y).
    /// </summary>
    static byte[] Applied(Rule rule, int x, int y, byte[] state, int MX)
    {
        byte[] result = new byte[state.Length];
        Array.Copy(state, result, state.Length);
        for (int dz = 0; dz < rule.OMZ; dz++) for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++)
                {
                    byte newValue = rule.output[dx + dy * rule.OMX + dz * rule.OMX * rule.OMY];
                    if (newValue != 0xff) result[x + dx + (y + dy) * MX] = newValue;
                }
        return result;
    }
    
    /// <summary>
    /// Prints a textual representation of the grid state to the console.
    /// </summary>
    static void Print(this byte[] state, int MX, int MY)
    {
        char[] characters = new[] { '.', 'R', 'W', '#', 'a', '!', '?', '%', '0', '1', '2', '3', '4', '5' };
        for (int y = 0; y < MY; y++)
        {
            for (int x = 0; x < MX; x++) Console.Write($"{characters[state[x + y * MX]]} ");
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Determines whether this (x, y) tuple is inside the input pattern of the
    /// given rule at the given position.
    /// </summary>
    public static bool IsInside(this (int x, int y) p, Rule rule, int x, int y) =>
        x <= p.x && p.x < x + rule.IMX && y <= p.y && p.y < y + rule.IMY;
    
    /// <summary>
    /// Indicates whether two rules' input patterns overlap, at the given
    /// positions.
    /// </summary>
    public static bool Overlap(Rule rule0, int x0, int y0, Rule rule1, int x1, int y1)
    {
        for (int dy = 0; dy < rule0.IMY; dy++) for (int dx = 0; dx < rule0.IMX; dx++)
                if ((x0 + dx, y0 + dy).IsInside(rule1, x1, y1)) return true;
        return false;
    }

    /// <summary>
    /// Returns a list of the states which can be reached from this state in
    /// one step by executing an 'all' node with the given rules.
    /// </summary>
    public static List<byte[]> AllChildStates(this byte[] state, int MX, int MY, Rule[] rules)
    {
        var list = new List<(Rule, int)>();
        int[] amounts = new int[state.Length];
        for (int i = 0; i < state.Length; i++)
        {
            int x = i % MX, y = i / MX;
            for (int r = 0; r < rules.Length; r++)
            {
                Rule rule = rules[r];
                if (rule.Matches(x, y, state, MX, MY))
                {
                    list.Add((rule, i));
                    for (int dy = 0; dy < rule.IMY; dy++) for (int dx = 0; dx < rule.IMX; dx++) amounts[x + dx + (y + dy) * MX]++;
                }
            }
        }
        (Rule, int)[] tiles = list.ToArray();
        bool[] mask = AH.Array1D(tiles.Length, true);
        List<(Rule, int)> solution = new();

        List<byte[]> result = new();
        Enumerate(result, solution, tiles, amounts, mask, state, MX);
        return result;
    }

    /// <summary>
    /// Finds and applies all maximal non-overlapping sets of matches for the
    /// rules of an 'all' node, by a recursive backtracking search.
    /// </summary>
    /// <param name="children">The list of grid states which result from applying solutions, which is appended to when a solution is found.</param>
    /// <param name="solution">The partial solution in the backtracking search.</param>
    /// <param name="tiles">An array of all pairs (r, x + y * MX) where rule <c>r</c> matches at position (x, y) in the grid.</param>
    /// <param name="amounts">A flat array mapping each (x + y * MX) to the number of non-hidden matches which overlap the position (x, y).</param>
    /// <param name="mask">An array of flags for each match in <c>tiles</c> indicating whether it is available or hidden.</param>
    /// <param name="state">The current grid state.</param>
    /// <param name="MX"><inheritdoc cref="Grid.MX" path="/summary"/></param>
    static void Enumerate(List<byte[]> children, List<(Rule, int)> solution, (Rule, int)[] tiles, int[] amounts, bool[] mask, byte[] state, int MX)
    {
        int I = amounts.MaxPositiveIndex();
        int X = I % MX, Y = I / MX;
        if (I < 0)
        {
            children.Add(state.Apply(solution, MX));
            return;
        }

        List<(Rule, int)> cover = new();
        for (int l = 0; l < tiles.Length; l++)
        {
            var (rule, i) = tiles[l];
            if (mask[l] && (X, Y).IsInside(rule, i % MX, i / MX)) cover.Add((rule, i));
        }

        foreach (var (rule, i) in cover)
        {
            // add this match to the partial solution
            solution.Add((rule, i));
            
            // find all intersecting matches, including the current one
            List<int> intersecting = new();
            for (int l = 0; l < tiles.Length; l++) if (mask[l])
                {
                    var (rule1, i1) = tiles[l];
                    if (Overlap(rule, i % MX, i / MX, rule1, i1 % MX, i1 / MX)) intersecting.Add(l);
                }
            
            // recurse
            foreach (int l in intersecting) Hide(l, false, tiles, amounts, mask, MX);
            Enumerate(children, solution, tiles, amounts, mask, state, MX);
            foreach (int l in intersecting) Hide(l, true, tiles, amounts, mask, MX);

            // backtrack
            solution.RemoveAt(solution.Count - 1);
        }
    }

    /// <summary>
    /// Updates the backtracking search state in the <c>Enumerate</c> method to
    /// hide or unhide the rule match at index <c>l</c>. Hidden matches cannot
    /// be added to the current partial solution.
    /// </summary>
    static void Hide(int l, bool unhide, (Rule, int)[] tiles, int[] amounts, bool[] mask, int MX)
    {
        mask[l] = unhide;
        var (rule, i) = tiles[l];
        int x = i % MX, y = i / MX;
        int incr = unhide ? 1 : -1;
        for (int dy = 0; dy < rule.IMY; dy++) for (int dx = 0; dx < rule.IMX; dx++) amounts[x + dx + (y + dy) * MX] += incr;
    }

    /// <summary>
    /// Applies a rule at the given position in the grid state, in-place.
    /// </summary>
    static void Apply(this Rule rule, int x, int y, byte[] state, int MX)
    {
        for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++)
                {
                    byte c = rule.output[dx + dy * rule.OMX];
                    if(c != 0xff) state[x + dx + (y + dy) * MX] = c;
                }
    }
    
    /// <summary>
    /// Applies a set of non-overlapping rule matches to this grid state,
    /// returning a new grid state.
    /// </summary>
    static byte[] Apply(this byte[] state, List<(Rule, int)> solution, int MX)
    {
        byte[] result = new byte[state.Length];
        Array.Copy(state, result, state.Length);
        foreach (var (rule, i) in solution) Apply(rule, i % MX, i / MX, result, MX);
        return result;
    }
}

/// <summary>
/// A grid state considered by an <see cref="Search">A* search</see>.
/// </summary>
class Board
{
    /// <summary>
    /// The grid state.
    /// </summary>
    public byte[] state;
    
    /// <summary>
    /// The index of this state's parent in the database.
    /// </summary>
    public int parentIndex;
    
    /// <summary>
    /// The number of steps between this state and the root state.
    /// </summary>
    public int depth;
    
    /// <summary>
    /// The 'score' for this grid state, computed from the backward potentials.
    /// </summary>
    public int backwardEstimate;
    
    /// <summary>
    /// The 'score' for this grid state, computed from the forward potentials.
    /// </summary>
    public int forwardEstimate;

    /// <param name="state"><inheritdoc cref="Board.state" path="/summary"/></param>
    /// <param name="parentIndex"><inheritdoc cref="Board.parentIndex" path="/summary"/></param>
    /// <param name="depth"><inheritdoc cref="Board.depth" path="/summary"/></param>
    /// <param name="backwardEstimate"><inheritdoc cref="Board.backwardEstimate" path="/summary"/></param>
    /// <param name="forwardEstimate"><inheritdoc cref="Board.forwardEstimate" path="/summary"/></param>
    public Board(byte[] state, int parentIndex, int depth, int backwardEstimate, int forwardEstimate)
    {
        this.state = state;
        this.parentIndex = parentIndex;
        this.depth = depth;
        this.backwardEstimate = backwardEstimate;
        this.forwardEstimate = forwardEstimate;
    }

    /// <summary>
    /// Returns the priority of this grid state in the A* search, including a
    /// small noise term to differentiate states of otherwise equal priority.
    /// </summary>
    /// <param name="random">A PRNG instance which will be used to add a small noise term.</param>
    /// <param name="depthCoefficient"><inheritdoc cref="RuleNode.depthCoefficient" path="/summary"/></param>
    public double Rank(Random random, double depthCoefficient)
    {
        // A* search prioritises nodes by (distance from start to here + min goal distance from here)
        // `depth` is the distance from the start
        // `forwardEstimate` and `backwardEstimate` are heuristic estimates of the min goal distance
        double result = depthCoefficient < 0.0 ? 1000 - depth : forwardEstimate + backwardEstimate + 2.0 * depthCoefficient * depth;
        return result + 0.0001 * random.NextDouble();
    }

    /// <summary>
    /// Computes the trajectory to the grid state at the given index in the
    /// database, as a list in reverse order.
    /// </summary>
    public static List<Board> Trajectory(int index, List<Board> database)
    {
        List<Board> result = new();
        for (Board board = database[index]; board.parentIndex >= 0; board = database[board.parentIndex]) result.Add(board);
        return result;
    }
}

class StateComparer : IEqualityComparer<byte[]>
{
    public bool Equals(byte[] a, byte[] b)
    {
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    public int GetHashCode(byte[] a)
    {
        int result = 17;
        for (int i = 0; i < a.Length; i++) unchecked { result = result * 29 + a[i]; }
        return result;
    }
}
