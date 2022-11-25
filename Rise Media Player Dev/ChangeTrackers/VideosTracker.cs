﻿using Rise.App.ViewModels;
using Rise.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Rise.Common.Extensions;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;
using System.IO;
using System.Threading;
using System.Linq;
using Rise.Common;

namespace Rise.App.ChangeTrackers
{
    public class VideosTracker
    {
        /// <summary>
        /// Gets the app-wide MViewModel instance.
        /// </summary>
        private static MainViewModel MViewModel => App.MViewModel;

        /// <summary>
        /// Manage changes to the videos library.
        /// </summary>
        /// <param name="change">Change that ocurred.</param>
        public static async Task ManageVideoChange(StorageLibraryChange change)
        {
            StorageFile file;
            Video video;
            // Temp variable used for instantiating StorageFiles for sorting if needed later
            switch (change.ChangeType)
            {
                // New File in the Library
                case StorageLibraryChangeType.Created:
                    // Song was created..?
                    file = (StorageFile)await change.GetStorageItemAsync();
                    video = await Video.GetFromFileAsync(file);

                    await new VideoViewModel(video).SaveAsync();
                    break;

                case StorageLibraryChangeType.MovedIntoLibrary:
                    // Song was moved into the library
                    file = (StorageFile)await change.GetStorageItemAsync();
                    video = await Video.GetFromFileAsync(file);

                    await new VideoViewModel(video).SaveAsync();
                    break;

                case StorageLibraryChangeType.MovedOrRenamed:
                    // Song was renamed/moved
                    file = (StorageFile)await change.GetStorageItemAsync();
                    for (int i = 0; i < MViewModel.Videos.Count; i++)
                    {
                        if (change.PreviousPath == MViewModel.Videos[i].Location)
                        {
                            await MViewModel.Videos[i].DeleteAsync();
                        }
                    }
                    break;

                // File Removed From Library
                case StorageLibraryChangeType.Deleted:
                    // Song was deleted
                    for (int i = 0; i < MViewModel.Videos.Count; i++)
                    {
                        if (change.PreviousPath == MViewModel.Videos[i].Location)
                        {
                            await MViewModel.Videos[i].DeleteAsync();
                        }
                    }
                    break;

                case StorageLibraryChangeType.MovedOutOfLibrary:
                    // Song got moved out of the library
                    for (int i = 0; i < MViewModel.Videos.Count; i++)
                    {
                        if (change.PreviousPath == MViewModel.Videos[i].Location)
                        {
                            await MViewModel.Videos[i].DeleteAsync();
                        }
                    }
                    break;

                // Modified Contents
                case StorageLibraryChangeType.ContentsChanged:
                    // Song content was modified..?
                    file = (StorageFile)await change.GetStorageItemAsync();
                    for (int i = 0; i < MViewModel.Videos.Count; i++)
                    {
                        if (change.PreviousPath == MViewModel.Videos[i].Location)
                        {
                            await MViewModel.Videos[i].DeleteAsync();
                            await MViewModel.Videos[i].SaveAsync();
                        }
                    }
                    break;

                // Ignored Cases
                case StorageLibraryChangeType.EncryptionChanged:
                case StorageLibraryChangeType.ContentsReplaced:
                case StorageLibraryChangeType.IndexingStatusChanged:
                default:
                    // These are safe to ignore, I think
                    break;
            }
        }

        /// <summary>
        /// Manage changes to the videos library folders.
        /// </summary>
        public static async Task HandleVideosFolderChangesAsync(CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                return;

            List<VideoViewModel> toRemove = new();

            // Check if the video doesn't exist anymore, if so queue it then remove.
            for (int i = 0; i < MViewModel.Videos.Count; i++)
            {
                if (!File.Exists(MViewModel.Videos[i].Location))
                    toRemove.Add(MViewModel.Videos[i]);
            }

            foreach (VideoViewModel video in toRemove)
                await video.DeleteAsync();

            List<VideoViewModel> duplicates = new();

            // Check for duplicates and remove if any duplicate is found.
            for (int i = 0; i < MViewModel.Videos.Count; i++)
            {
                if (token.IsCancellationRequested)
                    return;

                for (int j = i + 1; j < MViewModel.Videos.Count; j++)
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (MViewModel.Videos[i].Location == MViewModel.Videos[j].Location)
                    {
                        duplicates.Add(MViewModel.Videos[j]);
                    }
                }
            }

            foreach (VideoViewModel video in duplicates)
            {
                if (token.IsCancellationRequested)
                    return;

                await video.DeleteAsync();
            }
        }

        /// <summary>
        /// Handle changes in the user's video library.
        /// </summary>
        public static async void VideosLibrary_ContentsChanged(IStorageQueryResultBase sender, object args)
        {
            StorageFolder changedFolder = sender.Folder;
            StorageLibraryChangeTracker folderTracker = changedFolder.TryGetChangeTracker();

            if (folderTracker != null)
            {
                folderTracker.Enable();

                StorageLibraryChangeReader changeReader = folderTracker.GetChangeReader();
                IReadOnlyList<StorageLibraryChange> changes = await changeReader.ReadBatchAsync();

                foreach (StorageLibraryChange change in changes)
                {
                    if (change.ChangeType == StorageLibraryChangeType.ChangeTrackingLost)
                    {
                        // Change tracker is in an invalid state and must be reset
                        // This should be a very rare case, but must be handled
                        folderTracker.Reset();
                        return;
                    }

                    if (change.IsOfType(StorageItemTypes.File))
                    {
                        await ManageVideoChange(change);
                    }
                    else if (change.IsOfType(StorageItemTypes.Folder))
                    {
                        // Not interested in folders
                    }
                    else
                    {
                        if (change.ChangeType == StorageLibraryChangeType.Deleted)
                        {
                            for (int i = 0; i < MViewModel.Videos.Count; i++)
                            {
                                if (change.PreviousPath == MViewModel.Videos[i].Location)
                                {
                                    await MViewModel.Videos[i].DeleteAsync();
                                }
                            }
                        }
                    }
                }

                // Mark that all the changes have been seen and for the change tracker
                // to never return these changes again
                await changeReader.AcceptChangesAsync();
            }
        }

        public static async Task HandleLibraryChangesAsync()
        {
            Debug.WriteLine("New changes!");
            StorageLibraryChangeTracker folderTracker = App.VideoLibrary.ChangeTracker;

            if (folderTracker != null)
            {
                StorageLibraryChangeReader changeReader = folderTracker.GetChangeReader();
                IReadOnlyList<StorageLibraryChange> changes = await changeReader.ReadBatchAsync();

                foreach (StorageLibraryChange change in changes)
                {
                    if (change.ChangeType == StorageLibraryChangeType.ChangeTrackingLost)
                    {
                        // Change tracker is in an invalid state and must be reset.
                        // This should be a very rare case, but must be handled.
                        folderTracker.Reset();
                        await MViewModel.StartFullCrawlAsync();
                        return;
                    }

                    if (change.IsOfType(StorageItemTypes.File))
                    {
                        await ManageVideoChange(change);
                    }
                    else if (change.IsOfType(StorageItemTypes.Folder))
                    {
                        await ManageFolderChangeAsync(change);
                    }
                    else
                    {
                        if (change.ChangeType == StorageLibraryChangeType.Deleted)
                        {
                            var videoOccurrences = MViewModel.Videos.Where(v => v.Location == change.PreviousPath);

                            foreach (var video in videoOccurrences)
                                await video.DeleteAsync();
                        }
                    }
                }

                // Mark that all the changes have been seen and for the change tracker
                // to never return these changes again
                await changeReader.AcceptChangesAsync();
            }
        }

        /// <summary>
        /// Manage changes to a folder in the video library using the <see cref="StorageLibraryChange" /> provided.
        /// </summary>
        /// <param name="change">Change that occurred.</param>
        public static async Task ManageFolderChangeAsync(StorageLibraryChange change)
        {
            StorageFolder folder = await change.GetStorageItemAsync() as StorageFolder;

            switch (change.ChangeType)
            {
                case StorageLibraryChangeType.MovedIntoLibrary:
                    await foreach (var file in folder.IndexAsync(QueryPresets.SongQueryOptions))
                        await MViewModel.SaveVideoModelAsync(file);
                    break;
                case StorageLibraryChangeType.MovedOutOfLibrary:
                case StorageLibraryChangeType.Deleted:
                    for (int i = 0; i < MViewModel.Videos.Count; i++)
                    {
                        string folderPath = Path.GetDirectoryName(MViewModel.Videos[i].Location);

                        if (change.PreviousPath == folderPath)
                            await MViewModel.Videos[i].DeleteAsync();
                    }
                    break;
                case StorageLibraryChangeType.MovedOrRenamed:
                    for (int i = 0; i < MViewModel.Videos.Count; i++)
                    {
                        string folderPath = Path.GetDirectoryName(MViewModel.Videos[i].Location);

                        if (change.PreviousPath == folderPath)
                        {
                            MViewModel.Songs[i].Location = Path.Combine(folder.Path, MViewModel.Videos[i].FileName);
                            await MViewModel.Videos[i].SaveAsync();
                        }
                    }
                    break;
            }
        }
    }
}
