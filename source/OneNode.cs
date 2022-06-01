// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Xml.Linq;
using System.Collections.Generic;

class OneNode : RuleNode
{
    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        if (!base.Load(xelem, parentSymmetry, grid)) return false;
        matches = new List<(int, int, int, int)>();
        matchMask = AH.Array2D(rules.Length, grid.state.Length, false);
        return true;
    }

    override public void Reset()
    {
        base.Reset();
        if (matchCount != 0)
        {
            matchMask.Set2D(false);
            matchCount = 0;
        }
    }

    void Apply(Rule rule, int x, int y, int z)
    {
        int MX = grid.MX, MY = grid.MY;
        var changes = ip.changes;

        for (int dz = 0; dz < rule.OMZ; dz++) for (int dy = 0; dy < rule.OMY; dy++) for (int dx = 0; dx < rule.OMX; dx++)
                {
                    byte newValue = rule.output[dx + dy * rule.OMX + dz * rule.OMX * rule.OMY];
                    if (newValue != 0xff)
                    {
                        int sx = x + dx;
                        int sy = y + dy;
                        int sz = z + dz;
                        int si = sx + sy * MX + sz * MX * MY;
                        byte oldValue = grid.state[si];
                        if (newValue != oldValue)
                        {
                            grid.state[si] = newValue;
                            changes.Add((sx, sy, sz));
                        }
                    }
                }
    }

    override public bool Go()
    {
        if (!base.Go()) return false;
        lastMatchedTurn = ip.counter;

        if (trajectory != null)
        {
            if (counter >= trajectory.Length) return false;
            Array.Copy(trajectory[counter], grid.state, grid.state.Length);
            counter++;
            return true;
        }

        var (R, X, Y, Z) = RandomMatch(ip.random);
        if (R < 0) return false;
        else
        {
            last[R] = true;
            Apply(rules[R], X, Y, Z);
            counter++;
            return true;
        }
    }

    (int r, int x, int y, int z) RandomMatch(Random random)
    {
        if (potentials != null)
        {
            if (observations != null && Observation.IsGoalReached(grid.state, future))
            {
                futureComputed = false;
                return (-1, -1, -1, -1);
            }
            double max = -1000.0;
            int argmax = -1;

            double firstHeuristic = 0.0;
            bool firstHeuristicComputed = false;

            for (int k = 0; k < matchCount; k++)
            {
                var (r, x, y, z) = matches[k];
                int i = x + y * grid.MX + z * grid.MX * grid.MY;
                if (!grid.Matches(rules[r], x, y, z))
                {
                    matchMask[r][i] = false;
                    matches[k] = matches[matchCount - 1];
                    matchCount--;
                    k--;
                }
                else
                {
                    double? heuristic = Field.DeltaPointwise(grid.state, rules[r], x, y, z, fields, potentials, grid.MX, grid.MY);
                    if (heuristic == null) continue;
                    double h = (double)heuristic;
                    if (!firstHeuristicComputed)
                    {
                        firstHeuristic = h;
                        firstHeuristicComputed = true;
                    }
                    double u = random.NextDouble();
                    double key = temperature > 0 ? Math.Pow(u, Math.Exp((h - firstHeuristic) / temperature)) : -h + 0.001 * u;
                    if (key > max)
                    {
                        max = key;
                        argmax = k;
                    }
                }
            }
            return argmax >= 0 ? matches[argmax] : (-1, -1, -1, -1);
        }
        else
        {
            while (matchCount > 0)
            {
                int matchIndex = random.Next(matchCount);

                var (r, x, y, z) = matches[matchIndex];
                int i = x + y * grid.MX + z * grid.MX * grid.MY;

                matchMask[r][i] = false;
                matches[matchIndex] = matches[matchCount - 1];
                matchCount--;

                if (grid.Matches(rules[r], x, y, z)) return (r, x, y, z);
            }
            return (-1, -1, -1, -1);
        }
    }
}
