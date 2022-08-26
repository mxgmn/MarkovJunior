// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Xml.Linq;
using System.Collections.Generic;

/// <summary>
/// Runs a MarkovJunior program, yielding either the final grid state, or the
/// sequence of grid states.
/// </summary>
class Interpreter
{
    /// <summary>The root AST node of the MarkovJunior program being interpreted.</summary>
    public Branch root;
    
    /// <summary>The currently active AST node of the MarkovJunior program.</summary>
    public Branch current;
    
    /// <summary>
    /// The current grid, whose state is updated as the MarkovJunior program is
    /// executed. The grid may also be replaced during execution, in particular
    /// by a <see cref="MapNode">Map</see> or <see cref="WFCNode">WFC</see> node.
    /// </summary>
    public Grid grid;
    
    /// <summary>The initial grid.</summary>
    Grid startgrid;
    
    /// <summary>
    /// If true, the grid will initially have a single non-empty cell in the
    /// center. The center cell will have the second color from the grid's
    /// alphabet; the reset of the grid will have the first color.
    /// </summary>
    bool origin;
    
    /// <summary>The PRNG instance.</summary>
    public Random random;

    /// <summary>
    /// A list of (x, y, z) coordinates of all changes to all grid cells made
    /// during the current program execution. The list is never cleared, except
    /// at the start of the program; rather, the <see cref="Interpreter.first">Interpreter.first</see>
    /// list holds indices into this list.
    /// </summary>
    public List<(int, int, int)> changes;
    
    /// <summary>
    /// A list of indices into the <see cref="Interpreter.changes">Interpreter.changes</see>
    /// list. <c>first[i]</c> is the index of the first change to the grid
    /// which happened after step <c>i</c> of the program's execution.
    /// </summary>
    public List<int> first;
    
    /// <summary>Counts the number of steps that have been executed by the interpreter.</summary>
    public int counter;
    
    /// <summary>
    /// If true, the interpreter will emit the grid's state at each step of
    /// executing the MarkovJunior program; the resulting images may be made
    /// into an animated GIF using a separate tool. Otherwise, only the final
    /// grid state is emitted.
    /// </summary>
    public bool gif;

    Interpreter() { }
    
    /// <summary>
    /// Creates a new Interpreter instance, with the given MarkovJunior program.
    /// </summary>
    /// <param name="xelem">The XML root node of the MarkovJunior program.</param>
    /// <param name="MX"><inheritdoc cref="Grid.MX" path="/summary"/></param>
    /// <param name="MY"><inheritdoc cref="Grid.MY" path="/summary"/></param>
    /// <param name="MZ"><inheritdoc cref="Grid.MZ" path="/summary"/></param>
    public static Interpreter Load(XElement xelem, int MX, int MY, int MZ)
    {
        Interpreter ip = new();
        ip.origin = xelem.Get("origin", false);
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

    /// <summary>
    /// <inheritdoc cref="Interpreter" path="/summary"/>
    /// </summary>
    /// <param name="seed">The PRNG seed.</param>
    /// <param name="steps">The maximum number of steps to execute. If 0 or negative, there is no maximum and the program is run until it terminates.</param>
    /// <param name="gif">If <c>true</c>, every intermediate grid state is yielded; otherwise, only the final grid state is yielded.</param>
    /// <returns>An enumerable of (state, alphabet, MX, MY, MZ) tuples.</returns>
    public IEnumerable<(byte[], char[], int, int, int)> Run(int seed, int steps, bool gif)
    {
        random = new Random(seed);
        grid = startgrid;
        grid.Clear();
        if (origin) grid.state[grid.MX / 2 + (grid.MY / 2) * grid.MX + (grid.MZ / 2) * grid.MX * grid.MY] = 1;

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

    /// <summary>Writes a string to the log, with a newline.</summary>
    public static void WriteLine(string s) => Console.WriteLine(s);
    
    /// <summary>Writes a string to the log, without a newline.</summary>
    public static void Write(string s) => Console.Write(s);
}
