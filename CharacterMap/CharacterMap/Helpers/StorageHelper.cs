using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CharacterMap.Helpers;

internal static class StorageHelper
{
    private static string CurrentTempFolderName = "T";
    public static string CurrentTempPath => Path.Combine(ApplicationData.Current.TemporaryFolder.Path, CurrentTempFolderName);

    public static string AppStorageFolder => field ?? Path.GetDirectoryName(ApplicationData.Current.TemporaryFolder.Path);

    /// <summary>
    /// Make sure we're  maintaining the size of our temp folder by clearing it at the start of every run.
    /// (Windows can also delete the contents of this folder if it needs to)
    /// </summary>
    /// <returns></returns>
    public static Task PrepareTempAsync()
    {
        return Task.Run(() =>
        {
            // 1. Find existing temp dirs
            var dirs = Directory.EnumerateDirectories(ApplicationData.Current.TemporaryFolder.Path).ToList();

            // 2. Set new temp dir for this session
            do
            {
                CurrentTempFolderName = Utils.Random.Next(0, 1000).ToString();
            }
            while (dirs.Any(d => Path.GetDirectoryName(d) == CurrentTempFolderName));

            // 3. Delete existing temp files
            foreach (var dir in dirs)
                Directory.Delete(dir, true);
        });
    }

    public static IAsyncOperation<StorageFolder> CreateTempFolderAsync(string path, CreationCollisionOption option = CreationCollisionOption.GenerateUniqueName)
    {
        // TODO : T can change per session
        return ApplicationData.Current.TemporaryFolder.CreateFolderAsync(Path.Combine(CurrentTempFolderName, path), option);
    }

    public static IAsyncOperation<StorageFile> CreateTempFileAsync(string name, CreationCollisionOption option = CreationCollisionOption.GenerateUniqueName)
    {
        // TODO : T can change per session
        return ApplicationData.Current.TemporaryFolder.CreateFileAsync(Path.Combine(CurrentTempFolderName, name), option);
    }

    public static IAsyncOperation<StorageFolder> PickFolderAsync()
    {
        FolderPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");

        return picker.PickSingleFolderAsync();
    }

    public static IAsyncOperation<StorageFile> PickOpenFileAsync(IEnumerable<string> fileTypes, string commitText)
    {
        FileOpenPicker picker = new ();
        foreach (var format in fileTypes)
            picker.FileTypeFilter.Add(format);

        picker.CommitButtonText = commitText;
        return picker.PickSingleFileAsync();
    }

    public static async Task<StorageFile> PickSaveFileAsync(
        string fileName,
        string key,
        IList<string> values,
        PickerLocationId suggestedLocation = PickerLocationId.PicturesLibrary)
    {
        FileSavePicker savePicker = new()
        {
            SuggestedStartLocation = suggestedLocation,
            SuggestedFileName = fileName
        };

        savePicker.FileTypeChoices.Add(key, values);

        try
        {
            return await savePicker.PickSaveFileAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task DeleteAsync(this StorageFolder folder, bool deleteFolder = false)
    {
        // 0. We have guaranteed access to use the System.IO api's within our own application
        //    storage folders
        if (deleteFolder && folder.Path.StartsWith(AppStorageFolder))
        {
            await Task.Run(() => Directory.Delete(folder.Path, true));
            return;
        }

        // 1. Delete all child folders
        var folders = await folder.GetFoldersAsync().AsTask().ConfigureAwait(false);
        if (folders.Count > 0)
        {
            var tasks = folders.Select(f => DeleteAsync(f, true));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        // 2. Delete child files
        var files = await folder.GetFilesAsync().AsTask().ConfigureAwait(false);
        if (files.Count > 0)
        {
            var tasks = files.Select(f => f.DeleteAsync(StorageDeleteOption.PermanentDelete).AsTask());
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        // 3. Delete folder
        if (deleteFolder)
            await folder.DeleteAsync(StorageDeleteOption.PermanentDelete).AsTask().ConfigureAwait(false);
    }
}
