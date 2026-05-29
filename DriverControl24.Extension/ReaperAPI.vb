Imports System.Runtime.InteropServices

Public Class ReaperAPI

    ' ── Règle NativeAOT : string sortante → IntPtr, string entrante → LPStr ──────
    ' Lire un buffer IntPtr : Marshal.PtrToStringAnsi(buf)
    ' Allouer un buffer    : Marshal.AllocHGlobal(size) / Marshal.FreeHGlobal(ptr)

    ' ═══════════════════════════════════════════════════════════════
    '  PROJET — création / fermeture / ouverture
    ' ═══════════════════════════════════════════════════════════════

    '''' <summary>Exécute une action Reaper par son ID (équivalent Main_OnCommand).</summary>
    'Public Delegate Sub Main_OnCommandDel(command As Integer, flag As Integer)
    'Public Shared Main_OnCommand As Main_OnCommandDel

    ''' <summary>Insère une piste à l'index donné (0-based). wantDefaults=True initialise les défauts.</summary>
    Public Delegate Sub InsertTrackAtIndexDel(idx As Integer, wantDefaults As Boolean)
    Public Shared InsertTrackAtIndex As InsertTrackAtIndexDel

    ''' <summary>Ouvre un projet .rpp par son chemin complet.</summary>
    Public Delegate Sub main_openProjectDel(<MarshalAs(UnmanagedType.LPStr)> fname As String)
    Public Shared main_openProject As main_openProjectDel

    ''' <summary>Marque le projet comme modifié (dirty &lt;&gt; 0) ou non modifié (dirty = 0).</summary>
    Public Delegate Sub MarkProjectDirtyDel(proj As IntPtr)
    Public Shared MarkProjectDirty As MarkProjectDirtyDel

    ' ═══════════════════════════════════════════════════════════════
    '  PROJET
    ' ═══════════════════════════════════════════════════════════════

    ''' <summary>Retourne le nombre de pistes du projet (section 0 = current project).</summary>
    Public Delegate Function GetNumTracksDel() As Integer
    Public Shared GetNumTracks As GetNumTracksDel

    ''' <summary>Retourne le pointeur sur le projet actif (idx=-1) ou un projet énuméré.</summary>
    Public Delegate Function EnumProjectsDel(idx As Integer, projfn As IntPtr, projfnsz As Integer) As IntPtr
    Public Shared EnumProjects As EnumProjectsDel

    ''' <summary>Chemin du projet dans buf (IntPtr alloué par l'appelant).</summary>
    Public Delegate Sub GetProjectPathDel(buf As IntPtr, bufsz As Integer)
    Public Shared GetProjectPath As GetProjectPathDel

    ''' <summary>Chemin du projet pour un projet spécifique.</summary>
    Public Delegate Sub GetProjectPathExDel(proj As IntPtr, buf As IntPtr, bufsz As Integer)
    Public Shared GetProjectPathEx As GetProjectPathExDel

    ''' <summary>Nombre de pistes (alternative à GetNumTracks, accepte un projet).</summary>
    Public Delegate Function CountTracksDel(proj As IntPtr) As Integer
    Public Shared CountTracks As CountTracksDel

    ''' <summary>Piste maître du projet.</summary>
    Public Delegate Function GetMasterTrackDel(proj As IntPtr) As IntPtr
    Public Shared GetMasterTrack As GetMasterTrackDel

    ''' <summary>Supprime une piste du projet actif par son index 0-based.</summary>
    Public Delegate Sub DeleteTrackDel(track As IntPtr)
    Public Shared DeleteTrack As DeleteTrackDel

    ' ═══════════════════════════════════════════════════════════════
    '  TRANSPORT
    ' ═══════════════════════════════════════════════════════════════

    ''' <summary>0=stop 1=play 2=pause 4=recording 5=rec+pause.</summary>
    Public Delegate Function GetPlayStateDel() As Integer
    Public Shared GetPlayState As GetPlayStateDel

    ''' <summary>Position de lecture actuelle en secondes.</summary>
    Public Delegate Function GetPlayPositionDel() As Double
    Public Shared GetPlayPosition As GetPlayPositionDel

    ''' <summary>Position rendue (légèrement en avance sur GetPlayPosition).</summary>
    Public Delegate Function GetPlayPosition2Del() As Double
    Public Shared GetPlayPosition2 As GetPlayPosition2Del

    ''' <summary>Position du curseur d'édition en secondes.</summary>
    Public Delegate Function GetCursorPositionDel() As Double
    Public Shared GetCursorPosition As GetCursorPositionDel

    ''' <summary>Déplace le curseur d'édition.</summary>
    Public Delegate Sub SetEditCurPosDel(time As Double, moveview As Boolean, seekplay As Boolean)
    Public Shared SetEditCurPos As SetEditCurPosDel

    ''' <summary>Tempo du projet en BPM.</summary>
    Public Delegate Function Master_GetTempoDel() As Double
    Public Shared Master_GetTempo As Master_GetTempoDel

    ''' <summary>Vitesse de lecture à un instant donné.</summary>
    Public Delegate Function Master_GetPlayRateAtTimeDel(time_s As Double) As Double
    Public Shared Master_GetPlayRateAtTime As Master_GetPlayRateAtTimeDel

    ''' <summary>Convertit secondes → QN (Quarter Notes).</summary>
    Public Delegate Function TimeMap_timeToQNDel(tpos As Double) As Double
    Public Shared TimeMap_timeToQN As TimeMap_timeToQNDel

    ''' <summary>Convertit QN → secondes.</summary>
    Public Delegate Function TimeMap_QNToTimeDel(qn As Double) As Double
    Public Shared TimeMap_QNToTime As TimeMap_QNToTimeDel

    ''' <summary>Position en mesures/temps (buf = "1.2.3.00" etc).</summary>
    Public Delegate Sub format_timepos2Del(tpos As Double, buf As IntPtr, buflen As Integer, modeoverride As Integer)
    Public Shared format_timepos2 As format_timepos2Del

    ' ═══════════════════════════════════════════════════════════════
    '  PISTES SENDS
    ' ═══════════════════════════════════════════════════════════════

    ''' <summary>Pointeur sur la piste (proj=0 pour projet actif, trackidx 0-based).</summary>
    Public Delegate Function GetTrackDel(proj As IntPtr, trackidx As Integer) As IntPtr
    Public Shared GetTrack As GetTrackDel

    ''' <summary>Nom de la piste dans buf.</summary>
    Public Delegate Function GetTrackNameDel(track As IntPtr, buf As IntPtr, bufsz As Integer) As Boolean
    Public Shared GetTrackName As GetTrackNameDel

    ''' <summary>Valeur d'un paramètre de piste ("D_VOL","D_PAN","B_MUTE","I_SOLO","B_ARM"…).</summary>
    Public Delegate Function GetMediaTrackInfo_ValueDel(
        tr As IntPtr,
        <MarshalAs(UnmanagedType.LPStr)> parmname As String) As Double
    Public Shared GetMediaTrackInfo_Value As GetMediaTrackInfo_ValueDel

    ''' <summary>Modifie un paramètre de piste.</summary>
    Public Delegate Function SetMediaTrackInfo_ValueDel(
        tr As IntPtr,
        <MarshalAs(UnmanagedType.LPStr)> parmname As String,
        newvalue As Double) As Boolean
    Public Shared SetMediaTrackInfo_Value As SetMediaTrackInfo_ValueDel

    ''' <deprecated>Utiliser SetMediaTrackInfo_Value.</deprecated>
    Public Delegate Sub SetMediaTrackInfoDel(
        tr As IntPtr,
        <MarshalAs(UnmanagedType.LPStr)> parmname As String,
        newvalue As Double)
    Public Shared SetMediaTrackInfo As SetMediaTrackInfoDel

    ''' <summary>Nombre d'envois/reçus d'une piste (category: 0=send -1=recv 1=HW).</summary>
    Public Delegate Function GetTrackNumSendsDel(track As IntPtr, category As Integer) As Integer
    Public Shared GetTrackNumSends As GetTrackNumSendsDel

    ''' <summary>Valeur d'un paramètre d'envoi.</summary>
    Public Delegate Function GetTrackSendInfo_ValueDel(
        track As IntPtr, category As Integer, sendidx As Integer,
        <MarshalAs(UnmanagedType.LPStr)> parmname As String) As Double
    Public Shared GetTrackSendInfo_Value As GetTrackSendInfo_ValueDel

    ''' <summary>Modifie un paramètre d'envoi.</summary>
    Public Delegate Function SetTrackSendInfo_ValueDel(
        track As IntPtr, category As Integer, sendidx As Integer,
        <MarshalAs(UnmanagedType.LPStr)> parmname As String,
        newvalue As Double) As Boolean
    Public Shared SetTrackSendInfo_Value As SetTrackSendInfo_ValueDel

    ''' <summary>Retourne un pointeur sur la dernière piste modifiée par l'utilisateur (IntPtr.Zero si aucune).</summary>
    Public Delegate Function GetLastTouchedTrackDel() As IntPtr
    Public Shared GetLastTouchedTrack As GetLastTouchedTrackDel
    ''' <summary>
    ''' Retourne True si au moins une piste du projet est en solo.
    ''' proj = IntPtr.Zero pour le projet actif.
    ''' </summary>
    Public Delegate Function AnyTrackSoloDel(proj As IntPtr) As Boolean
    Public Shared AnyTrackSolo As AnyTrackSoloDel

    ' ══════════════════════════════════════════════════════════════════════════
    '  ROUTING — CRÉATION / SUPPRESSION D'ENVOIS
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Crée un envoi depuis <c>tr</c> vers <c>desttrIn</c>.
    ''' Si <c>desttrIn</c> = IntPtr.Zero → sortie matérielle (hardware output).
    ''' Retourne l'index 0-based du nouvel envoi, ou -1 en cas d'échec.
    ''' </summary>
    Public Delegate Function CreateTrackSendDel(tr As IntPtr, desttrIn As IntPtr) As Integer
    Public Shared CreateTrackSend As CreateTrackSendDel

    ''' <summary>
    ''' Supprime un envoi/reçu d'une piste.
    ''' category : 0=envois  -1=reçus  1=sorties HW
    ''' sendidx  : index 0-based de l'envoi à supprimer.
    ''' Retourne True si supprimé avec succès.
    ''' </summary>
    Public Delegate Function RemoveTrackSendDel(tr As IntPtr, category As Integer, sendidx As Integer) As Boolean
    Public Shared RemoveTrackSend As RemoveTrackSendDel
    ' ═══════════════════════════════════════════════════════════════
    '  SÉLECTION
    ' ═══════════════════════════════════════════════════════════════

    ''' <summary>Nombre de pistes sélectionnées.</summary>
    Public Delegate Function CountSelectedTracksDel(proj As IntPtr) As Integer
    Public Shared CountSelectedTracks As CountSelectedTracksDel

    ''' <summary>Piste sélectionnée par index (0-based parmi les sélectionnées).</summary>
    Public Delegate Function GetSelectedTrackDel(proj As IntPtr, seltrackidx As Integer) As IntPtr
    Public Shared GetSelectedTrack As GetSelectedTrackDel

    ''' <summary>Sélectionner / désélectionner une piste.</summary>
    Public Delegate Sub SetTrackSelectedDel(track As IntPtr, selected As Boolean)
    Public Shared SetTrackSelected As SetTrackSelectedDel

    ''' <summary>Nombre d'items sélectionnés.</summary>
    Public Delegate Function CountSelectedMediaItemsDel(proj As IntPtr) As Integer
    Public Shared CountSelectedMediaItems As CountSelectedMediaItemsDel

    ''' <summary>Item sélectionné par index.</summary>
    Public Delegate Function GetSelectedMediaItemDel(proj As IntPtr, selitemidx As Integer) As IntPtr
    Public Shared GetSelectedMediaItem As GetSelectedMediaItemDel

    ' ═══════════════════════════════════════════════════════════════
    '  FX
    ' ═══════════════════════════════════════════════════════════════

    ''' <summary>Nombre de FX sur la piste.</summary>
    Public Delegate Function TrackFX_GetCountDel(track As IntPtr) As Integer
    Public Shared TrackFX_GetCount As TrackFX_GetCountDel

    ''' <summary>Nom du FX dans buf.</summary>
    Public Delegate Function TrackFX_GetFXNameDel(track As IntPtr, fx As Integer, buf As IntPtr, bufsz As Integer) As Boolean
    Public Shared TrackFX_GetFXName As TrackFX_GetFXNameDel

    ''' <summary>FX activé ou non.</summary>
    Public Delegate Function TrackFX_GetEnabledDel(track As IntPtr, fx As Integer) As Boolean
    Public Shared TrackFX_GetEnabled As TrackFX_GetEnabledDel

    ''' <summary>Active/désactive un FX.</summary>
    Public Delegate Sub TrackFX_SetEnabledDel(track As IntPtr, fx As Integer, enabled As Boolean)
    Public Shared TrackFX_SetEnabled As TrackFX_SetEnabledDel

    ''' <summary>Nombre de paramètres d'un FX.</summary>
    Public Delegate Function TrackFX_GetNumParamsDel(track As IntPtr, fx As Integer) As Integer
    Public Shared TrackFX_GetNumParams As TrackFX_GetNumParamsDel

    ''' <summary>Valeur normalisée [0..1] d'un paramètre.</summary>
    Public Delegate Function TrackFX_GetParamNormalizedDel(track As IntPtr, fx As Integer, param As Integer) As Double
    Public Shared TrackFX_GetParamNormalized As TrackFX_GetParamNormalizedDel

    ''' <summary>Modifie la valeur normalisée d'un paramètre.</summary>
    Public Delegate Function TrackFX_SetParamNormalizedDel(track As IntPtr, fx As Integer, param As Integer, value As Double) As Boolean
    Public Shared TrackFX_SetParamNormalized As TrackFX_SetParamNormalizedDel

    ''' <summary>Valeur brute + plage (minval/maxval par ByRef).</summary>
    Public Delegate Function TrackFX_GetParamDel(track As IntPtr, fx As Integer, param As Integer, ByRef minval As Double, ByRef maxval As Double) As Double
    Public Shared TrackFX_GetParam As TrackFX_GetParamDel

    ''' <summary>Modifie la valeur brute d'un paramètre.</summary>
    Public Delegate Function TrackFX_SetParamDel(tr As IntPtr, fx As Integer, param As Integer, val As Double) As Boolean
    Public Shared TrackFX_SetParam As TrackFX_SetParamDel

    ''' <summary>Nom d'un paramètre FX dans buf.</summary>
    Public Delegate Function TrackFX_GetParamNameDel(track As IntPtr, fx As Integer, param As Integer, buf As IntPtr, bufsz As Integer) As Boolean
    Public Shared TrackFX_GetParamName As TrackFX_GetParamNameDel

    ''' <summary>Valeur formatée d'un paramètre FX dans buf (ex: "-6.0 dB").</summary>
    Public Delegate Function TrackFX_GetFormattedParamValueDel(track As IntPtr, fx As Integer, param As Integer, buf As IntPtr, bufsz As Integer) As Boolean
    Public Shared TrackFX_GetFormattedParamValue As TrackFX_GetFormattedParamValueDel

    ''' <summary>Ouvre ou ferme la fenêtre d'un FX de piste.</summary>
    Public Delegate Sub TrackFX_SetOpenDel(track As IntPtr, fx As Integer, open As Boolean)
    Public Shared TrackFX_SetOpen As TrackFX_SetOpenDel

    ''' <summary>Retourne True si la fenêtre du FX est actuellement ouverte.</summary>
    Public Delegate Function TrackFX_GetOpenDel(track As IntPtr, fx As Integer) As Boolean
    Public Shared TrackFX_GetOpen As TrackFX_GetOpenDel

    ''' <summary>TrackFX_GetParamStepSizes — retourne les tailles de pas d'un paramètre FX.</summary>
    Public Delegate Function TrackFX_GetParamStepSizesDel(
    track As IntPtr,
    fxIndex As Integer,
    paramIndex As Integer,
    ByRef [step] As Double,
    ByRef smallStep As Double,
    ByRef largeStep As Double,
    ByRef isToggle As Boolean) As Boolean

    Public Shared TrackFX_GetParamStepSizes As TrackFX_GetParamStepSizesDel
    ''' <summary>TrackFX_GetParamEx — valeur brute + min, max, mid d'un paramètre FX.</summary>
    Public Delegate Function TrackFX_GetParamExDel(
        track As IntPtr,
        fxIndex As Integer,
        paramIndex As Integer,
        ByRef minVal As Double,
        ByRef maxVal As Double,
        ByRef midVal As Double) As Double

    Public Shared TrackFX_GetParamEx As TrackFX_GetParamExDel
    ''' <summary>TrackFX_FormatParamValueNormalized — formate une valeur normalisée arbitraire.</summary>
    Public Delegate Function TrackFX_FormatParamValueNormalizedDel(
        track As IntPtr,
        fx As Integer,
        param As Integer,
        normalizedValue As Double,
        buf As IntPtr,
        bufsz As Integer) As Boolean

    Public Shared TrackFX_FormatParamValueNormalized As TrackFX_FormatParamValueNormalizedDel

    ''' <summary>
    ''' Ouvre ou ferme la fenêtre d'un FX de piste.
    ''' showFlag : 0=cacher  1=montrer dans chaîne  2=toggle flottant  3=ouvrir flottant.
    ''' </summary>
    Public Delegate Sub TrackFX_ShowDel(track As IntPtr, fx As Integer, showFlag As Integer)
    Public Shared TrackFX_Show As TrackFX_ShowDel
    ''' <summary>
    ''' Délègue une action UI vers le thread principal de Reaper.
    ''' Initialisé par ReaperEntry au démarrage du plugin.
    ''' </summary>
    Public Delegate Sub EnqueueUIActionDel(action As Action)
    Public Shared EnqueueUIAction As EnqueueUIActionDel
    ' ═══════════════════════════════════════════════════════════════
    '  ITEMS / TAKES
    ' ═══════════════════════════════════════════════════════════════

    ''' <summary>Nombre d'items sur une piste.</summary>
    Public Delegate Function GetTrackNumMediaItemsDel(track As IntPtr) As Integer
    Public Shared GetTrackNumMediaItems As GetTrackNumMediaItemsDel

    ''' <summary>Item d'une piste par index.</summary>
    Public Delegate Function GetTrackMediaItemDel(track As IntPtr, itemidx As Integer) As IntPtr
    Public Shared GetTrackMediaItem As GetTrackMediaItemDel

    ''' <summary>Take actif d'un item.</summary>
    Public Delegate Function GetActiveTakeDel(item As IntPtr) As IntPtr
    Public Shared GetActiveTake As GetActiveTakeDel

    ''' <summary>Valeur d'un paramètre d'item ("D_POSITION","D_LENGTH","D_VOL"…).</summary>
    Public Delegate Function GetMediaItemInfo_ValueDel(
        item As IntPtr,
        <MarshalAs(UnmanagedType.LPStr)> parmname As String) As Double
    Public Shared GetMediaItemInfo_Value As GetMediaItemInfo_ValueDel

    ''' <summary>Modifie un paramètre d'item.</summary>
    Public Delegate Function SetMediaItemInfo_ValueDel(
        item As IntPtr,
        <MarshalAs(UnmanagedType.LPStr)> parmname As String,
        newvalue As Double) As Boolean
    Public Shared SetMediaItemInfo_Value As SetMediaItemInfo_ValueDel

    ' ═══════════════════════════════════════════════════════════════
    '  MARQUEURS / RÉGIONS
    ' ═══════════════════════════════════════════════════════════════

    ''' <summary>Nombre total de marqueurs+régions (nummarkersout/numregionsout par ByRef).</summary>
    Public Delegate Function CountProjectMarkersDel(proj As IntPtr, ByRef nummarkersout As Integer, ByRef numregionsout As Integer) As Integer
    Public Shared CountProjectMarkers As CountProjectMarkersDel

    ''' <summary>Énumère marqueurs et régions (name = IntPtr → Marshal.PtrToStringAnsi).</summary>
    Public Delegate Function EnumProjectMarkersDel(
        idx As Integer,
        ByRef isrgn As Boolean,
        ByRef pos As Double,
        ByRef rgnend As Double,
        ByRef name As IntPtr,
        ByRef markrgnindexnumber As Integer) As Integer
    Public Shared EnumProjectMarkers As EnumProjectMarkersDel

    ''' <summary>Ajoute un marqueur ou une région.</summary>
    Public Delegate Function AddProjectMarkerDel(
        proj As IntPtr, isrgn As Boolean, pos As Double, rgnend As Double,
        <MarshalAs(UnmanagedType.LPStr)> name As String,
        wantidx As Integer) As Integer
    Public Shared AddProjectMarker As AddProjectMarkerDel

    ''' <summary>Supprime un marqueur/région par son numéro.</summary>
    Public Delegate Function DeleteProjectMarkerDel(proj As IntPtr, markrgnindexnumber As Integer, isrgn As Boolean) As Boolean
    Public Shared DeleteProjectMarker As DeleteProjectMarkerDel

    ''' <summary>Va au marqueur numéroté (use_timeline_order=True pour ordre chronologique).</summary>
    Public Delegate Sub GoToMarkerDel(proj As IntPtr, marker_index As Integer, use_timeline_order As Boolean)
    Public Shared GoToMarker As GoToMarkerDel

    ''' <summary>Va à la région numérotée.</summary>
    Public Delegate Sub GoToRegionDel(proj As IntPtr, region_index As Integer, use_timeline_order As Boolean)
    Public Shared GoToRegion As GoToRegionDel

    ' ═══════════════════════════════════════════════════════════════
    '  SURFACE DE CONTRÔLE (CSurf)
    ' ═══════════════════════════════════════════════════════════════

    ''' <summary>Notifie Reaper d'un changement de volume via surface (retourne la valeur appliquée).</summary>
    Public Delegate Function CSurf_OnVolumeChangeDel(trackid As IntPtr, volume As Double, relative As Boolean) As Double
    Public Shared CSurf_OnVolumeChange As CSurf_OnVolumeChangeDel

    ''' <summary>Notifie Reaper d'un changement de pan via surface.</summary>
    Public Delegate Function CSurf_OnPanChangeDel(trackid As IntPtr, pan As Double, relative As Boolean) As Double
    Public Shared CSurf_OnPanChange As CSurf_OnPanChangeDel

    ''' <summary>Notifie Reaper d'un changement de mute via surface.</summary>
    Public Delegate Function CSurf_OnMuteChangeDel(trackid As IntPtr, mute As Integer) As Boolean
    Public Shared CSurf_OnMuteChange As CSurf_OnMuteChangeDel

    ''' <summary>Notifie Reaper d'un changement de solo via surface.</summary>
    Public Delegate Function CSurf_OnSoloChangeDel(trackid As IntPtr, solo As Integer) As Boolean
    Public Shared CSurf_OnSoloChange As CSurf_OnSoloChangeDel

    ''' <summary>Notifie Reaper d'un changement de rec arm via surface.</summary>
    Public Delegate Function CSurf_OnRecArmChangeDel(trackid As IntPtr, recarm As Integer) As Boolean
    Public Shared CSurf_OnRecArmChange As CSurf_OnRecArmChangeDel

    ''' <summary>Notifie Reaper d'un changement de volume d'envoi via surface.</summary>
    Public Delegate Function CSurf_OnSendVolumeChangeDel(trackid As IntPtr, send_index As Integer, volume As Double, relative As Boolean) As Double
    Public Shared CSurf_OnSendVolumeChange As CSurf_OnSendVolumeChangeDel

    ''' <summary>Pousse le volume vers la surface (toutes sauf ignoresurf).</summary>
    Public Delegate Sub CSurf_SetSurfaceVolumeDel(trackid As IntPtr, volume As Double, ignoresurf As IntPtr)
    Public Shared CSurf_SetSurfaceVolume As CSurf_SetSurfaceVolumeDel

    ''' <summary>Pousse le pan vers la surface.</summary>
    Public Delegate Sub CSurf_SetSurfacePanDel(trackid As IntPtr, pan As Double, ignoresurf As IntPtr)
    Public Shared CSurf_SetSurfacePan As CSurf_SetSurfacePanDel

    ''' <summary>Pousse le mute vers la surface.</summary>
    Public Delegate Sub CSurf_SetSurfaceMuteDel(trackid As IntPtr, mute As Boolean, ignoresurf As IntPtr)
    Public Shared CSurf_SetSurfaceMute As CSurf_SetSurfaceMuteDel

    ''' <summary>Pousse le solo vers la surface.</summary>
    Public Delegate Sub CSurf_SetSurfaceSoloDel(trackid As IntPtr, solo As Boolean, ignoresurf As IntPtr)
    Public Shared CSurf_SetSurfaceSolo As CSurf_SetSurfaceSoloDel

    ''' <summary>Pousse le rec arm vers la surface.</summary>
    Public Delegate Sub CSurf_SetSurfaceRecArmDel(trackid As IntPtr, recarm As Boolean, ignoresurf As IntPtr)
    Public Shared CSurf_SetSurfaceRecArm As CSurf_SetSurfaceRecArmDel

    ''' <summary>Scroll du mixer (position de la piste la plus à gauche).</summary>
    Public Delegate Function GetMixerScrollDel() As IntPtr
    Public Shared GetMixerScroll As GetMixerScrollDel

    ''' <summary>Définit la piste la plus à gauche du mixer visible.</summary>
    Public Delegate Function SetMixerScrollDel(leftmosttrack As IntPtr) As IntPtr
    Public Shared SetMixerScroll As SetMixerScrollDel

    ''' <summary>
    ''' Retourne le volume et le pan POST-automation affichés dans l'UI Reaper.
    ''' C'est la seule fonction qui reflète la valeur réelle du fader en lecture.
    ''' volumeOut : 0..4  (1.0 = 0 dB)   panOut : -1..1
    ''' </summary>
    Public Delegate Function GetTrackUIVolPanDel(track As IntPtr,
                                                 ByRef volumeOut As Double,
                                                 ByRef panOut As Double) As Boolean
    Public Shared GetTrackUIVolPan As GetTrackUIVolPanDel

    ''' <summary>
    ''' Retourne le volume et le pan POST-automation d'un envoi (catégorie 0 uniquement).
    ''' Équivalent de <see cref="GetTrackUIVolPan"/> mais pour les envois.
    ''' volumeOut : 0..4  (1.0 = 0 dB)   panOut : -1..1
    ''' Retourne False si l'index est hors limites.
    ''' </summary>
    Public Delegate Function GetTrackSendUIVolPanDel(track As IntPtr,
                                                      sendIndex As Integer,
                                                      ByRef volumeOut As Double,
                                                      ByRef panOut As Double) As Boolean
    Public Shared GetTrackSendUIVolPan As GetTrackSendUIVolPanDel
    ''' <summary>
    ''' Écrit le volume d'un envoi au niveau UI (post-automation, category=0 uniquement).
    ''' Pendant le lecture d'automation, enregistre si la piste est en Touch/Write/Latch.
    ''' vol    : 0..4  (1.0 = 0 dB)
    ''' isend  : 0=drag en cours  1=fin de drag/commit  -1=écriture directe sans undo
    ''' </summary>
    Public Delegate Function SetTrackSendUIVolDel(track As IntPtr,
                                                   sendIndex As Integer,
                                                   vol As Double,
                                                   isend As Integer) As Boolean
    Public Shared SetTrackSendUIVol As SetTrackSendUIVolDel


    ''' <summary>
    ''' Écrit le pan d'une piste au niveau UI.
    ''' Pendant la lecture d'automation, enregistre si la piste est en Touch/Write/Latch.
    ''' pan   : -1..1  (-1=Gauche  0=Centre  1=Droite)
    ''' isend : 0=drag en cours  1=fin de drag/commit  -1=écriture directe sans undo
    ''' </summary>
    Public Delegate Function SetTrackUIPanDel(track As IntPtr,
                                               pan As Double,
                                               isend As Integer) As Boolean
    Public Shared SetTrackUIPan As SetTrackUIPanDel

    ''' <summary>
    ''' Retourne l'état mute POST-automation d'une piste (valeur affichée dans l'UI Reaper).
    ''' Équivalent de <see cref="GetTrackUIVolPan"/> mais pour le mute.
    ''' muteOut : True=muet  False=actif
    ''' Retourne False si l'index est hors limites.
    ''' </summary>
    Public Delegate Function GetTrackUIMuteDel(track As IntPtr,
                                                ByRef muteOut As Boolean) As Boolean
    Public Shared GetTrackUIMute As GetTrackUIMuteDel
    ''' <summary>
    ''' Écrit l'état mute d'une piste au niveau UI.
    ''' Pendant la lecture d'automation, enregistre si la piste est en Touch/Write/Latch.
    ''' isend : 0=drag en cours  1=fin de drag/commit  -1=écriture directe sans undo
    ''' </summary>
    Public Delegate Function SetTrackUIMuteDel(track As IntPtr,
                                                mute As Boolean,
                                                isend As Integer) As Boolean
    Public Shared SetTrackUIMute As SetTrackUIMuteDel
    ''' <summary>
    ''' Retourne l'état mute POST-automation d'un envoi (category=0 uniquement).
    ''' Équivalent de <see cref="GetTrackUIMute"/> mais pour les envois.
    ''' muteOut : True=muet  False=actif
    ''' Retourne False si l'index est hors limites.
    ''' </summary>
    Public Delegate Function GetTrackSendUIMuteDel(track As IntPtr,
                                                    sendIndex As Integer,
                                                    ByRef muteOut As Boolean) As Boolean
    Public Shared GetTrackSendUIMute As GetTrackSendUIMuteDel

    ''' <summary>
    ''' Bascule l'état mute d'un envoi au niveau UI en un seul appel atomique.
    ''' Préférer cette fonction à Get+Set pour éviter la race condition.
    ''' isend : 0=drag en cours  1=fin de drag/commit  -1=écriture directe sans undo
    ''' Retourne True si la valeur a été appliquée.
    ''' </summary>
    Public Delegate Function ToggleTrackSendUIMuteDel(track As IntPtr,
                                                       sendIndex As Integer,
                                                       isend As Integer) As Boolean
    Public Shared ToggleTrackSendUIMute As ToggleTrackSendUIMuteDel


    ''' <summary>
    ''' Écrit le volume d'une piste au niveau UI (post-automation, surface-safe).
    ''' Pendant la lecture d'automation, enregistre si la piste est en Touch/Write/Latch.
    ''' vol   : 0..4  (1.0 = 0 dB)
    ''' isend : 0=drag en cours  1=fin de drag/commit  -1=écriture directe sans undo
    ''' </summary>
    Public Delegate Function SetTrackUIVolumeDel(track As IntPtr,
                                                  vol As Double,
                                                  isend As Integer) As Boolean
    Public Shared SetTrackUIVolume As SetTrackUIVolumeDel


    ' ═══════════════════════════════════════════════════════════════
    '  UNDO / UI
    ' ═══════════════════════════════════════════════════════════════

    ''' <summary>Ouvre un bloc d'annulation.</summary>
    Public Delegate Sub Undo_BeginBlockDel()
    Public Shared Undo_BeginBlock As Undo_BeginBlockDel

    ''' <summary>Ferme un bloc d'annulation (extraflags : 1=tracks 4=items 8=fx).</summary>
    Public Delegate Sub Undo_EndBlockDel(<MarshalAs(UnmanagedType.LPStr)> descchange As String, extraflags As Integer)
    Public Shared Undo_EndBlock As Undo_EndBlockDel

    ''' <summary>Description de la prochaine annulation possible (Nothing si pile vide).</summary>
    Public Delegate Function Undo_CanUndo2Del(proj As IntPtr) As IntPtr
    Public Shared Undo_CanUndo2 As Undo_CanUndo2Del

    ''' <summary>Description du prochain refaire possible.</summary>
    Public Delegate Function Undo_CanRedo2Del(proj As IntPtr) As IntPtr
    Public Shared Undo_CanRedo2 As Undo_CanRedo2Del

    ''' <summary>Rafraîchit la timeline (arrangement).</summary>
    Public Delegate Sub UpdateTimelineDel()
    Public Shared UpdateTimeline As UpdateTimelineDel

    ''' <summary>Rafraîchit la vue d'arrangement.</summary>
    Public Delegate Sub UpdateArrangeDel()
    Public Shared UpdateArrange As UpdateArrangeDel

    ''' <summary>Rafraîchit toutes les surfaces externes.</summary>
    Public Delegate Sub TrackList_AdjustWindowsDel(isMinor As Boolean)
    Public Shared TrackList_AdjustWindows As TrackList_AdjustWindowsDel

    ''' <summary>HWND de la fenêtre principale Reaper.</summary>
    Public Delegate Function GetMainHwndDel() As IntPtr
    Public Shared GetMainHwnd As GetMainHwndDel

    ' ══════════════════════════════════════════════════════════════
    '  BOÎTE DE DIALOGUE
    ' ══════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Affiche une boîte de dialogue modale dans Reaper.
    ''' <list type="table">
    ''' <item><term>type 0</term><description>OK</description></item>
    ''' <item><term>type 1</term><description>OK / Annuler</description></item>
    ''' <item><term>type 2</term><description>Abandonner / Réessayer / Ignorer</description></item>
    ''' <item><term>type 3</term><description>Oui / Non / Annuler</description></item>
    ''' <item><term>type 4</term><description>Oui / Non</description></item>
    ''' <item><term>type 5</term><description>Réessayer / Annuler</description></item>
    ''' </list>
    ''' Valeurs de retour :
    ''' 1=OK  2=Annuler  3=Abandonner  4=Réessayer  5=Ignorer  6=Oui  7=Non
    ''' </summary>
    Public Delegate Function ShowMessageBoxDel(
        <MarshalAs(UnmanagedType.LPStr)> msg As String,
        <MarshalAs(UnmanagedType.LPStr)> title As String,
        type As Integer) As Integer
    Public Shared ShowMessageBox As ShowMessageBoxDel

    ' ═══════════════════════════════════════════════════════════════
    '  ACTIONS / ENREGISTREMENT
    ' ═══════════════════════════════════════════════════════════════

    Public Delegate Sub Main_OnCommandDel(command As Integer, flag As Integer)
    Public Shared Main_OnCommand As Main_OnCommandDel

    Public Delegate Sub ShowConsoleMsgDel(<MarshalAs(UnmanagedType.LPStr)> msg As String)
    Public Shared ShowConsoleMsg As ShowConsoleMsgDel

    Public Delegate Function NamedCommandLookupDel(
        <MarshalAs(UnmanagedType.LPStr)> command_name As String) As Integer
    Public Shared NamedCommandLookup As NamedCommandLookupDel

    Public Delegate Function GetUserInputsDel(
        title As IntPtr,
        numInputs As Integer,
        captions As IntPtr,
        retvals As IntPtr,
        retlen As Integer) As Boolean
    Public Shared GetUserInputs As GetUserInputsDel

    Public Delegate Function PluginRegisterDel(
        <MarshalAs(UnmanagedType.LPStr)> name As String,
        infostruct As IntPtr) As Integer
    Public Shared Plugin_Register As PluginRegisterDel

    Public Delegate Function HookCommandDel(cmd As Integer, flag As Integer) As Boolean

    ' ══════════════════════════════════════════════════════════════
    '  TEMPS / TEMPO / SIGNATURE (batch 2)
    ' ══════════════════════════════════════════════════════════════

    ''' <summary>Durée totale du projet en secondes.</summary>
    Public Delegate Function GetProjectLengthDel(proj As IntPtr) As Double
    Public Shared GetProjectLength As GetProjectLengthDel

    ''' <summary>Convertit secondes → mesures/temps (mesures, cml, beatpos, denom par ByRef).</summary>
    Public Delegate Function TimeMap2_timeToBeatsDel(
        proj As IntPtr, tpos As Double,
        ByRef measuresOut As Integer, ByRef cmlOut As Integer,
        ByRef fullbeatsOut As Double, ByRef cdenomOut As Integer) As Double
    Public Shared TimeMap2_timeToBeats As TimeMap2_timeToBeatsDel

    ''' <summary>Convertit mesures/temps → secondes.</summary>
    Public Delegate Function TimeMap2_beatsToTimeDel(
        proj As IntPtr, tpos As Double, ByRef measuresIn As Integer) As Double
    Public Shared TimeMap2_beatsToTime As TimeMap2_beatsToTimeDel

    ''' <summary>Signature et tempo à un instant (num/denom/bpm par ByRef).</summary>
    Public Delegate Sub TimeMap_GetTimeSigAtTimeDel(
        proj As IntPtr, time As Double,
        ByRef timesig_num As Integer,
        ByRef timesig_denom As Integer,
        ByRef tempo As Double)
    Public Shared TimeMap_GetTimeSigAtTime As TimeMap_GetTimeSigAtTimeDel

    ''' <summary>Nombre de marqueurs tempo/signature.</summary>
    Public Delegate Function CountTempoTimeSigMarkersDel(proj As IntPtr) As Integer
    Public Shared CountTempoTimeSigMarkers As CountTempoTimeSigMarkersDel

    ''' <summary>Marqueur tempo/signature par index (tous paramètres par ByRef).</summary>
    Public Delegate Function GetTempoTimeSigMarkerDel(
        proj As IntPtr, ptidx As Integer,
        ByRef timeposOut As Double, ByRef measureposOut As Integer,
        ByRef beatposOut As Double, ByRef bpmOut As Double,
        ByRef timesig_numOut As Integer, ByRef timesig_denomOut As Integer,
        ByRef lineartempo As Boolean) As Boolean
    Public Shared GetTempoTimeSigMarker As GetTempoTimeSigMarkerDel

    ''' <summary>Formate une position en string "H:M:S" ou "M.B.T" dans buf.</summary>
    Public Delegate Sub format_timeposDel(tpos As Double, buf As IntPtr, buflen As Integer)
    Public Shared format_timepos As format_timeposDel

    ' ══════════════════════════════════════════════════════════════
    '  SÉLECTION TEMPORELLE / BOUCLE
    ' ══════════════════════════════════════════════════════════════

    ''' <summary>Plage de sélection/boucle (start/end en secondes par ByRef).</summary>
    Public Delegate Sub GetLoopTimeRange2Del(
        proj As IntPtr, isLoop As Boolean, allowautoseek As Boolean,
        ByRef startOut As Double, ByRef endOut As Double)
    Public Shared GetLoopTimeRange2 As GetLoopTimeRange2Del

    ''' <summary>Définit la plage de sélection/boucle.</summary>
    Public Delegate Sub SetLoopTimeRange2Del(
        proj As IntPtr, isLoop As Boolean, allowautoseek As Boolean,
        startIn As Double, endIn As Double, undoBeginBlock As Boolean)
    Public Shared SetLoopTimeRange2 As SetLoopTimeRange2Del

    ' ══════════════════════════════════════════════════════════════
    '  VOLUME / NIVEAU / PEAK
    ' ══════════════════════════════════════════════════════════════

    ''' <summary>Peak actuel d'un canal de piste (0=L 1=R…).</summary>
    Public Delegate Function Track_GetPeakInfoDel(track As IntPtr, channel As Integer) As Double
    Public Shared Track_GetPeakInfo As Track_GetPeakInfoDel

    ''' <summary>Peak hold en dB (clear=True pour réinitialiser).</summary>
    Public Delegate Function Track_GetPeakHoldDBDel(track As IntPtr, channel As Integer, clear As Boolean) As Double
    Public Shared Track_GetPeakHoldDB As Track_GetPeakHoldDBDel

    ''' <summary>Convertit dB → valeur de slider Reaper [0..4].</summary>
    Public Delegate Function DB2SLIDERDel(x As Double) As Double
    Public Shared DB2SLIDER As DB2SLIDERDel

    ''' <summary>Convertit valeur slider [0..4] → dB.</summary>
    Public Delegate Function SLIDER2DBDel(y As Double) As Double
    Public Shared SLIDER2DB As SLIDER2DBDel

    ' ══════════════════════════════════════════════════════════════
    '  COULEURS
    ' ══════════════════════════════════════════════════════════════

    ''' <summary>Couleur native de la piste (0 = par défaut).</summary>
    Public Delegate Function GetTrackColorDel(track As IntPtr) As Integer
    Public Shared GetTrackColor As GetTrackColorDel

    ''' <summary>Définit la couleur native d'une piste.</summary>
    Public Delegate Sub SetTrackColorDel(track As IntPtr, color As Integer)
    Public Shared SetTrackColor As SetTrackColorDel

    ''' <summary>Convertit R,G,B [0-255] → couleur native Reaper (OS-specific).</summary>
    Public Delegate Function ColorToNativeDel(r As Integer, g As Integer, b As Integer) As Integer
    Public Shared ColorToNative As ColorToNativeDel

    ''' <summary>Convertit couleur native → R,G,B [0-255] par ByRef.</summary>
    Public Delegate Sub NativeToColorDel(col As Integer, ByRef r As Integer, ByRef g As Integer, ByRef b As Integer)
    Public Shared NativeToColor As NativeToColorDel

    ' ══════════════════════════════════════════════════════════════
    '  TAKES / MEDIA
    ' ══════════════════════════════════════════════════════════════

    ''' <summary>Nombre de takes d'un item.</summary>
    Public Delegate Function CountTakesDel(item As IntPtr) As Integer
    Public Shared CountTakes As CountTakesDel

    ''' <summary>Take d'un item par index.</summary>
    Public Delegate Function GetMediaItemTakeDel(item As IntPtr, tk As Integer) As IntPtr
    Public Shared GetMediaItemTake As GetMediaItemTakeDel

    ''' <summary>Nom du take (retourne IntPtr → Marshal.PtrToStringAnsi).</summary>
    Public Delegate Function GetTakeNameDel(take As IntPtr) As IntPtr
    Public Shared GetTakeName As GetTakeNameDel

    ''' <summary>Définit le take actif d'un item.</summary>
    Public Delegate Sub SetActiveTakeDel(take As IntPtr)
    Public Shared SetActiveTake As SetActiveTakeDel

    ''' <summary>Source média d'un take.</summary>
    Public Delegate Function GetMediaItemTake_SourceDel(take As IntPtr) As IntPtr
    Public Shared GetMediaItemTake_Source As GetMediaItemTake_SourceDel

    ''' <summary>Type de source dans buf ("WAV","MIDI","VIDEO"…).</summary>
    Public Delegate Function GetMediaSourceTypeDel(source As IntPtr, typebuf As IntPtr, typebuf_sz As Integer) As Boolean
    Public Shared GetMediaSourceType As GetMediaSourceTypeDel

    ''' <summary>Durée de la source (lengthIsQN=True si la durée est en QN).</summary>
    Public Delegate Function GetMediaSourceLengthDel(source As IntPtr, ByRef lengthIsQN As Boolean) As Double
    Public Shared GetMediaSourceLength As GetMediaSourceLengthDel

    ' ══════════════════════════════════════════════════════════════
    '  FX SUPPLÉMENTAIRES
    ' ══════════════════════════════════════════════════════════════

    ''' <summary>Preset actuel d'un FX dans buf.</summary>
    Public Delegate Function TrackFX_GetPresetDel(track As IntPtr, fx As Integer, presname As IntPtr, presname_sz As Integer) As Boolean
    Public Shared TrackFX_GetPreset As TrackFX_GetPresetDel

    ''' <summary>Applique un preset par nom.</summary>
    Public Delegate Function TrackFX_SetPresetDel(
        track As IntPtr, fx As Integer,
        <MarshalAs(UnmanagedType.LPStr)> presetname As String) As Boolean
    Public Shared TrackFX_SetPreset As TrackFX_SetPresetDel

    ''' <summary>Navigue parmi les presets (presetmove: +1/-1/0).</summary>
    Public Delegate Function TrackFX_NavigatePresetsDel(track As IntPtr, fx As Integer, presetmove As Integer) As Boolean
    Public Shared TrackFX_NavigatePresets As TrackFX_NavigatePresetsDel

    ''' <summary>Supprime un FX de la piste.</summary>
    Public Delegate Function TrackFX_DeleteDel(track As IntPtr, fx As Integer) As Boolean
    Public Shared TrackFX_Delete As TrackFX_DeleteDel

    ''' <summary>Ajoute un FX par nom (instantiate: -1=dernier -1000=premier, retourne index ou -1).</summary>
    Public Delegate Function TrackFX_AddByNameDel(
        track As IntPtr,
        <MarshalAs(UnmanagedType.LPStr)> fxname As String,
        recFX As Boolean,
        instantiate As Integer) As Integer
    Public Shared TrackFX_AddByName As TrackFX_AddByNameDel
    ''' <summary>
    ''' Retourne le dernier paramètre de FX touché par l'utilisateur.
    ''' tracknumber : 0=master  1=première piste  etc. (1-based)
    ''' fxnumber    : index du FX (0-based)
    ''' paramnumber : index du paramètre (0-based)
    ''' Retourne False si aucun FX n'a encore été touché.
    ''' </summary>
    Public Delegate Function GetLastTouchedFXDel(
        ByRef tracknumber As Integer,
        ByRef fxnumber As Integer,
        ByRef paramnumber As Integer) As Boolean
    Public Shared GetLastTouchedFX As GetLastTouchedFXDel


    ''' <summary>
    ''' Copie ou déplace un FX d'une piste vers une autre.
    ''' destFxIndex : position cible (-1 = ajouter à la fin).
    ''' move        : True = déplacer (supprime de la source), False = copier.
    ''' </summary>
    Public Delegate Sub TrackFX_CopyToTrackDel(
        src As IntPtr, src_fx As Integer,
        dest As IntPtr, dest_fx_index As Integer,
        move As Boolean)
    Public Shared TrackFX_CopyToTrack As TrackFX_CopyToTrackDel
    ' ══════════════════════════════════════════════════════════════════════════
    '  FX INSTALLÉS
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Énumère les FX installés dans Reaper.
    ''' Retourne False quand l'index dépasse la liste.
    ''' nameOut  : nom affiché du plugin ex: "FabFilter Pro-Q 3 (FabFilter)".
    ''' identOut : identifiant complet ex: "VST3: FabFilter Pro-Q 3 (FabFilter)".
    '''            Correspond à la valeur à passer à TrackFX_AddByName.
    ''' </summary>
    Public Delegate Function EnumInstalledFXDel(
        index As Integer,
        ByRef nameOut As IntPtr,
        ByRef identOut As IntPtr) As Boolean
    Public Shared EnumInstalledFX As EnumInstalledFXDel
    ' ══════════════════════════════════════════════════════════════════════════
    '  GROUPES DE PISTE
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>
    ''' Lit ou modifie l'appartenance à un groupe de piste.
    ''' groupname : "VOLUME_LEAD" "VOLUME_FOLLOW" "PAN_LEAD" "MUTE_LEAD" etc.
    ''' setmask   : masque des bits à modifier (0 = lecture seule)
    ''' setval    : valeur des bits couverts par setmask
    ''' Retourne  : masque courant (bits 0-31 → groupes 1-32)
    ''' </summary>
    Public Delegate Function GetSetTrackGroupMembershipExDel(
        tr As IntPtr,
        <MarshalAs(UnmanagedType.LPStr)> groupname As String,
        setmask As UInteger,
        setval As UInteger) As UInteger
    Public Shared GetSetTrackGroupMembershipEx As GetSetTrackGroupMembershipExDel
    ' ══════════════════════════════════════════════════════════════
    '  ENVELOPPES / AUTOMATION
    ' ══════════════════════════════════════════════════════════════

    ''' <summary>Nombre d'enveloppes d'une piste.</summary>
    Public Delegate Function CountTrackEnvelopesDel(track As IntPtr) As Integer
    Public Shared CountTrackEnvelopes As CountTrackEnvelopesDel

    ''' <summary>Enveloppe d'une piste par index.</summary>
    Public Delegate Function GetTrackEnvelopeDel(track As IntPtr, envelopeidx As Integer) As IntPtr
    Public Shared GetTrackEnvelope As GetTrackEnvelopeDel

    ''' <summary>Enveloppe d'un paramètre FX (create=True pour la créer si absente).</summary>
    Public Delegate Function GetFXEnvelopeDel(track As IntPtr, fxindex As Integer, parameterindex As Integer, create As Boolean) As IntPtr
    Public Shared GetFXEnvelope As GetFXEnvelopeDel

    ''' <summary>
    ''' Copie le nom affiché d'une enveloppe dans <paramref name="buf"/>.
    ''' Retourne True si le nom a été copié avec succès.
    ''' env : TrackEnvelope* obtenu via <see cref="GetTrackEnvelope"/>.
    ''' </summary>
    Public Delegate Function GetEnvelopeNameDel(env As IntPtr,
                                                 buf As System.Text.StringBuilder,
                                                 buflen As Integer) As Boolean
    Public Shared GetEnvelopeName As GetEnvelopeNameDel

    ''' <summary>Nombre de points dans une enveloppe.</summary>
    Public Delegate Function CountEnvelopePointsDel(envelope As IntPtr) As Integer
    Public Shared CountEnvelopePoints As CountEnvelopePointsDel

    ''' <summary>Lit un point d'enveloppe par index (tous paramètres par ByRef).</summary>
    Public Delegate Function GetEnvelopePointDel(
        envelope As IntPtr, ptidx As Integer,
        ByRef timeOut As Double, ByRef valueOut As Double,
        ByRef shapeOut As Integer, ByRef tensionOut As Double,
        ByRef selectedOut As Boolean) As Boolean
    Public Shared GetEnvelopePoint As GetEnvelopePointDel

    ''' <summary>
    ''' Insère un point dans une enveloppe.
    ''' ⚠️  <paramref name="noSortIn"/> est un <c>bool*</c> dans l'API native (paramètre optionnel).
    '''      Passer <see cref="IntPtr.Zero"/> = tri normal après l'insertion.
    '''      Passer un pointeur vers <c>True</c> = pas de tri (mode batch — appeler
    '''      <see cref="Envelope_SortPoints"/> manuellement après le batch).
    '''      Ne jamais passer True/False directement : Reaper interprèterait 0 ou 1
    '''      comme une adresse mémoire et crasherait immédiatement.
    ''' </summary>
    Public Delegate Function InsertEnvelopePointDel(
            envelope As IntPtr,
            time As Double,
            value As Double,
            shape As Integer,
            tension As Double,
            selected As Boolean,
            noSortIn As IntPtr) As Boolean
    Public Shared InsertEnvelopePoint As InsertEnvelopePointDel

    ''' <summary>Supprime les points dans un intervalle de temps.</summary>
    Public Delegate Function DeleteEnvelopePointRangeDel(
        envelope As IntPtr, starttime As Double, endtime As Double) As Boolean
    Public Shared DeleteEnvelopePointRange As DeleteEnvelopePointRangeDel

    ''' <summary>Trie les points d'enveloppe après modification.</summary>
    Public Delegate Function Envelope_SortPointsDel(envelope As IntPtr) As Boolean
    Public Shared Envelope_SortPoints As Envelope_SortPointsDel

    ''' <summary>
    ''' Évalue une enveloppe à une position temporelle.
    ''' valueOut    : valeur interpolée à <c>time</c>.
    ''' dVdSOut     : dérivée première (vitesse de changement).
    ''' ddVdSOut    : dérivée seconde.
    ''' dddVdSOut   : dérivée troisième.
    ''' Passer samplerate=0 et samplesRequested=0 si seule valueOut est nécessaire.
    ''' </summary>
    Public Delegate Function Envelope_EvaluateDel(envelope As IntPtr,
                                                   time As Double,
                                                   samplerate As Double,
                                                   samplesRequested As Integer,
                                                   ByRef valueOut As Double,
                                                   ByRef dVdSOut As Double,
                                                   ByRef ddVdSOut As Double,
                                                   ByRef dddVdSOut As Double) As Integer
    Public Shared Envelope_Evaluate As Envelope_EvaluateDel

    ''' <summary>
    ''' Lit ou écrit une propriété d'enveloppe par nom.
    ''' Propriétés disponibles :
    ''' ACTIVE, VISIBLE, ARMED, IN_LANE (bool 0/1),
    ''' LANEHEIGHT, DEFSHAPE, AUTO_ITEMS_OPTIONS (int),
    ''' MIN_VALUE, MAX_VALUE, CENTER_VALUE, FADER_SCALING (lecture seule).
    ''' setNewValue = False → lecture  True → écriture.
    ''' Retourne la valeur courante (lecture) ou la nouvelle valeur (écriture).
    ''' </summary>
    Public Delegate Function GetSetEnvelopeInfo_ValueDel(
        env As IntPtr,
        <MarshalAs(UnmanagedType.LPStr)> parmname As String,
        newvalue As Double,
        setNewValue As Boolean) As Double
    Public Shared GetSetEnvelopeInfo_Value As GetSetEnvelopeInfo_ValueDel

    ''' <summary>
    ''' Lit ou écrit une propriété de type chaîne d'une enveloppe.
    ''' Paramètres courants :
    ''' "GUID"      → {XXXXXXXX-…} identifiant stable de l'enveloppe (lecture seule).
    ''' "P_EXT:xxx" → état d'extension personnalisé clé/valeur (lecture/écriture).
    ''' stringNeedBig : buffer pré-alloué pour la lecture, ou chaîne source pour l'écriture.
    ''' setNewValue   : False=lire  True=écrire.
    ''' Retourne True si la propriété a été traitée avec succès.
    ''' </summary>
    Public Delegate Function GetSetEnvelopeInfo_StringDel(
            env As IntPtr,
            <MarshalAs(UnmanagedType.LPStr)> parmname As String,
            stringNeedBig As IntPtr,
            stringNeedBig_sz As Integer,
            setNewValue As Boolean) As Boolean
    Public Shared GetSetEnvelopeInfo_String As GetSetEnvelopeInfo_StringDel

    ''' <summary>
    ''' Lit une propriété numérique d'une enveloppe (lecture seule).
    ''' Alternative simplifiée à <see cref="GetSetEnvelopeInfo_Value"/> —
    ''' ne prend pas de valeur en entrée ni de flag setNewValue.
    ''' Paramètres courants :
    ''' "ACTIVE" "VISIBLE" "ARMED" "IN_LANE" "LANEHEIGHT" "DEFSHAPE"
    ''' "MINVALUE" "MAXVALUE" "CENTERVALUE" "FADERSCALING"
    ''' "AUTO_ITEMS_OPTIONS" "I_SEND_IDX".
    ''' </summary>
    Public Delegate Function GetEnvelopeInfo_ValueDel(
            env As IntPtr,
            <MarshalAs(UnmanagedType.LPStr)> parmname As String) As Double
    Public Shared GetEnvelopeInfo_Value As GetEnvelopeInfo_ValueDel

    ' ── Enveloppe — State Chunk ───────────────────────────────────────────────

    ''' <summary>
    ''' Sérialise l'état complet d'une enveloppe (format .rpp) dans un buffer.
    ''' strNeedBig  : buffer pré-alloué par l'appelant (taille typique : 65 536).
    ''' isundopoint : True pour créer un point d'annulation Reaper.
    ''' Retourne True si réussi.
    ''' Analogue de <see cref="GetTrackStateChunkDel"/> pour les enveloppes.
    ''' </summary>
    Public Delegate Function GetEnvelopeStateChunkDel(
            env As IntPtr,
            strNeedBig As IntPtr,
            strNeedBig_sz As Integer,
            isundopoint As Boolean) As Boolean
    Public Shared GetEnvelopeStateChunk As GetEnvelopeStateChunkDel

    ''' <summary>
    ''' Restaure l'état d'une enveloppe depuis un state chunk .rpp.
    ''' isundopoint : True pour créer un point d'annulation Reaper.
    ''' Retourne True si réussi.
    ''' Analogue de <see cref="SetTrackStateChunkDel"/> pour les enveloppes.
    ''' </summary>
    Public Delegate Function SetEnvelopeStateChunkDel(
            env As IntPtr,
            <MarshalAs(UnmanagedType.LPStr)> str As String,
            isundopoint As Boolean) As Boolean
    Public Shared SetEnvelopeStateChunk As SetEnvelopeStateChunkDel

    ' ══════════════════════════════════════════════════════════════
    '  MIDI EDITOR
    ' ══════════════════════════════════════════════════════════════

    ''' <summary>Éditeur MIDI actif (nullptr si aucun).</summary>
    Public Delegate Function MIDIEditor_GetActiveDel() As IntPtr
    Public Shared MIDIEditor_GetActive As MIDIEditor_GetActiveDel

    ''' <summary>Take affiché dans l'éditeur MIDI.</summary>
    Public Delegate Function MIDIEditor_GetTakeDel(midieditor As IntPtr) As IntPtr
    Public Shared MIDIEditor_GetTake As MIDIEditor_GetTakeDel

    ''' <summary>Paramètre entier de l'éditeur MIDI ("zoom","snap_enabled"…).</summary>
    Public Delegate Function MIDIEditor_GetSetting_intDel(
        midieditor As IntPtr,
        <MarshalAs(UnmanagedType.LPStr)> setting_desc As String) As Integer
    Public Shared MIDIEditor_GetSetting_int As MIDIEditor_GetSetting_intDel

    ''' <summary>Modifie un paramètre entier de l'éditeur MIDI.</summary>
    Public Delegate Function MIDIEditor_SetSetting_intDel(
        midieditor As IntPtr,
        <MarshalAs(UnmanagedType.LPStr)> setting_desc As String,
        newvalue As Integer) As Boolean
    Public Shared MIDIEditor_SetSetting_int As MIDIEditor_SetSetting_intDel

    ' ══════════════════════════════════════════════════════════════
    '  STATE CHUNK
    ' ══════════════════════════════════════════════════════════════

    ''' <summary>Sérialise l'état d'une piste dans strNeedBig (IntPtr → allouer suffisamment).</summary>
    Public Delegate Function GetTrackStateChunkDel(
        track As IntPtr, strNeedBig As IntPtr,
        maxstrlen As Integer, isundopoint As Boolean) As Boolean
    Public Shared GetTrackStateChunk As GetTrackStateChunkDel

    ''' <summary>Restaure l'état d'une piste depuis une string.</summary>
    Public Delegate Function SetTrackStateChunkDel(
        track As IntPtr,
        <MarshalAs(UnmanagedType.LPStr)> str As String,
        isundopoint As Boolean) As Boolean
    Public Shared SetTrackStateChunk As SetTrackStateChunkDel

    ' ══════════════════════════════════════════════════════════════
    '  INFORMATIONS APPLICATION
    ' ══════════════════════════════════════════════════════════════

    ''' <summary>Version de Reaper (ex: "7.05/x64") — retourne IntPtr const char*.</summary>
    Public Delegate Function GetAppVersionDel() As IntPtr
    Public Shared GetAppVersion As GetAppVersionDel

    ''' <summary>Chemin du dossier de ressources Reaper — retourne IntPtr const char*.</summary>
    Public Delegate Function GetResourcePathDel() As IntPtr
    Public Shared GetResourcePath As GetResourcePathDel

    ''' <summary>Chemin de l'exécutable Reaper — retourne IntPtr const char*.</summary>
    Public Delegate Function GetExePathDel() As IntPtr
    Public Shared GetExePath As GetExePathDel


    ' ══════════════════════════════════════════════════════════════════════════
    '  CANAUX AUDIO
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>Nombre d'entrées audio disponibles sur l'interface.</summary>
    Public Delegate Function GetNumAudioInputsDel() As Integer
    Public Shared GetNumAudioInputs As GetNumAudioInputsDel

    ''' <summary>Nombre de sorties audio disponibles sur l'interface.</summary>
    Public Delegate Function GetNumAudioOutputsDel() As Integer
    Public Shared GetNumAudioOutputs As GetNumAudioOutputsDel

    ''' <summary>
    ''' Nom d'une entrée audio (pointeur statique — ne pas libérer).
    ''' channelIndex : 0-based.
    ''' </summary>
    Public Delegate Function GetInputChannelNameDel(channelIndex As Integer) As IntPtr
    Public Shared GetInputChannelName As GetInputChannelNameDel

    ''' <summary>
    ''' Nom d'une sortie audio (pointeur statique — ne pas libérer).
    ''' channelIndex : 0-based.
    ''' </summary>
    Public Delegate Function GetOutputChannelNameDel(channelIndex As Integer) As IntPtr
    Public Shared GetOutputChannelName As GetOutputChannelNameDel

    ' ══════════════════════════════════════════════════════════════════════════
    '  UNDO — compléments (BeginBlock/EndBlock/CanUndo2/CanRedo2 déjà déclarés)
    ' ══════════════════════════════════════════════════════════════════════════

    ''' <summary>Exécute un Undo sur le projet (Ctrl+Z).</summary>
    Public Delegate Sub Undo_DoUndo2Del(proj As IntPtr)
    Public Shared Undo_DoUndo2 As Undo_DoUndo2Del

    ''' <summary>Exécute un Redo sur le projet (Ctrl+Y).</summary>
    Public Delegate Sub Undo_DoRedo2Del(proj As IntPtr)
    Public Shared Undo_DoRedo2 As Undo_DoRedo2Del

    ' ── Complément de la section PROJET ──────────────────────────────────────

    ''' <summary>
    ''' Compteur d'état du projet — s'incrémente à chaque modification.
    ''' Idéal pour du polling : comparer avec la valeur précédente.
    ''' </summary>
    Public Delegate Function GetProjectStateChangeCountDel(proj As IntPtr) As Integer
    Public Shared GetProjectStateChangeCount As GetProjectStateChangeCountDel

    ''' <summary>Retourne 1 si le projet a des modifications non sauvegardées.</summary>
    Public Delegate Function IsProjectDirtyDel(proj As IntPtr) As Integer
    Public Shared IsProjectDirty As IsProjectDirtyDel

    ''' <summary>
    ''' Sauvegarde le projet.
    ''' saveAsIn = True → ouvre la boîte de dialogue "Enregistrer sous".
    ''' </summary>
    Public Delegate Sub Main_SaveProjectDel(proj As IntPtr, saveAsIn As Boolean)
    Public Shared Main_SaveProject As Main_SaveProjectDel

    ''' <summary>Active un projet ouvert dans Reaper (le met au premier plan).</summary>
    Public Delegate Sub SelectProjectInstanceDel(proj As IntPtr)
    Public Shared SelectProjectInstance As SelectProjectInstanceDel



End Class