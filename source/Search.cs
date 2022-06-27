// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Collections.Generic;

static class Search
{
    public static byte[][] Run(byte[] present, int[] future, Rule[] rules, int MX, int MY, int MZ, int C, bool all, int limit, double depthCoefficient, int seed)
    {
        //Console.WriteLine("START SEARCH");
        //present.Print(MX, MY);
        int[][] bpotentials = AH.Array2D(C, present.Length, -1);
        int[][] fpotentials = AH.Array2D(C, present.Length, -1);

        Observation.ComputeBackwardPotentials(bpotentials, future, MX, MY, MZ, rules);
        int rootBackwardEstimate = Observation.BackwardPointwise(bpotentials, present);
        Observation.ComputeForwardPotentials(fpotentials, present, MX, MY, MZ, rules);
        int rootForwardEstimate = Observation.ForwardPointwise(fpotentials, future);

        if (rootBackwardEstimate < 0 || rootForwardEstimate < 0)
        {
            Console.WriteLine("INCORRECT PROBLEM");
            return null;
        }
        Console.WriteLine($"root estimate = ({rootBackwardEstimate}, {rootForwardEstimate})");
        if (rootBackwardEstimate == 0) return Array.Empty<byte[]>();
        Board rootBoard = new(present, -1, 0, rootBackwardEstimate, rootForwardEstimate);

        List<Board> database = new();
        database.Add(rootBoard);
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

    static List<byte[]> OneChildStates(this byte[] state, int MX, int MY, Rule[] rules)
    {
        List<byte[]> result = new();
        foreach (Rule rule in rules)
            for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                    if (Matches(rule, x, y, state, MX, MY)) result.Add(Applied(rule, x, y, state, MX));
        return result;
    }

    static bool Matches(this Rule rule, int x, int y, byte[] state, int MX, int MY)
    {
        if (x + rule.IMX > MX || y + rule.IMY > MY) return false;

        int dy = 0, dx = 0;
        for (int di = 0; di < rule.input.Length; di++) //попробовать binput, но в этот раз здесь тоже заменить!
        {
            if ((rule.input[di] & (1 << state[x + dx + (y + dy) * MX])) == 0) return false;
            dx++;
            if (dx == rule.IMX) { dx = 0; dy++; }
        }
        return true;
    }

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

    static void Print(this byte[] state, int MX, int MY)
    {
        char[] characters = new[] { '.', 'R', 'W', '#', 'a', '!', '?', '%', '0', '1', '2', '3', '4', '5' };
        for (int y = 0; y < MY; y++)
        {
            for (int x = 0; x < MX; x++) Console.Write($"{characters[state[x + y * MX]]} ");
            Console.WriteLine();
        }
    }

    public static bool IsInside(this (int x, int y) p, Rule rule, int x, int y) =>
        x <= p.x && p.x < x + rule.IMX && y <= p.y && p.y < y + rule.IMY;
    public static bool Overlap(Rule rule0, int x0, int y0, Rule rule1, int x1, int y1)
    {
        for (int dy = 0; dy < rule0.IMY; dy++) for (int dx = 0; dx < rule0.IMX; dx++)
                if ((x0 + dx, y0 + dy).IsInside(rule1, x1, y1)) return true;
        return false;
    }

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
            solution.Add((rule, i));

            List<int> intersecting = new();
            for (int l = 0; l < tiles.Length; l++) if (mask[l])
                {
                    var (rule1, i1) = tiles[l];
                    if (Overlap(rule, i % MX, i / MX, rule1, i1 % MX, i1 / MX)) intersecting.Add(l);
                }

            foreach (int l in intersecting) Hide(l, false, tiles, amounts, mask, MX);
            Enumerate(children, solution, tiles, amounts, mask, state, MX);
            foreach (int l in intersecting) Hide(l, true, tiles, amounts, mask, MX);

            solution.RemoveAt(solution.Count - 1);
        }
    }

    static void Hide(int l, bool unhide, (Rule, int)[] tiles, int[] amounts, bool[] mask, int MX)
    {
        mask[l] = unhide;
        var (rule, i) = tiles[l];
        int x = i % MX, y = i / MX;
        int incr = unhide ? 1 : -1;
        for (int dy = 0; dy < rule.IMY; dy++) for (int dx = 0; dx < rule.IMX; dx++) amounts[x + dx + (y + dy) * MX] += incr;
    }

    static void Apply(this Rule rule, int x, int y, byte[] state, int MX)
    {
        for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++) state[x + dx + (y + dy) * MX] = rule.output[dx + dy * rule.OMX];
    }
    static byte[] Apply(this byte[] state, List<(Rule, int)> solution, int MX)
    {
        byte[] result = new byte[state.Length];
        Array.Copy(state, result, state.Length);
        foreach (var (rule, i) in solution) Apply(rule, i % MX, i / MX, result, MX);
        return result;
    }
}

class Board
{
    public byte[] state;
    public int parentIndex, depth, backwardEstimate, forwardEstimate;

    public Board(byte[] state, int parentIndex, int depth, int backwardEstimate, int forwardEstimate)
    {
        this.state = state;
        this.parentIndex = parentIndex;
        this.depth = depth;
        this.backwardEstimate = backwardEstimate;
        this.forwardEstimate = forwardEstimate;
    }

    public double Rank(Random random, double depthCoefficient)
    {
        double result = depthCoefficient < 0.0 ? 1000 - depth : forwardEstimate + backwardEstimate + 2.0 * depthCoefficient * depth;
        return result + 0.0001 * random.NextDouble();
    }

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
