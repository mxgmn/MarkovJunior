// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

static class VoxHelper
{
    public static (int[], int, int, int) LoadVox(string filename)
    {
        try
        {
            using FileStream file = File.Open(filename, FileMode.Open);
            var stream = new BinaryReader(file);

            int[] result = null;
            int MX = -1, MY = -1, MZ = -1;

            string magic = new(stream.ReadChars(4));
            int version = stream.ReadInt32();

            while (stream.BaseStream.Position < stream.BaseStream.Length)
            {
                byte[] bt = stream.ReadBytes(1);
                char head = Encoding.ASCII.GetChars(bt)[0];

                if (head == 'S')
                {
                    string tail = Encoding.ASCII.GetString(stream.ReadBytes(3));
                    if (tail != "IZE") continue;

                    int chunkSize = stream.ReadInt32();
                    stream.ReadBytes(4);
                    //Console.WriteLine("found SIZE chunk");
                    MX = stream.ReadInt32();
                    MY = stream.ReadInt32();
                    MZ = stream.ReadInt32();
                    stream.ReadBytes(chunkSize - 4 * 3);
                    //Console.WriteLine($"size = ({MX}, {MY}, {MZ})");
                }
                else if (head == 'X')
                {
                    string tail = Encoding.ASCII.GetString(stream.ReadBytes(3));
                    if (tail != "YZI") continue;

                    if (MX <= 0 || MY <= 0 || MZ <= 0) return (null, MX, MY, MZ);
                    result = new int[MX * MY * MZ];
                    for (int i = 0; i < result.Length; i++) result[i] = -1;

                    //Console.WriteLine("found XYZI chunk");
                    stream.ReadBytes(8);
                    int numVoxels = stream.ReadInt32();
                    //Console.WriteLine($"number of voxels = {numVoxels}");
                    for (int i = 0; i < numVoxels; i++)
                    {
                        byte x = stream.ReadByte();
                        byte y = stream.ReadByte();
                        byte z = stream.ReadByte();
                        byte color = stream.ReadByte();
                        result[x + y * MX + z * MX * MY] = color;
                        //Console.WriteLine($"adding voxel {x} {y} {z} of color {color}");
                    }
                }
            }
            file.Close();
            return (result, MX, MY, MZ);
        }
        catch (Exception) { return (null, -1, -1, -1); }
    }

    static void WriteString(this BinaryWriter stream, string s) { foreach (char c in s) stream.Write(c); }
    public static void SaveVox(byte[] state, byte MX, byte MY, byte MZ, int[] palette, string filename)
    {
        List<(byte, byte, byte, byte)> voxels = new();
        for (byte z = 0; z < MZ; z++) for (byte y = 0; y < MY; y++) for (byte x = 0; x < MX; x++)
                {
                    int i = x + y * MX + z * MX * MY;
                    byte v = state[i];
                    if (v != 0) voxels.Add((x, y, z, (byte)(v + 1)));
                }

        FileStream file = File.Open(filename, FileMode.Create);
        using BinaryWriter stream = new(file);

        stream.WriteString("VOX ");
        stream.Write(150);

        stream.WriteString("MAIN");
        stream.Write(0);
        stream.Write(1092 + voxels.Count * 4);

        stream.WriteString("PACK");
        stream.Write(4);
        stream.Write(0);
        stream.Write(1);

        stream.WriteString("SIZE");
        stream.Write(12);
        stream.Write(0);
        stream.Write((int)MX);
        stream.Write((int)MY);
        stream.Write((int)MZ);

        stream.WriteString("XYZI");
        stream.Write(4 + voxels.Count * 4);
        stream.Write(0);
        stream.Write(voxels.Count);

        foreach (var (x, y, z, color) in voxels)
        {
            stream.Write(x);
            //stream.Write((byte)(size.y - v.y - 1));
            stream.Write(y);
            stream.Write(z);
            stream.Write(color);
        }

        stream.WriteString("RGBA");
        stream.Write(1024);
        stream.Write(0);

        foreach (int c in palette)
        {
            //(byte R, byte G, byte B) = c.ToTuple();
            stream.Write((byte)((c & 0xff0000) >> 16));
            stream.Write((byte)((c & 0xff00) >> 8));
            stream.Write((byte)(c & 0xff));
            stream.Write((byte)0);
        }
        for (int i = palette.Length; i < 255; i++)
        {
            stream.Write((byte)(0xff - i - 1));
            stream.Write((byte)(0xff - i - 1));
            stream.Write((byte)(0xff - i - 1));
            stream.Write((byte)(0xff));
        }
        stream.Write(0);
        file.Close();
    }
}
