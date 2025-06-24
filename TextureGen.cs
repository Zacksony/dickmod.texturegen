#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static DickMod.TextureGen.Printer;

namespace DickMod.TextureGen;

public static class Program
{
  const string SpritesPath = @"D:\SoR4Cracking\DickMod\dicksprites";
  const string OverrideSpritesPath = @"D:\SoR4Cracking\DickMod\dickoverridesprites";
  const string TexturesPath = @"D:\SoR4Cracking\DickMod\dicktextures";
  static readonly bool DoClear = true;

  public unsafe static void TranspBG()
  {
    string path = @"D:\SoR4Cracking\DickMod\dicksprites\objects\sor2\arcade.png";
    var pixel32 = Pixel32Convert.FromImageFile(path)!;
    uint color = 0xFF844100;
    uint* pixels = (uint*)pixel32.Ptr;
    for (int i = 0; i < pixel32.PixelCount; i++)
    {
      if (pixels[i] == color)
      {
        pixels[i] = 0;
      }
    }
    Pixel32Convert.ToPng(pixel32, path);
  }

  public unsafe static void Main()
  {
    //TranspBG();return;
    "Cleanup texture files..".Println();

    if (DoClear)
    {
      File.Create(Path.Combine(TexturesPath, "dick_textures")).Dispose();
      File.Create(Path.Combine(TexturesPath, "dick_texture_table")).Dispose();
    }

    "Preparing files..".Println();

    //string[] allPals = Directory.GetFiles(SpritesPath, "*.pal", SearchOption.AllDirectories);
    //foreach (string palFilePath in allPals)
    //{
    //  byte[] palBytes = File.ReadAllBytes(palFilePath);
    //  string palPngPath = Path.ChangeExtension(palFilePath, ".png");
    //  StandalonePixel32 palPixel32 = new(palBytes.Length / 3, 1);
    //  int* pixels = (int*)palPixel32.Ptr;
    //  for (int i = 0; i < palPixel32.PixelCount; i++)
    //  {
    //    pixels[i] = 
    //      palBytes[i * 3 + 0] << 24
    //    | palBytes[i * 3 + 1] << 16
    //    | palBytes[i * 3 + 2] << 8
    //    | 0;
    //  }
    //  Pixel32Convert.ToPng(palPixel32, palPngPath);
    //}    

    (string filePath, string assetPath)[] allFiles =
      [
        .. Directory.GetFiles(SpritesPath, "*.png", SearchOption.AllDirectories)
                    .Select(x => (x, Path.Combine("dicksprites", Path.ChangeExtension(Path.GetRelativePath(SpritesPath, x), null))
                                         .Replace("\\", "/"))),

        .. Directory.GetFiles(OverrideSpritesPath, "*.png", SearchOption.AllDirectories)
                    .Select(x => (x, Path.ChangeExtension(Path.GetRelativePath(OverrideSpritesPath, x), null).Replace("\\", "/")))
      ];

    TextureManager manager = new(TexturesPath);
    
    $"Total: {allFiles.Length}".Println();
    "Converting images..".Println();

    int currentIndex = -1;
    List<string> errorFilePaths = [];
    foreach ((string filePath, string assetPath) in allFiles)
    {
      currentIndex++;
      $"Current: [{currentIndex + 1} / {allFiles.Length}] {assetPath}".PrintStatic();

      using IPixel32? image = Pixel32Convert.FromImageFile(filePath);
      if (image is null)
      {
        errorFilePaths.Add(filePath);
        continue;
      }
      manager.AddOrReplace(assetPath, image);      
    }

    "Saving texture files..".PrintlnStatic();

    manager.Save();

    if (errorFilePaths.Count == 0)
    {
      Console.ForegroundColor = ConsoleColor.Green;
      "All successful!".Println();
      Console.ResetColor();
    }
    else
    {
      Console.ForegroundColor = ConsoleColor.DarkRed;
      ENDL.Print();
      $"Finished with {errorFilePaths.Count} errors. Please check the files below.".Println();
      ENDL.Print();
      Console.ForegroundColor = ConsoleColor.Red;
      foreach (var errorFilePath in errorFilePaths)
      {
        errorFilePath.Println();
      }
      Console.ResetColor();
    }

    Pause("\nPress any key to exit..");
  }
}
