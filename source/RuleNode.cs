// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

abstract class RuleNode : Node
{
    public Rule[] rules;
    public int counter, steps;

    protected List<(int, int, int, int)> matches;
    protected int matchCount, lastMatchedTurn;
    protected bool[][] matchMask;

    protected int[][] potentials;
    public Field[] fields;
    protected Observation[] observations;
    protected double temperature;

    protected bool search, futureComputed;
    protected int[] future;
    protected byte[][] trajectory;

    int limit;
    double depthCoefficient;

    public bool[] last;

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        string symmetryString = xelem.Get<string>("symmetry", null);
        bool[] symmetry = SymmetryHelper.GetSymmetry(grid.MZ == 1, symmetryString, parentSymmetry);
        if (symmetry == null)
        {
            Interpreter.WriteLine($"unknown symmetry {symmetryString} at line {xelem.LineNumber()}");
            return false;
        }

        List<Rule> ruleList = new();
        XElement[] xrules = xelem.Elements("rule").ToArray();
        XElement[] ruleElements = xrules.Length > 0 ? xrules : new XElement[] { xelem };
        foreach (XElement xrule in ruleElements)
        {
            Rule rule = Rule.Load(xrule, grid, grid);
            if (rule == null) return false;
            rule.original = true;

            string ruleSymmetryString = xrule.Get<string>("symmetry", null);
            bool[] ruleSymmetry = SymmetryHelper.GetSymmetry(grid.MZ == 1, ruleSymmetryString, symmetry);
            if (ruleSymmetry == null)
            {
                Interpreter.WriteLine($"unknown symmetry {ruleSymmetryString} at line {xrule.LineNumber()}");
                return false;
            }
            foreach (Rule r in rule.Symmetries(ruleSymmetry, grid.MZ == 1)) ruleList.Add(r);
        }
        rules = ruleList.ToArray();
        last = new bool[rules.Length];

        steps = xelem.Get("steps", 0);

        temperature = xelem.Get("temperature", 0.0);
        var xfields = xelem.Elements("field");
        if (xfields.Any())
        {
            fields = new Field[grid.C];
            foreach (XElement xfield in xfields)
            {
                char c = xfield.Get<char>("for");
                if (grid.values.TryGetValue(c, out byte value)) fields[value] = new Field(xfield, grid);
                else
                {
                    Interpreter.WriteLine($"unknown field value {c} at line {xfield.LineNumber()}");
                    return false;
                }
            }
            potentials = AH.Array2D(grid.C, grid.state.Length, 0);
        }

        var xobservations = xelem.Elements("observe");
        if (xobservations.Any())
        {
            observations = new Observation[grid.C];
            foreach (var x in xobservations)
            {
                byte value = grid.values[x.Get<char>("value")];
                observations[value] = new Observation(x.Get("from", grid.characters[value]), x.Get<string>("to"), grid);
            }

            search = xelem.Get("search", false);
            if (search)
            {
                limit = xelem.Get("limit", -1);
                depthCoefficient = xelem.Get("depthCoefficient", 0.5);
            }
            else potentials = AH.Array2D(grid.C, grid.state.Length, 0);
            future = new int[grid.state.Length];
        }

        return true;
    }

    override public void Reset()
    {
        lastMatchedTurn = -1;
        counter = 0;
        futureComputed = false;

        for (int r = 0; r < last.Length; r++) last[r] = false;
    }

    protected virtual void Add(int r, int x, int y, int z, bool[] maskr)
    {
        maskr[x + y * grid.MX + z * grid.MX * grid.MY] = true;

        var match = (r, x, y, z);
        if (matchCount < matches.Count) matches[matchCount] = match;
        else matches.Add(match);
        matchCount++;
    }

    public override bool Go()
    {
        for (int r = 0; r < last.Length; r++) last[r] = false;

        if (steps > 0 && counter >= steps) return false; //есть вариант вернуть false на том же ходу, на котором мы достигли предела

        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;
        if (observations != null && !futureComputed)
        {
            if (!Observation.ComputeFutureSetPresent(future, grid.state, observations)) return false;
            else
            {
                futureComputed = true;
                if (search)
                {
                    trajectory = null;
                    int TRIES = limit < 0 ? 1 : 20;
                    for (int k = 0; k < TRIES && trajectory == null; k++) trajectory = Search.Run(grid.state, future, rules, grid.MX, grid.MY, grid.MZ, grid.C, this is AllNode, limit, depthCoefficient, ip.random.Next());
                    if (trajectory == null) Console.WriteLine("SEARCH RETURNED NULL");
                }
                else Observation.ComputeBackwardPotentials(potentials, future, MX, MY, MZ, rules);
            }
        }

        if (lastMatchedTurn >= 0)
        {
            for (int n = ip.first[lastMatchedTurn]; n < ip.changes.Count; n++)
            {
                var (x, y, z) = ip.changes[n];
                byte value = grid.state[x + y * MX + z * MX * MY];
                for (int r = 0; r < rules.Length; r++)
                {
                    Rule rule = rules[r];
                    bool[] maskr = matchMask[r];
                    (int x, int y, int z)[] shifts = rule.ishifts[value];
                    for (int l = 0; l < shifts.Length; l++)
                    {
                        var (shiftx, shifty, shiftz) = shifts[l];
                        int sx = x - shiftx;
                        int sy = y - shifty;
                        int sz = z - shiftz;

                        if (sx < 0 || sy < 0 || sz < 0 || sx + rule.IMX > MX || sy + rule.IMY > MY || sz + rule.IMZ > MZ) continue;
                        int si = sx + sy * MX + sz * MX * MY;

                        if (!maskr[si] && grid.Matches(rule, sx, sy, sz)) Add(r, sx, sy, sz, maskr);
                    }
                }
            }
        }
        else
        {
            matchCount = 0;
            for (int r = 0; r < rules.Length; r++)
            {
                Rule rule = rules[r];
                bool[] maskr = matchMask?[r];
                for (int z = rule.IMZ - 1; z < MZ; z += rule.IMZ)
                    for (int y = rule.IMY - 1; y < MY; y += rule.IMY)
                        for (int x = rule.IMX - 1; x < MX; x += rule.IMX)
                        {
                            var shifts = rule.ishifts[grid.state[x + y * MX + z * MX * MY]];
                            for (int l = 0; l < shifts.Length; l++)
                            {
                                var (shiftx, shifty, shiftz) = shifts[l];
                                int sx = x - shiftx;
                                int sy = y - shifty;
                                int sz = z - shiftz;
                                if (sx < 0 || sy < 0 || sz < 0 || sx + rule.IMX > MX || sy + rule.IMY > MY || sz + rule.IMZ > MZ) continue;

                                if (grid.Matches(rule, sx, sy, sz)) Add(r, sx, sy, sz, maskr);
                            }
                        }
            }
        }

        if (fields != null)
        {
            bool anysuccess = false, anycomputation = false;
            for (int c = 0; c < fields.Length; c++)
            {
                Field field = fields[c];
                if (field != null && (counter == 0 || field.recompute))
                {
                    bool success = field.Compute(potentials[c], grid);
                    if (!success && field.essential) return false;
                    anysuccess |= success;
                    anycomputation = true;
                }
            }
            if (anycomputation && !anysuccess) return false;
        }

        return true;
    }
}
