using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DickMod.TextureGen;

public static class MyStreamExtensions
{
  public static void CopyByLength(this Stream source, Stream destination, int length)
  {
    byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
    int bytesRemaining = length;
    int bytesRead;

    try
    {
      while (bytesRemaining > 0 && (bytesRead = source.Read(buffer, 0, Math.Min(buffer.Length, bytesRemaining))) > 0)
      {
        destination.Write(buffer, 0, bytesRead);
        bytesRemaining -= bytesRead;
      }
    }
    catch
    {
      throw;
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buffer);
    }
  }
}
