// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

class Rule
{
    public int IMX, IMY, IMZ, OMX, OMY, OMZ;
    public int[] input;
    public byte[] output, binput;

    public double p;
    public (int, int, int)[][] ishifts, oshifts;

    public bool original;

    public Rule(int[] input, int IMX, int IMY, int IMZ, byte[] output, int OMX, int OMY, int OMZ, int C, double p)
    {
        this.input = input;
        this.output = output;
        this.IMX = IMX;
        this.IMY = IMY;
        this.IMZ = IMZ;
        this.OMX = OMX;
        this.OMY = OMY;
        this.OMZ = OMZ;

        this.p = p;

        List<(int, int, int)>[] lists = new List<(int, int, int)>[C];
        for (int c = 0; c < C; c++) lists[c] = new List<(int, int, int)>();
        for (int z = 0; z < IMZ; z++) for (int y = 0; y < IMY; y++) for (int x = 0; x < IMX; x++)
                {
                    int w = input[x + y * IMX + z * IMX * IMY];
                    for (int c = 0; c < C; c++, w >>= 1) if ((w & 1) == 1) lists[c].Add((x, y, z));
                }
        ishifts = new (int, int, int)[C][];
        for (int c = 0; c < C; c++) ishifts[c] = lists[c].ToArray();

        if (OMX == IMX && OMY == IMY && OMZ == IMZ)
        {
            for (int c = 0; c < C; c++) lists[c].Clear();
            for (int z = 0; z < OMZ; z++) for (int y = 0; y < OMY; y++) for (int x = 0; x < OMX; x++)
                    {
                        byte o = output[x + y * OMX + z * OMX * OMY];
                        if (o != 0xff) lists[o].Add((x, y, z));
                        else for (int c = 0; c < C; c++) lists[c].Add((x, y, z));
                    }
            oshifts = new (int, int, int)[C][];
            for (int c = 0; c < C; c++) oshifts[c] = lists[c].ToArray();
        }

        int wildcard = (1 << C) - 1;
        binput = new byte[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            int w = input[i];
            binput[i] = w == wildcard ? (byte)0xff : (byte)System.Numerics.BitOperations.TrailingZeroCount(w);
        }
    }

    public Rule ZRotated()
    {
        int[] newinput = new int[input.Length];
        for (int z = 0; z < IMZ; z++) for (int y = 0; y < IMX; y++) for (int x = 0; x < IMY; x++)
                    newinput[x + y * IMY + z * IMX * IMY] = input[IMX - 1 - y + x * IMX + z * IMX * IMY];

        byte[] newoutput = new byte[output.Length];
        for (int z = 0; z < OMZ; z++) for (int y = 0; y < OMX; y++) for (int x = 0; x < OMY; x++)
                    newoutput[x + y * OMY + z * OMX * OMY] = output[OMX - 1 - y + x * OMX + z * OMX * OMY];

        return new Rule(newinput, IMY, IMX, IMZ, newoutput, OMY, OMX, OMZ, ishifts.Length, p);
    }

    public Rule YRotated()
    {
        int[] newinput = new int[input.Length];
        for (int z = 0; z < IMX; z++) for (int y = 0; y < IMY; y++) for (int x = 0; x < IMZ; x++)
                    newinput[x + y * IMZ + z * IMZ * IMY] = input[IMX - 1 - z + y * IMX + x * IMX * IMY];

        byte[] newoutput = new byte[output.Length];
        for (int z = 0; z < OMX; z++) for (int y = 0; y < OMY; y++) for (int x = 0; x < OMZ; x++)
                    newoutput[x + y * OMZ + z * OMZ * OMY] = output[OMX - 1 - z + y * OMX + x * OMX * OMY];

        return new Rule(newinput, IMZ, IMY, IMX, newoutput, OMZ, OMY, OMX, ishifts.Length, p);
    }

    public Rule Reflected()
    {
        int[] newinput = new int[input.Length];
        for (int z = 0; z < IMZ; z++) for (int y = 0; y < IMY; y++) for (int x = 0; x < IMX; x++)
                    newinput[x + y * IMX + z * IMX * IMY] = input[IMX - 1 - x + y * IMX + z * IMX * IMY];

        byte[] newoutput = new byte[output.Length];
        for (int z = 0; z < OMZ; z++) for (int y = 0; y < OMY; y++) for (int x = 0; x < OMX; x++)
                    newoutput[x + y * OMX + z * OMX * OMY] = output[OMX - 1 - x + y * OMX + z * OMX * OMY];

        return new Rule(newinput, IMX, IMY, IMZ, newoutput, OMX, OMY, OMZ, ishifts.Length, p);
    }

    public static bool Same(Rule a1, Rule a2)
    {
        if (a1.IMX != a2.IMX || a1.IMY != a2.IMY || a1.IMZ != a2.IMZ || a1.OMX != a2.OMX || a1.OMY != a2.OMY || a1.OMZ != a2.OMZ) return false;
        for (int i = 0; i < a1.IMX * a1.IMY * a1.IMZ; i++) if (a1.input[i] != a2.input[i]) return false;
        for (int i = 0; i < a1.OMX * a1.OMY * a1.OMZ; i++) if (a1.output[i] != a2.output[i]) return false;
        return true;
    }

    public IEnumerable<Rule> Symmetries(bool[] symmetry, bool d2)
    {
        if (d2) return SymmetryHelper.SquareSymmetries(this, r => r.ZRotated(), r => r.Reflected(), Same, symmetry);
        else return SymmetryHelper.CubeSymmetries(this, r => r.ZRotated(), r => r.YRotated(), r => r.Reflected(), Same, symmetry);
    }

    public static (char[] data, int MX, int MY, int MZ) LoadResource(string filename, string legend, bool d2)
    {
        if (legend == null)
        {
            Interpreter.WriteLine($"no legend for {filename}");
            return (null, -1, -1, -1);
        }
        (int[] data, int MX, int MY, int MZ) = d2 ? Graphics.LoadBitmap(filename) : VoxHelper.LoadVox(filename);
        if (data == null)
        {
            Interpreter.WriteLine($"couldn't read {filename}");
            return (null, MX, MY, MZ);
        }
        (byte[] ords, int amount) = data.Ords();
        if (amount > legend.Length)
        {
            Interpreter.WriteLine($"the amount of colors {amount} in {filename} is more than {legend.Length}");
            return (null, MX, MY, MZ);
        }
        return (ords.Select(o => legend[o]).ToArray(), MX, MY, MZ);
    }

    static (char[], int, int, int) Parse(string s)
    {
        string[][] lines = Helper.Split(s, ' ', '/');
        int MX = lines[0][0].Length;
        int MY = lines[0].Length;
        int MZ = lines.Length;
        char[] result = new char[MX * MY * MZ];

        for (int z = 0; z < MZ; z++)
        {
            string[] linesz = lines[MZ - 1 - z];
            if (linesz.Length != MY)
            {
                Interpreter.Write("non-rectangular pattern");
                return (null, -1, -1, -1);
            }
            for (int y = 0; y < MY; y++)
            {
                string lineszy = linesz[y];
                if (lineszy.Length != MX)
                {
                    Interpreter.Write("non-rectangular pattern");
                    return (null, -1, -1, -1);
                }
                for (int x = 0; x < MX; x++) result[x + y * MX + z * MX * MY] = lineszy[x];
            }
        }
        
        return (result, MX, MY, MZ);
    }

    public static Rule Load(XElement xelem, Grid gin, Grid gout)
    {
        int lineNumber = xelem.LineNumber();
        string filepath(string name)
        {
            string result = "resources/rules/";
            if (gout.folder != null) result += gout.folder + "/";
            result += name;
            result += gin.MZ == 1 ? ".png" : ".vox";
            return result;
        };

        string inString = xelem.Get<string>("in", null);
        string outString = xelem.Get<string>("out", null);
        string finString = xelem.Get<string>("fin", null);
        string foutString = xelem.Get<string>("fout", null);
        string fileString = xelem.Get<string>("file", null);
        string legend = xelem.Get<string>("legend", null);

        char[] inRect, outRect;
        int IMX = -1, IMY = -1, IMZ = -1, OMX = -1, OMY = -1, OMZ = -1;
        if (fileString == null)
        {
            if (inString == null && finString == null)
            {
                Interpreter.WriteLine($"no input in a rule at line {lineNumber}");
                return null;
            }
            if (outString == null && foutString == null)
            {
                Interpreter.WriteLine($"no output in a rule at line {lineNumber}");
                return null;
            }

            (inRect, IMX, IMY, IMZ) = inString != null ? Parse(inString) : LoadResource(filepath(finString), legend, gin.MZ == 1);
            if (inRect == null)
            {
                Interpreter.WriteLine($" in input at line {lineNumber}");
                return null;
            }

            (outRect, OMX, OMY, OMZ) = outString != null ? Parse(outString) : LoadResource(filepath(foutString), legend, gin.MZ == 1);
            if (outRect == null)
            {
                Interpreter.WriteLine($" in output at line {lineNumber}");
                return null;
            }

            if (gin == gout && (OMZ != IMZ || OMY != IMY || OMX != IMX))
            {
                Interpreter.WriteLine($"non-matching pattern sizes at line {lineNumber}");
                return null;
            }
        }
        else
        {
            if (inString != null || finString != null || outString != null || foutString != null)
            {
                Interpreter.WriteLine($"rule at line {lineNumber} already contains a file attribute");
                return null;
            }
            (char[] rect, int FX, int FY, int FZ) = LoadResource(filepath(fileString), legend, gin.MZ == 1);
            if (rect == null)
            {
                Interpreter.WriteLine($" in a rule at line {lineNumber}");
                return null;
            }
            if (FX % 2 != 0)
            {
                Interpreter.WriteLine($"odd width {FX} in {fileString}");
                return null;
            }
            
            IMX = OMX = FX / 2;
            IMY = OMY = FY;
            IMZ = OMZ = FZ;

            inRect = AH.FlatArray3D(FX / 2, FY, FZ, (x, y, z) => rect[x + y * FX + z * FX * FY]);
            outRect = AH.FlatArray3D(FX / 2, FY, FZ, (x, y, z) => rect[x + FX / 2 + y * FX + z * FX * FY]);
        }

        int[] input = new int[inRect.Length];
        for (int i = 0; i < inRect.Length; i++)
        {
            char c = inRect[i];
            bool success = gin.waves.TryGetValue(c, out int value);
            if (!success)
            {
                Interpreter.WriteLine($"input code {c} at line {lineNumber} is not found in codes");
                return null;
            }
            input[i] = value;
        }

        byte[] output = new byte[outRect.Length];
        for (int o = 0; o < outRect.Length; o++)
        {
            char c = outRect[o];
            if (c == '*') output[o] = 0xff;
            else
            {
                bool success = gout.values.TryGetValue(c, out byte value);
                if (!success)
                {
                    Interpreter.WriteLine($"output code {c} at line {lineNumber} is not found in codes");
                    return null;
                }
                output[o] = value;
            }
        }

        double p = xelem.Get("p", 1.0);
        return new Rule(input, IMX, IMY, IMZ, output, OMX, OMY, OMZ, gin.C, p);
    }
}
