// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class ConvolutionNode : Node
{
    ConvolutionRule[] rules;
    int[] kernel;
    bool periodic;
    public int counter, steps;

    int[][] sumfield;

    static readonly Dictionary<string, int[]> kernels2d = new()
    {
        ["VonNeumann"] = new int[9] { 0, 1, 0, 1, 0, 1, 0, 1, 0 },
        ["Moore"] = new int[9] { 1, 1, 1, 1, 0, 1, 1, 1, 1 },
    };
    static readonly Dictionary<string, int[]> kernels3d = new()
    {
        ["VonNeumann"] = new int[27] { 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0 },
        ["NoCorners"] = new int[27] { 0, 1, 0, 1, 1, 1, 0, 1, 0, 1, 1, 1, 1, 0, 1, 1, 1, 1, 0, 1, 0, 1, 1, 1, 0, 1, 0 },
    };

    override protected bool Load(XElement xelem, bool[] parentSymmetry, Grid grid)
    {
        XElement[] xrules = xelem.Elements("rule").ToArray();
        if (xrules.Length == 0) xrules = new[] { xelem };
        rules = new ConvolutionRule[xrules.Length];
        for (int k = 0; k < rules.Length; k++)
        {
            rules[k] = new();
            if (!rules[k].Load(xrules[k], grid)) return false;
        }

        steps = xelem.Get("steps", -1);
        periodic = xelem.Get("periodic", false);
        string neighborhood = xelem.Get<string>("neighborhood");
        kernel = grid.MZ == 1 ? kernels2d[neighborhood] : kernels3d[neighborhood];

        sumfield = AH.Array2D(grid.state.Length, grid.C, 0);
        return true;
    }

    override public void Reset()
    {
        counter = 0;
    }

    override public bool Go()
    {
        if (steps > 0 && counter >= steps) return false;
        int MX = grid.MX, MY = grid.MY, MZ = grid.MZ;

        sumfield.Set2D(0);
        if (MZ == 1)
        {
            for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                {
                    int[] sums = sumfield[x + y * MX];
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        {
                            int sx = x + dx;
                            int sy = y + dy;

                            if (periodic)
                            {
                                if (sx < 0) sx += MX;
                                else if (sx >= MX) sx -= MX;
                                if (sy < 0) sy += MY;
                                else if (sy >= MY) sy -= MY;
                            }
                            else if (sx < 0 || sy < 0 || sx >= MX || sy >= MY) continue;

                            sums[grid.state[sx + sy * MX]] += kernel[dx + 1 + (dy + 1) * 3];
                        }
                }
        }
        else
        {
            for (int z = 0; z < MZ; z++) for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
                    {
                        int[] sums = sumfield[x + y * MX + z * MX * MY];
                        for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                                {
                                    int sx = x + dx;
                                    int sy = y + dy;
                                    int sz = z + dz;

                                    if (periodic)
                                    {
                                        if (sx < 0) sx += MX;
                                        else if (sx >= MX) sx -= MX;
                                        if (sy < 0) sy += MY;
                                        else if (sy >= MY) sy -= MY;
                                        if (sz < 0) sz += MZ;
                                        else if (sz >= MZ) sz -= MZ;
                                    }
                                    else if (sx < 0 || sy < 0 || sz < 0 || sx >= MX || sy >= MY || sz >= MZ) continue;

                                    sums[grid.state[sx + sy * MX + sz * MX * MY]] += kernel[dx + 1 + (dy + 1) * 3 + (dz + 1) * 9];
                                }
                    }
        }        

        bool change = false;
        for (int i = 0; i < sumfield.Length; i++)
        {
            int[] sums = sumfield[i];
            byte input = grid.state[i];
            for (int r = 0; r < rules.Length; r++)
            {
                ConvolutionRule rule = rules[r];
                if (input == rule.input && rule.output != grid.state[i] && (rule.p == 1.0 || ip.random.Next() < rule.p * int.MaxValue))
                {
                    bool success = true;
                    if (rule.sums != null)
                    {
                        int sum = 0;
                        for (int c = 0; c < rule.values.Length; c++) sum += sums[rule.values[c]];
                        success = rule.sums[sum];
                    }
                    if (success)
                    {
                        grid.state[i] = rule.output;
                        change = true;
                        break;
                    }
                }
            }
        }

        counter++;
        return change;
    }

    class ConvolutionRule
    {
        public byte input, output;
        public byte[] values;
        public bool[] sums;
        public double p;

        public bool Load(XElement xelem, Grid grid)
        {
            input = grid.values[xelem.Get<char>("in")];
            output = grid.values[xelem.Get<char>("out")];
            p = xelem.Get("p", 1.0);

            static int[] interval(string s)
            {
                if (s.Contains('.'))
                {
                    string[] bounds = s.Split("..");
                    int min = int.Parse(bounds[0]);
                    int max = int.Parse(bounds[1]);
                    int[] result = new int[max - min + 1];
                    for (int i = 0; i < result.Length; i++) result[i] = min + i;
                    return result;
                }
                else return new int[1] { int.Parse(s) };
            };

            string valueString = xelem.Get<string>("values", null);
            string sumsString = xelem.Get<string>("sum", null);
            if (valueString != null && sumsString == null)
            {
                Interpreter.WriteLine($"missing \"sum\" attribute at line {xelem.LineNumber()}");
                return false;
            }
            if (valueString == null && sumsString != null)
            {
                Interpreter.WriteLine($"missing \"values\" attribute at line {xelem.LineNumber()}");
                return false;
            }
            
            if (valueString != null)
            {
                values = valueString.Select(c => grid.values[c]).ToArray();

                sums = new bool[28];
                string[] intervals = sumsString.Split(',');
                foreach (string s in intervals) foreach (int i in interval(s)) sums[i] = true;
            }
            return true;
        }
    }
}
