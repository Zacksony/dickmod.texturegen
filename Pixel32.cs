#nullable enable

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace DickMod.TextureGen;

public static class Pixel32Convert
{
  public unsafe static IPixel32? FromImageFile(string path)
  {
    StandalonePixel32? pixel32 = null;
    try
    {
      using Image<Rgba32> image = Image.Load<Rgba32>(path);
      image.Configuration.PreferContiguousImageBuffers = true;
      pixel32 = new(image.Width, image.Height);

      if (!image.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> pixelMemory))
      {
        return null;
      }

      for (int i = 0; i < pixelMemory.Span.Length; i++)
      {
        byte* pixel32Ptr = (byte*)pixel32.Ptr;
        pixel32Ptr[i * 4 + 0] = pixelMemory.Span[i].R;
        pixel32Ptr[i * 4 + 1] = pixelMemory.Span[i].G;
        pixel32Ptr[i * 4 + 2] = pixelMemory.Span[i].B;
        pixel32Ptr[i * 4 + 3] = pixelMemory.Span[i].A;
      }

      return pixel32;
    }
    catch
    {
      pixel32?.Dispose();
      return null;
    }
  }

  public unsafe static void ToPng(IPixel32 pixel32, string path)
  {
    using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(new ReadOnlySpan<byte>(pixel32.Ptr.ToPointer(), pixel32.ByteCount), pixel32.PixelWidth, pixel32.PixelHeight);
    image.SaveAsPng(path);
  }

  #region XNB

  // XNB format converter source ref:
  // https://github.com/jlcebrian/SOR4Explorer

  /// <summary>
  /// Converts an <see cref="IPixel32"/> to an XNB Texture2D format data with compression.
  /// </summary>
  /// <param name="pixel32">The raw image data to convert.</param>
  /// <returns>A byte array contains the converted data.</returns>
  public unsafe static byte[] ToCompressedXNB(IPixel32 pixel32)
  {
    int pixelDataByteCount = pixel32.ByteCount;
    using MemoryStream baseMemoryStream = new();
    using DeflateStream deflateStream = new(baseMemoryStream, CompressionLevel.Optimal, leaveOpen: true);
    using BinaryWriter writer = new(deflateStream, Encoding.UTF8);
    writer.Write(Encoding.ASCII.GetBytes("XNBw"));                   // Signature
    writer.Write((byte)5);                                           // Version code
    writer.Write((byte)0);                                           
    writer.Write((Int32)85 + pixelDataByteCount);                    // File size (= 85 + pixel data size)
    writer.Write((byte)1);                                           // Item count
    writer.Write("Microsoft.Xna.Framework.Content.Texture2DReader");
    writer.Write((Int32)0);                                          // Always 0
    writer.Write((Int16)256);                                        // Always 256
    writer.Write((Int32)0);                                          // Texture format (0 = canonical)
    writer.Write((Int32)pixel32.PixelWidth);                            
    writer.Write((Int32)pixel32.PixelHeight);                           
    writer.Write((Int32)1);                                          // Mipmap count
    writer.Write((Int32)pixelDataByteCount);                         // Uncompressed size

    ReadOnlySpan<byte> pixelBytes = new(pixel32.Ptr.ToPointer(), pixelDataByteCount);
    writer.Write(pixelBytes);
    deflateStream.Dispose();

    return baseMemoryStream.ToArray();
  }

  /// <summary>
  /// Converts a compressed XNB Texture2D format data to a raw <see cref="IPixel32"/>.
  /// </summary>
  /// <param name="compressedXNBDataStream">A stream contains a compressed XNB Texture2D format data.</param>
  /// <returns>A standalone <see cref="IPixel32"/> contains raw image data.</returns>
  public unsafe static StandalonePixel32 FromCompressedXNB(Stream compressedXNBDataStream)
  {
    DeflateStream deflateStream = new(compressedXNBDataStream, CompressionMode.Decompress, leaveOpen: true);
    BinaryReader deflateReader = new(deflateStream, Encoding.UTF8, leaveOpen: false);

    try
    {
      _ = deflateReader.ReadBytes(69);
      int width = deflateReader.ReadInt32();
      int height = deflateReader.ReadInt32();
      int levelCount = deflateReader.ReadInt32();
      int dataSize = deflateReader.ReadInt32();

      StandalonePixel32 pixelData = new(width, height);

      using (UnmanagedMemoryStream pixelDataStream = new((byte*)pixelData.Ptr, dataSize, dataSize, FileAccess.Write))
      {
        deflateStream.CopyTo(pixelDataStream);
      }        

      return pixelData;
    }
    catch
    {
      throw;
    }
    finally
    {
      deflateReader.Dispose();
    }
  }

  #endregion
}

public unsafe static partial class Pixel32Extensions
{
  [LibraryImport("msvcrt.dll", EntryPoint = "memset")]
  [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
  private static partial IntPtr Memset(IntPtr dest, int c, int count);

  [LibraryImport("msvcrt.dll", EntryPoint = "memcpy")]
  [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
  private static partial IntPtr Memcpy(IntPtr dest, IntPtr src, uint count);

  public static StandalonePixel32 Clone(this IPixel32 source)
  {
    StandalonePixel32 cloned = new(source.PixelWidth, source.PixelHeight);

    Memcpy(cloned.Ptr, source.Ptr, (uint)source.ByteCount);

    return cloned;
  }

  public static void ClearPixels(this IPixel32 pixel32)
  {
    Memset(pixel32.Ptr, 0, pixel32.ByteCount);
  }

  public static void RGBAToOrFromBGRA(this IPixel32 pixel32)
  {
    uint* pixels = (uint*)pixel32.Ptr;
    int pixelCount = pixel32.PixelCount;
    for (int i = 0; i < pixelCount; i++)
    {
      pixels[i] = ( pixels[i] & 0xFF000000)         |
                  ((pixels[i] & 0x00FF0000) >> 16)  |
                  ( pixels[i] & 0x0000FF00)         |
                  ((pixels[i] & 0x000000FF) << 16)  ;
    }
  }
  
  public static void ColorMultipleRGBA(this IPixel32 pixel32, byte r, byte g, byte b, byte a)
  {
    uint* pixels = (uint*)pixel32.Ptr;
    int pixelCount = pixel32.PixelCount;
    for (int i = 0; i < pixelCount; i++)
    {
      byte* rgba = (byte*)(&(pixels[i]));
      rgba[0] = (byte)((rgba[0] * r) / 255);
      rgba[1] = (byte)((rgba[1] * g) / 255);
      rgba[2] = (byte)((rgba[2] * b) / 255);
      rgba[3] = (byte)((rgba[3] * a) / 255);
    }
  }

  public static void ColorMultipleRGB(this IPixel32 pixel32, byte r, byte g, byte b)
  {
    uint* pixels = (uint*)pixel32.Ptr;
    int pixelCount = pixel32.PixelCount;
    for (int i = 0; i < pixelCount; i++)
    {
      byte* rgba = (byte*)(&(pixels[i]));
      rgba[0] = (byte)((rgba[0] * r) / 255);
      rgba[1] = (byte)((rgba[1] * g) / 255);
      rgba[2] = (byte)((rgba[2] * b) / 255);
    }
  }

  public static void AlphaBlend(byte* dest, byte* source, int width, int height)
  {
    int stride = width * 4;

    for (int y = 0; y < height; y++)
    {
      byte* srcRow = source + y * stride;
      byte* dstRow = dest + y * stride;

      for (int x = 0; x < stride; x += 4)
      {
        byte srcB = srcRow[x];
        byte srcG = srcRow[x + 1];
        byte srcR = srcRow[x + 2];
        byte srcA = srcRow[x + 3];

        byte dstB = dstRow[x];
        byte dstG = dstRow[x + 1];
        byte dstR = dstRow[x + 2];
        byte dstA = dstRow[x + 3];

        int alpha = srcA;
        int invAlpha = 255 - alpha;

        dstRow[x] = (byte)((srcB * alpha + dstB * invAlpha) / 255);
        dstRow[x + 1] = (byte)((srcG * alpha + dstG * invAlpha) / 255);
        dstRow[x + 2] = (byte)((srcR * alpha + dstR * invAlpha) / 255);
        dstRow[x + 3] = (byte)(Math.Max(srcA, dstA));
      }
    }
  }
}

public sealed class StandalonePixel32(int pixelWidth, int pixelHeight) : IPixel32
{
  public IntPtr _ptr               = Marshal.AllocCoTaskMem(pixelWidth * pixelHeight * IPixel32.BytesPerPixel);
  public readonly int _pixelWidth  = pixelWidth;
  public readonly int _pixelHeight = pixelHeight;

  private bool _disposedValue = false;

  ~StandalonePixel32()
  {
    Dispose(disposing: false);
  }

  public IntPtr Ptr => _ptr;

  public int PixelWidth => _pixelWidth;

  public int PixelHeight => _pixelHeight;

  public int PixelCount => ((IPixel32)this).PixelCount;

  public int Stride => ((IPixel32)this).Stride;

  public int ByteCount => ((IPixel32)this).ByteCount;

  public void Dispose()
  {
    Dispose(disposing: true);
    GC.SuppressFinalize(this);
  }

  #region privates  

  private void Dispose(bool disposing)
  {    
    if (!_disposedValue)
    {
      if (disposing)
      {
        // no managed disposable here
      }

      if (_ptr != IntPtr.Zero)
      {
        Marshal.FreeCoTaskMem(_ptr);
        _ptr = IntPtr.Zero;
      }

      _disposedValue = true;
    }
  }

  #endregion
}

/// <summary>
/// Represents raw image data with 4 bytes per pixel.
/// </summary>
public interface IPixel32 : IDisposable
{
  public const byte BytesPerPixel = 4;

  /// <summary>
  /// Gets the pointer to the first byte of the image.
  /// </summary>
  public IntPtr Ptr { get; }

  /// <summary>
  /// Gets the pixel width of the image.
  /// </summary>
  public int PixelWidth { get; }

  /// <summary>
  /// Gets the pixel height of the image.
  /// </summary>
  public int PixelHeight { get; }

  /// <summary>
  /// Gets the total pixel count of the image. It is calculated by <see cref="PixelWidth"/> and <see cref="PixelHeight"/>.
  /// </summary>
  public sealed int PixelCount => PixelWidth * PixelHeight;

  /// <summary>
  /// Gets the count of bytes in a row of pixels. It is calculated by <see cref="PixelWidth"/> and <see cref="BytesPerPixel"/>.
  /// </summary>
  public sealed int Stride => PixelWidth * BytesPerPixel;

  /// <summary>
  /// Gets the total count of bytes of the image. It is caculated by <see cref="Stride"/> and <see cref="PixelHeight"/>.
  /// </summary>
  public sealed int ByteCount => Stride * PixelHeight;
}