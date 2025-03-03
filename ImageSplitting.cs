using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media;
using ColorSplitter.Algorithms;
using SkiaSharp;

namespace ColorSplitter;

public class ImageSplitting
{
    public static byte Colors = 12; // The total amount of colors to split to.
    public static Color backgroundColor = default; // The Alpha Background Color
    private static Dictionary<Color, int> colorDictionary = new(); // The Color Directory, listing all colors.
    private static SKBitmap quantizedBitmap = new();
    public static bool RemoveStrayPixels = false; // Flag to enable/disable stray pixel removal

    public enum Algorithm
    {
        KMeans
    }

    // Handles Quantization of an Image
    public static (SKBitmap,Dictionary<Color, int>) colorQuantize(SKBitmap bitmap, Algorithm algorithm = Algorithm.KMeans, object? argument = null, int argument2 = 0, bool lab = true)
    {
        SKBitmap accessedBitmap = bitmap.Copy();
        switch (algorithm)
        {
            case Algorithm.KMeans:
                int Iterations = argument == null ? 4 : (int)argument;
                var kMeans = new KMeans(Colors, Iterations);
                (quantizedBitmap, colorDictionary) = kMeans.applyKMeans(accessedBitmap, lab);
                break;
        }
        
        // Don't even bother asking what int in colorDictionary was used for before, my guess is it was the total amount of that color?? :shrug:
        return (quantizedBitmap, colorDictionary);
    }

    // Gets the post-processed image with optional stray pixel removal applied
    public static SKBitmap getProcessedImage()
    {
        // Apply stray pixel removal if enabled, otherwise just return the quantized bitmap
        if (RemoveStrayPixels)
        {
            return removeStrayPixels(quantizedBitmap);
        }
        
        return quantizedBitmap;
    }

    // Removes stray pixels by replacing them with the most common neighboring color
    public static unsafe SKBitmap removeStrayPixels(SKBitmap bitmap)
    {
        // Create a copy of the bitmap to work with
        SKBitmap outputBitmap = bitmap.Copy();
        
        // Skip processing if the image is too small
        int width = bitmap.Width;
        int height = bitmap.Height;
        if (width <= 2 || height <= 2)
            return outputBitmap;
            
        // Get Memory Pointers for both Original and New Bitmaps
        byte* srcPtr = (byte*)bitmap.GetPixels().ToPointer();
        byte* dstPtr = (byte*)outputBitmap.GetPixels().ToPointer();
        
        // Define adjacent directions (up, right, down, left)
        int[] offsets = new int[4];
        offsets[0] = -width * 4;  // up
        offsets[1] = 4;           // right
        offsets[2] = width * 4;   // down
        offsets[3] = -4;          // left
        
        // Create a temporary buffer for a single scan line to avoid modifying pixels we're still checking
        byte* tempRow = stackalloc byte[width * 4];
        
        // Process each row (excluding the border rows)
        for (int y = 1; y < height - 1; y++)
        {
            // Calculate the start of the current row
            byte* rowStart = srcPtr + (y * width * 4);
            byte* outputRowStart = dstPtr + (y * width * 4);
            
            // Copy the current row to our temp buffer
            Buffer.MemoryCopy(outputRowStart, tempRow, width * 4, width * 4);
            
            // Process each pixel in the row (excluding the border pixels)
            for (int x = 1; x < width - 1; x++)
            {
                // Calculate the current pixel position
                byte* pixelPtr = rowStart + (x * 4);
                
                // Get current pixel color
                byte b = pixelPtr[0];
                byte g = pixelPtr[1];
                byte r = pixelPtr[2];
                byte a = pixelPtr[3];
                
                // Skip transparent pixels
                if (a == 0)
                    continue;
                
                // Check if pixel is isolated (no adjacent pixels of same color)
                bool isStrayPixel = true;
                
                // Check the 4 adjacent neighbors
                for (int i = 0; i < 4 && isStrayPixel; i++)
                {
                    // Get neighbor pixel
                    byte* neighborPtr = pixelPtr + offsets[i];
                    
                    // If any adjacent neighbor has the same color, it's not isolated
                    if (neighborPtr[0] == b && 
                        neighborPtr[1] == g && 
                        neighborPtr[2] == r)
                    {
                        isStrayPixel = false;
                    }
                }
                
                // Replace isolated pixels with most common adjacent color
                if (isStrayPixel)
                {
                    // We only have 4 neighbors, so we can use a simple array instead of a dictionary
                    // Each entry is a color and its count: (r, g, b, count)
                    byte[,] colorCounts = new byte[4, 4]; // Max 4 different colors (one from each direction)
                    int colorCount = 0;
                    
                    // Loop through the 4 adjacent neighbors
                    for (int i = 0; i < 4; i++)
                    {
                        // Get neighbor pixel
                        byte* neighborPtr = pixelPtr + offsets[i];
                        
                        // Skip transparent neighbors
                        if (neighborPtr[3] == 0)
                            continue;
                            
                        // Get neighbor color
                        byte nb = neighborPtr[0];
                        byte ng = neighborPtr[1];
                        byte nr = neighborPtr[2];
                        
                        // Check if we already have this color
                        bool foundColor = false;
                        for (int j = 0; j < colorCount; j++)
                        {
                            if (colorCounts[j, 0] == nr && 
                                colorCounts[j, 1] == ng && 
                                colorCounts[j, 2] == nb)
                            {
                                colorCounts[j, 3]++;
                                foundColor = true;
                                break;
                            }
                        }
                        
                        // If not found, add it
                        if (!foundColor && colorCount < 4)
                        {
                            colorCounts[colorCount, 0] = nr;
                            colorCounts[colorCount, 1] = ng;
                            colorCounts[colorCount, 2] = nb;
                            colorCounts[colorCount, 3] = 1;
                            colorCount++;
                        }
                    }
                    
                    // Find the most common color
                    byte maxCount = 0;
                    int maxIndex = 0;
                    
                    for (int j = 0; j < colorCount; j++)
                    {
                        if (colorCounts[j, 3] > maxCount)
                        {
                            maxCount = colorCounts[j, 3];
                            maxIndex = j;
                        }
                    }
                    
                    // Write the most common color to our temp buffer
                    byte* outputPixel = tempRow + (x * 4);
                    outputPixel[0] = colorCounts[maxIndex, 2]; // B
                    outputPixel[1] = colorCounts[maxIndex, 1]; // G
                    outputPixel[2] = colorCounts[maxIndex, 0]; // R
                    outputPixel[3] = a;                        // Keep original alpha
                }
            }
            
            // Copy the modified row back to the output bitmap
            Buffer.MemoryCopy(tempRow, outputRowStart, width * 4, width * 4);
        }
        
        // Return the Output Bitmap
        return outputBitmap;
    }

    // Gets the Layers from the latest MagickImage
    public static Dictionary<SKBitmap, string> getLayers(bool lowRes)
    {
        // Generates a Dictionary of <Image,Color>
        Dictionary<SKBitmap, string> Layers = new Dictionary<SKBitmap, string>();

        SKBitmap _Bitmap = quantizedBitmap.Copy();

        // If lowRes, we only handle a 64x64 image, for previews.
        if (lowRes)
        {
            _Bitmap = _Bitmap.Resize(new SKSizeI(64, 64), SKFilterQuality.None);
        }

        // Loop through each image in color dictionary, get the layer of the color, and the Hex Color, add to dictionary.
        foreach (var (key, value) in colorDictionary)
        {
            Layers.Add(getLayer(_Bitmap, key),key.R.ToString("X2") + key.G.ToString("X2") + key.B.ToString("X2"));
        }

        // Return fetched layers
        return Layers;
    }
    
    // Get Layer from a Bitmap, based on Color.
    public static unsafe SKBitmap getLayer(SKBitmap _Bitmap, Color color)
    {
        // Generate a new SkiaSharp bitmap based on original image size.
        SKBitmap OutputBitmap = new(_Bitmap.Width, _Bitmap.Height);

        // Get Memory Pointers for both Original and New Bitmaps
        var srcPtr = (byte*)_Bitmap.GetPixels().ToPointer();
        var dstPtr = (byte*)OutputBitmap.GetPixels().ToPointer();

        // Fetch & store Width and Height, for performance.
        var width = _Bitmap.Width;
        var height = _Bitmap.Height;

        // Loop through all rows & columns
        for (var row = 0; row < height; row++)
        for (var col = 0; col < width; col++)
        {
            // Fetch Original Image's Color from Memory, in BGRA8888 format.
            var b = *srcPtr++;
            var g = *srcPtr++;
            var r = *srcPtr++;
            var a = *srcPtr++;
            // If Color Matches, write to OutputBitmap Memory the same color.
            if (r == color.R && g == color.G && b == color.B)
            {
                *dstPtr++ = b;
                *dstPtr++ = g;
                *dstPtr++ = r;
                *dstPtr++ = a;
            }
            // Else, write Transparent pixel.
            else
            {
                *dstPtr++ = 0;
                *dstPtr++ = 0;
                *dstPtr++ = 0;
                *dstPtr++ = 0;
            }
        }
        
        // Return the Output Bitmap.
        return OutputBitmap;
    }
}