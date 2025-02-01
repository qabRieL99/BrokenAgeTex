using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Please drag and drop .tex or .dds files onto the executable.");
            return;
        }

        int successCount = 0;
        int failureCount = 0;

        foreach (string inputFile in args)
        {
            try
            {
                string extension = Path.GetExtension(inputFile).ToLower();

                if (extension == ".tex" || extension == ".dds")
                {
                    Console.WriteLine($"\nProcessing: {Path.GetFileName(inputFile)}");

                    if (extension == ".tex")
                    {
                        ConvertTexToDds(inputFile);
                        successCount++;
                    }
                    else if (extension == ".dds")
                    {
                        ConvertDdsToTex(inputFile);
                        successCount++;
                    }
                }
                else
                {
                    Console.WriteLine($"\nSkipping {Path.GetFileName(inputFile)}: Unsupported file format. Please use .tex or .dds files.");
                    failureCount++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError processing {Path.GetFileName(inputFile)}: {ex.Message}");
                failureCount++;
            }
        }

        Console.WriteLine($"\nConversion complete!");
        Console.WriteLine($"Successfully converted: {successCount} file(s)");
        if (failureCount > 0)
        {
            Console.WriteLine($"Failed to convert: {failureCount} file(s)");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static void ConvertTexToDds(string inputFile)
    {
        using (var fs = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
        using (var br = new BinaryReader(fs))
        {
            // Read header
            string magic = new string(br.ReadChars(4));
            if (magic != "TEX ")
                throw new Exception("Invalid TEX file format");

            // Read image info
            short width = br.ReadInt16();
            short height = br.ReadInt16();
            byte mipmapCount = br.ReadByte();
            byte imageFormat = br.ReadByte();
            short paddingFlag = br.ReadInt16();

            // Calculate the dimensions of the first mipmap if needed
            if (mipmapCount > 1)
            {
                width /= 2;
                height /= 2;
            }

            if (paddingFlag == 0)
            {
                br.BaseStream.Position += 8; // Skip unknown values
            }

            // Read lengths
            int compressedLength = br.ReadInt32();
            int decompressedLength = br.ReadInt32();

            // Skip to offset 32
            long currentPosition = br.BaseStream.Position;
            if (currentPosition < 32)
                br.BaseStream.Position += 32 - currentPosition;

            // Read image data
            byte[] imageData;
            if (compressedLength == decompressedLength)
            {
                imageData = br.ReadBytes(decompressedLength);
            }
            else
            {
                byte[] compressedData = br.ReadBytes(compressedLength);
                imageData = new byte[decompressedLength];

                using (var compressedStream = new MemoryStream(compressedData))
                using (var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
                {
                    deflateStream.Read(imageData, 0, decompressedLength);
                }
            }

            // Write DDS file
            string outputFile = Path.ChangeExtension(inputFile, ".dds");
            using (var outFs = new FileStream(outputFile, FileMode.Create))
            using (var bw = new BinaryWriter(outFs))
            {
                WriteDdsHeader(bw, width, height, imageFormat == 3 ? "DXT1" : "DXT5");
                bw.Write(imageData);
            }

            Console.WriteLine($"Successfully converted to: {outputFile}");
        }
    }

    static void ConvertDdsToTex(string inputFile)
    {
        using (var fs = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
        using (var br = new BinaryReader(fs))
        {
            // Validate DDS file
            string magic = new string(br.ReadChars(4));
            if (magic != "DDS ")
                throw new Exception("Invalid DDS file format");

            // Read DDS header
            br.BaseStream.Position = 12; // Skip to dimensions
            int height = br.ReadInt32();
            int width = br.ReadInt32();

            // Skip to format
            br.BaseStream.Position = 84;
            string format = new string(br.ReadChars(4));

            // Read image data (skip 128 byte header)
            br.BaseStream.Position = 128;
            byte[] imageData = br.ReadBytes((int)(fs.Length - 128));

            // Compress the image data
            byte[] compressedData;
            using (var memoryStream = new MemoryStream())
            {
                using (var deflateStream = new DeflateStream(memoryStream, CompressionLevel.Optimal, true))
                {
                    deflateStream.Write(imageData, 0, imageData.Length);
                }
                compressedData = memoryStream.ToArray();
            }

            // Write TEX file
            string outputFile = Path.ChangeExtension(inputFile, ".tex");
            using (var outFs = new FileStream(outputFile, FileMode.Create))
            using (var bw = new BinaryWriter(outFs))
            {
                // Write TEX header
                bw.Write("TEX ".ToCharArray());
                bw.Write((short)width);
                bw.Write((short)height);
                bw.Write((byte)1); // Always use 1 mipmap
                bw.Write((byte)(format == "DXT1" ? 3 : 5)); // format
                bw.Write((short)0); // padding flag

                // Write additional padding data
                bw.Write(0); // Unknown value 1
                bw.Write(0); // Unknown value 2

                // Write lengths
                bw.Write(compressedData.Length);
                bw.Write(imageData.Length);

                // Pad to offset 32
                while (outFs.Position < 32)
                    bw.Write((byte)0);

                // Write compressed image data
                bw.Write(compressedData);
            }

            Console.WriteLine($"Successfully converted to: {outputFile}");
        }
    }

    static void WriteDdsHeader(BinaryWriter bw, int width, int height, string format)
    {
        // DDS header structure
        bw.Write("DDS ".ToCharArray());
        bw.Write(124); // header size
        bw.Write(0x1 | 0x2 | 0x4 | 0x1000); // flags (CAPS | HEIGHT | WIDTH | PIXELFORMAT)
        bw.Write(height);
        bw.Write(width);
        bw.Write(format == "DXT1" ? width * height / 2 : width * height); // pitchOrLinearSize
        bw.Write(0); // depth
        bw.Write(1); // mipmap count (always 1)

        // Reserved
        for (int i = 0; i < 11; i++)
            bw.Write(0);

        // Pixel format
        bw.Write(32); // size
        bw.Write(0x4); // flags (FOURCC)
        bw.Write(format.ToCharArray()); // four CC
        bw.Write(0); // RGB bit count
        bw.Write(0); // R mask
        bw.Write(0); // G mask
        bw.Write(0); // B mask
        bw.Write(0); // A mask

        // Caps
        bw.Write(0x1000); // caps 1 (TEXTURE)
        bw.Write(0); // caps 2
        bw.Write(0); // caps 3
        bw.Write(0); // caps 4
        bw.Write(0); // reserved
    }
}