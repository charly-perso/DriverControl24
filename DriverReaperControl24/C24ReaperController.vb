Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Security.Cryptography.X509Certificates
Imports System.Threading
Imports DriverReaperControl24.C24ReaperController
Imports DriverReaperControl24.Control24
Imports DriverReaperControl24.Control24.Commands
Imports DriverReaperControl24.Control24.Hardware
Imports DriverReaperControl24.Reaper
Imports DriverReaperControl24.Reaper.Managers
Imports DriverReaperControl24.Reaper.Monitors
Imports DriverReaperControl24.Reaper.PageManagers

Public Class C24ReaperController
    Dim PageSendsLetter As New Dictionary(Of Integer, String()) From {
        {1, {"A", "B", "C", "D", "E"}},
        {2, {"F", "G", "H", "I", "J"}}
    }

    'Dim Page2SendsLetter As String() = 
    Private _disposedValue As Boolean = False
    Private _ReaperSessionConfig As Reaper.ReaperSessionConfig
    Private WithEvents _C24_Desk As C24_Desk
    Private WithEvents _ReaperSession As Reaper.ReaperSession
    Private WithEvents _ReaperProj As Reaper.ReaperProject
    Private WithEvents _ReaperTracksManager As Reaper.PageManagers.ReaperTracksManager
    Private WithEvents _ReaperPeakLevelManager As Reaper.PageManagers.ReaperPeakLevelManager
    Private WithEvents _ReaperTransportManager As ReaperTransportManager
    Private WithEvents _ReaperFxParamMonitor As ReaperFxParamMonitor
    Private WithEvents _ReaperTracksSendManager As ReaperTracksSendManager
    Private WithEvents _ReaperTrackSendsManager As ReaperTrackSendsManager
    Private _ReaperProjectMonitor As Monitors.ReaperProjectMonitor
    Public FxParamValueChangeH As [Delegate]
    Private Flip_Mode As Boolean = False
    Private AutoSelect_Mode As Boolean = False
    Private TracksChunk As New Dictionary(Of String, String)
    Private PrePost_Mode As Boolean = True
    Private SendMute_Mode As Boolean = False
    Private SndXLetter As String = ""
    ' ── Auto Handlers Status ───────────────
    Private Event FaderVpotVolHandlers(C24trkVolEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackVolEnvelope), status As Boolean)
    Private _C24trkVolEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackVolEnvelope)
    Private IsFaderVpotVolHandlers As Boolean = False
    Private Event FaderVpotPanHandlers(C24trkPanEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackPanEnvelope), status As Boolean)
    Private _C24trkPanEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackPanEnvelope)
    Private IsFaderVpotPanHandlers As Boolean = False
    Private Event MuteBtnHandlers(C24trkMuteEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackMuteEnvelope), status As Boolean)
    Private _C24trkMuteEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackMuteEnvelope)
    Private IsMuteBtnHandlers As Boolean = False
    Private Event FaderVpotSendLevelHandlers(C24trkSendVolEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperSendVolEnvelope), status As Boolean)
    Private _C24trkSendVolEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperSendVolEnvelope)
    Private IsFaderVpotSendLevelHandlers As Boolean = False
    Private Event SendMuteBtnHandlers(C24trkSendMuteEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperSendMuteEnvelope), status As Boolean)
    Private _C24trkSendMuteEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperSendMuteEnvelope)
    Private IsSendMuteBtnHandlers As Boolean = False
    Private Event FaderVpotPluginParamHandlers(C24trkPluginParamEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperFxParamEnvelope), status As Boolean)
    Private _C24trkPluginParamEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperFxParamEnvelope)
    Private IsFaderVpotPluginParamHandlers As Boolean = False
    ' ── Auto Status ───────────────
    Private FaderAuto As Boolean = False
    Private PanAuto As Boolean = False
    Private MuteAuto As Boolean = False
    Private SendLevelAuto As Boolean = False
    Private SendMuteAuto As Boolean = False
    Private PluginAuto As Boolean = False

    ' ── Modifiers ───────────────
    Private Opt As Boolean = False
    Private Shift As Boolean = False
    Private Ctl As Boolean = False
    Private Ctrl As Boolean = False

    Private SoloSafeTracks As New Dictionary(Of Integer, Boolean)
    Private SoloTracks As New Dictionary(Of Integer, Boolean)
    Private Names As New Dictionary(Of Integer, String)
    Private ParamIndexToTrackIndex As New Dictionary(Of Integer, Integer)
    Private TrkIndexEqFxListManagers As New Dictionary(Of Integer, EqFxListManager)
    Private TrkIndexDynFxListManagers As New Dictionary(Of Integer, DynFxListManager)

    ' ── Stockage des delegates par fonctions ───────────────────────────────────
    Private _CommonHandlers As New List(Of StripHandlers)
    Private _PanSendStripHandlers As New List(Of PanSendStripHandlers)
    Private _PanSend_PanStripHandlers As New List(Of PanSend_PanStripHandlers)
    Private _PanSend_SendStripHandlers As New List(Of PanSend_SendStripHandlers)

    Private _FaderVolumeHandlers As New List(Of VolumeHandlers)
    Private _FaderSendVolumeHandlers As New List(Of SendVolumeHandlers)
    Private _FaderPanHandlers As New List(Of PanHandlers)
    Private _VpotVolumeHandlers As New List(Of VolumeHandlers)
    Private _VpotSendVolumeHandlers As New List(Of SendVolumeHandlers)
    Private _VpotPanHandlers As New List(Of PanHandlers)
    Private _MuteHandlers As New List(Of MutesHandlers)
    Private _SolosHandlers As New List(Of SolosHandlers)
    Private _InsertParamBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler = Nothing
    Private _SolosClearSafeHandlers As New List(Of SolosClearSafeHandlers)
    Private _BankHandlers As New List(Of BankHandlers)
    Private _ShowHandlers As New List(Of ShowHandlers)
    Private _PageHandlers As New List(Of PageHandlers)
    Private _AssignHandlers As New List(Of AssignHandlers)
    Private _TransportHandlers As New List(Of TransportHandlers)
    Private _InsertHandlers As New List(Of InsertHandlers)
    Private _FxParamHandlers As New List(Of FxParamHandlers)
    Private _FlipFxParamHandlers As New List(Of FxParamHandlers)
    Private _ModifiersHandlers As New List(Of ModifiersHandlers)
    Private _SendsStripHandlers As New List(Of SendsStripHandlers)
    Private _SendMuteHandlers As New List(Of SendMuteHandlers)
    Private _SendPrePostHandlers As New List(Of SendPrePostHandlers)
    Private _SendsLetterHandlers As New List(Of SendsLetterHandlers)
    Private _AutomationHandlers As New List(Of AutomationHandlers)
    Private _AutomationEnableHandlers As New List(Of AutomationEnableHandlers)
    Private _AutomationVolumeHandlers As New List(Of AutomationVolumeHandlers)
    Private _AutomationPanHandlers As New List(Of AutomationPanHandlers)
    Private _AutomationMuteHandlers As New List(Of AutomationMuteHandlers)
    Private _AutomationSendMuteHandlers As New List(Of AutomationSendMuteHandlers)
    Private _AutomationPluginHandlers As New List(Of AutomationPluginHandlers)
    Private _ReaperEnvArmedHandlers As New List(Of ReaperEnvArmedHandlers)
    Private _ReaperTrackSoloHandlers As New List(Of ReaperTrackSoloHandlers)
    Private _ReaperAutoModeHandlers As New List(Of ReaperAutoModeHandler)
    Private _HomeStripHandlers As New List(Of HomeStripHandler)
    Private _FlipHandlers As New List(Of FlipHandler)
    Private _AutomationSendLevelHandlers As New List(Of AutomationSendLevelHandlers)
    Private _AutoSelectModeHandlers As New List(Of AutoSelectModeHandler)

    Private _Solo_Mode As Solo_Mode = Solo_Mode.Solo
    Private _ActualC24ViewMode As C24ViewModes = C24ViewModes.Home
    Private _LastC24ViewMode As C24ViewModes = C24ViewModes.None
    Private Assign_Mode As Boolean = False

    Private ActualFxEdited As ReaperFX
    Private ActualSendIndex As Integer = -1
    ' ── Timer VU-mètres ───────────────────────────────────────────────────────
    Private _PeakTimer As System.Timers.Timer
    Private _peakTimerBusy As Integer = 0   ' 0=libre  1=occupé (Interlocked)
    Private _ActualPlayState As Integer = 0



    ' ── Timers de répétition transport ────────────────────────────────────────
    Private _fwdTimer As System.Timers.Timer
    Private _rwdTimer As System.Timers.Timer
    Dim r As Integer = 0

    Private _CounterModeHandlers As New List(Of CounterModeHandlers)
    Private InsertSelectedIndex As Integer? = Nothing
    ''' <summary>
    ''' Modes d'horloge cyclables dans l'ordre du bouton Counter Mode.
    ''' </summary>
    Private Shared ReadOnly CounterModesCycle As Commands.ClockModes() = {
        Commands.ClockModes.HourMinuteSecondMS,
        Commands.ClockModes.HourMinuteSecondFrames,
        Commands.ClockModes.FeetFrames,
        Commands.ClockModes.BarBeats
    }

#Region "Properties"

    ReadOnly Property C24_Desk As Control24.Hardware.C24_Desk
        Get
            Return _C24_Desk
        End Get
    End Property
    Property ActualC24ViewMode As C24ViewModes
        Get
            Return _ActualC24ViewMode
        End Get
        Set
            _LastC24ViewMode = _ActualC24ViewMode
            _ActualC24ViewMode = Value
        End Set
    End Property
    ReadOnly Property LastC24ViewMode As C24ViewModes
        Get
            Return _LastC24ViewMode
        End Get
    End Property
    ReadOnly Property ActualPlayState As Integer
        Get
            Return _ActualPlayState
        End Get
    End Property
#End Region
#Region "Handler Class"
    ''' <summary>
    ''' Regroupe tous les delegates d'un channel strip en mode PanSend.
    ''' Permet AddHandler/RemoveHandler symétriques.
    ''' </summary>
    Private Class StripHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip


        Public PanChangeH As [Delegate]
        Public VpotValueH As [Delegate]


        Public RecArmChangeH As [Delegate]
        Public RecArmBtnH As [Delegate]
        Public SelectedChangeH As [Delegate]
        Public SelectBtnH As [Delegate]

        Public AutoModeBtnH As [Delegate]
        Public SendPanBtnH As [Delegate]
        Public SwitchActiveBtnH As [Delegate]
        Public EqBtnH As [Delegate]
        Public DynBtnH As [Delegate]
        Public InsertBtnH As [Delegate]
    End Class
    Private Class SendsStripHandlers
        Public PrePostBtnH As [Delegate]
        Public SendMuteBtnH As [Delegate]
    End Class

    Private Class MutesHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip
        Public TrackMonitor As Reaper.Monitors.ReaperTrackMonitor
        Public TrackMuteEnvelope As ReaperTrackMuteEnvelope
        Public MuteBtnH As [Delegate]
        Public TrackMuteEnvelopeChangeH As [Delegate]
        Public ReaperMuteBtnChangeH As [Delegate]
    End Class
    Private Class SolosHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip

        Public SoloBtnH As [Delegate]


    End Class
    Private Class VolumeHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip
        Public TrackMonitor As Reaper.Monitors.ReaperTrackMonitor
        Public TrackVolEnvelope As Reaper.ReaperTrackVolEnvelope

        Public VolChangeH As [Delegate]
        Public FaderHumanH As [Delegate]
        Public VpotValueChangeH As [Delegate]
        Public ReaperVolChangeH As [Delegate]
        Public TrackVolEnvelopeVolChangeH As [Delegate]

    End Class
    Private Class SendVolumeHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip
        Public SendMonitor As Reaper.Monitors.ReaperSendMonitor
        Public SendsMonitor As Reaper.Monitors.ReaperTrackSendsMonitor
        Public SendVolEnvelope As Reaper.ReaperSendVolEnvelope

        Public SendVolChangeH As [Delegate]
        Public FaderHumanH As [Delegate]
        Public VpotValueChangeH As [Delegate]
        Public ReaperSendVolChangeH As [Delegate]
        Public SendVolEnvelopeVolChangeH As [Delegate]

    End Class
    Private Class SendMuteHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip
        Public SendMonitor As Reaper.Monitors.ReaperSendMonitor
        Public SendsMonitor As Reaper.Monitors.ReaperTrackSendsMonitor
        Public SendMuteEnvelope As Reaper.ReaperSendMuteEnvelope

        Public SendMuteChangeH As [Delegate]
        Public ReaperSendMuteChangeH As [Delegate]
        Public SendMuteEnvelopeMuteChangeH As [Delegate]

    End Class
    Private Class SendPrePostHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip
        Public SendMonitor As Reaper.Monitors.ReaperSendMonitor

        Public SendPrePostChangeH As [Delegate]
        Public ReaperSendPrePostChangeH As [Delegate]


    End Class
    Private Class PanHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip
        Public TrackMonitor As Reaper.Monitors.ReaperTrackMonitor
        Public TrackPanEnvelope As Reaper.ReaperTrackPanEnvelope

        Public PanChangeH As [Delegate]
        Public FaderHumanH As [Delegate]
        Public VpotValueChangeH As [Delegate]
        Public ReaperPanChangeH As [Delegate]
        Public TrackPanEnvelopeVolChangeH As [Delegate]

    End Class

    Private Class SolosClearSafeHandlers

        Public SoloClearBtnH As [Delegate]
        Public SoloSafeBtnH As [Delegate]
    End Class
    Private Class BankHandlers

        Public BankLeftBtnH As [Delegate]
        Public BankRightBtnH As [Delegate]
        Public BankNudgeBtnH As [Delegate]

    End Class
    Private Class ShowHandlers
        Public cs0 As Hardware.C24_ChannelStrip
        Public cs1 As Hardware.C24_ChannelStrip

        Public Vel_Sens_EncodersBtnH As [Delegate]
        Public Show_ValuesBtnH As [Delegate]
        Public ScribbleScriptBackToOriginal0H As [Delegate]
        Public ScribbleScriptBackToOriginal1H As [Delegate]

        Public Show_NamesBtnH As [Delegate]
    End Class
    Private Class PageHandlers

        Public PageLeftBtnH As [Delegate]
        Public PageRightBtnH As [Delegate]
    End Class
    Private Class AssignHandlers

        Public AssignBtnH As [Delegate]
        Public EscBtnH As [Delegate]
    End Class

    Private Class PanSendStripHandlers
        Public ReaperSend As Reaper.ReaperSend

        Public C24_trk_SndX As Hardware.C24_ChannelStrip
        Public C24_trk_SndName As Hardware.C24_ChannelStrip
        Public C24_trk_SndPre As Hardware.C24_ChannelStrip
        Public C24_trk_LR As Hardware.C24_ChannelStrip

        Public switchActiveBtnH_PanEnter As [Delegate]
        Public switchActiveBtnH_SendXEnter As [Delegate]
        Public switchActiveBtnH_SendPrePost As [Delegate]
    End Class
    Private Class PanSend_PanStripHandlers


        Public C24_trk_SndX As Hardware.C24_ChannelStrip
        Public C24_trk_SndName As Hardware.C24_ChannelStrip
        Public C24_trk_SndPre As Hardware.C24_ChannelStrip
        Public C24_trk_LR As Hardware.C24_ChannelStrip

        Public switchActiveBtnH_PanEnter As [Delegate]
        Public switchActiveBtnH_SendXEnter As [Delegate]
        Public switchActiveBtnH_SendPrePost As [Delegate]
    End Class
    Private Class PanSend_SendStripHandlers


        Public C24_trk_SndX As Hardware.C24_ChannelStrip
        Public C24_trk_SndName As Hardware.C24_ChannelStrip
        Public C24_trk_SndPre As Hardware.C24_ChannelStrip
        Public C24_trk_LR As Hardware.C24_ChannelStrip

        Public switchActiveBtnH_PanEnter As [Delegate]
        Public switchActiveBtnH_SendXEnter As [Delegate]
        Public switchActiveBtnH_SendPrePost As [Delegate]
    End Class
    Private Class TransportHandlers
        Public PlayPositionH As [Delegate]
        Public PlayStateChangeH As [Delegate]
        Public PlayBtnH As [Delegate]
        Public StopBtnH As [Delegate]
        Public RecBtnH As [Delegate]
        Public FwdBtnH As [Delegate]
        Public RwdBtnH As [Delegate]
        Public FwdBtnReleasedH As [Delegate]
        Public RwdBtnPressedH As [Delegate]
    End Class
    Private Class CounterModeHandlers
        Public CounterModeBtnH As [Delegate]
    End Class
    Public Class InsertHandlers

        Public C24_trk_FX_Name1 As C24_ChannelStrip
        Public C24_trk_FX_Name2 As C24_ChannelStrip


        Public SelectBtnH As [Delegate]
        Public BypassBtnH As [Delegate]
        Public BypMasterBtnH As [Delegate]
        Public VpotSelectItemH As [Delegate]
        Public InsertParamBtnH As [Delegate]
    End Class
    Public Class FxParamHandlers
        Public ReaperFxParam As Reaper.ReaperFXParam
        Public C24Trk As Hardware.C24_ChannelStrip
        Public SelectBtnH As [Delegate]
        Public BypMasterBtnH As [Delegate]
        Public VpotValueChangeH As [Delegate]
        Public ParamMonitorChangeH As [Delegate]
        Public FaderValueChangeH As [Delegate]
    End Class
    Public Class ModifiersHandlers
        Public OptPressBtnH As [Delegate]
        Public CtlPressBtnH As [Delegate]
        Public CtrlPressBtnH As [Delegate]
        Public ShiftPressBtnH As [Delegate]
        Public OptRealBtnH As [Delegate]
        Public CtlRealBtnH As [Delegate]
        Public CtrlRealBtnH As [Delegate]
        Public ShiftRealBtnH As [Delegate]
    End Class

    Public Class SendsLetterHandlers
        Public A_FPressBtnH As [Delegate]
        Public B_GPressBtnH As [Delegate]
        Public C_HPressBtnH As [Delegate]
        Public D_IPressBtnH As [Delegate]
        Public E_JRealBtnH As [Delegate]
    End Class
    Public Class AutomationHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip

    End Class
    Public Class AutomationEnableHandlers
        Public Auto_FaderBtnH As [Delegate]
        Public Auto_PanBtnH As [Delegate]
        Public Auto_MuteBtnH As [Delegate]
        Public Auto_Send_LevelBtnH As [Delegate]
        Public Auto_Send_MuteBtnH As [Delegate]
        Public Auto_Plug_InBtnH As [Delegate]
    End Class
    Public Class AutomationVolumeHandlers
        Public reaperTrk As ReaperTrack
        Public c24Trk As C24_ChannelStrip
        Public env As ReaperTrackVolEnvelope
        Public AutovolChangeH As [Delegate]
    End Class
    Public Class AutomationPanHandlers
        Public reaperTrk As ReaperTrack
        Public c24Trk As C24_ChannelStrip
        Public env As ReaperTrackPanEnvelope
        Public AutoPanChangeH As [Delegate]
    End Class
    Public Class AutomationMuteHandlers
        Public reaperTrk As ReaperTrack
        Public c24Trk As C24_ChannelStrip
        Public env As ReaperTrackMuteEnvelope
        Public AutoMuteChangeH As [Delegate]
    End Class
    Public Class AutomationSendLevelHandlers
        Public reaperTrk As ReaperTrack
        Public c24Trk As C24_ChannelStrip
        Public SendVolEnvelope As ReaperSendVolEnvelope
        Public AutoSendVolumeChangeH As [Delegate]
    End Class
    Public Class AutomationSendMuteHandlers
        Public reaperTrk As ReaperTrack
        Public c24Trk As C24_ChannelStrip
        Public SendMuteEnvelope As ReaperSendMuteEnvelope
        Public AutoSendMuteChangeH As [Delegate]
    End Class
    Public Class ReaperEnvArmedHandlers
        Public ReaperTrk As ReaperTrack
        Public env As ReaperTrackEnvelope
        Public ReaperEnvArmChangeH As [Delegate]
    End Class
    Public Class ReaperTrackSoloHandlers
        Public ReaperTrk As ReaperTrack
        Public ReaperSoloChangeH As [Delegate]
        Public ReaperSoloDefeatChangeH As [Delegate]
    End Class
    Public Class ReaperAutoModeHandler
        Public ReaperTrk As ReaperTrack
        Public ReaperAutoModeChangeH As [Delegate]
    End Class
    Public Class AutomationPluginHandlers
        Public reaperTrk As ReaperTrack
        Public c24Trk As C24_ChannelStrip
        Public FxParamEnvelope As ReaperFxParamEnvelope
        Public AutoFxParamChangeH As [Delegate]
    End Class
    Public Class HomeStripHandler
        Public reaperTrk As ReaperTrack
        Public c24Trk As C24_ChannelStrip
        Public PanChangeH As [Delegate]
        Public VpotValueH As [Delegate]
        Public FaderHumanChangePanValueH As [Delegate]
    End Class
    Public Class FlipHandler
        Public FlipBtnH As [Delegate]
        Public EscBtnH As [Delegate]
    End Class
    Public Class AutoSelectModeHandler
        Public AutoSelectBtnH As [Delegate]
    End Class
#End Region







    Sub New(C24_Desk As Control24.Hardware.C24_Desk)
        _C24_Desk = C24_Desk

        ' ── SessionConfig AVANT Start() car On_ProjectChanged
        ' ── est déclenché de façon synchrone depuis ReaperSession.Start()
        _ReaperSessionConfig = New Reaper.ReaperSessionConfig

        ReaperPollHub.Instance.Start()

        _ReaperSession = New Reaper.ReaperSession
        _ReaperSession.Start()

        ' ── Démarrage du timer VU-mètres ──────────────────────────────────────
        _PeakTimer = New System.Timers.Timer(50)
        _PeakTimer.AutoReset = True
        AddHandler _PeakTimer.Elapsed, AddressOf OnPeakTimerElapsed
        _PeakTimer.Start()
    End Sub




    Sub ReaperSession_On_ProjectChanged(ReaperProj As Reaper.ReaperProject) Handles _ReaperSession.On_ProjectChanged
        _ReaperProj = ReaperProj

        'refresh Solo et solosafe dics
        RefreshDics(_ReaperProj)

        ' ── Quitter la vue COURANTE avant de disposer les managers ────────────
        _LastC24ViewMode = _ActualC24ViewMode
        RemoveViewsStripHandlers()

        ' ── Filet de sécurité : monitor FxParam ───────────────────────────────
        If _ReaperFxParamMonitor IsNot Nothing Then
            _ReaperFxParamMonitor.ClearWatched()
            _ReaperFxParamMonitor.Stop()
            _ReaperFxParamMonitor.Dispose()
            _ReaperFxParamMonitor = Nothing
        End If

        ' ── Libérer l'ancien ProjectMonitor ───────────────────────────────────
        If _ReaperProjectMonitor IsNot Nothing Then
            _ReaperProjectMonitor.Stop()
            _ReaperProjectMonitor.Dispose()
            _ReaperProjectMonitor = Nothing
        End If

        ' ── Libérer les anciens managers avant recréation ─────────────────────
        If _ReaperTracksManager IsNot Nothing Then
            RemoveCommonStripHandlers()
            _ReaperTracksManager.Dispose()
            _ReaperTracksManager = Nothing
        End If



        If _ReaperPeakLevelManager IsNot Nothing Then
            _ReaperPeakLevelManager.Dispose()
            _ReaperPeakLevelManager = Nothing
        End If

        _ReaperTracksManager = New Reaper.PageManagers.ReaperTracksManager(_ReaperProj,, _ReaperSessionConfig)

        _ReaperPeakLevelManager = New Reaper.PageManagers.ReaperPeakLevelManager()

        'RemoveTransportHandlers()
        _ReaperTransportManager = New Reaper.Managers.ReaperTransportManager
        _ReaperTransportManager.Start()

        ' ── Créer et démarrer le ProjectMonitor ───────────────────────────────
        '_ReaperProjectMonitor = New Monitors.ReaperProjectMonitor()
        'AddHandler _ReaperProjectMonitor.On_TrackCountChanged, AddressOf OnProjectMonitor_TrackCountChanged
        '_ReaperProjectMonitor.Start()

        RefreshTrackManagerPage()
        AddAllCommonC24Function()
        RefreshC24HomeViewMode()
    End Sub







#Region "Common"

    Private Sub AddCommonStripHandlers()

        '-------------------------Common Handlers------------------------------
        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)



                Dim h As New StripHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


                c24Trk.RecArm.Led = If(reaperTrk.RecArm = 0, False, True)
                Dim recArmChangeH As Reaper.ReaperTrack.On_RecArmChangeEventHandler =
                    Sub(recArm As Integer)
                        c24Trk.RecArm.Led = recArm <> 0
                    End Sub
                h.RecArmChangeH = recArmChangeH
                AddHandler reaperTrk.On_RecArmChange, recArmChangeH

                Dim recArmBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        If reaperTrk.RecArm = 0 Then
                            _ReaperTracksManager.SetRecArm(trackNb - 1, 1)
                            c24Trk.RecArm.IsBlinking = True
                        Else
                            _ReaperTracksManager.SetRecArm(trackNb - 1, 0)
                            c24Trk.RecArm.IsBlinking = False
                            c24Trk.RecArm.Led = False
                        End If
                    End Sub
                h.RecArmBtnH = recArmBtnH
                AddHandler c24Trk.RecArm.OnButtonReleased, recArmBtnH

                c24Trk.Select.Led = reaperTrk.Selected
                Dim selectedChangeH As Reaper.ReaperTrack.On_SelectedChangeEventHandler =
                    Sub(selected As Integer)
                        c24Trk.Select.Led = selected <> 0
                    End Sub
                h.SelectedChangeH = selectedChangeH
                AddHandler reaperTrk.On_SelectedChange, selectedChangeH

                Dim selectBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        If reaperTrk.Selected = 0 Then
                            _ReaperTracksManager.SetSelect(trackNb - 1, 1)
                            c24Trk.Select.Led = True
                        Else
                            _ReaperTracksManager.SetSelect(trackNb - 1, 0)
                            c24Trk.Select.Led = False
                        End If
                    End Sub
                h.SelectBtnH = selectBtnH
                AddHandler c24Trk.Select.OnButtonReleased, selectBtnH


                c24Trk.AutoModeLed.AutoMode = GetC24AutoMode(reaperTrk.AutoMode)
                Dim autoModeBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        Dim idx As Integer = Hardware.Utility.AutoModeButtonToOrder(c24Trk.AutoModeLed.AutoMode) + 1
                        If idx = 6 Then
                            idx = 0
                        End If
                        On_AutoModeChange(trackNb, Hardware.Utility.OrderToAutoModeButton(idx))
                    End Sub
                h.AutoModeBtnH = autoModeBtnH
                AddHandler c24Trk.AutoMode.OnButtonReleased, autoModeBtnH

                c24Trk.SendPanMode.Led = False
                Dim sendPanBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        If c24Trk.SendPanMode.Led Then
                            RefreshC24HomeViewMode()
                        Else
                            RefreshC24PanSendViewMode(trackNb)
                        End If
                        c24Trk.SendPanMode.Led = Not c24Trk.SendPanMode.Led
                    End Sub
                h.SendPanBtnH = sendPanBtnH
                AddHandler c24Trk.SendPanMode.OnButtonReleased, sendPanBtnH


                c24Trk.EqMode.Led = Not _ReaperTracksManager.IsTrackEqFxEmpty(reaperTrk.TrackIndex)
                Dim eqBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        Dim TrkIndex = _ReaperTracksManager.GetAbsoluteTrackIndex(trackNb - 1)
                        Dim _EqFxListManager As EqFxListManager = Nothing
                        If TrkIndexEqFxListManagers.ContainsKey(TrkIndex) Then
                            _EqFxListManager = TrkIndexEqFxListManagers(TrkIndex)
                        Else
                            _EqFxListManager = New EqFxListManager(TrkIndex, _ReaperTracksManager, _ReaperSessionConfig)
                            TrkIndexEqFxListManagers.Add(TrkIndex, _EqFxListManager)
                        End If
                        Dim EqFx = _EqFxListManager.EqBtnRelease
                        If EqFx Is Nothing Then
                            'il n'y a plus ou pas de fxeq dans la liste                           
                            c24Trk.EqMode.IsBlinking = False
                            c24Trk.EqMode.Led = Not _ReaperTracksManager.IsTrackEqFxEmpty(reaperTrk.TrackIndex)
                            RefreshC24HomeViewMode()
                        Else

                            c24Trk.EqMode.IsBlinking = True
                            RefreshC24FxParamViewMode(EqFx)

                        End If

                    End Sub
                h.EqBtnH = eqBtnH
                AddHandler c24Trk.EqMode.OnButtonReleased, eqBtnH

                c24Trk.DynMode.Led = Not _ReaperTracksManager.IsTrackDynFxEmpty(reaperTrk.TrackIndex)
                Dim dynBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        Dim TrkIndex = _ReaperTracksManager.GetAbsoluteTrackIndex(trackNb - 1)
                        Dim _DynFxListManager As DynFxListManager = Nothing
                        If TrkIndexDynFxListManagers.ContainsKey(TrkIndex) Then
                            _DynFxListManager = TrkIndexDynFxListManagers(TrkIndex)
                        Else
                            _DynFxListManager = New DynFxListManager(TrkIndex, _ReaperTracksManager, _ReaperSessionConfig)
                            TrkIndexDynFxListManagers.Add(TrkIndex, _DynFxListManager)
                        End If
                        Dim DynFx = _DynFxListManager.DynBtnRelease
                        If DynFx Is Nothing Then
                            'il n'y a plus ou pas de fxDyn dans la liste                           
                            c24Trk.DynMode.IsBlinking = False
                            c24Trk.DynMode.Led = Not _ReaperTracksManager.IsTrackDynFxEmpty(reaperTrk.TrackIndex)
                            RefreshC24HomeViewMode()
                        Else
                            'RefreshEqFxModeLed()
                            c24Trk.DynMode.IsBlinking = True
                            RefreshC24FxParamViewMode(DynFx)

                        End If
                    End Sub
                h.DynBtnH = dynBtnH
                AddHandler c24Trk.DynMode.OnButtonReleased, dynBtnH

                c24Trk.InsertMode.Led = Not _ReaperTracksManager.IsTrackGenericFxEmpty(reaperTrk.TrackIndex)
                Dim insertBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)


                        If c24Trk.InsertMode.IsBlinking Then
                            c24Trk.InsertMode.IsBlinking = False

                            c24Trk.InsertMode.Led = Not _ReaperTracksManager.IsTrackGenericFxEmpty(reaperTrk.TrackIndex)
                            RefreshC24HomeViewMode()
                        Else
                            RefreshInsertModeLed()
                            c24Trk.InsertMode.IsBlinking = True
                            RefreshC24InsertViewMode(trackNb)
                        End If



                    End Sub
                h.InsertBtnH = insertBtnH
                AddHandler c24Trk.InsertMode.OnButtonReleased, insertBtnH

                _CommonHandlers.Add(h)
                '----------------------------------------------------------------------------
            End If
        Next

    End Sub


    Public Sub RemoveCommonStripHandlers()

        For Each h In _CommonHandlers
            Dim r = h.ReaperTrk
            Dim c = h.C24Trk



            RemoveHandler r.On_RecArmChange, h.RecArmChangeH
            RemoveHandler c.RecArm.OnButtonReleased, h.RecArmBtnH
            RemoveHandler r.On_SelectedChange, h.SelectedChangeH
            RemoveHandler c.Select.OnButtonReleased, h.SelectBtnH

            RemoveHandler c.AutoMode.OnButtonReleased, h.AutoModeBtnH
            RemoveHandler c.SendPanMode.OnButtonReleased, h.SendPanBtnH

            RemoveHandler c.EqMode.OnButtonReleased, h.EqBtnH
            RemoveHandler c.DynMode.OnButtonReleased, h.DynBtnH
            RemoveHandler c.InsertMode.OnButtonReleased, h.InsertBtnH
        Next
        _CommonHandlers.Clear()
    End Sub

    Sub AddAllCommonC24Function()


        'Add C24 Handlers and Reaper C24 tracks
        AddSoloClearSafeC24Handlers()
        AddBankC24Handlers()
        AddTransportHandlers()
        AddShowC24Handlers()
        AddCounterModeHandler()
        AddModifiersHandlers()
        AddSendsLetterHandlers()
        AddAutomationEnableHandlers()


        'Add Reaper all tracks Handlers
        AddReaperEnvArmedHandlers()
        AddReaperSoloHandlers()
        AddReaperAutoModeHandlers()


    End Sub


    ''' <summary>Remet un channel strip à l'état vide (aucune piste assignée).</summary>
    Private Sub ClearChannelStripValues(c24Trk As Hardware.C24_ChannelStrip)
        c24Trk.ScribbleStrip.Text = ""
        c24Trk.Fader.SetInitFaderDbVolume(-150.0)
        c24Trk.Vpot.SetLedsOff()
        c24Trk.LedSwitchActive.Led = False
        c24Trk.Mute.IsBlinking = False
        c24Trk.Solo.Led = False
        c24Trk.RecArm.Led = False
        c24Trk.Select.Led = False
        c24Trk.SendPanMode.Led = False
        c24Trk.EqMode.Led = False
        c24Trk.DynMode.Led = False
        c24Trk.InsertMode.Led = False
        c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.None
        c24Trk.VueMeter.Peak_Left = 0
        c24Trk.VueMeter.Peak_Right = 0
        'c24Trk.VueMeter.ResetPeaks()
    End Sub

    Sub ClearC24_MiddleStrip(Optional ClearText As String = "    ")
        For Each C24_trk In C24_Desk.ChannelStrips
            C24_trk.ScribbleStrip.Text = ClearText
            C24_trk.Vpot.SetLedsOff()
            C24_trk.SwitchActive.IsBlinking = False
            C24_trk.LedSwitchActive.IsBlinking = False
            C24_trk.Vpot.ScribbleScriptsBlinking = False
        Next
    End Sub

    Sub AllScribbleScriptBlinking(Value As Boolean)
        For Each cs In C24_Desk.ChannelStrips
            If cs.Vpot.ScribbleScriptsBlinking <> Value Then
                cs.Vpot.ScribbleScriptsBlinking = Value
            End If
        Next
    End Sub
    Sub AllSwitchActive(LedValue As Boolean, Isblinking As Boolean)
        For Each cs In C24_Desk.ChannelStrips
            If cs.SwitchActive.Led <> LedValue Then
                cs.SwitchActive.Led = LedValue
            End If
            If cs.SwitchActive.IsBlinking <> Isblinking Then
                cs.SwitchActive.IsBlinking = Isblinking
            End If
        Next
    End Sub
    Sub AllSwitchActiveLed(LedValue As Boolean, Isblinking As Boolean)
        For Each cs In C24_Desk.ChannelStrips
            If cs.LedSwitchActive.Led <> LedValue Then
                cs.LedSwitchActive.Led = LedValue
            End If
            If cs.LedSwitchActive.IsBlinking <> Isblinking Then
                cs.LedSwitchActive.IsBlinking = Isblinking
            End If
        Next
    End Sub


    ''' <summary>
    ''' Déclenché par <see cref="Monitors.ReaperProjectMonitor"/> quand le nombre
    ''' de pistes du projet change (ajout ou suppression de piste dans Reaper).
    ''' Reconstruit la page courante du <see cref="_ReaperTracksManager"/>.
    ''' </summary>
    Private Sub OnProjectMonitor_TrackCountChanged(count As Integer)
        If _ReaperTracksManager Is Nothing Then Return
        RefreshTrackManagerPage()
    End Sub


    Sub AddFlipHandlers(RefreshView As [Delegate])

        C24_Desk.Flip.IsBlinking = Flip_Mode
        C24_Desk.Esc_Group.SetAllBlinking(Flip_Mode)
        Dim fh As New FlipHandler
        Dim FlipBtnH As C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        Flip_Mode = Not Flip_Mode
                        RefreshView.DynamicInvoke()

                    End Sub
        fh.FlipBtnH = FlipBtnH
        AddHandler C24_Desk.Flip.OnButtonReleased, FlipBtnH

        If Flip_Mode Then
            Dim EscBtnH As C24_GroupLedButton.OnGroupButtonReleasedEventHandler =
                 Sub()
                     Flip_Mode = Not Flip_Mode
                     RefreshView.DynamicInvoke()
                 End Sub
            fh.EscBtnH = EscBtnH
            AddHandler C24_Desk.Esc_Group.OnGroupButtonReleased, EscBtnH
        End If
        _FlipHandlers.Add(fh)
    End Sub

    Sub RemoveFlipHandlers()
        For Each fh In _FlipHandlers
            RemoveHandler C24_Desk.Flip.OnButtonReleased, fh.FlipBtnH
            RemoveHandler C24_Desk.Esc_Group.OnGroupButtonReleased, fh.EscBtnH
        Next
        _FlipHandlers.Clear()
    End Sub



#End Region
#Region "Volume"
    Sub AddFadersVolumeHandlers(Optional InitScribbleStripText As Boolean = False)
        RemoveFadersVolumeHandlers()

        _C24trkVolEnvelope.Clear()
        Dim sh As New VolumeHandlers
        sh.TrackMonitor = _ReaperTracksManager.TrackMonitor
        Dim ReaperVolChangeH As Reaper.Monitors.ReaperTrackMonitor.On_VolChangeEventHandler =
                    Sub(TrackIndex As Integer, vol As Double)
                        Dim c24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(TrackIndex))
                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        c24Trk.Fader.SetFaderDbVolume(Reaper.utility.SLIDER2DB(vol))
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
        sh.ReaperVolChangeH = ReaperVolChangeH
        AddHandler _ReaperTracksManager.TrackMonitor.On_VolChange, ReaperVolChangeH
        _FaderVolumeHandlers.Add(sh)

        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)
            c24Trk.Fader.Fader_Mode = C24_Fader.Mode.Db
            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)

                Dim h As New VolumeHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

                Dim TrackVolEnvelope As ReaperTrackVolEnvelope = If(_ReaperTracksManager.VolEnvelopes.ContainsKey(reaperTrk.TrackIndex), _ReaperTracksManager.VolEnvelopes(reaperTrk.TrackIndex), Nothing)
                If TrackVolEnvelope IsNot Nothing Then
                    _C24trkVolEnvelope.Add(c24Trk, TrackVolEnvelope)
                    h.TrackVolEnvelope = TrackVolEnvelope

                    Dim TrackVolEnvelopeVolChangeH As Reaper.ReaperTrackVolEnvelope.On_TrackUIVolChangeEventHandler =
                    Sub(_vol As Double)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        TrackVolEnvelope.Suspend_events = True
                        c24Trk.Fader.SetFaderDbVolume(Reaper.utility.SLIDER2DB(_vol))
                        TrackVolEnvelope.Suspend_events = False
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
                    h.TrackVolEnvelopeVolChangeH = TrackVolEnvelopeVolChangeH
                    AddHandler TrackVolEnvelope.On_TrackUIVolChange, TrackVolEnvelopeVolChangeH

                End If


                c24Trk.Fader.Fader_Mode = C24_Fader.Mode.Db
                c24Trk.Fader.SetInitFaderDbVolume(Reaper.utility.SLIDER2DB(reaperTrk.Vol))
                If InitScribbleStripText Then
                    c24Trk.ScribbleStrip.Text = reaperTrk.Name
                End If


                Dim faderHumanH As Hardware.C24_Fader.On_FaderHumanChangeDbValueEventHandler =
                    Sub(trackNb As Integer, dbValue As Double)
                        TrackVolEnvelope = _C24trkVolEnvelope(c24Trk)
                        'Automation
                        If TrackVolEnvelope IsNot Nothing Then
                            TrackVolEnvelope.Suspend_events = True
                        End If
                        ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                        Select Case reaperTrk.AutoMode
                            Case 0
                                _ReaperTracksManager.SetVolume(trackNb - 1, Reaper.utility.DB2SLIDER(dbValue))
                            Case Else
                                If TrackVolEnvelope IsNot Nothing Then
                                    _ReaperTracksManager.SuspendTrack(trackNb - 1)
                                    TrackVolEnvelope.Value = Reaper.utility.DB2SLIDER(dbValue)
                                    _ReaperTracksManager.ResumeTrack(trackNb - 1)
                                End If

                        End Select
                        If TrackVolEnvelope IsNot Nothing Then
                            TrackVolEnvelope.Suspend_events = False
                        End If

                    End Sub
                h.FaderHumanH = faderHumanH
                AddHandler c24Trk.Fader.On_FaderHumanChangeDbValue, faderHumanH






                _FaderVolumeHandlers.Add(h)
            Else
                ClearChannelStripValues(c24Trk)

            End If
        Next
        RaiseEvent FaderVpotVolHandlers(_C24trkVolEnvelope, True)
    End Sub
    Sub RemoveFadersVolumeHandlers()

        For Each h In _FaderVolumeHandlers
            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.Fader.On_FaderHumanChangeDbValue, h.FaderHumanH

                If h.TrackVolEnvelope IsNot Nothing Then
                    RemoveHandler h.TrackVolEnvelope.On_TrackUIVolChange, h.TrackVolEnvelopeVolChangeH
                End If
            End If


            If h.TrackMonitor IsNot Nothing Then
                RemoveHandler h.TrackMonitor.On_VolChange, h.ReaperVolChangeH
            End If

        Next

        _FaderVolumeHandlers.Clear()
    End Sub
    Sub AddVpotVolumeHandlers(Optional InitScribbleStripText As Boolean = False)
        RemoveVpotVolumeHandlers()

        _C24trkVolEnvelope.Clear()

        Dim sh As New VolumeHandlers
        sh.TrackMonitor = _ReaperTracksManager.TrackMonitor
        Dim ReaperVolChangeH As Reaper.Monitors.ReaperTrackMonitor.On_VolChangeEventHandler =
                    Sub(TrackIndex As Integer, vol As Double)
                        Dim c24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(TrackIndex))
                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)

                        c24Trk.Vpot.Value = vol
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
        sh.ReaperVolChangeH = ReaperVolChangeH
        AddHandler _ReaperTracksManager.TrackMonitor.On_VolChange, ReaperVolChangeH
        _VpotVolumeHandlers.Add(sh)



        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)

                Dim h As New VolumeHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

                Dim TrackVolEnvelope As ReaperTrackVolEnvelope = If(_ReaperTracksManager.VolEnvelopes.ContainsKey(reaperTrk.TrackIndex), _ReaperTracksManager.VolEnvelopes(reaperTrk.TrackIndex), Nothing)
                If TrackVolEnvelope IsNot Nothing Then
                    _C24trkVolEnvelope.Add(c24Trk, TrackVolEnvelope)
                    h.TrackVolEnvelope = TrackVolEnvelope

                    Dim TrackVolEnvelopeVolChangeH As Reaper.ReaperTrackVolEnvelope.On_TrackUIVolChangeEventHandler =
                    Sub(_vol As Double)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)

                        c24Trk.Vpot.Value = _vol
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
                    h.TrackVolEnvelopeVolChangeH = TrackVolEnvelopeVolChangeH
                    AddHandler TrackVolEnvelope.On_TrackUIVolChange, TrackVolEnvelopeVolChangeH
                End If



                c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.VolumeLinear
                c24Trk.Vpot.Value = reaperTrk.Vol
                If InitScribbleStripText Then
                    c24Trk.ScribbleStrip.Text = reaperTrk.Name
                End If



                Dim VpotValueChangeH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                    Sub(trackNb As Integer, Value As Double)
                        'Automation

                        ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                        Select Case reaperTrk.AutoMode
                            Case 0
                                _ReaperTracksManager.SetVolume(trackNb - 1, Value)
                            Case Else
                                If TrackVolEnvelope IsNot Nothing Then
                                    _ReaperTracksManager.SuspendTrack(trackNb - 1)
                                    TrackVolEnvelope.Value = Value
                                    _ReaperTracksManager.ResumeTrack(trackNb - 1)

                                End If

                        End Select

                    End Sub
                h.VpotValueChangeH = VpotValueChangeH
                AddHandler c24Trk.Vpot.OnVpotValueChange, VpotValueChangeH

                _VpotVolumeHandlers.Add(h)
            Else
                ClearChannelStripValues(c24Trk)
            End If
        Next
        RaiseEvent FaderVpotVolHandlers(_C24trkVolEnvelope, True)
    End Sub
    Sub RemoveVpotVolumeHandlers()

        For Each h In _VpotVolumeHandlers

            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.Vpot.OnVpotValueChange, h.VpotValueChangeH

                If h.TrackVolEnvelope IsNot Nothing Then
                    RemoveHandler h.TrackVolEnvelope.On_TrackUIVolChange, h.TrackVolEnvelopeVolChangeH
                End If
            End If


            If h.TrackMonitor IsNot Nothing Then
                RemoveHandler h.TrackMonitor.On_VolChange, h.ReaperVolChangeH
            End If

        Next
        _VpotVolumeHandlers.Clear()
    End Sub
    Sub AddVpotVolumeHandler(c24Trk As C24_ChannelStrip, ReaperTrk As ReaperTrack)
        RemoveVpotVolumeHandlers()

        _C24trkVolEnvelope.Clear()

        Dim sh As New VolumeHandlers
        sh.TrackMonitor = _ReaperTracksManager.TrackMonitor
        Dim ReaperVolChangeH As Reaper.Monitors.ReaperTrackMonitor.On_VolChangeEventHandler =
                    Sub(TrackIndex As Integer, vol As Double)
                        If TrackIndex = ReaperTrk.TrackIndex Then
                            Dim Faderc24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(TrackIndex))
                            _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                            c24Trk.Vpot.Value = vol
                            If Faderc24Trk IsNot Nothing Then
                                Faderc24Trk.Fader.DbVolume = Reaper.utility.SLIDER2DB(vol)
                            End If
                            _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                        End If


                    End Sub
        sh.ReaperVolChangeH = ReaperVolChangeH
        AddHandler _ReaperTracksManager.TrackMonitor.On_VolChange, ReaperVolChangeH
        _VpotVolumeHandlers.Add(sh)





        Dim h As New VolumeHandlers With {
                    .ReaperTrk = ReaperTrk,
                    .C24Trk = c24Trk
                }

        Dim TrackVolEnvelope As ReaperTrackVolEnvelope = If(_ReaperTracksManager.VolEnvelopes.ContainsKey(ReaperTrk.TrackIndex), _ReaperTracksManager.VolEnvelopes(ReaperTrk.TrackIndex), Nothing)
        If TrackVolEnvelope IsNot Nothing Then
            _C24trkVolEnvelope.Add(c24Trk, TrackVolEnvelope)
            h.TrackVolEnvelope = TrackVolEnvelope

            Dim TrackVolEnvelopeVolChangeH As Reaper.ReaperTrackVolEnvelope.On_TrackUIVolChangeEventHandler =
                    Sub(_vol As Double)
                        Dim Faderc24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(ReaperTrk.TrackIndex))
                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        c24Trk.Vpot.Value = _vol
                        If Faderc24Trk IsNot Nothing Then
                            Faderc24Trk.Fader.DbVolume = Reaper.utility.SLIDER2DB(_vol)
                        End If
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
            h.TrackVolEnvelopeVolChangeH = TrackVolEnvelopeVolChangeH
            AddHandler TrackVolEnvelope.On_TrackUIVolChange, TrackVolEnvelopeVolChangeH
        End If



        c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.VolumeLinear
        c24Trk.Vpot.Value = ReaperTrk.Vol




        Dim VpotValueChangeH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                    Sub(trackNb As Integer, Value As Double)
                        'Automation

                        ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                        Select Case ReaperTrk.AutoMode
                            Case 0
                                _ReaperTracksManager.SetVolume(trackNb - 1, Value)
                            Case Else
                                If TrackVolEnvelope IsNot Nothing Then
                                    _ReaperTracksManager.SuspendTrack(trackNb - 1)
                                    TrackVolEnvelope.Value = Value
                                    _ReaperTracksManager.ResumeTrack(trackNb - 1)

                                End If

                        End Select

                    End Sub
        h.VpotValueChangeH = VpotValueChangeH
        AddHandler c24Trk.Vpot.OnVpotValueChange, VpotValueChangeH

        _VpotVolumeHandlers.Add(h)


        RaiseEvent FaderVpotVolHandlers(_C24trkVolEnvelope, True)
    End Sub


    Sub AddAutomationVolumeHandlers(C24trkVolEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackVolEnvelope), status As Boolean) Handles Me.FaderVpotVolHandlers
        IsFaderVpotVolHandlers = status
        _C24trkVolEnvelope = C24trkVolEnvelope
        RemoveAutomationVolumeHandlers()

        For Each c24Trk In C24trkVolEnvelope.Keys

            Dim TrackVolEnvelope = C24trkVolEnvelope(c24Trk)
            Dim reaperTrk = _ReaperProj.Tracks(TrackVolEnvelope.TrackIndex)

            Dim h As New AutomationVolumeHandlers
            h.reaperTrk = reaperTrk
            h.c24Trk = c24Trk

            h.env = TrackVolEnvelope
            TrackVolEnvelope.Refresh()

            If c24Trk IsNot Nothing Then
                _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
            End If

            If FaderAuto AndAlso status Then
                ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                Select Case reaperTrk.AutoMode
                    Case 1
                        TrackVolEnvelope.Active = True
                        TrackVolEnvelope.Armed = True


                        If c24Trk IsNot Nothing Then
                            If Flip_Mode = False Then
                                Dim AutovolChangeH As Reaper.ReaperTrackVolEnvelope.On_TrackUIVolChangeEventHandler =
                                Sub(_vol As Double)
                                    c24Trk.Fader.SetFaderDbVolume(Reaper.utility.SLIDER2DB(_vol))

                                End Sub
                                h.AutovolChangeH = AutovolChangeH
                                AddHandler TrackVolEnvelope.On_TrackUIVolChange, AutovolChangeH
                            Else 'flip mode 
                                Dim AutovolChangeH As Reaper.ReaperTrackVolEnvelope.On_TrackUIVolChangeEventHandler =
                                Sub(_vol As Double)

                                    c24Trk.Vpot.Value = _vol

                                End Sub
                                h.AutovolChangeH = AutovolChangeH
                                AddHandler TrackVolEnvelope.On_TrackUIVolChange, AutovolChangeH
                            End If

                        End If


                    Case 0
                        TrackVolEnvelope.Active = False
                        TrackVolEnvelope.Armed = False

                    Case Else
                        TrackVolEnvelope.Active = True
                        TrackVolEnvelope.Armed = True

                End Select
            Else
                TrackVolEnvelope.Active = False
                TrackVolEnvelope.Armed = False


            End If

            If c24Trk IsNot Nothing Then
                _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
            End If


            _AutomationVolumeHandlers.Add(h)
        Next

    End Sub
    Sub RemoveAutomationVolumeHandlers()

        For Each h In _AutomationVolumeHandlers
            If h.env IsNot Nothing Then
                RemoveHandler h.env.On_TrackUIVolChange, h.AutovolChangeH
            End If
        Next



        _AutomationVolumeHandlers.Clear()
    End Sub

#End Region
#Region "Pan"
    Sub AddFadersPanHandlers(Optional InitScribbleStripText As Boolean = False)
        RemoveFadersPanHandlers()

        _C24trkPanEnvelope.Clear()
        Dim sh As New PanHandlers
        sh.TrackMonitor = _ReaperTracksManager.TrackMonitor
        Dim ReaperPanChangeH As Reaper.Monitors.ReaperTrackMonitor.On_PanChangeEventHandler =
                    Sub(TrackIndex As Integer, pan As Double)
                        Dim c24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(TrackIndex))
                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        c24Trk.Fader.SetFaderPanValue(pan)
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
        sh.ReaperPanChangeH = ReaperPanChangeH
        AddHandler _ReaperTracksManager.TrackMonitor.On_PanChange, ReaperPanChangeH
        _FaderPanHandlers.Add(sh)

        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)



                Dim h As New PanHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

                Dim TrackPanEnvelope As ReaperTrackPanEnvelope = If(_ReaperTracksManager.PanEnvelopes.ContainsKey(reaperTrk.TrackIndex), _ReaperTracksManager.PanEnvelopes(reaperTrk.TrackIndex), Nothing)
                If TrackPanEnvelope IsNot Nothing Then
                    _C24trkPanEnvelope.Add(c24Trk, TrackPanEnvelope)
                    h.TrackPanEnvelope = TrackPanEnvelope

                    Dim TrackPanEnvelopeVolChangeH As Reaper.ReaperTrackPanEnvelope.On_TrackUIPanChangeEventHandler =
                    Sub(_pan As Double)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        TrackPanEnvelope.Suspend_events = True
                        c24Trk.Fader.SetFaderPanValue(_pan)
                        TrackPanEnvelope.Suspend_events = False
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)

                    End Sub
                    h.TrackPanEnvelopeVolChangeH = TrackPanEnvelopeVolChangeH
                    AddHandler TrackPanEnvelope.On_TrackUIPanChange, TrackPanEnvelopeVolChangeH

                End If




                c24Trk.Fader.Fader_Mode = C24_Fader.Mode.Pan
                c24Trk.Fader.SetFaderPanValue(reaperTrk.Pan)
                If InitScribbleStripText Then
                    c24Trk.ScribbleStrip.Text = reaperTrk.Name
                End If

                Dim FaderHumanH As Hardware.C24_Fader.On_FaderHumanChangePanValueEventHandler =
                    Sub(trackNb As Integer, _Panvalue As Double)
                        'Automation
                        If TrackPanEnvelope IsNot Nothing Then
                            TrackPanEnvelope.Suspend_events = True
                        End If
                        ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                        Select Case reaperTrk.AutoMode
                            Case 0
                                _ReaperTracksManager.SetPan(trackNb - 1, _Panvalue)
                            Case Else
                                If _ReaperTracksManager.PanEnvelopes.ContainsKey(trackNb - 1) Then
                                    _ReaperTracksManager.SuspendTrack(trackNb - 1)
                                    TrackPanEnvelope.Value = _Panvalue
                                    _ReaperTracksManager.ResumeTrack(trackNb - 1)

                                End If

                        End Select
                        If TrackPanEnvelope IsNot Nothing Then
                            TrackPanEnvelope.Suspend_events = False
                        End If
                    End Sub
                h.FaderHumanH = FaderHumanH
                AddHandler c24Trk.Fader.On_FaderHumanChangePanValue, FaderHumanH

                _FaderPanHandlers.Add(h)

            Else
                ClearChannelStripValues(c24Trk)
            End If
        Next
        RaiseEvent FaderVpotPanHandlers(_C24trkPanEnvelope, True)


    End Sub
    Sub RemoveFadersPanHandlers()
        For Each h In _FaderPanHandlers
            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.Fader.On_FaderHumanChangePanValue, h.FaderHumanH

                If h.TrackPanEnvelope IsNot Nothing Then
                    RemoveHandler h.TrackPanEnvelope.On_TrackUIPanChange, h.TrackPanEnvelopeVolChangeH
                End If
            End If


            If h.TrackMonitor IsNot Nothing Then
                RemoveHandler h.TrackMonitor.On_PanChange, h.ReaperPanChangeH
            End If

        Next

        _FaderPanHandlers.Clear()
    End Sub
    Sub AddVpotPanHandlers(Optional InitScribbleStripText As Boolean = False)
        RemoveVpotPanHandlers()

        _C24trkPanEnvelope.Clear()
        Dim sh As New PanHandlers
        sh.TrackMonitor = _ReaperTracksManager.TrackMonitor
        Dim ReaperPanChangeH As Reaper.Monitors.ReaperTrackMonitor.On_PanChangeEventHandler =
                    Sub(TrackIndex As Integer, pan As Double)

                        Dim c24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(TrackIndex))
                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)

                        c24Trk.Vpot.Value = pan

                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
        sh.ReaperPanChangeH = ReaperPanChangeH
        AddHandler _ReaperTracksManager.TrackMonitor.On_PanChange, ReaperPanChangeH
        _VpotPanHandlers.Add(sh)

        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)



                Dim h As New PanHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

                Dim TrackPanEnvelope As ReaperTrackPanEnvelope = If(_ReaperTracksManager.PanEnvelopes.ContainsKey(reaperTrk.TrackIndex), _ReaperTracksManager.PanEnvelopes(reaperTrk.TrackIndex), Nothing)
                If TrackPanEnvelope IsNot Nothing Then
                    _C24trkPanEnvelope.Add(c24Trk, TrackPanEnvelope)
                    h.TrackPanEnvelope = TrackPanEnvelope

                    Dim TrackPanEnvelopeVolChangeH As Reaper.ReaperTrackPanEnvelope.On_TrackUIPanChangeEventHandler =
                    Sub(_pan As Double)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        TrackPanEnvelope.Suspend_events = True
                        c24Trk.Vpot.Value = _pan
                        TrackPanEnvelope.Suspend_events = False
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
                    h.TrackPanEnvelopeVolChangeH = TrackPanEnvelopeVolChangeH
                    AddHandler TrackPanEnvelope.On_TrackUIPanChange, TrackPanEnvelopeVolChangeH


                End If



                c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.Pan
                c24Trk.Vpot.Value = reaperTrk.Pan
                If InitScribbleStripText Then
                    c24Trk.ScribbleStrip.Text = reaperTrk.Name
                End If


                Dim VpotValueChangeH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                    Sub(trackNb As Integer, value As Double)
                        'Automation
                        If TrackPanEnvelope IsNot Nothing Then
                            TrackPanEnvelope.Suspend_events = True
                        End If
                        ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                        Select Case reaperTrk.AutoMode
                            Case 0, 1
                                _ReaperTracksManager.SetPan(trackNb - 1, value)
                                '

                            Case Else
                                If TrackPanEnvelope IsNot Nothing Then
                                    _ReaperTracksManager.SuspendTrack(trackNb - 1)


                                    TrackPanEnvelope.Value = value



                                    _ReaperTracksManager.ResumeTrack(trackNb - 1)
                                End If
                        End Select

                        If TrackPanEnvelope IsNot Nothing Then
                            TrackPanEnvelope.Suspend_events = False
                        End If

                    End Sub
                h.VpotValueChangeH = VpotValueChangeH
                AddHandler c24Trk.Vpot.OnVpotValueChange, VpotValueChangeH





                _VpotPanHandlers.Add(h)

            Else
                ClearChannelStripValues(c24Trk)
            End If
        Next
        RaiseEvent FaderVpotPanHandlers(_C24trkPanEnvelope, True)


    End Sub
    Sub AddVpotPanHandler(c24Trk As C24_ChannelStrip, ReaperTrk As ReaperTrack)
        RemoveVpotPanHandlers()

        _C24trkPanEnvelope.Clear()
        Dim sh As New PanHandlers
        sh.TrackMonitor = _ReaperTracksManager.TrackMonitor
        Dim ReaperPanChangeH As Reaper.Monitors.ReaperTrackMonitor.On_PanChangeEventHandler =
                    Sub(TrackIndex As Integer, pan As Double)
                        If TrackIndex = ReaperTrk.TrackIndex Then
                            _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                            c24Trk.Vpot.Value = pan
                            _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                        End If
                    End Sub
        sh.ReaperPanChangeH = ReaperPanChangeH
        AddHandler _ReaperTracksManager.TrackMonitor.On_PanChange, ReaperPanChangeH
        _VpotPanHandlers.Add(sh)



        Dim h As New PanHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

                Dim TrackPanEnvelope As ReaperTrackPanEnvelope = If(_ReaperTracksManager.PanEnvelopes.ContainsKey(reaperTrk.TrackIndex), _ReaperTracksManager.PanEnvelopes(reaperTrk.TrackIndex), Nothing)
                If TrackPanEnvelope IsNot Nothing Then
                    _C24trkPanEnvelope.Add(c24Trk, TrackPanEnvelope)
                    h.TrackPanEnvelope = TrackPanEnvelope

                    Dim TrackPanEnvelopeVolChangeH As Reaper.ReaperTrackPanEnvelope.On_TrackUIPanChangeEventHandler =
                    Sub(_pan As Double)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        TrackPanEnvelope.Suspend_events = True
                        c24Trk.Vpot.Value = _pan
                        TrackPanEnvelope.Suspend_events = False
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
                    h.TrackPanEnvelopeVolChangeH = TrackPanEnvelopeVolChangeH
                    AddHandler TrackPanEnvelope.On_TrackUIPanChange, TrackPanEnvelopeVolChangeH


                End If



                c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.Pan
                c24Trk.Vpot.Value = reaperTrk.Pan



        Dim VpotValueChangeH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                    Sub(trackNb As Integer, value As Double)
                        'Automation
                        If TrackPanEnvelope IsNot Nothing Then
                            TrackPanEnvelope.Suspend_events = True
                        End If
                        ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                        Select Case reaperTrk.AutoMode
                            Case 0, 1
                                _ReaperTracksManager.SetPan(trackNb - 1, value)
                                '

                            Case Else
                                If TrackPanEnvelope IsNot Nothing Then
                                    _ReaperTracksManager.SuspendTrack(trackNb - 1)


                                    TrackPanEnvelope.Value = value



                                    _ReaperTracksManager.ResumeTrack(trackNb - 1)
                                End If
                        End Select

                        If TrackPanEnvelope IsNot Nothing Then
                            TrackPanEnvelope.Suspend_events = False
                        End If

                    End Sub
                h.VpotValueChangeH = VpotValueChangeH
                AddHandler c24Trk.Vpot.OnVpotValueChange, VpotValueChangeH





                _VpotPanHandlers.Add(h)


        RaiseEvent FaderVpotPanHandlers(_C24trkPanEnvelope, True)


    End Sub

    Sub RemoveVpotPanHandlers()
        For Each h In _VpotPanHandlers

            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.Vpot.OnVpotValueChange, h.VpotValueChangeH

                If h.TrackPanEnvelope IsNot Nothing Then
                    RemoveHandler h.TrackPanEnvelope.On_TrackUIPanChange, h.TrackPanEnvelopeVolChangeH
                End If
            End If


            If h.TrackMonitor IsNot Nothing Then
                RemoveHandler h.TrackMonitor.On_PanChange, h.ReaperPanChangeH
            End If

        Next
        _VpotPanHandlers.Clear()
    End Sub

    Sub AddAutomationPanHandlers(C24trkPanEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackPanEnvelope), status As Boolean) Handles Me.FaderVpotPanHandlers
        _C24trkPanEnvelope = C24trkPanEnvelope
        IsFaderVpotPanHandlers = status
        RemoveAutomationPanHandlers()


        For Each c24Trk In C24trkPanEnvelope.Keys

            Dim TrackPanEnvelope = C24trkPanEnvelope(c24Trk)
            Dim reaperTrk = _ReaperProj.Tracks(TrackPanEnvelope.TrackIndex)

            Dim h As New AutomationPanHandlers
            h.reaperTrk = reaperTrk
            h.c24Trk = c24Trk

            h.env = TrackPanEnvelope
            TrackPanEnvelope.Refresh()

            If c24Trk IsNot Nothing Then
                _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
            End If

            If PanAuto AndAlso status Then
                ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                Select Case reaperTrk.AutoMode
                    Case 1
                        TrackPanEnvelope.Active = True
                        TrackPanEnvelope.Armed = True


                        If c24Trk IsNot Nothing Then
                            If Flip_Mode = False Then
                                Dim AutoPanChangeH As Reaper.ReaperTrackPanEnvelope.On_TrackUIPanChangeEventHandler =
                            Sub(_pan As Double)
                                c24Trk.Vpot.Value = _pan

                            End Sub
                                h.AutoPanChangeH = AutoPanChangeH
                                AddHandler TrackPanEnvelope.On_TrackUIPanChange, AutoPanChangeH
                            Else 'flip mode 
                                Dim AutoPanChangeH As Reaper.ReaperTrackPanEnvelope.On_TrackUIPanChangeEventHandler =
                            Sub(_pan As Double)

                                c24Trk.Fader.SetFaderPanValue(_pan)

                            End Sub
                                h.AutoPanChangeH = AutoPanChangeH
                                AddHandler TrackPanEnvelope.On_TrackUIPanChange, AutoPanChangeH
                            End If

                        End If


                    Case 0
                        TrackPanEnvelope.Active = False
                        TrackPanEnvelope.Armed = False

                    Case Else
                        TrackPanEnvelope.Active = True
                        TrackPanEnvelope.Armed = True

                End Select
            Else
                TrackPanEnvelope.Active = False
                TrackPanEnvelope.Armed = False


            End If

            If c24Trk IsNot Nothing Then
                _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
            End If


            _AutomationPanHandlers.Add(h)
        Next


    End Sub
    Sub RemoveAutomationPanHandlers()

        For Each h In _AutomationPanHandlers
            If h.env IsNot Nothing Then
                RemoveHandler h.env.On_TrackUIPanChange, h.AutoPanChangeH
            End If
        Next



        _AutomationPanHandlers.Clear()
    End Sub

#End Region
#Region "Mute"
    Sub AddMutesBtnHandlers()
        RemoveMutesBtnHandlers()
        _C24trkMuteEnvelope.Clear()

        Dim sh As New MutesHandlers
        sh.TrackMonitor = _ReaperTracksManager.TrackMonitor
        Dim ReaperMuteBtnChangeH As Reaper.Monitors.ReaperTrackMonitor.On_MuteChangeEventHandler =
                    Sub(TrackIndex As Integer, mute As Boolean)

                        Dim c24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(TrackIndex))
                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        MuteLedChange(TrackIndex, mute)
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
        sh.ReaperMuteBtnChangeH = ReaperMuteBtnChangeH
        AddHandler _ReaperTracksManager.TrackMonitor.On_MuteChange, ReaperMuteBtnChangeH
        _MuteHandlers.Add(sh)

        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)
                Dim h As New MutesHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

                Dim TrackMuteEnvelope As ReaperTrackMuteEnvelope = If(_ReaperTracksManager.MuteEnvelopes.ContainsKey(reaperTrk.TrackIndex), _ReaperTracksManager.MuteEnvelopes(reaperTrk.TrackIndex), Nothing)
                If TrackMuteEnvelope IsNot Nothing Then
                    h.TrackMuteEnvelope = TrackMuteEnvelope
                    _C24trkMuteEnvelope.Add(c24Trk, TrackMuteEnvelope)

                    Dim TrackMuteEnvelopeChangeH As Reaper.ReaperTrackMuteEnvelope.On_TrackUIMuteChangeEventHandler =
                    Sub(_mute As Boolean)
                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        TrackMuteEnvelope.Suspend_events = True
                        MuteLedChange(reaperTrk.TrackIndex, _mute)
                        TrackMuteEnvelope.Suspend_events = False
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
                    h.TrackMuteEnvelopeChangeH = TrackMuteEnvelopeChangeH
                    AddHandler TrackMuteEnvelope.On_TrackUIMuteChange, TrackMuteEnvelopeChangeH

                End If

                'init
                _ReaperTracksManager.Set_MuteStatus(_ReaperTracksManager.GetRelativeTrackIndex(reaperTrk.TrackIndex), reaperTrk.Mute)

                Dim muteBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)

                        'Automation
                        If TrackMuteEnvelope IsNot Nothing Then
                            TrackMuteEnvelope.Suspend_events = True
                        End If
                        ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                        Select Case reaperTrk.AutoMode

                            Case 0
                                _ReaperTracksManager.Set_MuteStatus(trackNb - 1, Not reaperTrk.Mute)

                            Case Else
                                If TrackMuteEnvelope IsNot Nothing Then
                                    _ReaperTracksManager.SuspendTrack(trackNb - 1)
                                    TrackMuteEnvelope.Value = If(TrackMuteEnvelope.Value = 1, 0, 1)
                                    _ReaperTracksManager.ResumeTrack(trackNb - 1)
                                End If


                        End Select

                        MuteLedChange(reaperTrk.TrackIndex, reaperTrk.Mute)

                        If TrackMuteEnvelope IsNot Nothing Then
                            TrackMuteEnvelope.Suspend_events = False
                        End If


                    End Sub
                h.MuteBtnH = muteBtnH
                AddHandler c24Trk.Mute.OnButtonReleased, muteBtnH
                _MuteHandlers.Add(h)
            End If
        Next
        RaiseEvent MuteBtnHandlers(_C24trkMuteEnvelope, True)
    End Sub
    Sub RemoveMutesBtnHandlers()
        ClearMuteLeds()

        For Each h In _MuteHandlers

            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.Mute.OnButtonReleased, h.MuteBtnH

                If h.TrackMuteEnvelope IsNot Nothing Then
                    RemoveHandler h.TrackMuteEnvelope.On_TrackUIMuteChange, h.TrackMuteEnvelopeChangeH
                End If
            End If


            If h.TrackMonitor IsNot Nothing Then
                RemoveHandler h.TrackMonitor.On_MuteChange, h.ReaperMuteBtnChangeH
            End If


        Next
        _MuteHandlers.Clear()
    End Sub
    Sub ClearMuteLeds()
        For i = 0 To 23
            Dim cs = C24_Desk.ChannelStrips(i)
            cs.Mute.IsBlinking = False
        Next
    End Sub

    Sub AddAutomationMuteHandlers(C24trkMuteEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackMuteEnvelope), status As Boolean) Handles Me.MuteBtnHandlers
        _C24trkMuteEnvelope = C24trkMuteEnvelope
        IsMuteBtnHandlers = status
        RemoveAutomationMuteHandlers()

        For Each c24Trk In C24trkMuteEnvelope.Keys

            Dim TrackMuteEnvelope = C24trkMuteEnvelope(c24Trk)
            Dim reaperTrk = _ReaperProj.Tracks(TrackMuteEnvelope.TrackIndex)

            Dim h As New AutomationMuteHandlers
            h.reaperTrk = reaperTrk
            h.c24Trk = c24Trk

            h.env = TrackMuteEnvelope
            TrackMuteEnvelope.Refresh()



            If c24Trk IsNot Nothing Then
                _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
            End If

            If MuteAuto AndAlso status Then
                ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                Select Case reaperTrk.AutoMode
                    Case 1
                        TrackMuteEnvelope.Active = True
                        TrackMuteEnvelope.Armed = True


                        If c24Trk IsNot Nothing Then

                            Dim AutoMuteChangeH As Reaper.ReaperTrackMuteEnvelope.On_TrackUIMuteChangeEventHandler =
                            Sub(_mute As Boolean)

                                MuteLedChange(reaperTrk.TrackIndex, _mute)

                            End Sub
                            h.AutoMuteChangeH = AutoMuteChangeH
                            AddHandler TrackMuteEnvelope.On_TrackUIMuteChange, AutoMuteChangeH


                        End If


                    Case 0
                        TrackMuteEnvelope.Active = False
                        TrackMuteEnvelope.Armed = False

                    Case Else
                        TrackMuteEnvelope.Active = True
                        TrackMuteEnvelope.Armed = True

                End Select
            Else
                TrackMuteEnvelope.Active = False
                TrackMuteEnvelope.Armed = False


            End If

            If c24Trk IsNot Nothing Then
                _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
            End If


            _AutomationMuteHandlers.Add(h)

        Next

    End Sub
    Sub RemoveAutomationMuteHandlers()
        For Each h In _AutomationMuteHandlers
            If h.env IsNot Nothing Then
                RemoveHandler h.env.On_TrackUIMuteChange, h.AutoMuteChangeH
            End If
        Next



        _AutomationMuteHandlers.Clear()
    End Sub



#End Region
#Region "Send Volume"
    Sub AddFadersSendVolumeHandlers(Optional InitScribbleStripText As Boolean = False)
        RemoveFadersSendVolumeHandlers()

        _C24trkSendVolEnvelope.Clear()
        Dim sh As New SendVolumeHandlers
        sh.SendMonitor = _ReaperTracksSendManager._SendMonitor
        Dim ReaperSendVolChangeH As Reaper.Monitors.ReaperSendMonitor.On_VolChangeEventHandler =
                    Sub(TrackIndex As Integer, vol As Double)
                        Dim c24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(TrackIndex))
                        _ReaperTracksSendManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        c24Trk.Fader.SetFaderDbVolume(Reaper.utility.SLIDER2DB(vol))
                        _ReaperTracksSendManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
        sh.ReaperSendVolChangeH = ReaperSendVolChangeH
        AddHandler _ReaperTracksSendManager._SendMonitor.On_VolChange, ReaperSendVolChangeH
        _FaderSendVolumeHandlers.Add(sh)



        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)
            c24Trk.Fader.Fader_Mode = C24_Fader.Mode.Db
            If i < _ReaperTracksSendManager.Tracks.Count Then

                Dim reaperTrk = _ReaperTracksManager.Tracks(i)
                Dim send As ReaperSend = Nothing
                send = _ReaperTracksSendManager.Sends(i)




                If send Is Nothing Then
                    ClearSendChannelStripValues(c24Trk)
                    Continue For
                Else
                    'init values
                    If InitScribbleStripText Then
                        Dim _ReaperTrackSendsEditor = New Reaper.PageManagers.ReaperTrackSendsEditor(reaperTrk.TrackIndex, _ReaperProj.Tracks)
                        c24Trk.ScribbleStrip.Text = _ReaperTrackSendsEditor.Sends_Names(send.SendIndex)
                    End If
                End If

                Dim h As New SendVolumeHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


                Dim SendVolEnvelope As ReaperSendVolEnvelope = Nothing
                For Each _SendVolEnvelope In reaperTrk.Envelopes.AllOfType(Of ReaperSendVolEnvelope)
                    If _SendVolEnvelope.SendId = send.SendIndex Then
                        SendVolEnvelope = _SendVolEnvelope
                        Exit For
                    End If
                Next
                If SendVolEnvelope IsNot Nothing Then
                    _C24trkSendVolEnvelope.Add(c24Trk, SendVolEnvelope)

                    Dim SendVolEnvelopeVolChangeH As Reaper.ReaperSendVolEnvelope.On_SendUIVolChangeEventHandler =
                    Sub(_vol As Double)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        SendVolEnvelope.Suspend_events = True
                        c24Trk.Fader.SetFaderDbVolume(Reaper.utility.SLIDER2DB(_vol))
                        SendVolEnvelope.Suspend_events = False
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
                    h.SendVolEnvelopeVolChangeH = SendVolEnvelopeVolChangeH
                    AddHandler SendVolEnvelope.On_SendUIVolChange, SendVolEnvelopeVolChangeH


                End If


                'init Fader value
                c24Trk.Fader.Fader_Mode = C24_Fader.Mode.Db
                c24Trk.Fader.SetFaderDbVolume(Reaper.utility.SLIDER2DB(send.Vol))

                Dim FaderHumanH As Hardware.C24_Fader.On_FaderHumanChangeDbValueEventHandler =
                         Sub(TrackNb As Integer, DbValue As Double)

                             'Automation
                             If SendVolEnvelope IsNot Nothing Then
                                 SendVolEnvelope.Suspend_events = True
                             End If
                             ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                             Select Case reaperTrk.AutoMode
                                 Case 0, 1
                                     send.Vol = Reaper.utility.DB2SLIDER(DbValue)
                                 Case Else
                                     SendVolEnvelope = _C24trkSendVolEnvelope(c24Trk)
                                     If SendVolEnvelope IsNot Nothing Then
                                         _ReaperTracksManager.SuspendTrack(TrackNb - 1)
                                         SendVolEnvelope.Value = Reaper.utility.DB2SLIDER(DbValue)
                                         _ReaperTracksManager.ResumeTrack(TrackNb - 1)
                                     End If
                             End Select
                             If SendVolEnvelope IsNot Nothing Then
                                 SendVolEnvelope.Suspend_events = False
                             End If
                         End Sub
                h.FaderHumanH = FaderHumanH
                AddHandler c24Trk.Fader.On_FaderHumanChangeDbValue, FaderHumanH


                _FaderSendVolumeHandlers.Add(h)
            Else
                ClearSendChannelStripValues(c24Trk)
            End If
        Next
        RaiseEvent FaderVpotSendLevelHandlers(_C24trkSendVolEnvelope, True)
    End Sub
    Sub RemoveFadersSendVolumeHandlers()

        For Each h In _FaderSendVolumeHandlers
            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.Fader.On_FaderHumanChangeDbValue, h.FaderHumanH


                If h.SendVolEnvelope IsNot Nothing Then
                    AddHandler h.SendVolEnvelope.On_SendUIVolChange, h.SendVolEnvelopeVolChangeH
                End If
            End If


            If h.SendMonitor IsNot Nothing Then
                RemoveHandler h.SendMonitor.On_VolChange, h.ReaperSendVolChangeH
            End If

        Next

        _FaderSendVolumeHandlers.Clear()
    End Sub
    Sub AddVpotSendVolumeHandlers(Optional InitScribbleStripText As Boolean = False)
        RemoveVpotSendVolumeHandlers()

        _C24trkSendVolEnvelope.Clear()
        Dim sh As New SendVolumeHandlers
        sh.SendMonitor = _ReaperTracksSendManager._SendMonitor
        Dim ReaperSendVolChangeH As Reaper.Monitors.ReaperSendMonitor.On_VolChangeEventHandler =
                    Sub(TrackIndex As Integer, vol As Double)
                        Dim c24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(TrackIndex))
                        _ReaperTracksSendManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        c24Trk.Vpot.Value = vol
                        _ReaperTracksSendManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
        sh.ReaperSendVolChangeH = ReaperSendVolChangeH
        AddHandler _ReaperTracksSendManager._SendMonitor.On_VolChange, ReaperSendVolChangeH
        _VpotSendVolumeHandlers.Add(sh)



        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)
            If i < _ReaperTracksSendManager.Tracks.Count Then

                Dim reaperTrk = _ReaperTracksManager.Tracks(i)
                Dim send As ReaperSend = Nothing
                send = _ReaperTracksSendManager.Sends(i)




                If send Is Nothing Then
                    ClearSendChannelStripValues(c24Trk)
                    Continue For
                Else
                    'init values
                    If InitScribbleStripText Then
                        Dim _ReaperTrackSendsEditor = New Reaper.PageManagers.ReaperTrackSendsEditor(reaperTrk.TrackIndex, _ReaperProj.Tracks)
                        c24Trk.ScribbleStrip.Text = _ReaperTrackSendsEditor.Sends_Names(send.SendIndex)
                    End If
                End If

                Dim h As New SendVolumeHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


                Dim SendVolEnvelope As ReaperSendVolEnvelope = Nothing
                For Each _SendVolEnvelope In reaperTrk.Envelopes.AllOfType(Of ReaperSendVolEnvelope)
                    If _SendVolEnvelope.SendId = send.SendIndex Then
                        SendVolEnvelope = _SendVolEnvelope
                        Exit For
                    End If
                Next
                If SendVolEnvelope IsNot Nothing Then
                    _C24trkSendVolEnvelope.Add(c24Trk, SendVolEnvelope)

                    Dim SendVolEnvelopeVolChangeH As Reaper.ReaperSendVolEnvelope.On_SendUIVolChangeEventHandler =
                    Sub(_vol As Double)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        SendVolEnvelope.Suspend_events = True
                        c24Trk.Vpot.Value = _vol
                        SendVolEnvelope.Suspend_events = False
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
                    h.SendVolEnvelopeVolChangeH = SendVolEnvelopeVolChangeH
                    AddHandler SendVolEnvelope.On_SendUIVolChange, SendVolEnvelopeVolChangeH


                End If


                'init Vpot value
                c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.VolumeLinear
                c24Trk.Vpot.Value = send.Vol

                Dim VpotValueChangeH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                         Sub(TrackNb As Integer, Value As Double)

                             'Automation
                             If SendVolEnvelope IsNot Nothing Then
                                 SendVolEnvelope.Suspend_events = True
                             End If
                             ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                             Select Case reaperTrk.AutoMode
                                 Case 0, 1
                                     send.Vol = Value
                                 Case Else
                                     SendVolEnvelope = _C24trkSendVolEnvelope(c24Trk)
                                     If SendVolEnvelope IsNot Nothing Then
                                         _ReaperTracksManager.SuspendTrack(TrackNb - 1)
                                         SendVolEnvelope.Value = Value
                                         _ReaperTracksManager.ResumeTrack(TrackNb - 1)
                                     End If
                             End Select
                             If SendVolEnvelope IsNot Nothing Then
                                 SendVolEnvelope.Suspend_events = False
                             End If
                         End Sub
                h.VpotValueChangeH = VpotValueChangeH
                AddHandler c24Trk.Vpot.OnVpotValueChange, VpotValueChangeH


                _VpotSendVolumeHandlers.Add(h)
            Else
                ClearSendChannelStripValues(c24Trk)
            End If
        Next
        RaiseEvent FaderVpotSendLevelHandlers(_C24trkSendVolEnvelope, True)
    End Sub
    Sub RemoveVpotSendVolumeHandlers()

        For Each h In _VpotSendVolumeHandlers
            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.Vpot.OnVpotValueChange, h.VpotValueChangeH

                If h.SendVolEnvelope IsNot Nothing Then
                    RemoveHandler h.SendVolEnvelope.On_SendUIVolChange, h.SendVolEnvelopeVolChangeH
                End If
            End If


            If h.SendMonitor IsNot Nothing Then
                RemoveHandler h.SendMonitor.On_VolChange, h.ReaperSendVolChangeH
            End If

        Next

        _VpotSendVolumeHandlers.Clear()
    End Sub

    Sub AddVpotSendsVolumeHandlers(c24TrkList As List(Of C24_ChannelStrip), SendIndexList As List(Of Integer))
        RemoveVpotSendsVolumeHandlers()

        _C24trkSendVolEnvelope.Clear()
        Dim sh As New SendVolumeHandlers
        sh.SendsMonitor = _ReaperTrackSendsManager.Monitor
        Dim ReaperSendVolChangeH As Reaper.Monitors.ReaperTrackSendsMonitor.On_VolChangeEventHandler =
                    Sub(SendIndex As Integer, vol As Double)
                        Dim c24Trk = c24TrkList(SendIndex)
                        Dim PageSendIndex = _ReaperTrackSendsManager.GetRelativeSendIndex(SendIndex)
                        _ReaperTrackSendsManager.SuspendSend(PageSendIndex)
                        c24Trk.Vpot.Value = vol
                        _ReaperTrackSendsManager.ResumeSend(PageSendIndex)
                    End Sub
        sh.ReaperSendVolChangeH = ReaperSendVolChangeH
        AddHandler _ReaperTrackSendsManager.Monitor.On_VolChange, ReaperSendVolChangeH
        _VpotSendVolumeHandlers.Add(sh)



        For i = 0 To c24TrkList.Count - 1
            Dim c24Trk = c24TrkList(i)
            Dim PageSendIndex = i

            Dim reaperTrk = _ReaperProj.Tracks(_ReaperTrackSendsManager.TrackIndex)
            Dim send As ReaperSend = Nothing
            send = _ReaperTrackSendsManager.Sends(SendIndexList(i))


            If send Is Nothing Then
                c24Trk.Vpot.SetLedsOff()
                Continue For

            End If

            Dim h As New SendVolumeHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


            Dim SendVolEnvelope As ReaperSendVolEnvelope = Nothing
            For Each _SendVolEnvelope In reaperTrk.Envelopes.AllOfType(Of ReaperSendVolEnvelope)
                If _SendVolEnvelope.SendId = send.SendIndex Then
                    SendVolEnvelope = _SendVolEnvelope
                    Exit For
                End If
            Next
            If SendVolEnvelope IsNot Nothing Then
                _C24trkSendVolEnvelope.Add(c24Trk, SendVolEnvelope)

                Dim SendVolEnvelopeVolChangeH As Reaper.ReaperSendVolEnvelope.On_SendUIVolChangeEventHandler =
                    Sub(_vol As Double)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        SendVolEnvelope.Suspend_events = True
                        _ReaperTrackSendsManager.SuspendSend(PageSendIndex)
                        c24Trk.Vpot.Value = _vol
                        _ReaperTrackSendsManager.ResumeSend(PageSendIndex)
                        SendVolEnvelope.Suspend_events = False
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
                h.SendVolEnvelopeVolChangeH = SendVolEnvelopeVolChangeH
                AddHandler SendVolEnvelope.On_SendUIVolChange, SendVolEnvelopeVolChangeH


            End If


            'init Vpot value
            c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.VolumeLinear
            c24Trk.Vpot.Value = send.Vol

            Dim VpotValueChangeH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                         Sub(TrackNb As Integer, Value As Double)

                             'Automation
                             If SendVolEnvelope IsNot Nothing Then
                                 SendVolEnvelope.Suspend_events = True
                             End If
                             ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                             Select Case reaperTrk.AutoMode
                                 Case 0, 1
                                     send.Vol = Value
                                 Case Else
                                     SendVolEnvelope = _C24trkSendVolEnvelope(c24Trk)
                                     If SendVolEnvelope IsNot Nothing Then
                                         _ReaperTracksManager.SuspendTrack(TrackNb - 1)
                                         _ReaperTrackSendsManager.SuspendSend(PageSendIndex)
                                         SendVolEnvelope.Value = Value
                                         _ReaperTrackSendsManager.ResumeSend(PageSendIndex)
                                         _ReaperTracksManager.ResumeTrack(TrackNb - 1)
                                     End If
                             End Select
                             If SendVolEnvelope IsNot Nothing Then
                                 SendVolEnvelope.Suspend_events = False
                             End If
                         End Sub
            h.VpotValueChangeH = VpotValueChangeH
            AddHandler c24Trk.Vpot.OnVpotValueChange, VpotValueChangeH


            _VpotSendVolumeHandlers.Add(h)

        Next
        RaiseEvent FaderVpotSendLevelHandlers(_C24trkSendVolEnvelope, True)
    End Sub
    Sub RemoveVpotSendsVolumeHandlers()

        For Each h In _VpotSendVolumeHandlers
            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.Vpot.OnVpotValueChange, h.VpotValueChangeH

                If h.SendVolEnvelope IsNot Nothing Then
                    RemoveHandler h.SendVolEnvelope.On_SendUIVolChange, h.SendVolEnvelopeVolChangeH
                End If
            End If


            If h.SendsMonitor IsNot Nothing Then
                RemoveHandler h.SendsMonitor.On_VolChange, h.ReaperSendVolChangeH
            End If

        Next

        _VpotSendVolumeHandlers.Clear()
    End Sub

    Sub ClearSendChannelStripValues(c24Trk As C24_ChannelStrip)
        c24Trk.ScribbleStrip.Text = " -  "
        If Flip_Mode Then
            c24Trk.Fader.SetInitFaderDbVolume(-150)
            c24Trk.Mute.Led = False
            c24Trk.Solo.Led = False
        Else
            c24Trk.Vpot.SetLedsOff()
        End If
    End Sub

    Sub AddAutomationSendVolumeHandlers(C24trkVolEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackVolEnvelope), status As Boolean) Handles Me.FaderVpotVolHandlers
        IsFaderVpotVolHandlers = status
        _C24trkVolEnvelope = C24trkVolEnvelope
        RemoveAutomationVolumeHandlers()

        For Each c24Trk In C24trkVolEnvelope.Keys

            Dim TrackVolEnvelope = C24trkVolEnvelope(c24Trk)
            Dim reaperTrk = _ReaperProj.Tracks(TrackVolEnvelope.TrackIndex)

            Dim h As New AutomationVolumeHandlers
            h.reaperTrk = reaperTrk
            h.c24Trk = c24Trk

            h.env = TrackVolEnvelope
            TrackVolEnvelope.Refresh()

            If c24Trk IsNot Nothing Then
                _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
            End If

            If FaderAuto AndAlso status Then
                ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                Select Case reaperTrk.AutoMode
                    Case 1
                        TrackVolEnvelope.Active = True
                        TrackVolEnvelope.Armed = True


                        If c24Trk IsNot Nothing Then
                            If Flip_Mode = False Then
                                Dim AutovolChangeH As Reaper.ReaperTrackVolEnvelope.On_TrackUIVolChangeEventHandler =
                                Sub(_vol As Double)
                                    c24Trk.Fader.SetFaderDbVolume(Reaper.utility.SLIDER2DB(_vol))

                                End Sub
                                h.AutovolChangeH = AutovolChangeH
                                AddHandler TrackVolEnvelope.On_TrackUIVolChange, AutovolChangeH
                            Else 'flip mode 
                                Dim AutovolChangeH As Reaper.ReaperTrackVolEnvelope.On_TrackUIVolChangeEventHandler =
                                Sub(_vol As Double)

                                    c24Trk.Vpot.Value = _vol

                                End Sub
                                h.AutovolChangeH = AutovolChangeH
                                AddHandler TrackVolEnvelope.On_TrackUIVolChange, AutovolChangeH
                            End If

                        End If


                    Case 0
                        TrackVolEnvelope.Active = False
                        TrackVolEnvelope.Armed = False

                    Case Else
                        TrackVolEnvelope.Active = True
                        TrackVolEnvelope.Armed = True

                End Select
            Else
                TrackVolEnvelope.Active = False
                TrackVolEnvelope.Armed = False


            End If

            If c24Trk IsNot Nothing Then
                _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
            End If


            _AutomationVolumeHandlers.Add(h)
        Next

    End Sub
    Sub RemoveAutomationSendVolumeHandlers()

        For Each h In _AutomationVolumeHandlers
            If h.env IsNot Nothing Then
                RemoveHandler h.env.On_TrackUIVolChange, h.AutovolChangeH
            End If
        Next



        _AutomationVolumeHandlers.Clear()
    End Sub
#End Region

#Region "Send Mute"
    Sub AddSendMutesBtnHandlers()        'trouver un moyen d'ajouter des AddSendMutesBtnHandlers avec une seule c24Trk
        RemoveSendMutesBtnHandlers()
        _C24trkSendMuteEnvelope.Clear()

        Dim sh As New SendMuteHandlers
        sh.SendMonitor = _ReaperTracksSendManager._SendMonitor
        Dim ReaperSendMuteChangeH As Reaper.Monitors.ReaperSendMonitor.On_MuteChangeEventHandler =
                    Sub(TrackIndex As Integer, mute As Boolean)
                        Dim C24TrackIndex = _ReaperTracksManager.GetRelativeTrackIndex(TrackIndex)
                        Dim c24Trk = C24_Desk.ChannelStrips(C24TrackIndex)


                        _ReaperTracksSendManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        If Flip_Mode Then
                            c24Trk.Mute.Led = mute
                        Else
                            If SendMute_Mode Then
                                c24Trk.SwitchActive.Led = mute
                                c24Trk.ScribbleStrip.ShowTempText("Mute", If(mute, "  On", " Off"))
                            End If
                        End If

                        _ReaperTracksSendManager.ResumeTrack(c24Trk.TrackNumber - 1)


                    End Sub
        sh.ReaperSendMuteChangeH = ReaperSendMuteChangeH
        AddHandler _ReaperTracksSendManager._SendMonitor.On_MuteChange, ReaperSendMuteChangeH
        _SendMuteHandlers.Add(sh)

        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksSendManager.Tracks.Count Then

                Dim reaperTrk = _ReaperTracksManager.Tracks(i)
                Dim send As ReaperSend = Nothing
                send = _ReaperTracksSendManager.Sends(i)




                If send Is Nothing Then
                    Continue For

                End If

                Dim h As New SendMuteHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


                Dim SendMuteEnvelope As ReaperSendMuteEnvelope = Nothing
                For Each _SendMuteEnvelope In reaperTrk.Envelopes.AllOfType(Of ReaperSendMuteEnvelope)
                    If _SendMuteEnvelope.SendId = send.SendIndex Then
                        SendMuteEnvelope = _SendMuteEnvelope
                        Exit For
                    End If
                Next
                If SendMuteEnvelope IsNot Nothing Then
                    _C24trkSendMuteEnvelope.Add(c24Trk, SendMuteEnvelope)

                    Dim SendMuteEnvelopeMuteChangeH As Reaper.ReaperSendMuteEnvelope.On_SendUIMuteChangeEventHandler =
                    Sub(_mute As Boolean)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        SendMuteEnvelope.Suspend_events = True
                        If Flip_Mode Then
                            c24Trk.Mute.Led = _mute
                        Else
                            If SendMute_Mode Then
                                c24Trk.ScribbleStrip.ShowTempText("Mute", If(_mute, "  On", " Off"))
                                c24Trk.SwitchActive.Led = _mute
                            End If
                        End If
                        SendMuteEnvelope.Suspend_events = False
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
                    h.SendMuteEnvelopeMuteChangeH = SendMuteEnvelopeMuteChangeH
                    AddHandler SendMuteEnvelope.On_SendUIMuteChange, SendMuteEnvelopeMuteChangeH


                End If


                'init SendMutes Led 
                If Flip_Mode Then
                    c24Trk.Mute.Led = send.Mute
                Else
                    If SendMute_Mode Then
                        c24Trk.LedSwitchActive.Led = True
                        c24Trk.SwitchActive.Led = send.Mute
                    End If
                End If

                If Flip_Mode Then
                    Dim SendMuteChangeH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        Sub(TrackNb As Integer?)

                            'Automation
                            If SendMuteEnvelope IsNot Nothing Then
                                SendMuteEnvelope.Suspend_events = True
                            End If

                            send.Mute = Not send.Mute

                            ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                            Select Case reaperTrk.AutoMode

                                Case 0, 1

                                Case Else
                                    SendMuteEnvelope = _C24trkSendMuteEnvelope(c24Trk)
                                    If SendMuteEnvelope IsNot Nothing Then
                                        _ReaperTracksManager.SuspendTrack(TrackNb - 1)
                                        SendMuteEnvelope.Value = If(send.Mute, 1, 0)
                                        _ReaperTracksManager.ResumeTrack(TrackNb - 1)
                                    End If
                            End Select
                            If SendMuteEnvelope IsNot Nothing Then
                                SendMuteEnvelope.Suspend_events = False
                            End If
                        End Sub
                    h.SendMuteChangeH = SendMuteChangeH
                    AddHandler c24Trk.Mute.OnButtonReleased, SendMuteChangeH
                Else
                    If SendMute_Mode Then

                        Dim SendMuteChangeH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        Sub(TrackNb As Integer?)
                            send.Mute = Not send.Mute
                            c24Trk.ScribbleStrip.ShowTempText("Mute", If(send.Mute, "  On", " Off"))
                            'Automation
                            If SendMuteEnvelope IsNot Nothing Then
                                SendMuteEnvelope.Suspend_events = True
                            End If
                            ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                            Select Case reaperTrk.AutoMode

                                Case 0, 1

                                Case Else
                                    SendMuteEnvelope = _C24trkSendMuteEnvelope(c24Trk)
                                    If SendMuteEnvelope IsNot Nothing Then
                                        _ReaperTracksManager.SuspendTrack(TrackNb - 1)
                                        SendMuteEnvelope.Value = If(send.Mute, 1, 0)
                                        _ReaperTracksManager.ResumeTrack(TrackNb - 1)
                                    End If
                            End Select
                            If SendMuteEnvelope IsNot Nothing Then
                                SendMuteEnvelope.Suspend_events = False
                            End If
                        End Sub
                        h.SendMuteChangeH = SendMuteChangeH
                        AddHandler c24Trk.SwitchActive.OnButtonReleased, SendMuteChangeH

                    End If
                End If



                _SendMuteHandlers.Add(h)
            Else
                'ClearSendChannelStripValues(c24Trk)
            End If
        Next
        RaiseEvent SendMuteBtnHandlers(_C24trkSendMuteEnvelope, True)
    End Sub
    Sub RemoveSendMutesBtnHandlers()
        ClearMuteLeds()

        For Each h In _SendMuteHandlers

            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.SwitchActive.OnButtonReleased, h.SendMuteChangeH
                RemoveHandler h.C24Trk.Mute.OnButtonReleased, h.SendMuteChangeH

                If h.SendMuteEnvelope IsNot Nothing Then
                    RemoveHandler h.SendMuteEnvelope.On_SendUIMuteChange, h.SendMuteEnvelopeMuteChangeH
                End If
            End If


            If h.SendMonitor IsNot Nothing Then
                RemoveHandler h.SendMonitor.On_MuteChange, h.ReaperSendMuteChangeH
            End If


        Next
        _SendMuteHandlers.Clear()
    End Sub

    Sub AddSendsMutesBtnHandlers(c24TrkList As List(Of C24_ChannelStrip), SendIndexList As List(Of Integer))
        RemoveSendsMutesBtnHandlers()
        _C24trkSendMuteEnvelope.Clear()

        Dim sh As New SendMuteHandlers
        sh.SendsMonitor = _ReaperTrackSendsManager.Monitor
        Dim ReaperSendMuteChangeH As Reaper.Monitors.ReaperTrackSendsMonitor.On_MuteChangeEventHandler =
                    Sub(SendIndex As Integer, mute As Boolean)

                        Dim c24Trk = c24TrkList(SendIndex)
                        Dim PageSendIndex = _ReaperTrackSendsManager.GetRelativeSendIndex(SendIndex)

                        _ReaperTrackSendsManager.SuspendSend(PageSendIndex)

                        c24Trk.SwitchActive.Led = mute
                        c24Trk.ScribbleStrip.ShowTempText("Mute", If(mute, "  On", " Off"))


                        _ReaperTrackSendsManager.ResumeSend(PageSendIndex)


                    End Sub
        sh.ReaperSendMuteChangeH = ReaperSendMuteChangeH
        AddHandler _ReaperTrackSendsManager.Monitor.On_MuteChange, ReaperSendMuteChangeH
        _SendMuteHandlers.Add(sh)

        For i = 0 To c24TrkList.Count - 1
            Dim c24Trk = c24TrkList(i)
            Dim PageSendIndex = i

            Dim reaperTrk = _ReaperProj.Tracks(_ReaperTrackSendsManager.TrackIndex)
            Dim send As ReaperSend = Nothing
            send = _ReaperTrackSendsManager.Sends(SendIndexList(i))


            If send Is Nothing Then
                Continue For

            End If

            Dim h As New SendMuteHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


            Dim SendMuteEnvelope As ReaperSendMuteEnvelope = Nothing
            For Each _SendMuteEnvelope In reaperTrk.Envelopes.AllOfType(Of ReaperSendMuteEnvelope)
                If _SendMuteEnvelope.SendId = send.SendIndex Then
                    SendMuteEnvelope = _SendMuteEnvelope
                    Exit For
                End If
            Next
            If SendMuteEnvelope IsNot Nothing Then
                _C24trkSendMuteEnvelope.Add(c24Trk, SendMuteEnvelope)

                Dim SendMuteEnvelopeMuteChangeH As Reaper.ReaperSendMuteEnvelope.On_SendUIMuteChangeEventHandler =
                    Sub(_mute As Boolean)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        SendMuteEnvelope.Suspend_events = True
                        _ReaperTrackSendsManager.SuspendSend(PageSendIndex)

                        c24Trk.ScribbleStrip.ShowTempText("Mute", If(_mute, "  On", " Off"))
                        c24Trk.SwitchActive.Led = _mute

                        _ReaperTrackSendsManager.ResumeSend(PageSendIndex)
                        SendMuteEnvelope.Suspend_events = False
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
                h.SendMuteEnvelopeMuteChangeH = SendMuteEnvelopeMuteChangeH
                AddHandler SendMuteEnvelope.On_SendUIMuteChange, SendMuteEnvelopeMuteChangeH


            End If


            'init SendMutes Led 

            c24Trk.LedSwitchActive.Led = True
            c24Trk.SwitchActive.Led = send.Mute

            Dim SendMuteChangeH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        Sub(TrackNb As Integer?)
                            send.Mute = Not send.Mute
                            c24Trk.ScribbleStrip.ShowTempText("Mute", If(send.Mute, "  On", " Off"))
                            'Automation
                            If SendMuteEnvelope IsNot Nothing Then
                                SendMuteEnvelope.Suspend_events = True
                            End If
                            ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                            Select Case reaperTrk.AutoMode

                                Case 0, 1

                                Case Else
                                    SendMuteEnvelope = _C24trkSendMuteEnvelope(c24Trk)
                                    If SendMuteEnvelope IsNot Nothing Then
                                        _ReaperTracksManager.SuspendTrack(TrackNb - 1)
                                        _ReaperTrackSendsManager.SuspendSend(PageSendIndex)

                                        SendMuteEnvelope.Value = If(send.Mute, 1, 0)

                                        _ReaperTrackSendsManager.ResumeSend(PageSendIndex)
                                        _ReaperTracksManager.ResumeTrack(TrackNb - 1)
                                    End If
                            End Select
                            If SendMuteEnvelope IsNot Nothing Then
                                SendMuteEnvelope.Suspend_events = False
                            End If
                        End Sub
            h.SendMuteChangeH = SendMuteChangeH
            AddHandler c24Trk.SwitchActive.OnButtonReleased, SendMuteChangeH



            _SendMuteHandlers.Add(h)

        Next
        RaiseEvent SendMuteBtnHandlers(_C24trkSendMuteEnvelope, True)
    End Sub
    Sub RemoveSendsMutesBtnHandlers()
        ClearMuteLeds()

        For Each h In _SendMuteHandlers

            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.SwitchActive.OnButtonReleased, h.SendMuteChangeH


                If h.SendMuteEnvelope IsNot Nothing Then
                    RemoveHandler h.SendMuteEnvelope.On_SendUIMuteChange, h.SendMuteEnvelopeMuteChangeH
                End If
            End If


            If h.SendsMonitor IsNot Nothing Then
                RemoveHandler h.SendsMonitor.On_MuteChange, h.ReaperSendMuteChangeH
            End If


        Next
        _SendMuteHandlers.Clear()
    End Sub

    Sub AddAutomationSendMuteHandlers(C24trkSendMuteEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperSendMuteEnvelope), status As Boolean) Handles Me.SendMuteBtnHandlers
        _C24trkSendMuteEnvelope = C24trkSendMuteEnvelope
        IsSendMuteBtnHandlers = status
        RemoveAutomationSendMuteHandlers()

        For Each C24Trk In C24trkSendMuteEnvelope.Keys
            Dim SendMuteEnvelope = C24trkSendMuteEnvelope(C24Trk)

            Dim h As New AutomationSendMuteHandlers

            h.c24Trk = C24Trk
            h.SendMuteEnvelope = SendMuteEnvelope
            SendMuteEnvelope.Refresh()
            Dim reaperTrk = _ReaperProj.Tracks(SendMuteEnvelope.TrackIndex)

            _ReaperTracksManager.SuspendTrack(C24Trk.TrackNumber - 1)


            If SendMuteAuto AndAlso IsSendMuteBtnHandlers Then
                ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                Select Case reaperTrk.AutoMode
                    Case 1
                        SendMuteEnvelope.Active = True
                        SendMuteEnvelope.Armed = True


                        If C24Trk IsNot Nothing Then



                            If Flip_Mode = False Then
                                Dim AutoSendMuteChangeH As Reaper.ReaperSendMuteEnvelope.On_SendUIMuteChangeEventHandler =
                                Sub(_mute As Boolean)
                                    If C24_Desk.Send_Mute.Led Then
                                        C24Trk.SwitchActive.Led = _mute
                                    End If

                                End Sub
                                h.AutoSendMuteChangeH = AutoSendMuteChangeH
                                AddHandler SendMuteEnvelope.On_SendUIMuteChange, AutoSendMuteChangeH
                            Else 'flip mode 
                                Dim AutoSendMuteChangeH As Reaper.ReaperSendMuteEnvelope.On_SendUIMuteChangeEventHandler =
                                Sub(_mute As Boolean)

                                    C24Trk.Mute.Led = _mute

                                End Sub
                                h.AutoSendMuteChangeH = AutoSendMuteChangeH
                                AddHandler SendMuteEnvelope.On_SendUIMuteChange, AutoSendMuteChangeH

                            End If



                        End If


                    Case 0
                        SendMuteEnvelope.Active = False
                        SendMuteEnvelope.Armed = False

                    Case Else
                        SendMuteEnvelope.Active = True
                        SendMuteEnvelope.Armed = True

                End Select
            Else
                SendMuteEnvelope.Active = False
                SendMuteEnvelope.Armed = False


            End If


            _ReaperTracksManager.ResumeTrack(C24Trk.TrackNumber - 1)



            _AutomationSendMuteHandlers.Add(h)

        Next



    End Sub
    Sub RemoveAutomationSendMuteHandlers()
        For Each h In _AutomationSendMuteHandlers
            RemoveHandler h.SendMuteEnvelope.On_SendUIMuteChange, h.AutoSendMuteChangeH

        Next
        _AutomationSendLevelHandlers.Clear()
    End Sub
#End Region
#Region "Send PrePost"
    Sub AddSendPrePostBtnHandlers()
        RemoveSendPrePostBtnHandlers()


        Dim sh As New SendPrePostHandlers
        sh.SendMonitor = _ReaperTracksSendManager._SendMonitor
        Dim ReaperSendPrePostChangeH As Reaper.Monitors.ReaperSendMonitor.On_SendModeChangeEventHandler =
                    Sub(TrackIndex As Integer, sendMode As Integer)
                        Dim c24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(TrackIndex))
                        _ReaperTracksSendManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        If PrePost_Mode Then
                            If Controler_Utility.GetC24SendMode(sendMode) = C24SendMode.Post Then

                                c24Trk.SwitchActive.Led = True
                            Else

                                c24Trk.SwitchActive.Led = False
                            End If
                        End If
                        _ReaperTracksSendManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
        sh.ReaperSendPrePostChangeH = ReaperSendPrePostChangeH
        AddHandler _ReaperTracksSendManager._SendMonitor.On_SendModeChange, ReaperSendPrePostChangeH
        _SendPrePostHandlers.Add(sh)

        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksSendManager.Tracks.Count Then

                Dim reaperTrk = _ReaperTracksManager.Tracks(i)
                Dim send As ReaperSend = Nothing
                send = _ReaperTracksSendManager.Sends(i)




                If send Is Nothing Then
                    Continue For

                End If

                Dim h As New SendPrePostHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


                'init prepost Led

                If PrePost_Mode Then
                    c24Trk.LedSwitchActive.Led = True
                    If Controler_Utility.GetC24SendMode(send.SendMode) = C24SendMode.Post Then

                        c24Trk.SwitchActive.Led = True
                    Else

                        c24Trk.SwitchActive.Led = False
                    End If
                End If



                Dim SendPrePostChangeH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        Sub(TrackNb As Integer?)

                            _ReaperTracksManager.SuspendTrack(TrackNb - 1)
                            If Controler_Utility.GetC24SendMode(send.SendMode) = C24SendMode.Pre Then
                                'set Post
                                send.SendMode = 0
                                c24Trk.ScribbleStrip.ShowTempText("Post")
                                c24Trk.SwitchActive.Led = True
                            Else
                                'set Pre
                                send.SendMode = 3
                                c24Trk.ScribbleStrip.ShowTempText("Pre")
                                c24Trk.SwitchActive.Led = False
                            End If
                            _ReaperTracksManager.ResumeTrack(TrackNb - 1)


                        End Sub
                h.SendPrePostChangeH = SendPrePostChangeH
                AddHandler c24Trk.SwitchActive.OnButtonReleased, SendPrePostChangeH

                _SendPrePostHandlers.Add(h)
            Else
                'ClearSendChannelStripValues(c24Trk)

            End If
        Next

    End Sub
    Sub RemoveSendPrePostBtnHandlers()


        For Each h In _SendPrePostHandlers

            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.SwitchActive.OnButtonReleased, h.SendPrePostChangeH

            End If


            If h.SendMonitor IsNot Nothing Then
                RemoveHandler h.SendMonitor.On_SendModeChange, h.ReaperSendPrePostChangeH
            End If


        Next
        _SendPrePostHandlers.Clear()
    End Sub

#End Region

#Region "Solo"
    Sub AddSolosHandlers()
        RemoveSolosHandlers()

        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)
            c24Trk.Fader.Fader_Mode = C24_Fader.Mode.Db
            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)



                Dim h As New SolosHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


                Dim TrackIndex = reaperTrk.TrackIndex
                If _Solo_Mode = Solo_Mode.Solo Then

                    Dim soloBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                         Sub(trackNb As Integer?)
                             SoloTracks(TrackIndex) = Not SoloTracks(TrackIndex)
                             If SoloTracks(TrackIndex) = False AndAlso SoloSafeTracks(TrackIndex) = True Then
                                 _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.SafeSoloInPlace)
                             ElseIf SoloTracks(TrackIndex) = False AndAlso SoloSafeTracks(TrackIndex) = False Then
                                 _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.Off)
                             ElseIf SoloTracks(TrackIndex) = True Then
                                 _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.SoloInPlace)
                             End If
                             SetSoloLed()
                             RefreshSoloClear()
                         End Sub


                    h.SoloBtnH = soloBtnH
                    AddHandler c24Trk.Solo.OnButtonReleased, soloBtnH

                Else 'SoloSafe Mode

                    Dim soloBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                         Sub(trackNb As Integer?)
                             SoloSafeTracks(TrackIndex) = Not SoloSafeTracks(TrackIndex)

                             If SoloSafeTracks(TrackIndex) = True Then

                                 _ReaperTracksManager.Set_SoloDefeat(trackNb - 1, True)

                             Else
                                 If SoloTracks(TrackIndex) Then
                                     _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.SoloInPlace)
                                 Else
                                     _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.Off)
                                 End If


                                 _ReaperTracksManager.Set_SoloDefeat(trackNb - 1, False)
                             End If
                             SetSoloLed()

                         End Sub
                    h.SoloBtnH = soloBtnH
                    AddHandler c24Trk.Solo.OnButtonReleased, soloBtnH

                End If



                _SolosHandlers.Add(h)
            End If
        Next
        SetSoloLed()
        RefreshSoloClear()

    End Sub
    Sub RemoveSolosHandlers()
        For Each h In _SolosHandlers
            RemoveHandler h.C24Trk.Solo.OnButtonReleased, h.SoloBtnH

        Next
        _SolosHandlers.Clear()
    End Sub


    Sub AddReaperSoloHandlers()
        RemoveReaperSoloHandlers()

        For Each reaperTrk In _ReaperProj.Tracks

            Dim h As New ReaperTrackSoloHandlers
            h.ReaperTrk = reaperTrk

            Dim ReaperSoloChangeH As ReaperTrack.On_SoloChangeEventHandler =
                    Sub(solo As Integer)

                        Select Case GetC24Solo(solo)
                            Case C24_SoloTypes.Solo
                                SoloTracks(reaperTrk.TrackIndex) = True
                            Case C24_SoloTypes.NotSolo
                                SoloTracks(reaperTrk.TrackIndex) = False
                        End Select
                        SetSoloLed()
                        RefreshSoloClear()
                    End Sub
            h.ReaperSoloChangeH = ReaperSoloChangeH
            AddHandler reaperTrk.On_SoloChange, ReaperSoloChangeH

            Dim ReaperSoloDefeatChangeH As Reaper.ReaperTrack.On_SoloDefeatChangeEventHandler =
                    Sub(_soloDefeat As Boolean)
                        SoloSafeTracks(reaperTrk.TrackIndex) = _soloDefeat

                        SetSoloLed()
                        RefreshSoloSafe()

                    End Sub
            h.ReaperSoloDefeatChangeH = ReaperSoloDefeatChangeH
            AddHandler reaperTrk.On_SoloDefeatChange, ReaperSoloDefeatChangeH

            _ReaperTrackSoloHandlers.Add(h)

        Next

    End Sub
    Sub RemoveReaperSoloHandlers()
        For Each h In _ReaperTrackSoloHandlers
            RemoveHandler h.ReaperTrk.On_SoloChange, h.ReaperSoloChangeH
            RemoveHandler h.ReaperTrk.On_SoloDefeatChange, h.ReaperSoloDefeatChangeH
        Next
        _ReaperTrackSoloHandlers.Clear()
    End Sub

    Sub SetSoloLed()


        For i = 0 To 23

            Dim c24Trk = C24_Desk.ChannelStrips(i)
            If i < _ReaperTracksManager.Tracks.Count Then

                Dim reaperTrk = _ReaperTracksManager.Tracks(i)

                Select Case _Solo_Mode
                    Case Solo_Mode.Solo
                        c24Trk.Solo.IsBlinking = False
                        If SoloTracks(reaperTrk.TrackIndex) Then
                            c24Trk.Solo.Led = True
                        End If
                    Case Solo_Mode.SoloSafe
                        If SoloSafeTracks(reaperTrk.TrackIndex) Then
                            c24Trk.Solo.IsBlinking = True
                        Else
                            c24Trk.Solo.IsBlinking = False
                        End If

                End Select

            Else
                c24Trk.Solo.IsBlinking = False
            End If


        Next

        SetMutesLedFromSoloState()
    End Sub
    Sub SetMutesLedFromSoloState()


        Dim isOneSolo = SoloTracks.Values.Contains(True)
        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)
            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)


                If reaperTrk.Mute Then
                    c24Trk.Mute.IsBlinking = False
                    c24Trk.Mute.Led = True
                Else
                    If isOneSolo Then
                        If c24Trk.Solo.Led = False Then
                            c24Trk.Mute.IsBlinking = True
                        Else
                            c24Trk.Mute.IsBlinking = False
                        End If

                    Else
                        c24Trk.Mute.IsBlinking = False
                    End If
                End If

            Else
                c24Trk.Mute.IsBlinking = False
            End If

        Next


    End Sub

    Sub ClearSoloLeds()
        For i = 0 To 23
            Dim cs = C24_Desk.ChannelStrips(i)
            cs.Solo.IsBlinking = False
        Next
    End Sub
#End Region
#Region "Counter Peaks"

    ''' <summary>
    ''' Déclenché toutes les 50 ms par <see cref="_PeakTimer"/>.
    ''' Garde une exécution unique à la fois — ignore le tick si le précédent
    ''' n'est pas terminé (<c>Interlocked.CompareExchange</c> = pas de lock).
    ''' </summary>
    Private Sub OnPeakTimerElapsed(sender As Object, e As System.Timers.ElapsedEventArgs)
        If Threading.Interlocked.CompareExchange(_peakTimerBusy, 1, 0) <> 0 Then Return
        Try

            If _ReaperTracksManager Is Nothing Then Return
            If _ReaperPeakLevelManager Is Nothing Then Return

            Dim Peaks = _ReaperPeakLevelManager.Refresh()

            'Display tracks Peaks
            For i = 0 To _ReaperTracksManager.Tracks.Count - 1
                Dim ReaperTrk_Index = _ReaperTracksManager.Tracks(i).TrackIndex
                Dim C24_VueMeter = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(ReaperTrk_Index)).VueMeter
                'If Peaks.GetTrackHoldDB(ReaperTrk_Index) Then
                C24_VueMeter.Hold_Value_Left = Peaks.GetTrackHoldDB(ReaperTrk_Index).L
                C24_VueMeter.Hold_Value_Right = Peaks.GetTrackHoldDB(ReaperTrk_Index).R
                'End If
                ' If Peaks.TrackPeaks.ContainsKey(ReaperTrk_Index) Then
                C24_VueMeter.Value_Left = Peaks.GetTrackPeakDB(ReaperTrk_Index).L
                C24_VueMeter.Value_Right = Peaks.GetTrackPeakDB(ReaperTrk_Index).R
                ' End If

            Next

            'Display Master Peaks

            C24_Desk.VueMeterMaster1.Hold_Value_Left = Peaks.MasterHoldDB.L
            C24_Desk.VueMeterMaster1.Hold_Value_Right = Peaks.MasterHoldDB.R
            C24_Desk.VueMeterMaster1.Value_Left = Peaks.MasterDB.L
            C24_Desk.VueMeterMaster1.Value_Right = Peaks.MasterDB.R

            'Display Clock

            Dim rawPos As Double = Peaks.PlayPosition
            If rawPos < 0 Then
                C24_Desk.Clock.Clock_Value = "--------"
            Else
                r += 1
                C24_Desk.Clock.Set_Time_Ms(CLng(rawPos * 1000))
                'If Math.IEEERemainder(r, 20) = 0 Then
                '    'Console.WriteLine(Peaks.TrackPeaks(0).L & "|" & Peaks.TrackHolds(0).L)
                '    'Console.WriteLine(C24_Desk.Clock.Clock_Value)
                'End If

            End If

        Finally
            Threading.Interlocked.Exchange(_peakTimerBusy, 0)
        End Try
    End Sub
    Sub ClearTracksPeakLevels()
        'toutes les pistes 
        For i = 0 To 23
            Dim C24_trk = C24_Desk.ChannelStrips(i)
            C24_trk.VueMeter.Hold_Value_Left = C24_trk.VueMeter.Min_Value
            C24_trk.VueMeter.Hold_Value_Right = C24_trk.VueMeter.Min_Value
            C24_trk.VueMeter.Value_Left = C24_trk.VueMeter.Min_Value
            C24_trk.VueMeter.Value_Right = C24_trk.VueMeter.Min_Value


        Next
    End Sub
    ''' <summary>
    ''' Branche le bouton <see cref="C24_Desk.Counter_Mode"/> pour faire défiler
    ''' les modes d'horloge (<see cref="Commands.ClockModes"/>) sur le <see cref="Hardware.C24_Clock"/>.
    ''' Chaque appui passe au mode suivant dans le cycle :
    ''' HourMinuteSecondMS → HourMinuteSecondFrames → FeetFrames → BarBeats → HourMinuteSecondMS…
    ''' La LED reste allumée tant que le mode n'est pas <see cref="Commands.ClockModes.HourMinuteSecondMS"/>.
    ''' </summary>
    Sub AddCounterModeHandler()
        RemoveCounterModeHandler()

        Dim h As New CounterModeHandlers

        Dim counterModeBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub(trackNb As Integer?)
                Dim currentMode As Commands.ClockModes = _C24_Desk.Clock.ClockMode
                Dim currentIndex As Integer = Array.IndexOf(CounterModesCycle, currentMode)
                Dim nextIndex As Integer = (currentIndex + 1) Mod CounterModesCycle.Length
                Dim nextMode As Commands.ClockModes = CounterModesCycle(nextIndex)
                If nextMode = ClockModes.BarBeats Then
                    Dim bpm As Integer = Reaper.GetBpm
                    Dim beatperbar As Integer = Reaper.GetBeatsPerBar
                    _C24_Desk.Clock.bpm = bpm
                    _C24_Desk.Clock.beatsPerBar = beatperbar
                End If



                _C24_Desk.Clock.ClockMode = nextMode


            End Sub

        h.CounterModeBtnH = counterModeBtnH
        AddHandler _C24_Desk.Counter_Mode.OnButtonReleased, counterModeBtnH
        _CounterModeHandlers.Add(h)

        'init first mode :
        C24_Desk.Clock.ClockMode = ClockModes.HourMinuteSecondMS

    End Sub
    ''' <summary>Supprime le handler du bouton Counter Mode.</summary>
    Public Sub RemoveCounterModeHandler()
        For Each h In _CounterModeHandlers
            RemoveHandler _C24_Desk.Counter_Mode.OnButtonReleased, h.CounterModeBtnH
        Next
        _CounterModeHandlers.Clear()
    End Sub




#End Region
#Region "Solo Clear Safe"
    Sub AddSoloClearSafeC24Handlers()
        RemoveSolosClearSafeHandlers()

        'init
        SetSoloLed()
        RefreshSoloClear()

        Dim h As New SolosClearSafeHandlers
        'Solo Clear
        Dim soloClearBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub(trackNb As Integer?)
                If C24_Desk.SoloClear.Led Then
                    _ReaperTracksManager.TrackMonitor.Suspend = True

                    For Each ReaperTrk In _ReaperProj.Tracks
                        If SoloSafeTracks(ReaperTrk.TrackIndex) = True Then
                            _ReaperProj.Tracks(ReaperTrk.TrackIndex).Solo = Reaper.SoloState.SafeSoloInPlace
                        Else
                            _ReaperProj.Tracks(ReaperTrk.TrackIndex).Solo = Reaper.SoloState.Off
                        End If
                        If SoloTracks.ContainsKey(ReaperTrk.TrackIndex) Then
                            SoloTracks(ReaperTrk.TrackIndex) = False
                        End If
                    Next


                    _ReaperTracksManager.TrackMonitor.Suspend = False
                    SetSoloLed()
                    RefreshSoloClear()


                End If


            End Sub
        h.SoloClearBtnH = soloClearBtnH
        AddHandler C24_Desk.SoloClear.OnButtonReleased, soloClearBtnH

        'Solo Safe

        Dim soloSafeBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub(trackNb As Integer?)

                If _Solo_Mode = Solo_Mode.Solo Then
                    _Solo_Mode = Solo_Mode.SoloSafe
                    C24_Desk.SoloSafe.IsBlinking = True

                Else
                    _Solo_Mode = Solo_Mode.Solo
                    C24_Desk.SoloSafe.IsBlinking = False
                    RefreshSoloSafe()
                End If

                AddSolosHandlers()


            End Sub
        h.SoloSafeBtnH = soloSafeBtnH
        AddHandler C24_Desk.SoloSafe.OnButtonReleased, soloSafeBtnH

        _SolosClearSafeHandlers.Add(h)
    End Sub
    Enum Solo_Mode
        Solo
        SoloSafe
    End Enum


    Public Sub RemoveSolosClearSafeHandlers()
        C24_Desk.SoloClear.IsBlinking = False
        C24_Desk.SoloSafe.IsBlinking = False
        For Each h In _SolosClearSafeHandlers
            RemoveHandler C24_Desk.SoloClear.OnButtonReleased, h.SoloClearBtnH
            RemoveHandler C24_Desk.SoloSafe.OnButtonReleased, h.SoloSafeBtnH
        Next
        _SolosClearSafeHandlers.Clear()
    End Sub
    Sub RefreshDics(ReaperProj As Reaper.ReaperProject)
        SoloTracks = New Dictionary(Of Integer, Boolean)
        SoloSafeTracks = New Dictionary(Of Integer, Boolean)
        Names = New Dictionary(Of Integer, String)
        For Each trk In ReaperProj.Tracks
            SoloTracks.Add(trk.TrackIndex, If(GetC24Solo(trk.Solo) = C24_SoloTypes.Solo, True, False))
            SoloSafeTracks.Add(trk.TrackIndex, trk.SoloDefeat)
            Names.Add(trk.TrackIndex, trk.Name)
        Next

    End Sub

    Function GetC24Solo(ReaperSolo As Integer) As C24_SoloTypes
        ''' <summary>I_SOLO — 0=off 1=solo 2=solo-in-place 5=safe 6=safe-in-place</summary>
        Select Case ReaperSolo
            Case 0
                Return C24_SoloTypes.NotSolo
            Case Else
                Return C24_SoloTypes.Solo
        End Select
        Return C24_SoloTypes.NotSolo
    End Function

    Function GetReaperSolo(C24Solo As C24_SoloTypes) As Integer
        Select Case C24Solo
            Case C24_SoloTypes.NotSolo
                Return 0
            Case C24_SoloTypes.Solo
                Return 2
            Case C24_SoloTypes.SoloSafe
                Return 6
        End Select
        Return 0
    End Function

    Sub MuteLedChange(track_index As Integer, Mute As Boolean)
        Dim c24Trk = C24_Desk.ChannelStrips(track_index)
        If SoloTracks.Values.Contains(True) AndAlso SoloSafeTracks(track_index) = False Then
            If Mute Then
                c24Trk.Mute.IsBlinking = False
                c24Trk.Mute.Led = True
            Else
                c24Trk.Mute.IsBlinking = True
            End If
        Else
            c24Trk.Mute.IsBlinking = False
            c24Trk.Mute.Led = Mute
        End If
    End Sub


    Sub RefreshSoloSafe()
        C24_Desk.SoloSafe.Led = SoloSafeTracks.Values.Contains(True)
    End Sub

    Sub RefreshSoloClear()
        C24_Desk.SoloClear.Led = SoloTracks.Values.Contains(True)
    End Sub



#End Region
#Region "Banks"
    Sub AddBankC24Handlers()
        RemoveBankHandlers()
        'Bank Navigation
        Dim h As New BankHandlers

        Dim BankLeftBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If _ReaperTracksManager.IsPageLeftExist Then
                    _ReaperTracksManager.Set_Prev_Page()

                    RefreshTrackManagerPage()
                    If ActualC24ViewMode = C24ViewModes.Sends Then
                        RefreshC24SendsViewMode()
                    Else
                        RefreshC24HomeViewMode()
                    End If
                End If
            End Sub
        h.BankLeftBtnH = BankLeftBtnH
        AddHandler C24_Desk.Bank_Left.OnButtonReleased, BankLeftBtnH

        Dim BankRightBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If _ReaperTracksManager.IsPageRightExist Then
                    _ReaperTracksManager.Set_Next_Page()

                    RefreshTrackManagerPage()
                    If ActualC24ViewMode = C24ViewModes.Sends Then
                        RefreshC24SendsViewMode()
                    Else
                        RefreshC24HomeViewMode()
                    End If

                End If
            End Sub
        h.BankRightBtnH = BankRightBtnH
        AddHandler C24_Desk.Bank_Right.OnButtonReleased, BankRightBtnH

        _BankHandlers.Add(h)
    End Sub

    ''' <summary>Supprime tous les handlers Banks du C24.</summary>
    Public Sub RemoveBankHandlers()
        For Each h In _BankHandlers
            RemoveHandler C24_Desk.Bank_Left.OnButtonReleased, h.BankLeftBtnH
            RemoveHandler C24_Desk.Bank_Right.OnButtonReleased, h.BankRightBtnH
            RemoveHandler C24_Desk.Nudge.OnButtonReleased, h.BankNudgeBtnH
        Next
        _BankHandlers.Clear()
    End Sub
    ' ── Reconstruction au changement de page ──────────────────────────
    Sub RefreshTrackManagerPage()
        If _ReaperTracksManager Is Nothing Then Return
        If _ReaperTracksManager.Tracks Is Nothing OrElse _ReaperTracksManager.Tracks.Count = 0 Then
            For i As Integer = 0 To 23
                ClearChannelStripValues(C24_Desk.ChannelStrips(i))
            Next
            Return
        End If



        _ReaperPeakLevelManager.ClearWatched()
        _ReaperPeakLevelManager.WatchTracks(_ReaperTracksManager.Tracks)
        _ReaperPeakLevelManager.WatchTrack(-1)
        ClearTracksPeakLevels()

        RemoveCommonStripHandlers()
        AddCommonStripHandlers()
        'RefreshAutomationMode()

        AddFadersVolumeHandlers()
        AddMutesBtnHandlers()
        RemoveSolosHandlers()
        AddSolosHandlers()


    End Sub

#End Region
#Region "Transport"


    Sub AddTransportHandlers()
        RemoveTransportHandlers()
        Dim h As New TransportHandlers

        ' ── PlayState change (Reaper → C24) ───────────────────────────────────
        Dim PlayStateChangeH As ReaperTransportManager.On_PlayStateChangeEventHandler =
            Sub(PlayState As Integer)
                _ActualPlayState = PlayState
                RefreshC24_TransportBtnLed(PlayState)
            End Sub
        h.PlayStateChangeH = PlayStateChangeH
        AddHandler _ReaperTransportManager.On_PlayStateChange, PlayStateChangeH

        ' ── Rec ──────────────────────────────────────────────────────────────
        Dim RecBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                SetReaperPlayState(C24TransportBtn.Rec)
            End Sub
        h.RecBtnH = RecBtnH
        AddHandler C24_Desk.Rec.OnButtonReleased, RecBtnH
        ' ── Play ──────────────────────────────────────────────────────────────
        Dim PlayBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                SetReaperPlayState(C24TransportBtn.Play)
            End Sub
        h.PlayBtnH = PlayBtnH
        AddHandler C24_Desk.Play.OnButtonReleased, PlayBtnH

        ' ── Stop ──────────────────────────────────────────────────────────────
        Dim StopBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                SetReaperPlayState(C24TransportBtn.Stop)
            End Sub
        h.StopBtnH = StopBtnH
        AddHandler C24_Desk.Stop.OnButtonReleased, StopBtnH

        ' ── Fwd : Press → appel immédiat + timer 500 ms ───────────────────────
        _fwdTimer = New System.Timers.Timer(500) With {.AutoReset = True}
        AddHandler _fwdTimer.Elapsed,
            Sub(s As Object, ev As System.Timers.ElapsedEventArgs)
                SetReaperPlayState(C24TransportBtn.Fwd)
            End Sub

        Dim FwdBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                C24_Desk.Fwd.Led = True
                SetReaperPlayState(C24TransportBtn.Fwd)
                _fwdTimer.Start()
            End Sub
        h.FwdBtnH = FwdBtnH
        AddHandler C24_Desk.Fwd.OnButtonPressed, FwdBtnH

        Dim FwdBtnReleasedH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                _fwdTimer.Stop()
                C24_Desk.Fwd.Led = False
            End Sub
        h.FwdBtnReleasedH = FwdBtnReleasedH
        AddHandler C24_Desk.Fwd.OnButtonReleased, FwdBtnReleasedH

        ' ── Rwd : Press → appel immédiat + timer 500 ms ───────────────────────
        _rwdTimer = New System.Timers.Timer(500) With {.AutoReset = True}
        AddHandler _rwdTimer.Elapsed,
            Sub(s As Object, ev As System.Timers.ElapsedEventArgs)
                SetReaperPlayState(C24TransportBtn.Rwd)
            End Sub

        Dim RwdBtnPressedH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                C24_Desk.Rew.Led = True
                SetReaperPlayState(C24TransportBtn.Rwd)
                _rwdTimer.Start()
            End Sub
        h.RwdBtnPressedH = RwdBtnPressedH
        AddHandler C24_Desk.Rew.OnButtonPressed, RwdBtnPressedH

        Dim RwdBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                _rwdTimer.Stop()
                C24_Desk.Rew.Led = False
            End Sub
        h.RwdBtnH = RwdBtnH
        AddHandler C24_Desk.Rew.OnButtonReleased, RwdBtnH

        _TransportHandlers.Add(h)
    End Sub

    Private Enum C24TransportBtn
        Play
        [Stop]
        Fwd
        Rwd
        Rec
        Talk
    End Enum
    Private Sub SetReaperPlayState(_C24TransportBtn As C24TransportBtn)
        Dim Set_ReaperPlayState As Integer? = Nothing
        '0=stop  1=play  2=pause  4=rec  5=rec+play 6=pause+rec
        Select Case _C24TransportBtn
            Case C24TransportBtn.Play
                Select Case ActualPlayState
                    Case 0
                        Set_ReaperPlayState = 1
                    Case 1
                        Set_ReaperPlayState = 2
                    Case 2
                        Set_ReaperPlayState = 1
                    Case 4
                        Set_ReaperPlayState = 5
                    Case 5
                        Set_ReaperPlayState = 6
                    Case 6
                        Set_ReaperPlayState = 5
                End Select
            Case C24TransportBtn.Stop
                Set_ReaperPlayState = 0
            Case C24TransportBtn.Rec
                Select Case ActualPlayState
                    Case 0
                        Set_ReaperPlayState = 4
                    Case 1
                        Set_ReaperPlayState = 5
                    Case 2
                        Set_ReaperPlayState = 6
                    Case 4
                        Set_ReaperPlayState = 4
                    Case 5
                        Set_ReaperPlayState = 1
                    Case 6
                        Set_ReaperPlayState = 2
                End Select
            Case C24TransportBtn.Fwd

                _ReaperTransportManager.FastForward()

            Case C24TransportBtn.Rwd
                _ReaperTransportManager.Rewind()

        End Select
        If Set_ReaperPlayState IsNot Nothing Then
            _ReaperTransportManager.SuspendMonitor()
            Reaper.utility.SetReaperPlayState(Set_ReaperPlayState)
            RefreshC24_TransportBtnLed(Set_ReaperPlayState)
            _ReaperTransportManager.ResumeMonitor()
        End If


    End Sub
    Private Sub RefreshC24_TransportBtnLed(PlayState As Integer)
        C24_Desk.Play.IsBlinking = False
        C24_Desk.Stop.IsBlinking = False
        C24_Desk.Rew.IsBlinking = False
        C24_Desk.Fwd.IsBlinking = False
        C24_Desk.Rec.IsBlinking = False
        C24_Desk.Talk_Back.IsBlinking = False
        '0=stop  1=play  2=pause  4=rec  5=rec+play 6=pause+rec
        Select Case PlayState
            Case 0

                C24_Desk.Stop.Led = True
            Case 1

                C24_Desk.Play.Led = True
            Case 2

                C24_Desk.Play.IsBlinking = True
            Case 4

                C24_Desk.Rec.IsBlinking = True
            Case 5

                C24_Desk.Rec.Led = True
                C24_Desk.Play.Led = True
            Case 6

                C24_Desk.Rec.IsBlinking = True
                C24_Desk.Play.IsBlinking = True

        End Select
    End Sub
    Public Sub RemoveTransportHandlers()
        If _fwdTimer IsNot Nothing Then
            _fwdTimer.Stop()
            _fwdTimer.Dispose()
            _fwdTimer = Nothing
        End If
        If _rwdTimer IsNot Nothing Then
            _rwdTimer.Stop()
            _rwdTimer.Dispose()
            _rwdTimer = Nothing
        End If

        For Each h In _TransportHandlers
            RemoveHandler _ReaperTransportManager.On_PlayStateChange, h.PlayStateChangeH
            RemoveHandler C24_Desk.Rec.OnButtonReleased, h.RecBtnH
            RemoveHandler C24_Desk.Play.OnButtonReleased, h.PlayBtnH
            RemoveHandler C24_Desk.Stop.OnButtonReleased, h.StopBtnH
            RemoveHandler C24_Desk.Fwd.OnButtonPressed, h.FwdBtnH
            RemoveHandler C24_Desk.Fwd.OnButtonReleased, h.FwdBtnReleasedH
            RemoveHandler C24_Desk.Rew.OnButtonPressed, h.RwdBtnPressedH
            RemoveHandler C24_Desk.Rew.OnButtonReleased, h.RwdBtnH
        Next
        _TransportHandlers.Clear()

        '' ── Stopper et libérer le manager ─────────────────────────────────────
        'If _ReaperTransportManager IsNot Nothing Then
        '    _ReaperTransportManager.Stop()
        '    _ReaperTransportManager.Dispose()
        '    _ReaperTransportManager = Nothing
        'End If
    End Sub


#End Region
#Region "Automation"



    Function GetC24AutoMode(ReaperAutoMode As Integer) As Commands.AutoMode
        Select Case ReaperAutoMode
            Case 0
                Return Commands.AutoMode.None
            Case 1
                Return Commands.AutoMode.Read
            Case 2
                Return Commands.AutoMode.Touch
            Case 3
                Return Commands.AutoMode.Write
            Case 4
                Return Commands.AutoMode.Latch
        End Select
        Return Commands.AutoMode.None
    End Function

    Sub On_AutoModeChange(TrackNb As Integer, AutoMode As Control24.Commands.AutoMode)
        Dim c24Trk = _C24_Desk.ChannelStrips(TrackNb - 1)
        c24Trk.AutoModeLed.IsBlinking = False
        Select Case AutoMode
            Case Commands.AutoMode.None
                _ReaperTracksManager.SetAutoMode(TrackNb - 1, 0)
                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.None
            Case Commands.AutoMode.Trim
                _ReaperTracksManager.SetAutoMode(TrackNb - 1, 0)
                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Trim
            Case Commands.AutoMode.Read
                _ReaperTracksManager.SetAutoMode(TrackNb - 1, 1)
                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Read
            Case Commands.AutoMode.Touch
                _ReaperTracksManager.SetAutoMode(TrackNb - 1, 2)
                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Touch
            Case Commands.AutoMode.Write
                _ReaperTracksManager.SetAutoMode(TrackNb - 1, 3)
                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Write
                c24Trk.AutoModeLed.IsBlinking = True
            Case Commands.AutoMode.Latch
                _ReaperTracksManager.SetAutoMode(TrackNb - 1, 4)
                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Latch
        End Select
        RefreshAutomationModeLeds()
    End Sub
    Sub On_AutoEnableChange(EnableBtn As C24_LedButton)
        Dim status As Boolean = False
        If EnableBtn.IsBlinking OrElse EnableBtn.Led Then
            status = True
        End If

        Select Case EnableBtn.Name
            Case "Auto_Fader"
                For Each ReaperTrk In _ReaperProj.Tracks
                    For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperTrackVolEnvelope)
                        env.Armed = status
                    Next
                Next
            Case "Auto_Pan"
                For Each ReaperTrk In _ReaperProj.Tracks
                    For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperTrackPanEnvelope)
                        env.Armed = status
                    Next
                Next
            Case "Auto_Mute"
                For Each ReaperTrk In _ReaperProj.Tracks
                    For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperTrackMuteEnvelope)
                        env.Armed = status
                    Next
                Next
            Case "Auto_Send_Level"
                For Each ReaperTrk In _ReaperProj.Tracks
                    For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperSendVolEnvelope)
                        env.Armed = status
                    Next
                Next
            Case "Auto_Send_Mute"
                For Each ReaperTrk In _ReaperProj.Tracks
                    For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperSendMuteEnvelope)
                        env.Armed = status
                    Next
                Next
            Case "Auto_Plug_In"
                For Each ReaperTrk In _ReaperProj.Tracks
                    For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperFxParamEnvelope)
                        env.Armed = status
                    Next
                Next
        End Select
    End Sub
    Sub RefreshAutomationModeLeds()

        C24_Desk.Auto_Write.IsBlinking = False
        C24_Desk.Auto_Touch.IsBlinking = False
        C24_Desk.Auto_Latch.IsBlinking = False
        C24_Desk.Auto_Trim.IsBlinking = False
        C24_Desk.Auto_Read.IsBlinking = False
        C24_Desk.Auto_Off.IsBlinking = False



        For Each ReaperTrk In _ReaperProj.Tracks
            'I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch<
            If ReaperTrk.AutoMode = 3 Then C24_Desk.Auto_Write.IsBlinking = True
            If ReaperTrk.AutoMode = 2 Then C24_Desk.Auto_Touch.Led = True
            If ReaperTrk.AutoMode = 4 Then C24_Desk.Auto_Latch.Led = True
            'If ReaperTrk.AutoMode = 0 Then C24_Desk.Auto_Trim.Led = True
            If ReaperTrk.AutoMode = 1 Then C24_Desk.Auto_Read.Led = True
            If ReaperTrk.AutoMode = 0 Then C24_Desk.Auto_Off.Led = True
        Next

    End Sub
    Sub RefreshAutomationFaderEnableLed()



        C24_Desk.Auto_Fader.IsBlinking = False


        For Each ReaperTrk In _ReaperProj.Tracks
            For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperTrackVolEnvelope)
                If env.Armed Then
                    If C24_Desk.Auto_Write.IsBlinking = True Then
                        C24_Desk.Auto_Fader.IsBlinking = True
                    Else
                        C24_Desk.Auto_Fader.IsBlinking = False
                        C24_Desk.Auto_Fader.Led = True
                    End If
                    Return
                End If
            Next
        Next



    End Sub

    Sub AddAutomationEnableHandlers()
        RemoveAutomationEnableHandlers()
        Dim h As New AutomationEnableHandlers
        Dim Auto_FaderBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If C24_Desk.Auto_Write.IsBlinking Then
                    C24_Desk.Auto_Fader.IsBlinking = Not C24_Desk.Auto_Fader.IsBlinking
                Else
                    C24_Desk.Auto_Fader.Led = Not C24_Desk.Auto_Fader.Led
                End If

                If C24_Desk.Auto_Fader.Led OrElse C24_Desk.Auto_Fader.IsBlinking Then
                    FaderAuto = True
                Else
                    FaderAuto = False
                End If
                For Each ReaperTrk In _ReaperProj.Tracks
                    AddAutomationVolumeHandlers(_C24trkVolEnvelope, IsFaderVpotVolHandlers)
                Next
            End Sub
        h.Auto_FaderBtnH = Auto_FaderBtnH
        AddHandler C24_Desk.Auto_Fader.OnButtonReleased, Auto_FaderBtnH

        Dim Auto_PanBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If C24_Desk.Auto_Write.IsBlinking Then
                    C24_Desk.Auto_Pan.IsBlinking = Not C24_Desk.Auto_Pan.IsBlinking
                Else
                    C24_Desk.Auto_Pan.Led = Not C24_Desk.Auto_Pan.Led
                End If
                If C24_Desk.Auto_Pan.Led OrElse C24_Desk.Auto_Pan.IsBlinking Then
                    PanAuto = True
                Else
                    PanAuto = False
                End If
                For Each ReaperTrk In _ReaperProj.Tracks
                    AddAutomationPanHandlers(_C24trkPanEnvelope, IsFaderVpotPanHandlers)
                Next
            End Sub
        h.Auto_PanBtnH = Auto_PanBtnH
        AddHandler C24_Desk.Auto_Pan.OnButtonReleased, Auto_PanBtnH

        Dim Auto_MuteBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If C24_Desk.Auto_Write.IsBlinking Then
                    C24_Desk.Auto_Mute.IsBlinking = Not C24_Desk.Auto_Mute.IsBlinking
                Else
                    C24_Desk.Auto_Mute.Led = Not C24_Desk.Auto_Mute.Led
                End If
                If C24_Desk.Auto_Mute.Led OrElse C24_Desk.Auto_Mute.IsBlinking Then
                    MuteAuto = True
                Else
                    MuteAuto = False
                End If
                For Each ReaperTrk In _ReaperProj.Tracks
                    AddAutomationMuteHandlers(_C24trkMuteEnvelope, IsMuteBtnHandlers)
                Next
            End Sub
        h.Auto_MuteBtnH = Auto_MuteBtnH
        AddHandler C24_Desk.Auto_Mute.OnButtonReleased, Auto_MuteBtnH

        Dim Auto_Send_LevelBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If C24_Desk.Auto_Write.IsBlinking Then
                    C24_Desk.Auto_Send_Level.IsBlinking = Not C24_Desk.Auto_Send_Level.IsBlinking
                Else
                    C24_Desk.Auto_Send_Level.Led = Not C24_Desk.Auto_Send_Level.Led
                End If
                If C24_Desk.Auto_Send_Level.Led OrElse C24_Desk.Auto_Send_Level.IsBlinking Then
                    SendLevelAuto = True
                Else
                    SendLevelAuto = False
                End If
                For Each ReaperTrk In _ReaperProj.Tracks
                    AddAutomationSendLevelHandlers(_C24trkSendVolEnvelope, IsFaderVpotSendLevelHandlers)
                Next
            End Sub
        h.Auto_Send_LevelBtnH = Auto_Send_LevelBtnH
        AddHandler C24_Desk.Auto_Send_Level.OnButtonReleased, Auto_Send_LevelBtnH

        Dim Auto_Send_MuteBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If C24_Desk.Auto_Write.IsBlinking Then
                    C24_Desk.Auto_Send_Mute.IsBlinking = Not C24_Desk.Auto_Send_Mute.IsBlinking
                Else
                    C24_Desk.Auto_Send_Mute.Led = Not C24_Desk.Auto_Send_Mute.Led
                End If
                For Each ReaperTrk In _ReaperProj.Tracks
                    AddAutomationSendMuteHandlers(_C24trkSendMuteEnvelope, IsSendMuteBtnHandlers)
                Next
            End Sub
        h.Auto_Send_MuteBtnH = Auto_Send_MuteBtnH
        AddHandler C24_Desk.Auto_Send_Mute.OnButtonReleased, Auto_Send_MuteBtnH

        Dim Auto_Plug_InBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If C24_Desk.Auto_Write.IsBlinking Then
                    C24_Desk.Auto_Plug_In.IsBlinking = Not C24_Desk.Auto_Plug_In.IsBlinking
                Else
                    C24_Desk.Auto_Plug_In.Led = Not C24_Desk.Auto_Plug_In.Led
                End If
            End Sub
        For Each ReaperTrk In _ReaperProj.Tracks
            AddAutomationPluginHandlers(_C24trkPluginParamEnvelope, IsFaderVpotPluginParamHandlers)
        Next

        h.Auto_Plug_InBtnH = Auto_Plug_InBtnH
        AddHandler C24_Desk.Auto_Plug_In.OnButtonReleased, Auto_Plug_InBtnH




        _AutomationEnableHandlers.Add(h)
    End Sub
    Sub RemoveAutomationEnableHandlers()
        For Each h In _AutomationEnableHandlers
            RemoveHandler C24_Desk.Auto_Fader.OnButtonReleased, h.Auto_FaderBtnH
            RemoveHandler C24_Desk.Auto_Pan.OnButtonReleased, h.Auto_PanBtnH
            RemoveHandler C24_Desk.Auto_Mute.OnButtonReleased, h.Auto_MuteBtnH
            RemoveHandler C24_Desk.Auto_Send_Level.OnButtonReleased, h.Auto_Send_LevelBtnH
            RemoveHandler C24_Desk.Auto_Send_Mute.OnButtonReleased, h.Auto_Send_MuteBtnH
            RemoveHandler C24_Desk.Auto_Plug_In.OnButtonReleased, h.Auto_Plug_InBtnH
        Next
        _AutomationEnableHandlers.Clear()
    End Sub

    Sub AddReaperEnvArmedHandlers()
        RemoveReaperEnvArmedHandlers()

        For Each Trk In _ReaperProj.Tracks
            For Each env In Trk.Envelopes.Envelopes
                Dim h As New ReaperEnvArmedHandlers
                h.ReaperTrk = Trk
                h.env = env
                Dim ReaperEnvArmChangeH As ReaperTrackEnvelope.On_ArmedChangeEventHandler =
                    Sub(_armed As Boolean)
                        RefreshAutomationFaderEnableLed()
                    End Sub
                h.ReaperEnvArmChangeH = ReaperEnvArmChangeH
                AddHandler env.On_ArmedChange, ReaperEnvArmChangeH
                _ReaperEnvArmedHandlers.Add(h)
            Next
        Next





    End Sub
    Sub RemoveReaperEnvArmedHandlers()
        For Each h In _ReaperEnvArmedHandlers
            RemoveHandler h.env.On_ArmedChange, h.ReaperEnvArmChangeH
        Next
        _ReaperEnvArmedHandlers.Clear()
    End Sub
    Sub AddReaperAutoModeHandlers()
        RemoveReaperAutoModeHandlers()

        For Each Trk In _ReaperProj.Tracks

            Dim h As New ReaperAutoModeHandler
            h.ReaperTrk = Trk

            Dim c24Trk As C24_ChannelStrip = Nothing
            For Each pageTrk In _ReaperTracksManager.Tracks
                If pageTrk.TrackIndex = Trk.TrackIndex Then
                    c24Trk = C24_Desk.ChannelStrips(pageTrk.TrackNumber - 1)
                    c24Trk.AutoModeLed.AutoMode = GetC24AutoMode(Trk.AutoMode)
                End If

            Next




            AddAutomationVolumeHandlers(_C24trkVolEnvelope, IsFaderVpotVolHandlers)
            AddAutomationPanHandlers(_C24trkPanEnvelope, IsFaderVpotPanHandlers)
            AddAutomationMuteHandlers(_C24trkMuteEnvelope, IsMuteBtnHandlers)
            AddAutomationSendLevelHandlers(_C24trkSendVolEnvelope, IsFaderVpotSendLevelHandlers)
            AddAutomationSendMuteHandlers(_C24trkSendMuteEnvelope, IsSendMuteBtnHandlers)
            AddAutomationPluginHandlers(_C24trkPluginParamEnvelope, IsFaderVpotPluginParamHandlers)

            RefreshAutomationModeLeds()
            RefreshAutomationFaderEnableLed()


            Dim ReaperAutoModeChangeH As ReaperTrack.On_AutoModeChangeEventHandler =
                Sub(_autoMode As Integer)
                    Dim _c24Trk As C24_ChannelStrip = Nothing
                    For Each pageTrk In _ReaperTracksManager.Tracks
                        If pageTrk.TrackIndex = Trk.TrackIndex Then
                            _c24Trk = C24_Desk.ChannelStrips(pageTrk.TrackNumber - 1)
                        End If

                    Next

                    If _c24Trk IsNot Nothing Then
                        Select Case _autoMode
                            Case 0
                                _c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.None
                            Case 1
                                _c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Read
                            Case 2
                                _c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Touch
                            Case 3
                                _c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Write
                            Case 4
                                _c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Latch
                        End Select

                        'automation
                        AddAutomationVolumeHandlers(_C24trkVolEnvelope, IsFaderVpotVolHandlers)
                        AddAutomationPanHandlers(_C24trkPanEnvelope, IsFaderVpotPanHandlers)
                        AddAutomationMuteHandlers(_C24trkMuteEnvelope, IsMuteBtnHandlers)
                        AddAutomationSendLevelHandlers(_C24trkSendVolEnvelope, IsFaderVpotSendLevelHandlers)
                        AddAutomationSendMuteHandlers(_C24trkSendMuteEnvelope, IsSendMuteBtnHandlers)
                        AddAutomationPluginHandlers(_C24trkPluginParamEnvelope, IsFaderVpotPluginParamHandlers)

                    End If


                    RefreshAutomationModeLeds()
                    RefreshAutomationFaderEnableLed()


                End Sub
            h.ReaperAutoModeChangeH = ReaperAutoModeChangeH
            AddHandler Trk.On_AutoModeChange, ReaperAutoModeChangeH
            _ReaperAutoModeHandlers.Add(h)

        Next
    End Sub
    Sub RemoveReaperAutoModeHandlers()
        For Each h In _ReaperAutoModeHandlers
            RemoveHandler h.ReaperTrk.On_AutoModeChange, h.ReaperAutoModeChangeH
        Next
        _ReaperAutoModeHandlers.Clear()
    End Sub
#End Region
#Region "Show"
    Sub AddShowC24Handlers()
        RemoveShowC24Handlers()
        'Show and velocity C24 buttons
        Dim h As New ShowHandlers

        Dim Vel_Sens_EncodersBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If C24_Desk.Vel_Sens_Encoders.Led Then
                    C24_Desk.Vel_Sens_Encoders.Led = False
                    C24_Desk.ScreenLine.ShowTempText("Velocity sensible :  Off")
                Else
                    C24_Desk.ScreenLine.ShowTempText("Velocity sensible :   On")
                    C24_Desk.Vel_Sens_Encoders.Led = True
                End If
                For Each cs In C24_Desk.ChannelStrips
                    cs.Vpot.Velocity_sensible = C24_Desk.Vel_Sens_Encoders.Led
                Next


            End Sub
        h.Vel_Sens_EncodersBtnH = Vel_Sens_EncodersBtnH
        AddHandler C24_Desk.Vel_Sens_Encoders.OnButtonReleased, Vel_Sens_EncodersBtnH

        Dim Show_ValuesBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)

                C24_Desk.Show_Values.Led = True
                Dim cs0 = C24_Desk.ChannelStrips(0)
                h.cs0 = cs0
                Dim ScribbleScriptBackToOriginal0H As Hardware.C24_Vpot.OnScribbleScriptBackToOriginalTextEventHandler =
                            Sub()
                                C24_Desk.Show_Values.Led = False
                                C24_Desk.Show_Channel_Names.Led = False
                                RemoveHandler cs0.Vpot.OnScribbleScriptBackToOriginalText, ScribbleScriptBackToOriginal0H
                            End Sub
                h.ScribbleScriptBackToOriginal0H = ScribbleScriptBackToOriginal0H
                AddHandler cs0.Vpot.OnScribbleScriptBackToOriginalText, ScribbleScriptBackToOriginal0H


                For Each cs In C24_Desk.ChannelStrips
                    If cs.Vpot.LedMode <> C24_Vpot.LedModeType.off Then
                        cs.Vpot.RefreshScribbleStrip(cs.Vpot.Value)
                    End If
                Next


            End Sub
        h.Show_ValuesBtnH = Show_ValuesBtnH
        AddHandler C24_Desk.Show_Values.OnButtonReleased, Show_ValuesBtnH



        Dim Show_NamesBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
           Sub(TrackNb As Integer?)

               C24_Desk.Show_Channel_Names.Led = True
               Dim cs0 = C24_Desk.ChannelStrips(0)
               h.cs0 = cs0
               Dim ScribbleScriptBackToOriginal1H As Hardware.C24_ScribbleStrip.OnOriginaltextBackEventHandler =
                            Sub()
                                C24_Desk.Show_Values.Led = False
                                C24_Desk.Show_Channel_Names.Led = False
                                RemoveHandler cs0.ScribbleStrip.OnOriginaltextBack, ScribbleScriptBackToOriginal1H
                            End Sub
               h.ScribbleScriptBackToOriginal1H = ScribbleScriptBackToOriginal1H
               AddHandler cs0.ScribbleStrip.OnOriginaltextBack, ScribbleScriptBackToOriginal1H


               For Each cs In C24_Desk.ChannelStrips
                   Dim trk_index = _ReaperTracksManager.GetAbsoluteTrackIndex(cs.TrackNumber - 1)
                   If trk_index <> -1 Then
                       cs.ScribbleStrip.ShowTempText(_ReaperTracksManager.Tracks(trk_index).Name)
                   Else
                       cs.ScribbleStrip.ShowTempText("    ")
                   End If

               Next


           End Sub
        h.Show_NamesBtnH = Show_NamesBtnH
        AddHandler C24_Desk.Show_Channel_Names.OnButtonReleased, Show_NamesBtnH

        _ShowHandlers.Add(h)

    End Sub
    Sub RemoveShowC24Handlers()
        For Each h In _ShowHandlers

            RemoveHandler C24_Desk.Vel_Sens_Encoders.OnButtonReleased, h.Vel_Sens_EncodersBtnH
            RemoveHandler C24_Desk.Show_Values.OnButtonReleased, h.Show_ValuesBtnH
            If h.cs0 IsNot Nothing Then
                RemoveHandler h.cs0.Vpot.OnScribbleScriptBackToOriginalText, h.ScribbleScriptBackToOriginal0H
                RemoveHandler h.cs0.ScribbleStrip.OnOriginaltextBack, h.ScribbleScriptBackToOriginal1H
            End If

        Next
        _ShowHandlers.Clear()
    End Sub
#End Region
#Region "Modifiers"
    Sub AddModifiersHandlers()
        RemoveModifiersHandlers()
        Dim h As New ModifiersHandlers

        Dim OptPressBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                Opt = True
            End Sub
        h.OptPressBtnH = OptPressBtnH
        AddHandler C24_Desk.Opt.OnButtonPressed, OptPressBtnH

        Dim OptRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Opt = False
            End Sub
        h.OptRealBtnH = OptRealBtnH
        AddHandler C24_Desk.Opt.OnButtonReleased, OptRealBtnH


        Dim CtlPressBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                Ctl = True
            End Sub
        h.CtlPressBtnH = CtlPressBtnH
        AddHandler C24_Desk.Ctl_Clutch.OnButtonPressed, CtlPressBtnH

        Dim CtlRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Ctl = False
            End Sub
        h.CtlRealBtnH = CtlRealBtnH
        AddHandler C24_Desk.Ctl_Clutch.OnButtonReleased, CtlRealBtnH

        Dim CtrlPressBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                Ctrl = True
            End Sub
        h.CtrlPressBtnH = CtrlPressBtnH
        AddHandler C24_Desk.Ctrl.OnButtonPressed, CtrlPressBtnH

        Dim CtrlRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Ctrl = False
            End Sub
        h.CtrlRealBtnH = CtrlRealBtnH
        AddHandler C24_Desk.Ctrl.OnButtonReleased, CtrlRealBtnH

        Dim ShiftPressBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                Shift = True
            End Sub
        h.ShiftPressBtnH = ShiftPressBtnH
        AddHandler C24_Desk.Shift.OnButtonPressed, ShiftPressBtnH

        Dim ShiftRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Shift = False
            End Sub
        h.ShiftRealBtnH = ShiftRealBtnH
        AddHandler C24_Desk.Shift.OnButtonReleased, ShiftRealBtnH


        _ModifiersHandlers.Add(h)
    End Sub
    Sub RemoveModifiersHandlers()
        For Each h In _ModifiersHandlers
            RemoveHandler C24_Desk.Opt.OnButtonPressed, h.OptPressBtnH
            RemoveHandler C24_Desk.Opt.OnButtonReleased, h.OptRealBtnH
            RemoveHandler C24_Desk.Ctl_Clutch.OnButtonPressed, h.CtlPressBtnH
            RemoveHandler C24_Desk.Ctl_Clutch.OnButtonReleased, h.CtlRealBtnH
            RemoveHandler C24_Desk.Ctrl.OnButtonPressed, h.CtrlPressBtnH
            RemoveHandler C24_Desk.Ctrl.OnButtonReleased, h.CtrlRealBtnH
            RemoveHandler C24_Desk.Shift.OnButtonPressed, h.ShiftPressBtnH
            RemoveHandler C24_Desk.Shift.OnButtonReleased, h.ShiftRealBtnH
        Next
    End Sub
#End Region
#Region "SendLetters"
    Sub AddSendsLetterHandlers()
        RemoveSendsLetterHandlers()
        Dim h As New SendsLetterHandlers

        Dim A_FPressBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Shift Then
                    If ActualSendIndex = 5 Then
                        ActualSendIndex = -1
                        Flip_Mode = False
                        RefreshC24HomeViewMode()
                    Else
                        SetSendLetterLed("F")
                        ActualSendIndex = 5
                        RefreshC24SendsViewMode()
                    End If

                Else
                    If ActualSendIndex = 0 Then
                        ActualSendIndex = -1
                        Flip_Mode = False
                        RefreshC24HomeViewMode()
                    Else
                        SetSendLetterLed("A")
                        ActualSendIndex = 0
                        RefreshC24SendsViewMode()
                    End If

                End If

            End Sub
        h.A_FPressBtnH = A_FPressBtnH
        AddHandler C24_Desk.Send_A_F.OnButtonReleased, A_FPressBtnH

        Dim B_GPressBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Shift Then
                    If ActualSendIndex = 6 Then
                        ActualSendIndex = -1
                        Flip_Mode = False
                        RefreshC24HomeViewMode()
                    Else
                        SetSendLetterLed("G")
                        ActualSendIndex = 6
                        RefreshC24SendsViewMode()
                    End If

                Else
                    If ActualSendIndex = 1 Then
                        ActualSendIndex = -1
                        Flip_Mode = False
                        RefreshC24HomeViewMode()
                    Else
                        SetSendLetterLed("B")
                        ActualSendIndex = 1
                        RefreshC24SendsViewMode()
                    End If

                End If

            End Sub
        h.B_GPressBtnH = B_GPressBtnH
        AddHandler C24_Desk.Send_B_G.OnButtonReleased, B_GPressBtnH


        Dim C_HPressBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Shift Then
                    If ActualSendIndex = 7 Then
                        ActualSendIndex = -1
                        Flip_Mode = False
                        RefreshC24HomeViewMode()
                    Else
                        SetSendLetterLed("H")
                        ActualSendIndex = 7
                        RefreshC24SendsViewMode()
                    End If

                Else
                    If ActualSendIndex = 2 Then
                        ActualSendIndex = -1
                        Flip_Mode = False
                        RefreshC24HomeViewMode()
                    Else
                        SetSendLetterLed("C")
                        ActualSendIndex = 2
                        RefreshC24SendsViewMode()
                    End If

                End If

            End Sub
        h.C_HPressBtnH = C_HPressBtnH
        AddHandler C24_Desk.Send_C_H.OnButtonReleased, C_HPressBtnH

        Dim D_IPressBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Shift Then
                    If ActualSendIndex = 8 Then
                        ActualSendIndex = -1
                        Flip_Mode = False
                        RefreshC24HomeViewMode()
                    Else
                        SetSendLetterLed("I")
                        ActualSendIndex = 8
                        RefreshC24SendsViewMode()
                    End If

                Else
                    If ActualSendIndex = 3 Then
                        ActualSendIndex = -1
                        Flip_Mode = False
                        RefreshC24HomeViewMode()
                    Else
                        SetSendLetterLed("D")
                        ActualSendIndex = 3
                        RefreshC24SendsViewMode()
                    End If

                End If

            End Sub
        h.D_IPressBtnH = D_IPressBtnH
        AddHandler C24_Desk.Send_D_I.OnButtonReleased, D_IPressBtnH

        Dim E_JRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Shift Then
                    If ActualSendIndex = 9 Then
                        ActualSendIndex = -1
                        Flip_Mode = False
                        RefreshC24HomeViewMode()
                    Else
                        SetSendLetterLed("J")
                        ActualSendIndex = 9
                        RefreshC24SendsViewMode()
                    End If

                Else
                    If ActualSendIndex = 4 Then
                        ActualSendIndex = -1
                        Flip_Mode = False
                        RefreshC24HomeViewMode()
                    Else
                        SetSendLetterLed("E")
                        ActualSendIndex = 4
                        RefreshC24SendsViewMode()
                    End If

                End If

            End Sub
        h.E_JRealBtnH = E_JRealBtnH
        AddHandler C24_Desk.Send_E_J.OnButtonReleased, E_JRealBtnH

        _SendsLetterHandlers.Add(h)
    End Sub
    Sub RemoveSendsLetterHandlers()
        For Each h In _SendsLetterHandlers
            RemoveHandler C24_Desk.Send_A_F.OnButtonPressed, h.A_FPressBtnH
            RemoveHandler C24_Desk.Send_B_G.OnButtonPressed, h.B_GPressBtnH
            RemoveHandler C24_Desk.Send_C_H.OnButtonPressed, h.C_HPressBtnH
            RemoveHandler C24_Desk.Send_D_I.OnButtonPressed, h.D_IPressBtnH
            RemoveHandler C24_Desk.Send_E_J.OnButtonPressed, h.E_JRealBtnH
        Next
    End Sub
    Sub AddAutomationSendLevelHandlers(C24trkSendVolEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperSendVolEnvelope), status As Boolean) Handles Me.FaderVpotSendLevelHandlers
        _C24trkSendVolEnvelope = C24trkSendVolEnvelope
        IsFaderVpotSendLevelHandlers = status
        RemoveAutomationSendLevelHandlers()

        For Each C24Trk In C24trkSendVolEnvelope.Keys
            Dim SendVolEnvelope = C24trkSendVolEnvelope(C24Trk)

            Dim h As New AutomationSendLevelHandlers

            h.c24Trk = C24Trk
            h.SendVolEnvelope = SendVolEnvelope
            SendVolEnvelope.Refresh()
            Dim reaperTrk = _ReaperProj.Tracks(SendVolEnvelope.TrackIndex)

            _ReaperTracksManager.SuspendTrack(C24Trk.TrackNumber - 1)


            If SendLevelAuto AndAlso IsFaderVpotSendLevelHandlers Then
                ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                Select Case reaperTrk.AutoMode
                    Case 1
                        SendVolEnvelope.Active = True
                        SendVolEnvelope.Armed = True


                        If C24Trk IsNot Nothing Then



                            If Flip_Mode = False Then
                                Dim AutoSendVolumeChangeH As Reaper.ReaperSendVolEnvelope.On_ValueChangeEventHandler =
                                Sub(_value As Double)
                                    C24Trk.Fader.SetFaderDbVolume(Reaper.utility.SLIDER2DB(_value))

                                End Sub
                                h.AutoSendVolumeChangeH = AutoSendVolumeChangeH
                                AddHandler SendVolEnvelope.On_ValueChange, AutoSendVolumeChangeH
                            Else 'flip mode 
                                Dim AutoSendVolumeChangeH As Reaper.ReaperSendVolEnvelope.On_ValueChangeEventHandler =
                                Sub(_value As Double)

                                    C24Trk.Vpot.Value = _value

                                End Sub
                                h.AutoSendVolumeChangeH = AutoSendVolumeChangeH
                                AddHandler SendVolEnvelope.On_ValueChange, AutoSendVolumeChangeH
                            End If



                        End If


                    Case 0
                        SendVolEnvelope.Active = False
                        SendVolEnvelope.Armed = False

                    Case Else
                        SendVolEnvelope.Active = True
                        SendVolEnvelope.Armed = True

                End Select
            Else
                SendVolEnvelope.Active = False
                SendVolEnvelope.Armed = False


            End If


            _ReaperTracksManager.ResumeTrack(C24Trk.TrackNumber - 1)



            _AutomationSendLevelHandlers.Add(h)

        Next



    End Sub
    Sub RemoveAutomationSendLevelHandlers()
        For Each h In _AutomationSendLevelHandlers
            RemoveHandler h.SendVolEnvelope.On_ValueChange, h.AutoSendVolumeChangeH
            RemoveHandler h.SendVolEnvelope.On_ValueChange, h.AutoSendVolumeChangeH
        Next
        _AutomationSendLevelHandlers.Clear()
    End Sub
#End Region

#Region "Views"
    Public Enum C24ViewModes
        None
        Home
        PanSend
        SendX
        PanSend_Pan
        PanSend_Send
        Insert
        FxParam
        Sends
    End Enum

#Region "Refresh View Modes"
    Sub RefreshC24HomeViewMode()
        'HOME View
        ActualC24ViewMode = C24ViewModes.Home
        RemoveViewsStripHandlers()
        C24_Desk.ScreenChannelBarScroll.SetText("L<>RPan ")
        AddHomeStripHandlers()
        AllScribbleScriptBlinking(False)
    End Sub
    Sub RefreshC24PanSendViewMode(TrackNb As Integer, Optional SendPageNb As Integer = 1)
        'PAN SEND View
        ActualC24ViewMode = C24ViewModes.PanSend
        RemoveViewsStripHandlers()
        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        C24_Desk.ScreenChannelBarScroll.SetText(reaperTrk.Name)
        AddPanSendStripHandlers(TrackNb, SendPageNb)
        AllScribbleScriptBlinking(False)

    End Sub
    Sub RefreshC24PanSend_PanViewMode(TrackNb As Integer, Optional SendPageNb As Integer = 1)
        'PANSEND/Pan View
        ActualC24ViewMode = C24ViewModes.PanSend_Pan
        RemoveViewsStripHandlers()
        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        C24_Desk.ScreenChannelBarScroll.SetText(reaperTrk.Name)
        AddPanSend_PanStripHandlers(TrackNb, SendPageNb)
        AllScribbleScriptBlinking(False)

    End Sub
    Sub RefreshC24PanSend_SendViewMode(TrackNb As Integer, Optional SendPageNb As Integer = 1)
        'PANSEND/Send View
        ActualC24ViewMode = C24ViewModes.PanSend_Send
        RemoveViewsStripHandlers()
        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        C24_Desk.ScreenChannelBarScroll.SetText(reaperTrk.Name)
        AddPanSend_SendStripHandlers(TrackNb, SendPageNb)
        AllScribbleScriptBlinking(False)

    End Sub
    Sub RefreshC24InsertViewMode(TrackNb As Integer, Optional SendPageNb As Integer = 1)
        'INSERT View
        ActualC24ViewMode = C24ViewModes.Insert
        RemoveViewsStripHandlers()
        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        C24_Desk.ScreenChannelBarScroll.SetText(reaperTrk.Name)
        SetInsertStripHandlers(TrackNb, SendPageNb)


    End Sub
    Sub RefreshC24FxParamViewMode(Fx As ReaperFX, Optional SendPageNb As Integer = 1)
        'FxParam View
        ActualC24ViewMode = C24ViewModes.FxParam
        RemoveViewsStripHandlers()


        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_ReaperSessionConfig.fxParamConfigs(Fx.Name).Count / CDbl(24))))

        C24_Desk.ScreenChannelBarScroll.SetText(_ReaperSessionConfig.GetPluginShortName(Fx.Name))
        C24_Desk.ScreenChannelBarScroll.ShowTempText("Page" & SendPageNb & "/" & totalPages)
        SetFxParamStripHandlers(Fx, SendPageNb)
        AllScribbleScriptBlinking(False)

    End Sub
    Sub RefreshC24SendsViewMode()
        'Sends A,B,C etc... View
        ActualC24ViewMode = C24ViewModes.Sends
        RemoveViewsStripHandlers()
        Dim SendIndex = ActualSendIndex
        Dim SendPage As Integer = Math.Truncate(SendIndex / PageSendsLetter.Values.First.Count) + 1
        Dim SendIndexLetter As Integer = SendIndex - ((SendPage - 1) * PageSendsLetter.Values.First.Count)
        Dim SendLetter As String = PageSendsLetter(SendPage)(SendIndexLetter)

        If Flip_Mode = False Then
            C24_Desk.ScreenChannelBarScroll.SetText("ViewSnd" & SendLetter)
        Else
            C24_Desk.ScreenChannelBarScroll.SetText("SendFlip")
        End If



        SetSendsStripHandlers(SendIndex, SendLetter)
        AllScribbleScriptBlinking(False)

    End Sub

    Public Sub RemoveViewsStripHandlers()
        IsFaderVpotVolHandlers = False
        RemoveAutomationVolumeHandlers()
        IsFaderVpotPanHandlers = False
        RemoveAutomationPanHandlers()
        IsMuteBtnHandlers = False
        RemoveAutomationMuteHandlers()
        IsFaderVpotSendLevelHandlers = False
        RemoveAutomationSendLevelHandlers()
        IsSendMuteBtnHandlers = False
        RemoveAutomationSendMuteHandlers()
        IsFaderVpotPluginParamHandlers = False
        RemoveAutomationPluginHandlers()

        Select Case LastC24ViewMode
            Case C24ViewModes.None

            Case C24ViewModes.Home

                RemoveHomeStripHandlers()



            Case C24ViewModes.PanSend, C24ViewModes.SendX, C24ViewModes.PanSend_Pan



                RemovePageHandlers()
                RemoveAssignHandlers()
                RemoveSendMutesBtnHandlers()
                RemoveSendPrePostBtnHandlers()

                For Each sh In _PanSendStripHandlers
                    If sh.C24_trk_LR IsNot Nothing Then
                        RemoveHandler sh.C24_trk_LR.SwitchActive.OnButtonReleased, sh.SwitchActiveBtnH
                    End If
                    If sh.C24_trk_SndName IsNot Nothing Then
                        RemoveHandler sh.C24_trk_SndName.Vpot.OnVpotValueChange, sh.VpotValueH
                        RemoveHandler sh.C24_trk_SndName.Vpot.OnVpotSelectedItemChange, sh.VpotSelectItemH
                        RemoveHandler sh.C24_trk_SndName.SwitchActive.OnButtonReleased, sh.SendMuteBtnH

                    End If
                    If sh.C24_trk_SndPre IsNot Nothing Then
                        RemoveHandler sh.C24_trk_SndPre.SwitchActive.OnButtonReleased, sh.SendModeBtnH
                        RemoveHandler sh.C24_trk_SndPre.Vpot.OnVpotValueChange, sh.VpotValueH
                        RemoveHandler sh.C24_trk_SndPre.Vpot.OnVpotValueChange, sh.vpotValueH_Pan
                    End If
                    If sh.C24_trk_SndX IsNot Nothing Then
                        RemoveHandler sh.C24_trk_SndX.SwitchActive.OnButtonReleased, sh.SwitchActiveBtnH
                        RemoveHandler sh.C24_trk_SndX.SwitchActive.OnButtonReleased, sh.switchActiveBtnH_Pan
                    End If
                    If sh.C24_trk_LR IsNot Nothing Then
                        RemoveHandler sh.C24_trk_LR.SwitchActive.OnButtonReleased, sh.switchActiveBtnH_PanEnter
                    End If

                Next
                _PanSendStripHandlers.Clear()

            Case C24ViewModes.Insert
                RemovePageHandlers()
                RemoveAssignHandlers()
                RemoveMasterBypassHandler()

                ' ── Retirer le handler Inserts_Param si présent ───────────────
                If _InsertParamBtnH IsNot Nothing Then
                    RemoveHandler C24_Desk.Inserts_Param.OnButtonReleased, _InsertParamBtnH
                    _InsertParamBtnH = Nothing
                End If
                C24_Desk.Inserts_Param.Led = False

                For Each iH In _InsertHandlers
                    If iH.C24_trk_FX_Name1 IsNot Nothing Then
                        RemoveHandler iH.C24_trk_FX_Name1.SwitchActive.OnButtonReleased, iH.SelectBtnH
                        RemoveHandler iH.C24_trk_FX_Name1.Vpot.OnVpotSelectedItemChange, iH.VpotSelectItemH
                    End If
                    If iH.C24_trk_FX_Name2 IsNot Nothing Then
                        RemoveHandler iH.C24_trk_FX_Name2.SwitchActive.OnButtonReleased, iH.BypassBtnH
                    End If
                Next
                _InsertHandlers.Clear()

            Case C24ViewModes.FxParam
                C24_Desk.Inserts_Param.Led = False

                ActualFxEdited.IsOpen = False

                RemovePageHandlers()
                'RemoveAssignHandlers()

                RemoveFxParamHandlers()
                RemoveFlipFxParamHandlers()
                RemoveVpotVolumeHandlers()

                RemoveFlipHandlers()
                AddFadersVolumeHandlers()

                ' ── Arrêter et libérer le monitor FxParam ─────────────────────
                If _ReaperFxParamMonitor IsNot Nothing Then
                    _ReaperFxParamMonitor.ClearWatched()
                    _ReaperFxParamMonitor.Stop()
                    _ReaperFxParamMonitor.Dispose()
                    _ReaperFxParamMonitor = Nothing
                End If

            Case C24ViewModes.Sends
                'eteinds les leds des sends
                SetSendLetterLed("")


                RemoveSendsStripHandlers()


        End Select
    End Sub

    Public Sub RemovePageHandlers()
        C24_Desk.Cbs_Right.IsBlinking = False
        C24_Desk.Cbs_Left.IsBlinking = False


        For Each h In _PageHandlers
            RemoveHandler C24_Desk.Cbs_Right.OnButtonReleased, h.PageRightBtnH
            RemoveHandler C24_Desk.Cbs_Left.OnButtonReleased, h.PageLeftBtnH
        Next
        _PageHandlers.Clear()
    End Sub
    Public Sub RemoveAssignHandlers()
        C24_Desk.Assign.IsBlinking = False
        C24_Desk.Esc_Group.SetAllBlinking(False)


        For Each h In _AssignHandlers
            RemoveHandler C24_Desk.Assign.OnButtonReleased, h.AssignBtnH
            RemoveHandler C24_Desk.Esc_Group.OnGroupButtonReleased, h.EscBtnH
        Next
        _AssignHandlers.Clear()
    End Sub
    Public Sub RemoveMasterBypassHandler()
        C24_Desk.Master_Bypass.IsBlinking = False
        For Each iH In _InsertHandlers
            RemoveHandler C24_Desk.Master_Bypass.OnButtonReleased, iH.BypMasterBtnH
        Next

    End Sub

#End Region



#Region "Home"

    Private Sub AddHomeStripHandlers()

        '--------------------------- Home Mode----------------------------------
        RemoveHomeStripHandlers()

        'If Flip_Mode = False Then
        '    RefreshTrackManagerPage()
        'End If

        '----------------- Flip Handler -------------------------------------------------

        Dim _RefreshViewMode As [Delegate] = Sub()
                                                 RefreshC24HomeViewMode()
                                             End Sub
        AddFlipHandlers(_RefreshViewMode)


        ClearC24_MiddleStrip()

        ' ── Configurer Faders/Vpots ──────────



        If Flip_Mode Then

            AddVpotVolumeHandlers()
            AddFadersPanHandlers()
        Else

            AddFadersVolumeHandlers()
            AddVpotPanHandlers()
        End If



    End Sub

    Sub RemoveHomeStripHandlers()
        RemoveFlipHandlers()
        RemoveFadersVolumeHandlers()
        RemoveVpotPanHandlers()
        RemoveVpotVolumeHandlers()
        RemoveFadersPanHandlers()

    End Sub
#End Region

#Region "PanSend"

    Private Sub AddPanSendStripHandlers(TrackNb As Integer, SendPageNb As Integer)
        RemovePanSendStripHandlers()

        If _ReaperTrackSendsManager IsNot Nothing Then
            _ReaperTrackSendsManager.Dispose()
            _ReaperTrackSendsManager = Nothing
        End If



        'Pas de Flip Mode
        If Flip_Mode Then
            Flip_Mode = False
        End If

        ClearC24_MiddleStrip()

        '--------------------------- PanSend Mode----------------------------------

        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        Dim _ReaperTrackSendsEditor = New Reaper.PageManagers.ReaperTrackSendsEditor(reaperTrk.TrackIndex, _ReaperProj.Tracks)
        Dim Sends As New Reaper.ReaperTrackSends(reaperTrk.TrackIndex)
        Dim OriginalSends(9) As String
        For i = 0 To OriginalSends.Count - 1
            If i < _ReaperTrackSendsEditor.Sends_Names.Count Then
                OriginalSends(i) = _ReaperTrackSendsEditor.Sends_Names(i)
            Else
                OriginalSends(i) = Nothing
            End If
        Next
        Dim ModifySends As String() = OriginalSends.Clone
        Dim sendsIndexes As New List(Of Integer)
        If SendPageNb = 1 Then
            sendsIndexes.AddRange({0, 1, 2, 3, 4})
        ElseIf SendPageNb = 2 Then
            sendsIndexes.AddRange({5, 6, 7, 8, 9})
        End If
        _ReaperTrackSendsManager = New ReaperTrackSendsManager(reaperTrk.TrackIndex, sendsIndexes)

        '------------Page Handlers-------------------------------------------------
        Dim h As New PageHandlers

        'init leds
        C24_Desk.Cbs_Right.IsBlinking = False

        C24_Desk.Cbs_Left.IsBlinking = False


        If SendPageNb = 1 Then
            C24_Desk.Cbs_Right.IsBlinking = True


            Dim PageRightBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()

                    RefreshC24PanSendViewMode(TrackNb, SendPageNb + 1)
                End Sub
            h.PageRightBtnH = PageRightBtnH
            AddHandler C24_Desk.Cbs_Right.OnButtonReleased, PageRightBtnH
        End If

        If SendPageNb = 2 Then
            C24_Desk.Cbs_Left.IsBlinking = True

            Dim PageLeftBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()

                    RefreshC24PanSendViewMode(TrackNb, SendPageNb - 1)
                End Sub
            h.PageLeftBtnH = PageLeftBtnH
            AddHandler C24_Desk.Cbs_Left.OnButtonReleased, PageLeftBtnH
        End If

        _PageHandlers.Add(h)
        '--------------- Assign Handlers ----------------------------------------------------------
        Dim ah As New AssignHandlers

        If _ReaperTrackSendsEditor.AvailableDestinations.Count > 0 Then

            'init leds
            C24_Desk.Assign.IsBlinking = False

            C24_Desk.Esc_Group.SetAllBlinking(False)
            C24_Desk.Esc_Group.SetAllOff()



            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If Assign_Mode = True Then

                        'save all sends assign modification
                        Dim SendsToRemove As New List(Of String)
                        For i = 0 To OriginalSends.Count - 1
                            If OriginalSends(i) <> ModifySends(i) Then
                                SendsToRemove.Add(OriginalSends(i))
                                If ModifySends(i) <> " No" Then
                                    Dim AvailableSendIndex = _ReaperTrackSendsEditor.AvailableDestinationsIndexByName(ModifySends(i))
                                    If AvailableSendIndex >= 0 Then
                                        _ReaperTrackSendsEditor.AddSend(AvailableSendIndex)
                                    End If
                                End If

                            End If
                        Next
                        For Each SendName In SendsToRemove
                            Dim AvailableSendIndex = _ReaperTrackSendsEditor.SendsIndexByName(SendName)
                            If AvailableSendIndex >= 0 Then
                                _ReaperTrackSendsEditor.RemoveSend(AvailableSendIndex)
                            End If
                        Next

                        Assign_Mode = False
                        RefreshC24PanSendViewMode(TrackNb, SendPageNb)
                        C24_Desk.Assign.IsBlinking = False
                        C24_Desk.Esc_Group.SetAllBlinking(False)
                    Else
                        'enter Assign Mode

                        Assign_Mode = True
                        RefreshC24PanSendViewMode(TrackNb, SendPageNb)
                        C24_Desk.Assign.IsBlinking = True
                        C24_Desk.Esc_Group.SetAllBlinking(True)
                        ' Exit Sub
                    End If


                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH

            Dim EscBtnH As Hardware.C24_GroupLedButton.OnGroupButtonReleasedEventHandler =
                Sub()
                    If Assign_Mode = True Then
                        'quit Assign Mode sans validation
                        C24_Desk.Assign.IsBlinking = False

                        C24_Desk.Esc_Group.SetAllBlinking(True)
                        C24_Desk.Esc_Group.SetAllOff()
                        Assign_Mode = False
                        RefreshC24PanSendViewMode(TrackNb, SendPageNb)
                    End If
                End Sub
            ah.EscBtnH = EscBtnH
            AddHandler C24_Desk.Esc_Group.OnGroupButtonReleased, EscBtnH


            _AssignHandlers.Add(ah)
        End If

        '----------------- Display + SendHandlers -------------------------------------------------
        '|L<>R|    |SndX|Name|Pre |    |SndX|  No|Name| etc...

        Dim sh As New PanSendStripHandlers

        'Pan : |L<>R|
        Dim C24_trk_LR = C24_Desk.ChannelStrips(0)
        sh.C24_trk_LR = C24_trk_LR
        C24_trk_LR.ScribbleStrip.Text = "L<>R"
        C24_trk_LR.LedSwitchActive.Led = True
        C24_trk_LR.SwitchActive.Led = False

        'Add switchActiveBtnH_PanEnter Handler
        Dim switchActiveBtnH_PanEnter As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(Track_Nb As Integer?)
                        RefreshC24PanSend_PanViewMode(TrackNb, SendPageNb)
                    End Sub
        sh.switchActiveBtnH_PanEnter = switchActiveBtnH_PanEnter
        AddHandler C24_trk_LR.SwitchActive.OnButtonReleased, switchActiveBtnH_PanEnter

        _PanSendStripHandlers.Add(sh)


        Dim SendMuteC24_Tracks As New List(Of C24_ChannelStrip)

        'Sends : |    |SndX|Name|Pre |  -> 5 fois 
        For i = 0 To 4
            sh = New PanSendStripHandlers

            'init commun
            Dim sendIndex = i + ((SendPageNb - 1) * 4)
            Dim send = Sends.Sends(sendIndex)

            Dim C24_trk_clear = C24_Desk.ChannelStrips(0 + (4 * i) + 1)
            Dim C24_trk_SndX = C24_Desk.ChannelStrips(1 + (4 * i) + 1)
            sh.C24_trk_SndX = C24_trk_SndX

            C24_trk_SndX.ScribbleStrip.Text = "Snd" & PageSendsLetter(SendPageNb)(i)
            Dim C24_trk_SndName = C24_Desk.ChannelStrips(2 + (4 * i) + 1)
            sh.C24_trk_SndName = C24_trk_SndName
            SendMuteC24_Tracks.Add(C24_trk_SndName)
            Dim C24_trk_SndPre = C24_Desk.ChannelStrips(3 + (4 * i) + 1)
            sh.C24_trk_SndPre = C24_trk_SndPre

            If OriginalSends(sendIndex) IsNot Nothing Then

                'init C24_trk_SndX
                C24_trk_SndX.LedSwitchActive.Led = True
                C24_trk_SndX.SwitchActive.IsBlinking = True
                'Add switchActiveBtnH_SendXEnter Handler
                Dim switchActiveBtnH_SendXEnter As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(_TrackNb As Integer?)
                        SndXLetter = C24_Desk.ChannelStrips(_TrackNb - 1).ScribbleStrip.Text.ToCharArray.ToArray(3).ToString
                        RefreshC24PanSend_SendViewMode(TrackNb, SendPageNb)
                    End Sub
                sh.switchActiveBtnH_SendXEnter = switchActiveBtnH_SendXEnter
                AddHandler C24_trk_SndX.SwitchActive.OnButtonReleased, switchActiveBtnH_SendXEnter

                'init C24_trk_SndName
                C24_trk_SndName.ScribbleStrip.Text = OriginalSends(sendIndex)
                C24_trk_SndName.LedSwitchActive.Led = True
                'Handlers SendMute ajoutés par AddSendsMutesBtnHandlers

                'init C24_trk_SndPre
                C24_trk_SndPre.LedSwitchActive.Led = True
                If Controler_Utility.GetC24SendMode(send.SendMode) = C24SendMode.Post Then
                    C24_trk_SndPre.ScribbleStrip.Text = "Post"
                    C24_trk_SndPre.SwitchActive.Led = False
                Else
                    C24_trk_SndPre.ScribbleStrip.Text = "Pre"
                    C24_trk_SndPre.SwitchActive.Led = True
                End If
                'Add switchActiveBtnH_SendPrePost Handler
                Dim switchActiveBtnH_SendPrePost As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(_TrackNb As Integer?)

                        If Controler_Utility.GetC24SendMode(send.SendMode) = C24SendMode.Pre Then
                            send.SendMode = 0 'Post Fader et PostFX
                            C24_trk_SndPre.ScribbleStrip.Text = "Post"
                            C24_trk_SndPre.SwitchActive.Led = False
                        Else
                            send.SendMode = 3 'Pre Fader
                            C24_trk_SndPre.ScribbleStrip.Text = "Pre"
                            C24_trk_SndPre.SwitchActive.Led = True
                        End If

                    End Sub
                sh.switchActiveBtnH_SendPrePost = switchActiveBtnH_SendPrePost
                AddHandler C24_trk_SndPre.SwitchActive.OnButtonReleased, switchActiveBtnH_SendPrePost



            Else
                C24_trk_SndPre.LedSwitchActive.Led = False
                C24_trk_SndPre.SwitchActive.Led = False
                C24_trk_SndName.LedSwitchActive.Led = False
                C24_trk_SndName.SwitchActive.Led = False
                C24_trk_SndX.LedSwitchActive.Led = False
                C24_trk_SndX.SwitchActive.Led = False
                C24_trk_SndName.ScribbleStrip.Text = "  No"
                C24_trk_SndPre.ScribbleStrip.Text = "Send"
            End If




            _PanSendStripHandlers.Add(sh)

        Next

        AddSendsMutesBtnHandlers(SendMuteC24_Tracks, sendsIndexes)
        AddVpotSendsVolumeHandlers(SendMuteC24_Tracks, sendsIndexes)




        '----------------------------------------------------------------------

    End Sub
    Private Sub RemovePanSendStripHandlers()
        RemovePageHandlers()
        RemoveAssignHandlers()
        RemoveSendsMutesBtnHandlers()
        RemoveVpotSendsVolumeHandlers()

        For Each h In _PanSendStripHandlers
            If h.C24_trk_LR IsNot Nothing Then
                RemoveHandler h.C24_trk_LR.SwitchActive.OnButtonReleased, h.switchActiveBtnH_PanEnter
            End If
            If h.C24_trk_SndX IsNot Nothing Then
                RemoveHandler h.C24_trk_SndX.SwitchActive.OnButtonReleased, h.switchActiveBtnH_SendXEnter
            End If


        Next

        _ReaperTrackSendsManager.Dispose()

        _PanSendStripHandlers.Clear()
    End Sub


    Private Sub AddPanSend_PanStripHandlers(TrackNb As Integer, SendPageNb As Integer)
        RemovePanSend_PanStripHandlers()

        If _ReaperTrackSendsManager IsNot Nothing Then
            _ReaperTrackSendsManager.Dispose()
            _ReaperTrackSendsManager = Nothing
        End If



        'Pas de Flip Mode
        If Flip_Mode Then
            Flip_Mode = False
        End If

        ClearC24_MiddleStrip()

        '--------------------------- PanSend Mode----------------------------------

        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        Dim _ReaperTrackSendsEditor = New Reaper.PageManagers.ReaperTrackSendsEditor(reaperTrk.TrackIndex, _ReaperProj.Tracks)
        Dim Sends As New Reaper.ReaperTrackSends(reaperTrk.TrackIndex)

        Dim sendsIndexes As New List(Of Integer)
        If SendPageNb = 1 Then
            sendsIndexes.AddRange({0, 1, 2, 3, 4})
        ElseIf SendPageNb = 2 Then
            sendsIndexes.AddRange({5, 6, 7, 8, 9})
        End If
        _ReaperTrackSendsManager = New ReaperTrackSendsManager(reaperTrk.TrackIndex, sendsIndexes)



        '----------------- Display + Handlers -------------------------------------------------


        'Pan ViewMode
        'Scribb ->|<Bck     |main      |    |L<>R     |
        'Switch ->|Back blk |          |    |         |
        'Vpot   ->|         |Track Vol |    |Track Pan|

        Dim sh As New PanSend_PanStripHandlers

        'Bck : |<Bck|
        Dim C24_trk_SndX = C24_Desk.ChannelStrips(0)
        sh.C24_trk_SndX = C24_trk_SndX
        C24_trk_SndX.ScribbleStrip.Text = "<Bck"
        C24_trk_SndX.SwitchActive.IsBlinking = True
        C24_trk_SndX.LedSwitchActive.Led = True
        'Add switchActiveBtnH_SendPrePost Handler
        Dim switchActiveBtnH_SendXEnter As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(_TrackNb As Integer?)
                        RefreshC24PanSendViewMode(TrackNb, SendPageNb)
                    End Sub
        sh.switchActiveBtnH_SendXEnter = switchActiveBtnH_SendXEnter
        AddHandler C24_trk_SndX.SwitchActive.OnButtonReleased, switchActiveBtnH_SendXEnter

        'Track Name : |Name|
        Dim C24_trk_SndName = C24_Desk.ChannelStrips(1)
        sh.C24_trk_SndName = C24_trk_SndName
        C24_trk_SndName.ScribbleStrip.Text = "main"
        AddVpotVolumeHandler(C24_trk_SndName, reaperTrk)
        'Clear : |    |
        Dim C24_trk_clear = C24_Desk.ChannelStrips(2)

        'Pan : |L<>R|
        Dim C24_trk_LR = C24_Desk.ChannelStrips(3)
        sh.C24_trk_LR = C24_trk_LR
        C24_trk_LR.ScribbleStrip.Text = "L<>R"
        AddVpotPanHandler(C24_trk_LR, reaperTrk)

        _PanSend_PanStripHandlers.Add(sh)
        '----------------------------------------------------------------------

    End Sub
    Private Sub RemovePanSend_PanStripHandlers()
        RemoveVpotVolumeHandlers()
        RemoveVpotPanHandlers()

        For Each h In _PanSendStripHandlers

            If h.C24_trk_SndX IsNot Nothing Then
                RemoveHandler h.C24_trk_SndX.SwitchActive.OnButtonReleased, h.switchActiveBtnH_SendXEnter
            End If


        Next

        _ReaperTrackSendsManager.Dispose()

        _PanSendStripHandlers.Clear()
    End Sub

    Private Sub AddPanSend_SendStripHandlers(TrackNb As Integer, SendPageNb As Integer)
        RemovePanSend_SendStripHandlers()

        If _ReaperTrackSendsManager IsNot Nothing Then
            _ReaperTrackSendsManager.Dispose()
            _ReaperTrackSendsManager = Nothing
        End If



        'Pas de Flip Mode
        If Flip_Mode Then
            Flip_Mode = False
        End If

        ClearC24_MiddleStrip()

        '--------------------------- PanSend Mode----------------------------------

        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        Dim _ReaperTrackSendsEditor = New Reaper.PageManagers.ReaperTrackSendsEditor(reaperTrk.TrackIndex, _ReaperProj.Tracks)
        Dim Sends As New Reaper.ReaperTrackSends(reaperTrk.TrackIndex)
        Dim SendIndex As Integer = PageSendsLetter(SendPageNb).ToList.IndexOf(SndXLetter)
        Dim send = Sends(SendIndex)
        Dim sendsIndexes As New List(Of Integer)
        If SendPageNb = 1 Then
            sendsIndexes.AddRange({0, 1, 2, 3, 4})
        ElseIf SendPageNb = 2 Then
            sendsIndexes.AddRange({5, 6, 7, 8, 9})
        End If
        _ReaperTrackSendsManager = New ReaperTrackSendsManager(reaperTrk.TrackIndex, sendsIndexes)
        Dim SendName = _ReaperTrackSendsEditor.Sends_Names(SendIndex)


        '----------------- Display + Handlers -------------------------------------------------


        'Pan ViewMode
        'Scribb ->|SndX    |SndName1  |SndName2 |
        'Switch ->|Back blk|SndMute   |Pre/Post |    
        'Vpot   ->|        |Snd Vol   |         |

        Dim sh As New PanSend_SendStripHandlers

        'Bck : |SndX|
        Dim C24_trk_SndX = C24_Desk.ChannelStrips(0)
        sh.C24_trk_SndX = C24_trk_SndX
        C24_trk_SndX.ScribbleStrip.Text = "Snd" & SndXLetter
        C24_trk_SndX.SwitchActive.IsBlinking = True
        C24_trk_SndX.LedSwitchActive.Led = True
        'Add switchActiveBtnH_SendPrePost Handler
        Dim switchActiveBtnH_SendXEnter As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(_TrackNb As Integer?)
                        RefreshC24PanSendViewMode(TrackNb, SendPageNb)
                    End Sub
        sh.switchActiveBtnH_SendXEnter = switchActiveBtnH_SendXEnter
        AddHandler C24_trk_SndX.SwitchActive.OnButtonReleased, switchActiveBtnH_SendXEnter

        'Snd Name1 : |Name1|
        Dim C24_trk_SndName = C24_Desk.ChannelStrips(1)
        C24_trk_SndName.ScribbleStrip.Text = SendName
        C24_trk_SndName.LedSwitchActive.Led = True
        AddSendsMutesBtnHandlers({C24_trk_SndName}.ToList, {SendIndex}.ToList)
        AddVpotSendsVolumeHandlers({C24_trk_SndName}.ToList, {SendIndex}.ToList)

        'Snd Name2 : |Name2|
        Dim C24_trk_SndPre = C24_Desk.ChannelStrips(2)
        sh.C24_trk_SndPre = C24_trk_SndPre
        'init C24_trk_SndPre
        C24_trk_SndPre.ScribbleStrip.Text = SendName.Substring(4)
        C24_trk_SndPre.LedSwitchActive.Led = True
        If Controler_Utility.GetC24SendMode(send.SendMode) = C24SendMode.Post Then
            C24_trk_SndPre.ScribbleStrip.ShowTempText("Post")
            C24_trk_SndPre.SwitchActive.Led = False
        Else
            C24_trk_SndPre.ScribbleStrip.ShowTempText("Pre")
            C24_trk_SndPre.SwitchActive.Led = True
        End If
        'Add switchActiveBtnH_SendPrePost Handler
        Dim switchActiveBtnH_SendPrePost As Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub(_TrackNb As Integer?)

                If Controler_Utility.GetC24SendMode(send.SendMode) = C24SendMode.Pre Then
                    send.SendMode = 0 'Post Fader et PostFX
                    C24_trk_SndPre.ScribbleStrip.ShowTempText("Post")
                    C24_trk_SndPre.SwitchActive.Led = False
                Else
                    send.SendMode = 3 'Pre Fader
                    C24_trk_SndPre.ScribbleStrip.ShowTempText("Pre")
                    C24_trk_SndPre.SwitchActive.Led = True
                End If

            End Sub
        sh.switchActiveBtnH_SendPrePost = switchActiveBtnH_SendPrePost
        AddHandler C24_trk_SndPre.SwitchActive.OnButtonReleased, switchActiveBtnH_SendPrePost

        '----------------------------------------------------------------------

    End Sub
    Private Sub RemovePanSend_SendStripHandlers()
        RemoveSendsMutesBtnHandlers()
        RemoveVpotSendsVolumeHandlers()

        For Each h In _PanSend_SendStripHandlers

            If h.C24_trk_SndX IsNot Nothing Then
                RemoveHandler h.C24_trk_SndX.SwitchActive.OnButtonReleased, h.switchActiveBtnH_SendXEnter
            End If

            If h.C24_trk_SndPre IsNot Nothing Then
                RemoveHandler h.C24_trk_SndPre.SwitchActive.OnButtonReleased, h.switchActiveBtnH_SendPrePost
            End If

        Next

        _ReaperTrackSendsManager.Dispose()

        _PanSendStripHandlers.Clear()
    End Sub






    Private Sub SetSndX(Sends As Reaper.ReaperTrackSends, SendLetter As String, SendPageNb As Integer, SendXindex As Integer, SendXOffset As Integer, _PanSendHandlers As List(Of PanSendStripHandlers), TrackNb As Integer, _ReaperTrackSendsEditor As Reaper.PageManagers.ReaperTrackSendsEditor, ModifySends As String())

        Dim sh As New PanSendStripHandlers

        Dim sendIndex = SendXindex + ((SendPageNb - 1) * 4)

        If SendXOffset = 0 Then
            SendXindex = 0
        End If

        Dim C24_trk_clear = C24_Desk.ChannelStrips(0 + (4 * SendXindex) + SendXOffset)
        Dim C24_trk_SndX = C24_Desk.ChannelStrips(1 + (4 * SendXindex) + SendXOffset)
        sh.C24_trk_SndX = C24_trk_SndX
        Dim C24_trk_SndName = C24_Desk.ChannelStrips(2 + (4 * SendXindex) + SendXOffset)
        sh.C24_trk_SndName = C24_trk_SndName
        Dim C24_trk_SndPre = C24_Desk.ChannelStrips(3 + (4 * SendXindex) + SendXOffset)
        sh.C24_trk_SndPre = C24_trk_SndPre



        C24_trk_SndX.ScribbleStrip.Text = "Snd" & SendLetter

        If Sends.Count > sendIndex Then
            '|    |SndX|Name|Pre |
            Dim send = Sends.Sends(sendIndex)
            sh.ReaperSend = send

            If Assign_Mode Then
                'rien
            Else
                C24_trk_SndX.SwitchActive.IsBlinking = True
                C24_trk_SndX.LedSwitchActive.Led = False
                'SendXMode Handler
                Dim switchActiveBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(Track_Nb As Integer?)
                        If ActualC24ViewMode = C24ViewModes.PanSend Then
                            'SendX ViewMode
                            ActualC24ViewMode = C24ViewModes.SendX
                            RemoveViewsStripHandlers()
                            ClearC24_MiddleStrip()
                            SetSndX(Sends, SendLetter, 1, SendXindex, 0, _PanSendHandlers, TrackNb, _ReaperTrackSendsEditor, ModifySends)
                        Else
                            'Back to PanSend ViewMode
                            RefreshC24PanSendViewMode(TrackNb, SendPageNb)
                        End If

                    End Sub
                sh.SwitchActiveBtnH = switchActiveBtnH
                AddHandler C24_trk_SndX.SwitchActive.OnButtonReleased, switchActiveBtnH
            End If

            If Assign_Mode Then
                Dim SendList As New List(Of String)
                SendList.Add(" No")
                SendList.AddRange(_ReaperTrackSendsEditor.AvailableDestinations_Names)
                Dim SendList_check = {_ReaperProj.Tracks(send.DestTrackIndex).Name}
                C24_trk_SndName.Vpot.InitTextList(SendList, SendList_check)
                C24_trk_SndName.Vpot.VpotMode = C24_Vpot.VpotModeType.TextList
                C24_trk_SndName.Vpot.SelectItemByText(SendList_check.First)

                'Vpot handler
                Dim VpotSelectItemH As Hardware.C24_Vpot.OnVpotSelectedItemChangeEventHandler =
                        Sub(Track_Nb As Integer, selectedIndex As Integer)
                            C24_trk_SndName.SwitchActive.IsBlinking = True
                            C24_trk_SndName.LedSwitchActive.Led = True
                        End Sub
                sh.VpotSelectItemH = VpotSelectItemH
                AddHandler C24_trk_SndName.Vpot.OnVpotSelectedItemChange, VpotSelectItemH


                'Check Handler              
                Dim SendMuteBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        Sub(Track_Nb As Integer?)
                            C24_trk_SndName.Vpot.CheckAllItems(False)
                            C24_trk_SndName.Vpot.CheckSelectedItem(True)
                            Dim ModifyIndex As Integer? = Nothing
                            For i = 0 To PageSendsLetter(SendPageNb).Count - 1
                                If SendLetter = PageSendsLetter(SendPageNb)(i) Then
                                    ModifyIndex = i + (5 * (SendPageNb - 1))
                                End If
                            Next
                            If ModifyIndex IsNot Nothing AndAlso C24_trk_SndName.Vpot.GetCheckedItems.Count = 1 Then
                                ModifySends(ModifyIndex) = C24_trk_SndName.Vpot.GetCheckedItems.First
                            End If

                            C24_trk_SndName.SwitchActive.IsBlinking = False
                            C24_trk_SndName.LedSwitchActive.Led = False
                        End Sub
                sh.SendMuteBtnH = SendMuteBtnH
                AddHandler C24_trk_SndName.SwitchActive.OnButtonReleased, SendMuteBtnH



            Else 'Normal Mode
                C24_trk_SndName.ScribbleStrip.Text = _ReaperProj.Tracks(send.DestTrackIndex).Name
                C24_trk_SndName.Vpot.VpotMode = C24_Vpot.VpotModeType.VolumeLinear
                C24_trk_SndName.Vpot.Value = send.Vol
                Dim ReaperTrk = _ReaperProj.Tracks(send.TrackIndex)

                Dim SendVolEnvelope As ReaperSendVolEnvelope = Nothing
                For Each _SendVolEnvelope In ReaperTrk.Envelopes.AllOfType(Of ReaperSendVolEnvelope)
                    If _SendVolEnvelope.SendId = send.SendIndex Then
                        SendVolEnvelope = _SendVolEnvelope
                        Exit For
                    End If
                Next

                If SendVolEnvelope IsNot Nothing Then
                    _C24trkSendVolEnvelope.Add(C24_trk_SndName, SendVolEnvelope)
                End If


                'Vpot sendVol handler


                Dim vpotValueH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                        Sub(Track_Nb As Integer, value As Double)


                            'Automation
                            ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                            Select Case ReaperTrk.AutoMode
                                Case 0, 1
                                    send.Vol = value
                                Case Else
                                    SendVolEnvelope = _C24trkSendVolEnvelope(C24_trk_SndName)
                                    If SendVolEnvelope IsNot Nothing Then
                                        _ReaperTracksManager.SuspendTrack(TrackNb - 1)
                                        SendVolEnvelope.Value = value
                                        _ReaperTracksManager.ResumeTrack(TrackNb - 1)
                                    End If


                            End Select


                        End Sub
                sh.VpotValueH = vpotValueH
                AddHandler C24_trk_SndName.Vpot.OnVpotValueChange, vpotValueH

                C24_trk_SndName.LedSwitchActive.Led = True
                C24_trk_SndName.SwitchActive.Led = send.Mute

                'Send Mute Handle

                Dim SendMuteEnvelope As ReaperSendMuteEnvelope = Nothing
                For Each _SendMuteEnvelope In ReaperTrk.Envelopes.AllOfType(Of ReaperSendMuteEnvelope)
                    If _SendMuteEnvelope.SendId = send.SendIndex Then
                        SendMuteEnvelope = _SendMuteEnvelope
                        Exit For
                    End If
                Next

                If SendMuteEnvelope IsNot Nothing Then
                    _C24trkSendMuteEnvelope.Add(C24_trk_SndName, SendMuteEnvelope)
                End If

                Dim SendMuteBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        Sub(Track_Nb As Integer?)


                            send.Mute = Not send.Mute
                            C24_trk_SndName.SwitchActive.Led = Not C24_trk_SndName.SwitchActive.Led
                            C24_trk_SndName.ScribbleStrip.ShowTempText("Mute", If(send.Mute, "  On", " Off"))

                            'Automation
                            ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                            Select Case ReaperTrk.AutoMode
                                Case 0, 1

                                Case Else
                                    SendMuteEnvelope = _C24trkSendMuteEnvelope(C24_trk_SndName)
                                    If SendMuteEnvelope IsNot Nothing Then
                                        _ReaperTracksManager.SuspendTrack(TrackNb - 1)
                                        SendMuteEnvelope.Value = If(send.Mute, 1, 0)
                                        _ReaperTracksManager.ResumeTrack(TrackNb - 1)
                                    End If


                            End Select




                        End Sub
                sh.SendMuteBtnH = SendMuteBtnH
                AddHandler C24_trk_SndName.SwitchActive.OnButtonReleased, SendMuteBtnH

                If Controler_Utility.GetC24SendMode(send.SendMode) = C24SendMode.Pre Then
                    C24_trk_SndPre.ScribbleStrip.Text = "Pre"
                    C24_trk_SndPre.LedSwitchActive.Led = True
                    C24_trk_SndPre.SwitchActive.Led = True
                Else
                    C24_trk_SndPre.ScribbleStrip.Text = "Post"
                    C24_trk_SndPre.LedSwitchActive.Led = True
                    C24_trk_SndPre.SwitchActive.Led = False
                End If
                'Pre Handle              
                Dim SendModeBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        Sub(Track_Nb As Integer?)
                            '3
                            If Controler_Utility.GetC24SendMode(send.SendMode) = C24SendMode.Pre Then
                                send.SendMode = 0 'Post Fader et PostFX
                                C24_trk_SndPre.ScribbleStrip.Text = "Post"
                            Else
                                send.SendMode = 3 'Pre Fader
                                C24_trk_SndPre.ScribbleStrip.Text = "Pre"
                            End If
                            C24_trk_SndPre.SwitchActive.Led = Not C24_trk_SndPre.SwitchActive.Led
                        End Sub
                sh.SendModeBtnH = SendModeBtnH
                AddHandler C24_trk_SndPre.SwitchActive.OnButtonReleased, SendModeBtnH
            End If
        Else 'No Send
            If Assign_Mode Then
                Dim SendList As New List(Of String)
                SendList.Add(" No")
                SendList.AddRange(_ReaperTrackSendsEditor.AvailableDestinations_Names)
                Dim SendList_check = {" No"}
                C24_trk_SndName.Vpot.InitTextList(SendList, SendList_check)
                C24_trk_SndName.Vpot.VpotMode = C24_Vpot.VpotModeType.TextList
                C24_trk_SndName.Vpot.SelectItemByText(SendList_check.First)



                'Vpot handler
                Dim VpotSelectItemH As Hardware.C24_Vpot.OnVpotSelectedItemChangeEventHandler =
                            Sub(Track_Nb As Integer, selectedIndex As Integer)
                                C24_trk_SndName.SwitchActive.IsBlinking = True
                                C24_trk_SndName.LedSwitchActive.Led = True
                            End Sub
                sh.VpotSelectItemH = VpotSelectItemH
                AddHandler C24_trk_SndName.Vpot.OnVpotSelectedItemChange, VpotSelectItemH


                'Check Handler              
                Dim SendMuteBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                            Sub(Track_Nb As Integer?)
                                C24_trk_SndName.Vpot.CheckAllItems(False)
                                C24_trk_SndName.Vpot.CheckSelectedItem(True)
                                Dim ModifyIndex As Integer? = Nothing
                                For i = 0 To PageSendsLetter(SendPageNb).Count - 1
                                    If SendLetter = PageSendsLetter(SendPageNb)(i) Then
                                        ModifyIndex = i + (5 * (SendPageNb - 1))
                                    End If
                                Next
                                If ModifyIndex IsNot Nothing AndAlso C24_trk_SndName.Vpot.GetCheckedItems.Count = 1 Then
                                    ModifySends(ModifyIndex) = C24_trk_SndName.Vpot.GetCheckedItems.First
                                End If


                                C24_trk_SndName.SwitchActive.IsBlinking = False
                                C24_trk_SndName.LedSwitchActive.Led = False
                            End Sub
                sh.SendMuteBtnH = SendMuteBtnH
                AddHandler C24_trk_SndName.SwitchActive.OnButtonReleased, SendMuteBtnH
            Else
                'Normal Mode
                '|    |SndX|  No|Send|
                C24_trk_SndX.LedSwitchActive.Led = False
                C24_trk_SndX.SwitchActive.Led = False

                C24_trk_SndName.ScribbleStrip.Text = "  No"
                C24_trk_SndName.LedSwitchActive.Led = False
                C24_trk_SndName.SwitchActive.Led = False

                C24_trk_SndPre.ScribbleStrip.Text = "Send"
                C24_trk_SndPre.LedSwitchActive.Led = False
                C24_trk_SndPre.SwitchActive.Led = False
            End If

        End If

        _PanSendHandlers.Add(sh)
    End Sub



#End Region

#Region "Insert"
    Private Sub SetInsertStripHandlers(TrackNb As Integer, InsertPageNb As Integer)

        'Pas de Flip Mode
        If Flip_Mode Then
            RemoveVpotVolumeHandlers()
            AddFadersVolumeHandlers()
            Flip_Mode = False
        End If


        ClearC24_MiddleStrip()

        '--------------------------- Insert Mode----------------------------------
        'init Selection FxInsert    
        If InsertSelectedIndex IsNot Nothing Then

            Dim selected_trkNb As Integer = (InsertSelectedIndex * 3) - ((InsertPageNb - 1) * 24)
            If selected_trkNb >= 0 AndAlso selected_trkNb < 24 Then
                C24_Desk.ChannelStrips(selected_trkNb).Vpot.ScribbleScriptsBlinking = True
                C24_Desk.ChannelStrips(selected_trkNb).SwitchActive.Led = True
                C24_Desk.ChannelStrips(selected_trkNb + 1).LedSwitchActive.Led = True
            End If

        End If

        Dim _ReaperTrackFxEditor As New ReaperTrackFxEditor(TrackNb - 1, _ReaperSessionConfig)
        _ReaperTrackFxEditor.Refresh()

        ' ── ModifyFxNames étendu à 16 slots (2 pages × 8) ────────────────────
        Dim ModifyFxNames(15) As String
        For i = 0 To 15
            If i < _ReaperTrackFxEditor.Count Then
                ModifyFxNames(i) = _ReaperTrackFxEditor.FxNames(i)
            Else
                ModifyFxNames(i) = "NoInsert"
            End If
        Next

        '------------Page Handlers-------------------------------------------------
        Dim h As New PageHandlers
        C24_Desk.Cbs_Right.IsBlinking = False
        C24_Desk.Cbs_Left.IsBlinking = False

        If InsertPageNb = 1 Then
            C24_Desk.Cbs_Right.IsBlinking = True
            C24_Desk.Cbs_Left.Led = False
            Dim PageRightBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    RefreshC24InsertViewMode(TrackNb, InsertPageNb + 1)
                End Sub
            h.PageRightBtnH = PageRightBtnH
            AddHandler C24_Desk.Cbs_Right.OnButtonReleased, PageRightBtnH
        End If

        If InsertPageNb = 2 Then
            C24_Desk.Cbs_Left.IsBlinking = True
            C24_Desk.Cbs_Right.Led = False
            Dim PageLeftBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    RefreshC24InsertViewMode(TrackNb, InsertPageNb - 1)
                End Sub
            h.PageLeftBtnH = PageLeftBtnH
            AddHandler C24_Desk.Cbs_Left.OnButtonReleased, PageLeftBtnH
        End If
        _PageHandlers.Add(h)

        '--------------- Assign Handlers ------------------------------------------
        Dim ah As New AssignHandlers

        If Assign_Mode Then
            C24_Desk.Assign.IsBlinking = True
            C24_Desk.Esc_Group.SetAllBlinking(True)
        Else
            C24_Desk.Assign.IsBlinking = False
            C24_Desk.Esc_Group.SetAllBlinking(False)
            C24_Desk.Esc_Group.SetAllOff()
        End If


        If _ReaperSessionConfig.GetVisiblePlugins.Count > 0 Then



            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If Assign_Mode = True Then
                        For i = 0 To ModifyFxNames.Length - 1
                            If i < _ReaperTrackFxEditor.Count Then
                                Dim reapName = _ReaperTrackFxEditor.FxNames(i)
                                Dim c24Name = ModifyFxNames(i)
                                If reapName <> c24Name Then
                                    _ReaperTrackFxEditor.DeleteFx(i)
                                    If c24Name <> "NoInsert" Then
                                        _ReaperTrackFxEditor.AddFx(c24Name)
                                    End If
                                End If
                            ElseIf ModifyFxNames(i) <> "NoInsert" Then
                                _ReaperTrackFxEditor.AddFx(ModifyFxNames(i))
                            End If
                        Next
                        Assign_Mode = False
                        C24_Desk.Assign.IsBlinking = False
                        C24_Desk.Esc_Group.SetAllBlinking(False)
                        RefreshC24InsertViewMode(TrackNb, InsertPageNb)
                    Else
                        Assign_Mode = True
                        C24_Desk.Assign.IsBlinking = True
                        C24_Desk.Esc_Group.SetAllBlinking(True)
                        InsertSelectedIndex = Nothing
                        RefreshC24InsertViewMode(TrackNb, InsertPageNb)
                    End If
                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH

            Dim EscBtnH As Hardware.C24_GroupLedButton.OnGroupButtonReleasedEventHandler =
                Sub()
                    If Assign_Mode = True Then
                        C24_Desk.Assign.IsBlinking = False
                        C24_Desk.Esc_Group.SetAllBlinking(False)
                        C24_Desk.Esc_Group.SetAllOff()
                        Assign_Mode = False
                        RefreshC24InsertViewMode(TrackNb, InsertPageNb)
                    End If
                End Sub
            ah.EscBtnH = EscBtnH
            AddHandler C24_Desk.Esc_Group.OnGroupButtonReleased, EscBtnH
            _AssignHandlers.Add(ah)
        End If

        '----------------- Display + InsertHandlers -------------------------------

        For i = 0 To 7
            Dim Fx_index = i + ((InsertPageNb - 1) * 8)
            Dim Fx As ReaperFX = Nothing
            If Fx_index < _ReaperTrackFxEditor.Count Then
                ' Recharger le FX depuis le trackIndex
                Dim fxList As New Reaper.ReaperTrackFX(TrackNb - 1)
                fxList.Refresh()
                If Fx_index < fxList.Count Then
                    Fx = fxList(Fx_index)
                End If
            End If
            SetInsertFx(i, InsertPageNb, Fx_index, Fx, _InsertHandlers, _ReaperTrackFxEditor, ModifyFxNames)
        Next
        RefreshBypassLed(_ReaperTrackFxEditor, InsertPageNb)
        '----------------- Master Bypass ------------------------------------------
        RefreshMasterBypassLed(_ReaperTrackFxEditor)

        If Assign_Mode = False Then
            Dim iH As New InsertHandlers
            ' ── Le lambda capture l'état au moment du clic, pas du setup ──────
            Dim BypMasterBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    _ReaperTrackFxEditor.Refresh()
                    Dim enableAll = _ReaperTrackFxEditor.GetFxList().Any(Function(f) Not f.IsEnabled)
                    For idx = 0 To _ReaperTrackFxEditor.Count - 1
                        _ReaperTrackFxEditor.SetFxEnabled(idx, enableAll)
                    Next
                    RefreshC24InsertViewMode(TrackNb, InsertPageNb)  ' ← dans le lambda ✓
                End Sub
            iH.BypMasterBtnH = BypMasterBtnH
            AddHandler C24_Desk.Master_Bypass.OnButtonReleased, BypMasterBtnH
            _InsertHandlers.Add(iH)
        End If
    End Sub
    Sub SetInsertFx(Insert_place As Integer, InsertPageNb As Integer, Fx_index As Integer, Fx As ReaperFX, _InsertHandlers As List(Of InsertHandlers), _ReaperTrackFxEditor As ReaperTrackFxEditor, ModifyFxNames As String())

        Dim iH As New InsertHandlers

        Dim C24_trk_FX_Name1 = C24_Desk.ChannelStrips((Insert_place * 3) + 0)
        iH.C24_trk_FX_Name1 = C24_trk_FX_Name1
        Dim C24_trk_FX_Name2 = C24_Desk.ChannelStrips((Insert_place * 3) + 1)
        iH.C24_trk_FX_Name2 = C24_trk_FX_Name2
        C24_trk_FX_Name1.Vpot.ExtendedScribbles = {C24_trk_FX_Name2.ScribbleStrip}.ToList
        Dim C24_trk_FX_clear = C24_Desk.ChannelStrips((Insert_place * 3) + 2)

        If Assign_Mode = False Then
            If Fx Is Nothing Then
                C24_trk_FX_Name1.Vpot.SetAllScribbleText("NoInsert")
            Else
                ' ── Nom court depuis le INI (8 car., modifiable par l'utilisateur) ──
                Dim shortName = _ReaperSessionConfig.GetPluginShortName(Fx.Name)
                If Fx.Enabled Then
                    shortName = shortName.ToLower
                    C24_trk_FX_Name2.SwitchActive.Led = True
                Else
                    shortName = shortName.ToUpper
                    C24_trk_FX_Name2.SwitchActive.Led = False
                End If

                C24_trk_FX_Name1.Vpot.SetAllScribbleText(shortName)

            End If

            Dim SelectBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub(TrackNb As Integer?)
                    If Fx IsNot Nothing Then
                        If C24_trk_FX_Name1.Vpot.ScribbleScriptsBlinking Then

                            If Fx IsNot Nothing Then
                                'go to param fx

                                ' ── Définir le delegate dans une variable locale ──────────────
                                ' _InsertParamBtnH sera mis à Nothing par RemoveViewsStripHandlers()
                                ' (Case Insert) appelé depuis RefreshC24FxParamViewMode.
                                ' La variable locale survit à ce nettoyage.
                                Dim localInsertParamBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                                    Sub()
                                        If C24_Desk.Inserts_Param.Led = True Then
                                            C24_Desk.Inserts_Param.Led = False
                                            RemoveViewsStripHandlers()
                                            RefreshC24InsertViewMode(_ReaperTracksManager.GetRelativeTrackIndex(Fx.TrackIndex) + 1, InsertPageNb)
                                        End If
                                    End Sub

                                ' ── RefreshC24FxParamViewMode appelle RemoveViewsStripHandlers ─
                                ' qui fait _InsertParamBtnH = Nothing.
                                ' On réassigne _InsertParamBtnH APRÈS ce nettoyage.
                                RefreshC24FxParamViewMode(Fx)
                                C24_Desk.Inserts_Param.Led = True
                                _InsertParamBtnH = localInsertParamBtnH
                                AddHandler C24_Desk.Inserts_Param.OnButtonReleased, _InsertParamBtnH
                            End If

                        Else
                            AllScribbleScriptBlinking(False)
                            AllSwitchActive(False, False)
                            RefreshBypassLed(_ReaperTrackFxEditor, InsertPageNb)
                            AllSwitchActiveLed(False, False)
                            C24_trk_FX_Name1.Vpot.ScribbleScriptsBlinking = True
                            InsertSelectedIndex = Insert_place + ((InsertPageNb - 1) * 8)
                            C24_trk_FX_Name1.SwitchActive.Led = True
                            C24_trk_FX_Name2.LedSwitchActive.Led = True
                            C24_trk_FX_Name2.SwitchActive.Led = Fx.Enabled
                        End If
                    End If
                End Sub
            iH.SelectBtnH = SelectBtnH
            AddHandler C24_trk_FX_Name1.SwitchActive.OnButtonReleased, SelectBtnH

            Dim BypassBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If C24_trk_FX_Name1.SwitchActive.Led = True AndAlso Fx IsNot Nothing Then
                        Dim shortName = _ReaperSessionConfig.GetPluginShortName(Fx.Name)
                        If Fx.Enabled Then
                            Fx.Enabled = False
                            shortName = shortName.ToUpper

                            C24_trk_FX_Name1.Vpot.SetAllScribbleText(shortName)
                            C24_trk_FX_Name1.Vpot.ShowTempAllScribbleText("Byp:  On")
                            C24_trk_FX_Name2.SwitchActive.Led = False
                        Else
                            Fx.Enabled = True
                            If Fx.Enabled = True Then
                                shortName = shortName.ToLower
                                C24_trk_FX_Name1.Vpot.SetAllScribbleText(shortName)
                                C24_trk_FX_Name1.Vpot.ShowTempAllScribbleText("Byp: Off")
                                C24_trk_FX_Name2.SwitchActive.Led = True
                            Else
                                C24_trk_FX_Name1.Vpot.ShowTempAllScribbleText("Blocked!")
                            End If

                        End If
                        RefreshMasterBypassLed(_ReaperTrackFxEditor)
                    End If

                End Sub
            iH.BypassBtnH = BypassBtnH
            AddHandler C24_trk_FX_Name2.SwitchActive.OnButtonReleased, BypassBtnH

        Else 'Assign Mode = True

            Dim FxList As New List(Of String)
            FxList.Add("NoInsert")
            For Each visible_plugin In _ReaperSessionConfig.GetVisiblePlugins
                FxList.Add(_ReaperSessionConfig.GetPluginShortName(visible_plugin))
            Next
            For Each FxName In _ReaperTrackFxEditor.FxNames
                Dim FxShortName As String = _ReaperSessionConfig.GetPluginShortName(FxName)
                If FxList.Contains(FxShortName) = False Then
                    FxList.Add(FxShortName)
                End If
            Next



            If Fx Is Nothing Then
                Dim FxList_check = {"NoInsert"}
                C24_trk_FX_Name1.Vpot.InitTextList(FxList, FxList_check)
                C24_trk_FX_Name1.Vpot.VpotMode = C24_Vpot.VpotModeType.TextList
                C24_trk_FX_Name1.Vpot.SelectItemByText(FxList_check.First)
            Else
                Dim FxList_check = {_ReaperSessionConfig.GetPluginShortName(Fx.Name)}
                C24_trk_FX_Name1.Vpot.InitTextList(FxList, FxList_check)
                C24_trk_FX_Name1.Vpot.VpotMode = C24_Vpot.VpotModeType.TextList
                C24_trk_FX_Name1.Vpot.SelectItemByText(FxList_check.First)
            End If


            Dim SelectBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub(TrackNb As Integer?)
                    C24_trk_FX_Name1.SwitchActive.IsBlinking = False
                    C24_trk_FX_Name1.LedSwitchActive.Led = False
                    C24_trk_FX_Name1.Vpot.CheckAllItems(False)
                    C24_trk_FX_Name1.Vpot.CheckSelectedItem(True)
                    ModifyFxNames(Fx_index) = _ReaperSessionConfig.GetPluginFullName(C24_trk_FX_Name1.Vpot.GetCheckedItems.First)
                End Sub
            iH.SelectBtnH = SelectBtnH
            AddHandler C24_trk_FX_Name1.SwitchActive.OnButtonReleased, SelectBtnH

            'Vpot handler
            Dim VpotSelectItemH As Hardware.C24_Vpot.OnVpotSelectedItemChangeEventHandler =
                        Sub(Track_Nb As Integer, selectedIndex As Integer)
                            C24_trk_FX_Name1.SwitchActive.IsBlinking = True
                            C24_trk_FX_Name1.LedSwitchActive.Led = True
                        End Sub
            iH.VpotSelectItemH = VpotSelectItemH
            AddHandler C24_trk_FX_Name1.Vpot.OnVpotSelectedItemChange, VpotSelectItemH


        End If


        _InsertHandlers.Add(iH)


    End Sub
    Sub RefreshBypassLed(_ReaperTrackFxEditor As ReaperTrackFxEditor, InsertPageNb As Integer)
        _ReaperTrackFxEditor.Refresh()
        For i = 0 To _ReaperTrackFxEditor.Count - 1
            Dim cs_index As Integer = ((i * 3) + 1) - ((InsertPageNb - 1) * 8)
            If cs_index >= 0 Then
                If _ReaperTrackFxEditor.GetFxList(i).IsEnabled Then
                    C24_Desk.ChannelStrips(cs_index).SwitchActive.Led = True
                Else
                    C24_Desk.ChannelStrips(cs_index).SwitchActive.Led = False
                End If
            End If

        Next
    End Sub
    Sub RefreshMasterBypassLed(_ReaperTrackFxEditor As ReaperTrackFxEditor)
        _ReaperTrackFxEditor.Refresh()
        Dim allBypassed = _ReaperTrackFxEditor.GetFxList().Count > 0 AndAlso
                          _ReaperTrackFxEditor.GetFxList().All(Function(f) Not f.IsEnabled)
        Dim anyBypassed = _ReaperTrackFxEditor.GetFxList().Any(Function(f) Not f.IsEnabled)

        C24_Desk.Master_Bypass.IsBlinking = False
        C24_Desk.Master_Bypass.Led = False
        If allBypassed Then
            C24_Desk.Master_Bypass.Led = True
        ElseIf anyBypassed Then
            C24_Desk.Master_Bypass.IsBlinking = True
        End If
    End Sub
    Sub RefreshInsertModeLed()

        For Each trk In _ReaperTracksManager.Tracks
            Dim C24_cs = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(trk.TrackIndex))
            C24_cs.InsertMode.IsBlinking = False
            C24_cs.InsertMode.Led = Not _ReaperTracksManager.IsTrackGenericFxEmpty(trk.TrackIndex)
        Next
    End Sub


#End Region

#Region "FxParam"
    Private Sub SetFxParamStripHandlers(Fx As ReaperFX, FxParamPageNb As Integer)
        Fx.Refresh()
        If Flip_Mode = False Then
            RefreshTrackManagerPage()
        End If
        ' ── Stopper l'ancien monitor si changement de page ────────────────────
        If _ReaperFxParamMonitor IsNot Nothing Then
            _ReaperFxParamMonitor.ClearWatched()
            _ReaperFxParamMonitor.Stop()
            _ReaperFxParamMonitor.Dispose()
            _ReaperFxParamMonitor = Nothing
        End If

        ParamIndexToTrackIndex = New Dictionary(Of Integer, Integer)
        _ReaperFxParamMonitor = New ReaperFxParamMonitor(Fx.TrackIndex, Fx.FxIndex)
        _ReaperFxParamMonitor.Start()
        _ReaperFxParamMonitor.ClearWatched()
        _ReaperFxParamMonitor.WatchParams(0, Fx.ParamCount)
        ActualFxEdited = Fx
        '--------------------------- FxParam Mode----------------------------------
        Const ParamsPerPage As Integer = 24
        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_ReaperSessionConfig.fxParamConfigs(Fx.Name).Count / CDbl(ParamsPerPage))))

        '------------Page Handlers-------------------------------------------------
        Dim h As New PageHandlers

        ' ── LEDs page ─────────────────────────────────────────────────────────
        C24_Desk.Cbs_Right.IsBlinking = False
        C24_Desk.Cbs_Left.IsBlinking = False

        ' ── Page suivante : visible si pas sur la dernière page ───────────────
        If FxParamPageNb < totalPages Then
            C24_Desk.Cbs_Right.IsBlinking = True

            Dim PageRightBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    RefreshC24FxParamViewMode(Fx, FxParamPageNb + 1)
                End Sub
            h.PageRightBtnH = PageRightBtnH
            AddHandler C24_Desk.Cbs_Right.OnButtonReleased, PageRightBtnH
        End If

        ' ── Page précédente : visible si pas sur la première page ─────────────
        If FxParamPageNb > 1 Then
            C24_Desk.Cbs_Left.IsBlinking = True

            Dim PageLeftBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    RefreshC24FxParamViewMode(Fx, FxParamPageNb - 1)
                End Sub
            h.PageLeftBtnH = PageLeftBtnH
            AddHandler C24_Desk.Cbs_Left.OnButtonReleased, PageLeftBtnH
        End If

        _PageHandlers.Add(h)



        '----------------- Flip Handler -------------------------------------------------
        RemoveFlipHandlers()
        Dim _RefreshViewMode As [Delegate] = Sub()
                                                 RefreshC24FxParamViewMode(Fx, FxParamPageNb)
                                             End Sub
        AddFlipHandlers(_RefreshViewMode)

        '----------------- Auto/Select Mode Handler -------------------------------------------------
        RemoveAutoSelectModeHandlers()
        Dim ASMh As New AutoSelectModeHandler
        Dim AutoSelectBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                C24_Desk.Auto_Select.Led = Not C24_Desk.Auto_Select.Led
                AutoSelect_Mode = Not AutoSelect_Mode
                RefreshC24FxParamViewMode(Fx, FxParamPageNb)
            End Sub
        ASMh.AutoSelectBtnH = AutoSelectBtnH
        AddHandler C24_Desk.Auto_Select.OnButtonReleased, AutoSelectBtnH
        _AutoSelectModeHandlers.Add(ASMh)
        '----------------- Display + FxParamHandlers -------------------------------------------------

        ClearC24_MiddleStrip()


        Fx.IsOpen = True

        'reload Track state chunk
        Dim Trk_guid = _ReaperProj.TrackGuids(Fx.TrackIndex)
        If TracksChunk.ContainsKey(Trk_guid) Then
            Reaper.SetTrackStateChunk(Fx.TrackIndex, TracksChunk(Trk_guid))
        End If

        'Force UI modification
        Reaper.SetTrackUIVolume(Fx.TrackIndex, _ReaperProj.Tracks(Fx.TrackIndex).Vol)
        Fx.Refresh()


        ' ── Configurer Faders/Vpots UNE SEULE FOIS avant la boucle ──────────
        If Flip_Mode Then
            RemoveFadersVolumeHandlers()
            AddVpotVolumeHandlers()
        Else
            RemoveVpotVolumeHandlers()
            AddFadersVolumeHandlers()
        End If
        RemoveFxParamHandlers()

        Dim _fxParamConfig = _ReaperSessionConfig.fxParamConfigs(Fx.Name)
        Dim fpH As FxParamHandlers = Nothing

        _C24trkPluginParamEnvelope.Clear()

        '|Param1|Param2|etc...
        For i = 0 To Math.Min(23, (_fxParamConfig.Count - (24 * (FxParamPageNb - 1))) - 1)
            Dim cs = C24_Desk.ChannelStrips(i)
            Dim FxParamConfigIndex As Integer = i + (24 * (FxParamPageNb - 1))
            fpH = New FxParamHandlers
            fpH.C24Trk = cs
            Dim FxShortName = _fxParamConfig(FxParamConfigIndex).ShortName
            'text separator
            If _fxParamConfig(FxParamConfigIndex).TextOnly IsNot Nothing Then
                cs.ScribbleStrip.Text = _fxParamConfig(FxParamConfigIndex).TextOnly
                'fxparam
            ElseIf _fxParamConfig(FxParamConfigIndex).Visible Then

                cs.ScribbleStrip.Text = FxShortName
                Dim FxParam = Fx.Params(_fxParamConfig(FxParamConfigIndex).ParamIndex)
                ParamIndexToTrackIndex.Add(FxParam.ParamIndex, i)
                fpH.ReaperFxParam = FxParam

                'Dico des FxParamEnvelope avec les Cs
                Dim ReaperTrk = _ReaperProj.Tracks(Fx.TrackIndex)
                Dim FxParamEnvelope As ReaperFxParamEnvelope = Nothing
                For Each _FxParamEnvelope In ReaperTrk.Envelopes.AllOfType(Of ReaperFxParamEnvelope)
                    If _FxParamEnvelope.FxIndex = Fx.FxIndex Then
                        If _FxParamEnvelope.ParamIndex = FxParam.ParamIndex Then
                            FxParamEnvelope = _FxParamEnvelope
                        End If

                    End If
                Next
                If FxParamEnvelope IsNot Nothing Then
                    _C24trkPluginParamEnvelope.Add(cs, FxParamEnvelope)
                End If

                If AutoSelect_Mode = False Then
                    'ajout du switch active pour les Fxparam booléen
                    If FxParam.IsToggle Then
                        cs.LedSwitchActive.Led = True
                        cs.SwitchActive.Led = If(FxParam.NormValue = 0, False, True)

                        Dim SelectBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        Sub(TrackNb As Integer?)
                            If FxParam.NormValue = 0 Then
                                FxParam.NormValue = 1
                                cs.ScribbleStrip.ShowTempText("  On")
                            Else
                                FxParam.NormValue = 0
                                cs.ScribbleStrip.ShowTempText(" Off")
                            End If
                            cs.SwitchActive.Led = If(FxParam.NormValue = 0, False, True)
                        End Sub
                        fpH.SelectBtnH = SelectBtnH
                        AddHandler cs.SwitchActive.OnButtonReleased, SelectBtnH
                    End If
                Else 'Auto Select Mode 

                    If _C24trkPluginParamEnvelope.ContainsKey(cs) Then


                        cs.SwitchActive.Led = True
                        cs.LedSwitchActive.Led = _C24trkPluginParamEnvelope(cs).Armed

                        Dim SelectBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                       Sub(TrackNb As Integer?)
                           cs.LedSwitchActive.Led = Not cs.LedSwitchActive.Led
                           _C24trkPluginParamEnvelope(cs).Armed = Not _C24trkPluginParamEnvelope(cs).Armed
                           cs.ScribbleStrip.ShowTempText("Auto", If(cs.LedSwitchActive.Led, "  On", " Off"))
                       End Sub
                        fpH.SelectBtnH = SelectBtnH
                        AddHandler cs.SwitchActive.OnButtonReleased, SelectBtnH


                    End If
                End If


                'init Vpot values
                cs.Vpot.VpotMode = C24_Vpot.VpotModeType.FxParamLinear
                cs.Vpot.Value = FxParam.NormValue
                cs.Vpot.FxParamValueToDisplay = FxParam.Formatted

                'Le Fx param est géré comme un param dont les valeurs sont Normalisées.
                If Flip_Mode = False Then


                    Dim r = (FxParam.MaxValue - FxParam.MinValue)
                    If r > 0 Then
                        If FxParam.Step IsNot Nothing AndAlso FxParam.Step <> 0 Then
                            'Step paramétré
                            If r / FxParam.Step > 100 Then
                                'step trop petit pour un Vpot --> modification du step volontaire
                                Do Until r / FxParam.Step <= 100
                                    FxParam.Step = FxParam.Step * 10
                                Loop
                            End If
                            cs.Vpot.FxParam_StepValue = FxParam.Step / r
                            'Large and Small Step paramétrés
                            If FxParam.LargeStep IsNot Nothing AndAlso FxParam.LargeStep <> 0 Then
                                cs.Vpot.FxParam_LargeStepValue = FxParam.LargeStep / r
                            Else
                                cs.Vpot.FxParam_LargeStepValue = FxParam.Step / r
                            End If
                            If FxParam.SmallStep IsNot Nothing AndAlso FxParam.SmallStep <> 0 Then
                                cs.Vpot.FxParam_SmallStepValue = FxParam.SmallStep / r
                            Else
                                cs.Vpot.FxParam_SmallStepValue = (FxParam.Step / 10) / r
                            End If
                        End If
                    End If


                    Dim VpotValueChangeH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                        Sub(Track_Nb As Integer, Value As Double)


                            'Automation
                            ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                            Select Case ReaperTrk.AutoMode
                                Case 0, 1
                                    '_ReaperFxParamMonitor.SuspendAllParams()
                                    _ReaperFxParamMonitor.SuspendParam(FxParam.ParamIndex)
                                    FxParam.NormValue = Value

                                    'Force UI modification
                                    Reaper.SetTrackUIVolume(Fx.TrackIndex, _ReaperProj.Tracks(Fx.TrackIndex).Vol)

                                    'Save Chunk

                                    Trk_guid = _ReaperProj.TrackGuids(Fx.TrackIndex)
                                    If TracksChunk.ContainsKey(Trk_guid) Then
                                        TracksChunk(Trk_guid) = Reaper.GetTrackStateChunk(Fx.TrackIndex)
                                    Else
                                        TracksChunk.Add(Trk_guid, Reaper.GetTrackStateChunk(Fx.TrackIndex))
                                    End If

                                    cs.Vpot.FxParamValueToDisplay = FxParam.Formatted
                                    '_ReaperFxParamMonitor.ResumeAllParams()
                                    _ReaperFxParamMonitor.ResumeParam(FxParam.ParamIndex)
                                Case Else

                                    FxParamEnvelope = _C24trkPluginParamEnvelope(cs)
                                    If FxParamEnvelope IsNot Nothing Then
                                        _ReaperFxParamMonitor.SuspendParam(FxParam.ParamIndex)
                                        FxParamEnvelope.Value = Value
                                        _ReaperFxParamMonitor.ResumeParam(FxParam.ParamIndex)
                                    End If


                            End Select







                        End Sub
                    fpH.VpotValueChangeH = VpotValueChangeH
                    AddHandler cs.Vpot.OnVpotValueChange, VpotValueChangeH







                Else 'Flip mode

                    cs.Fader.Fader_Mode = C24_Fader.Mode.NormValue
                    ' ── Init position + envoi hardware (symétrique avec SetInitFaderDbVolume) ──
                    cs.Fader.SetFaderNormValue(New Tuple(Of Double, String)(FxParam.NormValue, FxParam.Formatted))
                    cs.Vpot.Value = FxParam.NormValue

                    Dim FaderValueChangeH As Hardware.C24_Fader.On_FaderHumanChangeNormValueEventHandler =
                        Sub(Track_Nb As Integer, NormFormatedValues As Tuple(Of Double, String))
                            'Automation

                            Select Case ReaperTrk.AutoMode
                                Case 0, 1
                                    _ReaperFxParamMonitor.SuspendParam(FxParam.ParamIndex)
                                    FxParam.NormValue = NormFormatedValues.Item1
                                    cs.Vpot.Value = FxParam.NormValue
                                    _ReaperFxParamMonitor.ResumeParam(FxParam.ParamIndex)
                                    FxParam.Refresh()
                                    NormFormatedValues = New Tuple(Of Double, String)(FxParam.NormValue, FxParam.Formatted)
                                    cs.Fader.SetFaderNormValue(NormFormatedValues)
                                Case Else

                                    FxParamEnvelope = _C24trkPluginParamEnvelope(cs)
                                    If FxParamEnvelope IsNot Nothing Then
                                        _ReaperFxParamMonitor.SuspendParam(FxParam.ParamIndex)
                                        FxParamEnvelope.Value = NormFormatedValues.Item1
                                        _ReaperFxParamMonitor.ResumeParam(FxParam.ParamIndex)
                                        'FxParam.Refresh()
                                    End If


                            End Select



                        End Sub
                    fpH.FaderValueChangeH = FaderValueChangeH
                    AddHandler cs.Fader.On_FaderHumanChangeNormValue, FaderValueChangeH






                End If

            End If
            _FxParamHandlers.Add(fpH)
            If Flip_Mode AndAlso fpH.FaderValueChangeH IsNot Nothing Then
                _FlipFxParamHandlers.Add(fpH)
            End If



            'Console.Write("|" & cs.Vpot.FxParamValueToDisplay)
        Next

        'ajout des handlers venant de Reaper
        fpH = New FxParamHandlers

        Dim FxParamMonitorChangeH As Reaper.Monitors.ReaperFxParamMonitor.On_ParamChangeEventHandler =
                         Sub(ParamIndex As Integer, NormValue As Double)
                             If Not ParamIndexToTrackIndex.ContainsKey(ParamIndex) Then Return
                             Dim Fxp = Fx.Params(ParamIndex)
                             Dim _cs = C24_Desk.ChannelStrips(ParamIndexToTrackIndex(ParamIndex))

                             If Fxp.IsToggle Then
                                 _cs.ScribbleStrip.ShowTempText(If(CInt(NormValue) = 1, "  On", " Off"))
                                 _cs.SwitchActive.Led = Fxp.NormValue <> 0
                             Else
                                 _cs.Vpot.Value = NormValue
                                 Fxp.Refresh()
                                 If Flip_Mode Then
                                     ' ── SetFaderNormValue appelle SendAndArmVerify → fader bouge ──
                                     _cs.Fader.SetFaderNormValue(New Tuple(Of Double, String)(NormValue, Fxp.Formatted))
                                 End If

                             End If
                         End Sub
        fpH.ParamMonitorChangeH = FxParamMonitorChangeH
        AddHandler _ReaperFxParamMonitor.On_ParamChange, FxParamMonitorChangeH


        RaiseEvent FaderVpotPluginParamHandlers(_C24trkPluginParamEnvelope, True)


        _FxParamHandlers.Add(fpH)



    End Sub


    Sub RemoveFxParamHandlers()
        For Each fpH In _FxParamHandlers
            If fpH.C24Trk IsNot Nothing Then
                Dim cs = fpH.C24Trk
                RemoveHandler cs.SwitchActive.OnButtonReleased, fpH.SelectBtnH
                RemoveHandler cs.Vpot.OnVpotValueChange, fpH.VpotValueChangeH
            End If

            If fpH.ParamMonitorChangeH IsNot Nothing AndAlso _ReaperFxParamMonitor IsNot Nothing Then
                RemoveHandler _ReaperFxParamMonitor.On_ParamChange, fpH.ParamMonitorChangeH
            End If
        Next
        _FxParamHandlers.Clear()
    End Sub
    Sub RemoveFlipFxParamHandlers()
        ' ── Retire UNIQUEMENT FaderValueChangeH (spécifique au mode Flip) ────
        ' SelectBtnH et ParamMonitorChangeH sont déjà gérés par RemoveFxParamHandlers
        For Each fpH In _FlipFxParamHandlers
            If fpH.FaderValueChangeH IsNot Nothing Then
                RemoveHandler fpH.C24Trk.Fader.On_FaderHumanChangeNormValue, fpH.FaderValueChangeH
            End If
        Next
        _FlipFxParamHandlers.Clear()
    End Sub
    Sub RemoveAutoSelectModeHandlers()
        For Each h In _AutoSelectModeHandlers
            RemoveHandler C24_Desk.Auto_Select.OnButtonReleased, h.AutoSelectBtnH
        Next
        _AutoSelectModeHandlers.Clear()
    End Sub
    Sub AddAutomationPluginHandlers(C24trkPluginParamEnvelope As Dictionary(Of C24_ChannelStrip, Reaper.ReaperFxParamEnvelope), status As Boolean) Handles Me.FaderVpotPluginParamHandlers
        _C24trkPluginParamEnvelope = C24trkPluginParamEnvelope
        IsFaderVpotPluginParamHandlers = status
        RemoveAutomationPluginHandlers()

        For Each C24Trk In C24trkPluginParamEnvelope.Keys
            Dim FxParamEnvelope = C24trkPluginParamEnvelope(C24Trk)

            Dim h As New AutomationPluginHandlers

            h.c24Trk = C24Trk
            h.FxParamEnvelope = FxParamEnvelope
            FxParamEnvelope.Refresh()
            Dim reaperTrk = _ReaperProj.Tracks(FxParamEnvelope.TrackIndex)

            _ReaperTracksManager.SuspendTrack(C24Trk.TrackNumber - 1)


            If PluginAuto AndAlso IsFaderVpotPluginParamHandlers Then
                ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                Select Case reaperTrk.AutoMode
                    Case 1
                        FxParamEnvelope.Active = True
                        FxParamEnvelope.Armed = True


                        If C24Trk IsNot Nothing Then

                            Dim Fx = _ReaperProj.TrackFXList(FxParamEnvelope.TrackIndex).FxList(FxParamEnvelope.FxIndex)

                            If Flip_Mode = False Then
                                Dim AutoFxParamChangeH As Reaper.ReaperFxParamEnvelope.On_NormalizedValueChangeEventHandler =
                                Sub(_value As Double, _paramIndex As Integer)
                                    Dim FxParam = Fx.Params(FxParamEnvelope.ParamIndex)
                                    FxParam.Refresh()
                                    C24Trk.Fader.SetFaderNormValue(New Tuple(Of Double, String)(_value, FxParam.Formatted))

                                End Sub
                                h.AutoFxParamChangeH = AutoFxParamChangeH
                                AddHandler FxParamEnvelope.On_NormalizedValueChange, AutoFxParamChangeH
                            Else 'flip mode 
                                Dim AutoFxParamChangeH As Reaper.ReaperFxParamEnvelope.On_NormalizedValueChangeEventHandler =
                                Sub(_value As Double, _paramIndex As Integer)
                                    Dim FxParam = Fx.Params(FxParamEnvelope.ParamIndex)
                                    FxParam.Refresh()
                                    C24Trk.Vpot.Value = _value
                                    C24Trk.Vpot.ScribbleStrip.ShowTempText(FxParam.Formatted)
                                End Sub
                                h.AutoFxParamChangeH = AutoFxParamChangeH
                                AddHandler FxParamEnvelope.On_NormalizedValueChange, AutoFxParamChangeH
                            End If



                        End If


                    Case 0
                        FxParamEnvelope.Active = False
                        FxParamEnvelope.Armed = False

                    Case Else
                        FxParamEnvelope.Active = True
                        FxParamEnvelope.Armed = True

                End Select
            Else
                FxParamEnvelope.Active = False
                FxParamEnvelope.Armed = False


            End If


            _ReaperTracksManager.ResumeTrack(C24Trk.TrackNumber - 1)



            _AutomationPluginHandlers.Add(h)

        Next



    End Sub
    Sub RemoveAutomationPluginHandlers()
        For Each h In _AutomationPluginHandlers
            RemoveHandler h.FxParamEnvelope.On_NormalizedValueChange, h.AutoFxParamChangeH

        Next
        _AutomationPluginHandlers.Clear()
    End Sub
#End Region

#Region "Sends"
    Private Sub SetSendsStripHandlers(SendIndex As Integer, SendLetter As String)
        RemoveSendsStripHandlers()

        If _ReaperTracksSendManager IsNot Nothing Then
            _ReaperTracksSendManager.Dispose()
            _ReaperTracksSendManager = Nothing
        End If

        _ReaperTracksSendManager = New ReaperTracksSendManager(_ReaperTracksManager.Tracks(), SendIndex)

        'Force PrePost Mode
        If Flip_Mode AndAlso SendMute_Mode Then
            SendMute_Mode = False
            PrePost_Mode = True
            C24_Desk.Pre_Post.Led = True
            C24_Desk.Send_Mute.Led = False
        End If


        '--------------------------- Sends Mode----------------------------------
        Dim _sh As New SendsStripHandlers
        SetSendLetterLed(SendLetter)


        '----------------- Flip Handler -------------------------------------------------

        Dim _RefreshViewMode As [Delegate] = Sub()

                                                 RefreshC24SendsViewMode()
                                             End Sub
        AddFlipHandlers(_RefreshViewMode)

        '----------------- Pre/Post Handlers -------------------------------------------------


        'init
        C24_Desk.Pre_Post.Led = PrePost_Mode
        If PrePost_Mode Then
            For i = 0 To _ReaperTracksManager.Tracks.Count - 1
                Dim ReaperTrk = _ReaperTracksManager.Tracks(i)
                Dim Sends As New Reaper.ReaperTrackSends(ReaperTrk.TrackIndex)
                If SendIndex > Sends.Count - 1 Then Continue For
                Dim send = Sends.Sends(SendIndex)
                Dim cs = C24_Desk.ChannelStrips(i)

                If Controler_Utility.GetC24SendMode(send.SendMode) = C24SendMode.Pre Then
                    cs.ScribbleStrip.ShowTempText("Pre")
                    cs.SwitchActive.Led = True
                Else
                    cs.ScribbleStrip.ShowTempText("Post")
                    cs.SwitchActive.Led = False
                End If

            Next
        End If

        Dim PrePostBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                         Sub()
                             If PrePost_Mode = False Then

                                 PrePost_Mode = True
                                 SendMute_Mode = False
                                 C24_Desk.Pre_Post.Led = True
                                 C24_Desk.Send_Mute.Led = False
                                 RefreshC24SendsViewMode()
                             End If

                         End Sub
        _sh.PrePostBtnH = PrePostBtnH
        AddHandler C24_Desk.Pre_Post.OnButtonReleased, PrePostBtnH


        '----------------- SendMute Handlers -------------------------------------------------

        'init
        C24_Desk.Send_Mute.Led = SendMute_Mode
        'init des sendmutes faites par les SendMuteHandlers



        Dim SendMuteBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                         Sub()
                             If SendMute_Mode = False Then

                                 If Flip_Mode = False Then

                                     SendMute_Mode = True
                                     PrePost_Mode = False
                                     C24_Desk.Send_Mute.Led = True
                                     C24_Desk.Pre_Post.Led = False
                                     RefreshC24SendsViewMode()

                                 Else 'Flip mode
                                     C24_Desk.ScreenLine.ShowTempText("When in send flip mode, send mute controled by main strip mute switch")
                                 End If
                             End If



                         End Sub
        _sh.SendMuteBtnH = SendMuteBtnH
        AddHandler C24_Desk.Send_Mute.OnButtonReleased, SendMuteBtnH

        _SendsStripHandlers.Add(_sh)
        '----------------- Display + SendsHandlers -------------------------------------------------



        ClearC24_MiddleStrip()

        ' ── Configurer Faders/Vpots  ──────────



        If Flip_Mode Then

            AddFadersSendVolumeHandlers(True)
            AddVpotPanHandlers()

        Else 'Mode normal


            AddVpotSendVolumeHandlers(True)
            AddFadersVolumeHandlers()
            AddMutesBtnHandlers()
            AddSolosHandlers()
            AddSoloClearSafeC24Handlers()

        End If

        AddSendMutesBtnHandlers()

        If PrePost_Mode Then
            AddSendPrePostBtnHandlers()
        End If


    End Sub


    Sub RemoveSendsStripHandlers()
        RemoveFlipHandlers()
        RemoveVpotSendVolumeHandlers()
        RemoveFadersVolumeHandlers()
        RemoveMutesBtnHandlers()
        RemoveSolosHandlers()
        RemoveSolosClearSafeHandlers()
        RemoveFadersSendVolumeHandlers()
        RemoveVpotPanHandlers()
        RemoveSendMutesBtnHandlers()
        RemoveSendPrePostBtnHandlers()

        For Each h In _SendsStripHandlers
            RemoveHandler C24_Desk.Send_Mute.OnButtonReleased, h.SendMuteBtnH
            RemoveHandler C24_Desk.Pre_Post.OnButtonReleased, h.PrePostBtnH
        Next

        _ReaperTracksSendManager.Dispose()

        _SendsStripHandlers.Clear()
    End Sub


    Sub SetSendLetterLed(SendLetter As String)

        C24_Desk.Send_A_F.IsBlinking = False
        C24_Desk.Send_B_G.IsBlinking = False
        C24_Desk.Send_C_H.IsBlinking = False
        C24_Desk.Send_D_I.IsBlinking = False
        C24_Desk.Send_E_J.IsBlinking = False

        Select Case SendLetter
            Case "A"
                C24_Desk.Send_A_F.Led = True
            Case "F"
                C24_Desk.Send_A_F.IsBlinking = True
            Case "B"
                C24_Desk.Send_B_G.Led = True
            Case "G"
                C24_Desk.Send_B_G.IsBlinking = True
            Case "C"
                C24_Desk.Send_C_H.Led = True
            Case "H"
                C24_Desk.Send_C_H.IsBlinking = True
            Case "D"
                C24_Desk.Send_D_I.Led = True
            Case "I"
                C24_Desk.Send_D_I.IsBlinking = True
            Case "E"
                C24_Desk.Send_E_J.Led = True
            Case "J"
                C24_Desk.Send_E_J.IsBlinking = True
            Case Else
        End Select


    End Sub

#End Region




#End Region







    ''' <summary>
    ''' Libère toutes les ressources du contrôleur.
    ''' À appeler lors de la fermeture de l'application.
    ''' Idempotent : sans effet si déjà appelé.
    ''' </summary>
    Public Sub Dispose()
        If _disposedValue Then Return
        _disposedValue = True

        ' ── Arrêt du timer VU-mètres ──────────────────────────────────────────
        If _PeakTimer IsNot Nothing Then
            _PeakTimer.Stop()
            _PeakTimer.Dispose()
            _PeakTimer = Nothing
        End If

        If _ReaperProjectMonitor IsNot Nothing Then
            _ReaperProjectMonitor.Stop()
            _ReaperProjectMonitor.Dispose()
            _ReaperProjectMonitor = Nothing
        End If

        ' ── Forcer le nettoyage de la vue COURANTE (pas de la précédente) ─────
        ' RemoveViewsStripHandlers utilise LastC24ViewMode → on l'aligne sur
        ' ActualC24ViewMode pour garantir que la vue active est bien nettoyée.
        _LastC24ViewMode = _ActualC24ViewMode
        RemoveViewsStripHandlers()

        ' ── Filet de sécurité : monitor FxParam toujours stoppé ───────────────
        If _ReaperFxParamMonitor IsNot Nothing Then
            _ReaperFxParamMonitor.ClearWatched()
            _ReaperFxParamMonitor.Stop()
            _ReaperFxParamMonitor.Dispose()
            _ReaperFxParamMonitor = Nothing
        End If

        ' ── Filet de sécurité : handler Inserts_Param toujours retiré ─────────
        If _InsertParamBtnH IsNot Nothing Then
            RemoveHandler _C24_Desk.Inserts_Param.OnButtonReleased, _InsertParamBtnH
            _InsertParamBtnH = Nothing
        End If

        ' ── Handlers communs ──────────────────────────────────────────────────
        RemoveCommonStripHandlers()
        RemoveSolosClearSafeHandlers()
        RemoveBankHandlers()
        RemoveCounterModeHandler()
        RemoveSendsLetterHandlers()
        RemoveFadersVolumeHandlers()
        RemoveMutesBtnHandlers()
        RemoveSolosHandlers()
        RemoveModifiersHandlers()
        RemoveShowC24Handlers()
        RemoveAutomationEnableHandlers()
        RemoveFlipHandlers()

        'Remove Reaper all tracks Handlers
        RemoveReaperEnvArmedHandlers()
        RemoveReaperSoloHandlers()
        RemoveReaperAutoModeHandlers()

        ' ── Transport (stoppe et dispose _ReaperTransportManager) ─────────────
        RemoveTransportHandlers()

        ' ── Managers ──────────────────────────────────────────────────────────
        If _ReaperTracksManager IsNot Nothing Then
            _ReaperTracksManager.Dispose()
            _ReaperTracksManager = Nothing
        End If
        If _ReaperPeakLevelManager IsNot Nothing Then
            _ReaperPeakLevelManager.Dispose()
            _ReaperPeakLevelManager = Nothing
        End If

        ' ── Session et hub ────────────────────────────────────────────────────
        If _ReaperSession IsNot Nothing Then
            _ReaperSession.Stop()
            _ReaperSession.Dispose()
            _ReaperSession = Nothing
        End If

        ReaperPollHub.Instance.Stop()
    End Sub

End Class

Class EqFxListManager
    Private _TrackIndex As Integer
    Private _ReaperTracksManager As ReaperTracksManager
    Private _ReaperSessionConfig As ReaperSessionConfig
    Private _CurrentIndex As Integer = -1
    Private _EqFxList As List(Of ReaperFX) = Nothing

    Sub New(TrackIndex As Integer, ReaperTracksManager As ReaperTracksManager, ReaperSessionConfig As ReaperSessionConfig)
        _TrackIndex = TrackIndex
        _ReaperTracksManager = ReaperTracksManager
        _ReaperSessionConfig = ReaperSessionConfig
    End Sub

    ''' <summary>
    ''' Retourne le prochain FX EQ de la piste à chaque appel.
    ''' Revient à Nothing après le dernier, puis recommence au suivant.
    ''' Retourne Nothing si aucun FX EQ n'est présent.
    ''' </summary>
    Public Function EqBtnRelease() As ReaperFX
        ' ── Construire / rafraîchir la liste à chaque cycle complet ──────────
        If _EqFxList Is Nothing OrElse _CurrentIndex = -1 Then
            Dim _ReaperTrackFX As New ReaperTrackFX(_TrackIndex)
            _ReaperTrackFX.Refresh()
            _EqFxList = New List(Of ReaperFX)
            For Each fx In _ReaperTrackFX.FxList
                If _ReaperSessionConfig.IsEQPlugin(fx.Name) Then
                    _EqFxList.Add(fx)
                End If
            Next
            _CurrentIndex = 0
        End If

        ' ── Aucun EQ sur la piste ─────────────────────────────────────────────
        If _EqFxList.Count = 0 Then
            _EqFxList = Nothing
            _CurrentIndex = -1
            Return Nothing
        End If

        ' ── Fin de cycle → renvoyer Nothing et réinitialiser ─────────────────
        If _CurrentIndex >= _EqFxList.Count Then
            _EqFxList = Nothing
            _CurrentIndex = -1
            Return Nothing
        End If

        ' ── Retourner le FX courant et avancer le curseur ─────────────────────
        Dim result As ReaperFX = _EqFxList(_CurrentIndex)
        _CurrentIndex += 1
        Return result
    End Function

End Class
Class DynFxListManager
    Private _TrackIndex As Integer
    Private _ReaperTracksManager As ReaperTracksManager
    Private _ReaperSessionConfig As ReaperSessionConfig
    Private _CurrentIndex As Integer = -1
    Private _DynFxList As List(Of ReaperFX) = Nothing

    Sub New(TrackIndex As Integer, ReaperTracksManager As ReaperTracksManager, ReaperSessionConfig As ReaperSessionConfig)
        _TrackIndex = TrackIndex
        _ReaperTracksManager = ReaperTracksManager
        _ReaperSessionConfig = ReaperSessionConfig
    End Sub

    ''' <summary>
    ''' Retourne le prochain FX DYN de la piste à chaque appel.
    ''' Revient à Nothing après le dernier, puis recommence au suivant.
    ''' Retourne Nothing si aucun FX DYN n'est présent.
    ''' </summary>
    Public Function DynBtnRelease() As ReaperFX
        ' ── Construire / rafraîchir la liste à chaque cycle complet ──────────
        If _DynFxList Is Nothing OrElse _CurrentIndex = -1 Then
            Dim _ReaperTrackFX As New ReaperTrackFX(_TrackIndex)
            _ReaperTrackFX.Refresh()
            _DynFxList = New List(Of ReaperFX)
            For Each fx In _ReaperTrackFX.FxList
                If _ReaperSessionConfig.IsDynPlugin(fx.Name) Then
                    _DynFxList.Add(fx)
                End If
            Next
            _CurrentIndex = 0
        End If

        ' ── Aucun DYN sur la piste ────────────────────────────────────────────
        If _DynFxList.Count = 0 Then
            _DynFxList = Nothing
            _CurrentIndex = -1
            Return Nothing
        End If

        ' ── Fin de cycle → renvoyer Nothing et réinitialiser ─────────────────
        If _CurrentIndex >= _DynFxList.Count Then
            _DynFxList = Nothing
            _CurrentIndex = -1
            Return Nothing
        End If

        ' ── Retourner le FX courant et avancer le curseur ─────────────────────
        Dim result As ReaperFX = _DynFxList(_CurrentIndex)
        _CurrentIndex += 1
        Return result
    End Function

End Class
Module Controler_Utility
    Enum C24SendMode
        Pre
        Post
    End Enum
    Enum ReaperSendMode
        PostFader = 0
        PreFx = 1
        PostFx = 3
    End Enum
    Function GetC24SendMode(ReaperSendMode As Integer) As C24SendMode
        Select Case ReaperSendMode
            Case 0
                Return C24SendMode.Post
            Case 1
                Return C24SendMode.Pre
            Case 2
                Return C24SendMode.Pre
            Case 3
                Return C24SendMode.Pre
        End Select
        Return Nothing
    End Function
End Module