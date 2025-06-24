#nullable enable

using SubstreamSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace DickMod.TextureGen;

/// <summary>
/// Provides various operations for loading, modifying, and saving SOR4 textures.
/// </summary>
public sealed class TextureManager : IDisposable
{
  /// <summary>
  /// Creates a new instance of <see cref="TextureManager"/> manages textures from <paramref name="textureFilesDirectory"/>.
  /// </summary>
  /// <param name="textureFilesDirectory">The directory containing texture files. Generally, it is the game's /data folder.</param>
  public TextureManager(string textureFilesDirectory)
  {
    // create custom texture cache file

    _ioGuid = Guid.NewGuid();

    try
    {
      (_textureCacheFilePath, _textureCacheFileStream) 
        = CreateTextureCacheFile(_ioGuid);
    }
    catch
    {
      DisposeAndDeleteCacheFile();
      throw;
    }

    // get texture paths

    _texturesFilesDirectory = textureFilesDirectory;

    try
    {
      (_textureFilePaths, _textureTableFilePaths, _backupTextureTableFilePaths) 
        = GetTexturePathsFromDirectory(textureFilesDirectory);
    }
    catch
    {
      throw;
    }

    // load chunks

    try
    {
      (_originalTextureFileLengths, _originalChunks, _customChunks) 
        = LoadChunks(_textureTableFilePaths, _backupTextureTableFilePaths);
    }
    catch
    {
      throw;
    }

    // initialize memory cache

    _memoryCache = new(path => ReadTexture(GetTexturePointerFromPath(path)), 
                       paths => ReadTextures(paths.Select(GetTexturePointerFromPath)));
  }

  /// <summary>
  /// Gets an enumerable containing all texture paths.
  /// <para>e.g. ["animatedsprites/sor4/playables/sprsor4adam/adam_0000", "animatedsprites/sor4/playables/sprsor4adam/adam_0003", ..]</para>
  /// </summary>
  public IEnumerable<string> TexturePaths => _customChunks.Keys.Concat(_originalChunks.Keys).Distinct();

  /// <summary>
  /// Gets the texture files' directory currently opened by the texture manager.
  /// </summary>
  public string TextureFilesDirectory => _texturesFilesDirectory;

  /// <summary>
  /// Gets or sets the index of the texture files (sorted in ascending order by file names) to be written to when a new texture is imported.
  /// <para>e.g. There're texture files in order:</para>
  /// <para>textures textures02 textures08</para>
  /// <para>Then if you set the index = 1, the file 'textures02' will be written;</para>
  /// <para>if you set the index = ^1, the file 'textures08' will be written.</para>
  /// <para>Default value is ^1.</para>
  /// </summary>
  public Index ExportFileIndexForCustomTextures { get; set; } = ^1;

  /// <summary>
  /// Gets the texture from the memory cache based on the path,
  /// <para>or loads from the file and cache it if the texture is not in the memory cache.</para>
  /// </summary>
  /// <param name="path">The path of the texture.</param>
  /// <returns>
  /// A new instance of <see cref="IPixel32"/> providing access to the image data.
  /// <para>Be sure to call <see cref="IDisposable.Dispose"/> at an appropriate time after use,</para>
  /// <para>or wrap it with a <see langword="using"/> statement.</para>
  /// </returns>
  public IPixel32 GetOrLoad(string path)
  {
    return _memoryCache.GetOrLoad(path);
  }

  /// <summary>
  /// Gets the textures from the memory cache based on the paths in order,
  /// <para>or loads from the files and cache them if the texture is not in the memory cache.</para>
  /// </summary>
  /// <param name="paths">The list of paths to be read.</param>
  /// <returns>
  /// An array of new instances of <see cref="IPixel32"/> providing access to the image data.
  /// <para>Be sure to call <see cref="IDisposable.Dispose"/> at an appropriate time after use,</para>
  /// <para>or wrap it with a <see langword="using"/> statement.</para>
  /// </returns>
  public IPixel32[] GetOrLoadMany(IList<string> paths)
  {
    return _memoryCache.GetOrLoadMany(paths);
  }  

  /// <summary>
  /// Gets the textures from the memory cache based on the paths in order,
  /// <para>or loads from the files and cache them if the texture is not in the memory cache.</para>
  /// </summary>
  /// <param name="paths">The list of paths to be read.</param>
  /// <returns>
  /// An array of new instances of <see cref="IPixel32"/> providing access to the image data.
  /// <para>Be sure to call <see cref="IDisposable.Dispose"/> at an appropriate time after use,</para>
  /// <para>or wrap it with a <see langword="using"/> statement.</para>
  /// </returns>
  public IPixel32[] GetOrLoadMany(IEnumerable<string> paths)
  {
    return GetOrLoadMany(paths.ToArray());
  } 

  /// <summary>
  /// Adds or replaces a custom texture to a given texture path.
  /// </summary>
  /// <param name="path">The texture path to add or replace.</param>
  /// <param name="data">The image data to add or replace.</param>
  public void AddOrReplace(string path, IPixel32 data)
  {
    _customChunks[path] = AddTextureAsXNBToFileCache(data);
    _memoryCache.Update(path, data);
  }

  /// <summary>
  /// Adds or replaces a custom texture to a given texture path, adding to memory cache.
  /// </summary>
  /// <param name="path">The texture path to add or replace.</param>
  /// <param name="data">The image data to add or replace.</param>
  /// <param name="handle">The handle of image data from the cache.</param>
  public void AddOrReplace(string path, IPixel32 data, out IPixel32 handle)
  {
    _customChunks[path] = AddTextureAsXNBToFileCache(data);
    handle = _memoryCache.AddOrUpdate(path, data);
  }

  /// <summary>
  /// Removes a texture. <paramref name="path"/> must be a non-original texture path.
  /// </summary>
  /// <param name="path">The texture path to remove.</param>
  /// <returns><see langword="true"/> if the texture is successfully removed, otherwise, <see langword="false"/></returns>
  public bool Remove(string path)
  {
    if (IsOriginalPath(path))
    {
      return false;
    }

    // remove from chunks
    return _customChunks.TryRemove(path, out _);
  }

  /// <summary>
  /// Restores a texture to its original. <paramref name="path"/> must be an original texture path.
  /// </summary>
  /// <param name="path">The texture path to restore.</param>
  /// <returns><see langword="true"/> if the texture is successfully restored, otherwise, <see langword="false"/></returns>
  public bool RestoreToOriginal(string path)
  {
    if (!IsOriginalPath(path))
    {
      return false;
    }

    _customChunks.TryRemove(path, out _);
    _memoryCache.AddOrUpdate(path, ReadTexture(GetTexturePointerFromPath(path)));
    return true;
  }

  /// <summary>
  /// Restores textures to their original. Only the original path in <paramref name="paths"/> will be restored.
  /// </summary>
  /// <param name="paths">The texture paths to restore.</param>
  public void RestoreToOriginal(IEnumerable<string> paths)
  {
    foreach (string path in paths)
    {
      if (!IsOriginalPath(path))
      {
        return;
      }

      _customChunks.TryRemove(path, out _);
    }

    foreach (var (path, pixel32) in paths.Zip(ReadTextures(paths.Select(GetTexturePointerFromPath))))
    {
      _memoryCache.AddOrUpdate(path, pixel32);
    }
  }

  /// <summary>
  /// Determines whether the texture at <paramref name="path"/> has been cached to the memory.
  /// </summary>
  /// <param name="path">The texture path to check.</param>
  /// <returns><see langword="true"/> if the texture at <paramref name="path"/> has been cached, otherwise, <see langword="false"/>.</returns>
  public bool HasCached(string path)
  {
    return _memoryCache.HasCached(path);
  }  

  /// <summary>
  /// Determines whether the any current chunks contains the specified <paramref name="path"/>.
  /// </summary>
  /// <param name="path">The texture path to check.</param>
  /// <returns><see langword="true"/> if the any current chunks contains <paramref name="path"/>, otherwise, <see langword="false"/>.</returns>
  public bool ContainsPath(string path)
  {
    return _customChunks.ContainsKey(path) || _originalChunks.ContainsKey(path);
  }

  /// <summary>
  /// Determines whether the given <paramref name="path"/> is one of the original paths.
  /// </summary>
  /// <param name="path">The texture path to check.</param>
  /// <returns><see langword="true"/> if <paramref name="path"/> is one of the original paths, otherwise, <see langword="false"/>.</returns>
  public bool IsOriginalPath(string path)
  {
    return _originalChunks.ContainsKey(path);
  }

  /// <summary>
  /// Determines whether the texture specified by <paramref name="path"/> is a custom texture.
  /// </summary>
  /// <param name="path">The texture path to check.</param>
  /// <returns><see langword="true"/> if the texture specified by <paramref name="path"/> is a custom texture, otherwise, <see langword="false"/>.</returns>
  public bool IsCustomTexture(string path)
  {
    return _customChunks.ContainsKey(path);
  }

  /// <summary>
  /// Saves all changes made by the current manager and permanently writes them to the texture file and table file.
  /// <para>This method is the only one that will actually make changes to the texture+table files.</para>
  /// </summary>
  public void Save()
  {
    List<FileStream> textureFileStreams = _textureFilePaths.Select(path => File.Open(path, FileMode.Open)).ToList();

    // create a temp file to write custom textures
    string customExportTempFilePath = Path.Combine(_texturesFilesDirectory, ProgramFolder, $"custom-chunks@{_ioGuid:N}.temp");
    FileStream customExportTempFileStream = File.Create(customExportTempFilePath);

    // write the custom chunks to the temp file
    // here the file index and positions are automatically calculated as if they were already in the file to be exported
    Dictionary<string, TexturePointer> exportingCustomChunks = [];
    customExportTempFileStream.Position = 0;
    long customExportTargetStartPosition = _originalTextureFileLengths[ExportFileIndexForCustomTextures];
    foreach ((string path, TexturePointer pointer) in _customChunks)
    {
      long startPosition = customExportTempFileStream.Position + customExportTargetStartPosition;
      CopyRawDataFromFile(textureFileStreams, _textureCacheFileStream, pointer, customExportTempFileStream);

      exportingCustomChunks.Add(path, (ExportFileIndexForCustomTextures, startPosition, pointer.Length));
    }

    // remove the custom chunks from texture files
    for (int i = 0; i < textureFileStreams.Count; i++)
    {
      textureFileStreams[i].SetLength(_originalTextureFileLengths[i]);
      textureFileStreams[i].Position = _originalTextureFileLengths[i];
    }

    // write the custom chunks to the target file
    FileStream customExportTargetFileStream = textureFileStreams[ExportFileIndexForCustomTextures];
    customExportTargetFileStream.Position = customExportTargetStartPosition;
    customExportTempFileStream.Position = 0;
    customExportTempFileStream.CopyTo(customExportTargetFileStream);

    // re-create table files
    List<BinaryWriter> tableFileStreamWriters = _textureTableFilePaths.Select(path => new BinaryWriter(File.Create(path), Encoding.Unicode)).ToList();
    foreach ((string path, TexturePointer pointer) in exportingCustomChunks.Concat(_originalChunks.Where(kv => !exportingCustomChunks.ContainsKey(kv.Key))))
    {
      BinaryWriter currentWriter = tableFileStreamWriters[pointer.TextureFileIndex];
      currentWriter.Write(path);
      currentWriter.Write(pointer.Position);
      currentWriter.Write(pointer.Length);
    }

    // dispose all streams
    customExportTempFileStream.Dispose();
    File.Delete(customExportTempFilePath);
    tableFileStreamWriters.ForEach(x => x.Dispose());
    textureFileStreams.ForEach(x => x.Dispose());
  }

  /// <summary>
  /// Deletes cache file, disposes all <see cref="IPixel32"/> in the memory cache.
  /// </summary>
  public void Dispose()
  {
    DisposeAndDeleteCacheFile();
    _memoryCache.Dispose();
  }

  #region non-publics

  #region private fields

  /*
   * The reason I use Immutable and Concurrent collections:
   * Because they are thread-safe.  
   * Texture reading may be performed by multiple threads simultaneously.  
   * Although there are ways to directly prevent WPF and WinForms from 
   * reading the texture at the same time, 
   * considering future scalability, I decided to use these 
   * thread-safe collections from the beginning to avoid excessive refactoring later.
   */

  // consts
  private const string ProgramFolder = ".texture-manager";
  private readonly static ImmutableArray<string> _possibleTextureFileNames = ["dick_textures"];
  private readonly static ImmutableArray<string> _possibleTextureTableFileNames = ["dick_texture_table"];

  // custom texture cache file
  private readonly Guid       _ioGuid;
  private readonly string     _textureCacheFilePath;
  private readonly FileStream _textureCacheFileStream;

  // file path info
  private readonly string                 _texturesFilesDirectory;
  private readonly ImmutableArray<string> _textureFilePaths;
  private readonly ImmutableArray<string> _textureTableFilePaths;
  private readonly ImmutableArray<string> _backupTextureTableFilePaths;
  private readonly ImmutableArray<long>   _originalTextureFileLengths;

  // chunk containers  
  private readonly ImmutableDictionary<string, TexturePointer>  _originalChunks;
  private readonly ConcurrentDictionary<string, TexturePointer> _customChunks;

  // cache
  private readonly SharedPixel32MemoryCache _memoryCache;

  #endregion

  #region private methods

  private TexturePointer GetTexturePointerFromPath(string path)
  {
    if (_customChunks.TryGetValue(path, out TexturePointer pointerInCustom))
    {
      return pointerInCustom;
    }
    else
    {
      return _originalChunks[path];
    }
  }

  private StandalonePixel32 ReadTexture(TexturePointer pointer)
  {
    if (pointer.IsCache)
    {
      return ReadPixelDataFromCompressedXNBFile(_textureCacheFileStream, pointer);
    }
    else
    {
      using FileStream textureStream = File.OpenRead(_textureFilePaths[pointer.TextureFileIndex]);
      return ReadPixelDataFromCompressedXNBFile(textureStream, pointer);
    }
  }

  private IEnumerable<StandalonePixel32> ReadTextures(IEnumerable<TexturePointer> pointers)
  {
    FileStream?[] textureFileStreams = new FileStream?[_textureFilePaths.Length];

    // read all
    foreach (TexturePointer pointer in pointers)
    {
      if (pointer.IsCache)
      {
        yield return ReadPixelDataFromCompressedXNBFile(_textureCacheFileStream, pointer);
      }
      else
      {
        FileStream textureStream = textureFileStreams[pointer.TextureFileIndex] ??= File.OpenRead(_textureFilePaths[pointer.TextureFileIndex]);
        yield return ReadPixelDataFromCompressedXNBFile(textureStream, pointer);
      }
    }

    // dispose all texture file streams
    foreach (FileStream? textureFileStream in textureFileStreams)
    {
      textureFileStream?.Dispose();
    }
  }

  private TexturePointer AddTextureAsXNBToFileCache(IPixel32 pixelData)
  {
    _textureCacheFileStream.Seek(0, SeekOrigin.End);
    long startPosition = _textureCacheFileStream.Position;
    byte[] xnb = Pixel32Convert.ToCompressedXNB(pixelData);
    _textureCacheFileStream.Write(xnb);
    return new TexturePointer(null, startPosition, xnb.Length);
  }

  private void DisposeAndDeleteCacheFile()
  {
    _textureCacheFileStream?.Dispose();

    if (_textureCacheFilePath is null)
    {
      return;
    }

    if (File.Exists(_textureCacheFilePath))
    {
      File.Delete(_textureCacheFilePath);
    }
  }

  #endregion

  #region private static methods

  private static void CopyRawDataFromFile(IList<FileStream> sourceTextureStreams, FileStream sourceCacheStream, TexturePointer pointer, FileStream destination)
  {
    FileStream sourceStream;

    if (pointer.IsCache)
    {
      sourceStream = sourceCacheStream;
    }
    else
    {
      sourceStream = sourceTextureStreams[pointer.TextureFileIndex];
    }

    sourceStream.Position = pointer.Position;
    sourceStream.CopyByLength(destination, pointer.Length);
  }

  private static StandalonePixel32 ReadPixelDataFromCompressedXNBFile(FileStream stream, TexturePointer pointer)
  {
    Substream substream = stream.Substream(pointer.Position, pointer.Length);
    return Pixel32Convert.FromCompressedXNB(substream);
  }

  private static (ImmutableArray<string> textureFilePaths,
                  ImmutableArray<string> textureTableFilePaths,
                  ImmutableArray<string> backupTextureTableFilePaths)
  GetTexturePathsFromDirectory(string directory)
  {
    List<string> textureFilePaths = [];
    List<string> textureTableFilePaths = [];

    // get texture file paths
    foreach (string textureFileName in _possibleTextureFileNames)
    {
      string textureFilePath = Path.Combine(directory, textureFileName);
      if (File.Exists(textureFilePath))
      {
        textureFilePaths.Add(textureFilePath);
      }
    }

    // get texture table file paths
    foreach (string textureTableFileName in _possibleTextureTableFileNames)
    {
      string textureTableFilePath = Path.Combine(directory, textureTableFileName);
      if (File.Exists(textureTableFilePath))
      {
        textureTableFilePaths.Add(textureTableFilePath);
      }
    }

    // the count of textures and tables must be equal
    if (textureFilePaths.Count != textureTableFilePaths.Count)
    {
      throw new InvalidOperationException("The count of textures and tables are not equal.");
    }

    // and, must be > 0
    if (textureFilePaths.Count == 0)
    {
      throw new InvalidOperationException("The count of textures and tables must be > 0.");
    }

    // backup
    List<string> backupTextureTableFilePaths = TryBackupTableFilesAndGetPaths(directory);

    return ([.. textureFilePaths],
            [.. textureTableFilePaths],
            [.. backupTextureTableFilePaths]);
  }

  private static (ImmutableArray<long> originalChunkSizes,
                  ImmutableDictionary<string, TexturePointer> originalChunks,
                  ConcurrentDictionary<string, TexturePointer> customChunks)
  LoadChunks(IList<string> textureTableFilePaths, IList<string> backupTextureTableFilePaths)
  {
    Dictionary<string, TexturePointer> originalChunks = [];
    ConcurrentDictionary<string, TexturePointer> customChunks = [];

    List<long> originalChunkSizes = [];

    for (int fileIndex = 0; fileIndex < textureTableFilePaths.Count; fileIndex++)
    {
      // read backuped file to get original chunks

      BinaryReader backupTableReader = new(File.OpenRead(backupTextureTableFilePaths[fileIndex]), Encoding.Unicode);

      try
      {
        long maxSize = 0;

        while (backupTableReader.BaseStream.Position < backupTableReader.BaseStream.Length)
        {
          string key = backupTableReader.ReadString();
          long position = backupTableReader.ReadInt64();
          int length = backupTableReader.ReadInt32();

          maxSize = Math.Max(maxSize, position + length);

          originalChunks.TryAdd(key, (fileIndex, position, length));
        }

        originalChunkSizes.Add(maxSize);
      }
      catch
      {
        throw;
      }
      finally
      {
        backupTableReader.Dispose();
      }

      // read current file to get custom chunks

      BinaryReader tableReader = new(File.OpenRead(textureTableFilePaths[fileIndex]), Encoding.Unicode);

      try
      {
        while (tableReader.BaseStream.Position < tableReader.BaseStream.Length)
        {
          string key = tableReader.ReadString();
          long position = tableReader.ReadInt64();
          int length = tableReader.ReadInt32();

          TexturePointer pointer = (fileIndex, position, length);

          // i.e. if found the same path in the original, then compare the pointer, if equals, then skip.
          if (!(originalChunks.TryGetValue(key, out TexturePointer pointerInBackup) && pointerInBackup == pointer))
          {
            customChunks.TryAdd(key, pointer);
          }
        }
      }
      catch
      {
        throw;
      }
      finally
      {
        tableReader.Dispose();
      }
    }

    return ([.. originalChunkSizes],
            originalChunks.ToImmutableDictionary(),
            customChunks);
  }

  private static (string filePath, FileStream stream) CreateTextureCacheFile(Guid guid)
  {
    string cacheFileName = $"cache@{guid:N}";
    string cacheFileDir = Path.Combine(".texture-cache");
    Directory.CreateDirectory(cacheFileDir);
    string cacheFilePath = Path.Combine(cacheFileDir, cacheFileName);
    return (cacheFilePath, File.Create(cacheFilePath));
  }

  private static List<string> TryBackupTableFilesAndGetPaths(string directory)
  {
    List<string> backupPaths = [];

    string destDir = Path.Combine(directory, ProgramFolder);
    Directory.CreateDirectory(destDir);

    string backupExtension = ".bak";

    foreach (string sourceFileName in _possibleTextureTableFileNames)
    {
      string sourcePath = Path.Combine(directory, sourceFileName);
      if (!File.Exists(sourcePath))
      {
        continue;
      }

      string destPath = Path.Combine(destDir, sourceFileName + backupExtension);
      backupPaths.Add(destPath);

      if (File.Exists(destPath))
      {
        continue;
      }

      File.Copy(sourcePath, destPath, true);
    }

    return backupPaths;
  }

  #endregion

  #region TexturePointer

  private readonly record struct TexturePointer(Index? Index, long Position, int Length)
  {
    public bool IsCache => Index is null;
    public Index TextureFileIndex => Index!.Value;

    public static implicit operator TexturePointer((Index?, long, int) v) => new(v.Item1, v.Item2, v.Item3);
    public static implicit operator (Index?, long, int)(TexturePointer v) => (v.Index, v.Position, v.Length);
  }

  #endregion

  #region SharedPixel32MemoryCache

  private sealed class SharedPixel32MemoryCache(Func<string, IPixel32> loadSingleDelegate,
                                                Func<IEnumerable<string>, IEnumerable<IPixel32>> loadManyDelegate) : IDisposable
  {
    private readonly ConcurrentDictionary<string, SharedPixel32> _cacheDict             = [];
    private readonly Func<string, IPixel32> _loadSingleDelegate                         = loadSingleDelegate;
    private readonly Func<IEnumerable<string>, IEnumerable<IPixel32>> _loadManyDelegate = loadManyDelegate;

    public Pixel32Handle GetOnlyFromCache(string path)
    {
      return new Pixel32Handle(this, path, _cacheDict[path]);
    }

    public Pixel32Handle GetOrLoad(string path)
    {
      return _cacheDict.TryGetValue(path, out SharedPixel32? cachedPixel32)
        ? new Pixel32Handle(this, path, cachedPixel32)
        : new Pixel32Handle(this, path, _cacheDict[path] = Load(path));
    }

    public Pixel32Handle[] GetOrLoadMany(IList<string> paths)
    {
      var pathsToLoad = new List<(int Index, string Path)>();
      var result = new Pixel32Handle[paths.Count];

      for (int i = 0; i < paths.Count; i++)
      {
        if (_cacheDict.TryGetValue(paths[i], out var cachedPixel32))
        {
          result[i] = new Pixel32Handle(this, paths[i], cachedPixel32);
        }
        else
        {
          pathsToLoad.Add((i, paths[i]));
        }
      }

      if (pathsToLoad.Count != 0)
      {
        foreach ((SharedPixel32 loadedPixel32, (int Index, string Path) pathInfo) in LoadMany(pathsToLoad.Select(p => p.Path)).Zip(pathsToLoad))
        {
          result[pathInfo.Index] = new Pixel32Handle(this, pathInfo.Path, loadedPixel32);
          _cacheDict[pathInfo.Path] = loadedPixel32;
        }
      }

      return result;
    }

    public SharedPixel32 AddOrUpdate(string path, IPixel32 basePixel32)
    {
      return _cacheDict.AddOrUpdate(path, new SharedPixel32(basePixel32), (k, v) => { v.Change(basePixel32); return v; });
    }

    public SharedPixel32? Update(string path, IPixel32 basePixel32)
    {
      if (!_cacheDict.ContainsKey(path))
      {
        return null;
      }

      SharedPixel32 sharedPixel32 = new(basePixel32);
      _cacheDict[path] = sharedPixel32;
      return sharedPixel32;
    }

    public void Remove(string path)
    {
      _cacheDict.TryRemove(path, out _);
    }

    public void Clear()
    {
      _cacheDict.Clear();
    }

    public bool HasCached(string path)
    {
      return _cacheDict.ContainsKey(path);
    }

    public void Dispose()
    {
      foreach (var (_, pixel32) in _cacheDict)
      {
        pixel32.Dispose();
      }

      Clear();
    }

    #region privates

    private SharedPixel32 Load(string path)
    {
      return new SharedPixel32(_loadSingleDelegate(path));
    }

    private IEnumerable<SharedPixel32> LoadMany(IEnumerable<string> path)
    {
      foreach (IPixel32 data in _loadManyDelegate(path))
      {
        yield return new SharedPixel32(data);
      }
    }

    #endregion   
  }

  #endregion

  #region Pixel32Handle

  private sealed class Pixel32Handle : IPixel32
  {
    private readonly SharedPixel32MemoryCache _sharedCache;
    private readonly string _path;
    private readonly SharedPixel32 _sharedPixel32;
    private readonly HashSet<Action<IPixel32>> _pointerChangingActions;
    private readonly HashSet<Action<IPixel32>> _pointerChangedActions;

    private bool _isDisposed = false;

    private SharedPixel32 Base => !_isDisposed ? _sharedPixel32 : throw new ObjectDisposedException($"{nameof(Pixel32Handle)}<{_path}>");

    public Pixel32Handle(SharedPixel32MemoryCache sharedCache, string path, SharedPixel32 sharedPixel32)
    {
      _sharedCache = sharedCache;
      _path = path;
      _sharedPixel32 = sharedPixel32; _sharedPixel32.AddRefCount();
      _pointerChangingActions = [];
      _pointerChangedActions = [];
    }

    public bool IsDisposed => _isDisposed;

    public string Path => _path;

    public nint Ptr => Base.Ptr;

    public int PixelWidth => Base.PixelWidth;

    public int PixelHeight => Base.PixelHeight;

    public event Action<IPixel32> SourceChanging
    {
      add
      {
        Base.SourceChanging -= value;
        Base.SourceChanging += value;
        _pointerChangingActions.Add(value);
      }

      remove
      {
        Base.SourceChanging -= value;
        _pointerChangingActions.Remove(value);
      }
    }

    public event Action<IPixel32> SourceChanged
    {
      add
      {
        Base.SourceChanged -= value;
        Base.SourceChanged += value;
        _pointerChangedActions.Add(value);
      }

      remove
      {
        Base.SourceChanged -= value;
        _pointerChangedActions.Remove(value);
      }
    }

    public void Dispose()
    {
      if (!_isDisposed)
      {
        // remove events that have subscribed to current instance
        // it's necessary for avoiding memory leak
        foreach (var action in _pointerChangingActions)
        {
          _sharedPixel32.SourceChanging -= action;
        }
        foreach (var action in _pointerChangedActions)
        {
          _sharedPixel32.SourceChanged -= action;
        }
        _pointerChangingActions.Clear();
        _pointerChangedActions.Clear();

        // refcount -= 1
        // if <= 0 then dispose and remove from the cache
        if (_sharedPixel32.ReleaseOne())
        {
          _sharedCache.Remove(_path);
        }

        _isDisposed = true;
      }
    }
  }

  #endregion

  #region SharedPixel32

  private sealed class SharedPixel32(IPixel32 @base) : IPixel32
  {
    private IPixel32 _base = @base;
    private int _refCount = 0;

    public int RefCount => _refCount;
    public nint Ptr => _base.Ptr;
    public int PixelWidth => _base.PixelWidth;
    public int PixelHeight => _base.PixelHeight;

    public event Action<IPixel32>? SourceChanging;
    public event Action<IPixel32>? SourceChanged;

    public void AddRefCount()
    {
      if (_refCount < 0)
      {
        _refCount = 1;
      }
      else
      {
        _refCount++;
      }
    }

    public bool ReleaseOne()
    {
      _refCount--;

      if (_refCount <= 0)
      {
        Dispose();
        return true;
      }
      else
      {
        return false;
      }
    }

    public bool Change(IPixel32 source)
    {
      if (source.Ptr == Ptr
       && source.PixelWidth == PixelWidth
       && source.PixelHeight == PixelHeight)
      {
        return false;
      }

      SourceChanging?.Invoke(this);

      _base = source;

      SourceChanged?.Invoke(this);

      return true;
    }

    public void Dispose()
    {
      _base.Dispose();
    }
  }

  #endregion
  #endregion
}
