// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Xml.Linq;
using System.Collections.Generic;

public sealed class Interpreter
{
    public Branch root, current;
    public Grid grid;
    Grid startgrid;
    
    Origin origin;
    enum Origin { None, Random, Center };
    public Random random;

    public List<(int, int, int)> changes;
    public List<int> first;
    public int counter;
    
    public bool gif;

    Interpreter() { }
    public static Interpreter Load(XElement xelem, int MX, int MY, int MZ)
    {
        Interpreter ip = new();

        string originName = xelem.Get<string>("origin", null);
        if (originName == "Random") ip.origin = Origin.Random;
        else if (originName == "Center") ip.origin = Origin.Center;
        else if (originName == null) ip.origin = Origin.None;
        else
        {
            WriteLine($"unknown origin name {originName}");
            return null;
        }

        ip.grid = Grid.Load(xelem, MX, MY, MZ);
        if (ip.grid == null)
        {
            Console.WriteLine("failed to load grid");
            return null;
        }
        ip.startgrid = ip.grid;

        string symmetryString = xelem.Get<string>("symmetry", null);
        bool[] symmetry = SymmetryHelper.GetSymmetry(ip.grid.MZ == 1, symmetryString, AH.Array1D(ip.grid.MZ == 1 ? 8 : 48, true));
        if (symmetry == null)
        {
            WriteLine($"unknown symmetry {symmetryString} at line {xelem.LineNumber()}");
            return null;
        }

        Node topnode = Node.Factory(xelem, symmetry, ip, ip.grid);
        if (topnode == null) return null;
        ip.root = topnode is Branch ? topnode as Branch : new MarkovNode(topnode, ip);

        ip.changes = new List<(int, int, int)>();
        ip.first = new List<int>();
        return ip;
    }

    public IEnumerable<(byte[], char[], int, int, int)> Run(int seed, int steps, bool gif)
    {
        random = new Random(seed);
        grid = startgrid;
        int originIndex = origin switch
        {
            Origin.Random => random.Next(grid.MX * grid.MY * grid.MZ),
            Origin.Center => grid.MX / 2 + (grid.MY / 2) * grid.MX + (grid.MZ / 2) * grid.MX * grid.MY,
            _ => -1
        };
        grid.Clear(originIndex);
        changes.Clear();
        first.Clear();
        first.Add(0);

        root.Reset();
        current = root;

        this.gif = gif;
        counter = 0;
        while (current != null && (steps <= 0 || counter < steps))
        {
            if (gif)
            {
                Console.WriteLine($"[{counter}]");
                yield return (grid.state, grid.characters, grid.MX, grid.MY, grid.MZ);
            }

            current.Go();
            counter++;
            first.Add(changes.Count);
        }

        yield return (grid.state, grid.characters, grid.MX, grid.MY, grid.MZ);
    }

    public static void WriteLine(string s) => Console.WriteLine(s);
    public static void Write(string s) => Console.Write(s);
}
