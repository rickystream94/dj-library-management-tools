// <copyright file="Constants.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>
#pragma warning disable SA1600 // Elements should be documented

namespace LibTools4DJs
{
    /// <summary>
    /// Centralized constants for Rekordbox XML attributes, playlist/folder names, Mixed In Key defaults, and CLI tokens.
    /// </summary>
    internal static class Constants
    {
        // Rekordbox XML
        internal const string LibraryManagement = "LIBRARY MANAGEMENT";
        internal const string MIKKeyAnalysis = "MIK Key Analysis";
        internal const string MIKEnergyAnalysis = "MIK Energy Level Analysis";
        internal const string DeletePlaylistName = "Delete";
        internal const string TrackIdAttributeName = "TrackID";
        internal const string LocationAttributeName = "Location";
        internal const string KindAttributeName = "Kind";
        internal const string TonalityAttributeName = "Tonality";
        internal const string KeyAttributeName = "Key";
        internal const string EntriesAttributeName = "Entries";
        internal const string NameAttributeName = "Name";
        internal const string TypeAttributeName = "Type";
        internal const string KeyTypeAttributeName = "KeyType";
        internal const string ColourAttributeName = "Colour";
        internal const string LocalFileUriPrefix = "file://localhost/";
        internal const string CUEAnalysisPlaylistName = "CUE Analysis Playlist";
        internal const string RootPlaylistName = "ROOT";
        internal const string MikCuePointsPlaylistName = "MIK Cue Points";
        internal const string SyncFromMikFolderName = "LibTools4DJs_SyncFromMIK";
        internal const string BackupFolderName = "LibTools4DJs_Backups";
        internal const string LogsFolderName = "LibTools4DJs_Logs";

        // Mixed In Key
        internal const string MikFolderName = "Mixed In Key";
        internal const string MikDefaultVersion = "11.0";
        internal const string EnergyLevelToColourCodeMappingFileName = "EnergyLevelToColorCode.json";
        internal const string MikDatabaseFileName = "MIKStore.db";

        // CLI Tool
        internal const string CliToolDescription = ".NET CLI tools for DJs to streamline library management across Rekordbox and Mixed In Key, reduce friction between apps, and automate reliable sync workflows.";
        internal const string CliToolName = "djtools";

        // CLI Commands
        internal const string DeleteTracksCommand = "delete-tracks";
        internal const string SyncMikTagsToRekordboxCommand = "sync-mik-tags-to-rekordbox";
        internal const string SyncRekordboxLibraryToMikCommand = "sync-rekordbox-library-to-mik";
        internal const string SyncMikFolderToRekordboxCommand = "sync-mik-folder-to-rekordbox";

        // CLI Options
        internal const string DebugOption = "--debug";
        internal const string WhatIfOption = "--what-if";
        internal const string RekordboxXmlOption = "--xml";
        internal const string SaveLogsOption = "--save-logs";
        internal const string MikDbOption = "--mik-db";
        internal const string MikVersionOption = "--mik-version";
        internal const string ResetMikLibraryOption = "--reset-mik-library";
        internal const string MikFolderOption = "--mik-folder";

        // Paths / Environment
        internal const string UserProfileVariableName = "USERPROFILE";
        internal const string AppDataFolderName = "AppData";
        internal const string LocalAppDataFolderName = "Local";
        internal const string DefaultTimestampFormat = "yyyyMMdd_HHmmss";
        internal const string ConfigurationFolderName = "Configuration";
    }
}

#pragma warning restore SA1600 // Elements should be documented
