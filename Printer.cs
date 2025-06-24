#nullable disable

using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DickMod.TextureGen;

public static partial class Printer
{
  public const string ENDL = "\n";

  private static bool _isConsoleAlloced = false;

  public static void Pause(string message = "Press any key to continue...")
  {
    CheckAndOpenConsole();

    if (message is not null)
    {
      Console.WriteLine(message);
    }

    Console.ReadKey();
  }

  public static void Stop()
  {
    FreeConsole();
    _isConsoleAlloced = false;
  }

  public static T Print<T>(this T obj, Predicate<T> printOnlyWhen = null)
  {
    if (printOnlyWhen is not null && !printOnlyWhen(obj))
    {
      return obj;
    }

    CheckAndOpenConsole();

    Console.Write(BuildString(obj).ToString());
    return obj;
  }

  public static T Print<T>(this T obj, Func<T, string> stringConverter, Predicate<T> printOnlyWhen = null)
  {
    if (printOnlyWhen is not null && !printOnlyWhen(obj))
    {
      return obj;
    }

    CheckAndOpenConsole();

    Console.Write(stringConverter(obj));
    return obj;
  }

  public static T PrintRaw<T>(this T obj) => Print(obj, o => o.ToString());

  public static T Println<T>(this T obj, Predicate<T> printOnlyWhen = null)
  {
    if (printOnlyWhen is not null && !printOnlyWhen(obj))
    {
      return obj;
    }

    CheckAndOpenConsole();

    Console.WriteLine(BuildString(obj).ToString());
    return obj;
  }

  public static ReadOnlySpan<T> Println<T>(this ReadOnlySpan<T> span)
  {
    CheckAndOpenConsole();

    Console.WriteLine(BuildString(span).ToString());
    return span;
  }

  public static Span<T> Println<T>(this Span<T> span)
  {
    Println((ReadOnlySpan<T>)span);
    return span;
  }

  public static T Println<T>(this T obj, Func<T, string> stringConverter, Predicate<T> printOnlyWhen = null)
  {
    if (printOnlyWhen is not null && !printOnlyWhen(obj))
    {
      return obj;
    }

    CheckAndOpenConsole();

    Console.WriteLine(stringConverter(obj));
    return obj;
  }

  public static T PrintlnRaw<T>(this T obj) => Println(obj, o => o.ToString());

  public static T PrintStatic<T>(this T obj, Predicate<T> printOnlyWhen = null)
  {
    if (printOnlyWhen is not null && !printOnlyWhen(obj))
    {
      return obj;
    }

    CheckAndOpenConsole();
    Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r" + BuildString(obj).ToString());
    return obj;
  }

  public static T PrintlnStatic<T>(this T obj, Predicate<T> printOnlyWhen = null)
  {
    if (printOnlyWhen is not null && !printOnlyWhen(obj))
    {
      return obj;
    }

    CheckAndOpenConsole();
    Console.WriteLine("\r" + new string(' ', Console.WindowWidth - 1) + "\r" + BuildString(obj).ToString());
    return obj;
  }

  public static StringBuilder BuildString<T>(ReadOnlySpan<T> obj)
  {
    StringBuilder builder = new();
    builder.Append('[');
    foreach (object item in obj)
    {
      builder.Append(BuildString(item)).Append(", ");
    }
    builder = builder.Length > 1 ?
              builder.Remove(builder.Length - 2, 2).Append(']') :
              builder.Append(']');
    return builder;
  }

  public static StringBuilder BuildString<T>(T obj)
  {
    Type objType = typeof(T);
    StringBuilder builder = new();

    MethodInfo toStringMethodInfo = objType.GetMethod(nameof(ToString), []);

    if (obj is string objString)
    {
      builder.Append(objString);
    }
    else if (obj is IEnumerable objIEnumerable)
    {
      builder.Append('[');
      foreach (object item in objIEnumerable)
      {
        builder.Append(BuildString(item)).Append(", ");
      }
      builder = builder.Length > 1 ?
                builder.Remove(builder.Length - 2, 2).Append(']') :
                builder.Append(']');
    }
    else if (objType.GetMethod("GetEnumerator", []) is MethodInfo getEnumeratorMethodInfo)
    {
      try
      {
        builder.Append('[');
        foreach (object item in (dynamic)obj)
        {
          builder.Append(BuildString(item)).Append(", ");
        }
        builder = builder.Length > 1 ?
                  builder.Remove(builder.Length - 2, 2).Append(']') :
                  builder.Append(']');
      }
      catch
      {
        goto TOSTRING_AND_RETURN;
      }      
    }
    else
    {
      builder.Append(obj.ToString());
    }

    return builder;

    TOSTRING_AND_RETURN:
    builder.Append(obj.ToString());
    return builder;
  }

  [LibraryImport("kernel32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool FreeConsole();

  [LibraryImport("kernel32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool AllocConsole();

  private static void CheckAndOpenConsole()
  {
    if (!_isConsoleAlloced)
    {
      AllocConsole();
      _isConsoleAlloced = true;
    }
  }
}
