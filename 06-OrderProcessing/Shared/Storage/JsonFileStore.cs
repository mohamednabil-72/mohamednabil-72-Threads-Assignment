using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderProcessing.Shared.Storage;

public sealed class JsonFileStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _directory;

    public JsonFileStore() : this(DataFiles.DirectoryPath)
    {
    }

    public JsonFileStore(string directory)
    {
        _directory = Path.GetFullPath(directory);
    }

    public string DirectoryPath => _directory;

    public void ResetDirectory(params string[] subDirectories)
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        Directory.CreateDirectory(_directory);

        foreach (string subDirectory in subDirectories)
        {
            Directory.CreateDirectory(Path.Combine(_directory, subDirectory));
        }
    }

    public async Task<List<T>> ReadAllAsync<T>(string fileName, CancellationToken ct)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = GateFor(path);

        await gate.WaitAsync(ct);
        try
        {
            return await ReadCoreAsync<T>(path, fileName, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task WriteAllAsync<T>(string fileName, IEnumerable<T> items, CancellationToken ct)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = GateFor(path);
        List<T> snapshot = items.ToList();

        await gate.WaitAsync(ct);
        try
        {
            await WriteCoreAsync(path, snapshot, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TResult> UpdateAsync<T, TResult>(
        string fileName,
        Func<List<T>, TResult> mutate,
        CancellationToken ct)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = GateFor(path);

        await gate.WaitAsync(ct);
        try
        {
            List<T> items = await ReadCoreAsync<T>(path, fileName, ct);
            TResult result = mutate(items);
            await WriteCoreAsync(path, items, ct);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateAsync<T>(string fileName, Action<List<T>> mutate, CancellationToken ct)
    {
        await UpdateAsync<T, bool>(
            fileName,
            items =>
            {
                mutate(items);
                return true;
            },
            ct);
    }

    public Task AppendAsync<T>(string fileName, T item, CancellationToken ct)
    {
        return UpdateAsync<T>(fileName, items => items.Add(item), ct);
    }


    // Raw-text access is kept here so diagnostics and self-tests do not bypass the store.
    // Normal application code should use the typed collection methods above.
    public async Task<string> ReadRawTextAsync(string fileName, CancellationToken ct)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = GateFor(path);

        await gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            return await File.ReadAllTextAsync(path, System.Text.Encoding.UTF8, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task WriteRawTextAsync(string fileName, string content, CancellationToken ct)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = GateFor(path);

        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = TempPathFor(path);
            try
            {
                await File.WriteAllTextAsync(temp, content, new System.Text.UTF8Encoding(false), ct);
                ct.ThrowIfCancellationRequested();
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> CountRecordsAsync(string fileName, CancellationToken ct)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = GateFor(path);

        await gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(path))
            {
                return 0;
            }

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new DataFileCorruptedException(fileName, "the root element is not a JSON array");
            }

            return document.RootElement.GetArrayLength();
        }
        catch (JsonException ex)
        {
            throw new DataFileCorruptedException(fileName, ex.Message, ex);
        }
        finally
        {
            gate.Release();
        }
    }

    public List<T> ReadAll<T>(string fileName)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = GateFor(path);

        gate.Wait();
        try
        {
            return ReadCore<T>(path, fileName);
        }
        finally
        {
            gate.Release();
        }
    }

    public void WriteAll<T>(string fileName, IEnumerable<T> items)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = GateFor(path);
        List<T> snapshot = items.ToList();

        gate.Wait();
        try
        {
            WriteCore(path, snapshot);
        }
        finally
        {
            gate.Release();
        }
    }

    public TResult Update<T, TResult>(string fileName, Func<List<T>, TResult> mutate)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = GateFor(path);

        gate.Wait();
        try
        {
            List<T> items = ReadCore<T>(path, fileName);
            TResult result = mutate(items);
            WriteCore(path, items);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Update<T>(string fileName, Action<List<T>> mutate)
    {
        Update<T, bool>(
            fileName,
            items =>
            {
                mutate(items);
                return true;
            });
    }

    public void Append<T>(string fileName, T item)
    {
        Update<T>(fileName, items => items.Add(item));
    }

    public async Task DeleteAsync(string fileName, CancellationToken ct)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = GateFor(path);

        await gate.WaitAsync(ct);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Delete(string fileName)
    {
        string path = PathOf(fileName);
        SemaphoreSlim gate = GateFor(path);

        gate.Wait();
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private string PathOf(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException("A plain file name is expected.", nameof(fileName));
        }

        return Path.Combine(_directory, fileName);
    }

    private static SemaphoreSlim GateFor(string path)
    {
        return Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
    }

    private static async Task<List<T>> ReadCoreAsync<T>(string path, string fileName, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return new List<T>();
        }

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            List<T>? items = await JsonSerializer.DeserializeAsync<List<T>>(stream, Options, ct);
            return items ?? throw new DataFileCorruptedException(fileName, "the file contains null instead of an array");
        }
        catch (JsonException ex)
        {
            throw new DataFileCorruptedException(fileName, ex.Message, ex);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new List<T>();
        }
    }

    private static List<T> ReadCore<T>(string path, string fileName)
    {
        if (!File.Exists(path))
        {
            return new List<T>();
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            List<T>? items = JsonSerializer.Deserialize<List<T>>(stream, Options);
            return items ?? throw new DataFileCorruptedException(fileName, "the file contains null instead of an array");
        }
        catch (JsonException ex)
        {
            throw new DataFileCorruptedException(fileName, ex.Message, ex);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new List<T>();
        }
    }

    private static async Task WriteCoreAsync<T>(string path, List<T> items, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = TempPathFor(path);

        try
        {
            await using (var stream = new FileStream(
                temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, items, Options, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void WriteCore<T>(string path, List<T> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = TempPathFor(path);

        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, items, Options);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static string TempPathFor(string path)
    {
        return $"{path}.{Guid.NewGuid():N}.tmp";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
