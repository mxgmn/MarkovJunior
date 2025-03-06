// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

static class VoxHelper
{
    // Loads a MagicaVoxel .vox file and returns the voxel data and dimensions
    // Returns a tuple containing: the color indices array, width (MX), height (MY), and depth (MZ)
    public static (int[], int, int, int) LoadVox(string filename)
    {
        try
        {
            using FileStream file = File.Open(filename, FileMode.Open);
            var stream = new BinaryReader(file);

            int[] result = null;  // Will hold the voxel color indices
            int MX = -1, MY = -1, MZ = -1;  // Dimensions of the voxel model

            // Check VOX file header
            string magic = new(stream.ReadChars(4));  // Should be "VOX "
            int version = stream.ReadInt32();  // Version number

            // Parse the file chunk by chunk
            while (stream.BaseStream.Position < stream.BaseStream.Length)
            {
                byte[] bt = stream.ReadBytes(1);
                char head = Encoding.ASCII.GetChars(bt)[0];

                // SIZE chunk - contains model dimensions
                if (head == 'S')
                {
                    string tail = Encoding.ASCII.GetString(stream.ReadBytes(3));
                    if (tail != "IZE") continue;  // Make sure it's "SIZE"

                    int chunkSize = stream.ReadInt32();  // Size of chunk content
                    stream.ReadBytes(4);  // Skip children chunk size (always 0)

                    // Read model dimensions
                    MX = stream.ReadInt32();  // Width
                    MY = stream.ReadInt32();  // Height
                    MZ = stream.ReadInt32();  // Depth

                    // Skip any remaining bytes in the chunk
                    stream.ReadBytes(chunkSize - 4 * 3);
                }
                // XYZI chunk - contains voxel data
                else if (head == 'X')
                {
                    string tail = Encoding.ASCII.GetString(stream.ReadBytes(3));
                    if (tail != "YZI") continue;  // Make sure it's "XYZI"

                    // Can't process voxel data without dimensions
                    if (MX <= 0 || MY <= 0 || MZ <= 0) return (null, MX, MY, MZ);

                    // Initialize the result array
                    result = new int[MX * MY * MZ];
                    for (int i = 0; i < result.Length; i++) result[i] = -1;  // -1 indicates empty space

                    stream.ReadBytes(8);  // Skip chunk size and children chunk size

                    int numVoxels = stream.ReadInt32();  // Number of voxels in the model

                    // Read each voxel
                    for (int i = 0; i < numVoxels; i++)
                    {
                        byte x = stream.ReadByte();  // X coordinate
                        byte y = stream.ReadByte();  // Y coordinate
                        byte z = stream.ReadByte();  // Z coordinate
                        byte color = stream.ReadByte();  // Color index

                        // Store the color index at the appropriate position
                        result[x + y * MX + z * MX * MY] = color;
                    }
                }
                // Other chunks are skipped
            }
            file.Close();
            return (result, MX, MY, MZ);
        }
        catch (Exception) { return (null, -1, -1, -1); }  // Return error codes on failure
    }

    // Helper method to write a string to a binary stream
    static void WriteString(this BinaryWriter stream, string s) { foreach (char c in s) stream.Write(c); }

    // Saves voxel data to a MagicaVoxel .vox file
    public static void SaveVox(byte[] state, byte MX, byte MY, byte MZ, int[] palette, string filename)
    {
        // Collect non-empty voxels to avoid storing empty space
        List<(byte, byte, byte, byte)> voxels = new();
        for (byte z = 0; z < MZ; z++) for (byte y = 0; y < MY; y++) for (byte x = 0; x < MX; x++)
                {
                    int i = x + y * MX + z * MX * MY;
                    byte v = state[i];
                    if (v != 0) voxels.Add((x, y, z, (byte)(v + 1)));  // 0 is empty space, so add 1 to indices
                }

        // Create output file stream
        FileStream file = File.Open(filename, FileMode.Create);
        using BinaryWriter stream = new(file);

        // Write file header
        stream.WriteString("VOX ");  // Magic number
        stream.Write(150);  // Version number

        // Write MAIN chunk
        stream.WriteString("MAIN");
        stream.Write(0);  // Size of chunk content (0 for MAIN)
        stream.Write(1092 + voxels.Count * 4);  // Size of child chunks

        // Write PACK chunk (number of models)
        stream.WriteString("PACK");
        stream.Write(4);  // Chunk size
        stream.Write(0);  // Child chunk size
        stream.Write(1);  // Number of models

        // Write SIZE chunk (model dimensions)
        stream.WriteString("SIZE");
        stream.Write(12);  // Chunk size (3 integers = 12 bytes)
        stream.Write(0);   // Child chunk size
        stream.Write((int)MX);  // Width
        stream.Write((int)MY);  // Height
        stream.Write((int)MZ);  // Depth

        // Write XYZI chunk (voxel data)
        stream.WriteString("XYZI");
        stream.Write(4 + voxels.Count * 4);  // Chunk size (4 bytes per voxel plus 4 for the count)
        stream.Write(0);  // Child chunk size
        stream.Write(voxels.Count);  // Number of voxels

        // Write each voxel
        foreach (var (x, y, z, color) in voxels)
        {
            stream.Write(x);
            stream.Write(y);
            stream.Write(z);
            stream.Write(color);
        }

        // Write RGBA chunk (color palette)
        stream.WriteString("RGBA");
        stream.Write(1024);  // Chunk size (256 colors * 4 bytes)
        stream.Write(0);     // Child chunk size

        // Write custom palette colors
        foreach (int c in palette)
        {
            stream.Write((byte)((c & 0xff0000) >> 16));  // Red component
            stream.Write((byte)((c & 0xff00) >> 8));     // Green component
            stream.Write((byte)(c & 0xff));              // Blue component
            stream.Write((byte)0);                       // Alpha (always 0 in this implementation)
        }

        // Fill remaining palette entries with graded colors
        for (int i = palette.Length; i < 255; i++)
        {
            stream.Write((byte)(0xff - i - 1));
            stream.Write((byte)(0xff - i - 1));
            stream.Write((byte)(0xff - i - 1));
            stream.Write((byte)(0xff));
        }

        // Write final palette entry (index 255)
        stream.Write(0);
        file.Close();
    }
}

/*
========== SUMMARY ==========

This code provides utilities for working with voxel models in the MagicaVoxel .vox file format. 
Think of it like a specialized image loader/saver, but for 3D "pixel" (voxel) models instead of 2D images.

In simple terms:

1. Loading Voxel Models:
   - The LoadVox function reads a .vox file and extracts:
     a) The dimensions of the 3D model (width, height, depth)
     b) The voxel data - which positions contain voxels and what color each voxel is
   - It parses the file chunk by chunk (SIZE chunk for dimensions, XYZI chunk for voxel data)
   - Empty spaces are represented as -1 in the result array

2. Saving Voxel Models:
   - The SaveVox function takes voxel data and saves it as a .vox file
   - It only stores non-empty voxels to save space (sparse representation)
   - It writes all the necessary chunks required by the VOX format:
     a) MAIN chunk (container)
     b) PACK chunk (number of models)
     c) SIZE chunk (dimensions)
     d) XYZI chunk (voxel positions and colors)
     e) RGBA chunk (color palette)

This code allows the procedural generation system to import and export 3D models that can be 
viewed and edited in MagicaVoxel or other voxel editors. This makes it possible to use 
hand-crafted voxel tiles as inputs for procedural generation and to save the generated results 
in a standard format that can be used with other tools.
*/