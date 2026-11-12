using System;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Track
{
    /// <summary>
    /// Supplies the track network used at runtime: the workbook chosen by the user (remembered between runs) or,
    /// when none is set or it is invalid, the configuration asset assigned in the scene. In Play Mode every consumer
    /// receives the same network instance, so switch states are shared.
    /// </summary>
    public static class TrackSource
    {
        private const string PathKey = "Track.FilePath";

        private static TrackNetwork fileNetwork;
        private static TrackNetwork sceneNetwork;
        private static TrackConfiguration sceneNetworkSource;
        private static bool attempted;

        /// <summary>Gets the remembered track file path, or an empty string.</summary>
        public static string SavedFilePath => PlayerPrefs.GetString(PathKey, string.Empty);

        /// <summary>Gets the file the active track came from, or null when the scene asset is used.</summary>
        public static string LoadedFilePath { get; private set; }

        /// <summary>Gets the last load error, or null.</summary>
        public static string LastError { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            fileNetwork = null;
            sceneNetwork = null;
            sceneNetworkSource = null;
            attempted = false;
            LoadedFilePath = null;
            LastError = null;
        }

        /// <summary>
        /// Returns the file network when one is loaded in Play Mode; otherwise a network built from the scene asset.
        /// With <paramref name="requireValidLoop"/> false an invalid asset still yields a preview network (editor use).
        /// </summary>
        public static TrackNetwork ResolveNetwork(TrackConfiguration sceneAsset, bool requireValidLoop = true)
        {
            if (!Application.isPlaying)
                return sceneAsset == null ? null : TrackNetwork.FromConfiguration(sceneAsset, requireValidLoop);

            if (!attempted)
            {
                attempted = true;
                var saved = SavedFilePath;
                if (!string.IsNullOrEmpty(saved))
                {
                    if (TryLoad(saved, out var network, out var error))
                    {
                        fileNetwork = network;
                        LoadedFilePath = saved;
                        Debug.Log($"Track loaded from '{saved}': {network.Edges.Count} routes, {network.Nodes.Count} switches, " +
                                  $"{network.TotalLengthMeters:F1} m.");
                    }
                    else
                    {
                        LastError = error;
                        Debug.LogError($"Track file '{saved}' could not be used, falling back to the scene track: {error}");
                    }
                }
            }

            if (fileNetwork != null)
                return fileNetwork;
            if (sceneAsset == null)
                return null;
            if (sceneNetwork == null || sceneNetworkSource != sceneAsset)
            {
                sceneNetwork = TrackNetwork.FromConfiguration(sceneAsset, requireValidLoop);
                sceneNetworkSource = sceneAsset;
            }
            return sceneNetwork;
        }

        /// <summary>Validates the file and, when usable, remembers it for the next scene load and run.</summary>
        public static bool TrySelect(string path, out string error)
        {
            path = path == null ? string.Empty : path.Trim().Trim('"');
            if (!TryLoad(path, out var network, out error))
            {
                LastError = error;
                return false;
            }

            fileNetwork = network;
            attempted = true;
            LoadedFilePath = path;
            LastError = null;
            PlayerPrefs.SetString(PathKey, path);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>Forgets the file so the scene track asset is used again.</summary>
        public static void ClearSelection()
        {
            fileNetwork = null;
            attempted = true;
            LoadedFilePath = null;
            LastError = null;
            PlayerPrefs.DeleteKey(PathKey);
            PlayerPrefs.Save();
        }

        /// <summary>Reads a workbook and builds its validated track network.</summary>
        public static bool TryLoad(string path, out TrackNetwork network, out string error)
        {
            network = null;
            try
            {
                network = TrackFileReader.Read(path);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }
}
