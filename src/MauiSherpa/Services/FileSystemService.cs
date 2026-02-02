using System.Runtime.InteropServices;
using System.Diagnostics;
#if MACCATALYST
using Foundation;
using ObjCRuntime;
#endif

namespace MauiSherpa.Services;

public class FileSystemService : MauiSherpa.Core.Interfaces.IFileSystemService
{
    public async Task<string?> ReadFileAsync(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task WriteFileAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(path, content);
    }

    public Task<bool> FileExistsAsync(string path)
    {
        return Task.FromResult(File.Exists(path));
    }

    public Task<bool> DirectoryExistsAsync(string path)
    {
        return Task.FromResult(Directory.Exists(path));
    }

    public Task<IReadOnlyList<string>> GetFilesAsync(string path, string searchPattern = "*")
    {
        try
        {
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, searchPattern);
                return Task.FromResult<IReadOnlyList<string>>(files);
            }
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
    }

    public Task CreateDirectoryAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
        return Task.CompletedTask;
    }

    public void RevealInFileManager(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
#if MACCATALYST
            // Use 'open' command on macOS - works for both files and directories
            if (File.Exists(path))
            {
                // Reveal and select the file in Finder
                Process.Start("open", $"-R \"{path}\"");
            }
            else if (Directory.Exists(path))
            {
                // Open the directory in Finder
                Process.Start("open", $"\"{path}\"");
            }
#elif WINDOWS
            if (File.Exists(path))
            {
                // Select the file in Explorer
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (Directory.Exists(path))
            {
                // Open the directory in Explorer
                Process.Start("explorer.exe", $"\"{path}\"");
            }
#endif
        }
        catch
        {
            // Silently fail if we can't open the file manager
        }
    }
}
