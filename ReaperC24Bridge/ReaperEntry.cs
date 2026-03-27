using DriverControl24.Extension;
using System;
using System.Runtime.InteropServices;
using System.Text;

public static unsafe class ReaperEntry
{
    delegate IntPtr GetFuncRawDelegate(IntPtr name);
    delegate int RegisterRawDelegate(IntPtr name, IntPtr infostruct);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetModuleFileNameW(IntPtr hModule, StringBuilder lpFilename, uint nSize);

    [UnmanagedCallersOnly]
    static int HookCommandNative(int command, int flag)
        => ReaperBridge.OnReaperCommand(command, flag) ? 1 : 0;

    [UnmanagedCallersOnly]
    static int HookCommand2Native(IntPtr sec, int command, int val, int valhw, int relmode, IntPtr hwnd)
        => ReaperBridge.OnReaperCommand(command, 0) ? 1 : 0;

    static IntPtr GetFuncAnsi(GetFuncRawDelegate f, string name)
    {
        var p = Marshal.StringToHGlobalAnsi(name);
        try { return f(p); } finally { Marshal.FreeHGlobal(p); }
    }

    static int RegisterAnsi(RegisterRawDelegate r, string name, IntPtr info)
    {
        var p = Marshal.StringToHGlobalAnsi(name);
        try { return r(p, info); } finally { Marshal.FreeHGlobal(p); }
    }

    /// <summary>
    /// Lie un delegate de façon sécurisée : retourne null si la fonction est absente de Reaper.
    /// Évite ArgumentNullException sur les fonctions optionnelles ou spécifiques à une version.
    /// </summary>
    static T? GetDel<T>(GetFuncRawDelegate getFn, string name) where T : Delegate
    {
        var ptr = GetFuncAnsi(getFn, name);
        return ptr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(ptr) : null;
    }

    [UnmanagedCallersOnly(EntryPoint = "ReaperPluginEntry")]
    public static int ReaperPluginEntry(IntPtr hInstance, IntPtr rec)
    {
        try
        {
            if (rec == IntPtr.Zero) return 0;

            var sb = new StringBuilder(260);
            GetModuleFileNameW(hInstance, sb, 260);
            var dllDir = System.IO.Path.GetDirectoryName(sb.ToString()) ?? string.Empty;

            var reg   = Marshal.GetDelegateForFunctionPointer<RegisterRawDelegate>(Marshal.ReadIntPtr(rec, 16));
            var getFn = Marshal.GetDelegateForFunctionPointer<GetFuncRawDelegate>(Marshal.ReadIntPtr(rec, 24));

            // ── Fonctions critiques — exception si absentes ────────────────────
            ReaperAPI.ShowConsoleMsg  = Marshal.GetDelegateForFunctionPointer<ReaperAPI.ShowConsoleMsgDel>(GetFuncAnsi(getFn, "ShowConsoleMsg"));
            ReaperAPI.Plugin_Register = (name, info) => RegisterAnsi(reg, name, info);

            // ── Hooks ──────────────────────────────────────────────────────────
            RegisterAnsi(reg, "hookcommand",  (nint)(delegate* unmanaged<int, int, int>)&HookCommandNative);
            RegisterAnsi(reg, "hookcommand2", (nint)(delegate* unmanaged<IntPtr, int, int, int, int, IntPtr, int>)&HookCommand2Native);

            // ── Toutes les autres — null-safe via GetDel<T> ────────────────────
            ReaperAPI.Main_OnCommand        = GetDel<ReaperAPI.Main_OnCommandDel>(getFn, "Main_OnCommand");
            ReaperAPI.NamedCommandLookup    = GetDel<ReaperAPI.NamedCommandLookupDel>(getFn, "NamedCommandLookup");
            ReaperAPI.GetUserInputs         = GetDel<ReaperAPI.GetUserInputsDel>(getFn, "GetUserInputs");
            ReaperAPI.ShowMessageBox        = GetDel<ReaperAPI.ShowMessageBoxDel>(getFn, "ShowMessageBox");
            ReaperAPI.GetMainHwnd           = GetDel<ReaperAPI.GetMainHwndDel>(getFn, "GetMainHwnd");
            ReaperAPI.GetAppVersion         = GetDel<ReaperAPI.GetAppVersionDel>(getFn, "GetAppVersion");
            ReaperAPI.GetResourcePath       = GetDel<ReaperAPI.GetResourcePathDel>(getFn, "GetResourcePath");
            ReaperAPI.GetExePath            = GetDel<ReaperAPI.GetExePathDel>(getFn, "GetExePath");

            // Projet
            ReaperAPI.GetNumTracks          = GetDel<ReaperAPI.GetNumTracksDel>(getFn, "GetNumTracks");
            ReaperAPI.CountTracks           = GetDel<ReaperAPI.CountTracksDel>(getFn, "CountTracks");
            ReaperAPI.EnumProjects          = GetDel<ReaperAPI.EnumProjectsDel>(getFn, "EnumProjects");
            ReaperAPI.GetProjectPath        = GetDel<ReaperAPI.GetProjectPathDel>(getFn, "GetProjectPath");
            ReaperAPI.GetProjectPathEx      = GetDel<ReaperAPI.GetProjectPathExDel>(getFn, "GetProjectPathEx");
            ReaperAPI.GetProjectLength      = GetDel<ReaperAPI.GetProjectLengthDel>(getFn, "GetProjectLength");

            // Transport
            ReaperAPI.GetPlayState          = GetDel<ReaperAPI.GetPlayStateDel>(getFn, "GetPlayState");
            ReaperAPI.GetPlayPosition       = GetDel<ReaperAPI.GetPlayPositionDel>(getFn, "GetPlayPosition");
            ReaperAPI.GetPlayPosition2      = GetDel<ReaperAPI.GetPlayPosition2Del>(getFn, "GetPlayPosition2");
            ReaperAPI.GetCursorPosition     = GetDel<ReaperAPI.GetCursorPositionDel>(getFn, "GetCursorPosition");
            ReaperAPI.SetEditCurPos         = GetDel<ReaperAPI.SetEditCurPosDel>(getFn, "SetEditCurPos");
            ReaperAPI.GetLoopTimeRange2     = GetDel<ReaperAPI.GetLoopTimeRange2Del>(getFn, "GetLoopTimeRange2");
            ReaperAPI.SetLoopTimeRange2     = GetDel<ReaperAPI.SetLoopTimeRange2Del>(getFn, "SetLoopTimeRange2");

            // Tempo / Signature
            ReaperAPI.Master_GetTempo           = GetDel<ReaperAPI.Master_GetTempoDel>(getFn, "Master_GetTempo");
            ReaperAPI.Master_GetPlayRateAtTime  = GetDel<ReaperAPI.Master_GetPlayRateAtTimeDel>(getFn, "Master_GetPlayRateAtTime");
            ReaperAPI.TimeMap_timeToQN          = GetDel<ReaperAPI.TimeMap_timeToQNDel>(getFn, "TimeMap_timeToQN");
            ReaperAPI.TimeMap_QNToTime          = GetDel<ReaperAPI.TimeMap_QNToTimeDel>(getFn, "TimeMap_QNToTime");
            ReaperAPI.TimeMap2_timeToBeats      = GetDel<ReaperAPI.TimeMap2_timeToBeatsDel>(getFn, "TimeMap2_timeToBeats");
            ReaperAPI.TimeMap2_beatsToTime      = GetDel<ReaperAPI.TimeMap2_beatsToTimeDel>(getFn, "TimeMap2_beatsToTime");
            ReaperAPI.TimeMap_GetTimeSigAtTime  = GetDel<ReaperAPI.TimeMap_GetTimeSigAtTimeDel>(getFn, "TimeMap_GetTimeSigAtTime");
            ReaperAPI.CountTempoTimeSigMarkers  = GetDel<ReaperAPI.CountTempoTimeSigMarkersDel>(getFn, "CountTempoTimeSigMarkers");
            ReaperAPI.GetTempoTimeSigMarker     = GetDel<ReaperAPI.GetTempoTimeSigMarkerDel>(getFn, "GetTempoTimeSigMarker");
            ReaperAPI.format_timepos            = GetDel<ReaperAPI.format_timeposDel>(getFn, "format_timepos");
            ReaperAPI.format_timepos2           = GetDel<ReaperAPI.format_timepos2Del>(getFn, "format_timepos2");

            // Groups
            ReaperAPI.GetSetTrackGroupMembershipEx = GetDel<ReaperAPI.GetSetTrackGroupMembershipExDel>(getFn, "GetSetTrackGroupMembershipEx");

            //Channels
            ReaperAPI.GetNumAudioInputs = GetDel<ReaperAPI.GetNumAudioInputsDel>(getFn, "GetNumAudioInputs");
            ReaperAPI.GetNumAudioOutputs = GetDel<ReaperAPI.GetNumAudioOutputsDel>(getFn, "GetNumAudioOutputs");
            ReaperAPI.GetInputChannelName = GetDel<ReaperAPI.GetInputChannelNameDel>(getFn, "GetInputChannelName");
            ReaperAPI.GetOutputChannelName = GetDel<ReaperAPI.GetOutputChannelNameDel>(getFn, "GetOutputChannelName");

            // Pistes
            ReaperAPI.GetTrack                  = GetDel<ReaperAPI.GetTrackDel>(getFn, "GetTrack");
            ReaperAPI.GetMasterTrack            = GetDel<ReaperAPI.GetMasterTrackDel>(getFn, "GetMasterTrack");
            ReaperAPI.GetTrackName              = GetDel<ReaperAPI.GetTrackNameDel>(getFn, "GetTrackName");
            ReaperAPI.GetMediaTrackInfo_Value   = GetDel<ReaperAPI.GetMediaTrackInfo_ValueDel>(getFn, "GetMediaTrackInfo_Value");
            ReaperAPI.SetMediaTrackInfo_Value   = GetDel<ReaperAPI.SetMediaTrackInfo_ValueDel>(getFn, "SetMediaTrackInfo_Value");
            ReaperAPI.SetMediaTrackInfo         = GetDel<ReaperAPI.SetMediaTrackInfoDel>(getFn, "SetMediaTrackInfo_Value");
            ReaperAPI.GetTrackNumSends          = GetDel<ReaperAPI.GetTrackNumSendsDel>(getFn, "GetTrackNumSends");
            ReaperAPI.GetTrackSendInfo_Value    = GetDel<ReaperAPI.GetTrackSendInfo_ValueDel>(getFn, "GetTrackSendInfo_Value");
            ReaperAPI.SetTrackSendInfo_Value    = GetDel<ReaperAPI.SetTrackSendInfo_ValueDel>(getFn, "SetTrackSendInfo_Value");
            ReaperAPI.GetTrackColor             = GetDel<ReaperAPI.GetTrackColorDel>(getFn, "GetTrackColor");
            ReaperAPI.SetTrackColor             = GetDel<ReaperAPI.SetTrackColorDel>(getFn, "SetTrackColor");
            ReaperAPI.GetTrackStateChunk        = GetDel<ReaperAPI.GetTrackStateChunkDel>(getFn, "GetTrackStateChunk");
            ReaperAPI.SetTrackStateChunk        = GetDel<ReaperAPI.SetTrackStateChunkDel>(getFn, "SetTrackStateChunk");
            ReaperAPI.CreateTrackSend = GetDel<ReaperAPI.CreateTrackSendDel>(getFn, "CreateTrackSend");
            ReaperAPI.RemoveTrackSend = GetDel<ReaperAPI.RemoveTrackSendDel>(getFn, "RemoveTrackSend");
            ReaperAPI.AnyTrackSolo = GetDel<ReaperAPI.AnyTrackSoloDel>(getFn, "AnyTrackSolo");

            // Sélection
            ReaperAPI.CountSelectedTracks       = GetDel<ReaperAPI.CountSelectedTracksDel>(getFn, "CountSelectedTracks");
            ReaperAPI.GetSelectedTrack          = GetDel<ReaperAPI.GetSelectedTrackDel>(getFn, "GetSelectedTrack");
            ReaperAPI.SetTrackSelected          = GetDel<ReaperAPI.SetTrackSelectedDel>(getFn, "SetTrackSelected");
            ReaperAPI.CountSelectedMediaItems   = GetDel<ReaperAPI.CountSelectedMediaItemsDel>(getFn, "CountSelectedMediaItems");
            ReaperAPI.GetSelectedMediaItem      = GetDel<ReaperAPI.GetSelectedMediaItemDel>(getFn, "GetSelectedMediaItem");

            // FX
            ReaperAPI.TrackFX_GetCount              = GetDel<ReaperAPI.TrackFX_GetCountDel>(getFn, "TrackFX_GetCount");
            ReaperAPI.TrackFX_GetFXName             = GetDel<ReaperAPI.TrackFX_GetFXNameDel>(getFn, "TrackFX_GetFXName");
            ReaperAPI.TrackFX_GetEnabled            = GetDel<ReaperAPI.TrackFX_GetEnabledDel>(getFn, "TrackFX_GetEnabled");
            ReaperAPI.TrackFX_SetEnabled            = GetDel<ReaperAPI.TrackFX_SetEnabledDel>(getFn, "TrackFX_SetEnabled");
            ReaperAPI.TrackFX_GetNumParams          = GetDel<ReaperAPI.TrackFX_GetNumParamsDel>(getFn, "TrackFX_GetNumParams");
            ReaperAPI.TrackFX_GetParam              = GetDel<ReaperAPI.TrackFX_GetParamDel>(getFn, "TrackFX_GetParam");
            ReaperAPI.TrackFX_SetParam              = GetDel<ReaperAPI.TrackFX_SetParamDel>(getFn, "TrackFX_SetParam");
            ReaperAPI.TrackFX_GetParamNormalized    = GetDel<ReaperAPI.TrackFX_GetParamNormalizedDel>(getFn, "TrackFX_GetParamNormalized");
            ReaperAPI.TrackFX_SetParamNormalized    = GetDel<ReaperAPI.TrackFX_SetParamNormalizedDel>(getFn, "TrackFX_SetParamNormalized");
            ReaperAPI.TrackFX_GetParamName          = GetDel<ReaperAPI.TrackFX_GetParamNameDel>(getFn, "TrackFX_GetParamName");
            ReaperAPI.TrackFX_GetFormattedParamValue = GetDel<ReaperAPI.TrackFX_GetFormattedParamValueDel>(getFn, "TrackFX_GetFormattedParamValue");
            ReaperAPI.TrackFX_GetPreset             = GetDel<ReaperAPI.TrackFX_GetPresetDel>(getFn, "TrackFX_GetPreset");
            ReaperAPI.TrackFX_SetPreset             = GetDel<ReaperAPI.TrackFX_SetPresetDel>(getFn, "TrackFX_SetPreset");
            ReaperAPI.TrackFX_NavigatePresets       = GetDel<ReaperAPI.TrackFX_NavigatePresetsDel>(getFn, "TrackFX_NavigatePresets");
            ReaperAPI.TrackFX_Delete                = GetDel<ReaperAPI.TrackFX_DeleteDel>(getFn, "TrackFX_Delete");
            ReaperAPI.TrackFX_AddByName             = GetDel<ReaperAPI.TrackFX_AddByNameDel>(getFn, "TrackFX_AddByName");
            ReaperAPI.GetLastTouchedFX = GetDel<ReaperAPI.GetLastTouchedFXDel>(getFn, "GetLastTouchedFX");
            ReaperAPI.GetLastTouchedTrack = GetDel<ReaperAPI.GetLastTouchedTrackDel>(getFn, "GetLastTouchedTrack");
            ReaperAPI.TrackFX_SetOpen = GetDel<ReaperAPI.TrackFX_SetOpenDel>(getFn, "TrackFX_SetOpen");
            ReaperAPI.TrackFX_GetOpen = GetDel<ReaperAPI.TrackFX_GetOpenDel>(getFn, "TrackFX_GetOpen");
            ReaperAPI.EnumInstalledFX = GetDel<ReaperAPI.EnumInstalledFXDel>(getFn, "EnumInstalledFX");
            ReaperAPI.TrackFX_CopyToTrack = GetDel<ReaperAPI.TrackFX_CopyToTrackDel>(getFn, "TrackFX_CopyToTrack");

            // Items / Takes
            ReaperAPI.GetTrackNumMediaItems     = GetDel<ReaperAPI.GetTrackNumMediaItemsDel>(getFn, "GetTrackNumMediaItems");
            ReaperAPI.GetTrackMediaItem         = GetDel<ReaperAPI.GetTrackMediaItemDel>(getFn, "GetTrackMediaItem");
            ReaperAPI.GetActiveTake             = GetDel<ReaperAPI.GetActiveTakeDel>(getFn, "GetActiveTake");
            ReaperAPI.GetMediaItemInfo_Value    = GetDel<ReaperAPI.GetMediaItemInfo_ValueDel>(getFn, "GetMediaItemInfo_Value");
            ReaperAPI.SetMediaItemInfo_Value    = GetDel<ReaperAPI.SetMediaItemInfo_ValueDel>(getFn, "SetMediaItemInfo_Value");
            ReaperAPI.CountTakes                = GetDel<ReaperAPI.CountTakesDel>(getFn, "CountTakes");
            ReaperAPI.GetMediaItemTake          = GetDel<ReaperAPI.GetMediaItemTakeDel>(getFn, "GetMediaItemTake");
            ReaperAPI.GetTakeName               = GetDel<ReaperAPI.GetTakeNameDel>(getFn, "GetTakeName");
            ReaperAPI.SetActiveTake             = GetDel<ReaperAPI.SetActiveTakeDel>(getFn, "SetActiveTake");
            ReaperAPI.GetMediaItemTake_Source   = GetDel<ReaperAPI.GetMediaItemTake_SourceDel>(getFn, "GetMediaItemTake_Source");
            ReaperAPI.GetMediaSourceType        = GetDel<ReaperAPI.GetMediaSourceTypeDel>(getFn, "GetMediaSourceType");
            ReaperAPI.GetMediaSourceLength      = GetDel<ReaperAPI.GetMediaSourceLengthDel>(getFn, "GetMediaSourceLength");

            // Marqueurs
            ReaperAPI.CountProjectMarkers       = GetDel<ReaperAPI.CountProjectMarkersDel>(getFn, "CountProjectMarkers");
            ReaperAPI.EnumProjectMarkers        = GetDel<ReaperAPI.EnumProjectMarkersDel>(getFn, "EnumProjectMarkers3");
            ReaperAPI.AddProjectMarker          = GetDel<ReaperAPI.AddProjectMarkerDel>(getFn, "AddProjectMarker2");
            ReaperAPI.DeleteProjectMarker       = GetDel<ReaperAPI.DeleteProjectMarkerDel>(getFn, "DeleteProjectMarker");
            ReaperAPI.GoToMarker                = GetDel<ReaperAPI.GoToMarkerDel>(getFn, "GoToMarker");
            ReaperAPI.GoToRegion                = GetDel<ReaperAPI.GoToRegionDel>(getFn, "GoToRegion");

            // Surface de contrôle
            ReaperAPI.CSurf_OnVolumeChange      = GetDel<ReaperAPI.CSurf_OnVolumeChangeDel>(getFn, "CSurf_OnVolumeChange");
            ReaperAPI.CSurf_OnPanChange         = GetDel<ReaperAPI.CSurf_OnPanChangeDel>(getFn, "CSurf_OnPanChange");
            ReaperAPI.CSurf_OnMuteChange        = GetDel<ReaperAPI.CSurf_OnMuteChangeDel>(getFn, "CSurf_OnMuteChange");
            ReaperAPI.CSurf_OnSoloChange        = GetDel<ReaperAPI.CSurf_OnSoloChangeDel>(getFn, "CSurf_OnSoloChange");
            ReaperAPI.CSurf_OnRecArmChange      = GetDel<ReaperAPI.CSurf_OnRecArmChangeDel>(getFn, "CSurf_OnRecArmChange");
            ReaperAPI.CSurf_SetSurfaceVolume    = GetDel<ReaperAPI.CSurf_SetSurfaceVolumeDel>(getFn, "CSurf_SetSurfaceVolume");
            ReaperAPI.CSurf_SetSurfacePan       = GetDel<ReaperAPI.CSurf_SetSurfacePanDel>(getFn, "CSurf_SetSurfacePan");
            ReaperAPI.CSurf_SetSurfaceMute      = GetDel<ReaperAPI.CSurf_SetSurfaceMuteDel>(getFn, "CSurf_SetSurfaceMute");
            ReaperAPI.CSurf_SetSurfaceSolo      = GetDel<ReaperAPI.CSurf_SetSurfaceSoloDel>(getFn, "CSurf_SetSurfaceSolo");
            ReaperAPI.CSurf_SetSurfaceRecArm    = GetDel<ReaperAPI.CSurf_SetSurfaceRecArmDel>(getFn, "CSurf_SetSurfaceRecArm");
            ReaperAPI.CSurf_OnSendVolumeChange  = GetDel<ReaperAPI.CSurf_OnSendVolumeChangeDel>(getFn, "CSurf_OnSendVolumeChange");
            ReaperAPI.GetMixerScroll            = GetDel<ReaperAPI.GetMixerScrollDel>(getFn, "GetMixerScroll");
            ReaperAPI.SetMixerScroll            = GetDel<ReaperAPI.SetMixerScrollDel>(getFn, "SetMixerScroll");

            // Volume / Peak
            ReaperAPI.Track_GetPeakInfo         = GetDel<ReaperAPI.Track_GetPeakInfoDel>(getFn, "Track_GetPeakInfo");
            ReaperAPI.Track_GetPeakHoldDB       = GetDel<ReaperAPI.Track_GetPeakHoldDBDel>(getFn, "Track_GetPeakHoldDB");
            ReaperAPI.DB2SLIDER                 = GetDel<ReaperAPI.DB2SLIDERDel>(getFn, "DB2SLIDER");
            ReaperAPI.SLIDER2DB                 = GetDel<ReaperAPI.SLIDER2DBDel>(getFn, "SLIDER2DB");

            // Couleurs
            ReaperAPI.ColorToNative             = GetDel<ReaperAPI.ColorToNativeDel>(getFn, "ColorToNative");
            ReaperAPI.NativeToColor             = GetDel<ReaperAPI.NativeToColorDel>(getFn, "ColorFromNative"); // ✅ nom réel Reaper

            // Enveloppes
            ReaperAPI.CountTrackEnvelopes       = GetDel<ReaperAPI.CountTrackEnvelopesDel>(getFn, "CountTrackEnvelopes");
            ReaperAPI.GetTrackEnvelope          = GetDel<ReaperAPI.GetTrackEnvelopeDel>(getFn, "GetTrackEnvelope");
            ReaperAPI.GetFXEnvelope             = GetDel<ReaperAPI.GetFXEnvelopeDel>(getFn, "GetFXEnvelope");
            ReaperAPI.GetEnvelopeName           = GetDel<ReaperAPI.GetEnvelopeNameDel>(getFn, "GetEnvelopeName");
            ReaperAPI.CountEnvelopePoints       = GetDel<ReaperAPI.CountEnvelopePointsDel>(getFn, "CountEnvelopePoints");
            ReaperAPI.GetEnvelopePoint          = GetDel<ReaperAPI.GetEnvelopePointDel>(getFn, "GetEnvelopePoint");
            ReaperAPI.InsertEnvelopePoint       = GetDel<ReaperAPI.InsertEnvelopePointDel>(getFn, "InsertEnvelopePoint");
            ReaperAPI.DeleteEnvelopePointRange  = GetDel<ReaperAPI.DeleteEnvelopePointRangeDel>(getFn, "DeleteEnvelopePointRange");
            ReaperAPI.Envelope_SortPoints       = GetDel<ReaperAPI.Envelope_SortPointsDel>(getFn, "Envelope_SortPoints");

            // MIDI Editor
            ReaperAPI.MIDIEditor_GetActive      = GetDel<ReaperAPI.MIDIEditor_GetActiveDel>(getFn, "MIDIEditor_GetActive");
            ReaperAPI.MIDIEditor_GetTake        = GetDel<ReaperAPI.MIDIEditor_GetTakeDel>(getFn, "MIDIEditor_GetTake");
            ReaperAPI.MIDIEditor_GetSetting_int = GetDel<ReaperAPI.MIDIEditor_GetSetting_intDel>(getFn, "MIDIEditor_GetSetting_int");
            ReaperAPI.MIDIEditor_SetSetting_int = GetDel<ReaperAPI.MIDIEditor_SetSetting_intDel>(getFn, "MIDIEditor_SetSetting_int");

            // Undo / UI
            ReaperAPI.Undo_DoUndo2        = GetDel<ReaperAPI.Undo_DoUndo2Del>(getFn, "Undo_DoUndo2");
            ReaperAPI.Undo_DoRedo2        = GetDel<ReaperAPI.Undo_DoRedo2Del>(getFn, "Undo_DoRedo2");
            ReaperAPI.Undo_BeginBlock     = GetDel<ReaperAPI.Undo_BeginBlockDel>(getFn, "Undo_BeginBlock");
            ReaperAPI.Undo_EndBlock       = GetDel<ReaperAPI.Undo_EndBlockDel>(getFn, "Undo_EndBlock");
            ReaperAPI.Undo_CanUndo2       = GetDel<ReaperAPI.Undo_CanUndo2Del>(getFn, "Undo_CanUndo2");
            ReaperAPI.Undo_CanRedo2       = GetDel<ReaperAPI.Undo_CanRedo2Del>(getFn, "Undo_CanRedo2");
            ReaperAPI.UpdateTimeline            = GetDel<ReaperAPI.UpdateTimelineDel>(getFn, "UpdateTimeline");
            ReaperAPI.UpdateArrange             = GetDel<ReaperAPI.UpdateArrangeDel>(getFn, "UpdateArrange");
            ReaperAPI.TrackList_AdjustWindows   = GetDel<ReaperAPI.TrackList_AdjustWindowsDel>(getFn, "TrackList_AdjustWindows");


            ReaperAPI.GetProjectStateChangeCount = GetDel<ReaperAPI.GetProjectStateChangeCountDel>(getFn, "GetProjectStateChangeCount");
            ReaperAPI.IsProjectDirty = GetDel<ReaperAPI.IsProjectDirtyDel>(getFn, "IsProjectDirty");
            ReaperAPI.Main_SaveProject = GetDel<ReaperAPI.Main_SaveProjectDel>(getFn, "Main_SaveProject");
            ReaperAPI.SelectProjectInstance = GetDel<ReaperAPI.SelectProjectInstanceDel>(getFn, "SelectProjectInstance");

            return ReaperBridge.Initialize(rec, dllDir);
        }
        catch (Exception ex)
        {
            ReaperAPI.ShowConsoleMsg?.Invoke($"[Control24] Startup error: {ex.Message}{Environment.NewLine}");
            return 0;
        }
    }
}