Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Reflection
Imports System.Runtime
Imports System.Runtime.InteropServices.JavaScript.JSType
Imports System.Security.Cryptography.X509Certificates
Imports System.Threading
Imports DriverReaperControl24.C24ReaperController
Imports DriverReaperControl24.Control24
Imports DriverReaperControl24.Control24.Commands
Imports DriverReaperControl24.Control24.Hardware
Imports DriverReaperControl24.DriverReaperControl24.Input
Imports DriverReaperControl24.Reaper
Imports DriverReaperControl24.Reaper.Managers
Imports DriverReaperControl24.Reaper.Monitors
Imports DriverReaperControl24.Reaper.PageManagers

Public Class C24ReaperController
    Dim PageSendsLetter As New Dictionary(Of Integer, String()) From {
        {1, {"A", "B", "C", "D", "E"}},
        {2, {"F", "G", "H", "I", "J"}}
    }

    '-------Time Monitoring Params ----------
    Private ActiveTimeMonitoring As Boolean = False
    Private MonitorTimeValue As Double = 0
    Private Event On_MonitorTimeValueExced()
    '----------------------------------------

    Private LastCurPos As Double = 0
    Private CtlPoint As Double = 0
    Private CompareAB As FxSettingManager = Nothing
    Private UpdateRecMasterBypass As Boolean = False
    Private EqDynBypassMode As Boolean = False
    Private InsertBypassMode As Boolean = False
    Private RecSafeMode As Boolean = False
    Private Event OnBankChangeEnd()
    Private LastReaperTrackIndexAutomation As Integer? = Nothing
    Private LastNavigationMode As NavigationModes = NavigationModes.None
    Private LastJogWheelToolMode As JogWheelToolModes = JogWheelToolModes.None
    Private LastSavedSelection As Tuple(Of Double, Double) = Nothing
    Private IsCursorPlacedLeft As Boolean = True
    Private NavigationMode As NavigationModes = NavigationModes.None
    Private DefautResumeScreensBtn As New List(Of C24_Button)
    Private NumPadMode As NumPadModes = NumPadModes.Input
    Private _NumPadOutput As New NumPadOutput
    Private JogWheelMode As JogWheelModes = JogWheelModes.Shuttle
    Private JogWheelToolMode As JogWheelToolModes = JogWheelToolModes.None
    Private ReadOnly _clipboardService As DriverReaperControl24.Input.IClipboardService
    Private ReadOnly _keyboardInjector As IKeyboardInjector
    Private BankValidViewModes As C24ViewModes() = {C24ViewModes.Home, C24ViewModes.Sends, C24ViewModes.FxParam}
    Private _disposedValue As Boolean = False
    Private _ReaperSessionConfig As Reaper.ReaperSessionConfig
    Private WithEvents _C24_Desk As C24_Desk
    Public WithEvents _ReaperSession As Reaper.ReaperSession
    Public WithEvents _ReaperProj As Reaper.ReaperProject
    Private _ReaperProjects As New Dictionary(Of String, Reaper.ReaperProject)
    Private WithEvents _ReaperTracksManager As Reaper.PageManagers.ReaperTracksManager
    Private WithEvents _ReaperPeakLevelManager As Reaper.PageManagers.ReaperPeakLevelManager
    Private WithEvents _ReaperTransportManager As ReaperTransportManager
    Private WithEvents _ReaperFxParamMonitor As ReaperFxParamMonitor
    Private WithEvents _ReaperFxParamEnvelopeMonitor As ReaperFxParamEnvelopeMonitor
    Private WithEvents _ReaperTracksSendManager As ReaperTracksSendManager
    Private WithEvents _ReaperTrackSendsManager As ReaperTrackSendsManager
    Private WithEvents _ReaperProjectMonitor As ReaperProjectMonitor
    Private WithEvents _SelectedItemsManager As SelectedItemsManager

    Public FxParamValueChangeH As [Delegate]
    Private Flip_Mode As Boolean = False
    Private AutoSelect_Mode As Boolean = False
    Private TracksChunk As New Dictionary(Of String, String)
    Private PrePost_Mode As Boolean = True
    Private SendMute_Mode As Boolean = False
    Private SndXLetter As String = ""
    Private PanSendViewMode_TrackNb As Integer = 0
    Private PanSendViewMode_PageNb As Integer = 1
    Private InsertViewMode_TrackNb As Integer = 0
    Private InsertViewMode_PageNb As Integer = 1

    Private LastValueToShow As String = ""
    Private LastTextInfoToShow As String = ""

    Private C24ActionLinkers As New List(Of C24ActionLinker)
    Private Regions As New Dictionary(Of Integer, Region)
    Private Markers As New Dictionary(Of Integer, Marker)

    ' ── Auto Handlers Status ───────────────
    Private _C24trkVolEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackVolEnvelope)
    Private _C24trkPanEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackPanEnvelope)
    Private _C24trkMuteEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperTrackMuteEnvelope)
    Private _C24trkSendVolEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperSendVolEnvelope)
    Private _C24trkSendPanEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperSendPanEnvelope)
    Private _C24trkSendMuteEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperSendMuteEnvelope)
    Private _C24trkPluginParamEnvelope As New Dictionary(Of C24_ChannelStrip, Reaper.ReaperFxParamEnvelope)
    ' ── Auto Status ───────────────
    Private FaderAuto As Boolean = False
    Private PanAuto As Boolean = False
    Private MuteAuto As Boolean = False
    Private SendLevelAuto As Boolean = False
    Private SendMuteAuto As Boolean = False
    Private PluginAuto As Boolean = False
    Private ReaperTrackVolEnvelopeGUIDArmStatus As New Dictionary(Of String, Boolean)
    ' ── Auto Modifier ───────────────
    Private Write As Boolean = False
    Private Touch As Boolean = False
    Private Latch As Boolean = False
    Private Trim As Boolean = False
    Private Read As Boolean = False
    Private Off As Boolean = False

    ' ── Modifiers ───────────────
    Public Opt As Boolean = False
    Public Shift As Boolean = False
    Public Ctl As Boolean = False
    Public Ctrl As Boolean = False


    Private SoloSafeTracks As New Dictionary(Of Integer, Boolean)
    Private SoloTracks As New Dictionary(Of Integer, Boolean)
    Private Names As New Dictionary(Of Integer, String)
    Private ParamIndexToTrackIndex As New Dictionary(Of Integer, Integer)
    Private TrkIndexEqFxListManagers As New Dictionary(Of Integer, EqFxListManager)
    Private TrkIndexDynFxListManagers As New Dictionary(Of Integer, DynFxListManager)
    Private SelectedFx As ReaperFX = Nothing
    Dim FxParamPageNb As Integer = 1
    Dim FxParamTotalPages As Integer = 1

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
    Private _InsertStripHandlers As New List(Of InsertStripHandler)
    Private _FxParamVpotHandlers As New List(Of FxParamVpotHandlers)
    Private _FxParamFaderHandlers As New List(Of FxParamFaderHandlers)
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
    Private _FxParamStripHandlers As New List(Of FxParamStripHandlers)
    Private _ProjectManagementHandler As New List(Of ProjectManagementHandler)
    Private _SessionManagementHandler As New List(Of SessionManagementHandler)
    Private _MessageHandlers As New List(Of MessageHandler)
    Private _VpotSendPanHandlers As New List(Of SendPanHandlers)
    Private _FxparamEnvHandlers As New List(Of FxparamEnvHandler)
    Private _OutputsStripHandlers As New List(Of OutputsStripHandler)
    Private _InputsStripHandlers As New List(Of InputsStripHandler)
    Private _MasterStripHandlers As New List(Of MasterStripHandler)
    Private _ScribbleScriptsHandlers As New List(Of ScribbleScriptsHandler)
    Private _ZoomPresetStripHandlers As New List(Of ZoomPresetStripHandler)
    Private _AutoModifiersHandlers As New List(Of AutoModifiersHandler)
    Private _JogWheelHandlers As New List(Of JogWheelHandler)
    Private _C24ActionLinkerHandlers As New List(Of C24ActionLinkerHandler)
    Private _NumPadHandlers As New List(Of NumPadHandler)
    Private _UndoSaveHandler As New List(Of UndoSaveHandler)
    Private _ChBarScreenHandlers As New List(Of ChBarScreenHandler)
    Private _ScreenLineHandlers As New List(Of ScreenLineHandler)
    Private _NavigationHandlers As New List(Of NavigationHandler)
    Private _UtilityHandlers As New List(Of UtilityHandler)
    Private _EditToolsHandlers As New List(Of EditToolsHandler)
    Private _EditFunctionHandlers As New List(Of EditFunctionHandler)
    Private _FunctionKeysHandlers As New List(Of FunctionKeysHandler)
    Private _GroupsHandlers As New List(Of GroupsHandler)
    Private _WindowsHandlers As New List(Of WindowsHandler)
    Private _PeaksHandlers As New List(Of PeaksHandler)
    Private _RecordHandlers As New List(Of RecordHandler)
    Private _MonitorHandlers As New List(Of MonitorHandler)
    Private _ChannelSettingsHandlers As New List(Of ChannelSettingsHandler)

    Private _Solo_Mode As Solo_Mode = Solo_Mode.Solo
    Private _ActualC24ViewMode As C24ViewModes = C24ViewModes.Home
    Private _LastC24ViewMode As C24ViewModes = C24ViewModes.None
    Private Assign_Mode As Boolean = False
    Private NeedInsertParam As Boolean = False


    Private ActualSendIndex As Integer = -1
    ' ── Timer VU-mètres ───────────────────────────────────────────────────────
    Private _PeakTimer As System.Timers.Timer
    Private _peakTimerBusy As Integer = 0   ' 0=libre  1=occupé (Interlocked)




    ' ── Timers de répétition transport ────────────────────────────────────────
    Private _fwdTimer As System.Timers.Timer
    Private _rwdTimer As System.Timers.Timer
    Dim r As Integer = 0

    ' ── Timers de retour au mode saisie pour le NumPad ────────────────────────────────────────
    Private _NumPadTimer As System.Timers.Timer

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

    ' ── Global ActionLink  ────────────────────────────────────────
    Dim UnselectAL As C24ActionLinker


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
        Public RecModeChangeH As [Delegate]
        Public RecArmBtnH As [Delegate]
        Public SelectedChangeH As [Delegate]
        Public SelectBtnH As [Delegate]

        Public AutoModeBtnH As [Delegate]
        Public SendPanBtnH As [Delegate]
        Public SwitchActiveBtnH As [Delegate]
        Public EqBtnH As [Delegate]
        Public DynBtnH As [Delegate]
        Public InsertBtnH As [Delegate]
        Public On_PlayStateChangeH As [Delegate]
    End Class
    Public Class ScribbleScriptsHandler
        Public c24Trk As Hardware.C24_ChannelStrip
        Public ScribbleTextDisplay As [Delegate]
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
        Public VpotSelectItemH As [Delegate]
        Public ValidSendBtnH As [Delegate]

    End Class
    Private Class SendPanHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip
        Public SendMonitor As Reaper.Monitors.ReaperSendMonitor
        Public SendsMonitor As Reaper.Monitors.ReaperTrackSendsMonitor
        Public SendPanEnvelope As Reaper.ReaperSendPanEnvelope

        Public SendPanChangeH As [Delegate]

        Public VpotValueChangeH As [Delegate]
        Public ReaperSendPanChangeH As [Delegate]
        Public SendPanEnvelopePanChangeH As [Delegate]

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
    Private Class MessageHandler
        Public EscBtnH As [Delegate]
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
    Public Class ChannelSettingsHandler
        Public Edit_BypBtnH As [Delegate]
        Public Ins_BypBtnH As [Delegate]
        Public Copy_SettingsBtnH As [Delegate]
        Public Paste_SettingBtnH As [Delegate]
    End Class
    Public Class RecordHandler
        Public Rec_SafeBtnH As [Delegate]
        Public Master_RecBtnH As [Delegate]
        Public On_PlayStateChangeH As [Delegate]
    End Class
    Public Class MonitorHandler
        Public MonitorBtnH As [Delegate]
    End Class
    Private Class BankHandlers

        Public BankLeftBtnH As [Delegate]
        Public BankRightBtnH As [Delegate]
        Public NudgePressBtnH As [Delegate]
        Public NudgeRealBtnH As [Delegate]

    End Class
    Private Class ShowHandlers


        Public Vel_Sens_EncodersBtnH As [Delegate]
        Public Show_ValuesBtnH As [Delegate]
        Public ScribbleScriptBackToOriginalText As [Delegate]
        Public Show_InfosBtn As [Delegate]
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
        Public VpotSelectItemH As [Delegate]
        Public ValidSendBtnH As [Delegate]
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
        Public PlayLoopAL As C24ActionLinker
        Public SelectionChangeH As [Delegate]
        Public InBtnH As [Delegate]
        Public OutBtnH As [Delegate]
        Public Goto_StartBtnH As [Delegate]
        Public Post_RollBtnH As [Delegate]
        Public Pre_RollBtnH As [Delegate]
        Public Goto_EndBtnH As [Delegate]
        Public Loop_RecBtnH As [Delegate]
        Public ReaperQuickPunchH As [Delegate]

        Public TimeValueExcedH As [Delegate]
        Public captureBtnH As [Delegate]
        Public Quick_PunchBtnH As [Delegate]
    End Class
    Public Class CounterModeHandlers
        Public CounterModeBtnH As [Delegate]
    End Class
    Public Class PeaksHandler
        Public InputPrePostFaderPeaks As C24RadioBtnHandler
        Public Clear_PeaksBtnH As [Delegate]
    End Class

    Public Class InsertStripHandler

        Public C24_trk_FX_Name1 As C24_ChannelStrip
        Public C24_trk_FX_Name2 As C24_ChannelStrip


        Public SelectBtnH As [Delegate]
        Public BypassBtnH As [Delegate]
        Public BypMasterBtnH As [Delegate]

        Public VpotSelectItemH As [Delegate]
        Public InsertParamBtnH As [Delegate]
    End Class
    Public Class FxParamVpotHandlers

        Public Monitor As ReaperFxParamMonitor
        Public FxParamEnvelope As ReaperFxParamEnvelope
        Public C24Trk As Hardware.C24_ChannelStrip

        Public SelectBtnH As [Delegate]
        Public FxParamEnvelopeValueChangeH As [Delegate]
        Public VpotValueChangeH As [Delegate]
        Public FxParamMonitorChangeH As [Delegate]

    End Class
    Public Class FxParamFaderHandlers

        Public Monitor As ReaperFxParamMonitor
        Public FxParamEnvelope As ReaperFxParamEnvelope
        Public C24Trk As Hardware.C24_ChannelStrip

        Public FxParamEnvelopeValueChangeH As [Delegate]
        Public FxParamMonitorChangeH As [Delegate]
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
    Public Class AutoModifiersHandler
        Public WritePressBtnH As [Delegate]
        Public WriteRealBtnH As [Delegate]
        Public TouchPressBtnH As [Delegate]
        Public TouchRealBtnH As [Delegate]
        Public LatchPressBtnH As [Delegate]
        Public LatchRealBtnH As [Delegate]
        Public TrimPressBtnH As [Delegate]
        Public TrimRealBtnH As [Delegate]
        Public ReadPressBtnH As [Delegate]
        Public ReadRealBtnH As [Delegate]
        Public OffPressBtnH As [Delegate]
        Public OffRealBtnH As [Delegate]

    End Class
    Public Class SendsLetterHandlers
        Public A_FBtnH As [Delegate]
        Public B_GBtnH As [Delegate]
        Public C_HBtnH As [Delegate]
        Public D_IBtnH As [Delegate]
        Public E_JBtnH As [Delegate]
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
        Public Auto_SuspendAL As C24ActionLinker
        Public Write_Auto_To_StartBtnH As [Delegate]
        Public Write_Auto_To_EndBtnH As [Delegate]
        Public Write_Auto_To_AllBtnH As [Delegate]
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
        Public OutputBtnH As [Delegate]
        Public InputBtnH As [Delegate]
        Public MasterBtnH As [Delegate]
    End Class

    Public Class FlipHandler
        Public FlipBtnH As [Delegate]
        Public EscBtnH As [Delegate]
    End Class
    Public Class AutoSelectModeHandler
        Public c24Trk As C24_ChannelStrip
        Public FxParamEnvelope As ReaperFxParamEnvelope

        Public AutoSelectBtnH As [Delegate]
        Public FxParamEnvelopeActiveChangeH As [Delegate]
        Public FxParamEnvelopeArmedChangeH As [Delegate]
        Public SelectAutoChangeH As [Delegate]


    End Class

    Public Class FxParamStripHandlers
        Public AutoSelectBtnH As [Delegate]
        Public InsertParamBtnH As [Delegate]
        Public CompareBtnH As [Delegate]
        Public Master_BypassBtnH As [Delegate]
    End Class
    Public Class ProjectManagementHandler
        Public SendsGuidsChangedH As [Delegate]
        Public TrackGuidsChangedH As [Delegate]
        Public EnvGuidsChangedH As [Delegate]
    End Class
    Public Class SessionManagementHandler
        Public ProjectChangedH As [Delegate]

    End Class
    Public Class FxparamEnvHandler
        Public FxparamEnv As ReaperFxParamEnvelope
    End Class
    Public Class OutputsStripHandler
        Public c24Trk As C24_ChannelStrip
        Public _ReaperTrackOutputsManager As ReaperTrackOutputsManager
        Public VpotSelectItemH As [Delegate]
        Public ValidSendBtnH As [Delegate]
        Public OutputBtnH As [Delegate]
    End Class
    Public Class InputsStripHandler
        Public c24Trk As C24_ChannelStrip
        Public _ReaperTrackInputsManager As ReaperTrackInputsManager
        Public VpotSelectItemH As [Delegate]
        Public ValidSendBtnH As [Delegate]
        Public InputBtnH As [Delegate]
    End Class
    Public Class MasterStripHandler
        Public MasterBtnH As [Delegate]
        Public OutputBtnH As [Delegate]
    End Class
    Public Class ZoomPresetStripHandler
        Public Preset_1BtnH As [Delegate]
        Public Preset_2BtnH As [Delegate]
        Public ZoomPreset1OnTracksRebuiltH As [Delegate]
        Public ZoomPreset2OnTracksRebuiltH As [Delegate]
    End Class
    Public Class JogWheelHandler
        Public JogWheelChangeH As [Delegate]
        Public SnapToGridAL As C24ActionLinker
        Public CtlBtnRH As [Delegate]
        Public CtlBtnPH As [Delegate]
    End Class
    Public Class C24ActionLinkerHandler
        Public C24BtnH As [Delegate]
        Public ReaperActionH As [Delegate]
    End Class
    Public Class NumPadHandler
        Public Num_CLearBtnH As [Delegate]
        Public Num_EnterBtnH As [Delegate]
        Public Num_DotBtnH As [Delegate]
        Public Num_9BtnH As [Delegate]
        Public Num_8BtnH As [Delegate]
        Public Num_7BtnH As [Delegate]
        Public Num_6BtnH As [Delegate]
        Public Num_5BtnH As [Delegate]
        Public Num_4BtnH As [Delegate]
        Public Num_3BtnH As [Delegate]
        Public Num_2BtnH As [Delegate]
        Public Num_1BtnH As [Delegate]
        Public Num_0BtnH As [Delegate]
        Public Num_MultBtnH As [Delegate]
        Public Num_DivBtnH As [Delegate]
        Public Num_SousBtnH As [Delegate]
        Public Num_AddBtnH As [Delegate]
        Public Num_EgalBtnH As [Delegate]
        Public TimerElapsedH As [Delegate]
    End Class
    Public Class UndoSaveHandler
        Public ProjectDirtyChangeH As [Delegate]
        Public UndoBtnH As [Delegate]
        Public SaveBtnH As [Delegate]
        Public AutoSaveTimer As Timers.Timer
        Public Undo_AL As C24ActionLinker
    End Class
    Public Class ChBarScreenHandler
        Public ChBarScreenH As [Delegate]
    End Class
    Public Class ScreenLineHandler
        Public ScreenLineH As [Delegate]
    End Class

    Public Class NavigationHandler
        Public UpBtnH As [Delegate]
        Public DownBtnH As [Delegate]
        Public LeftBtnH As [Delegate]
        Public RightBtnH As [Delegate]
        Public None As [Delegate]
        Public EditTools As [Delegate]
        Public SelAdj As [Delegate]
        Public Nav_C24RadioBtnHandler As C24RadioBtnHandler
    End Class
    Public Class UtilityHandler
        Public Esc_Cancel_2BtnH As [Delegate]
        Public ShuttleBtnH As [Delegate]

    End Class
    Public Class EditToolsHandler
        Public SelectionChangeH As [Delegate]
        Public Tools_SelectBtnH As [Delegate]
        Public Tools_MidiBtnH As [Delegate]
        Public SelectionActivateH As [Delegate]
        Public Tools_C24RadioBtnHandler As C24RadioBtnHandler
        Public SpotAL As C24ActionLinker
    End Class
    Public Class EditFunctionHandler
        Public CutH As [Delegate]
        Public CopyH As [Delegate]
        Public PasteH As [Delegate]
        Public Edit_DeleteH As [Delegate]
        Public SeparateH As [Delegate]
        Public DuplicateH As [Delegate]
        Public RepeatH As [Delegate]
        Public RepeatValidChangeH As [Delegate]
    End Class
    Public Class FunctionKeysHandler
        Public F1BtnH As [Delegate]
        Public F2BtnH As [Delegate]
        Public ZoomPreset1H As [Delegate]
        Public ZoomPreset2H As [Delegate]
    End Class
    Public Class GroupsHandler
        Public CreateH As [Delegate]
        Public EnableH As [Delegate]
        Public EditH As [Delegate]
        Public DeleteH As [Delegate]
        Public RepeatValidChangeH As [Delegate]
        Public SuspendAL As C24ActionLinker
    End Class
    Public Class WindowsHandler
        Public MixAL As C24ActionLinker
        Public W_PluginH As [Delegate]
        Public TransAL As C24ActionLinker
        Public StatusAL As C24ActionLinker
        Public Mem_LocAL As C24ActionLinker
        Public PlugInAL As C24ActionLinker
    End Class
#End Region







    Sub New(C24_Desk As Control24.Hardware.C24_Desk)
        _C24_Desk = C24_Desk

        ' ── SessionConfig AVANT Start() car On_ProjectChanged
        ' ── est déclenché de façon synchrone depuis ReaperSession.Start()
        _ReaperSessionConfig = New Reaper.ReaperSessionConfig

        ReaperPollHub.Instance.Start()


        _keyboardInjector = KeyboardInjectorFactory.Create()
        _clipboardService = DriverReaperControl24.Input.ClipboardServiceFactory.Create()

        _SelectedItemsManager = New SelectedItemsManager

        _ReaperSession = New Reaper.ReaperSession
        _ReaperProjectMonitor = New ReaperProjectMonitor(_ReaperSession, _SelectedItemsManager)
        AddSessionManagementHandler()
        _ReaperSession.Start()

        AddProjectManagementHandler()
        _ReaperProjectMonitor.Start()

        ReBuildMarkersRegions()
        _SelectedItemsManager.Refresh()

        DefautResumeScreensBtn = {C24_Desk.Esc_Cancel_2, C24_Desk.Num_Enter}.ToList


        ' ── Démarrage du timer VU-mètres ──────────────────────────────────────
        _PeakTimer = New System.Timers.Timer(50)
        _PeakTimer.AutoReset = True
        AddHandler _PeakTimer.Elapsed, AddressOf OnPeakTimerElapsed
        _PeakTimer.Start()

        ' ── Démarrage du Event Monitor ──────────────────────────────────────
        Reaper.Monitors.ReaperEventPipeMonitor.Start()

        ' ── Ajout des ActionLinker ──────────────────────────────────────
        UnselectAL = New C24ActionLinker(40020)
    End Sub

    '==============================================================================================================================================================================================================

#Region "Session"

    Sub AddSessionManagementHandler()
        RemoveSessionManagementHandler()

        Dim h As New SessionManagementHandler


        Dim ProjectChangedH As ReaperSession.On_ProjectChangedEventHandler =
                   Sub(ReaperProj As Reaper.ReaperProject)
                       _ReaperProj = ReaperProj
                       RefreshProject()
                       RefreshC24HomeViewMode()

                   End Sub
        h.ProjectChangedH = ProjectChangedH
        AddHandler _ReaperSession.On_ProjectChanged, ProjectChangedH

        _SessionManagementHandler.Add(h)

    End Sub
    Sub RemoveSessionManagementHandler()
        For Each h In _SessionManagementHandler
            RemoveHandler _ReaperSession.On_ProjectChanged, h.ProjectChangedH

        Next
        _SessionManagementHandler.Clear()
    End Sub


#End Region

#Region "Project"
    Sub AddProjectManagementHandler()
        RemoveProjectManagementHandler()

        Dim h As New ProjectManagementHandler

        'handler
        Dim TrackGuidsChangedH As ReaperProjectMonitor.On_TrackGuidsChangedEventHandler =
                   Sub(guids As List(Of String))
                       RefreshProject()
                       RefreshC24ViewMode()
                   End Sub
        h.TrackGuidsChangedH = TrackGuidsChangedH
        AddHandler _ReaperProjectMonitor.On_TrackGuidsChanged, TrackGuidsChangedH

        Dim SendsGuidsChangedH As ReaperProjectMonitor.On_SendGuidsChangedEventHandler =
                   Sub(TrackId As Integer)
                       RefreshProject()
                       RefreshC24ViewMode()
                   End Sub
        h.SendsGuidsChangedH = SendsGuidsChangedH
        AddHandler _ReaperProjectMonitor.On_SendGuidsChanged, SendsGuidsChangedH

        Dim EnvGuidsChangedH As ReaperProjectMonitor.On_EnvGuidsChangedEventHandler =
                   Sub(TrackId As Integer)
                       RefreshProject()
                       RefreshC24ViewMode()
                   End Sub
        h.EnvGuidsChangedH = EnvGuidsChangedH
        AddHandler _ReaperProjectMonitor.On_EnvGuidsChanged, EnvGuidsChangedH

        _ProjectManagementHandler.Add(h)

    End Sub
    Sub RemoveProjectManagementHandler()
        For Each h In _ProjectManagementHandler
            RemoveHandler _ReaperProjectMonitor.On_TrackGuidsChanged, h.TrackGuidsChangedH
            RemoveHandler _ReaperProjectMonitor.On_SendGuidsChanged, h.SendsGuidsChangedH
            RemoveHandler _ReaperProjectMonitor.On_EnvGuidsChanged, h.EnvGuidsChangedH
        Next
        _ProjectManagementHandler.Clear()
    End Sub
    Sub RefreshProject()

        ' ── Refresh Project ────────────

        ' ── Quitter la vue COURANTE avant de disposer les managers ────────────
        _LastC24ViewMode = _ActualC24ViewMode
        RemoveViewsStripHandlers()



        ' ── Dispose tous les managers / monitors  ───────────────────────────────

        If _ReaperTransportManager IsNot Nothing Then
            _ReaperTransportManager.Dispose()
            _ReaperTransportManager = Nothing
        End If

        If _ReaperTracksSendManager IsNot Nothing Then
            _ReaperTracksSendManager.Dispose()
            _ReaperTracksSendManager = Nothing
        End If

        If _ReaperTrackSendsManager IsNot Nothing Then
            _ReaperTrackSendsManager.Dispose()
            _ReaperTrackSendsManager = Nothing
        End If

        ' ── Filet de sécurité : monitor FxParam ───────────────────────────────
        If _ReaperFxParamMonitor IsNot Nothing Then
            _ReaperFxParamMonitor.Dispose()
            _ReaperFxParamMonitor = Nothing
        End If

        ' ── Libérer les anciens managers avant recréation ─────────────────────
        If _ReaperTracksManager IsNot Nothing Then
            _ReaperTracksManager.Dispose()
            _ReaperTracksManager = Nothing
        End If

        If _ReaperPeakLevelManager IsNot Nothing Then
            _ReaperPeakLevelManager.Dispose()
            _ReaperPeakLevelManager = Nothing
        End If



        ' ── recréé les managers / monitors  ───────────────────────────────



        _ReaperTracksManager = New Reaper.PageManagers.ReaperTracksManager(_ReaperProj,, _ReaperSessionConfig)

        _ReaperTransportManager = New Reaper.Managers.ReaperTransportManager
        _ReaperTransportManager.Start()

        _ReaperPeakLevelManager = New Reaper.PageManagers.ReaperPeakLevelManager()
        RefreshTrackManagerPeaks()

        'refresh Solo et solosafe dics
        RefreshDics(_ReaperProj)

        AddAllCommonC24Function()

        'refresh selection
        _SelectedItemsManager.Refresh()
        Dim selection = Reaper.utility.GetSelection
        If selection.Item1 <> -1 AndAlso selection.Item2 <> -1 Then
            _ReaperProjectMonitor.SelectionActive = True
            _ReaperProjectMonitor.SelectionStartTime = selection.Item1
            _ReaperProjectMonitor.SelectionEndTime = selection.Item2

        Else
            _ReaperProjectMonitor.SelectionActive = False
        End If


    End Sub


#End Region



#Region "Common"

    Private Sub AddCommonStripHandlers(Optional C24ViewMode As C24ViewModes = C24ViewModes.Home)
        RemoveCommonStripHandlers()

        '-------------------------Common Handlers------------------------------
        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)



                Dim h As New StripHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

                'init Rec Arm
                c24Trk.RecArm.IsBlinking = False
                If C24ViewMode <> C24ViewModes.Master Then
                    If RecSafeMode = False Then
                        c24Trk.RecArm.IsBlinking = If(reaperTrk.RecArm = 0, False, True)

                        Dim UpdateRecMaster = Sub()
                                                  If UpdateRecMasterBypass = False Then
                                                      For Each trk In _ReaperProj.Tracks
                                                          If trk.RecArm = 1 Then
                                                              If _ReaperTransportManager.IsRecording Then
                                                                  C24_Desk.Master_Rec.IsBlinking = False
                                                                  C24_Desk.Master_Rec.Led = True
                                                              Else
                                                                  C24_Desk.Master_Rec.IsBlinking = True
                                                              End If
                                                              Exit Sub
                                                          End If
                                                      Next
                                                      C24_Desk.Master_Rec.IsBlinking = False
                                                  End If

                                              End Sub

                        Dim recArmChangeH As Reaper.ReaperTrack.On_RecArmChangeEventHandler =
                            Sub(recArm As Integer)
                                If _ReaperTransportManager.IsRecording Then
                                    c24Trk.RecArm.IsBlinking = False
                                    c24Trk.RecArm.Led = recArm <> 0
                                Else
                                    c24Trk.RecArm.IsBlinking = recArm <> 0
                                End If
                                UpdateRecMaster.Invoke
                            End Sub
                        h.RecArmChangeH = recArmChangeH
                        AddHandler reaperTrk.On_RecArmChange, recArmChangeH

                        Dim recArmBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                             Sub(trackNb As Integer?)
                                 Dim NewState As Boolean = Not If(reaperTrk.RecArm = 0, False, True)
                                 _ReaperTracksManager.SetRecArm(trackNb - 1, If(NewState, "1", "0"))
                                 If _ReaperTransportManager.IsRecording Then
                                     c24Trk.RecArm.IsBlinking = False
                                     c24Trk.RecArm.Led = NewState
                                 Else
                                     c24Trk.RecArm.IsBlinking = NewState
                                 End If
                                 UpdateRecMasterBypass = False
                                 UpdateRecMaster.Invoke
                             End Sub
                        h.RecArmBtnH = recArmBtnH
                        AddHandler c24Trk.RecArm.OnButtonReleased, recArmBtnH


                        Dim On_PlayStateChangeH As ReaperTransportManager.On_PlayStateChangeEventHandler =
                             Sub(playstate As Integer)
                                 Dim status = reaperTrk.RecArm <> 0
                                 If _ReaperTransportManager.IsRecording Then
                                     c24Trk.RecArm.IsBlinking = False
                                     c24Trk.RecArm.Led = status
                                 Else
                                     c24Trk.RecArm.IsBlinking = status
                                 End If
                             End Sub
                        h.On_PlayStateChangeH = On_PlayStateChangeH
                        AddHandler _ReaperTransportManager.On_PlayStateChange, On_PlayStateChangeH

                    Else
                        'Rec Safe Mode
                        'init
                        c24Trk.RecArm.Led = If(reaperTrk.RecMode = 2, True, False)

                        Dim RecModeChangeH As Reaper.ReaperTrack.On_RecModeChangeEventHandler =
                                 Sub(_recmode As Integer)
                                     c24Trk.RecArm.IsBlinking = False
                                     c24Trk.RecArm.Led = If(_recmode = 2, True, False)
                                 End Sub
                        h.RecModeChangeH = RecModeChangeH
                        AddHandler reaperTrk.On_RecModeChange, RecModeChangeH

                        Dim recArmBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        Dim NewState As Boolean = Not If(reaperTrk.RecMode = 2, True, False)
                        _ReaperTracksManager.SetRecMode(trackNb - 1, If(NewState, "2", "0"))
                        c24Trk.RecArm.IsBlinking = False
                        c24Trk.RecArm.Led = NewState
                    End Sub
                        h.RecArmBtnH = recArmBtnH
                        AddHandler c24Trk.RecArm.OnButtonReleased, recArmBtnH
                    End If
                End If

                c24Trk.Select.Led = reaperTrk.Selected
                Dim selectedChangeH As Reaper.ReaperTrack.On_SelectedChangeEventHandler =
                Sub(selected As Integer)
                    c24Trk.Select.Led = selected <> 0
                    If c24Trk.Select.Led Then
                        Reaper.utility.Main_OnCommand(40913)
                    End If
                End Sub
                h.SelectedChangeH = selectedChangeH
                AddHandler reaperTrk.On_SelectedChange, selectedChangeH

                Dim selectBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub(trackNb As Integer?)

                    If reaperTrk.Selected = 0 Then
                        _ReaperTracksManager.SetSelect(trackNb - 1, 1)
                        c24Trk.Select.Led = True
                        Reaper.utility.Main_OnCommand(40913)

                    Else
                        _ReaperTracksManager.SetSelect(trackNb - 1, 0)
                        c24Trk.Select.Led = False
                    End If
                    If C24_Desk.Default.IsPressed Then
                        c24Trk.Fader.SetInitFaderDbVolume(0)
                    End If
                End Sub
                h.SelectBtnH = selectBtnH
                AddHandler c24Trk.Select.OnButtonReleased, selectBtnH

                c24Trk.AutoModeLed.IsBlinking = False
                c24Trk.AutoModeLed.AutoMode = GetC24AutoMode(reaperTrk.AutoMode)
                If LastReaperTrackIndexAutomation IsNot Nothing AndAlso LastReaperTrackIndexAutomation.Value = reaperTrk.TrackIndex Then
                    c24Trk.AutoModeLed.AutoMode = AutoMode.All
                    c24Trk.AutoModeLed.IsBlinking = True

                End If
                Dim autoModeBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        If Shift Then



                            'c24Trk = C24_Desk.ChannelStrips(trackNb - 1)




                            'remet le mode normal sur la piste en mode automation précédente
                            If LastReaperTrackIndexAutomation IsNot Nothing Then
                                If LastReaperTrackIndexAutomation.Value = reaperTrk.TrackIndex Then
                                    'eteind la piste actuelle
                                    c24Trk.AutoModeLed.IsBlinking = False
                                    c24Trk.AutoModeLed.AutoMode = GetC24AutoMode(reaperTrk.AutoMode)
                                    'Envelope: Hide all envelopes for all tracks
                                    Reaper.utility.Main_OnCommand(41150)
                                    JogWheelToolMode = LastJogWheelToolMode
                                    NavigationMode = LastNavigationMode
                                    LastReaperTrackIndexAutomation = Nothing
                                    Exit Sub
                                Else
                                    'eteind une autre piste
                                    'Envelope: Hide all envelopes for all tracks
                                    Reaper.utility.Main_OnCommand(41150)
                                    JogWheelToolMode = LastJogWheelToolMode
                                    NavigationMode = LastNavigationMode

                                    Dim _c24TrkIndex = _ReaperTracksManager.GetRelativeTrackIndex(LastReaperTrackIndexAutomation.Value)
                                    If _c24TrkIndex Is Nothing Then
                                        'la piste n'est pas a l'affichage de C24
                                    Else
                                        Dim _c24Trk = C24_Desk.ChannelStrips(_c24TrkIndex.Value)
                                        'eteind la piste actuelle
                                        _c24Trk.AutoModeLed.IsBlinking = False
                                        _c24Trk.AutoModeLed.AutoMode = GetC24AutoMode(reaperTrk.AutoMode)
                                    End If

                                End If

                            End If



                            'open Automation section on reaper and activate Automation navigation inside track
                            c24Trk.AutoModeLed.AutoMode = AutoMode.All
                            c24Trk.AutoModeLed.IsBlinking = True
                            LastReaperTrackIndexAutomation = reaperTrk.TrackIndex
                            'deselect toutes les tracks
                            Reaper.utility.Main_OnCommand(40297)
                            'selectionne la track 
                            _ReaperTracksManager.SetSelect(trackNb - 1, 1)
                            c24Trk.Select.Led = True

                            'Envelope: Show all envelopes for tracks
                            Reaper.utility.Main_OnCommand_SafeUI(41148)
                            LastJogWheelToolMode = JogWheelToolMode
                            LastNavigationMode = NavigationMode
                            JogWheelToolMode = JogWheelToolModes.Automation
                            NavigationMode = NavigationModes.Automation
                            Reaper.utility.Main_OnCommand_SafeUI(40913)




                        ElseIf Ctrl Then
                            'Ouvre et ferme les enveloppes sur la track
                            Reaper.utility.Main_OnCommand(41151)
                        Else
                            If Write Then
                                On_AutoModeChange(trackNb, AutoMode.Write)
                            ElseIf Touch Then
                                On_AutoModeChange(trackNb, AutoMode.Touch)
                            ElseIf Latch Then
                                On_AutoModeChange(trackNb, AutoMode.Latch)
                            ElseIf Trim Then
                                On_AutoModeChange(trackNb, AutoMode.Trim)
                            ElseIf Read Then
                                On_AutoModeChange(trackNb, AutoMode.Read)
                            ElseIf Off Then
                                On_AutoModeChange(trackNb, AutoMode.None)
                            Else
                                Dim idx As Integer = Hardware.Utility.AutoModeButtonToOrder(c24Trk.AutoModeLed.AutoMode) + 1
                                If idx = 6 Then
                                    idx = 0
                                End If
                                On_AutoModeChange(trackNb, Hardware.Utility.OrderToAutoModeButton(idx))
                            End If
                        End If

                    End Sub
                h.AutoModeBtnH = autoModeBtnH
                AddHandler c24Trk.AutoMode.OnButtonReleased, autoModeBtnH

                c24Trk.SendPanMode.IsBlinking = False
                If C24ViewMode <> C24ViewModes.Master Then

                    Dim sendPanBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        If c24Trk.SendPanMode.Led Then
                            c24Trk.SendPanMode.Led = False
                            RefreshC24ViewMode(C24ViewMode)
                        Else
                            RefreshC24PanSendViewMode(trackNb)
                        End If
                        c24Trk.SendPanMode.Led = Not c24Trk.SendPanMode.Led
                    End Sub
                    h.SendPanBtnH = sendPanBtnH
                    AddHandler c24Trk.SendPanMode.OnButtonReleased, sendPanBtnH
                End If



                If EqDynBypassMode Then
                    Dim _FXBypassStatus = _ReaperTracksManager.IsTrackEqFxBypassed(reaperTrk.TrackIndex)
                    Select Case _FXBypassStatus
                        Case ReaperTracksManager.FXBypassStatus.AllBypass
                            c24Trk.EqMode.Led = True
                        Case ReaperTracksManager.FXBypassStatus.NoBypass
                            c24Trk.EqMode.IsBlinking = False
                        Case ReaperTracksManager.FXBypassStatus.SomeBypass
                            c24Trk.EqMode.IsBlinking = True
                    End Select

                Else
                    c24Trk.EqMode.Led = Not _ReaperTracksManager.IsTrackEqFxEmpty(reaperTrk.TrackIndex)
                End If

                Dim eqBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        If EqDynBypassMode Then

                            If c24Trk.EqMode.IsBlinking Then
                                'Bypass tout
                                _ReaperTracksManager.SetTrackEqFxBypassed(reaperTrk.TrackIndex, True)

                            ElseIf c24Trk.EqMode.Led Then
                                'Bypass rien
                                _ReaperTracksManager.SetTrackEqFxBypassed(reaperTrk.TrackIndex, False)
                            Else
                                'Bypass tout
                                _ReaperTracksManager.SetTrackEqFxBypassed(reaperTrk.TrackIndex, True)
                            End If

                        Else
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
                                RefreshC24ViewMode(C24ViewMode)
                            Else

                                c24Trk.EqMode.IsBlinking = True
                                SelectedFx = EqFx
                                RefreshC24FxParamViewMode(SelectedFx)

                            End If
                        End If


                    End Sub
                h.EqBtnH = eqBtnH
                AddHandler c24Trk.EqMode.OnButtonReleased, eqBtnH


                If EqDynBypassMode Then
                    Dim _FXBypassStatus = _ReaperTracksManager.IsTrackDynFxBypassed(reaperTrk.TrackIndex)
                    Select Case _FXBypassStatus
                        Case ReaperTracksManager.FXBypassStatus.AllBypass
                            c24Trk.DynMode.Led = True
                        Case ReaperTracksManager.FXBypassStatus.NoBypass
                            c24Trk.DynMode.IsBlinking = False
                        Case ReaperTracksManager.FXBypassStatus.SomeBypass
                            c24Trk.DynMode.IsBlinking = True
                    End Select

                Else
                    c24Trk.DynMode.Led = Not _ReaperTracksManager.IsTrackDynFxEmpty(reaperTrk.TrackIndex)
                End If


                Dim dynBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        If EqDynBypassMode Then
                            If c24Trk.DynMode.IsBlinking Then
                                'Bypass tout
                                _ReaperTracksManager.SetTrackDynFxBypassed(reaperTrk.TrackIndex, True)

                            ElseIf c24Trk.DynMode.Led Then
                                'Bypass rien
                                _ReaperTracksManager.SetTrackDynFxBypassed(reaperTrk.TrackIndex, False)
                            Else
                                'Bypass tout
                                _ReaperTracksManager.SetTrackDynFxBypassed(reaperTrk.TrackIndex, True)
                            End If
                        Else
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
                                RefreshC24ViewMode(C24ViewMode)
                            Else
                                'RefreshEqFxModeLed()
                                c24Trk.DynMode.IsBlinking = True
                                SelectedFx = DynFx
                                RefreshC24FxParamViewMode(SelectedFx)

                            End If
                        End If

                    End Sub
                h.DynBtnH = dynBtnH
                AddHandler c24Trk.DynMode.OnButtonReleased, dynBtnH


                If InsertBypassMode Then
                    Dim _FXBypassStatus = _ReaperTracksManager.IsTrackGenericFxBypassed(reaperTrk.TrackIndex)
                    Select Case _FXBypassStatus
                        Case ReaperTracksManager.FXBypassStatus.AllBypass
                            c24Trk.InsertMode.Led = True
                        Case ReaperTracksManager.FXBypassStatus.NoBypass
                            c24Trk.InsertMode.IsBlinking = False
                        Case ReaperTracksManager.FXBypassStatus.SomeBypass
                            c24Trk.InsertMode.IsBlinking = True
                    End Select

                Else
                    c24Trk.InsertMode.Led = Not _ReaperTracksManager.IsTrackGenericFxEmpty(reaperTrk.TrackIndex)
                End If


                Dim insertBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        If InsertBypassMode Then
                            If c24Trk.InsertMode.IsBlinking Then
                                'Bypass tout
                                _ReaperTracksManager.SetTrackGenericFxBypassed(reaperTrk.TrackIndex, True)

                            ElseIf c24Trk.InsertMode.Led Then
                                'Bypass rien
                                _ReaperTracksManager.SetTrackGenericFxBypassed(reaperTrk.TrackIndex, False)
                            Else
                                'Bypass tout
                                _ReaperTracksManager.SetTrackGenericFxBypassed(reaperTrk.TrackIndex, True)
                            End If
                        Else
                            If c24Trk.InsertMode.IsBlinking Then
                                c24Trk.InsertMode.IsBlinking = False

                                c24Trk.InsertMode.Led = Not _ReaperTracksManager.IsTrackGenericFxEmpty(reaperTrk.TrackIndex)
                                RefreshC24ViewMode(C24ViewMode)
                            Else
                                RefreshInsertModeLed()
                                c24Trk.InsertMode.IsBlinking = True
                                RefreshC24InsertViewMode(_ReaperTracksManager.GetAbsoluteTrackIndex(trackNb - 1))
                            End If
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


            RemoveHandler r.On_RecModeChange, h.RecModeChangeH
            RemoveHandler r.On_RecArmChange, h.RecArmChangeH
            RemoveHandler c.RecArm.OnButtonReleased, h.RecArmBtnH
            RemoveHandler r.On_SelectedChange, h.SelectedChangeH
            RemoveHandler c.Select.OnButtonReleased, h.SelectBtnH

            RemoveHandler c.AutoMode.OnButtonReleased, h.AutoModeBtnH
            RemoveHandler c.SendPanMode.OnButtonReleased, h.SendPanBtnH

            RemoveHandler c.EqMode.OnButtonReleased, h.EqBtnH
            RemoveHandler c.DynMode.OnButtonReleased, h.DynBtnH
            RemoveHandler c.InsertMode.OnButtonReleased, h.InsertBtnH
            RemoveHandler _ReaperTransportManager.On_PlayStateChange, h.On_PlayStateChangeH
        Next
        _CommonHandlers.Clear()
    End Sub

    Sub AddAllCommonC24Function()
        RemoveAllCommonC24Function()

        'Add C24 Handlers and Reaper C24 tracks

        AddMutesBtnHandlers()
        AddSolosHandlers()
        AddSolosClearSafeHandlers()


        AddBankHandlers()
        AddTransportHandlers()
        AddRecordHandlers()
        AddChannelSettingsHandlers()
        AddMonitorHandlers()
        AddShowC24Handlers()
        AddScribbleScriptsHandlers()
        AddCounterModeHandler()
        AddPeaksHandler()
        AddModifiersHandlers()
        AddAutomationModifers()
        AddSendsLetterHandlers()
        AddAutomationEnableHandlers()
        AddZoomPresetHandlers()
        AddJogWheelHandler()
        AddNumPadHandler()
        AddUndoSaveHandler()
        AddNavigationHandler()
        AddUtilityHandler()
        AddEditToolsHandler()
        AddFunctionKeysHandlers()
        AddEditFunctionHandler()
        AddGroupsHandler()
        AddWindowsHandler()



        'Add Reaper all tracks Handlers
        AddReaperEnvArmedHandlers()
        AddReaperSoloHandlers()
        AddReaperAutoModeHandlers()


    End Sub
    Sub RemoveAllCommonC24Function()
        RemoveMutesBtnHandlers()
        RemoveSolosHandlers()
        RemoveSolosClearSafeHandlers()


        RemoveBankHandlers()
        RemoveTransportHandlers()
        RemoveRecordHandlers()
        RemoveChannelSettingsHandlers()
        RemoveMonitorHandlers()
        RemoveShowC24Handlers()
        RemoveScribbleScriptsHandlers()
        RemoveCounterModeHandler()
        RemovePeaksHandler()
        RemoveModifiersHandlers()
        RemoveAutomationModifers()
        RemoveSendsLetterHandlers()
        RemoveAutomationEnableHandlers()
        RemoveZoomPresetHandlers()
        RemoveJogWheelHandler()
        RemoveNumPadHandler()
        RemoveUndoSaveHandler()
        RemoveNavigationHandler()
        RemoveUtilityHandler()
        RemoveEditToolsHandler()
        RemoveFunctionKeysHandlers()
        RemoveEditFunctionHandler()
        RemoveGroupsHandler()
        RemoveWindowsHandler()


        'Remove Reaper all tracks Handlers
        RemoveReaperEnvArmedHandlers()
        RemoveReaperSoloHandlers()
        RemoveReaperAutoModeHandlers()
    End Sub

    ''' <summary>Remet un channel strip à l'état vide (aucune piste assignée).</summary>
    Private Sub ClearChannelStripValues(c24Trk As Hardware.C24_ChannelStrip)
        c24Trk.ScribbleStrip.Text = ""
        c24Trk.Fader.SetInitFaderDbVolume(-150.0)
        c24Trk.Vpot.SetLedsOff()
        c24Trk.LedSwitchActive.IsBlinking = False
        c24Trk.Mute.IsBlinking = False
        c24Trk.Solo.IsBlinking = False
        c24Trk.RecArm.IsBlinking = False
        c24Trk.Select.IsBlinking = False
        c24Trk.SendPanMode.IsBlinking = False
        c24Trk.EqMode.IsBlinking = False
        c24Trk.DynMode.IsBlinking = False
        c24Trk.InsertMode.IsBlinking = False
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
    Sub AddFadersVolumeHandlers(Optional InitScribbleStripText As Boolean = False, Optional ClearUnUsedCs As Boolean = True)
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

                    'init
                    If TrackVolEnvelope.Armed Then
                        C24_Desk.Auto_Fader.Led = True
                    End If

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
                If ClearUnUsedCs Then
                    ClearChannelStripValues(c24Trk)
                Else
                    c24Trk.Fader.SetInitFaderDbVolume(-150.0)
                End If


            End If
        Next
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
    Sub AddVpotVolumeHandlers(Optional InitScribbleStripText As Boolean = False, Optional ClearUnUsedCs As Boolean = True)
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

                    'init
                    If TrackVolEnvelope.Armed Then
                        C24_Desk.Auto_Fader.Led = True
                    End If

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
                If ClearUnUsedCs Then
                    ClearChannelStripValues(c24Trk)
                End If

            End If
        Next
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
                            Dim Faderc24Trk = Nothing
                            Dim RelTrackIndex = _ReaperTracksManager.GetRelativeTrackIndex(TrackIndex)
                            If RelTrackIndex >= 0 Then
                                Faderc24Trk = C24_Desk.ChannelStrips(RelTrackIndex)
                            End If
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

            'init
            If TrackVolEnvelope.Armed Then
                C24_Desk.Auto_Fader.Led = True
            End If

            Dim TrackVolEnvelopeVolChangeH As Reaper.ReaperTrackVolEnvelope.On_TrackUIVolChangeEventHandler =
                    Sub(_vol As Double)
                        Dim Faderc24Trk = Nothing
                        Dim RelTrackIndex = _ReaperTracksManager.GetRelativeTrackIndex(ReaperTrk.TrackIndex)
                        If RelTrackIndex >= 0 Then
                            Faderc24Trk = C24_Desk.ChannelStrips(RelTrackIndex)
                        End If
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
                        Dim RelTrkIndex = _ReaperTracksManager.GetRelativeTrackIndex(ReaperTrk.TrackIndex)
                        ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                        Select Case ReaperTrk.AutoMode
                            Case 0
                                _ReaperTracksManager.SetVolume(RelTrkIndex, Value)
                            Case Else
                                If TrackVolEnvelope IsNot Nothing Then
                                    _ReaperTracksManager.SuspendTrack(RelTrkIndex)
                                    TrackVolEnvelope.Value = Value
                                    _ReaperTracksManager.ResumeTrack(RelTrkIndex)

                                End If

                        End Select

                    End Sub
        h.VpotValueChangeH = VpotValueChangeH
        AddHandler c24Trk.Vpot.OnVpotValueChange, VpotValueChangeH

        _VpotVolumeHandlers.Add(h)


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

                    'init
                    If TrackPanEnvelope.Armed Then
                        C24_Desk.Auto_Fader.Led = True
                    End If

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
                    'init
                    If TrackPanEnvelope.Armed Then
                        C24_Desk.Auto_Fader.Led = True
                    End If
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
                'c24Trk.Vpot.RefreshLedStatus()

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
                    .ReaperTrk = ReaperTrk,
                    .C24Trk = c24Trk
                }

        Dim TrackPanEnvelope As ReaperTrackPanEnvelope = If(_ReaperTracksManager.PanEnvelopes.ContainsKey(ReaperTrk.TrackIndex), _ReaperTracksManager.PanEnvelopes(ReaperTrk.TrackIndex), Nothing)
        If TrackPanEnvelope IsNot Nothing Then
            _C24trkPanEnvelope.Add(c24Trk, TrackPanEnvelope)
            h.TrackPanEnvelope = TrackPanEnvelope
            'init
            If TrackPanEnvelope.Armed Then
                C24_Desk.Auto_Fader.Led = True
            End If
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
        c24Trk.Vpot.Value = ReaperTrk.Pan



        Dim VpotValueChangeH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                    Sub(trackNb As Integer, value As Double)
                        'Automation
                        Dim RelTrkIndex = _ReaperTracksManager.GetRelativeTrackIndex(ReaperTrk.TrackIndex)
                        If TrackPanEnvelope IsNot Nothing Then
                            TrackPanEnvelope.Suspend_events = True
                        End If
                        ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                        Select Case ReaperTrk.AutoMode
                            Case 0, 1
                                _ReaperTracksManager.SetPan(RelTrkIndex, value)
                                '

                            Case Else
                                If TrackPanEnvelope IsNot Nothing Then
                                    _ReaperTracksManager.SuspendTrack(RelTrkIndex)
                                    TrackPanEnvelope.Value = value
                                    _ReaperTracksManager.ResumeTrack(RelTrkIndex)
                                End If
                        End Select

                        If TrackPanEnvelope IsNot Nothing Then
                            TrackPanEnvelope.Suspend_events = False
                        End If

                    End Sub
        h.VpotValueChangeH = VpotValueChangeH
        AddHandler c24Trk.Vpot.OnVpotValueChange, VpotValueChangeH





        _VpotPanHandlers.Add(h)





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
                MuteLedChange(reaperTrk.TrackIndex, reaperTrk.Mute)

                Dim muteBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)

                        'Automation
                        If TrackMuteEnvelope IsNot Nothing Then
                            TrackMuteEnvelope.Suspend_events = True
                        End If
                        ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                        Select Case reaperTrk.AutoMode

                            Case 0, 1
                                _ReaperTracksManager.Set_MuteStatus(trackNb - 1, Not reaperTrk.Mute)
                                'Console.WriteLine("Mute " & If(reaperTrk.Mute, "On", "Off"))

                            Case Else
                                If TrackMuteEnvelope IsNot Nothing Then
                                    _ReaperTracksManager.SuspendTrack(trackNb - 1)
                                    TrackMuteEnvelope.Value = If(TrackMuteEnvelope.Value = 1, 0, 1)
                                    'Console.WriteLine("Mute env")
                                    'Console.WriteLine("Mute " & If(reaperTrk.Mute, "On", "Off"))
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
            If i < _ReaperTracksManager.Tracks.Count Then

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
                    h.SendVolEnvelope = SendVolEnvelope
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
                                 Case 0
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

    End Sub
    Sub RemoveFadersSendVolumeHandlers()

        For Each h In _FaderSendVolumeHandlers
            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.Fader.On_FaderHumanChangeDbValue, h.FaderHumanH


                If h.SendVolEnvelope IsNot Nothing Then
                    RemoveHandler h.SendVolEnvelope.On_SendUIVolChange, h.SendVolEnvelopeVolChangeH
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
        If Assign_Mode = False Then
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
        End If



        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)
            If i < _ReaperTracksManager.Tracks.Count Then

                Dim reaperTrk = _ReaperTracksManager.Tracks(i)
                Dim send As ReaperSend = Nothing
                send = _ReaperTracksSendManager.Sends(i)


                'Dim _ReaperTrackSendsEditor = New Reaper.PageManagers.ReaperTrackSendsEditor(reaperTrk.TrackIndex, _ReaperProj.Tracks)
                If Assign_Mode = False Then
                    If send Is Nothing Then
                        ClearSendChannelStripValues(c24Trk)

                        Continue For
                    Else
                        'init values
                        If InitScribbleStripText Then
                            c24Trk.ScribbleStrip.Text = send.Name
                            'Console.WriteLine(c24Trk.ScribbleStrip.Text)
                        End If
                    End If

                    'init Vpot value
                    c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.VolumeLinear
                    c24Trk.Vpot.Value = send.Vol
                Else
                    'Assign Mode
                    Dim SendListNames As List(Of String) = _ReaperTracksSendManager.ReaperTracksSendsEditor(i).GetVpotSendsList

                    Dim CheckList As New List(Of String)
                    If send Is Nothing Then
                        CheckList.Add(" -  ")
                    Else
                        CheckList.Add(send.Name)
                    End If

                    c24Trk.Vpot.ChannelScribbleScreen = C24_Desk.ScreenChannelBarScroll
                    c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.TextListWithChannelBar
                    c24Trk.Vpot.InitTextList(SendListNames, CheckList)
                    c24Trk.Vpot.SelectItemByText(CheckList.First)
                    c24Trk.LedSwitchActive.Led = False

                End If


                Dim h As New SendVolumeHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

                If Assign_Mode = False Then
                    Dim SendVolEnvelope As ReaperSendVolEnvelope = Nothing
                    For Each _SendVolEnvelope In reaperTrk.Envelopes.AllOfType(Of ReaperSendVolEnvelope)
                        If _SendVolEnvelope.SendId = send.SendIndex Then
                            SendVolEnvelope = _SendVolEnvelope
                            Exit For
                        End If
                    Next
                    If SendVolEnvelope IsNot Nothing Then
                        _C24trkSendVolEnvelope.Add(c24Trk, SendVolEnvelope)
                        h.SendVolEnvelope = SendVolEnvelope
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
                    'Vpot Assign handler

                    Dim _ReaperTrackSendsEditor = _ReaperTracksSendManager.ReaperTracksSendsEditor(reaperTrk.TrackIndex)

                    Dim VpotSelectItemH As Hardware.C24_Vpot.OnVpotSelectedItemChangeEventHandler =
                        Sub(Track_Nb As Integer, selectedIndex As Integer)
                            If c24Trk.LedSwitchActive.Led = False Then
                                'ajoute la validation si modification de choix dans la liste
                                c24Trk.SwitchActive.IsBlinking = True
                                c24Trk.LedSwitchActive.Led = True

                                Dim ssh As New SendVolumeHandlers
                                ssh.C24Trk = c24Trk
                                Dim ValidSendBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                                    Sub(_TrackNb As Integer?)
                                        c24Trk.Vpot.CheckAllItems(False)
                                        c24Trk.Vpot.CheckSelectedItem(True)
                                        Dim NewSendName = c24Trk.Vpot.GetCheckedItems.First

                                        If NewSendName = " -  " Then
                                            'retire le send actuel de la lettre
                                            _ReaperTrackSendsEditor.AddUpdateSendOrder(_ReaperTracksSendManager.SendLetter, Nothing)
                                        ElseIf NewSendName.StartsWith("+") Then
                                            'ajoute un send et l'enregistre sur la lettre
                                            NewSendName = NewSendName.Remove(0, 1)
                                            Dim AvailableSendIndex = _ReaperTrackSendsEditor.AvailableDestinationsIndexByName(NewSendName)
                                            If AvailableSendIndex >= 0 Then
                                                _ReaperTrackSendsEditor.AddSend(AvailableSendIndex, _ReaperTracksSendManager.SendLetter)
                                            End If
                                        ElseIf NewSendName.StartsWith("-") Then
                                            'retire un send, met à jour la liste des sends et le retire si besoin de la lettre
                                            NewSendName = NewSendName.Remove(0, 1)
                                            Dim RemoveSendIndex = _ReaperTrackSendsEditor.SendsIndexByName(send.Name)
                                            If RemoveSendIndex >= 0 Then
                                                _ReaperTrackSendsEditor.RemoveSend(RemoveSendIndex, _ReaperTracksSendManager.SendLetter)
                                            End If
                                        Else
                                            'met le send sur la lettre 
                                            _ReaperTrackSendsEditor.AddUpdateSendOrder(_ReaperTracksSendManager.SendLetter, NewSendName)
                                        End If



                                        c24Trk.SwitchActive.IsBlinking = False
                                        c24Trk.LedSwitchActive.Led = False

                                        RemoveHandler c24Trk.SwitchActive.OnButtonReleased, ValidSendBtnH
                                    End Sub
                                ssh.ValidSendBtnH = ValidSendBtnH
                                AddHandler c24Trk.SwitchActive.OnButtonReleased, ValidSendBtnH
                                _VpotSendVolumeHandlers.Add(ssh)

                            End If

                        End Sub
                    h.VpotSelectItemH = VpotSelectItemH
                    AddHandler c24Trk.Vpot.OnVpotSelectedItemChange, VpotSelectItemH
                    _VpotSendVolumeHandlers.Add(h)
                End If

            End If
        Next

    End Sub
    Sub RemoveVpotSendVolumeHandlers()

        For Each h In _VpotSendVolumeHandlers
            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.Vpot.OnVpotValueChange, h.VpotValueChangeH
                RemoveHandler h.C24Trk.SwitchActive.OnButtonReleased, h.ValidSendBtnH
                RemoveHandler h.C24Trk.Vpot.OnVpotSelectedItemChange, h.VpotSelectItemH

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

    Sub AddVpotSendsVolumeHandlers(c24TrkList As List(Of C24_ChannelStrip), SendLetterList As List(Of String))
        RemoveVpotSendsVolumeHandlers()

        Dim _ReaperTrackSendsEditor = _ReaperTrackSendsManager.ReaperTrackSendsEditor
        Dim SendsInOrder As New List(Of ReaperSend)
        For Each SendLetter In SendLetterList
            SendsInOrder.Add(_ReaperTrackSendsEditor.GetSend(SendLetter))
        Next
        _C24trkSendVolEnvelope.Clear()

        Dim sh As New SendVolumeHandlers
        sh.SendsMonitor = _ReaperTrackSendsManager.Monitor
        Dim ReaperSendVolChangeH As Reaper.Monitors.ReaperTrackSendsMonitor.On_VolChangeEventHandler =
                    Sub(SendIndex As Integer, vol As Double)
                        If SendIndex < _ReaperTrackSendsManager.Sends.Count Then
                            Dim send = _ReaperTrackSendsManager.Sends(SendIndex)
                            Dim c24TrkIndex = -1
                            For i = 0 To SendsInOrder.Count - 1
                                If SendsInOrder(i).Name = send.Name Then
                                    c24TrkIndex = i
                                    Exit For
                                End If
                            Next
                            If c24TrkIndex <> -1 Then
                                Dim c24Trk = c24TrkList(c24TrkIndex)
                                'Dim PageSendIndex = _ReaperTrackSendsManager.GetRelativeSendIndex(SendIndex)
                                _ReaperTrackSendsManager.SuspendSend(c24TrkIndex)
                                c24Trk.Vpot.Value = vol
                                _ReaperTrackSendsManager.ResumeSend(c24TrkIndex)
                            End If
                        End If
                    End Sub
        sh.ReaperSendVolChangeH = ReaperSendVolChangeH
        AddHandler _ReaperTrackSendsManager.Monitor.On_VolChange, ReaperSendVolChangeH
        _VpotSendVolumeHandlers.Add(sh)



        For i = 0 To c24TrkList.Count - 1
            Dim c24Trk = c24TrkList(i)

            Dim SendLetter = SendLetterList(i)
            Dim trackIndexSend = _ReaperTrackSendsManager.TrackIndex
            Dim reaperTrk = If(trackIndexSend = -1, _ReaperProj.MasterTrack, _ReaperProj.Tracks(trackIndexSend))
            Dim send As ReaperSend = _ReaperTrackSendsManager.ReaperTrackSendsEditor.GetSend(SendLetter)




            If send Is Nothing Then
                c24Trk.Vpot.SetLedsOff()
                Continue For

            End If

            Dim h As New SendVolumeHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


            Dim SendVolEnvelope As ReaperSendVolEnvelope = Nothing
            _ReaperTrackSendsManager.VolEnvelopes.TryGetValue(send.SendIndex, SendVolEnvelope)

            If SendVolEnvelope IsNot Nothing Then
                _C24trkSendVolEnvelope.Add(c24Trk, SendVolEnvelope)
                h.SendVolEnvelope = SendVolEnvelope
                Dim SendVolEnvelopeVolChangeH As Reaper.ReaperSendVolEnvelope.On_SendUIVolChangeEventHandler =
                    Sub(_vol As Double)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        SendVolEnvelope.Suspend_events = True
                        '_ReaperTrackSendsManager.SuspendSend(PageSendIndex)
                        c24Trk.Vpot.Value = _vol
                        'Console.WriteLine(c24Trk.TrackNumber & ";" & SendVolEnvelope.SendId & ";" & _vol)
                        '_ReaperTrackSendsManager.ResumeSend(PageSendIndex)
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
                                         _ReaperTrackSendsManager.SuspendSend(send.SendIndex)

                                         SendVolEnvelope.Value = Value
                                         _ReaperTrackSendsManager.ResumeSend(send.SendIndex)
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

#End Region

#Region "Send Pan"
    Sub AddVpotSendPanHandlers(Optional InitScribbleStripText As Boolean = False)
        RemoveVpotSendPanHandlers()


        Dim sh As New SendPanHandlers
        sh.SendMonitor = _ReaperTracksSendManager._SendMonitor
        Dim ReaperSendPanChangeH As Reaper.Monitors.ReaperSendMonitor.On_PanChangeEventHandler =
                    Sub(TrackIndex As Integer, pan As Double)
                        Dim c24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(TrackIndex))
                        _ReaperTracksSendManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        c24Trk.Vpot.Value = pan
                        _ReaperTracksSendManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
        sh.ReaperSendPanChangeH = ReaperSendPanChangeH
        AddHandler _ReaperTracksSendManager._SendMonitor.On_PanChange, ReaperSendPanChangeH
        _VpotSendPanHandlers.Add(sh)



        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)
            If i < _ReaperTracksManager.Tracks.Count Then

                Dim reaperTrk = _ReaperTracksManager.Tracks(i)
                Dim send As ReaperSend = Nothing
                send = _ReaperTracksSendManager.Sends(i)




                If send Is Nothing Then
                    ClearSendChannelStripValues(c24Trk)
                    'Console.WriteLine(" -  ")
                    Continue For
                Else
                    'init values
                    If InitScribbleStripText Then

                        Dim _ReaperTrackSendsEditor = New Reaper.PageManagers.ReaperTrackSendsEditor(reaperTrk.TrackIndex, _ReaperProj.Tracks)
                        c24Trk.ScribbleStrip.Text = _ReaperTrackSendsEditor.Sends_Names(send.SendIndex)
                        'Console.WriteLine(c24Trk.ScribbleStrip.Text)
                    End If
                End If

                Dim h As New SendPanHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


                Dim SendPanEnvelope As ReaperSendPanEnvelope = Nothing
                For Each _SendPanEnvelope In reaperTrk.Envelopes.AllOfType(Of ReaperSendPanEnvelope)
                    If _SendPanEnvelope.SendId = send.SendIndex Then
                        SendPanEnvelope = _SendPanEnvelope
                        Exit For
                    End If
                Next
                If SendPanEnvelope IsNot Nothing Then
                    _C24trkSendPanEnvelope(c24Trk) = SendPanEnvelope
                    h.SendPanEnvelope = SendPanEnvelope
                    Dim SendPanEnvelopePanChangeH As Reaper.ReaperSendPanEnvelope.On_SendUIPanChangeEventHandler =
                    Sub(_pan As Double)

                        _ReaperTracksManager.SuspendTrack(c24Trk.TrackNumber - 1)
                        SendPanEnvelope.Suspend_events = True
                        c24Trk.Vpot.Value = _pan

                        SendPanEnvelope.Suspend_events = False
                        _ReaperTracksManager.ResumeTrack(c24Trk.TrackNumber - 1)
                    End Sub
                    h.SendPanEnvelopePanChangeH = SendPanEnvelopePanChangeH
                    AddHandler SendPanEnvelope.On_SendUIPanChange, SendPanEnvelopePanChangeH


                End If


                'init Vpot value
                c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.Pan
                c24Trk.Vpot.Value = send.Pan

                Dim VpotValueChangeH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                         Sub(TrackNb As Integer, Value As Double)

                             'Automation
                             If SendPanEnvelope IsNot Nothing Then
                                 SendPanEnvelope.Suspend_events = True
                             End If
                             ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                             Select Case reaperTrk.AutoMode
                                 Case 0, 1
                                     send.Pan = Value
                                 Case Else
                                     SendPanEnvelope = _C24trkSendPanEnvelope(c24Trk)
                                     If SendPanEnvelope IsNot Nothing Then
                                         _ReaperTracksManager.SuspendTrack(TrackNb - 1)
                                         SendPanEnvelope.Value = Value
                                         _ReaperTracksManager.ResumeTrack(TrackNb - 1)
                                     End If
                             End Select
                             If SendPanEnvelope IsNot Nothing Then
                                 SendPanEnvelope.Suspend_events = False
                             End If
                         End Sub
                h.VpotValueChangeH = VpotValueChangeH
                AddHandler c24Trk.Vpot.OnVpotValueChange, VpotValueChangeH


                _VpotSendPanHandlers.Add(h)
                'Else
                '    ClearSendChannelStripValues(c24Trk)
            End If
        Next

    End Sub
    Sub RemoveVpotSendPanHandlers()

        For Each h In _VpotSendPanHandlers
            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.Vpot.OnVpotValueChange, h.VpotValueChangeH

                If h.SendPanEnvelope IsNot Nothing Then
                    RemoveHandler h.SendPanEnvelope.On_SendUIPanChange, h.SendPanEnvelopePanChangeH
                End If
            End If


            If h.SendMonitor IsNot Nothing Then
                RemoveHandler h.SendMonitor.On_PanChange, h.ReaperSendPanChangeH
            End If

        Next

        _VpotSendPanHandlers.Clear()
    End Sub
#End Region
#Region "Send Mute"
    Sub AddSendMutesBtnHandlers()
        RemoveSendMutesBtnHandlers()

        If Flip_Mode Then
            RemoveMutesBtnHandlers()
        End If

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
                    h.SendMuteEnvelope = SendMuteEnvelope
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

        'Remet les Mute handlers par défaut après avoir retirer les SendMutes

        AddMutesBtnHandlers()

    End Sub

    Sub AddSendsMutesBtnHandlers(c24TrkList As List(Of C24_ChannelStrip), SendLetterList As List(Of String))
        RemoveSendsMutesBtnHandlers()

        Dim _ReaperTrackSendsEditor = _ReaperTrackSendsManager.ReaperTrackSendsEditor
        Dim SendsInOrder As New List(Of ReaperSend)
        For Each SendLetter In SendLetterList
            SendsInOrder.Add(_ReaperTrackSendsEditor.GetSend(SendLetter))
        Next
        _C24trkSendMuteEnvelope.Clear()

        Dim sh As New SendMuteHandlers
        sh.SendsMonitor = _ReaperTrackSendsManager.Monitor
        Dim ReaperSendMuteChangeH As Reaper.Monitors.ReaperTrackSendsMonitor.On_MuteChangeEventHandler =
                    Sub(SendIndex As Integer, mute As Boolean)
                        Dim send = _ReaperTrackSendsManager.Sends(SendIndex)
                        Dim c24TrkIndex = -1
                        For i = 0 To SendsInOrder.Count - 1
                            If SendsInOrder(i).Name = send.Name Then
                                c24TrkIndex = i
                                Exit For
                            End If
                        Next
                        If c24TrkIndex <> -1 Then
                            Dim c24Trk = c24TrkList(c24TrkIndex)


                            _ReaperTrackSendsManager.SuspendSend(SendIndex)

                            c24Trk.SwitchActive.Led = mute
                            c24Trk.ScribbleStrip.ShowTempText("Mute", If(mute, "  On", " Off"))
                            _ReaperTrackSendsManager.ResumeSend(SendIndex)

                        End If


                    End Sub
        sh.ReaperSendMuteChangeH = ReaperSendMuteChangeH
        AddHandler _ReaperTrackSendsManager.Monitor.On_MuteChange, ReaperSendMuteChangeH
        _SendMuteHandlers.Add(sh)

        For i = 0 To c24TrkList.Count - 1
            Dim c24Trk = c24TrkList(i)
            Dim PageSendIndex = i
            Dim SendLetter = SendLetterList(i)
            Dim trackIndexSend = _ReaperTrackSendsManager.TrackIndex
            Dim reaperTrk = If(trackIndexSend = -1, _ReaperProj.MasterTrack, _ReaperProj.Tracks(trackIndexSend))
            Dim send As ReaperSend = _ReaperTrackSendsManager.ReaperTrackSendsEditor.GetSend(SendLetter)



            If send Is Nothing Then
                Continue For

            End If

            Dim h As New SendMuteHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


            Dim SendMuteEnvelope As ReaperSendMuteEnvelope = Nothing
            _ReaperTrackSendsManager.MuteEnvelopes.TryGetValue(send.SendIndex, SendMuteEnvelope)

            If SendMuteEnvelope IsNot Nothing Then
                _C24trkSendMuteEnvelope.Add(c24Trk, SendMuteEnvelope)
                h.SendMuteEnvelope = SendMuteEnvelope
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
                            send.Refresh()
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

#Region "Fx"
    Sub AddVpotFxParamHandlers()
        RemoveVpotFxParamHandlers()

        _C24trkPluginParamEnvelope.Clear()


        Dim fpH = New FxParamVpotHandlers
        fpH.Monitor = _ReaperFxParamMonitor
        Dim FxParamMonitorChangeH As Reaper.Monitors.ReaperFxParamMonitor.On_ParamChangeEventHandler =
                         Sub(ParamIndex As Integer, NormValue As Double)
                             If Not ParamIndexToTrackIndex.ContainsKey(ParamIndex) Then Return
                             Dim Fxp = SelectedFx.Params(ParamIndex)
                             Dim _cs = C24_Desk.ChannelStrips(ParamIndexToTrackIndex(ParamIndex))
                             _ReaperFxParamMonitor.SuspendParam(ParamIndex)
                             If Fxp.IsToggle Then
                                 _cs.Vpot.SetLedsOff()
                                 _cs.ScribbleStrip.ShowTempText(If(CInt(NormValue) = 1, "  On", " Off"))
                                 _cs.SwitchActive.Led = Fxp.NormValue <> 0
                             Else
                                 _cs.Vpot.Value = NormValue
                             End If

                             _ReaperFxParamMonitor.ResumeParam(ParamIndex)
                         End Sub
        fpH.FxParamMonitorChangeH = FxParamMonitorChangeH
        AddHandler _ReaperFxParamMonitor.On_ParamChange, FxParamMonitorChangeH
        _FxParamVpotHandlers.Add(fpH)

        Dim _fxParamConfig As List(Of FxParamConfig) = Nothing

        If _ReaperSessionConfig.fxParamConfigs.ContainsKey(SelectedFx.Name) Then
            _fxParamConfig = _ReaperSessionConfig.fxParamConfigs(SelectedFx.Name)
        Else
            _fxParamConfig = _ReaperSessionConfig.GetGenericFxParamConfig(SelectedFx)
        End If


        '|Param1|Param2|etc...
        Dim paramsNb = Math.Min(23, (_fxParamConfig.Count - (24 * (FxParamPageNb - 1))) - 1)
        'Console.WriteLine("ParamNb:" & paramsNb)
        For i = 0 To paramsNb
            Dim cs = C24_Desk.ChannelStrips(i)
            Dim FxParamConfigIndex As Integer = i + (24 * (FxParamPageNb - 1))

            fpH = New FxParamVpotHandlers
            fpH.C24Trk = cs
            Dim FxShortName = _fxParamConfig(FxParamConfigIndex).ShortName

            'text separator
            If _fxParamConfig(FxParamConfigIndex).TextOnly IsNot Nothing Then
                cs.ScribbleStrip.Text = _fxParamConfig(FxParamConfigIndex).TextOnly
                'fxparam
            ElseIf _fxParamConfig(FxParamConfigIndex).Visible Then

                cs.ScribbleStrip.Text = FxShortName
                Dim FxParam = SelectedFx.Params(_fxParamConfig(FxParamConfigIndex).ParamIndex)
                ParamIndexToTrackIndex.Add(FxParam.ParamIndex, i)



                Dim ReaperTrk As ReaperTrack = Nothing
                If SelectedFx.TrackIndex <> -1 Then
                    ReaperTrk = _ReaperProj.Tracks(SelectedFx.TrackIndex)
                Else
                    ReaperTrk = _ReaperProj.MasterTrack
                End If

                Dim FxParamEnvelope As ReaperFxParamEnvelope = Nothing
                '_ReaperFxParamEnvelopeMonitor
                For Each _FxParamEnvelope In ReaperTrk.Envelopes.AllOfType(Of ReaperFxParamEnvelope)
                    If _FxParamEnvelope.FxIndex = SelectedFx.FxIndex Then
                        If _FxParamEnvelope.ParamIndex = FxParam.ParamIndex Then
                            FxParamEnvelope = _FxParamEnvelope
                        End If

                    End If
                Next
                If FxParamEnvelope IsNot Nothing Then
                    _C24trkPluginParamEnvelope.Add(cs, FxParamEnvelope)
                    fpH.FxParamEnvelope = FxParamEnvelope


                    Dim FxParamEnvelopeValueChangeH As Reaper.ReaperFxParamEnvelope.On_NormalizedValueChangeEventHandler =
                    Sub(_value As Double, _paramIndex As Integer)
                        If Not ParamIndexToTrackIndex.ContainsKey(_paramIndex) Then Return
                        Dim Fxp = SelectedFx.Params(_paramIndex)
                        Dim _cs = C24_Desk.ChannelStrips(ParamIndexToTrackIndex(_paramIndex))
                        _ReaperFxParamMonitor.SuspendParam(_paramIndex)
                        If Fxp.IsToggle Then
                            _cs.Vpot.SetLedsOff()
                            _cs.ScribbleStrip.ShowTempText(If(CInt(_value) = 1, "  On", " Off"))
                            _cs.SwitchActive.Led = Fxp.NormValue <> 0
                        Else
                            _cs.Vpot.Value = _value
                        End If

                        _ReaperFxParamMonitor.ResumeParam(_paramIndex)
                    End Sub
                    fpH.FxParamEnvelopeValueChangeH = FxParamEnvelopeValueChangeH
                    AddHandler FxParamEnvelope.On_NormalizedValueChange, FxParamEnvelopeValueChangeH

                End If


                'Init switch active pour les Fxparam booléen
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
                Else
                    'init Vpot values
                    cs.Vpot.VpotMode = C24_Vpot.VpotModeType.FxParamLinear
                    cs.Vpot.Value = FxParam.NormValue
                    cs.Vpot.FxParamValueToDisplay = FxParam.Formatted
                End If



                'Le Fx param est géré comme un param dont les valeurs sont Normalisées.



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
                            'If _C24trkPluginParamEnvelope.Keys.Contains(cs) Then
                            '    FxParamEnvelope = _C24trkPluginParamEnvelope(cs)
                            'End If

                            'Automation
                            If FxParamEnvelope IsNot Nothing Then
                                FxParamEnvelope.Suspend_events = True
                            End If

                            ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                            Select Case ReaperTrk.AutoMode
                                Case 0, 1
                                    '_ReaperFxParamMonitor.SuspendAllParams()
                                    _ReaperFxParamMonitor.SuspendParam(FxParam.ParamIndex)
                                    FxParam.NormValue = Value


                                    'Force UI modification
                                    Reaper.SetTrackUIVolume(SelectedFx.TrackIndex, _ReaperProj.Tracks(SelectedFx.TrackIndex).Vol)

                                    'Save Chunk

                                    Dim Trk_guid = _ReaperProj.TrackGuids(SelectedFx.TrackIndex)
                                    If TracksChunk.ContainsKey(Trk_guid) Then
                                        TracksChunk(Trk_guid) = Reaper.GetTrackStateChunk(SelectedFx.TrackIndex)
                                    Else
                                        TracksChunk.Add(Trk_guid, Reaper.GetTrackStateChunk(SelectedFx.TrackIndex))
                                    End If

                                    cs.Vpot.FxParamValueToDisplay = FxParam.Formatted
                                    '_ReaperFxParamMonitor.ResumeAllParams()
                                    _ReaperFxParamMonitor.ResumeParam(FxParam.ParamIndex)
                                Case Else


                                    If FxParamEnvelope IsNot Nothing Then
                                        _ReaperFxParamMonitor.SuspendParam(FxParam.ParamIndex)

                                        'Force UI modification
                                        Reaper.SetTrackUIVolume(SelectedFx.TrackIndex, _ReaperProj.Tracks(SelectedFx.TrackIndex).Vol)

                                        'Save Chunk

                                        Dim Trk_guid = _ReaperProj.TrackGuids(SelectedFx.TrackIndex)
                                        If TracksChunk.ContainsKey(Trk_guid) Then
                                            TracksChunk(Trk_guid) = Reaper.GetTrackStateChunk(SelectedFx.TrackIndex)
                                        Else
                                            TracksChunk.Add(Trk_guid, Reaper.GetTrackStateChunk(SelectedFx.TrackIndex))
                                        End If
                                        If FxParamEnvelope IsNot Nothing Then
                                            FxParamEnvelope.Value = Value
                                        End If

                                        'force automation write
                                        Reaper.utility.TrackFxEndParamEdit(FxParam.TrackIndex, FxParam.FxIndex, FxParam.ParamIndex)

                                        _ReaperFxParamMonitor.ResumeParam(FxParam.ParamIndex)
                                    End If


                            End Select
                            If FxParamEnvelope IsNot Nothing Then
                                FxParamEnvelope.Suspend_events = False
                            End If


                        End Sub
                fpH.VpotValueChangeH = VpotValueChangeH
                AddHandler cs.Vpot.OnVpotValueChange, VpotValueChangeH
            End If
            _FxParamVpotHandlers.Add(fpH)



        Next








    End Sub
    Sub RemoveVpotFxParamHandlers()

        For Each h In _FxParamVpotHandlers
            If h.Monitor IsNot Nothing Then
                RemoveHandler h.Monitor.On_ParamChange, h.FxParamMonitorChangeH
            End If

            If h.FxParamEnvelope IsNot Nothing Then
                RemoveHandler h.FxParamEnvelope.On_NormalizedValueChange, h.FxParamEnvelopeValueChangeH
            End If

            If h.C24Trk IsNot Nothing Then
                RemoveHandler h.C24Trk.SwitchActive.OnButtonReleased, h.SelectBtnH
                RemoveHandler h.C24Trk.Vpot.OnVpotValueChange, h.VpotValueChangeH

            End If
        Next

        _FxParamVpotHandlers.Clear()

    End Sub
    Sub AddFaderFxParamHandlers()
        RemoveFaderFxParamHandlers()

        Dim fpH = New FxParamFaderHandlers
        fpH.Monitor = _ReaperFxParamMonitor
        Dim FxParamMonitorChangeH As Reaper.Monitors.ReaperFxParamMonitor.On_ParamChangeEventHandler =
                         Sub(ParamIndex As Integer, NormValue As Double)
                             If Not ParamIndexToTrackIndex.ContainsKey(ParamIndex) Then Return
                             Dim Fxp = SelectedFx.Params(ParamIndex)
                             Dim _cs = C24_Desk.ChannelStrips(ParamIndexToTrackIndex(ParamIndex))
                             _ReaperFxParamMonitor.SuspendParam(ParamIndex)
                             _cs.Fader.Fader_Mode = C24_Fader.Mode.NormValue
                             _cs.Fader.SetFaderNormValue(NormValue)
                             _cs.Fader.FormatedValue = Fxp.Formatted
                             _cs.Vpot.SetLedFromNormValue(NormValue)
                             _ReaperFxParamMonitor.ResumeParam(ParamIndex)
                         End Sub
        fpH.FxParamMonitorChangeH = FxParamMonitorChangeH
        AddHandler _ReaperFxParamMonitor.On_ParamChange, FxParamMonitorChangeH
        _FxParamFaderHandlers.Add(fpH)

        Dim _fxParamConfig As List(Of FxParamConfig) = Nothing

        If _ReaperSessionConfig.fxParamConfigs.ContainsKey(SelectedFx.Name) Then
            _fxParamConfig = _ReaperSessionConfig.fxParamConfigs(SelectedFx.Name)
        Else
            _fxParamConfig = _ReaperSessionConfig.GetGenericFxParamConfig(SelectedFx)
        End If

        '|Param1|Param2|etc...
        For i = 0 To Math.Min(23, (_fxParamConfig.Count - (24 * (FxParamPageNb - 1))) - 1)
            Dim cs = C24_Desk.ChannelStrips(i)
            Dim FxParamConfigIndex As Integer = i + (24 * (FxParamPageNb - 1))

            fpH = New FxParamFaderHandlers
            fpH.C24Trk = cs
            Dim FxShortName = _fxParamConfig(FxParamConfigIndex).ShortName

            'text separator
            If _fxParamConfig(FxParamConfigIndex).TextOnly IsNot Nothing Then
                cs.ScribbleStrip.Text = _fxParamConfig(FxParamConfigIndex).TextOnly
                cs.Fader.SetInitFaderDbVolume(-150)
                'fxparam
            ElseIf _fxParamConfig(FxParamConfigIndex).Visible Then

                cs.ScribbleStrip.Text = FxShortName
                Dim FxParam = SelectedFx.Params(_fxParamConfig(FxParamConfigIndex).ParamIndex)
                ParamIndexToTrackIndex.Add(FxParam.ParamIndex, i)

                'Dico des FxParamEnvelope avec les Cs
                Dim ReaperTrk = _ReaperProj.Tracks(SelectedFx.TrackIndex)
                Dim FxParamEnvelope As ReaperFxParamEnvelope = Nothing
                For Each _FxParamEnvelope In ReaperTrk.Envelopes.AllOfType(Of ReaperFxParamEnvelope)
                    If _FxParamEnvelope.FxIndex = SelectedFx.FxIndex Then
                        If _FxParamEnvelope.ParamIndex = FxParam.ParamIndex Then
                            FxParamEnvelope = _FxParamEnvelope
                        End If

                    End If
                Next


                If FxParamEnvelope IsNot Nothing Then
                    _C24trkPluginParamEnvelope.Add(cs, FxParamEnvelope)
                    fpH.FxParamEnvelope = FxParamEnvelope


                    Dim FxParamEnvelopeValueChangeH As Reaper.ReaperFxParamEnvelope.On_NormalizedValueChangeEventHandler =
                    Sub(_value As Double, _paramIndex As Integer)
                        If Not ParamIndexToTrackIndex.ContainsKey(_paramIndex) Then Return
                        Dim Fxp = SelectedFx.Params(_paramIndex)
                        Dim _cs = C24_Desk.ChannelStrips(ParamIndexToTrackIndex(_paramIndex))
                        _ReaperFxParamMonitor.SuspendParam(_paramIndex)
                        If Fxp.IsToggle Then
                            _cs.ScribbleStrip.ShowTempText(If(CInt(_value) = 1, "  On", " Off"))
                            _cs.SwitchActive.Led = Fxp.NormValue <> 0
                        End If
                        _cs.Fader.Fader_Mode = C24_Fader.Mode.NormValue
                        _cs.Fader.SetFaderNormValue(_value)

                        _cs.Vpot.SetLedFromNormValue(_value)
                        _ReaperFxParamMonitor.ResumeParam(_paramIndex)
                    End Sub
                    fpH.FxParamEnvelopeValueChangeH = FxParamEnvelopeValueChangeH
                    AddHandler FxParamEnvelope.On_NormalizedValueChange, FxParamEnvelopeValueChangeH

                End If


                cs.Fader.Fader_Mode = C24_Fader.Mode.NormValue
                ' ── Init position + envoi hardware (symétrique avec SetInitFaderDbVolume) ──
                cs.Fader.SetFaderNormValue(FxParam.NormValue)
                cs.Fader.FormatedValue = FxParam.Formatted
                cs.Vpot.SetLedFromNormValue(FxParam.NormValue)
                cs.Vpot.DisableVpot = True



                'Le Fx param est géré comme un param dont les valeurs sont Normalisées.


                Dim FaderValueChangeH As Hardware.C24_Fader.On_FaderHumanChangeNormValueEventHandler =
                        Sub(Track_Nb As Integer, NormFormatedValues As Tuple(Of Double, String))
                            'Automation

                            'Select Case ReaperTrk.AutoMode
                            '    Case 0, 1
                            '        _ReaperFxParamMonitor.SuspendParam(FxParam.ParamIndex)
                            '        FxParam.NormValue = NormFormatedValues.Item1
                            '        cs.Vpot.Value = FxParam.NormValue
                            '        _ReaperFxParamMonitor.ResumeParam(FxParam.ParamIndex)
                            '        FxParam.Refresh()
                            '        NormFormatedValues = New Tuple(Of Double, String)(FxParam.NormValue, FxParam.Formatted)
                            '        cs.Fader.SetFaderNormValue(NormFormatedValues)
                            '    Case Else

                            '        FxParamEnvelope = _C24trkPluginParamEnvelope(cs)
                            '        If FxParamEnvelope IsNot Nothing Then
                            '            _ReaperFxParamMonitor.SuspendParam(FxParam.ParamIndex)
                            '            FxParamEnvelope.Value = NormFormatedValues.Item1
                            '            _ReaperFxParamMonitor.ResumeParam(FxParam.ParamIndex)
                            '            'FxParam.Refresh()
                            '        End If


                            'End Select

                            'If _C24trkPluginParamEnvelope.Keys.Contains(cs) Then
                            '    FxParamEnvelope = _C24trkPluginParamEnvelope(cs)
                            'End If

                            'Automation
                            If FxParamEnvelope IsNot Nothing Then
                                FxParamEnvelope.Suspend_events = True
                            End If

                            ''' <summary>I_AUTOMODE — 0=trim/off 1=read 2=touch 3=write 4=latch</summary>
                            Select Case ReaperTrk.AutoMode
                                Case 0, 1
                                    '_ReaperFxParamMonitor.SuspendAllParams()
                                    _ReaperFxParamMonitor.SuspendParam(FxParam.ParamIndex)
                                    FxParam.NormValue = NormFormatedValues.Item1


                                    'Force UI modification
                                    Reaper.SetTrackUIVolume(SelectedFx.TrackIndex, _ReaperProj.Tracks(SelectedFx.TrackIndex).Vol)

                                    'Save Chunk

                                    Dim Trk_guid = _ReaperProj.TrackGuids(SelectedFx.TrackIndex)
                                    If TracksChunk.ContainsKey(Trk_guid) Then
                                        TracksChunk(Trk_guid) = Reaper.GetTrackStateChunk(SelectedFx.TrackIndex)
                                    Else
                                        TracksChunk.Add(Trk_guid, Reaper.GetTrackStateChunk(SelectedFx.TrackIndex))
                                    End If

                                    cs.Fader.FormatedValue = FxParam.Formatted
                                    cs.Vpot.SetLedFromNormValue(FxParam.NormValue)

                                    _ReaperFxParamMonitor.ResumeParam(FxParam.ParamIndex)
                                Case Else


                                    If FxParamEnvelope IsNot Nothing Then
                                        _ReaperFxParamMonitor.SuspendParam(FxParam.ParamIndex)

                                        'Force UI modification
                                        Reaper.SetTrackUIVolume(SelectedFx.TrackIndex, _ReaperProj.Tracks(SelectedFx.TrackIndex).Vol)

                                        'Save Chunk

                                        Dim Trk_guid = _ReaperProj.TrackGuids(SelectedFx.TrackIndex)
                                        If TracksChunk.ContainsKey(Trk_guid) Then
                                            TracksChunk(Trk_guid) = Reaper.GetTrackStateChunk(SelectedFx.TrackIndex)
                                        Else
                                            TracksChunk.Add(Trk_guid, Reaper.GetTrackStateChunk(SelectedFx.TrackIndex))
                                        End If
                                        If FxParamEnvelope IsNot Nothing Then
                                            FxParamEnvelope.Value = NormFormatedValues.Item1
                                        End If

                                        'force automation write
                                        Reaper.utility.TrackFxEndParamEdit(FxParam.TrackIndex, FxParam.FxIndex, FxParam.ParamIndex)
                                        cs.Fader.FormatedValue = FxParam.Formatted
                                        cs.Vpot.SetLedFromNormValue(FxParam.NormValue)

                                        _ReaperFxParamMonitor.ResumeParam(FxParam.ParamIndex)
                                    End If


                            End Select
                            If FxParamEnvelope IsNot Nothing Then
                                FxParamEnvelope.Suspend_events = False
                            End If


                        End Sub
                fpH.FaderValueChangeH = FaderValueChangeH
                AddHandler cs.Fader.On_FaderHumanChangeNormValue, FaderValueChangeH

            End If
            _FxParamFaderHandlers.Add(fpH)


            'Console.Write("|" & cs.Vpot.FxParamValueToDisplay)
        Next





    End Sub
    Sub RemoveFaderFxParamHandlers()
        For Each h In _FxParamFaderHandlers
            If h.Monitor IsNot Nothing Then
                RemoveHandler h.Monitor.On_ParamChange, h.FxParamMonitorChangeH
            End If

            If h.FxParamEnvelope IsNot Nothing Then
                RemoveHandler h.FxParamEnvelope.On_NormalizedValueChange, h.FxParamEnvelopeValueChangeH
            End If

            If h.C24Trk IsNot Nothing Then
                h.C24Trk.Vpot.DisableVpot = False
                RemoveHandler h.C24Trk.Fader.On_FaderHumanChangeNormValue, h.FaderValueChangeH
            End If
        Next

        _FxParamFaderHandlers.Clear()
    End Sub

    Sub AddAutoSelectFxParamHandlers()
        RemoveAutoSelectFxParamHandlers()


        Dim asmH As New AutoSelectModeHandler




        Dim _fxParamConfig = _ReaperSessionConfig.fxParamConfigs(SelectedFx.Name)

        '|Param1|Param2|etc...
        For i = 0 To Math.Min(23, (_fxParamConfig.Count - (24 * (FxParamPageNb - 1))) - 1)
            Dim cs = C24_Desk.ChannelStrips(i)
            Dim FxParamConfigIndex As Integer = i + (24 * (FxParamPageNb - 1))

            asmH = New AutoSelectModeHandler
            asmH.c24Trk = cs
            Dim FxShortName = _fxParamConfig(FxParamConfigIndex).ShortName

            'text separator
            If _fxParamConfig(FxParamConfigIndex).TextOnly IsNot Nothing Then
                cs.LedSwitchActive.IsBlinking = False
                cs.SwitchActive.IsBlinking = False
            ElseIf _fxParamConfig(FxParamConfigIndex).Visible Then
                'init
                Dim FxParamEnvelope As ReaperFxParamEnvelope = Nothing
                cs.LedSwitchActive.IsBlinking = False
                'indique que le Fxparam est sélectionnable pour une enveloppe.
                cs.LedSwitchActive.Led = True
                'init envelope armée
                cs.SwitchActive.IsBlinking = False
                For Each _cs In _C24trkPluginParamEnvelope.Keys
                    If _cs.TrackNumber = cs.TrackNumber Then
                        FxParamEnvelope = _C24trkPluginParamEnvelope(cs)
                        cs.SwitchActive.Led = FxParamEnvelope.Armed
                    End If
                Next

                'addhandler pour armer ou créer des enveloppes par FxParam

                Dim SelectAutoChangeH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                            Sub(Track_Nb As Integer?)
                                If FxParamEnvelope IsNot Nothing Then
                                    FxParamEnvelope.Armed = Not FxParamEnvelope.Armed
                                    cs.SwitchActive.Led = Not cs.SwitchActive.Led
                                Else
                                    _ReaperTracksManager.Tracks(SelectedFx.TrackIndex).Envelopes.CreatFxParamEnvelope(SelectedFx.FxIndex, _fxParamConfig(FxParamConfigIndex).ParamIndex)
                                    cs.SwitchActive.Led = True
                                End If

                            End Sub
                asmH.SelectAutoChangeH = SelectAutoChangeH
                AddHandler cs.SwitchActive.OnButtonReleased, SelectAutoChangeH



            End If

            _AutoSelectModeHandlers.Add(asmH)

        Next

    End Sub
    Sub RemoveAutoSelectFxParamHandlers()

        For Each h In _AutoSelectModeHandlers
            If h.FxParamEnvelope IsNot Nothing Then
                RemoveHandler h.FxParamEnvelope.On_ActiveChange, h.FxParamEnvelopeActiveChangeH
                RemoveHandler h.FxParamEnvelope.On_ArmedChange, h.FxParamEnvelopeArmedChangeH

                If h.c24Trk IsNot Nothing Then
                    RemoveHandler h.c24Trk.SwitchActive.OnButtonReleased, h.SelectAutoChangeH
                End If
            End If


        Next

        _AutoSelectModeHandlers.Clear()
    End Sub

#End Region

#Region "Solo"
    Sub AddSolosHandlers()
        RemoveSolosHandlers()

        'Les Modifications Solo venant de reaper sont gérés dans AddReaperSoloHandlers car il faut géré tous les solos et pas uniquement ceux des pistes C24 visibles. 

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
        Dim All_tracks As New List(Of ReaperTrack)
        All_tracks.AddRange(_ReaperProj.Tracks)
        All_tracks.Add(_ReaperProj.MasterTrack)
        For Each reaperTrk In All_tracks

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
                        Console.Clear()
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

                        If SoloTracks(reaperTrk.TrackIndex) Then
                            c24Trk.Solo.Led = True
                        Else
                            c24Trk.Solo.IsBlinking = False
                        End If
                        SetMuteLedFromSoloState(c24Trk, reaperTrk)
                    Case Solo_Mode.SoloSafe
                        c24Trk.Mute.IsBlinking = False
                        If SoloSafeTracks(reaperTrk.TrackIndex) Then
                            c24Trk.Solo.IsBlinking = True
                        Else
                            c24Trk.Solo.IsBlinking = False
                        End If

                End Select

            Else
                c24Trk.Solo.IsBlinking = False
                c24Trk.Mute.IsBlinking = False
            End If


        Next


    End Sub
    Sub SetMuteLedFromSoloState(c24Trk As C24_ChannelStrip, reaperTrk As ReaperTrack)


        Dim isOneSolo = SoloTracks.Values.Contains(True)


        If reaperTrk.Mute Then
            c24Trk.Mute.IsBlinking = False
            c24Trk.Mute.Led = True
        Else
            If isOneSolo Then
                If c24Trk.Solo.Led OrElse SoloSafeTracks(reaperTrk.TrackIndex) Then
                    c24Trk.Mute.IsBlinking = False
                Else
                    c24Trk.Mute.IsBlinking = True
                End If

            Else
                c24Trk.Mute.IsBlinking = False
            End If
        End If




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
                Dim C24_VueMeter = C24_Desk.ChannelStrips(i).VueMeter
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
            'gestion du time monitoring
            If ActiveTimeMonitoring Then
                If rawPos >= MonitorTimeValue Then
                    RaiseEvent On_MonitorTimeValueExced()

                End If
            End If



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

    Sub AddPeaksHandler()
        RemovePeaksHandler()

        Dim h As New PeaksHandler

        Dim Clear_PeaksBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
           Sub(trackNb As Integer?)
               Reaper.utility.Main_OnCommand(40527)
               'For i = 0 To _ReaperTracksManager.Tracks.Count - 1
               '    Dim cs = C24_Desk.ChannelStrips(i)
               '    cs.VueMeter.ResetPeaks()
               'Next
               'C24_Desk.VueMeterMaster1.ResetPeaks()
               'C24_Desk.VueMeterMaster2.ResetPeaks()
               'C24_Desk.VueMeterMaster3.ResetPeaks()
           End Sub

        h.Clear_PeaksBtnH = Clear_PeaksBtnH
        AddHandler C24_Desk.Ms_Clear_Peaks.OnButtonReleased, Clear_PeaksBtnH


        'init
        If Reaper.utility.GetToggleCommandStateBool(42076) = False Then
            C24_Desk.Ms_Pre_Fader.Led = True
        Else
            C24_Desk.Ms_Post_Fader.Led = True
        End If

        Dim PreFaderBtnH = Sub(fake As Integer?)
                               'désactive toutes les piste du rec arm
                               Reaper.utility.Main_OnCommand(40491)
                               If Reaper.utility.GetToggleCommandStateBool(42076) = False Then
                                   Reaper.utility.Main_OnCommand(42076)
                               End If
                           End Sub
        Dim PostFaderBtnH = Sub(fake As Integer?)
                                'désactive toutes les piste du rec arm
                                Reaper.utility.Main_OnCommand(40491)
                                If Reaper.utility.GetToggleCommandStateBool(42076) = True Then
                                    Reaper.utility.Main_OnCommand(42076)
                                End If
                            End Sub
        Dim InputBtnH = Sub(fake As Integer?)
                            'toutes les piste en rec arm
                            Reaper.utility.Main_OnCommand(40490)
                        End Sub

        Dim InputPrePostFaderPeaks As New C24RadioBtnHandler
        InputPrePostFaderPeaks.Add(C24_Desk.Ms_Pre_Fader, PreFaderBtnH)
        InputPrePostFaderPeaks.Add(C24_Desk.Ms_Post_Fader, PostFaderBtnH)
        InputPrePostFaderPeaks.Add(C24_Desk.Ms_Input, InputBtnH)

        InputPrePostFaderPeaks.AddHandlers()
        h.InputPrePostFaderPeaks = InputPrePostFaderPeaks

        _PeaksHandlers.add(h)



    End Sub
    Sub RemovePeaksHandler()
        For Each h In _PeaksHandlers
            RemoveHandler C24_Desk.Ms_Clear_Peaks.OnButtonReleased, h.Clear_PeaksBtnH
            h.InputPrePostFaderPeaks.Dispose()
        Next
        _PeaksHandlers.Clear()
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

    Sub RefreshTrackManagerPeaks()
        _ReaperPeakLevelManager.ClearWatched()
        _ReaperPeakLevelManager.WatchTracks(_ReaperTracksManager.Tracks)
        _ReaperPeakLevelManager.WatchTrack(-1)
        ClearTracksPeakLevels()
    End Sub


#End Region
#Region "Solo Clear Safe"
    Sub AddSolosClearSafeHandlers()
        RemoveSolosClearSafeHandlers()

        'init
        SetSoloLed()
        RefreshSoloClear()

        Dim h As New SolosClearSafeHandlers
        'Solo Clear
        Dim soloClearBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub(trackNb As Integer?)
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
        If ReaperProj.Tracks IsNot Nothing Then
            For Each trk In ReaperProj.Tracks
                SoloTracks.Add(trk.TrackIndex, If(GetC24Solo(trk.Solo) = C24_SoloTypes.Solo, True, False))
                SoloSafeTracks.Add(trk.TrackIndex, trk.SoloDefeat)
                Names.Add(trk.TrackIndex, trk.Name)
            Next
            Dim master = _ReaperProj.MasterTrack
            SoloTracks.Add(master.TrackIndex, If(GetC24Solo(master.Solo) = C24_SoloTypes.Solo, True, False))
            SoloSafeTracks.Add(master.TrackIndex, master.SoloDefeat)
            Names.Add(master.TrackIndex, master.Name)
        End If


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
        Dim c24Trk = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(track_index))
        If SoloTracks.Values.Contains(True) AndAlso SoloSafeTracks(track_index) = False Then
            If Mute Then
                If c24Trk.Mute.IsBlinking Then
                    c24Trk.Mute.IsBlinking = False
                End If

                c24Trk.Mute.Led = True
            Else
                If SoloTracks(track_index) Then
                    c24Trk.Mute.Led = False
                Else
                    c24Trk.Mute.IsBlinking = True
                End If

            End If
        Else
            'c24Trk.Mute.IsBlinking = False
            c24Trk.Mute.Led = Mute
        End If
    End Sub


    Sub RefreshSoloSafe()
        C24_Desk.SoloSafe.Led = SoloSafeTracks.Values.Contains(True)
    End Sub

    Sub RefreshSoloClear()
        C24_Desk.SoloClear.IsBlinking = SoloTracks.Values.Contains(True)
    End Sub

#End Region

#Region "ChannelSettings"
    Sub AddChannelSettingsHandlers()
        RemoveChannelSettingsHandlers()

        Dim h As New ChannelSettingsHandler



        Dim Edit_BypBtnH As C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                C24_Desk.Edit_Byp.Led = Not C24_Desk.Edit_Byp.Led
                If C24_Desk.Edit_Byp.Led Then
                    EqDynBypassMode = True
                Else
                    EqDynBypassMode = False
                End If
                AddCommonStripHandlers()

            End Sub
        h.Edit_BypBtnH = Edit_BypBtnH
        AddHandler C24_Desk.Edit_Byp.OnButtonReleased, Edit_BypBtnH

        Dim Ins_BypBtnH As C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                C24_Desk.Ins_Byp.Led = Not C24_Desk.Ins_Byp.Led
                If C24_Desk.Ins_Byp.Led Then
                    InsertBypassMode = True
                Else
                    InsertBypassMode = False
                End If
                AddCommonStripHandlers()

            End Sub
        h.Ins_BypBtnH = Ins_BypBtnH
        AddHandler C24_Desk.Ins_Byp.OnButtonReleased, Ins_BypBtnH

        Dim _FxSettingManager As FxSettingManager = Nothing

        'init
        C24_Desk.Copy_Settings.IsBlinking = False
        Dim Copy_SettingsBtnH As C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If SelectedFx IsNot Nothing Then
                    _FxSettingManager = New FxSettingManager(SelectedFx)
                    _FxSettingManager.CopyParams(FxSettingManager.CompareMode.A)
                    C24_Desk.Copy_Settings.IsBlinking = True
                End If

            End Sub
        h.Copy_SettingsBtnH = Copy_SettingsBtnH
        AddHandler C24_Desk.Copy_Settings.OnButtonReleased, Copy_SettingsBtnH

        'init
        C24_Desk.Paste_Setting.IsBlinking = False
        Dim Paste_SettingBtnH As C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If SelectedFx IsNot Nothing AndAlso _FxSettingManager IsNot Nothing Then
                    If _FxSettingManager.PasteSettingsTo(FxSettingManager.CompareMode.A, SelectedFx) = False Then
                        C24DisplayTempMessage("FxPaste", "Not same Fx choose to paste settings !")
                    Else
                        _FxSettingManager.Dispose()
                        _FxSettingManager = Nothing
                        C24_Desk.Copy_Settings.IsBlinking = False
                    End If
                End If

            End Sub
        h.Paste_SettingBtnH = Paste_SettingBtnH
        AddHandler C24_Desk.Paste_Setting.OnButtonReleased, Paste_SettingBtnH


        _ChannelSettingsHandlers.add(h)
    End Sub
    Sub RemoveChannelSettingsHandlers()
        For Each h In _ChannelSettingsHandlers
            RemoveHandler C24_Desk.Ins_Byp.OnButtonReleased, h.Ins_BypBtnH
            RemoveHandler C24_Desk.Edit_Byp.OnButtonReleased, h.Edit_BypBtnH
            RemoveHandler C24_Desk.Copy_Settings.OnButtonReleased, h.Copy_SettingsBtnH
            RemoveHandler C24_Desk.Paste_Setting.OnButtonReleased, h.Paste_SettingBtnH
        Next
        _ChannelSettingsHandlers.clear
    End Sub

    Public Class FxSettingManager
        Implements IDisposable
        Private _Name As String
        Private A_SettingsCopy As New List(Of Double)
        Private B_SettingsCopy As New List(Of Double)
        Private ActualCompareMode As CompareMode = CompareMode.Not_Init
        Private _FxSource As ReaperFX
        Private disposedValue As Boolean

        Public ReadOnly Property Name As String
            Get
                Return _Name
            End Get
        End Property

        Sub New(FxSource As ReaperFX)
            _Name = FxSource.Name
            _FxSource = FxSource

        End Sub

        Public Sub CopyParams(_CompareMode As CompareMode)
            If _CompareMode = CompareMode.A Then
                A_SettingsCopy.Clear()
                For Each param In _FxSource.Params
                    A_SettingsCopy.Add(param.RawValue)
                Next

            Else
                B_SettingsCopy.Clear()
                For Each param In _FxSource.Params
                    B_SettingsCopy.Add(param.RawValue)
                Next

            End If
        End Sub

        Public Function PasteSettingsTo(FromCompareMode As CompareMode, Fx As ReaperFX) As Boolean
            If Fx.Name <> Name Then
                Return False
            End If

            If FromCompareMode = CompareMode.A Then
                For i = 0 To A_SettingsCopy.Count - 1
                    Fx.Params(i).RawValue = A_SettingsCopy(i)
                Next
            Else
                For i = 0 To B_SettingsCopy.Count - 1
                    Fx.Params(i).RawValue = B_SettingsCopy(i)
                Next
            End If

            Return True

        End Function

        Public Sub InitCompareAB()
            CopyParams(CompareMode.A)
            ActualCompareMode = CompareMode.B
        End Sub

        Public Function CompareAB() As CompareMode
            If ActualCompareMode = CompareMode.B Then
                CopyParams(CompareMode.B)
                PasteSettingsTo(CompareMode.A, _FxSource)
                ActualCompareMode = CompareMode.A
                Return CompareMode.A
            Else
                PasteSettingsTo(CompareMode.B, _FxSource)
                ActualCompareMode = CompareMode.B
                Return CompareMode.B
            End If
        End Function
        Public Enum CompareMode
            A
            B
            Not_Init
        End Enum

        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not disposedValue Then
                If disposing Then
                    ' TODO: supprimer l'état managé (objets managés)
                End If
                A_SettingsCopy = Nothing
                B_SettingsCopy = Nothing
                ' TODO: libérer les ressources non managées (objets non managés) et substituer le finaliseur
                ' TODO: affecter aux grands champs une valeur null
                disposedValue = True
            End If
        End Sub

        ' ' TODO: substituer le finaliseur uniquement si 'Dispose(disposing As Boolean)' a du code pour libérer les ressources non managées
        ' Protected Overrides Sub Finalize()
        '     ' Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(disposing As Boolean)'
        '     Dispose(disposing:=False)
        '     MyBase.Finalize()
        ' End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            ' Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(disposing As Boolean)'
            Dispose(disposing:=True)
            GC.SuppressFinalize(Me)
        End Sub
    End Class
#End Region


#Region "Record"
    Sub AddRecordHandlers()
        RemoveRecordHandlers()

        Dim h As New RecordHandler
        'init
        C24_Desk.Rec_Safe.IsBlinking = False
        Dim Rec_SafeBtnH As C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                C24_Desk.Rec_Safe.Led = Not C24_Desk.Rec_Safe.Led

                If C24_Desk.Rec_Safe.Led Then
                    'Rec Safe Mode
                    RecSafeMode = True
                    AddCommonStripHandlers()
                Else
                    RecSafeMode = False
                    AddCommonStripHandlers()
                End If

            End Sub
        h.Rec_SafeBtnH = Rec_SafeBtnH
        AddHandler C24_Desk.Rec_Safe.OnButtonReleased, Rec_SafeBtnH


        'init
        C24_Desk.Master_Rec.IsBlinking = False
        For Each trk In _ReaperProj.Tracks
            If trk.RecArm = 1 Then
                C24_Desk.Master_Rec.IsBlinking = True
                Exit For
            End If
        Next
        Dim Master_RecBtnH As C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If _ReaperTransportManager.IsRecording = False Then
                    If C24_Desk.Master_Rec.IsBlinking Then
                        'Bypass la vérif des modfication d'armement par piste
                        UpdateRecMasterBypass = True
                        'Désarme toutes les pistes
                        Reaper.utility.Main_OnCommand(40491)
                        C24_Desk.Master_Rec.IsBlinking = False

                    Else
                        'Arme toutes les pistes qui ne sont pas RecSafe
                        For Each trk In _ReaperProj.Tracks
                            Dim cs As C24_ChannelStrip = Nothing
                            Dim cs_index = _ReaperTracksManager.GetRelativeTrackIndex(trk.TrackIndex)
                            If cs_index < _ReaperTracksManager.Tracks.Count Then
                                cs = C24_Desk.ChannelStrips(cs_index)
                            End If

                            If trk.RecInput < 0 Then
                                trk.RecArm = 0
                                If cs IsNot Nothing Then
                                    cs.RecArm.IsBlinking = False
                                End If
                            Else
                                trk.RecArm = 1
                                If cs IsNot Nothing Then
                                    cs.RecArm.IsBlinking = True
                                End If
                            End If
                        Next
                        C24_Desk.Master_Rec.IsBlinking = True
                    End If

                End If

            End Sub
        h.Master_RecBtnH = Master_RecBtnH
        AddHandler C24_Desk.Master_Rec.OnButtonReleased, Master_RecBtnH

        Dim On_PlayStateChangeH As ReaperTransportManager.On_PlayStateChangeEventHandler =
                        Sub(playstate As Integer)
                            Dim SomeTracksArmed As Boolean = False
                            For Each trk In _ReaperProj.Tracks
                                If trk.RecArm = 1 Then
                                    SomeTracksArmed = True
                                    Exit For
                                End If
                            Next


                            If _ReaperTransportManager.IsRecording Then
                                If SomeTracksArmed Then
                                    C24_Desk.Master_Rec.IsBlinking = False
                                    C24_Desk.Master_Rec.Led = True
                                Else
                                    C24_Desk.Master_Rec.IsBlinking = False
                                End If

                            Else
                                If SomeTracksArmed Then
                                    C24_Desk.Master_Rec.IsBlinking = True
                                Else
                                    C24_Desk.Master_Rec.IsBlinking = False
                                End If

                            End If
                        End Sub
        h.On_PlayStateChangeH = On_PlayStateChangeH
        AddHandler _ReaperTransportManager.On_PlayStateChange, On_PlayStateChangeH



        _RecordHandlers.add(h)




    End Sub

    Sub RemoveRecordHandlers()
        For Each h In _RecordHandlers
            RemoveHandler _ReaperTransportManager.On_PlayStateChange, h.On_PlayStateChangeH
            RemoveHandler C24_Desk.Master_Rec.OnButtonReleased, h.Master_RecBtnH
            RemoveHandler C24_Desk.Rec_Safe.OnButtonReleased, h.Rec_SafeBtnH
        Next
        _RecordHandlers.Clear()
    End Sub
#End Region

#Region "Monitor"
    Sub AddMonitorHandlers()
        RemoveMonitorHandlers()

        Dim TrackArmed As New List(Of Integer)

        Dim h As New MonitorHandler
        'init
        Dim SetMonitorMode = Sub(MonitorMode As Integer)
                                 C24_Desk.Monitor.IsBlinking = False
                                 Select Case MonitorMode
                                     Case 0
                                         TrackArmed.Clear()
                                         For Each trk In _ReaperProj.Tracks
                                             If trk.RecArm = 1 Then
                                                 TrackArmed.Add(trk.TrackIndex)
                                                 trk.RecArm = 0
                                             End If
                                             trk.RecMon = 0
                                         Next
                                         C24_Desk.Monitor.IsBlinking = False
                                         Reaper.utility.SetProjectExtState("C24MONITOR", "RECMODE", "0")
                                     Case 1
                                         For Each trk In _ReaperProj.Tracks
                                             If TrackArmed.Contains(trk.TrackIndex) Then
                                                 trk.RecArm = 1
                                             End If
                                             trk.RecMon = 1
                                         Next
                                         C24_Desk.Monitor.Led = True
                                         Reaper.utility.SetProjectExtState("C24MONITOR", "RECMODE", "1")
                                     Case 2
                                         For Each trk In _ReaperProj.Tracks
                                             If TrackArmed.Contains(trk.TrackIndex) Then
                                                 trk.RecArm = 1
                                             End If
                                             trk.RecMon = 2
                                         Next
                                         C24_Desk.Monitor.IsBlinking = True
                                         Reaper.utility.SetProjectExtState("C24MONITOR", "RECMODE", "2")
                                 End Select


                             End Sub


        'init
        Dim RecMode As Integer = 2
        Integer.TryParse(Reaper.utility.GetProjectExtState("C24MONITOR", "RECMODE", "2"), RecMode)
        SetMonitorMode.Invoke(RecMode)

        Dim MonitorBtnH As C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Integer.TryParse(Reaper.utility.GetProjectExtState("C24MONITOR", "RECMODE", "2"), RecMode)
                RecMode += 1
                If RecMode > 2 Then
                    RecMode = 0
                End If
                SetMonitorMode.Invoke(RecMode)
            End Sub
        h.MonitorBtnH = MonitorBtnH
        AddHandler C24_Desk.Monitor.OnButtonReleased, MonitorBtnH





        _MonitorHandlers.Add(h)




    End Sub

    Sub RemoveMonitorHandlers()
        For Each h In _MonitorHandlers
            RemoveHandler C24_Desk.Monitor.OnButtonReleased, h.MonitorBtnH

        Next
        _MonitorHandlers.Clear()
    End Sub
#End Region

#Region "Banks"
    Sub AddBankHandlers()
        RemoveBankHandlers()
        'Bank Navigation
        Dim h As New BankHandlers

        Dim BankLeftBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If BankValidViewModes.Contains(ActualC24ViewMode) Then

                    If _ReaperTracksManager.IsPageLeftExist Then
                        _ReaperTracksManager.Set_Prev_Page()
                        AddCommonStripHandlers()
                        AddMutesBtnHandlers()
                        AddSolosHandlers()
                        RefreshTrackManagerPeaks()
                        RefreshC24ViewMode()
                        RaiseEvent OnBankChangeEnd()
                    End If
                End If

            End Sub
        h.BankLeftBtnH = BankLeftBtnH
        AddHandler C24_Desk.Bank_Left.OnButtonReleased, BankLeftBtnH

        Dim BankRightBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If BankValidViewModes.Contains(ActualC24ViewMode) Then
                    If _ReaperTracksManager.IsPageRightExist Then
                        _ReaperTracksManager.Set_Next_Page()
                        AddCommonStripHandlers()
                        AddMutesBtnHandlers()
                        AddSolosHandlers()
                        RefreshTrackManagerPeaks()
                        RefreshC24ViewMode()
                        RaiseEvent OnBankChangeEnd()
                    End If
                End If

            End Sub
        h.BankRightBtnH = BankRightBtnH
        AddHandler C24_Desk.Bank_Right.OnButtonReleased, BankRightBtnH

        Dim NudgePressBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                LastJogWheelToolMode = JogWheelToolMode
                JogWheelToolMode = JogWheelToolModes.Nudge

            End Sub
        h.NudgePressBtnH = NudgePressBtnH
        AddHandler C24_Desk.Nudge.OnButtonPressed, NudgePressBtnH

        Dim NudgeRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                JogWheelToolMode = LastJogWheelToolMode
                'move tracks on C24 to appropriate page
                Dim trackindexes = Reaper.utility.GetSelectedTracks



                If trackindexes.Count > 0 Then
                    _ReaperTracksManager.Set_Page(trackindexes.First)
                    AddCommonStripHandlers()
                    AddMutesBtnHandlers()
                    AddSolosHandlers()
                    RefreshTrackManagerPeaks()
                    RefreshC24ViewMode()
                    Exit Sub
                End If


            End Sub
        h.NudgeRealBtnH = NudgeRealBtnH
        AddHandler C24_Desk.Nudge.OnButtonReleased, NudgeRealBtnH

        _BankHandlers.Add(h)
    End Sub
    Public Sub RemoveBankHandlers()
        For Each h In _BankHandlers
            RemoveHandler C24_Desk.Bank_Left.OnButtonReleased, h.BankLeftBtnH
            RemoveHandler C24_Desk.Bank_Right.OnButtonReleased, h.BankRightBtnH

            RemoveHandler C24_Desk.Nudge.OnButtonPressed, h.NudgePressBtnH
            RemoveHandler C24_Desk.Nudge.OnButtonReleased, h.NudgeRealBtnH
        Next
        _BankHandlers.Clear()
    End Sub


#End Region
#Region "Transport"


    Sub AddTransportHandlers()
        RemoveTransportHandlers()
        Dim h As New TransportHandlers
        Dim PlayLoopSavedState As Boolean = False


        Dim PlayLoopAL As New C24ActionLinker(C24_Desk.Loop_Play, 1068)
        h.PlayLoopAL = PlayLoopAL

        'Quick Punch et Capute Handlers
        'Les 2 modes AutoPunch sont exclusif de manière native dans Reaper.
        'init
        If Reaper.utility.GetToggleCommandStateBool(40252) Then
            'Mode record normal
            C24_Desk.Quick_Punch.IsBlinking = False
        ElseIf Reaper.utility.GetToggleCommandStateBool(40076) Then
            'Mode record QuickPunch Selection
            C24_Desk.Quick_Punch.Led = True
        Else
            'Mode record QuickPunch item
            C24_Desk.Quick_Punch.IsBlinking = True
        End If

        'Record AutoPunch dans une sélection
        Dim Quick_PunchBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)

                If C24_Desk.Quick_Punch.IsBlinking OrElse C24_Desk.Quick_Punch.Led = True Then
                    'Set Rec Normal Mode
                    Reaper.utility.Main_OnCommand_SafeUI(40252)
                    C24_Desk.Quick_Punch.IsBlinking = False
                Else
                    'Set Quick Punch selection
                    Reaper.utility.Main_OnCommand_SafeUI(40076)
                    C24_Desk.Quick_Punch.Led = True
                End If

            End Sub
        h.Quick_PunchBtnH = Quick_PunchBtnH
        AddHandler C24_Desk.Quick_Punch.OnButtonReleased, Quick_PunchBtnH


        'Record AutoPunch dans un item
        Dim captureBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If C24_Desk.Quick_Punch.IsBlinking = True Then
                    'Set Quick Punch selection
                    Reaper.utility.Main_OnCommand_SafeUI(40076)
                    C24_Desk.Quick_Punch.IsBlinking = False
                    C24_Desk.Quick_Punch.Led = True
                ElseIf C24_Desk.Quick_Punch.Led = True Then
                    'Set Quick Punch item
                    Reaper.utility.Main_OnCommand_SafeUI(40253)
                    C24_Desk.Quick_Punch.IsBlinking = True
                End If
            End Sub
        h.captureBtnH = captureBtnH
        AddHandler C24_Desk.Capture.OnButtonReleased, captureBtnH

        'ajout du handler Reaper
        Dim ReaperQuickPunchH As ReaperEventPipeMonitor.On_HookPostCommand2EventHandler =
                Sub(Command As Integer, val As Integer, valHw As Integer, relmode As Integer)
                    C24_Desk.Quick_Punch.IsBlinking = False
                    If Command = 40252 Then

                    ElseIf Command = 40076 Then
                        C24_Desk.Quick_Punch.Led = True
                    ElseIf Command = 40253 Then
                        C24_Desk.Quick_Punch.IsBlinking = True
                    End If
                End Sub
        h.ReaperQuickPunchH = ReaperQuickPunchH
        AddHandler ReaperEventPipeMonitor.On_HookPostCommand2, ReaperQuickPunchH





        'init Pre et Post Roll
        Dim PreRollTime As Double = 0
        Dim PreRollTimeStr = Reaper.utility.GetProjectExtState("C24pre_roll", "duration", "2")
        Double.TryParse(PreRollTimeStr, PreRollTime)
        Dim PostRollTime As Double = 0
        Dim PostRollTimeStr = Reaper.utility.GetProjectExtState("C24post_roll", "duration", "2")
        Double.TryParse(PostRollTimeStr, PostRollTime)
        Dim PreRollActive As Boolean = False
        Dim PostRollActive As Boolean = False
        Dim SelectionStartTime As Double = 0


        Dim PrePostRoll_OnPlay = Sub()
                                     'activation du PrePostRoll
                                     If PreRollActive OrElse PostRollActive Then
                                         Dim Selection = Reaper.utility.GetSelection
                                         If Selection.Item1 > 0 AndAlso Selection.Item2 > 0 Then
                                             'active le Pre roll
                                             SelectionStartTime = Selection.Item1
                                             Reaper.utility.SetCursorPosition(Selection.Item1 - PreRollTime, True, True)
                                             'active le Post roll

                                             ActiveTimeMonitoring = True
                                             MonitorTimeValue = Selection.Item2 + PostRollTime

                                             'Gestion du mode repeat en autonomie pour gérer pre et post roll

                                             ' découple la sélection de la loop sélection
                                             Reaper.utility.Main_OnCommand(40750)
                                             'supprime la sélection loop
                                             Reaper.utility.Main_OnCommand(40634)
                                         Else
                                             ActiveTimeMonitoring = False
                                         End If

                                     End If
                                 End Sub
        Dim PrePostRoll_OnStop = Sub()
                                     'PrePostRoll activé
                                     If PreRollActive OrElse PostRollActive Then
                                         ActiveTimeMonitoring = False
                                         ' recouple la sélection de la loop sélection
                                         Reaper.utility.Main_OnCommand(40749)
                                     End If
                                 End Sub

        ' ── PlayState change (Reaper → C24) ───────────────────────────────────
        Dim PlayStateChangeH As ReaperTransportManager.On_PlayStateChangeEventHandler =
            Sub(PlayState As Integer)

                RefreshC24_TransportBtnLed(PlayState)
                If PlayState = 1 Then
                    PrePostRoll_OnPlay.Invoke
                ElseIf PlayState = 0 Then
                    PrePostRoll_OnStop.Invoke
                End If
            End Sub
        h.PlayStateChangeH = PlayStateChangeH
        AddHandler _ReaperTransportManager.On_PlayStateChange, PlayStateChangeH

        ' ── Rec ──────────────────────────────────────────────────────────────
        Dim RecBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If C24_Desk.Rec.IsBlinking = True OrElse C24_Desk.Rec.Led = False Then
                    PlayLoopSavedState = PlayLoopAL.State
                    PlayLoopAL.State = C24_Desk.Loop_Rec.Led
                Else
                    PlayLoopAL.State = PlayLoopSavedState
                End If
                SetReaperPlayState(C24TransportBtn.Rec)

            End Sub
        h.RecBtnH = RecBtnH
        AddHandler C24_Desk.Rec.OnButtonReleased, RecBtnH
        ' ── Play ──────────────────────────────────────────────────────────────
        Dim PlayBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)

                PrePostRoll_OnPlay.Invoke
                SetReaperPlayState(C24TransportBtn.Play)

            End Sub
        h.PlayBtnH = PlayBtnH
        AddHandler C24_Desk.Play.OnButtonReleased, PlayBtnH

        ' ── Stop ──────────────────────────────────────────────────────────────
        Dim StopBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)


                PrePostRoll_OnStop.Invoke
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
                If Opt OrElse Ctrl Then

                Else
                    C24_Desk.Fwd.Led = True
                    SetReaperPlayState(C24TransportBtn.Fwd)
                    _fwdTimer.Start()
                End If
            End Sub
        h.FwdBtnH = FwdBtnH
        AddHandler C24_Desk.Fwd.OnButtonPressed, FwdBtnH

        Dim FwdBtnReleasedH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Opt OrElse Ctrl Then
                    ReBuildMarkersRegions()
                    Dim pos = Reaper.utility.GetCursorPosition



                    If Opt Then
                        'active sélection start
                        If Ctl Then
                            Reaper.utility.Main_OnCommand(40625)
                        End If
                        'aller au marker d'après
                        Dim NextMarker As Marker = FindNextPrevMarker(Markers.Values.ToList, pos, True)
                        If NextMarker IsNot Nothing Then
                            Reaper.utility.SetCursorPosition(NextMarker.Start_Pos, True, True)
                        End If
                        'active sélection End
                        If Ctl Then
                            Reaper.utility.Main_OnCommand(40626)
                        End If

                    ElseIf Ctrl Then
                        'aller a la région d'après
                        Dim NextRegion As Region = Nothing
                        If Shift Then
                            NextRegion = FindNextPrevRegion(Regions.Values.ToList, pos, True, True)
                            If NextRegion IsNot Nothing Then
                                Reaper.utility.SetCursorPosition(NextRegion.End_pos, True, True)
                            End If
                        Else
                            NextRegion = FindNextPrevRegion(Regions.Values.ToList, pos, True, False)
                            If NextRegion IsNot Nothing Then
                                Reaper.utility.SetCursorPosition(NextRegion.Start_Pos, True, True)
                            End If
                        End If

                        If Ctl Then
                            'aller a la région d'après et la sélectionner

                            If NextRegion IsNot Nothing Then
                                Reaper.utility.GetSet_LoopTimeRange(True, False, False, NextRegion.Start_Pos, NextRegion.End_pos, False)
                            End If
                        End If

                    End If
                Else
                    _fwdTimer.Stop()
                    C24_Desk.Fwd.Led = False
                End If



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
                If Opt OrElse Ctrl Then

                Else
                    C24_Desk.Rew.Led = True
                    SetReaperPlayState(C24TransportBtn.Rwd)
                    _rwdTimer.Start()
                End If

            End Sub
        h.RwdBtnPressedH = RwdBtnPressedH
        AddHandler C24_Desk.Rew.OnButtonPressed, RwdBtnPressedH

        Dim RwdBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)

                If Opt OrElse Ctrl Then
                    ReBuildMarkersRegions()
                    Dim pos = Reaper.utility.GetCursorPosition

                    If Opt Then
                        'active sélection start
                        If Ctl Then
                            Reaper.utility.Main_OnCommand(40625)
                        End If
                        'aller au marker d'avant
                        Dim LastMarker As Marker = FindNextPrevMarker(Markers.Values.ToList, pos, False)
                        If LastMarker IsNot Nothing Then
                            Reaper.utility.SetCursorPosition(LastMarker.Start_Pos, True, True)
                        End If
                        'active sélection End
                        If Ctl Then
                            Reaper.utility.Main_OnCommand(40626)
                        End If

                    ElseIf Ctrl Then
                        'aller a la région d'avant
                        Dim LastRegion As Region = Nothing
                        If Shift Then
                            LastRegion = FindNextPrevRegion(Regions.Values.ToList, pos, False, True)
                            If LastRegion IsNot Nothing Then
                                Reaper.utility.SetCursorPosition(LastRegion.End_pos, True, True)
                            End If

                        Else
                            LastRegion = FindNextPrevRegion(Regions.Values.ToList, pos, False, False)
                            If LastRegion IsNot Nothing Then
                                Reaper.utility.SetCursorPosition(LastRegion.Start_Pos, True, True)
                            End If

                        End If

                        If Ctl Then
                            'aller a la région d'avant et la sélectionner
                            If LastRegion IsNot Nothing Then
                                Reaper.utility.GetSet_LoopTimeRange(True, False, False, LastRegion.Start_Pos, LastRegion.End_pos, False)
                            End If
                        End If

                    End If


                Else
                    _rwdTimer.Stop()
                    C24_Desk.Rew.Led = False
                End If




            End Sub
        h.RwdBtnH = RwdBtnH
        AddHandler C24_Desk.Rew.OnButtonReleased, RwdBtnH



        'In
        Dim InBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Reaper.utility.Main_OnCommand(40625)
            End Sub
        h.InBtnH = InBtnH
        AddHandler C24_Desk.In.OnButtonReleased, InBtnH

        'Out
        Dim OutBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Reaper.utility.Main_OnCommand(40626)
            End Sub
        h.OutBtnH = OutBtnH
        AddHandler C24_Desk.Out.OnButtonReleased, OutBtnH

        'Goto Start
        Dim Goto_StartBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Reaper.utility.Main_OnCommand(40042)
            End Sub
        h.Goto_StartBtnH = Goto_StartBtnH
        AddHandler C24_Desk.Goto_Start.OnButtonReleased, Goto_StartBtnH

        'GotoEnd
        Dim Goto_EndBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Reaper.utility.Main_OnCommand(40043)
            End Sub
        h.Goto_EndBtnH = Goto_EndBtnH
        AddHandler C24_Desk.Goto_End.OnButtonReleased, Goto_EndBtnH

        'LoopRec
        Dim Loop_RecBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                C24_Desk.Loop_Rec.Led = Not C24_Desk.Loop_Rec.Led
            End Sub
        h.Loop_RecBtnH = Loop_RecBtnH
        AddHandler C24_Desk.Loop_Rec.OnButtonReleased, Loop_RecBtnH

        'PrePost Roll




        'init pre Roll
        C24_Desk.Pre_Roll.Led = If(Reaper.utility.GetProjectExtState("C24pre_roll", "active", "0") = "0", False, True)
        PreRollActive = C24_Desk.Pre_Roll.Led
        'Pre roll
        Dim Pre_RollBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)

                If C24_Desk.Pre_Roll.IsBlinking = True Then
                    'quitte le mode paramétrage du pré roll
                    C24_Desk.Pre_Roll.IsBlinking = False


                    'récupère la durée de la sélection

                    Dim LoopSelection = Reaper.utility.GetLoopSelection
                    PreRollTime = LoopSelection.Item2 - LoopSelection.Item1
                    'enregistre la duréee
                    Reaper.utility.SetProjectExtState("C24pre_roll", "duration", PreRollTime.ToString)
                    'supprime la sélection loop
                    Reaper.utility.Main_OnCommand(40634)
                    ' recouple la sélection de la loop sélection
                    Reaper.utility.Main_OnCommand(40749)

                    'active le mode pre roll
                    Reaper.utility.SetProjectExtState("C24pre_roll", "active", "1")
                    C24_Desk.Pre_Roll.Led = True

                    'remet le Jogwheel tool mode à none
                    JogWheelToolMode = JogWheelToolModes.None

                Else
                    If Ctrl AndAlso C24_Desk.Pre_Roll.IsBlinking = False Then
                        'mode paramétrage du pré roll
                        'Passage de la jogwheel en mode Pre Rool
                        _EditToolsHandlers.First.Tools_C24RadioBtnHandler.SetAllBtnOff()
                        JogWheelToolMode = JogWheelToolModes.PreRool
                        'paramétrage du pré roll
                        C24_Desk.Pre_Roll.IsBlinking = True

                        ' découple la sélection de la loop sélection
                        Reaper.utility.Main_OnCommand(40750)
                        'supprime la sélection loop
                        Reaper.utility.Main_OnCommand(40634)
                        'lire PreRoll
                        PreRollTimeStr = Reaper.utility.GetProjectExtState("C24pre_roll", "duration", "2")
                        Double.TryParse(PreRollTimeStr, PreRollTime)
                        'créé une sélection loop de la durée du PreRoll
                        Reaper.utility.GetSet_LoopTimeRange(True, True, True, _ReaperProjectMonitor.SelectionStartTime - PreRollTime, _ReaperProjectMonitor.SelectionStartTime)
                        Reaper.utility.SetCursorPosition(_ReaperProjectMonitor.SelectionStartTime - PreRollTime, True, True)

                    Else
                        'Active / Désactive pre roll (interne pas dans reaper)

                        C24_Desk.Pre_Roll.Led = Not C24_Desk.Pre_Roll.Led
                        Reaper.utility.SetProjectExtState("C24pre_roll", "active", If(C24_Desk.Pre_Roll.Led = True, "1", "0"))
                        PreRollActive = C24_Desk.Pre_Roll.Led

                        If C24_Desk.Pre_Roll.Led Then

                            PreRollTimeStr = Reaper.utility.GetProjectExtState("C24pre_roll", "duration", "2")
                            Double.TryParse(PreRollTimeStr, PreRollTime)
                        Else
                            PreRollTime = 0
                            Reaper.utility.SetCursorPosition(SelectionStartTime, True, True)
                        End If

                        If C24_Desk.Post_Roll.Led Then

                            PostRollTimeStr = Reaper.utility.GetProjectExtState("C24post_roll", "duration", "2")
                            Double.TryParse(PostRollTimeStr, PostRollTime)
                        Else
                            PostRollTime = 0
                        End If



                    End If
                End If


            End Sub
        h.Pre_RollBtnH = Pre_RollBtnH
        AddHandler C24_Desk.Pre_Roll.OnButtonReleased, Pre_RollBtnH


        'init Post Roll
        C24_Desk.Post_Roll.Led = If(Reaper.utility.GetProjectExtState("C24post_roll", "active", "0") = "0", False, True)
        PostRollActive = C24_Desk.Post_Roll.Led
        'Post roll
        Dim Post_RollBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)

                If C24_Desk.Post_Roll.IsBlinking = True Then
                    'quitte le mode paramétrage du pré roll
                    C24_Desk.Post_Roll.IsBlinking = False


                    'récupère la durée de la sélection

                    Dim LoopSelection = Reaper.utility.GetLoopSelection
                    PostRollTime = LoopSelection.Item2 - LoopSelection.Item1
                    'enregistre la duréee
                    Reaper.utility.SetProjectExtState("C24post_roll", "duration", PostRollTime.ToString)
                    'supprime la sélection loop
                    Reaper.utility.Main_OnCommand(40634)
                    ' recouple la sélection de la loop sélection
                    Reaper.utility.Main_OnCommand(40749)

                    'active le mode post roll
                    Reaper.utility.SetProjectExtState("C24post_roll", "active", "1")
                    C24_Desk.Post_Roll.Led = True

                    'remet le Jogwheel tool mode à none
                    JogWheelToolMode = JogWheelToolModes.None

                Else
                    If Ctrl AndAlso C24_Desk.Post_Roll.IsBlinking = False Then
                        'mode paramétrage du Post roll
                        'Passage de la jogwheel en mode Post Rool
                        _EditToolsHandlers.First.Tools_C24RadioBtnHandler.SetAllBtnOff()
                        JogWheelToolMode = JogWheelToolModes.PostRool
                        'paramétrage du Post roll
                        C24_Desk.Post_Roll.IsBlinking = True

                        ' découple la sélection de la loop sélection
                        Reaper.utility.Main_OnCommand(40750)
                        'supprime la sélection loop
                        Reaper.utility.Main_OnCommand(40634)
                        'lire PostRoll
                        PostRollTimeStr = Reaper.utility.GetProjectExtState("C24post_roll", "duration", "2")
                        Double.TryParse(PostRollTimeStr, PostRollTime)
                        'créé une sélection loop de la durée du PostRoll
                        Reaper.utility.GetSet_LoopTimeRange(True, True, True, _ReaperProjectMonitor.SelectionEndTime, _ReaperProjectMonitor.SelectionEndTime + PostRollTime)
                        Reaper.utility.SetCursorPosition(_ReaperProjectMonitor.SelectionEndTime + PostRollTime, True, True)

                    Else
                        'Active / Désactive Post roll (interne pas dans reaper)

                        C24_Desk.Post_Roll.Led = Not C24_Desk.Post_Roll.Led
                        Reaper.utility.SetProjectExtState("C24post_roll", "active", If(C24_Desk.Post_Roll.Led = True, "1", "0"))
                        PostRollActive = C24_Desk.Post_Roll.Led

                        If C24_Desk.Pre_Roll.Led Then

                            PreRollTimeStr = Reaper.utility.GetProjectExtState("C24pre_roll", "duration", "2")
                            Double.TryParse(PreRollTimeStr, PreRollTime)
                        Else
                            PreRollTime = 0
                        End If

                        If C24_Desk.Post_Roll.Led Then

                            PostRollTimeStr = Reaper.utility.GetProjectExtState("C24post_roll", "duration", "2")
                            Double.TryParse(PostRollTimeStr, PostRollTime)

                        Else
                            PostRollTime = 0
                        End If



                    End If
                End If


            End Sub
        h.Post_RollBtnH = Post_RollBtnH
        AddHandler C24_Desk.Post_Roll.OnButtonReleased, Post_RollBtnH

        'reaper selection active 
        Dim SelectionChangeH As ReaperProjectMonitor.On_SelectionChangeEventHandler =
            Sub(IsSelectionActive As Boolean)
                If IsSelectionActive = False Then
                    C24_Desk.In.IsBlinking = False
                    C24_Desk.Out.IsBlinking = False


                Else
                    If _ReaperProjectMonitor.SelectionStartTime <= 0 OrElse _ReaperProjectMonitor.SelectionEndTime = _ReaperProjectMonitor.SelectionStartTime Then
                        C24_Desk.In.Led = False
                    Else
                        C24_Desk.In.Led = True
                    End If
                    If _ReaperProjectMonitor.SelectionEndTime <= 0 OrElse _ReaperProjectMonitor.SelectionEndTime = _ReaperProjectMonitor.SelectionStartTime Then
                        C24_Desk.Out.Led = False
                    Else
                        C24_Desk.Out.Led = True
                    End If



                End If

            End Sub
        h.SelectionChangeH = SelectionChangeH
        AddHandler _ReaperProjectMonitor.On_SelectionChange, SelectionChangeH

        Dim TimeValueExcedH As C24ReaperController.On_MonitorTimeValueExcedEventHandler =
                                            Sub()
                                                If PlayLoopAL.State Then
                                                    Reaper.utility.SetCursorPosition(SelectionStartTime - PreRollTime, True, True)

                                                Else
                                                    Reaper.utility.Main_OnCommand(1016)
                                                End If
                                            End Sub
        h.TimeValueExcedH = TimeValueExcedH
        AddHandler Me.On_MonitorTimeValueExced, TimeValueExcedH


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
        '0=stop  1=play  2=pause    5=rec+play 6=pause+rec
        Dim ActualPlayState = _ReaperTransportManager.PlayState
        Select Case _C24TransportBtn
            Case C24TransportBtn.Play
                If C24_Desk.Rec.IsPressed Then
                    'Appuis sur REC et play pour déclencher le mode rec
                    Select Case ActualPlayState
                        Case 0
                            Set_ReaperPlayState = 5
                        Case 1
                            Set_ReaperPlayState = 5
                        Case 2
                            Set_ReaperPlayState = 6
                        Case 5
                            Set_ReaperPlayState = 6
                        Case 6
                            Set_ReaperPlayState = 5
                    End Select

                Else
                    'Mode play simple
                    Select Case ActualPlayState
                        Case 0
                            Set_ReaperPlayState = 1
                        Case 1
                            Set_ReaperPlayState = 2
                        Case 2
                            Set_ReaperPlayState = 1
                        Case 5
                            Set_ReaperPlayState = 6
                        Case 6
                            Set_ReaperPlayState = 5
                    End Select

                End If

            Case C24TransportBtn.Stop
                Set_ReaperPlayState = 0

            Case C24TransportBtn.Fwd

                Reaper.utility.Main_OnCommand_SafeUI(40085)
                '_ReaperTransportManager.FastForward()

            Case C24TransportBtn.Rwd
                Reaper.utility.Main_OnCommand_SafeUI(40084)
                '_ReaperTransportManager.Rewind()

        End Select
        If Set_ReaperPlayState IsNot Nothing Then

            'Reaper.utility.SetReaperPlayState(Set_ReaperPlayState)
            Select Case Set_ReaperPlayState
                Case 0  ' Stop
                    Reaper.utility.Main_OnCommand_SafeUI(1016)
                Case 1  ' Play
                    Reaper.utility.Main_OnCommand_SafeUI(1007)
                Case 2  ' Pause
                    Reaper.utility.Main_OnCommand_SafeUI(1008)

                Case 5  ' Record + Play → lancer l'enregistrement (play implicite)
                    Reaper.utility.Main_OnCommand_SafeUI(1013)
                Case 6  ' Pause + Record → pause
                    Reaper.utility.Main_OnCommand_SafeUI(1008)
            End Select
            RefreshC24_TransportBtnLed(Set_ReaperPlayState)

        End If


    End Sub
    Private Sub RefreshC24_TransportBtnLed(PlayState As Integer)
        C24_Desk.Play.IsBlinking = False
        C24_Desk.Stop.IsBlinking = False
        C24_Desk.Rew.IsBlinking = False
        C24_Desk.Fwd.IsBlinking = False
        C24_Desk.Rec.IsBlinking = False
        C24_Desk.Talk_Back.IsBlinking = False
        '0=stop  1=play  2=pause    5=rec+play 6=pause+rec
        Select Case PlayState
            Case 0

                C24_Desk.Stop.Led = True
            Case 1

                C24_Desk.Play.Led = True
            Case 2

                C24_Desk.Play.IsBlinking = True

            Case 5

                C24_Desk.Rec.Led = True
                C24_Desk.Play.Led = True
            Case 6

                C24_Desk.Rec.Led = True
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
            RemoveHandler Me.On_MonitorTimeValueExced, h.TimeValueExcedH
            RemoveHandler C24_Desk.Goto_Start.OnButtonReleased, h.Goto_StartBtnH
            RemoveHandler C24_Desk.Goto_End.OnButtonReleased, h.Goto_EndBtnH
            RemoveHandler C24_Desk.Loop_Rec.OnButtonReleased, h.Loop_RecBtnH
            RemoveHandler C24_Desk.Pre_Roll.OnButtonReleased, h.Pre_RollBtnH
            RemoveHandler C24_Desk.Post_Roll.OnButtonReleased, h.Post_RollBtnH
            RemoveHandler _ReaperTransportManager.On_PlayStateChange, h.PlayStateChangeH
            RemoveHandler C24_Desk.Rec.OnButtonReleased, h.RecBtnH
            RemoveHandler C24_Desk.Play.OnButtonReleased, h.PlayBtnH
            RemoveHandler C24_Desk.Stop.OnButtonReleased, h.StopBtnH
            RemoveHandler C24_Desk.Fwd.OnButtonPressed, h.FwdBtnH
            RemoveHandler C24_Desk.Fwd.OnButtonReleased, h.FwdBtnReleasedH
            RemoveHandler C24_Desk.Rew.OnButtonPressed, h.RwdBtnPressedH
            RemoveHandler C24_Desk.Rew.OnButtonReleased, h.RwdBtnH
            RemoveHandler _ReaperProjectMonitor.On_SelectionChange, h.SelectionChangeH
            RemoveHandler C24_Desk.In.OnButtonReleased, h.InBtnH
            RemoveHandler C24_Desk.Out.OnButtonReleased, h.OutBtnH
            RemoveHandler C24_Desk.Capture.OnButtonReleased, h.captureBtnH
            RemoveHandler C24_Desk.Quick_Punch.OnButtonReleased, h.Quick_PunchBtnH
            RemoveHandler ReaperEventPipeMonitor.On_HookPostCommand2, h.ReaperQuickPunchH
            h.PlayLoopAL.Dispose()

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


    ''' <summary>
    ''' Active/désactive les enveloppes et les led de C24 pour les Faders/Pan etc.. lors de l'appuis sur le bouton C24
    ''' </summary>
    Sub AddAutomationEnableHandlers()
        RemoveAutomationEnableHandlers()

        Dim h As New AutomationEnableHandlers

        Dim Auto_SuspendAL As New C24ActionLinker(C24_Desk.Auto_Suspend, 40908)
        h.Auto_SuspendAL = Auto_SuspendAL

        Dim Write_Auto_To_StartBtnH As C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Reaper.utility.Main_OnCommand_SafeUI(42014)
            End Sub
        h.Write_Auto_To_StartBtnH = Write_Auto_To_StartBtnH
        AddHandler C24_Desk.Write_Auto_To_Start.OnButtonReleased, Write_Auto_To_StartBtnH

        Dim Write_Auto_To_EndBtnH As C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Reaper.utility.Main_OnCommand_SafeUI(42015)
            End Sub
        h.Write_Auto_To_EndBtnH = Write_Auto_To_EndBtnH
        AddHandler C24_Desk.Write_Auto_To_End.OnButtonReleased, Write_Auto_To_EndBtnH

        Dim Write_Auto_To_AllBtnH As C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Shift Then
                    'selection
                    Reaper.utility.Main_OnCommand_SafeUI(42013)
                Else
                    'all project
                    Reaper.utility.Main_OnCommand_SafeUI(42017)
                End If

            End Sub
        h.Write_Auto_To_AllBtnH = Write_Auto_To_AllBtnH
        AddHandler C24_Desk.Write_Auto_To_All.OnButtonReleased, Write_Auto_To_AllBtnH

        Dim Auto_FaderBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)

                Dim Newstatus As Boolean = False


                If C24_Desk.Auto_Fader.IsBlinking Then
                    Newstatus = Not C24_Desk.Auto_Fader.IsBlinking
                Else
                    Newstatus = Not C24_Desk.Auto_Fader.Led
                End If

                C24_Desk.Auto_Fader.IsBlinking = False

                Dim AnyEnvIsArm As Boolean = True
                Dim TracksWithNoEnv As New List(Of Integer)
                For Each ReaperTrk In _ReaperProj.Tracks
                    If ReaperTrk.AutoMode <> AutoMode.None Then
                        If ReaperTrk.Envelopes.AllOfType(Of ReaperTrackVolEnvelope).Count > 0 Then
                            'active/désactive les enveloppes existantes
                            For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperTrackVolEnvelope)
                                env.Active = Not env.Active
                                'env.Armed = Newstatus
                                env.Refresh()

                                If env.Armed Then
                                    AnyEnvIsArm = False
                                End If
                            Next
                        Else
                            'ajoute le track nb a la liste des pistes qui n'ont pas d'envelope pour le message.
                            TracksWithNoEnv.Add(ReaperTrk.TrackNumber)
                        End If


                    End If

                Next


                If AnyEnvIsArm = False Then
                    If C24_Desk.Auto_Write.IsBlinking Then
                        C24_Desk.Auto_Fader.IsBlinking = True
                    Else
                        C24_Desk.Auto_Fader.Led = True
                    End If
                End If

                If TracksWithNoEnv.Count > 0 AndAlso Newstatus Then
                    Dim mess = BuildEnvMessage(TracksWithNoEnv)
                    C24DialogMessage("Arm Env", mess)

                End If

            End Sub
        h.Auto_FaderBtnH = Auto_FaderBtnH
        AddHandler C24_Desk.Auto_Fader.OnButtonReleased, Auto_FaderBtnH

        Dim Auto_PanBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Dim Newstatus As Boolean = False


                If C24_Desk.Auto_Pan.IsBlinking Then
                    Newstatus = Not C24_Desk.Auto_Pan.IsBlinking
                Else
                    Newstatus = Not C24_Desk.Auto_Pan.Led
                End If

                C24_Desk.Auto_Pan.IsBlinking = False

                Dim AnyEnvIsArm As Boolean = True
                Dim TracksWithNoEnv As New List(Of Integer)
                For Each ReaperTrk In _ReaperProj.Tracks
                    If ReaperTrk.AutoMode <> AutoMode.None Then
                        If ReaperTrk.Envelopes.AllOfType(Of ReaperTrackPanEnvelope).Count > 0 Then
                            'active/désactive les enveloppes existantes
                            For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperTrackPanEnvelope)
                                env.Active = Not env.Active
                                'env.Armed = Newstatus
                                env.Refresh()

                                If env.Armed Then
                                    AnyEnvIsArm = False
                                End If
                            Next
                        Else
                            'ajoute le track nb a la liste des pistes qui n'ont pas d'envelope pour le message.
                            TracksWithNoEnv.Add(ReaperTrk.TrackNumber)
                        End If


                    End If

                Next


                If AnyEnvIsArm = False Then
                    If C24_Desk.Auto_Write.IsBlinking Then
                        C24_Desk.Auto_Pan.IsBlinking = True
                    Else
                        C24_Desk.Auto_Pan.Led = True
                    End If
                End If

                If TracksWithNoEnv.Count > 0 AndAlso Newstatus Then
                    Dim mess = BuildEnvMessage(TracksWithNoEnv)
                    C24DialogMessage("Arm Env", mess)
                End If

            End Sub
        h.Auto_PanBtnH = Auto_PanBtnH
        AddHandler C24_Desk.Auto_Pan.OnButtonReleased, Auto_PanBtnH


        Dim Auto_MuteBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Dim Newstatus As Boolean = False


                If C24_Desk.Auto_Mute.IsBlinking Then
                    Newstatus = Not C24_Desk.Auto_Mute.IsBlinking
                Else
                    Newstatus = Not C24_Desk.Auto_Mute.Led
                End If

                C24_Desk.Auto_Mute.IsBlinking = False

                Dim AnyEnvIsArm As Boolean = True
                Dim TracksWithNoEnv As New List(Of Integer)
                For Each ReaperTrk In _ReaperProj.Tracks
                    If ReaperTrk.AutoMode <> AutoMode.None Then
                        If ReaperTrk.Envelopes.AllOfType(Of ReaperTrackMuteEnvelope).Count > 0 Then
                            'active/désactive les enveloppes existantes
                            For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperTrackMuteEnvelope)
                                env.Active = Not env.Active
                                'env.Armed = Newstatus
                                env.Refresh()

                                If env.Armed Then
                                    AnyEnvIsArm = False
                                End If
                            Next
                        Else
                            'ajoute le track nb a la liste des pistes qui n'ont pas d'envelope pour le message.
                            TracksWithNoEnv.Add(ReaperTrk.TrackNumber)
                        End If


                    End If

                Next


                If AnyEnvIsArm = False Then
                    If C24_Desk.Auto_Write.IsBlinking Then
                        C24_Desk.Auto_Mute.IsBlinking = True
                    Else
                        C24_Desk.Auto_Mute.Led = True
                    End If
                End If

                If TracksWithNoEnv.Count > 0 AndAlso Newstatus Then
                    Dim mess = BuildEnvMessage(TracksWithNoEnv)
                    C24DialogMessage("Arm Env", mess)
                End If
            End Sub
        h.Auto_MuteBtnH = Auto_MuteBtnH
        AddHandler C24_Desk.Auto_Mute.OnButtonReleased, Auto_MuteBtnH

        Dim Auto_Send_LevelBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Dim Newstatus As Boolean = False


                If C24_Desk.Auto_Send_Level.IsBlinking Then
                    Newstatus = Not C24_Desk.Auto_Send_Level.IsBlinking
                Else
                    Newstatus = Not C24_Desk.Auto_Send_Level.Led
                End If

                C24_Desk.Auto_Send_Level.IsBlinking = False

                Dim AnyEnvIsArm As Boolean = True
                Dim TracksWithNoEnv As New List(Of Integer)
                For Each ReaperTrk In _ReaperProj.Tracks
                    If ReaperTrk.AutoMode <> AutoMode.None Then
                        If ReaperTrk.Envelopes.AllOfType(Of ReaperSendVolEnvelope).Count > 0 Then
                            'active/désactive les enveloppes existantes
                            For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperSendVolEnvelope)
                                env.Active = Not env.Active
                                'env.Armed = Newstatus
                                env.Refresh()

                                If env.Armed Then
                                    AnyEnvIsArm = False
                                End If
                            Next
                        Else
                            'ajoute le track nb a la liste des pistes qui n'ont pas d'envelope pour le message.
                            TracksWithNoEnv.Add(ReaperTrk.TrackNumber)
                        End If


                    End If

                Next


                If AnyEnvIsArm = False Then
                    If C24_Desk.Auto_Write.IsBlinking Then
                        C24_Desk.Auto_Send_Level.IsBlinking = True
                    Else
                        C24_Desk.Auto_Send_Level.Led = True
                    End If
                End If

                If TracksWithNoEnv.Count > 0 AndAlso Newstatus Then
                    Dim mess = BuildEnvMessage(TracksWithNoEnv)
                    C24DialogMessage("Arm Env", mess)
                End If
            End Sub
        h.Auto_Send_LevelBtnH = Auto_Send_LevelBtnH
        AddHandler C24_Desk.Auto_Send_Level.OnButtonReleased, Auto_Send_LevelBtnH

        Dim Auto_Send_MuteBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Dim Newstatus As Boolean = False


                If C24_Desk.Auto_Send_Mute.IsBlinking Then
                    Newstatus = Not C24_Desk.Auto_Send_Mute.IsBlinking
                Else
                    Newstatus = Not C24_Desk.Auto_Send_Mute.Led
                End If

                C24_Desk.Auto_Send_Mute.IsBlinking = False

                Dim AnyEnvIsArm As Boolean = True
                Dim TracksWithNoEnv As New List(Of Integer)
                For Each ReaperTrk In _ReaperProj.Tracks
                    If ReaperTrk.AutoMode <> AutoMode.None Then
                        If ReaperTrk.Envelopes.AllOfType(Of ReaperSendMuteEnvelope).Count > 0 Then
                            'active/désactive les enveloppes existantes
                            For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperSendMuteEnvelope)
                                env.Active = Not env.Active
                                'env.Armed = Newstatus
                                env.Refresh()

                                If env.Armed Then
                                    AnyEnvIsArm = False
                                End If
                            Next
                        Else
                            'ajoute le track nb a la liste des pistes qui n'ont pas d'envelope pour le message.
                            TracksWithNoEnv.Add(ReaperTrk.TrackNumber)
                        End If


                    End If

                Next


                If AnyEnvIsArm = False Then
                    If C24_Desk.Auto_Write.IsBlinking Then
                        C24_Desk.Auto_Send_Mute.IsBlinking = True
                    Else
                        C24_Desk.Auto_Send_Mute.Led = True
                    End If
                End If

                If TracksWithNoEnv.Count > 0 AndAlso Newstatus Then
                    Dim mess = BuildEnvMessage(TracksWithNoEnv)
                    C24DialogMessage("Arm Env", mess)
                End If
            End Sub
        h.Auto_Send_MuteBtnH = Auto_Send_MuteBtnH
        AddHandler C24_Desk.Auto_Send_Mute.OnButtonReleased, Auto_Send_MuteBtnH

        Dim Auto_Plug_InBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Dim Newstatus As Boolean = False


                If C24_Desk.Auto_Plug_In.IsBlinking Then
                    Newstatus = Not C24_Desk.Auto_Plug_In.IsBlinking
                Else
                    Newstatus = Not C24_Desk.Auto_Plug_In.Led
                End If

                C24_Desk.Auto_Plug_In.IsBlinking = False

                Dim AnyEnvIsArm As Boolean = True
                Dim TracksWithNoEnv As New List(Of Integer)
                For Each ReaperTrk In _ReaperProj.Tracks
                    If ReaperTrk.AutoMode <> AutoMode.None Then
                        If ReaperTrk.Envelopes.AllOfType(Of ReaperFxParamEnvelope).Count > 0 Then
                            'active/désactive les enveloppes existantes
                            For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperFxParamEnvelope)
                                env.Active = Not env.Active
                                'env.Armed = Newstatus
                                env.Refresh()

                                If env.Armed Then
                                    AnyEnvIsArm = False
                                End If
                            Next
                        Else
                            'ajoute le track nb a la liste des pistes qui n'ont pas d'envelope pour le message.
                            TracksWithNoEnv.Add(ReaperTrk.TrackNumber)
                        End If


                    End If

                Next


                If AnyEnvIsArm = False Then
                    If C24_Desk.Auto_Write.IsBlinking Then
                        C24_Desk.Auto_Plug_In.IsBlinking = True
                    Else
                        C24_Desk.Auto_Plug_In.Led = True
                    End If
                End If

                If TracksWithNoEnv.Count > 0 AndAlso Newstatus Then
                    Dim mess = BuildEnvMessage(TracksWithNoEnv)
                    C24DialogMessage("Arm Env", mess)
                End If
            End Sub
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
            RemoveHandler C24_Desk.Write_Auto_To_Start.OnButtonReleased, h.Write_Auto_To_StartBtnH
            RemoveHandler C24_Desk.Write_Auto_To_All.OnButtonReleased, h.Write_Auto_To_AllBtnH
            RemoveHandler C24_Desk.Write_Auto_To_End.OnButtonReleased, h.Write_Auto_To_EndBtnH
            h.Auto_SuspendAL.Dispose()
        Next
        _AutomationEnableHandlers.Clear()
    End Sub
    ''' <summary>
    ''' Met à jour les leds de la section Automation Fader/Pan/ etc. quand le status armed change sur reaper
    ''' </summary>
    Sub AddReaperEnvArmedHandlers()
        RemoveReaperEnvArmedHandlers()


        For Each Trk In _ReaperProj.Tracks
            For Each env In Trk.Envelopes.Envelopes
                Dim h As New ReaperEnvArmedHandlers
                h.ReaperTrk = Trk
                h.env = env
                Select Case env.GetType

                    Case GetType(ReaperTrackVolEnvelope)
                        'init
                        RefreshAutomationFaderEnableLed()
                        Dim ReaperEnvArmChangeH As ReaperTrackEnvelope.On_ArmedChangeEventHandler =
                            Sub(_armed As Boolean)
                                RefreshAutomationFaderEnableLed()
                            End Sub
                        h.ReaperEnvArmChangeH = ReaperEnvArmChangeH
                        AddHandler env.On_ArmedChange, ReaperEnvArmChangeH
                        _ReaperEnvArmedHandlers.Add(h)
                    Case GetType(ReaperTrackPanEnvelope)
                        RefreshAutomationPanEnableLed()
                        Dim ReaperEnvArmChangeH As ReaperTrackEnvelope.On_ArmedChangeEventHandler =
                            Sub(_armed As Boolean)
                                RefreshAutomationPanEnableLed()
                            End Sub
                        h.ReaperEnvArmChangeH = ReaperEnvArmChangeH
                        AddHandler env.On_ArmedChange, ReaperEnvArmChangeH
                        _ReaperEnvArmedHandlers.Add(h)
                    Case GetType(ReaperTrackMuteEnvelope)
                        RefreshAutomationMuteEnableLed()
                        Dim ReaperEnvArmChangeH As ReaperTrackEnvelope.On_ArmedChangeEventHandler =
                            Sub(_armed As Boolean)
                                RefreshAutomationMuteEnableLed()
                            End Sub
                        h.ReaperEnvArmChangeH = ReaperEnvArmChangeH
                        AddHandler env.On_ArmedChange, ReaperEnvArmChangeH
                        _ReaperEnvArmedHandlers.Add(h)
                    Case GetType(ReaperSendVolEnvelope)
                        RefreshAutomationSendVolEnableLed()
                        Dim ReaperEnvArmChangeH As ReaperTrackEnvelope.On_ArmedChangeEventHandler =
                            Sub(_armed As Boolean)
                                RefreshAutomationSendVolEnableLed()
                            End Sub
                        h.ReaperEnvArmChangeH = ReaperEnvArmChangeH
                        AddHandler env.On_ArmedChange, ReaperEnvArmChangeH
                        _ReaperEnvArmedHandlers.Add(h)
                    Case GetType(ReaperSendMuteEnvelope)
                        RefreshAutomationSendMuteEnableLed()
                        Dim ReaperEnvArmChangeH As ReaperTrackEnvelope.On_ArmedChangeEventHandler =
                            Sub(_armed As Boolean)
                                RefreshAutomationSendMuteEnableLed()
                            End Sub
                        h.ReaperEnvArmChangeH = ReaperEnvArmChangeH
                        AddHandler env.On_ArmedChange, ReaperEnvArmChangeH
                        _ReaperEnvArmedHandlers.Add(h)
                    Case GetType(ReaperFxParamEnvelope)
                        RefreshAutomationPluginEnableLed()
                        Dim ReaperEnvArmChangeH As ReaperTrackEnvelope.On_ArmedChangeEventHandler =
                            Sub(_armed As Boolean)
                                RefreshAutomationPluginEnableLed()
                                If AutoSelect_Mode Then
                                    For Each kv In _C24trkPluginParamEnvelope
                                        If kv.Value.EnvIndex = env.EnvIndex Then
                                            kv.Key.SwitchActive.Led = env.Armed
                                        End If
                                    Next

                                End If
                            End Sub
                        h.ReaperEnvArmChangeH = ReaperEnvArmChangeH
                        AddHandler env.On_ArmedChange, ReaperEnvArmChangeH
                        _ReaperEnvArmedHandlers.Add(h)


                End Select


            Next
        Next





    End Sub
    Sub RemoveReaperEnvArmedHandlers()
        For Each h In _ReaperEnvArmedHandlers
            RemoveHandler h.env.On_ArmedChange, h.ReaperEnvArmChangeH
        Next
        _ReaperEnvArmedHandlers.Clear()
    End Sub
    Sub RefreshAutomationFaderEnableLed()



        C24_Desk.Auto_Fader.IsBlinking = False


        For Each ReaperTrk In _ReaperProj.Tracks
            If ReaperTrk.AutoMode <> AutoMode.None Then
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
            End If

        Next



    End Sub
    Sub RefreshAutomationPanEnableLed()



        C24_Desk.Auto_Pan.IsBlinking = False


        For Each ReaperTrk In _ReaperProj.Tracks
            If ReaperTrk.AutoMode <> AutoMode.None Then
                For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperTrackPanEnvelope)
                    If env.Armed Then
                        If C24_Desk.Auto_Write.IsBlinking = True Then
                            C24_Desk.Auto_Pan.IsBlinking = True
                        Else
                            C24_Desk.Auto_Pan.IsBlinking = False
                            C24_Desk.Auto_Pan.Led = True
                        End If
                        Return
                    End If

                Next
            End If
        Next



    End Sub
    Sub RefreshAutomationMuteEnableLed()



        C24_Desk.Auto_Mute.IsBlinking = False


        For Each ReaperTrk In _ReaperProj.Tracks
            If ReaperTrk.AutoMode <> AutoMode.None Then
                For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperTrackMuteEnvelope)
                    If env.Armed Then
                        If C24_Desk.Auto_Write.IsBlinking = True Then
                            C24_Desk.Auto_Mute.IsBlinking = True
                        Else
                            C24_Desk.Auto_Mute.IsBlinking = False
                            C24_Desk.Auto_Mute.Led = True
                        End If
                        Return
                    End If
                Next
            End If
        Next



    End Sub
    Sub RefreshAutomationSendVolEnableLed()



        C24_Desk.Auto_Send_Level.IsBlinking = False


        For Each ReaperTrk In _ReaperProj.Tracks
            If ReaperTrk.AutoMode <> AutoMode.None Then
                For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperSendVolEnvelope)
                    If env.Armed Then
                        If C24_Desk.Auto_Write.IsBlinking = True Then
                            C24_Desk.Auto_Send_Level.IsBlinking = True
                        Else
                            C24_Desk.Auto_Send_Level.IsBlinking = False
                            C24_Desk.Auto_Send_Level.Led = True
                        End If
                        Return
                    End If
                Next
            End If
        Next



    End Sub
    Sub RefreshAutomationSendMuteEnableLed()



        C24_Desk.Auto_Send_Mute.IsBlinking = False


        For Each ReaperTrk In _ReaperProj.Tracks
            If ReaperTrk.AutoMode <> AutoMode.None Then
                For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperSendMuteEnvelope)
                    If env.Armed Then
                        If C24_Desk.Auto_Write.IsBlinking = True Then
                            C24_Desk.Auto_Send_Mute.IsBlinking = True
                        Else
                            C24_Desk.Auto_Send_Mute.IsBlinking = False
                            C24_Desk.Auto_Send_Mute.Led = True
                        End If
                        Return
                    End If
                Next
            End If
        Next



    End Sub
    Sub RefreshAutomationPluginEnableLed()



        C24_Desk.Auto_Plug_In.IsBlinking = False


        For Each ReaperTrk In _ReaperProj.Tracks
            If ReaperTrk.AutoMode <> AutoMode.None Then
                For Each env In ReaperTrk.Envelopes.AllOfType(Of ReaperFxParamEnvelope)
                    If env.Armed Then
                        If C24_Desk.Auto_Write.IsBlinking = True Then
                            C24_Desk.Auto_Plug_In.IsBlinking = True
                        Else
                            C24_Desk.Auto_Plug_In.IsBlinking = False
                            C24_Desk.Auto_Plug_In.Led = True
                        End If
                        Return
                    End If
                Next
            End If
        Next



    End Sub

    ''' <summary>
    ''' Met à jour les led du Automation Mode dans la section Automation ainsi que sur chaque pistes de C24 si présente.
    ''' </summary>
    Sub AddReaperAutoModeHandlers()
        RemoveReaperAutoModeHandlers()

        RefreshAutomationModeLeds()
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



                    End If


                    RefreshAutomationModeLeds()


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
    Private Function BuildEnvMessage(TracksWithNoEnv As List(Of Integer)) As String
        Dim mess As String = "You need to Arm Env in Reaper for Tracks : "
        For Each trackNb In TracksWithNoEnv
            mess &= trackNb & ";"
        Next
        Return mess
    End Function

    Sub AddAutomationModifers()
        RemoveAutomationModifers()

        Dim h As New AutoModifiersHandler

        Dim WritePressBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                Write = True
            End Sub
        h.WritePressBtnH = WritePressBtnH
        AddHandler C24_Desk.Auto_Write.OnButtonPressed, WritePressBtnH

        Dim WriteRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Write = False
            End Sub
        h.WriteRealBtnH = WriteRealBtnH
        AddHandler C24_Desk.Auto_Write.OnButtonReleased, WriteRealBtnH

        Dim TouchPressBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                Touch = True
            End Sub
        h.TouchPressBtnH = TouchPressBtnH
        AddHandler C24_Desk.Auto_Touch.OnButtonPressed, TouchPressBtnH

        Dim TouchRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Touch = False
            End Sub
        h.TouchRealBtnH = TouchRealBtnH
        AddHandler C24_Desk.Auto_Touch.OnButtonReleased, TouchRealBtnH

        Dim LatchPressBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                Latch = True
            End Sub
        h.LatchPressBtnH = LatchPressBtnH
        AddHandler C24_Desk.Auto_Latch.OnButtonPressed, LatchPressBtnH

        Dim LatchRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Latch = False
            End Sub
        h.LatchRealBtnH = LatchRealBtnH
        AddHandler C24_Desk.Auto_Latch.OnButtonReleased, LatchRealBtnH

        Dim TrimPressBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                Trim = True
            End Sub
        h.TrimPressBtnH = TrimPressBtnH
        AddHandler C24_Desk.Auto_Trim.OnButtonPressed, TrimPressBtnH

        Dim TrimRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Trim = False
            End Sub
        h.TrimRealBtnH = TrimRealBtnH
        AddHandler C24_Desk.Auto_Trim.OnButtonReleased, TrimRealBtnH

        Dim ReadPressBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                Read = True
            End Sub
        h.ReadPressBtnH = ReadPressBtnH
        AddHandler C24_Desk.Auto_Read.OnButtonPressed, ReadPressBtnH

        Dim ReadRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Read = False
            End Sub
        h.ReadRealBtnH = ReadRealBtnH
        AddHandler C24_Desk.Auto_Read.OnButtonReleased, ReadRealBtnH

        Dim OffPressBtnH As Hardware.C24_LedButton.OnButtonPressedEventHandler =
            Sub(TrackNb As Integer?)
                Off = True
            End Sub
        h.OffPressBtnH = OffPressBtnH
        AddHandler C24_Desk.Auto_Off.OnButtonPressed, OffPressBtnH

        Dim OffRealBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                Off = False
            End Sub
        h.OffRealBtnH = OffRealBtnH
        AddHandler C24_Desk.Auto_Off.OnButtonReleased, OffRealBtnH

        _AutoModifiersHandlers.Add(h)
    End Sub
    Sub RemoveAutomationModifers()
        For Each h In _AutoModifiersHandlers
            RemoveHandler C24_Desk.Auto_Write.OnButtonPressed, h.WritePressBtnH
            RemoveHandler C24_Desk.Auto_Write.OnButtonReleased, h.WriteRealBtnH
            RemoveHandler C24_Desk.Auto_Touch.OnButtonPressed, h.TouchPressBtnH
            RemoveHandler C24_Desk.Auto_Touch.OnButtonReleased, h.TouchRealBtnH
            RemoveHandler C24_Desk.Auto_Latch.OnButtonPressed, h.LatchPressBtnH
            RemoveHandler C24_Desk.Auto_Latch.OnButtonReleased, h.LatchRealBtnH
            RemoveHandler C24_Desk.Auto_Trim.OnButtonPressed, h.TrimPressBtnH
            RemoveHandler C24_Desk.Auto_Trim.OnButtonReleased, h.TrimRealBtnH
            RemoveHandler C24_Desk.Auto_Off.OnButtonPressed, h.OffPressBtnH
            RemoveHandler C24_Desk.Auto_Off.OnButtonReleased, h.OffRealBtnH

        Next
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
                For i = 0 To 23
                    Dim c24Trk = C24_Desk.ChannelStrips(i)
                    If i < _ReaperTracksManager.Tracks.Count Then
                        Dim text As String = ""
                        If c24Trk.Vpot.Value IsNot Nothing Then
                            text = Hardware.Utility.GetScribbleTextfromDouble(c24Trk.Vpot.Value.Value)
                        End If
                        c24Trk.Vpot.ShowTempAllScribbleText(text)

                        If i = 0 Then
                            Dim Show_ValuesBacktoTextH As Hardware.C24_ScribbleStrip.OnOriginaltextBackEventHandler =
                            Sub()
                                C24_Desk.Show_Values.Led = False
                                RemoveHandler c24Trk.Vpot.ScribbleStrip.OnOriginaltextBack, Show_ValuesBacktoTextH
                            End Sub
                            AddHandler c24Trk.Vpot.ScribbleStrip.OnOriginaltextBack, Show_ValuesBacktoTextH
                        End If

                    End If

                Next

            End Sub
        h.Show_ValuesBtnH = Show_ValuesBtnH
        AddHandler C24_Desk.Show_Values.OnButtonReleased, Show_ValuesBtnH


        Dim Show_NamesBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
           Sub(TrackNb As Integer?)

               C24_Desk.Show_Channel_Names.Led = True

               For i = 0 To 23
                   Dim c24Trk = C24_Desk.ChannelStrips(i)

                   If i < _ReaperTracksManager.Tracks.Count Then
                       Dim reaperTrk = _ReaperTracksManager.Tracks(i)
                       c24Trk.ScribbleStrip.ShowTempText(reaperTrk.Name)

                       If i = 0 Then
                           Dim Show_Channel_NamesBacktoTextH As Hardware.C24_ScribbleStrip.OnOriginaltextBackEventHandler =
                           Sub()
                               C24_Desk.Show_Channel_Names.Led = False
                               RemoveHandler c24Trk.Vpot.ScribbleStrip.OnOriginaltextBack, Show_Channel_NamesBacktoTextH
                           End Sub
                           AddHandler c24Trk.Vpot.ScribbleStrip.OnOriginaltextBack, Show_Channel_NamesBacktoTextH
                       End If
                   End If
               Next

           End Sub
        h.Show_NamesBtnH = Show_NamesBtnH
        AddHandler C24_Desk.Show_Channel_Names.OnButtonReleased, Show_NamesBtnH

        Dim Show_InfosBtn As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
           Sub(TrackNb As Integer?)

               C24_Desk.Show_Info.Led = True
               C24_Desk.ScreenLine.ShowTempText(LastTextInfoToShow)

               Dim Show_InfoBacktoTextH As Hardware.C24_ScribbleScreen.OnOriginaltextBackEventHandler =
                          Sub()
                              C24_Desk.Show_Info.Led = False
                              RemoveHandler C24_Desk.ScreenLine.OnOriginaltextBack, Show_InfoBacktoTextH
                          End Sub
               AddHandler C24_Desk.ScreenLine.OnOriginaltextBack, Show_InfoBacktoTextH

           End Sub
        h.Show_InfosBtn = Show_InfosBtn
        AddHandler C24_Desk.Show_Info.OnButtonReleased, Show_InfosBtn


        _ShowHandlers.Add(h)

    End Sub
    Sub RemoveShowC24Handlers()
        For Each h In _ShowHandlers

            RemoveHandler C24_Desk.Vel_Sens_Encoders.OnButtonReleased, h.Vel_Sens_EncodersBtnH
            RemoveHandler C24_Desk.Show_Values.OnButtonReleased, h.Show_ValuesBtnH
            RemoveHandler C24_Desk.ChannelStrips(0).Vpot.OnScribbleScriptBackToOriginalText, h.ScribbleScriptBackToOriginalText
            RemoveHandler C24_Desk.Show_Info.OnButtonReleased, h.Show_InfosBtn

        Next
        _ShowHandlers.Clear()
    End Sub

    Sub AddScribbleScriptsHandlers()
        RemoveScribbleScriptsHandlers()

        For i = 0 To 23
            Dim h As New ScribbleScriptsHandler
            Dim c24Trk = C24_Desk.ChannelStrips(i)
            h.c24Trk = c24Trk

            Dim ScribbleTextDisplay As Hardware.C24_ScribbleStrip.OnScribbleTextDisplayEventHandler =
                    Sub(TrackNb As Integer, text As String)
                        LastTextInfoToShow = text
                    End Sub
            h.ScribbleTextDisplay = ScribbleTextDisplay
            AddHandler c24Trk.ScribbleStrip.OnScribbleTextDisplay, ScribbleTextDisplay

            _ScribbleScriptsHandlers.Add(h)
        Next



    End Sub
    Sub RemoveScribbleScriptsHandlers()

        For Each h In _ScribbleScriptsHandlers
            RemoveHandler h.c24Trk.ScribbleStrip.OnScribbleTextDisplay, h.ScribbleTextDisplay
        Next
        _ScribbleScriptsHandlers.Clear()
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

        Dim A_FBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Shift Then
                    If Ctrl Then
                        ResetC24SENDORDER("F")
                    Else
                        If ActualSendIndex = 5 Then
                            ActualSendIndex = -1
                            Flip_Mode = False
                            RefreshC24HomeViewMode()
                        Else
                            SetSendLetterLed("F")
                            ActualSendIndex = 5
                            RefreshC24SendsViewMode()
                        End If
                    End If


                Else
                    If Ctrl Then
                        ResetC24SENDORDER("A")
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
                End If

            End Sub
        h.A_FBtnH = A_FBtnH
        AddHandler C24_Desk.Send_A_F.OnButtonReleased, A_FBtnH

        Dim B_GBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Shift Then
                    If Ctrl Then
                        ResetC24SENDORDER("G")
                    Else
                        If ActualSendIndex = 6 Then
                            ActualSendIndex = -1
                            Flip_Mode = False
                            RefreshC24HomeViewMode()
                        Else
                            SetSendLetterLed("G")
                            ActualSendIndex = 6
                            RefreshC24SendsViewMode()
                        End If
                    End If
                Else
                    If Ctrl Then
                        ResetC24SENDORDER("B")
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
                End If

            End Sub
        h.B_GBtnH = B_GBtnH
        AddHandler C24_Desk.Send_B_G.OnButtonReleased, B_GBtnH


        Dim C_HBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Shift Then
                    If Ctrl Then
                        ResetC24SENDORDER("H")
                    Else
                        If ActualSendIndex = 7 Then
                            ActualSendIndex = -1
                            Flip_Mode = False
                            RefreshC24HomeViewMode()
                        Else
                            SetSendLetterLed("H")
                            ActualSendIndex = 7
                            RefreshC24SendsViewMode()
                        End If
                    End If
                Else
                    If Ctrl Then
                        ResetC24SENDORDER("C")
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
                End If

            End Sub
        h.C_HBtnH = C_HBtnH
        AddHandler C24_Desk.Send_C_H.OnButtonReleased, C_HBtnH

        Dim D_IBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Shift Then
                    If Ctrl Then
                        ResetC24SENDORDER("I")
                    Else
                        If ActualSendIndex = 8 Then
                            ActualSendIndex = -1
                            Flip_Mode = False
                            RefreshC24HomeViewMode()
                        Else
                            SetSendLetterLed("I")
                            ActualSendIndex = 8
                            RefreshC24SendsViewMode()
                        End If
                    End If
                Else
                    If Ctrl Then
                        ResetC24SENDORDER("D")
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
                End If

            End Sub
        h.D_IBtnH = D_IBtnH
        AddHandler C24_Desk.Send_D_I.OnButtonReleased, D_IBtnH

        Dim E_JBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Shift Then
                    If Ctrl Then
                        ResetC24SENDORDER("J")
                    Else
                        If ActualSendIndex = 9 Then
                            ActualSendIndex = -1
                            Flip_Mode = False
                            RefreshC24HomeViewMode()
                        Else
                            SetSendLetterLed("J")
                            ActualSendIndex = 9
                            RefreshC24SendsViewMode()
                        End If
                    End If
                Else
                    If Ctrl Then
                        ResetC24SENDORDER("E")
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
                End If

            End Sub
        h.E_JBtnH = E_JBtnH
        AddHandler C24_Desk.Send_E_J.OnButtonReleased, E_JBtnH

        _SendsLetterHandlers.Add(h)
    End Sub
    Sub RemoveSendsLetterHandlers()
        For Each h In _SendsLetterHandlers
            RemoveHandler C24_Desk.Send_A_F.OnButtonReleased, h.A_FBtnH
            RemoveHandler C24_Desk.Send_B_G.OnButtonReleased, h.B_GBtnH
            RemoveHandler C24_Desk.Send_C_H.OnButtonReleased, h.C_HBtnH
            RemoveHandler C24_Desk.Send_D_I.OnButtonReleased, h.D_IBtnH
            RemoveHandler C24_Desk.Send_E_J.OnButtonReleased, h.E_JBtnH
        Next
    End Sub
    Sub ResetC24SENDORDER(SendLetter As String)
        For Each track In _ReaperProj.Tracks
            Using editor As New ReaperTrackSendsEditor(track.TrackIndex, _ReaperProj.Tracks)
                editor.ResetC24SENDORDER(SendLetter)
            End Using
        Next
        C24_Desk.ScreenLine.ShowTempText("Send " & SendLetter & " are reset for all tracks.")
    End Sub
#End Region
#Region "ZoomPreset"
    Private Sub AddZoomPresetHandlers()
        RemoveZoomPresetHandlers()

        Dim h As New ZoomPresetStripHandler

        '----------------- ZoomPreset1 Handler -------------------------------------------------
        Dim Preset_1BtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub(TrackNb As Integer?)


                End Sub
        h.Preset_1BtnH = Preset_1BtnH
        AddHandler C24_Desk.Preset_1.OnButtonReleased, Preset_1BtnH

        '----------------- ZoomPreset2 Handler -------------------------------------------------
        Dim Preset_2BtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub(TrackNb As Integer?)



                End Sub
        h.Preset_2BtnH = Preset_2BtnH
        AddHandler C24_Desk.Preset_2.OnButtonReleased, Preset_2BtnH



        _ZoomPresetStripHandlers.Add(h)



    End Sub
    Private Sub RemoveZoomPresetHandlers()
        For Each h In _ZoomPresetStripHandlers
            RemoveHandler C24_Desk.Preset_1.OnButtonReleased, h.Preset_1BtnH
            RemoveHandler _ReaperTracksManager.On_TracksRebuilt, h.ZoomPreset1OnTracksRebuiltH
            RemoveHandler C24_Desk.Preset_2.OnButtonReleased, h.Preset_2BtnH
            RemoveHandler _ReaperTracksManager.On_TracksRebuilt, h.ZoomPreset2OnTracksRebuiltH
        Next
        _ZoomPresetStripHandlers.Clear()
    End Sub

#End Region

#Region "Function Keys"
    Private Sub AddFunctionKeysHandlers()
        RemoveFunctionKeysHandlers()

        Dim h As New FunctionKeysHandler

        '----------------- F1 Handler -------------------------------------------------
        Dim F1BtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub(TrackNb As Integer?)
                    If TrackNb IsNot Nothing Then
                        C24_Desk.F1.Led = If(TrackNb = 0, False, True)
                    Else
                        C24_Desk.F1.Led = Not C24_Desk.F1.Led
                    End If

                    If C24_Desk.F1.Led = True Then
                        'suivi des pistes dans le mixer
                        Dim ZoomPreset1H As C24ReaperController.OnBankChangeEndEventHandler =
                        Sub()
                            If _ReaperTracksManager.Tracks.Count > 0 Then
                                Reaper.utility.SetMixerScroll(_ReaperTracksManager.Tracks(0).TrackIndex)
                            End If

                        End Sub
                        h.ZoomPreset1H = ZoomPreset1H
                        AddHandler Me.OnBankChangeEnd, ZoomPreset1H

                        Reaper.utility.SetProjectExtState("C24FUNCTIONKEY", "1", "1")
                    Else
                        RemoveHandler Me.OnBankChangeEnd, h.ZoomPreset1H

                        C24_Desk.F1.Led = False
                        Reaper.utility.SetProjectExtState("C24FUNCTIONKEY", "1", "0")
                    End If

                End Sub
        h.F1BtnH = F1BtnH
        AddHandler C24_Desk.F1.OnButtonReleased, F1BtnH

        '----------------- F2 Handler -------------------------------------------------
        Dim F2BtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub(TrackNb As Integer?)

                    If TrackNb IsNot Nothing Then
                        C24_Desk.F2.Led = If(TrackNb = 0, False, True)
                    Else
                        C24_Desk.F2.Led = Not C24_Desk.F2.Led
                    End If

                    If C24_Desk.F2.Led = True Then
                        'selection de la première piste et suivi dans la vue générale
                        Dim ZoomPreset2H As C24ReaperController.OnBankChangeEndEventHandler =
                        Sub()
                            If _ReaperTracksManager.Tracks.Count > 0 Then
                                Dim ReaperTrk = _ReaperTracksManager.Tracks(0)
                                Dim c24cs = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(ReaperTrk.TrackIndex))
                                'unselect toutes les pistes
                                Reaper.utility.Main_OnCommand(40297)
                                _ReaperTracksManager.SetSelect(0, 1)
                                c24cs.Select.Led = True
                                Reaper.utility.Main_OnCommand_SafeUI(40913)
                            End If

                        End Sub
                        h.ZoomPreset2H = ZoomPreset2H
                        AddHandler Me.OnBankChangeEnd, ZoomPreset2H

                        Reaper.utility.SetProjectExtState("C24FUNCTIONKEY", "2", "1")
                    Else
                        RemoveHandler Me.OnBankChangeEnd, h.ZoomPreset2H

                        C24_Desk.F2.Led = False
                        Reaper.utility.SetProjectExtState("C24FUNCTIONKEY", "2", "0")
                    End If

                End Sub
        h.F2BtnH = F2BtnH
        AddHandler C24_Desk.F2.OnButtonReleased, F2BtnH



        _FunctionKeysHandlers.Add(h)
        'init
        Dim F1String As String = Reaper.utility.GetProjectExtState("C24FUNCTIONKEY", "1", "1")
        F1BtnH.Invoke(CInt(F1String))

        Dim F2String As String = Reaper.utility.GetProjectExtState("C24FUNCTIONKEY", "2", "1")
        F2BtnH.Invoke(CInt(F2String))

    End Sub
    Private Sub RemoveFunctionKeysHandlers()

        For Each h In _FunctionKeysHandlers
            RemoveHandler C24_Desk.F1.OnButtonReleased, h.F1BtnH
            RemoveHandler C24_Desk.F2.OnButtonReleased, h.F2BtnH

            If _ReaperTracksManager IsNot Nothing Then
                If h.ZoomPreset1H IsNot Nothing Then
                    RemoveHandler Me.OnBankChangeEnd, h.ZoomPreset1H

                End If
                If h.ZoomPreset2H IsNot Nothing Then
                    RemoveHandler Me.OnBankChangeEnd, h.ZoomPreset2H

                End If


            End If
        Next
        _FunctionKeysHandlers.Clear()

    End Sub

#End Region

#Region "JogWheel"

    Private Sub AddJogWheelHandler()
        RemoveJogWheelHandler()

        Dim SnapToGridAL As New C24ActionLinker(C24_Desk.Grid, 1157)
        Dim saved_selecteditems As List(Of ReaperItemRef) = Nothing
        Dim AddSelectedItemsMode As Boolean = False

        Dim h As New JogWheelHandler
        h.SnapToGridAL = SnapToGridAL
        'init

        'Modifier fonctions :
        'rien : déplacement curseur
        'ctl : déplacement curseur + sélection
        'Ctrl : déplacement curseur fin
        'Shift : Zoom X 
        'Shift + Ctl : Zoom Y
        'Opt : déplacement selection tracks
        'Opt + Ctrl : déplacement selection tracks + selection
        'Ctl : déselectionne tous les items

        Dim JogWheelChangeH As Hardware.C24_JogWheel.OnJogWheelChangeEventHandler =
            Sub(direction As Control24.Commands.Direction, velocity As Integer)
                Select Case JogWheelMode
                    'Mode Shuttle : déplacement du curseur + Zoom + selections
                    Case JogWheelModes.Shuttle
                        ''deplacement curseur avec/sans sélection
                        'Dim diff As Double = 0
                        'If SnapToGridAL.State Then
                        '    Dim pos = Reaper.utility.GetCursorPosition
                        '    'Dim next_pos As Double = Reaper.utility.BR_GetNextGridDivision(pos)
                        '    Dim next_pos As Double = Reaper.utility.SnapToGrid(pos)
                        '    diff = next_pos - pos
                        'Else
                        '    Dim ArrangeView2 = Reaper.utility.GetArrangeView2()
                        '    Dim MaxTimeView = Math.Abs(ArrangeView2.EndTime - ArrangeView2.StartTime)
                        '    If Ctrl Then
                        '        diff = MaxTimeView / 1000
                        '    Else
                        '        diff = MaxTimeView / 100
                        '    End If
                        'End If
                        If Shift = False AndAlso Opt = False Then

                            Select Case JogWheelToolMode
                                Case JogWheelToolModes.None
                                    'déplacement du curseur
                                    'Reaper.utility.MoveEditCursor((If(direction = Direction.Left, -1, 1) * velocity * diff), If(Ctl, True, False))
                                    MoveEditCursor(direction, velocity, SnapToGridAL)
                                Case JogWheelToolModes.Grab
                                    'reduction/agrandissement d'un item
                                    If IsCursorPlacedLeft Then
                                        'Reaper.utility.MoveEditCursor((If(direction = Direction.Left, -1, 1) * velocity * diff), False)
                                        MoveEditCursor(direction, velocity, SnapToGridAL, True)
                                        Reaper.utility.Main_OnCommand(40625)

                                    Else
                                        'Reaper.utility.MoveEditCursor((If(direction = Direction.Left, -1, 1) * velocity * diff), False)
                                        MoveEditCursor(direction, velocity, SnapToGridAL, True)
                                        Reaper.utility.Main_OnCommand(40626)

                                    End If
                                    Reaper.utility.Main_OnCommand(41206)
                                Case JogWheelToolModes.Trim
                                    'rognage d'un item
                                    If IsCursorPlacedLeft Then
                                        'Reaper.utility.MoveEditCursor((If(direction = Direction.Left, -1, 1) * velocity * diff), False)
                                        MoveEditCursor(direction, velocity, SnapToGridAL, True)
                                        Reaper.utility.Main_OnCommand(41305)
                                    Else
                                        'Reaper.utility.MoveEditCursor((If(direction = Direction.Left, -1, 1) * velocity * diff), False)
                                        MoveEditCursor(direction, velocity, SnapToGridAL, True)
                                        Reaper.utility.Main_OnCommand(41311)
                                    End If
                                Case JogWheelToolModes.Shuffle

                                    If Ctl Then
                                        'deplacement du contenu interne d'un item
                                        If direction = Direction.Left Then
                                            Reaper.utility.Main_OnCommand(40123)
                                        Else
                                            Reaper.utility.Main_OnCommand(40124)
                                        End If
                                    Else
                                        'deplacement d'un item
                                        If direction = Direction.Left Then
                                            Reaper.utility.Main_OnCommand(40120)
                                            If SnapToGridAL.State Then
                                                Reaper.utility.Main_OnCommand(41182)
                                            End If

                                        Else
                                            Reaper.utility.Main_OnCommand(40119)
                                            If SnapToGridAL.State Then
                                                Reaper.utility.Main_OnCommand(41183)
                                            End If
                                        End If
                                    End If


                                Case JogWheelToolModes.SelAdj
                                    'Modification d'une sélection
                                    If IsCursorPlacedLeft Then
                                        'Reaper.utility.MoveEditCursor((If(direction = Direction.Left, -1, 1) * velocity * diff), False)
                                        MoveEditCursor(direction, velocity, SnapToGridAL, True)
                                        Reaper.utility.Main_OnCommand(40625)
                                    Else
                                        'Reaper.utility.MoveEditCursor((If(direction = Direction.Left, -1, 1) * velocity * diff), False)
                                        MoveEditCursor(direction, velocity, SnapToGridAL, True)
                                        Reaper.utility.Main_OnCommand(40626)
                                    End If

                                Case JogWheelToolModes.PreRool
                                    'Modification de la durée du pre roll

                                    Dim SelectionStartTime = _ReaperProjectMonitor.SelectionStartTime
                                    MoveEditCursor(direction, velocity, SnapToGridAL, True)
                                    Reaper.utility.Main_OnCommand(40222)
                                    Dim CurPos = Reaper.utility.GetCursorPosition
                                    If CurPos > SelectionStartTime Then
                                        Reaper.utility.SetCursorPosition(SelectionStartTime, True)
                                        Reaper.utility.Main_OnCommand(40222)
                                        Reaper.utility.Main_OnCommand(40223)
                                    End If


                                    'Dim delta = (If(direction = Direction.Left, -1, 1) * velocity * diff)
                                    'Dim SelectionStartTime = _ReaperProjectMonitor.SelectionStartTime
                                    'Dim CurPos = Reaper.utility.GetCursorPosition

                                    'If CurPos + delta <= SelectionStartTime Then
                                    '    Reaper.utility.MoveEditCursor(delta, False)
                                    '    Reaper.utility.Main_OnCommand(40222)
                                    'Else
                                    '    Reaper.utility.SetCursorPosition(SelectionStartTime)
                                    '    Reaper.utility.Main_OnCommand(40222)
                                    'End If

                                Case JogWheelToolModes.PostRool
                                    'Modification de la durée du post roll

                                    Dim SelectionEndTime = _ReaperProjectMonitor.SelectionEndTime
                                    MoveEditCursor(direction, velocity, SnapToGridAL, True)
                                    Reaper.utility.Main_OnCommand(40223)
                                    Dim CurPos = Reaper.utility.GetCursorPosition
                                    If CurPos < SelectionEndTime Then
                                        Reaper.utility.SetCursorPosition(SelectionEndTime, True)
                                        Reaper.utility.Main_OnCommand(40222)
                                        Reaper.utility.Main_OnCommand(40223)
                                    End If

                                    'Dim delta = (If(direction = Direction.Left, -1, 1) * velocity * diff)
                                    'Dim SelectionEndTime = _ReaperProjectMonitor.SelectionEndTime
                                    'Dim CurPos = Reaper.utility.GetCursorPosition

                                    'If CurPos + delta >= SelectionEndTime Then
                                    '    Reaper.utility.MoveEditCursor(delta, False)
                                    '    Reaper.utility.Main_OnCommand(40223)
                                    'Else
                                    '    Reaper.utility.SetCursorPosition(SelectionEndTime)
                                    '    Reaper.utility.Main_OnCommand(40223)
                                    'End If
                                Case JogWheelToolModes.Automation

                                    'Reaper.utility.MoveEditCursor((If(direction = Direction.Left, -1, 1) * velocity * diff), If(Ctl, True, False))
                                    MoveEditCursor(direction, velocity, SnapToGridAL)
                                Case JogWheelToolModes.Nudge
                                    If direction = Direction.Left Then
                                        Reaper.utility.Main_OnCommand(40286)

                                    Else
                                        Reaper.utility.Main_OnCommand(40285)

                                    End If

                            End Select



                        ElseIf Shift = True AndAlso Ctl = False AndAlso Ctrl = False AndAlso Opt = False Then
                            'Zoom X in/out (Shift)
                            'Zoom Out
                            If direction = Direction.Left Then
                                Reaper.utility.Main_OnCommand(1011)
                            Else
                                'Zoom In
                                Reaper.utility.Main_OnCommand(1012)
                            End If
                        ElseIf Shift = True AndAlso Ctl = False AndAlso Ctrl = False AndAlso Opt = True Then
                            'Zoom Y in/out (Shift +Opt)
                            'Zoom Out
                            If direction = Direction.Left Then
                                Reaper.utility.Main_OnCommand(40112)
                            Else
                                'Zoom In
                                Reaper.utility.Main_OnCommand(40111)
                            End If
                        ElseIf Shift = False AndAlso Ctl = False AndAlso Ctrl = False AndAlso Opt = True Then
                            If JogWheelToolMode = JogWheelToolModes.Automation Then
                                'Track: Select next prev envelope
                                If direction = Direction.Left Then
                                    Reaper.utility.Main_OnCommand(41863)
                                Else
                                    Reaper.utility.Main_OnCommand(41864)
                                End If
                            Else
                                'Déplacement de la sélection d'une piste (Opt)
                                If direction = Direction.Left Then
                                    Reaper.utility.Main_OnCommand(40286)
                                Else
                                    Reaper.utility.Main_OnCommand(40285)
                                End If
                            End If

                        ElseIf Shift = False AndAlso Ctl = False AndAlso Ctrl = True AndAlso Opt = True Then
                            'Déplacement de la sélection pour sélectionner plusieurs pistes (Opt +Ctrl)
                            If direction = Direction.Left Then
                                Reaper.utility.Main_OnCommand(40288)
                            Else
                                Reaper.utility.Main_OnCommand(40287)
                            End If
                        ElseIf Shift = True AndAlso Ctl = True AndAlso Ctrl = False AndAlso Opt = False Then
                            Select Case JogWheelToolMode
                                Case JogWheelToolModes.None
                                    'déplacement du curseur
                                    'Reaper.utility.MoveEditCursor((If(direction = Direction.Left, -1, 1) * velocity * diff), If(Ctl, True, False))
                                    MoveEditCursor(direction, velocity, SnapToGridAL)
                            End Select




                        End If



                    Case JogWheelModes.Tab

                        'Mode Tab : déplacement dans les zones de saisies en simulatant des Tab sur le clavier
                        If direction = Direction.Left Then
                            _keyboardInjector.PressShiftAndKey(KeyboardKey.Tab)
                        Else

                            _keyboardInjector.PressKey(KeyboardKey.Tab)
                        End If


                End Select

            End Sub
        h.JogWheelChangeH = JogWheelChangeH
        AddHandler C24_Desk.JogWheel.OnJogWheelChange, JogWheelChangeH


        Dim CtlBtnPH As Control24.Hardware.C24_Button.OnButtonPressedEventHandler =
            Sub()
                If IsUnselectIsAllowed() Then
                    If JogWheelMode = JogWheelModes.Shuttle Then
                        If Shift = False AndAlso Opt = False AndAlso Ctrl = False Then
                            'Sauvegarde la sélection

                            LastSavedSelection = Reaper.utility.GetSelection
                            'Déselectionne  la zone de sélection
                            Reaper.utility.Main_OnCommand(40020)
                            'Déselectionne  les points de l'envelope
                            Reaper.utility.Main_OnCommand(40331)
                            'Paramètre le point in et out de la zone de sélection
                            Reaper.utility.Main_OnCommand(40625)
                            Reaper.utility.Main_OnCommand(40626)
                            'Fix Ctl Point
                            CtlPoint = Reaper.utility.GetCursorPosition
                        End If

                        If Shift = True Then
                            If C24_Desk.Tools_Select.Led Then
                                'sauvegarde les items sélectionnées
                                _SelectedItemsManager.Refresh()
                                saved_selecteditems = New List(Of ReaperItemRef)(_SelectedItemsManager.GetCachedSelection)
                                AddSelectedItemsMode = True
                            End If
                        End If

                        If Opt = True Then
                            'déselectionne toutes les pistes
                            Reaper.utility.Main_OnCommand(40297)
                        End If

                    End If
                End If
            End Sub
        h.CtlBtnPH = CtlBtnPH
        AddHandler C24_Desk.Ctl_Clutch.OnButtonPressed, CtlBtnPH

        Dim CtlBtnRH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                If JogWheelMode = JogWheelModes.Shuttle AndAlso C24_Desk.Tools_Select.Led Then
                    If Shift = True Then
                        'Ajoute les nouveaux items à ceux qui sont sauvegardés (inverse la sélection si déjà présente)

                        'selectionne tous les items des pistes selectionnées dans la selection
                        Reaper.utility.Main_OnCommand(40718)

                        If saved_selecteditems IsNot Nothing AndAlso saved_selecteditems.Count > 0 Then
                            Dim _DeselectItems As New List(Of ReaperItemRef)

                            For Each item In _SelectedItemsManager.GetSelectedItems
                                If saved_selecteditems.Contains(item) Then
                                    saved_selecteditems.Remove(item)
                                    _DeselectItems.Add(item)
                                End If
                            Next
                            If saved_selecteditems.Count > 0 Then
                                _SelectedItemsManager.SelectItems(saved_selecteditems)
                            End If
                            If _DeselectItems.Count > 0 Then
                                _SelectedItemsManager.DeselectItems(_DeselectItems)
                            End If
                        End If


                        If Ctrl Then
                            'Time selection: Set time selection to items
                            Reaper.utility.Main_OnCommand(40290)

                        End If

                    Else
                        'selectionne tous les items des pistes selectionnées dans la selection
                        Reaper.utility.Main_OnCommand(40718)
                        If Ctrl Then
                            'Time selection: Set time selection to items
                            Reaper.utility.Main_OnCommand(40290)

                        End If

                        'déplacement du curseur avec selection des points d'envelope
                        If JogWheelToolMode = JogWheelToolModes.Automation Then
                            Reaper.utility.Main_OnCommand(40330)
                        End If
                    End If
                    saved_selecteditems = Nothing
                    AddSelectedItemsMode = False
                End If

            End Sub
        h.CtlBtnRH = CtlBtnRH
        AddHandler C24_Desk.Ctl_Clutch.OnButtonReleased, CtlBtnRH



        _JogWheelHandlers.Add(h)


    End Sub
    Private Sub RemoveJogWheelHandler()
        For Each h In _JogWheelHandlers
            RemoveHandler C24_Desk.JogWheel.OnJogWheelChange, h.JogWheelChangeH
            RemoveHandler C24_Desk.Ctl_Clutch.OnButtonReleased, h.CtlBtnRH
            RemoveHandler C24_Desk.Ctl_Clutch.OnButtonPressed, h.CtlBtnPH

            If h.SnapToGridAL IsNot Nothing Then
                h.SnapToGridAL.Dispose()
            End If
        Next
        _JogWheelHandlers.Clear()
    End Sub
    Enum JogWheelModes
        Shuttle
        Tab

    End Enum
    Enum JogWheelToolModes
        Grab
        Shuffle
        Trim
        SelAdj
        None
        PreRool
        PostRool
        Automation
        Nudge
    End Enum
    Function IsUnselectIsAllowed() As Boolean
        If JogWheelToolMode = JogWheelToolModes.PreRool Then
            Return False
        End If
        If JogWheelToolMode = JogWheelToolModes.PostRool Then
            Return False
        End If
        Return True
    End Function
    Sub MoveEditCursor(direction As Control24.Commands.Direction, velocity As Integer, SnapToGridAL As C24ActionLinker, Optional SelectionNotAllowed As Boolean = False)

        Reaper.utility.Main_OnCommand(GetMoveCursorCommandID(direction, velocity, SnapToGridAL))
        Dim CurPos = Reaper.utility.GetCursorPosition
        If SelectionNotAllowed = False AndAlso Ctl Then
            If CurPos <= CtlPoint Then
                If LastCurPos >= CtlPoint Then
                    Reaper.utility.SetCursorPosition(CtlPoint)
                    Reaper.utility.Main_OnCommand(40626)
                    Reaper.utility.SetCursorPosition(CurPos)
                    Reaper.utility.Main_OnCommand(40625)
                Else
                    Reaper.utility.Main_OnCommand(40625)
                End If

            End If
            If CurPos >= CtlPoint Then
                If LastCurPos <= CtlPoint Then
                    Reaper.utility.SetCursorPosition(CtlPoint)
                    Reaper.utility.Main_OnCommand(40625)
                    Reaper.utility.SetCursorPosition(CurPos)
                    Reaper.utility.Main_OnCommand(40626)
                Else
                    Reaper.utility.Main_OnCommand(40626)
                End If

            End If

        End If
            LastCurPos = CurPos
    End Sub
    Function GetMoveCursorCommandID(direction As Control24.Commands.Direction, velocity As Integer, SnapToGridAL As C24ActionLinker) As Integer
        If direction = Direction.Left Then
            If SnapToGridAL.State Then
                Return 40646
            Else
                If velocity > 2 Then
                    Return 41666
                Else
                    Return 40104
                End If
            End If

        Else
            If SnapToGridAL.State Then
                Return 40647
            Else
                If velocity > 2 Then
                    Return 41667
                Else
                    Return 40105
                End If
            End If
        End If

    End Function
#End Region

#Region "Reaper Action"
    Public Class C24ActionLinker
        Implements IDisposable

        Private _C24Btn As C24_LedButton = Nothing
        Private _ReaperAction As Integer
        Private _C24BtnH As [Delegate] = Nothing
        Private _ReaperActionH As [Delegate] = Nothing
        Private disposedValue As Boolean
        Private _State As Boolean
        Private _SafeUI As Boolean = False
        Private _ModifiersBtnReaperAction As New Dictionary(Of C24_Button, Integer)
        Private _LedBlinking As Boolean = False
        Private _ModifiersLedBlinking As New Dictionary(Of C24_Button, Boolean)
        Public Event On_StateChange(State As Boolean)
        Private _OverrideModifierBtn As C24_Button = Nothing
        Dim C24BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler = Nothing
        Private _Inverse_Led As Boolean = False
        Private _ReaperActionToUse As Integer = -1
        Private _Inverse_LedToUse As Boolean = False
        Private _LedBlinkingToUse As Boolean = False
        Private _ModifiersInverse_Led As New Dictionary(Of C24_Button, Boolean)
        Private _ModifiersMode As Boolean = False

        Private _ModifiersModeTimer As System.Timers.Timer = Nothing
        Private _ModifiersModeTimerH As System.Timers.ElapsedEventHandler = Nothing
        Private _LastState As Boolean = False
        Private _MuteReaperHookCommand As Boolean = False
        Private _Use_Led As Boolean
        Private _ModifiersUse_Led As New Dictionary(Of C24_Button, Boolean)
        Private _ShouldUseLed As Boolean = True
        Private _ModifiersModeTimeoutMs As Integer = 5000
        Private _ExitModifierModeOnTimer As Boolean = False

        Property ExitModifierModeOnTimer As Boolean
            Get
                Return _ExitModifierModeOnTimer
            End Get
            Set
                _ExitModifierModeOnTimer = Value
            End Set
        End Property

        Property ModifiersModeTimeoutMs As Integer
            Get
                Return _ModifiersModeTimeoutMs
            End Get
            Set
                _ModifiersModeTimeoutMs = Value
            End Set
        End Property

        Property LedBlinking As Boolean
            Get
                Return _LedBlinking
            End Get
            Set
                _LedBlinking = Value
            End Set
        End Property
        Property ModifiersLedBlinking As Dictionary(Of C24_Button, Boolean)
            Get
                Return _ModifiersLedBlinking
            End Get
            Set
                _ModifiersLedBlinking = Value
            End Set
        End Property
        Property ModifiersInverse_Led As Dictionary(Of C24_Button, Boolean)
            Get
                Return _ModifiersInverse_Led
            End Get
            Set
                _ModifiersInverse_Led = Value
            End Set
        End Property
        Property ModifiersUse_Led As Dictionary(Of C24_Button, Boolean)
            Get
                Return _ModifiersUse_Led
            End Get
            Set
                _ModifiersUse_Led = Value
            End Set
        End Property

        Property Inverse_Led As Boolean
            Get
                Return _Inverse_Led
            End Get
            Set
                _Inverse_Led = Value
            End Set
        End Property
        Property MuteReaperHookCommand As Boolean
            Get
                Return _MuteReaperHookCommand
            End Get
            Set
                _MuteReaperHookCommand = Value
            End Set
        End Property
        Property Use_Led As Boolean
            Get
                Return _Use_Led
            End Get
            Set
                _Use_Led = Value
            End Set
        End Property

        Sub New(C24Btn As Control24.Hardware.C24_LedButton, ReaperAction As Integer, Optional Use_Led As Boolean = True, Optional LedBlinking As Boolean = False, Optional Inverse_Led As Boolean = False, Optional SafeUI As Boolean = False)

            _ModifiersModeTimer = New System.Timers.Timer(ModifiersModeTimeoutMs)
            _ModifiersModeTimer.AutoReset = False

            _C24Btn = C24Btn
            _ReaperAction = ReaperAction
            _LedBlinking = LedBlinking
            _Inverse_Led = Inverse_Led
            _SafeUI = SafeUI
            _ShouldUseLed = Use_Led

            'ajout du handler du C24btn
            'init
            _ReaperActionToUse = ReaperAction
            _LedBlinkingToUse = LedBlinking
            _Inverse_LedToUse = Inverse_Led
            _State = Reaper.utility.GetToggleCommandStateBool(ReaperAction)
            If _LedBlinkingToUse Then
                If _Inverse_LedToUse Then
                    C24Btn.IsBlinking = Not _State
                Else
                    C24Btn.IsBlinking = _State
                End If

            Else
                If _Inverse_LedToUse Then
                    C24Btn.Led = Not _State
                Else
                    C24Btn.Led = _State
                End If

            End If

            C24BtnH =
                Sub(TrackNb As Integer?)
                    _ReaperActionToUse = ReaperAction
                    _LedBlinkingToUse = LedBlinking
                    _Inverse_LedToUse = Inverse_Led
                    _ShouldUseLed = Use_Led

                    If _ModifiersMode Then
                        _State = Reaper.utility.GetToggleCommandStateBool(_ReaperActionToUse)
                        C24Btn.IsBlinking = False
                        If _ShouldUseLed Then
                            If _LedBlinkingToUse Then
                                If _Inverse_LedToUse Then
                                    C24Btn.IsBlinking = Not _State
                                Else
                                    C24Btn.IsBlinking = _State
                                End If

                            Else
                                If _Inverse_LedToUse Then
                                    C24Btn.Led = Not _State
                                Else
                                    C24Btn.Led = _State
                                End If

                            End If

                        End If
                        _ModifiersModeTimer.Stop()
                        _ModifiersMode = False
                        Exit Sub
                    Else
                        _State = Not Reaper.utility.GetToggleCommandStateBool(_ReaperActionToUse)
                    End If


                    If _ModifiersBtnReaperAction.Keys.Count > 0 Then
                        For Each C24Modifier In _ModifiersBtnReaperAction.Keys
                            If C24Modifier.IsPressed OrElse _OverrideModifierBtn IsNot Nothing Then
                                _ModifiersMode = True
                                If ExitModifierModeOnTimer Then
                                    _ModifiersModeTimer.Start()
                                End If

                                If _OverrideModifierBtn IsNot Nothing Then
                                    _ReaperActionToUse = _ModifiersBtnReaperAction(_OverrideModifierBtn)
                                    If _ModifiersLedBlinking.ContainsKey(_OverrideModifierBtn) Then
                                        _LedBlinkingToUse = _ModifiersLedBlinking(_OverrideModifierBtn)
                                    End If
                                    If _ModifiersInverse_Led.ContainsKey(_OverrideModifierBtn) Then
                                        _Inverse_LedToUse = _ModifiersInverse_Led(_OverrideModifierBtn)
                                    End If
                                    If _ModifiersUse_Led.ContainsKey(_OverrideModifierBtn) Then
                                        _ShouldUseLed = _ModifiersUse_Led(_OverrideModifierBtn)
                                    End If
                                Else
                                    _ReaperActionToUse = _ModifiersBtnReaperAction(C24Modifier)
                                    If _ModifiersLedBlinking.ContainsKey(C24Modifier) Then
                                        _LedBlinkingToUse = _ModifiersLedBlinking(C24Modifier)
                                    End If
                                    If _ModifiersInverse_Led.ContainsKey(C24Modifier) Then
                                        _Inverse_LedToUse = _ModifiersInverse_Led(C24Modifier)
                                    End If
                                    If _ModifiersUse_Led.ContainsKey(C24Modifier) Then
                                        _ShouldUseLed = _ModifiersUse_Led(C24Modifier)
                                    End If
                                End If



                            End If
                        Next

                    End If


                    If SafeUI Then
                        Reaper.utility.Main_OnCommand_SafeUI(_ReaperActionToUse)
                    Else
                        Reaper.utility.Main_OnCommand(_ReaperActionToUse)
                    End If


                    If _ShouldUseLed Then
                        If _LedBlinkingToUse Then
                            If _Inverse_LedToUse Then
                                C24Btn.IsBlinking = Not _State
                            Else
                                C24Btn.IsBlinking = _State
                            End If

                        Else
                            If _Inverse_LedToUse Then
                                C24Btn.Led = Not _State
                            Else
                                C24Btn.Led = _State
                            End If

                        End If
                    Else
                        C24Btn.IsBlinking = False
                    End If



                    If _State <> _LastState Then
                        RaiseEvent On_StateChange(_State)
                        _LastState = _State
                    End If


                End Sub
            _C24BtnH = C24BtnH
            AddHandler C24Btn.OnButtonReleased, C24BtnH

            'ajout du handler Reaper
            Dim ReaperActionH As ReaperEventPipeMonitor.On_HookPostCommand2EventHandler =
                Sub(Command As Integer, val As Integer, valHw As Integer, relmode As Integer)
                    If Command = _ReaperActionToUse AndAlso MuteReaperHookCommand = False Then
                        _State = Reaper.utility.GetToggleCommandStateBool(_ReaperActionToUse)

                        If _ShouldUseLed Then
                            If _LedBlinkingToUse Then
                                If _Inverse_LedToUse Then
                                    C24Btn.IsBlinking = Not _State
                                Else
                                    C24Btn.IsBlinking = _State
                                End If

                            Else
                                If _Inverse_LedToUse Then
                                    C24Btn.Led = Not _State
                                Else
                                    C24Btn.Led = _State
                                End If

                            End If
                        Else
                            C24Btn.IsBlinking = False
                        End If

                        If _State <> _LastState Then
                            RaiseEvent On_StateChange(_State)
                            _LastState = _State
                        End If
                    End If

                End Sub
            _ReaperActionH = ReaperActionH
            AddHandler ReaperEventPipeMonitor.On_HookPostCommand2, ReaperActionH

            _ModifiersModeTimerH =
                    Sub(sender As Object, e As System.Timers.ElapsedEventArgs)
                        If _ModifiersMode Then
                            _ModifiersModeTimer.Stop()
                            C24BtnH.Invoke(Nothing)
                        End If
                    End Sub
            AddHandler _ModifiersModeTimer.Elapsed, _ModifiersModeTimerH

        End Sub
        Sub New(ReaperAction As Integer)

            _ReaperAction = ReaperAction


            'init
            _State = Reaper.utility.GetToggleCommandStateBool(ReaperAction)

            'ajout du handler Reaper
            Dim ReaperActionH As ReaperEventPipeMonitor.On_HookPostCommand2EventHandler =
                Sub(Command As Integer, val As Integer, valHw As Integer, relmode As Integer)
                    If Command = ReaperAction Then
                        _State = Not _State
                        RaiseEvent On_StateChange(_State)
                    End If
                End Sub
            _ReaperActionH = ReaperActionH
            AddHandler ReaperEventPipeMonitor.On_HookPostCommand2, ReaperActionH



        End Sub
        Public Property State As Boolean
            Get
                Return _State
            End Get
            Set
                If Value <> _State Then
                    _State = Value
                    If SafeUI Then
                        Reaper.utility.Main_OnCommand_SafeUI(ReaperAction)
                    Else
                        Reaper.utility.Main_OnCommand(ReaperAction)
                    End If
                    If _C24Btn IsNot Nothing Then
                        If _ShouldUseLed Then
                            If _LedBlinkingToUse Then
                                If _Inverse_LedToUse Then
                                    C24Btn.IsBlinking = Not _State
                                Else
                                    C24Btn.IsBlinking = _State
                                End If

                            Else
                                If _Inverse_LedToUse Then
                                    C24Btn.Led = Not _State
                                Else
                                    C24Btn.Led = _State
                                End If

                            End If
                        Else
                            C24Btn.IsBlinking = False
                        End If
                    End If

                    RaiseEvent On_StateChange(_State)
                End If


            End Set
        End Property
        Public Property SafeUI As Boolean
            Get
                Return _SafeUI
            End Get
            Set
                _SafeUI = Value
            End Set
        End Property
        Public Property ModifiersBtnReaperAction As Dictionary(Of C24_Button, Integer)
            Get
                Return _ModifiersBtnReaperAction
            End Get
            Set
                _ModifiersBtnReaperAction = Value
            End Set
        End Property
        Private Function GetBtnStatus(C24Btn As C24_LedButton) As Boolean
            If C24Btn.IsBlinking Then
                Return True
            End If
            If C24Btn.Led Then
                Return True
            End If
            Return False
        End Function
        Public Sub ManualOverrideBtn(Optional OverrideModifierBtn As C24_Button = Nothing)
            _OverrideModifierBtn = OverrideModifierBtn
            C24BtnH.Invoke(Nothing)
        End Sub

        ReadOnly Property C24Btn As Control24.Hardware.C24_LedButton
            Get
                Return _C24Btn
            End Get
        End Property
        ReadOnly Property ReaperAction As Integer
            Get
                Return _ReaperAction
            End Get
        End Property

        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not disposedValue Then
                If disposing Then
                    ' TODO: supprimer l'état managé (objets managés)
                    If _C24Btn IsNot Nothing Then
                        RemoveHandler C24Btn.OnButtonReleased, _C24BtnH
                    End If
                    If _ReaperActionH IsNot Nothing Then
                        RemoveHandler ReaperEventPipeMonitor.On_HookPostCommand2, _ReaperActionH
                    End If
                    If _ModifiersModeTimer IsNot Nothing Then
                        _ModifiersModeTimer.Stop()
                        RemoveHandler _ModifiersModeTimer.Elapsed, _ModifiersModeTimerH
                        _ModifiersModeTimer.Dispose()
                        _ModifiersModeTimer = Nothing
                    End If
                End If

                ' TODO: libérer les ressources non managées (objets non managés) et substituer le finaliseur
                ' TODO: affecter aux grands champs une valeur null
                disposedValue = True
            End If
        End Sub

        ' ' TODO: substituer le finaliseur uniquement si 'Dispose(disposing As Boolean)' a du code pour libérer les ressources non managées
        ' Protected Overrides Sub Finalize()
        '     ' Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(disposing As Boolean)'
        '     Dispose(disposing:=False)
        '     MyBase.Finalize()
        ' End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            ' Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(disposing As Boolean)'
            Dispose(disposing:=True)
            GC.SuppressFinalize(Me)
        End Sub
    End Class
    Sub RemoveC24ActionLinkers()
        For Each _C24ActionLinker In C24ActionLinkers
            _C24ActionLinker.Dispose()
        Next
    End Sub
#End Region

#Region "NumPad"
    'comportement comme un pavé numérique + ajout/suppr/modif/select de Marker et de Regions
    Private Sub AddNumPadHandler()
        RemoveNumPadHandler()


        Dim ActualOperator As NumPadOperators = NumPadOperators.none
        Dim MarkerRegionMode As MarkerRegionModes = MarkerRegionModes.Select
        Dim Copied_Value As Double? = Nothing
        Dim NumPad_String As String = String.Empty
        Dim Calculed_Value As Double? = Nothing

        'Timer pour quitter automatiquement le mode Numpad (1 min)
        _NumPadTimer = New Timers.Timer(60000) With {.AutoReset = False}

        'ajout des handlers opérateurs
        Dim h As New NumPadHandler

        Dim EscBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                C24_Desk.Esc_Cancel_2.IsBlinking = False
                ActualOperator = NumPadOperators.none
                MarkerRegionMode = MarkerRegionModes.Select
                Calculed_Value = Nothing
                Copied_Value = Nothing
                NumPad_String = String.Empty
                _NumPadOutput.OutputTxt = String.Empty
                NumPadMode = NumPadModes.Input
                _NumPadTimer.Stop()
                RemoveHandler C24_Desk.Esc_Cancel_2.OnButtonReleased, EscBtnH
            End Sub


        Dim TimerElapsedH As Timers.ElapsedEventHandler =
            Sub()
                C24DisplayTempMessage("Too Late", "Exit Numpad Mode")
                EscBtnH.Invoke(Nothing)
            End Sub
        h.TimerElapsedH = TimerElapsedH
        AddHandler _NumPadTimer.Elapsed, TimerElapsedH

        Dim switch As Boolean = False

        Dim Num_EnterBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.Operator
                        If NumPad_String IsNot String.Empty Then
                            Dim NumPad_Value As Double = 0
                            If Double.TryParse(NumPad_String, NumPad_Value) Then
                                If Copied_Value IsNot Nothing Then
                                    Select Case ActualOperator
                                        Case NumPadOperators.add
                                            Calculed_Value = Copied_Value.Value + NumPad_Value
                                        Case NumPadOperators.sous
                                            Calculed_Value = Copied_Value.Value - NumPad_Value
                                        Case NumPadOperators.div
                                            Calculed_Value = Copied_Value.Value / NumPad_Value
                                        Case NumPadOperators.mult
                                            Calculed_Value = Copied_Value.Value * NumPad_Value
                                        Case NumPadOperators.none
                                            Calculed_Value = Nothing
                                    End Select
                                    If Calculed_Value IsNot Nothing Then
                                        _clipboardService.SetText(Calculed_Value.Value.ToString)
                                        _keyboardInjector.PressCtrlAndKey(KeyboardKey.V)
                                        _keyboardInjector.PressKey(KeyboardKey.Tab)
                                        _keyboardInjector.PressShiftAndKey(KeyboardKey.Tab)

                                        C24DisplayTempMessage("Calcul:", "Result: " & Math.Round(Calculed_Value.Value, 3).ToString)
                                    End If
                                End If
                            End If
                        End If
                        EscBtnH.Invoke(Nothing)
                    Case NumPadModes.NumPad
                        If JogWheelMode = JogWheelModes.Tab Then

                            If Shift Then
                                'inverse la selection d'une checkbox
                                switch = Not switch
                                If switch Then
                                    _keyboardInjector.PressKey(KeyboardKey.Substract)
                                    _keyboardInjector.PressKey(KeyboardKey.Equal)
                                Else
                                    _keyboardInjector.PressKey(KeyboardKey.Equal)
                                    _keyboardInjector.PressKey(KeyboardKey.Substract)
                                End If
                            Else
                                _keyboardInjector.PressKey(KeyboardKey.Enter)
                            End If



                        End If

                    Case NumPadModes.Marker
                        Select Case MarkerRegionMode


                            Case MarkerRegionModes.Select
                                'transporte le curseur au marker
                                ReBuildMarkersRegions()
                                Dim MarkerNb As Integer = 0
                                If Integer.TryParse(NumPad_String, MarkerNb) Then
                                    If Markers.ContainsKey(MarkerNb) Then
                                        Dim _marker = Markers(MarkerNb)
                                        Reaper.utility.SetCursorPosition(_marker.Start_Pos, True, True)
                                    Else
                                        C24DisplayTempMessage("Marker", MarkerNb & " is not an existing marker Nb ")
                                    End If
                                Else
                                    C24DisplayTempMessage("Marker", MarkerNb & " is not a valid marker ")
                                End If
                                EscBtnH.Invoke(Nothing)
                            Case MarkerRegionModes.Set
                                'set un marker à la posititon du curseur
                                Dim pos = Reaper.utility.GetCursorPosition
                                Dim MarkerNb As Integer = 0
                                If Integer.TryParse(NumPad_String, MarkerNb) Then
                                    If Markers.ContainsKey(MarkerNb) Then
                                        If Reaper.utility.SetProjectMarker(MarkerNb, False, pos, pos, String.Empty) Then
                                            ReBuildMarkersRegions()
                                            C24DisplayTempMessage("Marker", "Marker " & MarkerNb & " is setted")
                                        Else
                                            C24DisplayTempMessage("Marker", "Can't set marker " & MarkerNb)
                                        End If

                                    Else
                                        C24DisplayTempMessage("Marker", MarkerNb & " is not an existing marker Nb ")
                                    End If
                                Else
                                    C24DisplayTempMessage("Marker", MarkerNb & " is not a valid marker ")
                                End If

                                EscBtnH.Invoke(Nothing)
                            Case MarkerRegionModes.Add
                                'ajoute un marker à la position
                                Dim pos = Reaper.utility.GetCursorPosition
                                Dim newMarkerNb = Reaper.utility.AddProjectMarker(False, pos, pos, String.Empty)
                                'affiche
                                If newMarkerNb <> -1 Then
                                    ReBuildMarkersRegions()
                                    C24DisplayTempMessage("Marker", "Added marker : " & newMarkerNb)
                                Else
                                    C24DisplayTempMessage("Marker", "Can't creat marker at this place!")
                                End If
                                EscBtnH.Invoke(Nothing)

                            Case MarkerRegionModes.Remove
                                'Supprime un marker par son numero
                                Dim MarkerNb As Integer = 0
                                If Integer.TryParse(NumPad_String, MarkerNb) Then
                                    ReBuildMarkersRegions()
                                    If Markers.ContainsKey(MarkerNb) Then
                                        If Reaper.utility.DeleteProjectMarker(MarkerNb, False) Then
                                            Markers.Remove(MarkerNb)
                                            C24DisplayTempMessage("Marker", "Remove marker : " & MarkerNb)
                                        Else
                                            C24DisplayTempMessage("Marker", "Can't remove marker " & MarkerNb)
                                        End If
                                    Else
                                        C24DisplayTempMessage("Marker", MarkerNb & " is not an existing marker Nb ")
                                    End If
                                Else
                                    C24DisplayTempMessage("Marker", MarkerNb & " is not a valid marker ")
                                End If
                                EscBtnH.Invoke(Nothing)

                        End Select

                    Case NumPadModes.Region


                        Select Case MarkerRegionMode

                            Case MarkerRegionModes.Select
                                'transporte le curseur au début de la région
                                ReBuildMarkersRegions()
                                Dim RegionNb As Integer = 0
                                If Integer.TryParse(NumPad_String, RegionNb) Then
                                    If Regions.ContainsKey(RegionNb) Then
                                        Dim _region = Regions(RegionNb)
                                        If Shift Then
                                            Reaper.utility.SetCursorPosition(_region.End_pos, True, True)
                                        Else
                                            Reaper.utility.SetCursorPosition(_region.Start_Pos, True, True)
                                        End If
                                    Else
                                        C24DisplayTempMessage("Region", RegionNb & " is not an existing region Nb ")
                                    End If
                                Else
                                    C24DisplayTempMessage("Region", RegionNb & " is not a valid region ")
                                End If
                                EscBtnH.Invoke(Nothing)
                            Case MarkerRegionModes.Set
                                'set une region à la posititon de la selection

                                Dim RegionNb As Integer = 0
                                If Integer.TryParse(NumPad_String, RegionNb) Then
                                    If Regions.ContainsKey(RegionNb) Then
                                        If Reaper.utility.SetProjectMarker(RegionNb, True, _ReaperProjectMonitor.SelectionStartTime, _ReaperProjectMonitor.SelectionEndTime, String.Empty) Then
                                            ReBuildMarkersRegions()
                                            C24DisplayTempMessage("Region", "Region " & RegionNb & " is setted")
                                        Else
                                            C24DisplayTempMessage("Region", "Can't set region " & RegionNb)
                                        End If
                                    Else
                                        C24DisplayTempMessage("Region", RegionNb & " is not an existing region Nb ")
                                    End If
                                Else
                                    C24DisplayTempMessage("Region", RegionNb & " is not a valid region ")
                                End If


                                EscBtnH.Invoke(Nothing)

                            Case MarkerRegionModes.Add
                                Dim NewRegionNb = Reaper.utility.AddProjectMarker(True, _ReaperProjectMonitor.SelectionStartTime, _ReaperProjectMonitor.SelectionEndTime, String.Empty)
                                If NewRegionNb <> -1 Then
                                    ReBuildMarkersRegions()
                                    C24DisplayTempMessage("Region", "New region added : " & NewRegionNb)
                                Else
                                    C24DisplayTempMessage("Region", "Can't creat region at this place!")
                                End If
                                EscBtnH.Invoke(Nothing)

                            Case MarkerRegionModes.Remove

                                'Supprime un region par son numero
                                Dim RegionNb As Integer = 0
                                If Integer.TryParse(NumPad_String, RegionNb) Then
                                    If Regions.ContainsKey(RegionNb) Then
                                        If Reaper.utility.DeleteProjectMarker(RegionNb, True) Then
                                            Regions.Remove(RegionNb)
                                            C24DisplayTempMessage("Region", "Remove region : " & RegionNb)
                                        Else
                                            C24DisplayTempMessage("Region", "Can't remove region " & RegionNb)
                                        End If

                                    End If
                                End If
                                EscBtnH.Invoke(Nothing)
                        End Select

                End Select
            End Sub
        h.Num_EnterBtnH = Num_EnterBtnH
        AddHandler C24_Desk.Num_Enter.OnButtonReleased, Num_EnterBtnH

        Dim Num_CLearBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
           Sub()
               Select Case NumPadMode
                   Case NumPadModes.NumPad
                       _keyboardInjector.PressKey(KeyboardKey.BackSpace)
                   Case Else
                       NumPad_String = String.Empty
                       _NumPadOutput.OutputTxt = String.Empty
               End Select


           End Sub
        h.Num_CLearBtnH = Num_CLearBtnH
        AddHandler C24_Desk.Num_Clr.OnButtonReleased, Num_CLearBtnH


        Dim Num_EgalBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        'passage en mode operation
                        If C24_Desk.Esc_Cancel_2.IsBlinking = False Then
                            _keyboardInjector.PressCtrlAndKey(KeyboardKey.C)
                            Dim txt = _clipboardService.GetText()
                            If txt.Contains("+") Then
                                txt = txt.Replace("+", "")
                                _clipboardService.SetText(txt)
                                _keyboardInjector.PressCtrlAndKey(KeyboardKey.V)
                            End If
                            Dim value As Double = 0
                            If Double.TryParse(txt, value) Then
                                Copied_Value = value
                                _NumPadTimer.Start()
                                NumPadMode = NumPadModes.Operator
                                'Ajout Esc 
                                If C24_Desk.Esc_Cancel_2.IsBlinking = False Then
                                    C24_Desk.Esc_Cancel_2.IsBlinking = True
                                    AddHandler C24_Desk.Esc_Cancel_2.OnButtonReleased, EscBtnH
                                End If
                                'ajout message
                                _NumPadOutput.OutputTxt = String.Empty
                                C24DialogMessage("Calcul:", "Choose Operator (+ - * /) for " & Math.Round(Copied_Value.Value, 3), False, _NumPadTimer)
                            End If
                        Else
                            EscBtnH.Invoke(Nothing)
                        End If
                    Case NumPadModes.Operator
                        'equivalent Enter
                        Num_EnterBtnH.Invoke(Nothing)
                    Case NumPadModes.Marker
                        If MarkerRegionMode = MarkerRegionModes.Set Then
                            EscBtnH.Invoke(Nothing)
                        Else
                            MarkerRegionMode = MarkerRegionModes.Set
                            'affiche
                            _NumPadOutput.OutputTxt = String.Empty
                            Dim NewResumeScreensBtn As New List(Of C24_Button)
                            NewResumeScreensBtn.AddRange(DefautResumeScreensBtn)
                            NewResumeScreensBtn.Add(C24_Desk.Num_Egal)
                            C24DialogMessage("Marker", "Set at this place marker Nb : ", True, _NumPadTimer, NewResumeScreensBtn)
                        End If

                    Case NumPadModes.Region
                        If MarkerRegionMode = MarkerRegionModes.Set Then
                            EscBtnH.Invoke(Nothing)
                        Else
                            If _ReaperProjectMonitor.SelectionActive Then
                                MarkerRegionMode = MarkerRegionModes.Set
                                'affiche
                                _NumPadOutput.OutputTxt = String.Empty
                                Dim NewResumeScreensBtn As New List(Of C24_Button)
                                NewResumeScreensBtn.AddRange(DefautResumeScreensBtn)
                                NewResumeScreensBtn.Add(C24_Desk.Num_Egal)
                                C24DialogMessage("Region", "Set at this place region Nb : ", True, _NumPadTimer, NewResumeScreensBtn)
                            Else
                                C24DisplayTempMessage("Region", "No selection time available!")
                                EscBtnH.Invoke(Nothing)
                            End If

                        End If

                End Select


            End Sub
        h.Num_EgalBtnH = Num_EgalBtnH
        AddHandler C24_Desk.Num_Egal.OnButtonReleased, Num_EgalBtnH

        Dim Num_AddBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()

                Select Case NumPadMode
                    Case NumPadModes.Operator
                        ActualOperator = NumPadOperators.add
                        'affiche
                        _NumPadOutput.OutputTxt = String.Empty
                        C24DialogMessage("Calcul:", Math.Round(Copied_Value.Value, 3).ToString & " + ", True, _NumPadTimer)
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.Add)
                    Case NumPadModes.Marker
                        If MarkerRegionMode = MarkerRegionModes.Add Then
                            EscBtnH.Invoke(Nothing)
                        Else
                            MarkerRegionMode = MarkerRegionModes.Add
                            Dim NewResumeScreensBtn As New List(Of C24_Button)
                            NewResumeScreensBtn.AddRange(DefautResumeScreensBtn)
                            NewResumeScreensBtn.Add(C24_Desk.Num_Add)
                            C24DialogMessage("Marker", "Press Enter to add new marker.",, _NumPadTimer, NewResumeScreensBtn)
                        End If


                    Case NumPadModes.Region
                        If MarkerRegionMode = MarkerRegionModes.Add Then
                            EscBtnH.Invoke(Nothing)
                        Else
                            'ajoute une region à la selection
                            If _ReaperProjectMonitor.SelectionActive Then
                                MarkerRegionMode = MarkerRegionModes.Add
                                Dim NewResumeScreensBtn As New List(Of C24_Button)
                                NewResumeScreensBtn.AddRange(DefautResumeScreensBtn)
                                NewResumeScreensBtn.Add(C24_Desk.Num_Add)
                                C24DialogMessage("Region", "Press Enter to add new region.",, _NumPadTimer, NewResumeScreensBtn)
                            Else
                                C24DisplayTempMessage("Region", "No Selection available for new region")
                                EscBtnH.Invoke(Nothing)
                            End If
                        End If
                End Select

            End Sub
        h.Num_AddBtnH = Num_AddBtnH
        AddHandler C24_Desk.Num_Add.OnButtonReleased, Num_AddBtnH

        Dim Num_SousBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.Operator
                        ActualOperator = NumPadOperators.sous
                        'affiche
                        _NumPadOutput.OutputTxt = String.Empty
                        C24DialogMessage("Calcul:", Math.Round(Copied_Value.Value, 3).ToString & " - ", True, _NumPadTimer)
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.Substract)

                    Case NumPadModes.Marker
                        MarkerRegionMode = MarkerRegionModes.Remove
                        C24DialogMessage("Marker", "Remove marker nb : ", True, _NumPadTimer)



                    Case NumPadModes.Region
                        MarkerRegionMode = MarkerRegionModes.Remove
                        C24DialogMessage("Region", "Remove region nb : ", True, _NumPadTimer)






                End Select

            End Sub
        h.Num_SousBtnH = Num_SousBtnH
        AddHandler C24_Desk.Num_Sous.OnButtonReleased, Num_SousBtnH

        Dim Num_DivBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                _NumPadTimer.Start()
                Select Case NumPadMode
                    Case NumPadModes.Operator
                        ActualOperator = NumPadOperators.div
                        'affiche
                        _NumPadOutput.OutputTxt = String.Empty
                        C24DialogMessage("Calcul:", Math.Round(Copied_Value.Value, 3).ToString & " / ", True, _NumPadTimer)
                    Case NumPadModes.Input
                        'Ajout Esc 
                        If C24_Desk.Esc_Cancel_2.IsBlinking = False Then
                            C24_Desk.Esc_Cancel_2.IsBlinking = True
                            AddHandler C24_Desk.Esc_Cancel_2.OnButtonReleased, EscBtnH
                        End If
                        NumPadMode = NumPadModes.Marker
                        MarkerRegionMode = MarkerRegionModes.Select
                        'affiche
                        _NumPadOutput.OutputTxt = String.Empty
                        Dim DivResumeScreensBtn As New List(Of C24_Button)
                        DivResumeScreensBtn.AddRange(DefautResumeScreensBtn)
                        DivResumeScreensBtn.Add(C24_Desk.Num_Div)
                        C24DialogMessage("Marker:", "Select as Marker Nb : ", True, _NumPadTimer, DivResumeScreensBtn)
                    Case NumPadModes.Marker
                        EscBtnH.Invoke(Nothing)
                End Select

            End Sub
        h.Num_DivBtnH = Num_DivBtnH
        AddHandler C24_Desk.Num_Div.OnButtonReleased, Num_DivBtnH

        Dim Num_MultBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                _NumPadTimer.Start()
                Select Case NumPadMode
                    Case NumPadModes.Operator
                        ActualOperator = NumPadOperators.mult
                        'affiche
                        _NumPadOutput.OutputTxt = String.Empty
                        C24DialogMessage("Calcul:", Math.Round(Copied_Value.Value, 3).ToString & " * ", True, _NumPadTimer)
                    Case NumPadModes.Input
                        'Ajout Esc 
                        If C24_Desk.Esc_Cancel_2.IsBlinking = False Then
                            C24_Desk.Esc_Cancel_2.IsBlinking = True
                            AddHandler C24_Desk.Esc_Cancel_2.OnButtonReleased, EscBtnH
                        End If
                        NumPadMode = NumPadModes.Region
                        MarkerRegionMode = MarkerRegionModes.Select
                        'affiche
                        _NumPadOutput.OutputTxt = String.Empty
                        Dim MultResumeScreensBtn As New List(Of C24_Button)
                        MultResumeScreensBtn.AddRange(DefautResumeScreensBtn)
                        MultResumeScreensBtn.Add(C24_Desk.Num_Mult)
                        C24DialogMessage("Region:", "Select as Region Nb : ", True, _NumPadTimer, MultResumeScreensBtn)
                    Case NumPadModes.Region
                        EscBtnH.Invoke(Nothing)
                End Select

            End Sub
        h.Num_MultBtnH = Num_MultBtnH
        AddHandler C24_Desk.Num_Mult.OnButtonReleased, Num_MultBtnH

        'ajout des handlers numérique


        Dim Num_0BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.D0)
                    Case Else
                        NumPad_String &= "0"
                        _NumPadOutput.OutputTxt &= "0"
                End Select

            End Sub
        h.Num_0BtnH = Num_0BtnH
        AddHandler C24_Desk.Num_0.OnButtonReleased, Num_0BtnH

        Dim Num_1BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.D1)
                    Case Else
                        NumPad_String &= "1"
                        _NumPadOutput.OutputTxt &= "1"
                End Select
            End Sub
        h.Num_1BtnH = Num_1BtnH
        AddHandler C24_Desk.Num_1.OnButtonReleased, Num_1BtnH

        Dim Num_2BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.D2)
                    Case Else
                        NumPad_String &= "2"
                        _NumPadOutput.OutputTxt &= "2"
                End Select
            End Sub
        h.Num_2BtnH = Num_2BtnH
        AddHandler C24_Desk.Num_2.OnButtonReleased, Num_2BtnH

        Dim Num_3BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.D3)
                    Case Else
                        NumPad_String &= "3"
                        _NumPadOutput.OutputTxt &= "3"
                End Select
            End Sub
        h.Num_3BtnH = Num_3BtnH
        AddHandler C24_Desk.Num_3.OnButtonReleased, Num_3BtnH

        Dim Num_4BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.D4)
                    Case Else
                        NumPad_String &= "4"
                        _NumPadOutput.OutputTxt &= "4"
                End Select
            End Sub
        h.Num_4BtnH = Num_4BtnH
        AddHandler C24_Desk.Num_4.OnButtonReleased, Num_4BtnH

        Dim Num_5BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.D5)
                    Case Else
                        NumPad_String &= "5"
                        _NumPadOutput.OutputTxt &= "5"
                End Select
            End Sub
        h.Num_5BtnH = Num_5BtnH
        AddHandler C24_Desk.Num_5.OnButtonReleased, Num_5BtnH

        Dim Num_6BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.D6)
                    Case Else
                        NumPad_String &= "6"
                        _NumPadOutput.OutputTxt &= "6"
                End Select
            End Sub
        h.Num_6BtnH = Num_6BtnH
        AddHandler C24_Desk.Num_6.OnButtonReleased, Num_6BtnH

        Dim Num_7BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.D7)
                    Case Else
                        NumPad_String &= "7"
                        _NumPadOutput.OutputTxt &= "7"
                End Select
            End Sub
        h.Num_7BtnH = Num_7BtnH
        AddHandler C24_Desk.Num_7.OnButtonReleased, Num_7BtnH

        Dim Num_8BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.D8)
                    Case Else
                        NumPad_String &= "8"
                        _NumPadOutput.OutputTxt &= "8"
                End Select
            End Sub
        h.Num_8BtnH = Num_8BtnH
        AddHandler C24_Desk.Num_8.OnButtonReleased, Num_8BtnH

        Dim Num_9BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.D9)
                    Case Else
                        NumPad_String &= "9"
                        _NumPadOutput.OutputTxt &= "9"
                End Select
            End Sub
        h.Num_9BtnH = Num_9BtnH
        AddHandler C24_Desk.Num_9.OnButtonReleased, Num_9BtnH

        Dim Num_DotBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NumPadMode
                    Case NumPadModes.NumPad
                        _keyboardInjector.PressKey(KeyboardKey.Decimal)
                    Case Else
                        NumPad_String &= "."
                        _NumPadOutput.OutputTxt &= "."
                End Select
            End Sub
        h.Num_DotBtnH = Num_DotBtnH
        AddHandler C24_Desk.Num_Point.OnButtonReleased, Num_DotBtnH







        _NumPadHandlers.Add(h)
    End Sub
    Private Sub RemoveNumPadHandler()
        For Each h In _NumPadHandlers
            RemoveHandler C24_Desk.Num_Clr.OnButtonReleased, h.Num_CLearBtnH
            RemoveHandler C24_Desk.Num_Enter.OnButtonReleased, h.Num_EnterBtnH
            RemoveHandler C24_Desk.Num_Point.OnButtonReleased, h.Num_DotBtnH
            RemoveHandler C24_Desk.Num_9.OnButtonReleased, h.Num_9BtnH
            RemoveHandler C24_Desk.Num_8.OnButtonReleased, h.Num_8BtnH
            RemoveHandler C24_Desk.Num_7.OnButtonReleased, h.Num_7BtnH
            RemoveHandler C24_Desk.Num_6.OnButtonReleased, h.Num_6BtnH
            RemoveHandler C24_Desk.Num_5.OnButtonReleased, h.Num_5BtnH
            RemoveHandler C24_Desk.Num_4.OnButtonReleased, h.Num_4BtnH
            RemoveHandler C24_Desk.Num_3.OnButtonReleased, h.Num_3BtnH
            RemoveHandler C24_Desk.Num_2.OnButtonReleased, h.Num_2BtnH
            RemoveHandler C24_Desk.Num_1.OnButtonReleased, h.Num_1BtnH
            RemoveHandler C24_Desk.Num_0.OnButtonReleased, h.Num_0BtnH
            RemoveHandler C24_Desk.Num_Mult.OnButtonReleased, h.Num_MultBtnH
            RemoveHandler C24_Desk.Num_Div.OnButtonReleased, h.Num_DivBtnH
            RemoveHandler C24_Desk.Num_Sous.OnButtonReleased, h.Num_SousBtnH
            RemoveHandler C24_Desk.Num_Add.OnButtonReleased, h.Num_AddBtnH
            RemoveHandler C24_Desk.Num_Egal.OnButtonReleased, h.Num_EgalBtnH
            RemoveHandler _NumPadTimer.Elapsed, h.TimerElapsedH
        Next
    End Sub
    Enum NumPadOperators
        none
        add
        mult
        div
        sous
    End Enum
    Enum NumPadModes
        [Operator]
        NumPad
        Marker
        Region
        Input
    End Enum
    Enum MarkerRegionModes
        Add
        Remove
        [Select]
        [Set]
    End Enum
    Class NumPadOutput
        Private _OutputTxt As String = String.Empty
        Private _OutputValue As Double? = Nothing
        Public Event OnChangeTxt(OutputTxt As String)
        Public Event OnChangeValue(OutputValue As Double)
        Public Event OnValidChangeTxt(OutputTxt As String)
        Public Event OnValidChangeValue(OutputValue As Double)
        Dim ValidBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler
        Private _C24BtnList As List(Of C24_Button)



        ReadOnly Property OutputValue As Double?
            Get
                Return _OutputValue
            End Get

        End Property

        Property OutputTxt As String
            Get
                Return _OutputTxt
            End Get
            Set
                If Value <> _OutputTxt Then
                    _OutputTxt = Value
                    RaiseEvent OnChangeTxt(Value)
                    Dim NewDouble As Double = 0
                    If Double.TryParse(Value, NewDouble) Then
                        If OutputValue Is Nothing OrElse NewDouble <> _OutputValue.Value Then
                            _OutputValue = NewDouble
                            RaiseEvent OnChangeValue(NewDouble)
                        End If
                    End If
                End If

            End Set
        End Property

        Public Sub SetValidationBtn(C24BtnList As List(Of C24_Button))
            _C24BtnList = C24BtnList
            ValidBtnH = Sub()
                            For Each C24Btn In C24BtnList
                                Dim C24LedBtn = TryCast(C24Btn, C24_LedButton)
                                If C24LedBtn IsNot Nothing Then
                                    C24LedBtn.IsBlinking = False
                                End If
                                RemoveHandler C24Btn.OnButtonReleased, ValidBtnH
                            Next
                            RaiseEvent OnValidChangeTxt(OutputTxt)
                            RaiseEvent OnValidChangeValue(OutputValue)
                        End Sub

            For Each C24Btn In C24BtnList
                Dim C24LedBtn = TryCast(C24Btn, C24_LedButton)
                If C24LedBtn IsNot Nothing Then
                    C24LedBtn.IsBlinking = True
                End If
                AddHandler C24Btn.OnButtonReleased, ValidBtnH
            Next

        End Sub
        Public Sub UnSetValidationBtn()

            If _C24BtnList IsNot Nothing Then
                For Each C24Btn In _C24BtnList
                    Dim C24LedBtn = TryCast(C24Btn, C24_LedButton)
                    If C24LedBtn IsNot Nothing Then
                        C24LedBtn.IsBlinking = False
                    End If
                    RemoveHandler C24Btn.OnButtonReleased, ValidBtnH
                Next
            End If


        End Sub
    End Class
#End Region

#Region "Markers"
    MustInherit Class Marker_Base

        Private _Start_Pos As Double
        Private _Number As Integer
        Private _Name As String


        Public ReadOnly Property Start_Pos As Double
            Get
                Return _Start_Pos
            End Get
        End Property
        Public ReadOnly Property Number As Integer
            Get
                Return _Number
            End Get
        End Property

        Public ReadOnly Property Name As String
            Get
                Return _Name
            End Get
        End Property
        Sub New(Number As Integer, Start_Pos As Double, Name As String)

            _Start_Pos = Start_Pos
            _Name = Name
            _Number = Number
        End Sub
    End Class
    Class Marker
        Inherits Marker_Base
        Sub New(Number As Integer, Start_Pos As Double, Name As String)
            MyBase.New(Number, Start_Pos, Name)
        End Sub
    End Class
    Class Region
        Inherits Marker_Base

        Private _End_pos As Double

        ReadOnly Property End_pos As Double
            Get
                Return _End_pos
            End Get
        End Property

        Sub New(Number As Integer, Start_Pos As Double, Name As String, End_pos As Double)
            MyBase.New(Number, Start_Pos, Name)
            _End_pos = End_pos
        End Sub
    End Class
    Sub ReBuildMarkersRegions()
        Regions.Clear()
        Markers.Clear()
        Dim MarkerCount = Reaper.utility.CountProjectMarkers
        If MarkerCount.Total > 0 Then
            For i = 0 To MarkerCount.Total - 1
                Dim marker_result = Reaper.utility.EnumProjectMarkers(i)
                If marker_result.retval <> -1 Then
                    If marker_result.IsRegion Then
                        Dim _region As New Region(marker_result.markerId, marker_result.Position, marker_result.Name, marker_result.RegionEnd)
                        Regions.Add(_region.Number, _region)
                    Else
                        Dim _marker As New Marker(marker_result.markerId, marker_result.Position, marker_result.Name)
                        Markers.Add(_marker.Number, _marker)
                    End If
                End If
            Next
        End If

    End Sub
    Private Function FindNextPrevMarker(Markers As List(Of Marker), Pos As Double, [Next] As Boolean) As Marker
        If Markers.Count = 0 Then
            Return Nothing
        End If

        If [Next] = False AndAlso Pos > Markers.Last.Start_Pos Then
            Return Markers.Last
        End If

        For i = 0 To Markers.Count - 1
            Dim marker = Markers(i)
            If Pos <= marker.Start_Pos Then

                If [Next] Then
                    If Pos = marker.Start_Pos Then
                        If i + 1 < Markers.Count Then
                            Return Markers(i + 1)
                        Else
                            Return Nothing
                        End If
                    Else
                        Return Markers(i)
                    End If
                Else
                    If i - 1 >= 0 Then
                        Return Markers(i - 1)
                    Else
                        Return Nothing
                    End If
                End If
            End If
        Next
        Return Nothing
    End Function
    Private Function FindNextPrevRegion(Regions As List(Of Region), Pos As Double, [Next] As Boolean, FromEnd_Pos As Boolean) As Region

        If Regions.Count = 0 Then
            Return Nothing
        End If

        If [Next] = False AndAlso Pos > If(FromEnd_Pos, Regions.Last.End_pos, Regions.Last.Start_Pos) Then
            Return Regions.Last
        End If

        For i = 0 To Regions.Count - 1
            Dim region = Regions(i)

            If Pos <= If(FromEnd_Pos, region.End_pos, region.Start_Pos) Then

                If [Next] Then
                    If Pos = If(FromEnd_Pos, region.End_pos, region.Start_Pos) Then
                        If i + 1 < Regions.Count Then
                            Return Regions(i + 1)
                        Else
                            Return Nothing
                        End If
                    Else
                        Return Regions(i)
                    End If
                Else
                    If i - 1 >= 0 Then
                        Return Regions(i - 1)
                    Else
                        Return Nothing
                    End If
                End If
            End If
        Next
        Return Nothing
    End Function
#End Region

#Region "DialogMessage"
    Sub C24DialogMessage(Title_8chars As String, Message_96chars As String, Optional DisplayNumPadOutput As Boolean = False, Optional ResumeScreensTimer As Timers.Timer = Nothing, Optional CustomResumeScreensBtn As List(Of C24_Button) = Nothing)

        Dim UpdateScreenLine As NumPadOutput.OnChangeTxtEventHandler =
           Sub(NewTxt As String)
               C24_Desk.ScreenLine.ShowTempText(Message_96chars & NewTxt, -1)
           End Sub
        Dim ResumeScreensBtn = DefautResumeScreensBtn
        If CustomResumeScreensBtn IsNot Nothing Then
            ResumeScreensBtn = CustomResumeScreensBtn
        End If


        C24_Desk.ScreenChannelBarScroll.ShowTempText(Title_8chars, -1)


        If DisplayNumPadOutput Then
            'init
            C24_Desk.ScreenLine.ShowTempText(Message_96chars & _NumPadOutput.OutputTxt, -1)
            AddHandler _NumPadOutput.OnChangeTxt, UpdateScreenLine
        Else
            C24_Desk.ScreenLine.ShowTempText(Message_96chars, -1)
        End If



        Dim ResumeScreens As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
        Sub(TRackNb As Integer?)

            C24_Desk.ScreenChannelBarScroll.ResumeText()
            C24_Desk.ScreenLine.ResumeText()
            If DisplayNumPadOutput Then
                RemoveHandler _NumPadOutput.OnChangeTxt, UpdateScreenLine
            End If
            For Each C24Btn In ResumeScreensBtn
                RemoveHandler C24Btn.OnButtonReleased, ResumeScreens
                Dim C24BtnLed = TryCast(C24Btn, C24_LedButton)
                If C24BtnLed IsNot Nothing Then
                    C24BtnLed.IsBlinking = False
                End If
            Next
        End Sub


        For Each C24Btn In ResumeScreensBtn
            Dim C24BtnLed = TryCast(C24Btn, C24_LedButton)
            AddHandler C24Btn.OnButtonReleased, ResumeScreens
            If C24BtnLed IsNot Nothing Then
                C24BtnLed.IsBlinking = True
            End If
        Next

        Dim ResumeScreensFromTimer As Timers.ElapsedEventHandler =
          Sub()
              C24_Desk.ScreenChannelBarScroll.ResumeText()
              C24_Desk.ScreenLine.ResumeText()
              If DisplayNumPadOutput Then
                  RemoveHandler _NumPadOutput.OnChangeTxt, UpdateScreenLine
              End If
              For Each C24Btn In ResumeScreensBtn
                  Dim C24BtnLed = TryCast(C24Btn, C24_LedButton)
                  If C24BtnLed IsNot Nothing Then
                      C24BtnLed.IsBlinking = False
                  End If
                  RemoveHandler C24Btn.OnButtonReleased, ResumeScreens
              Next

              If ResumeScreensTimer IsNot Nothing Then
                  RemoveHandler ResumeScreensTimer.Elapsed, ResumeScreensFromTimer
              End If
          End Sub

        If ResumeScreensTimer IsNot Nothing Then
            ResumeScreensTimer.Stop()
            ResumeScreensTimer.Start()
            AddHandler ResumeScreensTimer.Elapsed, ResumeScreensFromTimer
        End If

    End Sub
    Sub C24DisplayTempMessage(Title_8chars As String, Message_96chars As String, Optional TempTime As Integer = 2000)


        If C24_Desk.ScreenChannelBarScroll.OriginalTextBack Then
            C24_Desk.ScreenChannelBarScroll.ShowTempText(Title_8chars, TempTime)
        Else
            Dim _ChBarScreenHandler As New ChBarScreenHandler
            Dim ChBarScreenH As Control24.Hardware.C24_ScribbleScreen.OnOriginaltextBackEventHandler = Sub()
                                                                                                           RemoveHandler C24_Desk.ScreenChannelBarScroll.OnOriginaltextBack, ChBarScreenH
                                                                                                           _ChBarScreenHandlers.Remove(_ChBarScreenHandler)
                                                                                                           C24_Desk.ScreenChannelBarScroll.ShowTempText(Title_8chars, TempTime)

                                                                                                       End Sub
            _ChBarScreenHandler.ChBarScreenH = ChBarScreenH
            _ChBarScreenHandlers.Add(_ChBarScreenHandler)
            AddHandler C24_Desk.ScreenChannelBarScroll.OnOriginaltextBack, ChBarScreenH
        End If

        If C24_Desk.ScreenLine.OriginalTextBack Then
            C24_Desk.ScreenLine.ShowTempText(Message_96chars, TempTime)
        Else
            Dim _ScreenLineHandler As New ScreenLineHandler
            Dim ScreenLineH As Control24.Hardware.C24_ScribbleScreen.OnOriginaltextBackEventHandler = Sub()
                                                                                                          RemoveHandler C24_Desk.ScreenLine.OnOriginaltextBack, ScreenLineH
                                                                                                          _ScreenLineHandlers.Remove(_ScreenLineHandler)
                                                                                                          C24_Desk.ScreenLine.ShowTempText(Message_96chars, TempTime)

                                                                                                      End Sub
            _ScreenLineHandler.ScreenLineH = ScreenLineH
            _ScreenLineHandlers.Add(_ScreenLineHandler)
            AddHandler C24_Desk.ScreenLine.OnOriginaltextBack, ScreenLineH
        End If




    End Sub
    Sub RemoveChBarScreenHandler()
        For Each h In _ChBarScreenHandlers
            RemoveHandler C24_Desk.ScreenChannelBarScroll.OnOriginaltextBack, h.ChBarScreenH
        Next
        _ChBarScreenHandlers.Clear()
    End Sub
    Sub RemoveScreenLineHandler()
        For Each h In _ScreenLineHandlers
            RemoveHandler C24_Desk.ScreenLine.OnOriginaltextBack, h.ScreenLineH
        Next
        _ScreenLineHandlers.Clear()
    End Sub

#End Region
#Region "Undo/Save"
    Private Sub AddUndoSaveHandler()
        RemoveUndoSaveHandler()

        Dim h As New UndoSaveHandler

        'Undo Action Linker avec Ctrl et Shift
        Dim Undo_AL As New C24ActionLinker(C24_Desk.Undo, 40029, False,,, True)
        'redo mode
        Undo_AL.ModifiersBtnReaperAction.Add(C24_Desk.Ctrl, 40030)
        Undo_AL.ModifiersUse_Led.Add(C24_Desk.Ctrl, False)
        'Show undo History
        Undo_AL.ModifiersBtnReaperAction.Add(C24_Desk.Shift, 40072)
        Undo_AL.ModifiersUse_Led.Add(C24_Desk.Shift, True)
        Undo_AL.ModifiersLedBlinking.Add(C24_Desk.Shift, True)
        h.Undo_AL = Undo_AL


        Dim AutoSaveTimer As New Timers.Timer()
        h.AutoSaveTimer = AutoSaveTimer

        'Handler from reaper
        'init
        C24_Desk.Save.IsBlinking = Reaper.utility.IsProjectDirty
        Dim ProjectDirtyChangeH As ReaperProjectMonitor.On_ProjectDirtyEventHandler =
            Sub(state As Boolean)
                C24_Desk.Save.IsBlinking = state
            End Sub
        h.ProjectDirtyChangeH = ProjectDirtyChangeH
        AddHandler _ReaperProjectMonitor.On_ProjectDirty, ProjectDirtyChangeH

        'handler from C24
        'init AutoSave mode
        Dim AutosaveTimeTxt = Reaper.utility.GetProjectExtState("C24AUTOSAVE", "TIME", "0")
        Dim AutosaveTimeInt As Integer = -1
        If Integer.TryParse(AutosaveTimeTxt, AutosaveTimeInt) Then
            If AutosaveTimeInt = 0 Then
                AutoSaveTimer.Stop()
            Else
                AutoSaveTimer.AutoReset = True
                AutoSaveTimer.Interval = AutosaveTimeInt * 60000
                AutoSaveTimer.Start()
            End If

        End If


        'init AutoSave
        Dim SetAutoSaveTime As [Delegate] = Sub(Value As Integer)
                                                Reaper.utility.SetProjectExtState("C24AUTOSAVE", "TIME", Value)
                                                If Value = 0 Then
                                                    AutoSaveTimer.Stop()
                                                Else
                                                    AutoSaveTimer.AutoReset = True
                                                    AutoSaveTimer.Interval = Value * 60000
                                                    AutoSaveTimer.Start()
                                                End If

                                            End Sub
        Dim GetAutoSaveTime As Func(Of Integer) = Function()
                                                      Dim _AutoSaveTimeTxt = Reaper.utility.GetProjectExtState("C24AUTOSAVE", "TIME", "0")
                                                      Dim _AutoSaveTimeInt As Integer = 0
                                                      If Integer.TryParse(_AutoSaveTimeTxt, _AutoSaveTimeInt) Then
                                                          Return _AutoSaveTimeInt
                                                      End If
                                                      Return 0
                                                  End Function
        Dim SaveOnReaper As Timers.ElapsedEventHandler = Sub(sender As Object, e As Timers.ElapsedEventArgs)
                                                             If C24_Desk.Save.IsBlinking Then
                                                                 'save
                                                                 C24_Desk.Save.IsBlinking = False
                                                                 Reaper.utility.Main_OnCommand(40026)
                                                             End If
                                                         End Sub
        AddHandler AutoSaveTimer.Elapsed, SaveOnReaper

        'init
        SetAutoSaveTime.DynamicInvoke(GetAutoSaveTime.Invoke)

        Dim SaveBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If Ctl Then
                    'configure Autosave Mode
                    _NumPadOutput.OutputTxt = GetAutoSaveTime.Invoke
                    C24DialogMessage("AutoSave", "Save automatiquely each (min) [Zero is disable] : ", True)
                    'Save Value
                    Dim Num_EnterBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(_TrackNb As Integer?)
                        Dim NewValueTxt = _NumPadOutput.OutputTxt
                        Dim NewValueInt As Integer = -1
                        If Integer.TryParse(NewValueTxt, NewValueInt) Then

                            If NewValueInt = 0 Then

                                C24DisplayTempMessage("AutoSave", "AutoSave is disable")
                            Else

                                C24DisplayTempMessage("AutoSave", "AutoSave is set at : " & NewValueTxt & " (min)")
                            End If
                            SetAutoSaveTime.DynamicInvoke(NewValueInt)
                        End If
                        RemoveHandler C24_Desk.Num_Enter.OnButtonReleased, Num_EnterBtnH
                    End Sub
                    AddHandler C24_Desk.Num_Enter.OnButtonReleased, Num_EnterBtnH

                    Dim Esc_Cancel_2BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(_TrackNb As Integer?)
                        C24DisplayTempMessage("AutoSave", "Escape without save")
                        RemoveHandler C24_Desk.Esc_Cancel_2.OnButtonReleased, Esc_Cancel_2BtnH
                    End Sub
                    AddHandler C24_Desk.Esc_Cancel_2.OnButtonReleased, Esc_Cancel_2BtnH
                Else
                    SaveOnReaper.DynamicInvoke({Nothing, Nothing})
                End If

            End Sub
        h.SaveBtnH = SaveBtnH
        AddHandler C24_Desk.Save.OnButtonReleased, SaveBtnH

        'Dim UndoBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
        '    Sub()
        '        Dim EscEnterBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler = Nothing

        '        If Ctl Then
        '            'redo
        '            Reaper.utility.Main_OnCommand(40030)
        '        ElseIf Shift Then
        '            'Affiche undo history
        '            Undo_AL.State = Not Undo_AL.State
        '            If Undo_AL.State Then
        '                NumPadMode = NumPadModes.NumPad
        '                C24_Desk.Esc_Cancel_2.IsBlinking = True

        '                EscEnterBtnH = Sub(_TrackNb As Integer?)
        '                                   'escape Undo history mode
        '                                   Undo_AL.State = False
        '                                   NumPadMode = NumPadModes.Input
        '                                   C24_Desk.Esc_Cancel_2.IsBlinking = False
        '                                   RemoveHandler C24_Desk.Esc_Cancel_2.OnButtonReleased, EscEnterBtnH
        '                                   RemoveHandler C24_Desk.Num_Enter.OnButtonReleased, EscEnterBtnH
        '                               End Sub
        '                AddHandler C24_Desk.Esc_Cancel_2.OnButtonReleased, EscEnterBtnH
        '                AddHandler C24_Desk.Num_Enter.OnButtonReleased, EscEnterBtnH
        '            Else
        '                'escape Undo history mode
        '                Undo_AL.State = False
        '                NumPadMode = NumPadModes.Input
        '                C24_Desk.Esc_Cancel_2.IsBlinking = False
        '                If EscEnterBtnH IsNot Nothing Then
        '                    RemoveHandler C24_Desk.Esc_Cancel_2.OnButtonReleased, EscEnterBtnH
        '                    RemoveHandler C24_Desk.Num_Enter.OnButtonReleased, EscEnterBtnH
        '                End If

        '            End If

        '        Else
        '            'undo
        '            Reaper.utility.Main_OnCommand(40029)
        '        End If
        '    End Sub
        'h.UndoBtnH = UndoBtnH
        'AddHandler C24_Desk.Undo.OnButtonReleased, UndoBtnH

        _UndoSaveHandler.Add(h)

    End Sub
    Private Sub RemoveUndoSaveHandler()

        For Each h In _UndoSaveHandler
            'RemoveHandler C24_Desk.Undo.OnButtonReleased, h.UndoBtnH
            RemoveHandler C24_Desk.Save.OnButtonReleased, h.SaveBtnH
            RemoveHandler _ReaperProjectMonitor.On_ProjectDirty, h.ProjectDirtyChangeH
            h.AutoSaveTimer.Dispose()
            h.Undo_AL.Dispose()
        Next
        _UndoSaveHandler.Clear()
    End Sub
#End Region

#Region "Navigation"

    Sub AddNavigationHandler()
        RemoveNavigationHandler()

        Dim h As New NavigationHandler




        Dim UpBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NavigationMode
                    Case NavigationModes.Nav
                        Reaper.utility.Main_OnCommand(40138)
                    Case NavigationModes.Zoom
                        Reaper.utility.Main_OnCommand(40111)
                    Case NavigationModes.Sel_Adj

                    Case NavigationModes.EditTools

                    Case NavigationModes.None
                        _keyboardInjector.PressKey(KeyboardKey.UpArrow)
                    Case NavigationModes.Automation
                        Reaper.utility.Main_OnCommand(41863)
                End Select

            End Sub
        h.UpBtnH = UpBtnH
        AddHandler C24_Desk.Up.OnButtonReleased, UpBtnH

        Dim DownBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NavigationMode
                    Case NavigationModes.Nav
                        Reaper.utility.Main_OnCommand(40139)
                    Case NavigationModes.Zoom
                        Reaper.utility.Main_OnCommand(40112)
                    Case NavigationModes.Sel_Adj

                    Case NavigationModes.EditTools

                    Case NavigationModes.None
                        _keyboardInjector.PressKey(KeyboardKey.DownArrow)
                    Case NavigationModes.Automation
                        Reaper.utility.Main_OnCommand(41863)
                End Select

            End Sub
        h.DownBtnH = DownBtnH
        AddHandler C24_Desk.Down.OnButtonReleased, DownBtnH

        Dim LeftBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NavigationMode
                    Case NavigationModes.Nav
                        Reaper.utility.Main_OnCommand(40140)
                    Case NavigationModes.Zoom
                        Reaper.utility.Main_OnCommand(1012)
                    Case NavigationModes.Sel_Adj
                        IsCursorPlacedLeft = True
                        Reaper.utility.SetCursorPosition(_ReaperProjectMonitor.SelectionStartTime)
                    Case NavigationModes.EditTools
                        IsCursorPlacedLeft = True
                        Reaper.utility.Main_OnCommand(41173)
                    Case NavigationModes.None
                        _keyboardInjector.PressKey(KeyboardKey.LeftArrow)

                End Select

            End Sub
        h.LeftBtnH = LeftBtnH
        AddHandler C24_Desk.Previous.OnButtonReleased, LeftBtnH

        Dim RightBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Select Case NavigationMode
                    Case NavigationModes.Nav
                        Reaper.utility.Main_OnCommand(40141)
                    Case NavigationModes.Zoom
                        Reaper.utility.Main_OnCommand(1011)
                    Case NavigationModes.Sel_Adj
                        IsCursorPlacedLeft = False
                        Reaper.utility.SetCursorPosition(_ReaperProjectMonitor.SelectionEndTime)
                    Case NavigationModes.EditTools
                        IsCursorPlacedLeft = False
                        Reaper.utility.Main_OnCommand(41174)
                    Case NavigationModes.None
                        _keyboardInjector.PressKey(KeyboardKey.RightArrow)

                End Select

            End Sub
        h.RightBtnH = RightBtnH
        AddHandler C24_Desk.Next.OnButtonReleased, RightBtnH

        Dim Nav_C24RadioBtnHandler As New C24RadioBtnHandler

        Dim NavBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)

                NavigationMode = NavigationModes.Nav

            End Sub
        Nav_C24RadioBtnHandler.Add(C24_Desk.Nav, NavBtnH)

        Dim ZoomBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
           Sub()
               NavigationMode = NavigationModes.Zoom
           End Sub
        Nav_C24RadioBtnHandler.Add(C24_Desk.Zoom, ZoomBtnH)



        Dim EditTools As [Delegate] = Sub()
                                          'eteint tous les NavigationMode
                                          Nav_C24RadioBtnHandler.SetAllBtnOff()
                                          NavigationMode = NavigationModes.EditTools
                                          If IsCursorPlacedLeft Then
                                              Reaper.utility.Main_OnCommand(41173)
                                          Else
                                              Reaper.utility.Main_OnCommand(41174)
                                          End If
                                      End Sub
        h.EditTools = EditTools

        Dim SelAdj As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If _ReaperProjectMonitor.SelectionActive = False Then
                    C24DisplayTempMessage("Select", "There is no selection avaiblable !")
                    C24_Desk.Sel_Adj.IsBlinking = False

                Else
                    'eteint les radios buttons de Tools_edit
                    If _EditToolsHandlers.Count > 0 Then
                        _EditToolsHandlers.First.Tools_C24RadioBtnHandler.SetAllBtnOff()
                    End If
                    'passe en mode ajustement de la sélection
                    NavigationMode = NavigationModes.Sel_Adj
                    JogWheelToolMode = JogWheelToolModes.SelAdj
                    If IsCursorPlacedLeft Then
                        Reaper.utility.SetCursorPosition(_ReaperProjectMonitor.SelectionStartTime)
                    Else
                        Reaper.utility.SetCursorPosition(_ReaperProjectMonitor.SelectionEndTime)
                    End If
                End If

            End Sub

        Nav_C24RadioBtnHandler.Add(C24_Desk.Sel_Adj, SelAdj)

        Dim None As [Delegate] = Sub()
                                     NavigationMode = NavigationModes.None
                                     If JogWheelToolMode = JogWheelToolModes.SelAdj Then
                                         JogWheelToolMode = JogWheelToolModes.None
                                     End If

                                 End Sub
        h.None = None
        Nav_C24RadioBtnHandler.NoLedBtnSelectedDelegate = None

        h.Nav_C24RadioBtnHandler = Nav_C24RadioBtnHandler
        Nav_C24RadioBtnHandler.AddHandlers()

        _NavigationHandlers.Add(h)
    End Sub
    Sub RemoveNavigationHandler()
        For Each h In _NavigationHandlers
            RemoveHandler C24_Desk.Next.OnButtonReleased, h.RightBtnH
            RemoveHandler C24_Desk.Previous.OnButtonReleased, h.LeftBtnH
            RemoveHandler C24_Desk.Down.OnButtonReleased, h.DownBtnH
            RemoveHandler C24_Desk.Up.OnButtonReleased, h.UpBtnH
            h.Nav_C24RadioBtnHandler.Dispose()
        Next

        _NavigationHandlers.Clear()
    End Sub

    Enum NavigationModes
        Nav
        Zoom
        Sel_Adj
        None
        EditTools
        Automation
    End Enum

#End Region

#Region "Utility"
    Sub AddUtilityHandler()
        RemoveUtilityHandler()


        Dim h As New UtilityHandler



        Dim Esc_Cancel_2BtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()

                _keyboardInjector.PressKey(KeyboardKey.Escape)

            End Sub
        h.Esc_Cancel_2BtnH = Esc_Cancel_2BtnH
        AddHandler C24_Desk.Esc_Cancel_2.OnButtonReleased, Esc_Cancel_2BtnH



        'init JogWheelMode
        Select Case JogWheelMode
            Case JogWheelModes.Shuttle
                C24_Desk.Shuttle.Led = True
        End Select
        'Add shuttle Handler
        Dim ShuttleBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                C24_Desk.Shuttle.Led = Not C24_Desk.Shuttle.Led
                If C24_Desk.Shuttle.Led Then
                    JogWheelMode = JogWheelModes.Shuttle
                    NumPadMode = NumPadModes.Input
                Else
                    JogWheelMode = JogWheelModes.Tab
                    NumPadMode = NumPadModes.NumPad
                End If
            End Sub
        h.ShuttleBtnH = ShuttleBtnH
        AddHandler C24_Desk.Shuttle.OnButtonReleased, ShuttleBtnH

        _UtilityHandlers.Add(h)

    End Sub
    Sub RemoveUtilityHandler()
        For Each h In _UtilityHandlers
            RemoveHandler C24_Desk.Shuttle.OnButtonReleased, h.ShuttleBtnH
            RemoveHandler C24_Desk.Esc_Cancel_2.OnButtonReleased, h.Esc_Cancel_2BtnH


        Next
    End Sub
#End Region

#Region "Edit Tools"

    Sub AddEditToolsHandler()
        RemoveEditToolsHandler()

        Dim h As New EditToolsHandler

        Dim Tools_C24RadioBtnHandler As New C24RadioBtnHandler

        'Selection activé sur Reaper
        Dim SelectionActivateH As ReaperProjectMonitor.On_SelectionChangeEventHandler =
            Sub(IsSelectionActive As Boolean)
                If IsSelectionActive = False Then
                    'désactive le Mode Sel_Adj 
                    If _NavigationHandlers.Count > 0 Then
                        _NavigationHandlers.First.Nav_C24RadioBtnHandler.SetBtnStatus(C24_Desk.Sel_Adj, False)
                    End If
                End If
            End Sub
        h.SelectionActivateH = SelectionActivateH
        AddHandler _ReaperProjectMonitor.On_SelectionChange, SelectionActivateH

        'Items sélectionnés de Reaper
        'Dim SelectionChangeH As SelectedItemsManager.On_SelectionChangeEventHandler =
        '    Sub(projectIndex As Integer, items As IReadOnlyList(Of ReaperItemRef))
        '        If items.Count > 0 Then
        '            C24_Desk.Tools_Select.Led = True
        '        Else
        '            C24_Desk.Tools_Select.Led = False
        '            'désactive tous les tools qui utilisent des items
        '            Tools_C24RadioBtnHandler.SetAllBtnOff()
        '        End If
        '    End Sub
        'h.SelectionChangeH = SelectionChangeH
        'AddHandler _SelectedItemsManager.On_SelectionChange, SelectionChangeH

        'init
        C24_Desk.Tools_Select.Led = If(Reaper.utility.GetProjectExtState("C24SELECT", "ACTIVE", "1") = "1", True, False)
        'Active la sélection des items sous la zone sélectionnée
        Dim Tools_SelectBtnH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
           Sub()

               C24_Desk.Tools_Select.Led = Not C24_Desk.Tools_Select.Led
               Reaper.utility.SetProjectExtState("C24SELECT", "ACTIVE", If(C24_Desk.Tools_Select.Led = True, "1", "0"))
               'déselectionne tous les items 
               If C24_Desk.Tools_Select.Led = False Then
                   Reaper.utility.Main_OnCommand(40289)
               End If

           End Sub
        h.Tools_SelectBtnH = Tools_SelectBtnH
        AddHandler C24_Desk.Tools_Select.OnButtonReleased, Tools_SelectBtnH



        Dim Tools_NoneD As [Delegate] =
           Sub()
               'eteint le mode EditTools dans Navigation
               If _NavigationHandlers.Count > 0 Then
                   _NavigationHandlers.First.None.DynamicInvoke()
               End If
               JogWheelToolMode = JogWheelToolModes.None
           End Sub
        Tools_C24RadioBtnHandler.NoLedBtnSelectedDelegate = Tools_NoneD




        Dim Tools_GrabBtnD As [Delegate] =
                  Sub(FakeArg As Integer?)
                      'verifie qu'un seul item est sélectionné
                      If _SelectedItemsManager.CountSelectedItems > 1 Then
                          C24DisplayTempMessage("Grab", "Too many items selected !")
                          C24_Desk.Tools_Grab.IsBlinking = False
                      ElseIf _SelectedItemsManager.CountSelectedItems = 0 Then
                          C24DisplayTempMessage("Grab", "No items selected !")
                          C24_Desk.Tools_Grab.IsBlinking = False
                      Else
                          'allume le mode EditTools dans Navigation
                          If _NavigationHandlers.Count > 0 Then
                              _NavigationHandlers.First.EditTools.DynamicInvoke()
                          End If
                          JogWheelToolMode = JogWheelToolModes.Grab
                          'Time selection: Set time selection to item
                          Reaper.utility.Main_OnCommand(40290)
                      End If

                  End Sub
        Tools_C24RadioBtnHandler.Add(C24_Desk.Tools_Grab, Tools_GrabBtnD)

        Dim Tools_TrimBtnD As [Delegate] =
                 Sub(FakeArg As Integer?)
                     'verifie qu'un seul item est sélectionné
                     If _SelectedItemsManager.CountSelectedItems > 1 Then
                         C24DisplayTempMessage("Trim", "Too many items selected !")
                         C24_Desk.Tools_Trim.IsBlinking = False
                     ElseIf _SelectedItemsManager.CountSelectedItems = 0 Then
                         C24DisplayTempMessage("Trim", "No items selected !")
                         C24_Desk.Tools_Trim.IsBlinking = False
                     Else
                         'allume le mode EditTools dans Navigation
                         If _NavigationHandlers.Count > 0 Then
                             _NavigationHandlers.First.EditTools.DynamicInvoke()
                         End If
                         JogWheelToolMode = JogWheelToolModes.Trim
                     End If

                 End Sub
        Tools_C24RadioBtnHandler.Add(C24_Desk.Tools_Trim, Tools_TrimBtnD)

        Dim ShuffleBtnD As [Delegate] =
                 Sub(FakeArg As Integer?)
                     'eteint le mode EditTools dans Navigation
                     If _NavigationHandlers.Count > 0 Then
                         _NavigationHandlers.First.None.DynamicInvoke()
                     End If

                     JogWheelToolMode = JogWheelToolModes.Shuffle


                 End Sub
        Tools_C24RadioBtnHandler.Add(C24_Desk.Shuffle, ShuffleBtnD)

        Tools_C24RadioBtnHandler.AddHandlers()
        h.Tools_C24RadioBtnHandler = Tools_C24RadioBtnHandler

        'Toogle le mode Lock pour tout le projet
        Dim SpotAL As New C24ActionLinker(C24_Desk.Spot, 1135,,,, True)

        'Toogle Lock pour les items sélectionnés
        SpotAL.ModifiersBtnReaperAction.Add(C24_Desk.Shift, 40687)
        SpotAL.ModifiersLedBlinking.Add(C24_Desk.Shift, True)
        SpotAL.ExitModifierModeOnTimer = True
        h.SpotAL = SpotAL

        'init

        Dim Tools_MidiBtnH As C24_Button.OnButtonReleasedEventHandler =
            Sub()
                If Shift Then
                    Reaper.utility.Main_OnCommand_SafeUI(40153)
                Else
                    Reaper.utility.Main_OnCommand_SafeUI(40716)
                End If



            End Sub
        h.Tools_MidiBtnH = Tools_MidiBtnH
        AddHandler C24_Desk.Tools_Midi.OnButtonReleased, Tools_MidiBtnH

        _EditToolsHandlers.Add(h)
    End Sub
    Sub RemoveEditToolsHandler()
        For Each h In _EditToolsHandlers
            RemoveHandler C24_Desk.Tools_Select.OnButtonReleased, h.Tools_SelectBtnH
            RemoveHandler _SelectedItemsManager.On_SelectionChange, h.SelectionChangeH
            RemoveHandler _ReaperProjectMonitor.On_SelectionChange, h.SelectionActivateH
            RemoveHandler C24_Desk.Tools_Midi.OnButtonReleased, h.Tools_MidiBtnH
            h.SpotAL.Dispose()
            h.Tools_C24RadioBtnHandler.Dispose()
        Next
        _EditToolsHandlers.Clear()
    End Sub

    Class C24RadioBtnHandler
        Inherits Dictionary(Of C24_LedButton, [Delegate])
        Implements IDisposable

        Private disposedValue As Boolean
        Private _SetBtnHandlers As New List(Of SetBtnHandler)
        Private _InitLedBtn As C24_LedButton = Nothing
        Private _BlinkingMode As Boolean = False
        Private _NoLedBtnSelectedDelegate As [Delegate] = Nothing

        Class SetBtnHandler
            Public SetBtn As [Delegate]
            Public LedBtn As Control24.Hardware.C24_LedButton
        End Class
        Public Property InitLedBtn As C24_LedButton
            Get
                Return _InitLedBtn
            End Get
            Set
                _InitLedBtn = Value
            End Set
        End Property
        Public Property BlinkingMode As Boolean
            Get
                Return _BlinkingMode
            End Get
            Set
                _BlinkingMode = Value
            End Set
        End Property
        Public Property NoLedBtnSelectedDelegate As [Delegate]
            Get
                Return _NoLedBtnSelectedDelegate
            End Get
            Set
                _NoLedBtnSelectedDelegate = Value
            End Set
        End Property
        Public Sub SetBtnStatus(C24Btn As C24_LedButton, Status As Boolean)
            If C24Btn.Led OrElse C24Btn.IsBlinking Then
                If Status = False Then
                    C24Btn.ButtonCmd_OnButtonStatusChange(Status)
                End If
            Else
                If Status = True Then
                    C24Btn.ButtonCmd_OnButtonStatusChange(Status)
                End If
            End If
        End Sub
        Public Sub SetAllBtnOff()
            For Each C24Btn In Me.Keys
                If C24Btn.Led OrElse C24Btn.IsBlinking Then
                    SetBtnStatus(C24Btn, False)
                End If
            Next
        End Sub
        Public Sub AddHandlers()


            For Each LedBtn In Me.Keys
                Dim h As New SetBtnHandler
                h.LedBtn = LedBtn

                Dim SetBtn As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub(TrackNb As Integer?)
                    'Leds
                    Dim _LedBtnList = Me.Keys
                    For Each _LedBtn In _LedBtnList
                        If _LedBtn.Name = LedBtn.Name Then
                            If BlinkingMode AndAlso LedBtn.IsBlinking AndAlso NoLedBtnSelectedDelegate IsNot Nothing Then
                                'on re appuis sur le C24Btn pour éteindre tous les boutons
                                LedBtn.IsBlinking = False
                                NoLedBtnSelectedDelegate.DynamicInvoke()
                            ElseIf BlinkingMode = False AndAlso LedBtn.Led AndAlso NoLedBtnSelectedDelegate IsNot Nothing Then
                                'on re appuis sur le C24Btn pour éteindre tous les boutons  
                                LedBtn.IsBlinking = False
                                NoLedBtnSelectedDelegate.DynamicInvoke()
                            Else
                                'changement de Btn Radio
                                If BlinkingMode Then
                                    _LedBtn.IsBlinking = True
                                Else
                                    _LedBtn.Led = True
                                End If
                                'delegate
                                Dim BtnDelegate As [Delegate] = Me(LedBtn)
                                If BtnDelegate IsNot Nothing Then
                                    BtnDelegate.DynamicInvoke({Nothing})
                                End If
                            End If

                        Else
                            _LedBtn.IsBlinking = False
                        End If
                    Next

                End Sub
                h.SetBtn = SetBtn
                AddHandler LedBtn.OnButtonReleased, SetBtn
                _SetBtnHandlers.Add(h)
            Next

            If InitLedBtn IsNot Nothing AndAlso Me.ContainsKey(InitLedBtn) Then
                Me(InitLedBtn).DynamicInvoke(Nothing)
            End If

            If InitLedBtn Is Nothing AndAlso NoLedBtnSelectedDelegate IsNot Nothing Then
                NoLedBtnSelectedDelegate.DynamicInvoke(Nothing)
            End If

        End Sub
        Public Sub RefreshHandlers()
            RemoveHandlers()
            AddHandlers()

        End Sub
        Public Sub RemoveHandlers()
            For Each h In _SetBtnHandlers
                RemoveHandler h.LedBtn.OnButtonReleased, h.SetBtn
            Next
            _SetBtnHandlers.Clear()
        End Sub



        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not disposedValue Then
                If disposing Then
                    ' TODO: supprimer l'état managé (objets managés)
                    RemoveHandlers()
                End If

                ' TODO: libérer les ressources non managées (objets non managés) et substituer le finaliseur
                ' TODO: affecter aux grands champs une valeur null
                disposedValue = True
            End If
        End Sub

        ' ' TODO: substituer le finaliseur uniquement si 'Dispose(disposing As Boolean)' a du code pour libérer les ressources non managées
        ' Protected Overrides Sub Finalize()
        '     ' Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(disposing As Boolean)'
        '     Dispose(disposing:=False)
        '     MyBase.Finalize()
        ' End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            ' Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(disposing As Boolean)'
            Dispose(disposing:=True)
            GC.SuppressFinalize(Me)
        End Sub
    End Class

#End Region
#Region "Edit Function"
    Sub AddEditFunctionHandler()
        RemoveEditFunctionHandler()

        Dim h As New EditFunctionHandler

        Dim CutH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                'Edit: Cut items/tracks/envelope points (depending on focus) within time selection, if any (smart cut)
                Reaper.Main_OnCommand(41384)
            End Sub
        h.CutH = CutH
        AddHandler C24_Desk.Cut.OnButtonReleased, CutH

        Dim CopyH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                'Edit: Copy items/tracks/envelope points (depending on focus) within time selection, if any (smart copy)
                Reaper.Main_OnCommand(41383)
            End Sub
        h.CopyH = CopyH
        AddHandler C24_Desk.Copy.OnButtonReleased, CopyH

        Dim PasteH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()


                If Shift Then
                    'Item: Insert time on tracks and paste items
                    Reaper.Main_OnCommand(41748)
                Else
                    'Item: Paste items/tracks
                    Reaper.Main_OnCommand(42398)
                End If

            End Sub
        h.PasteH = PasteH
        AddHandler C24_Desk.Paste.OnButtonReleased, PasteH

        Dim Edit_DeleteH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                'Remove items/tracks/envelope points (depending on focus)
                Reaper.Main_OnCommand(40697)
            End Sub
        h.Edit_DeleteH = Edit_DeleteH
        AddHandler C24_Desk.Edit_Delete.OnButtonReleased, Edit_DeleteH

        Dim SeparateH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Dim selection = Reaper.utility.GetSelection
                If selection.Item1 > 0 AndAlso selection.Item2 > 0 Then
                    'Item: Split items at time selection or razor edit
                    Reaper.Main_OnCommand(40061)

                Else
                    'Item: Split items at edit or play cursor (select right)
                    Reaper.Main_OnCommand(40012)
                End If

            End Sub
        h.SeparateH = SeparateH
        AddHandler C24_Desk.Separate.OnButtonReleased, SeparateH

        'Capture = Auto-punch en mode item voir la section transport

        Dim DuplicateH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Dim selection = Reaper.utility.GetSelection
                If selection.Item1 > 0 AndAlso selection.Item2 > 0 Then
                    If Shift Then
                        'Item: Duplicate selected area of items
                        Reaper.Main_OnCommand(41296)
                    Else
                        'Item: Duplicate items
                        Reaper.Main_OnCommand(41295)
                    End If
                End If
            End Sub
        h.DuplicateH = DuplicateH
        AddHandler C24_Desk.Duplicate.OnButtonReleased, DuplicateH

        Dim RepeatH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                Dim selection = Reaper.utility.GetSelection
                If selection.Item1 > 0 AndAlso selection.Item2 > 0 Then
                    _NumPadOutput.SetValidationBtn({C24_Desk.Num_Enter, C24_Desk.Esc_Cancel_2}.ToList)
                    If Shift Then
                        C24DialogMessage("Repeat", "Nb of selected area duplication : ", True)
                        Dim OnChangeValueH As NumPadOutput.OnValidChangeValueEventHandler =
                        Sub(OutputValue? As Double)
                            If OutputValue IsNot Nothing Then
                                For i = 0 To OutputValue.Value - 1
                                    'Item: Duplicate selected area of items
                                    Reaper.Main_OnCommand(41296)
                                Next
                            End If
                            RemoveHandler _NumPadOutput.OnValidChangeValue, OnChangeValueH
                            _NumPadOutput.UnSetValidationBtn()
                        End Sub
                        h.RepeatValidChangeH = OnChangeValueH
                        AddHandler _NumPadOutput.OnValidChangeValue, OnChangeValueH
                    Else
                        C24DialogMessage("Repeat", "Nb of item duplication : ", True)
                        Dim OnChangeValueH As NumPadOutput.OnValidChangeValueEventHandler =
                        Sub(OutputValue? As Double)
                            If OutputValue IsNot Nothing Then
                                For i = 0 To OutputValue.Value - 1
                                    'Item: Duplicate items
                                    Reaper.Main_OnCommand(41295)
                                Next
                            End If
                            _NumPadOutput.UnSetValidationBtn()
                            RemoveHandler _NumPadOutput.OnValidChangeValue, OnChangeValueH
                        End Sub
                        h.RepeatValidChangeH = OnChangeValueH
                        AddHandler _NumPadOutput.OnValidChangeValue, OnChangeValueH
                    End If

                End If
            End Sub
        h.RepeatH = RepeatH
        AddHandler C24_Desk.Repeat.OnButtonReleased, RepeatH

        _EditFunctionHandlers.Add(h)

    End Sub

    Sub RemoveEditFunctionHandler()
        For Each h In _EditFunctionHandlers
            RemoveHandler C24_Desk.Cut.OnButtonReleased, h.CutH
            RemoveHandler C24_Desk.Copy.OnButtonReleased, h.CopyH
            RemoveHandler C24_Desk.Paste.OnButtonReleased, h.PasteH
            RemoveHandler C24_Desk.Edit_Delete.OnButtonReleased, h.Edit_DeleteH
            RemoveHandler C24_Desk.Separate.OnButtonReleased, h.SeparateH
            RemoveHandler C24_Desk.Duplicate.OnButtonReleased, h.DuplicateH
            RemoveHandler C24_Desk.Repeat.OnButtonReleased, h.RepeatH

            If h.RepeatValidChangeH IsNot Nothing Then
                RemoveHandler _NumPadOutput.OnValidChangeValue, h.RepeatValidChangeH
            End If
        Next
        _EditFunctionHandlers.Clear()
    End Sub

#End Region

#Region "Groups"
    Sub AddGroupsHandler()
        RemoveGroupsHandler()

        Dim h As New GroupsHandler

        Dim CreateH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                'Track: Create new track media/razor editing group for selected tracks
                Reaper.Main_OnCommand(42578)
            End Sub
        h.CreateH = CreateH
        AddHandler C24_Desk.Create.OnButtonReleased, CreateH

        Dim EnableH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()


                C24DialogMessage("Groups", "Enable / Disable group Nb : ", True)
                _NumPadOutput.SetValidationBtn({C24_Desk.Num_Enter, C24_Desk.Esc_Cancel_2}.ToList)
                Dim OnChangeValueH As NumPadOutput.OnValidChangeValueEventHandler =
                    Sub(OutputValue? As Double)
                        If OutputValue IsNot Nothing Then
                            If OutputValue.Value <= 128 AndAlso OutputValue.Value > 0 Then
                                Dim Cmd As Integer = 42510 + OutputValue.Value
                                Reaper.Main_OnCommand(Cmd)
                            End If
                        End If
                        RemoveHandler _NumPadOutput.OnValidChangeValue, OnChangeValueH
                        _NumPadOutput.UnSetValidationBtn()
                    End Sub
                h.RepeatValidChangeH = OnChangeValueH
                AddHandler _NumPadOutput.OnValidChangeValue, OnChangeValueH

            End Sub
        h.EnableH = EnableH
        AddHandler C24_Desk.Enable.OnButtonReleased, EnableH

        Dim EditH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                'Track: Set track grouping parameters...
                Reaper.Main_OnCommand_SafeUI(40772)
            End Sub
        h.EditH = EditH
        AddHandler C24_Desk.Edit.OnButtonReleased, EditH

        Dim DeleteH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                'Track: Remove selected tracks from all track media/razor editing groups
                Reaper.Main_OnCommand(42579)
            End Sub
        h.DeleteH = DeleteH
        AddHandler C24_Desk.Delete.OnButtonReleased, DeleteH

        Dim SuspendAL As New C24ActionLinker(C24_Desk.Suspend, 40771,,, True)

        h.SuspendAL = SuspendAL

        _GroupsHandlers.add(h)
    End Sub
    Sub RemoveGroupsHandler()
        For Each h In _GroupsHandlers
            RemoveHandler C24_Desk.Create.OnButtonReleased, h.CreateH
            RemoveHandler C24_Desk.Enable.OnButtonReleased, h.EnableH
            RemoveHandler C24_Desk.Edit.OnButtonReleased, h.EditH
            RemoveHandler C24_Desk.Delete.OnButtonReleased, h.DeleteH
            If h.RepeatValidChangeH IsNot Nothing Then
                RemoveHandler _NumPadOutput.OnValidChangeValue, h.RepeatValidChangeH
            End If
            If h.SuspendAL IsNot Nothing Then
                h.SuspendAL.Dispose()
            End If
        Next
        _GroupsHandlers.Clear()
    End Sub

#End Region
#Region "Windows"
    Sub AddWindowsHandler()
        RemoveWindowsHandler()


        Dim h As New WindowsHandler

        Dim MixAL As New C24ActionLinker(C24_Desk.W_Mix, 40078,,,, True)

        h.MixAL = MixAL


        'If SelectedFx IsNot Nothing Then
        '    C24_Desk.W_Plugin.Led = SelectedFx.IsOpen
        'End If
        'Dim W_PluginH As Control24.Hardware.C24_Button.OnButtonReleasedEventHandler =
        '    Sub()
        '        If SelectedFx IsNot Nothing Then
        '            SelectedFx.IsOpen = Not SelectedFx.IsOpen
        '            C24_Desk.W_Plugin.Led = Not C24_Desk.W_Plugin.Led
        '        Else
        '            C24_Desk.W_Plugin.Led = False
        '        End If
        '    End Sub
        'h.W_PluginH = W_PluginH
        'AddHandler C24_Desk.W_Plugin.OnButtonReleased, W_PluginH

        Dim TransAL As New C24ActionLinker(C24_Desk.W_Trans, 40259,,,, True)

        h.TransAL = TransAL

        Dim StatusAL As New C24ActionLinker(C24_Desk.W_Status, 40240,,,, True)

        h.StatusAL = StatusAL

        Dim Mem_LocAL As New C24ActionLinker(C24_Desk.W_Mem_Loc, 40326,,,, True)

        h.Mem_LocAL = Mem_LocAL

        Dim PlugInAL As New C24ActionLinker(C24_Desk.W_Plugin, 4174,,,, True)

        h.PlugInAL = PlugInAL



        _WindowsHandlers.Add(h)
    End Sub
    Sub RemoveWindowsHandler()

        For Each h In _WindowsHandlers
            RemoveHandler C24_Desk.W_Plugin.OnButtonReleased, h.W_PluginH
            If h.MixAL IsNot Nothing Then
                h.MixAL.Dispose()
            End If
            If h.TransAL IsNot Nothing Then
                h.TransAL.Dispose()
            End If
            If h.StatusAL IsNot Nothing Then
                h.StatusAL.Dispose()
            End If
            If h.Mem_LocAL IsNot Nothing Then
                h.Mem_LocAL.Dispose()
            End If
            If h.PlugInAL IsNot Nothing Then
                h.PlugInAL.Dispose()
            End If
        Next
        _WindowsHandlers.Clear()

    End Sub
#End Region

    '=======================================================================================================================================================================================================

#Region "Views"
    Public Enum C24ViewModes
        None
        Home
        PanSend
        PanSend_Pan
        PanSend_Send
        Insert
        FxParam
        Sends
        Output
        Input
        Master
    End Enum

#Region "Refresh View Modes"
    Sub RefreshC24ViewMode(Optional C24ViewMode As C24ViewModes = Nothing)
        Dim ViewModeToRefresh As C24ViewModes? = C24ViewMode
        If ViewModeToRefresh Is Nothing Then
            ViewModeToRefresh = ActualC24ViewMode
        End If
        Select Case ViewModeToRefresh
            Case C24ViewModes.Home
                RefreshC24HomeViewMode()
                NeedInsertParam = False
            Case C24ViewModes.FxParam
                RefreshC24FxParamViewMode(SelectedFx, FxParamPageNb)
            Case C24ViewModes.Sends
                RefreshC24SendsViewMode()
            Case C24ViewModes.PanSend
                RefreshC24PanSendViewMode(PanSendViewMode_TrackNb, PanSendViewMode_PageNb)
            Case C24ViewModes.PanSend_Pan
                RefreshC24PanSend_PanViewMode(PanSendViewMode_TrackNb, PanSendViewMode_PageNb)
            Case C24ViewModes.PanSend_Send
                RefreshC24PanSend_SendViewMode(PanSendViewMode_TrackNb, PanSendViewMode_PageNb)
            Case C24ViewModes.Insert
                RefreshC24InsertViewMode(InsertViewMode_TrackNb, InsertViewMode_PageNb)
            Case C24ViewModes.Output
                RefreshC24OutputViewMode()
            Case C24ViewModes.Input
                RefreshC24InputViewMode()
            Case C24ViewModes.Master
                RefreshC24MasterViewMode()
            Case Else
                RefreshC24HomeViewMode()
        End Select
    End Sub
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
        PanSendViewMode_TrackNb = TrackNb
        PanSendViewMode_PageNb = SendPageNb
        ActualC24ViewMode = C24ViewModes.PanSend
        RemoveViewsStripHandlers()
        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        C24_Desk.ScreenChannelBarScroll.SetText(reaperTrk.Name)
        AddPanSendStripHandlers(TrackNb, SendPageNb)
        AllScribbleScriptBlinking(False)

    End Sub
    Sub RefreshC24PanSend_PanViewMode(TrackNb As Integer, Optional SendPageNb As Integer = 1)
        'PANSEND/Pan View
        PanSendViewMode_TrackNb = TrackNb
        PanSendViewMode_PageNb = SendPageNb
        ActualC24ViewMode = C24ViewModes.PanSend_Pan
        RemoveViewsStripHandlers()
        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        C24_Desk.ScreenChannelBarScroll.SetText(reaperTrk.Name)
        AddPanSend_PanStripHandlers(TrackNb, SendPageNb)
        AllScribbleScriptBlinking(False)

    End Sub
    Sub RefreshC24PanSend_SendViewMode(TrackNb As Integer, Optional SendPageNb As Integer = 1)
        'PANSEND/Send View
        PanSendViewMode_TrackNb = TrackNb
        PanSendViewMode_PageNb = SendPageNb
        ActualC24ViewMode = C24ViewModes.PanSend_Send
        RemoveViewsStripHandlers()
        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        C24_Desk.ScreenChannelBarScroll.SetText(reaperTrk.Name)
        AddPanSend_SendStripHandlers(TrackNb, SendPageNb)
        AllScribbleScriptBlinking(False)

    End Sub
    Sub RefreshC24InsertViewMode(TrackNb As Integer, Optional InsertPageNb As Integer = 1)
        'INSERT View
        InsertViewMode_TrackNb = TrackNb
        InsertViewMode_PageNb = InsertPageNb
        ActualC24ViewMode = C24ViewModes.Insert
        RemoveViewsStripHandlers()
        Dim reaperTrk = If(TrackNb = -1, _ReaperProj.MasterTrack, _ReaperProj.Tracks(TrackNb))
        C24_Desk.ScreenChannelBarScroll.SetText(reaperTrk.Name)
        AddInsertStripHandlers(TrackNb, InsertPageNb)


    End Sub
    Sub RefreshC24FxParamViewMode(Fx As ReaperFX, Optional _FxParamPageNb As Integer = 1)
        FxParamPageNb = _FxParamPageNb
        'FxParam View
        ActualC24ViewMode = C24ViewModes.FxParam
        RemoveViewsStripHandlers()


        If _ReaperSessionConfig.fxParamConfigs.ContainsKey(Fx.Name) Then
            FxParamTotalPages = Math.Max(1, CInt(Math.Ceiling(_ReaperSessionConfig.fxParamConfigs(Fx.Name).Count / CDbl(24))))
        Else
            FxParamTotalPages = Math.Max(1, CInt(Math.Ceiling(Fx.ParamCount / CDbl(24))))
        End If



        C24_Desk.ScreenChannelBarScroll.SetText(_ReaperSessionConfig.GetPluginShortName(Fx.Name))
        C24_Desk.ScreenChannelBarScroll.ShowTempText("Page" & FxParamPageNb & "/" & FxParamTotalPages)
        AddFxParamStripHandlers(Fx, FxParamPageNb)
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

        If Assign_Mode Then
            C24_Desk.ScreenChannelBarScroll.SetText("AsgnSnd" & SendLetter)
        Else
            If Flip_Mode = False Then
                C24_Desk.ScreenChannelBarScroll.SetText("ViewSnd" & SendLetter)
            Else
                C24_Desk.ScreenChannelBarScroll.SetText("SendFlip")
            End If
        End If





        AddSendsStripHandlers(SendIndex, SendLetter)
        AllScribbleScriptBlinking(False)

    End Sub
    Sub RefreshC24OutputViewMode(Optional ReturnToC24View As C24ViewModes = C24ViewModes.Home)
        'Output View
        ActualC24ViewMode = C24ViewModes.Output
        RemoveViewsStripHandlers()


        If Assign_Mode Then
            C24_Desk.ScreenChannelBarScroll.SetText("Asgn Out")
        Else

            C24_Desk.ScreenChannelBarScroll.SetText("Outputs")

        End If





        AddOutputsStripHandlers(ReturnToC24View)
        AllScribbleScriptBlinking(False)

    End Sub
    Sub RefreshC24InputViewMode()
        'Input View
        ActualC24ViewMode = C24ViewModes.Input
        RemoveViewsStripHandlers()


        If Assign_Mode Then
            C24_Desk.ScreenChannelBarScroll.SetText("Asgn  In")
        Else

            C24_Desk.ScreenChannelBarScroll.SetText("Inputs")

        End If





        AddInputsStripHandlers()
        AllScribbleScriptBlinking(False)

    End Sub
    Sub RefreshC24MasterViewMode()
        'Master View
        ActualC24ViewMode = C24ViewModes.Master
        RemoveViewsStripHandlers()
        C24_Desk.ScreenChannelBarScroll.SetText("Master")
        AddMasterStripHandlers()
        AllScribbleScriptBlinking(False)
    End Sub

    Public Sub RemoveViewsStripHandlers()


        Select Case LastC24ViewMode
            Case C24ViewModes.None

            Case C24ViewModes.Home

                RemoveHomeStripHandlers()

            Case C24ViewModes.PanSend

                RemovePanSendStripHandlers()
            Case C24ViewModes.PanSend_Pan

                RemovePanSend_PanStripHandlers()

            Case C24ViewModes.PanSend_Send

                RemovePanSend_SendStripHandlers()

            Case C24ViewModes.Insert

                RemoveInsertStripHandlers()

            Case C24ViewModes.FxParam

                SelectedFx.IsOpen = False

                RemoveFxParamStripHandlers()

            Case C24ViewModes.Sends
                'eteinds les leds des sends
                SetSendLetterLed("")


                RemoveSendsStripHandlers()


            Case C24ViewModes.Output
                RemoveOutputsStripHandlers()
            Case C24ViewModes.Input
                RemoveInputsStripHandlers()
            Case C24ViewModes.Master
                RemoveMasterStripHandlers()
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


#End Region



#Region "Home"

    Private Sub AddHomeStripHandlers()
        RemoveHomeStripHandlers()

        Dim h As New HomeStripHandler

        '--------------------------- Common Handlers----------------------------------
        AddCommonStripHandlers()
        AddMutesBtnHandlers()
        AddSolosHandlers()
        RefreshTrackManagerPeaks()
        '--------------------------- Bank Handlers----------------------------------

        AddBankHandlers()


        '----------------- Flip Handler -------------------------------------------------

        Dim _RefreshViewMode As [Delegate] = Sub()
                                                 RefreshC24HomeViewMode()
                                             End Sub
        AddFlipHandlers(_RefreshViewMode)


        '----------------- Output Handler -------------------------------------------------
        Dim OutputBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If C24_Desk.Output.IsBlinking = False Then
                        C24_Desk.Output.IsBlinking = True
                        RefreshC24OutputViewMode()
                    End If

                End Sub
        h.OutputBtnH = OutputBtnH
        AddHandler C24_Desk.Output.OnButtonReleased, OutputBtnH
        '----------------- Input Handler -------------------------------------------------
        Dim InputBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If C24_Desk.Input.IsBlinking = False Then
                        C24_Desk.Input.IsBlinking = True
                        RefreshC24InputViewMode()
                    End If

                End Sub
        h.InputBtnH = InputBtnH
        AddHandler C24_Desk.Input.OnButtonReleased, InputBtnH
        '----------------- Master Handler -------------------------------------------------
        Dim MasterBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If C24_Desk.Master_Faders.IsBlinking = False Then
                        C24_Desk.Master_Faders.IsBlinking = True
                        RefreshC24MasterViewMode()
                    End If

                End Sub
        h.MasterBtnH = MasterBtnH
        AddHandler C24_Desk.Master_Faders.OnButtonReleased, MasterBtnH

        _HomeStripHandlers.Add(h)

        ClearC24_MiddleStrip()




        ' ── Configurer Faders/Vpots ──────────



        If Flip_Mode Then

            AddVpotVolumeHandlers(True)
            AddFadersPanHandlers()
        Else

            AddFadersVolumeHandlers(True)
            AddVpotPanHandlers()
        End If



    End Sub
    Sub RemoveHomeStripHandlers()
        RemoveBankHandlers()
        RemoveFlipHandlers()
        RemoveFadersVolumeHandlers()
        RemoveVpotPanHandlers()
        RemoveVpotVolumeHandlers()
        RemoveFadersPanHandlers()

        For Each h In _HomeStripHandlers
            RemoveHandler C24_Desk.Output.OnButtonReleased, h.OutputBtnH
            RemoveHandler C24_Desk.Input.OnButtonReleased, h.InputBtnH
        Next
        _HomeStripHandlers.Clear()
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

        Dim sendsIndexes As New List(Of Integer)
        If SendPageNb = 1 Then
            sendsIndexes.AddRange({0, 1, 2, 3, 4})
        ElseIf SendPageNb = 2 Then
            sendsIndexes.AddRange({5, 6, 7, 8, 9})
        End If
        _ReaperTrackSendsManager = New ReaperTrackSendsManager(reaperTrk, _ReaperProj.Tracks, sendsIndexes)
        Dim _ReaperTrackSendsEditor = _ReaperTrackSendsManager.ReaperTrackSendsEditor
        Dim Sends = _ReaperTrackSendsManager.Sends

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

        'init leds
        If Assign_Mode = True Then
            C24_Desk.Assign.IsBlinking = True
            C24_Desk.Esc_Group.SetAllBlinking(True)

        Else
            C24_Desk.Assign.IsBlinking = False
            C24_Desk.Esc_Group.SetAllBlinking(False)
            C24_Desk.Esc_Group.SetAllOff()
        End If


        'add handlers

        If Assign_Mode = True Then

            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    'quit Assign Mode 

                    Assign_Mode = False
                    RefreshC24PanSendViewMode(TrackNb, SendPageNb)

                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH

            Dim EscBtnH As Hardware.C24_GroupLedButton.OnGroupButtonReleasedEventHandler =
                            Sub()
                                'quit Assign Mode 

                                Assign_Mode = False
                                RefreshC24PanSendViewMode(TrackNb, SendPageNb)

                            End Sub
            ah.EscBtnH = EscBtnH
            AddHandler C24_Desk.Esc_Group.OnGroupButtonReleased, EscBtnH

        Else

            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    'Entrer en mode Assign
                    Assign_Mode = True
                    RefreshC24PanSendViewMode(TrackNb, SendPageNb)

                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH



        End If


        _AssignHandlers.Add(ah)


        '----------------- Display + SendHandlers -------------------------------------------------
        '|L<>R|    |SndX|Name|Pre |    |SndX|  No|Name| etc...

        ' ── Configurer Faders ──────────
        AddFadersVolumeHandlers()


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
            'If Sends.Count > i Then

            sh = New PanSendStripHandlers
            Dim sendIndex = i + ((SendPageNb - 1) * 4)
            Dim _sendletter = PageSendsLetter(SendPageNb)(i)
            Dim send = _ReaperTrackSendsEditor.GetSend(_sendletter)




            If Assign_Mode = False Then

                'init commun




                Dim C24_trk_clear = C24_Desk.ChannelStrips(0 + (4 * i) + 1)
                Dim C24_trk_SndX = C24_Desk.ChannelStrips(1 + (4 * i) + 1)
                sh.C24_trk_SndX = C24_trk_SndX

                C24_trk_SndX.ScribbleStrip.Text = "Snd" & _sendletter
                Dim C24_trk_SndName = C24_Desk.ChannelStrips(2 + (4 * i) + 1)
                sh.C24_trk_SndName = C24_trk_SndName
                SendMuteC24_Tracks.Add(C24_trk_SndName)
                Dim C24_trk_SndPre = C24_Desk.ChannelStrips(3 + (4 * i) + 1)
                sh.C24_trk_SndPre = C24_trk_SndPre
                C24_trk_SndName.Vpot.ExtendedScribbles = {C24_trk_SndPre.ScribbleStrip}.ToList

                If send IsNot Nothing Then
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

                    C24_trk_SndName.Vpot.SetAllScribbleText(send.Name)
                    C24_trk_SndName.LedSwitchActive.Led = True
                    'Handlers SendMute ajoutés par AddSendsMutesBtnHandlers

                    'init C24_trk_SndPre

                    C24_trk_SndPre.LedSwitchActive.Led = True
                    If Controler_Utility.GetC24SendMode(send.SendMode) = C24SendMode.Post Then
                        'C24_trk_SndPre.ScribbleStrip.Text = "Post"
                        C24_trk_SndPre.SwitchActive.Led = False
                    Else
                        'C24_trk_SndPre.ScribbleStrip.Text = "Pre"
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



                Else
                    C24_trk_SndPre.LedSwitchActive.Led = False
                    C24_trk_SndPre.SwitchActive.Led = False
                    C24_trk_SndName.LedSwitchActive.Led = False
                    C24_trk_SndName.SwitchActive.Led = False
                    C24_trk_SndX.LedSwitchActive.Led = False
                    C24_trk_SndX.SwitchActive.Led = False
                    C24_trk_SndName.Vpot.SetAllScribbleText("  NoSend")

                End If


            Else 'Assign Mode

                'init commun



                Dim C24_trk_clear = C24_Desk.ChannelStrips(0 + (4 * i) + 1)
                Dim C24_trk_SndX = C24_Desk.ChannelStrips(1 + (4 * i) + 1)
                sh.C24_trk_SndX = C24_trk_SndX

                C24_trk_SndX.ScribbleStrip.Text = "Snd" & PageSendsLetter(SendPageNb)(i)
                Dim C24_trk_SndName = C24_Desk.ChannelStrips(2 + (4 * i) + 1)
                sh.C24_trk_SndName = C24_trk_SndName
                SendMuteC24_Tracks.Add(C24_trk_SndName)
                Dim C24_trk_SndPre = C24_Desk.ChannelStrips(3 + (4 * i) + 1)
                sh.C24_trk_SndPre = C24_trk_SndPre

                Dim SendList As List(Of String) = _ReaperTrackSendsEditor.GetVpotSendsList(" NoSend")
                Dim SendList_check As String() = {" NoSend"}
                If send IsNot Nothing Then
                    SendList_check = {send.Name}
                End If

                'init C24_trk_SndX

                'Add switchActiveBtnH_SendXEnter Handler
                Dim switchActiveBtnH_SendXEnter As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(_TrackNb As Integer?)
                        SndXLetter = C24_Desk.ChannelStrips(_TrackNb - 1).ScribbleStrip.Text.ToCharArray.ToArray(3).ToString
                        RefreshC24PanSend_SendViewMode(TrackNb, SendPageNb)
                    End Sub
                sh.switchActiveBtnH_SendXEnter = switchActiveBtnH_SendXEnter
                AddHandler C24_trk_SndX.SwitchActive.OnButtonReleased, switchActiveBtnH_SendXEnter

                'init C24_trk_SndName
                C24_trk_SndName.Vpot.ChannelScribbleScreen = C24_Desk.ScreenChannelBarScroll
                C24_trk_SndName.Vpot.ExtendedScribbles = {C24_trk_SndPre.ScribbleStrip}.ToList
                C24_trk_SndName.Vpot.InitTextList(SendList, SendList_check)
                C24_trk_SndName.Vpot.VpotMode = C24_Vpot.VpotModeType.TextListWithChannelBar
                C24_trk_SndName.Vpot.SelectItemByText(SendList_check.First)


                'Vpot handler
                Dim VpotSelectItemH As Hardware.C24_Vpot.OnVpotSelectedItemChangeEventHandler =
                        Sub(Track_Nb As Integer, selectedIndex As Integer)
                            Dim SendLetter As String = C24_Desk.ChannelStrips(Track_Nb - 2).ScribbleStrip.Text.ToCharArray.ToArray(3).ToString
                            If C24_trk_SndName.LedSwitchActive.Led = False Then
                                'ajoute la validation si modification de choix dans la liste
                                C24_trk_SndName.SwitchActive.IsBlinking = True
                                C24_trk_SndName.LedSwitchActive.Led = True

                                Dim ssh As New PanSendStripHandlers
                                Dim ValidSendBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                                    Sub(_TrackNb As Integer?)
                                        C24_trk_SndName.Vpot.CheckAllItems(False)
                                        C24_trk_SndName.Vpot.CheckSelectedItem(True)
                                        Dim NewSendName = C24_trk_SndName.Vpot.GetCheckedItems.First

                                        If NewSendName = " NoSend" Then
                                            'retire le send actuel de la lettre
                                            _ReaperTrackSendsEditor.AddUpdateSendOrder(SendLetter, Nothing)
                                        ElseIf NewSendName.StartsWith("+") Then
                                            'ajoute un send et l'enregistre sur la lettre
                                            NewSendName = NewSendName.Remove(0, 1)
                                            Dim AvailableSendIndex = _ReaperTrackSendsEditor.AvailableDestinationsIndexByName(NewSendName)
                                            If AvailableSendIndex >= 0 Then
                                                _ReaperTrackSendsEditor.AddSend(AvailableSendIndex, SendLetter)
                                            End If
                                        ElseIf NewSendName.StartsWith("-") Then
                                            'retire un send, met à jour la liste des sends et le retire si besoin de la lettre
                                            NewSendName = NewSendName.Remove(0, 1)
                                            Dim RemoveSendIndex = _ReaperTrackSendsEditor.SendsIndexByName(send.Name)
                                            If RemoveSendIndex >= 0 Then
                                                _ReaperTrackSendsEditor.RemoveSend(RemoveSendIndex, SendLetter)
                                            End If
                                        Else
                                            'met le send sur la lettre 
                                            _ReaperTrackSendsEditor.AddUpdateSendOrder(SendLetter, NewSendName)
                                        End If



                                        C24_trk_SndName.SwitchActive.IsBlinking = False
                                        C24_trk_SndName.LedSwitchActive.Led = False

                                        RemoveHandler C24_trk_SndName.SwitchActive.OnButtonReleased, ValidSendBtnH


                                    End Sub
                                ssh.ValidSendBtnH = ValidSendBtnH
                                AddHandler C24_trk_SndName.SwitchActive.OnButtonReleased, ValidSendBtnH
                                _PanSendStripHandlers.Add(ssh)

                            End If

                        End Sub
                sh.VpotSelectItemH = VpotSelectItemH
                AddHandler C24_trk_SndName.Vpot.OnVpotSelectedItemChange, VpotSelectItemH











            End If



            _PanSendStripHandlers.Add(sh)

        Next

        If Assign_Mode = False Then
            AddSendsMutesBtnHandlers(SendMuteC24_Tracks, PageSendsLetter(SendPageNb).ToList)
            AddVpotSendsVolumeHandlers(SendMuteC24_Tracks, PageSendsLetter(SendPageNb).ToList)
        End If

        '----------------------------------------------------------------------

    End Sub
    Private Sub RemovePanSendStripHandlers()
        RemoveFadersVolumeHandlers()
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
            If h.C24_trk_SndName IsNot Nothing Then
                RemoveHandler h.C24_trk_SndName.Vpot.OnVpotSelectedItemChange, h.VpotSelectItemH
                RemoveHandler h.C24_trk_SndName.SwitchActive.OnButtonReleased, h.ValidSendBtnH
            End If
        Next

        If _ReaperTrackSendsManager IsNot Nothing Then
            _ReaperTrackSendsManager.Dispose()
            _ReaperTrackSendsManager = Nothing
        End If


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
        _ReaperTrackSendsManager = New ReaperTrackSendsManager(reaperTrk, _ReaperProj.Tracks, sendsIndexes)



        '----------------- Display + Handlers -------------------------------------------------
        'Console.WriteLine("Pan mode")

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

        For Each h In _PanSend_PanStripHandlers

            If h.C24_trk_SndX IsNot Nothing Then
                RemoveHandler h.C24_trk_SndX.SwitchActive.OnButtonReleased, h.switchActiveBtnH_SendXEnter
            End If


        Next

        If _ReaperTrackSendsManager IsNot Nothing Then
            _ReaperTrackSendsManager.Dispose()
            _ReaperTrackSendsManager = Nothing
        End If

        _PanSend_PanStripHandlers.Clear()
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
        _ReaperTrackSendsManager = New ReaperTrackSendsManager(reaperTrk, _ReaperProj.Tracks, sendsIndexes)
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
        AddSendsMutesBtnHandlers({C24_trk_SndName}.ToList, {SndXLetter}.ToList)
        AddVpotSendsVolumeHandlers({C24_trk_SndName}.ToList, {SndXLetter}.ToList)

        'Snd Name2 : |Name2|
        Dim C24_trk_SndPre = C24_Desk.ChannelStrips(2)
        sh.C24_trk_SndPre = C24_trk_SndPre
        'init C24_trk_SndPre
        C24_trk_SndPre.ScribbleStrip.Text = If(SendName.Count > 4, SendName.Substring(4), "")
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

        If _ReaperTrackSendsManager IsNot Nothing Then
            _ReaperTrackSendsManager.Dispose()
            _ReaperTrackSendsManager = Nothing
        End If

        _PanSend_SendStripHandlers.Clear()
    End Sub


#End Region

#Region "Inserts"
    Private Sub AddInsertStripHandlers(TrackNb As Integer, InsertPageNb As Integer)
        RemoveInsertStripHandlers()

        'Pas de Flip Mode
        If Flip_Mode Then
            Flip_Mode = False
        End If


        ClearC24_MiddleStrip()

        '--------------------------- Insert Mode----------------------------------


        Dim _ReaperTrackFxEditor As New ReaperTrackFxEditor(TrackNb, _ReaperSessionConfig)
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

        'init leds
        If Assign_Mode = True Then
            C24_Desk.Assign.IsBlinking = True
            C24_Desk.Esc_Group.SetAllBlinking(True)

        Else
            C24_Desk.Assign.IsBlinking = False
            C24_Desk.Esc_Group.SetAllBlinking(False)
            C24_Desk.Esc_Group.SetAllOff()
        End If


        'add handlers

        If Assign_Mode = True Then

            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    For i = 0 To ModifyFxNames.Length - 1
                        If i < _ReaperTrackFxEditor.Count Then
                            Dim reaperFxName = _ReaperTrackFxEditor.FxNames(i)
                            Dim c24FxName = ModifyFxNames(i)
                            If reaperFxName <> c24FxName Then
                                _ReaperTrackFxEditor.DeleteFx(i)
                                If c24FxName <> "NoInsert" Then
                                    _ReaperTrackFxEditor.AddFx(c24FxName)
                                End If
                            End If
                        ElseIf ModifyFxNames(i) <> "NoInsert" Then
                            _ReaperTrackFxEditor.AddFx(ModifyFxNames(i))
                        End If
                    Next
                    Assign_Mode = False
                    RefreshC24InsertViewMode(TrackNb, InsertPageNb)
                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH

            Dim EscBtnH As Hardware.C24_GroupLedButton.OnGroupButtonReleasedEventHandler =
                            Sub()
                                'quit Assign Mode sans validation

                                Assign_Mode = False
                                RefreshC24InsertViewMode(TrackNb, InsertPageNb)

                            End Sub
            ah.EscBtnH = EscBtnH
            AddHandler C24_Desk.Esc_Group.OnGroupButtonReleased, EscBtnH

        Else

            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    'Entrer en mode Assign
                    Assign_Mode = True
                    RefreshC24InsertViewMode(TrackNb, InsertPageNb)

                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH



        End If


        _AssignHandlers.Add(ah)


        '----------------- Display + InsertHandlers -------------------------------

        AddFadersVolumeHandlers()

        For i = 0 To 7
            Dim Insert_place = i
            Dim Fx_index = i + ((InsertPageNb - 1) * 8)
            Dim Fx As ReaperFX = Nothing

            If Fx_index < _ReaperTrackFxEditor.Count Then
                ' Recharger le FX depuis le trackIndex
                Dim fxList As New Reaper.ReaperTrackFX(TrackNb)
                fxList.Refresh()
                If Fx_index < fxList.Count Then
                    Fx = fxList(Fx_index)
                End If
            End If
            'SelectedFx = Fx

            Dim iH As New InsertStripHandler

            Dim C24_trk_FX_Name1 = C24_Desk.ChannelStrips((Insert_place * 3) + 0)
            iH.C24_trk_FX_Name1 = C24_trk_FX_Name1
            Dim C24_trk_FX_Name2 = C24_Desk.ChannelStrips((Insert_place * 3) + 1)
            iH.C24_trk_FX_Name2 = C24_trk_FX_Name2
            C24_trk_FX_Name1.Vpot.ExtendedScribbles = {C24_trk_FX_Name2.ScribbleStrip}.ToList
            Dim C24_trk_FX_clear = C24_Desk.ChannelStrips((Insert_place * 3) + 2)

            If Assign_Mode = False Then

                'init
                If Fx Is Nothing Then
                    C24_trk_FX_Name1.Vpot.SetAllScribbleText("NoInsert")
                Else
                    'C24_trk_FX_Name1.SwitchActive.IsBlinking = True
                    'C24_trk_FX_Name2.LedSwitchActive.Led = True
                    ' ── Nom court depuis le INI (8 car., modifiable par l'utilisateur) ──
                    Dim shortName = _ReaperSessionConfig.GetPluginShortName(Fx.Name)
                    If Fx.Enabled Then
                        shortName = shortName.ToLower

                    Else
                        shortName = shortName.ToUpper

                    End If

                    C24_trk_FX_Name1.Vpot.SetAllScribbleText(shortName)

                End If
                If InsertSelectedIndex IsNot Nothing AndAlso Fx IsNot Nothing Then
                    If InsertSelectedIndex = Insert_place Then
                        C24_trk_FX_Name1.Vpot.ScribbleScriptsBlinking = True
                        C24_trk_FX_Name1.SwitchActive.IsBlinking = False
                        C24_trk_FX_Name1.SwitchActive.Led = True
                        C24_trk_FX_Name2.LedSwitchActive.Led = True
                        C24_trk_FX_Name2.SwitchActive.Led = Fx.Enabled
                        SelectedFx = Fx
                    Else
                        C24_trk_FX_Name1.Vpot.ScribbleScriptsBlinking = False
                        C24_trk_FX_Name1.SwitchActive.IsBlinking = False
                        C24_trk_FX_Name2.SwitchActive.IsBlinking = False
                        C24_trk_FX_Name2.LedSwitchActive.Led = False
                    End If
                End If

                'Add Handlers

                Dim SelectBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub(_TrackNb As Integer?)
                    'FX sélectionné
                    If SelectedFx IsNot Nothing AndAlso SelectedFx.Name = Fx.Name Then
                        NeedInsertParam = True
                        'go to param fx
                        RefreshC24FxParamViewMode(SelectedFx)
                        'End If

                    Else
                        If Fx IsNot Nothing Then
                            'FX non sélectionné --> selection du Fx
                            AllScribbleScriptBlinking(False)
                            AllSwitchActive(False, False)
                            'RefreshBypassLed(_ReaperTrackFxEditor, InsertPageNb)
                            AllSwitchActiveLed(False, False)
                            C24_trk_FX_Name1.Vpot.ScribbleScriptsBlinking = True
                            InsertSelectedIndex = Insert_place + ((InsertPageNb - 1) * 8)
                            C24_trk_FX_Name1.SwitchActive.Led = True
                            C24_trk_FX_Name2.LedSwitchActive.Led = True
                            C24_trk_FX_Name2.SwitchActive.Led = Fx.Enabled
                            SelectedFx = Fx
                        End If

                    End If

                End Sub
                iH.SelectBtnH = SelectBtnH
                AddHandler C24_trk_FX_Name1.SwitchActive.OnButtonReleased, SelectBtnH

                Dim BypassBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If C24_trk_FX_Name1.Vpot.ScribbleScriptsBlinking AndAlso SelectedFx IsNot Nothing Then
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




                Dim FxList_check = {"NoInsert"}
                If Fx IsNot Nothing Then
                    FxList_check = {_ReaperSessionConfig.GetPluginShortName(Fx.Name)}
                End If
                C24_trk_FX_Name1.Vpot.InitTextList(FxList, FxList_check)
                C24_trk_FX_Name1.Vpot.VpotMode = C24_Vpot.VpotModeType.TextList
                C24_trk_FX_Name1.Vpot.SelectItemByText(FxList_check.First)



                'Vpot handler

                Dim VpotSelectItemH As Hardware.C24_Vpot.OnVpotSelectedItemChangeEventHandler =
                        Sub(Track_Nb As Integer, selectedIndex As Integer)
                            C24_trk_FX_Name1.SwitchActive.IsBlinking = True
                            C24_trk_FX_Name1.LedSwitchActive.Led = True



                            'ajoute la validation si modification de choix dans la liste
                            C24_trk_FX_Name1.SwitchActive.IsBlinking = True
                            C24_trk_FX_Name1.LedSwitchActive.Led = True

                            Dim iih As New InsertStripHandler
                            Dim SelectBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                                    Sub(_TrackNb As Integer?)
                                        C24_trk_FX_Name1.Vpot.CheckAllItems(False)
                                        C24_trk_FX_Name1.Vpot.CheckSelectedItem(True)
                                        ModifyFxNames(Fx_index) = _ReaperSessionConfig.GetPluginFullName(C24_trk_FX_Name1.Vpot.GetCheckedItems.First)


                                        C24_trk_FX_Name1.SwitchActive.IsBlinking = False
                                        C24_trk_FX_Name1.LedSwitchActive.Led = False

                                        RemoveHandler C24_trk_FX_Name1.SwitchActive.OnButtonReleased, SelectBtnH
                                    End Sub
                            iih.SelectBtnH = SelectBtnH
                            AddHandler C24_trk_FX_Name1.SwitchActive.OnButtonReleased, SelectBtnH
                            _InsertStripHandlers.Add(iih)





                        End Sub
                iH.VpotSelectItemH = VpotSelectItemH
                AddHandler C24_trk_FX_Name1.Vpot.OnVpotSelectedItemChange, VpotSelectItemH


            End If


            _InsertStripHandlers.Add(iH)

        Next
        'RefreshBypassLed(_ReaperTrackFxEditor, InsertPageNb)
        '----------------- Master Bypass ------------------------------------------
        RefreshMasterBypassLed(_ReaperTrackFxEditor)

        If Assign_Mode = False Then
            Dim iih As New InsertStripHandler
            Dim BypMasterBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    _ReaperTrackFxEditor.Refresh()
                    Dim enableAll = _ReaperTrackFxEditor.GetFxList().Any(Function(f) Not f.IsEnabled)
                    For idx = 0 To _ReaperTrackFxEditor.Count - 1
                        _ReaperTrackFxEditor.SetFxEnabled(idx, enableAll)
                    Next
                    RefreshC24InsertViewMode(TrackNb, InsertPageNb)
                End Sub
            iih.BypMasterBtnH = BypMasterBtnH
            AddHandler C24_Desk.Master_Bypass.OnButtonReleased, BypMasterBtnH
            _InsertStripHandlers.Add(iih)
        End If
    End Sub
    Private Sub RemoveInsertStripHandlers()
        RemoveFadersVolumeHandlers()
        RemoveAssignHandlers()
        RemovePageHandlers()

        For Each h In _InsertStripHandlers
            If h.C24_trk_FX_Name1 IsNot Nothing Then
                RemoveHandler h.C24_trk_FX_Name1.SwitchActive.OnButtonReleased, h.SelectBtnH
                RemoveHandler h.C24_trk_FX_Name1.Vpot.OnVpotSelectedItemChange, h.VpotSelectItemH
            End If

            If h.C24_trk_FX_Name2 IsNot Nothing Then
                RemoveHandler h.C24_trk_FX_Name2.SwitchActive.OnButtonReleased, h.BypassBtnH
            End If

            If h.BypMasterBtnH IsNot Nothing Then
                RemoveHandler C24_Desk.Master_Bypass.OnButtonReleased, h.BypMasterBtnH
            End If


        Next


        _InsertStripHandlers.Clear()
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
    Private Sub AddFxParamStripHandlers(Fx As ReaperFX, _FxParamPageNb As Integer)
        RemoveFxParamStripHandlers()

        ClearC24_MiddleStrip()

        Fx.Refresh()

        Dim FxH As New FxParamStripHandlers

        ParamIndexToTrackIndex = New Dictionary(Of Integer, Integer)

        If _ReaperFxParamMonitor IsNot Nothing Then
            _ReaperFxParamMonitor.ClearWatched()
            _ReaperFxParamMonitor.Stop()
            _ReaperFxParamMonitor.Dispose()
        End If
        _ReaperFxParamMonitor = New ReaperFxParamMonitor(Fx.TrackIndex, Fx.FxIndex)
        _ReaperFxParamMonitor.Start()
        _ReaperFxParamMonitor.ClearWatched()
        _ReaperFxParamMonitor.WatchParams(0, Fx.ParamCount)

        If _ReaperFxParamEnvelopeMonitor IsNot Nothing Then
            _ReaperFxParamEnvelopeMonitor.ClearWatched()
            _ReaperFxParamEnvelopeMonitor.Stop()
            _ReaperFxParamEnvelopeMonitor.Dispose()
        End If
        _ReaperFxParamEnvelopeMonitor = New ReaperFxParamEnvelopeMonitor(Fx.TrackIndex, Fx.FxIndex)
        _ReaperFxParamEnvelopeMonitor.Start()
        _ReaperFxParamEnvelopeMonitor.ClearWatched()
        _ReaperFxParamEnvelopeMonitor.WatchParams(0, Fx.ParamCount)

        For Each FxparamEnv In _ReaperTracksManager.Tracks(_ReaperTracksManager.GetRelativeTrackIndex(Fx.TrackIndex)).Envelopes.AllOfType(Of ReaperFxParamEnvelope)
            If FxparamEnv.FxIndex = Fx.FxIndex Then
                Dim fh As New FxparamEnvHandler
                fh.FxparamEnv = FxparamEnv
                AddHandler _ReaperFxParamEnvelopeMonitor.On_ValueChange, AddressOf FxparamEnv.SetValueFromMonitor
                _FxparamEnvHandlers.Add(fh)
            End If
        Next


        SelectedFx = Fx
        FxParamPageNb = _FxParamPageNb
        '--------------------------- Inserts_Param Handler----------------------------------
        If NeedInsertParam Then

            'init
            C24_Desk.Inserts_Param.IsBlinking = True
            'add handler
            Dim InsertParamBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                                Sub()
                                    If C24_Desk.Inserts_Param.IsBlinking = True Then
                                        C24_Desk.Inserts_Param.IsBlinking = False

                                        RefreshC24InsertViewMode(SelectedFx.TrackIndex, InsertViewMode_PageNb)
                                        'SelectedFx = Fx
                                    End If
                                End Sub
            FxH.InsertParamBtnH = InsertParamBtnH
            AddHandler C24_Desk.Inserts_Param.OnButtonReleased, InsertParamBtnH

        End If
        '------------Bank Handlers-------------------------------------------------
        AddBankHandlers()


        '------------Page Handlers-------------------------------------------------
        Dim h As New PageHandlers

        ' ── LEDs page ─────────────────────────────────────────────────────────
        C24_Desk.Cbs_Right.IsBlinking = False
        C24_Desk.Cbs_Left.IsBlinking = False

        ' ── Page suivante : visible si pas sur la dernière page ───────────────
        If FxParamPageNb < FxParamTotalPages Then
            C24_Desk.Cbs_Right.IsBlinking = True

            Dim PageRightBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()

                    RefreshC24FxParamViewMode(SelectedFx, FxParamPageNb + 1)
                End Sub
            h.PageRightBtnH = PageRightBtnH
            AddHandler C24_Desk.Cbs_Right.OnButtonReleased, PageRightBtnH
        End If

        ' ── Page précédente : visible si pas sur la première page ─────────────
        If FxParamPageNb > 1 Then
            C24_Desk.Cbs_Left.IsBlinking = True

            Dim PageLeftBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    RefreshC24FxParamViewMode(SelectedFx, FxParamPageNb - 1)
                End Sub
            h.PageLeftBtnH = PageLeftBtnH
            AddHandler C24_Desk.Cbs_Left.OnButtonReleased, PageLeftBtnH
        End If

        _PageHandlers.Add(h)



        '----------------- Flip Handler -------------------------------------------------

        Dim _RefreshViewMode As [Delegate] = Sub()
                                                 RefreshC24FxParamViewMode(SelectedFx, FxParamPageNb)
                                             End Sub
        AddFlipHandlers(_RefreshViewMode)

        '----------------- Auto/Select Mode Handler -------------------------------------------------
        C24_Desk.Auto_Select.Led = AutoSelect_Mode
        Dim AutoSelectBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub()
                C24_Desk.Auto_Select.Led = Not C24_Desk.Auto_Select.Led
                AutoSelect_Mode = Not AutoSelect_Mode
                RefreshC24FxParamViewMode(SelectedFx, FxParamPageNb)
            End Sub
        FxH.AutoSelectBtnH = AutoSelectBtnH
        AddHandler C24_Desk.Auto_Select.OnButtonReleased, AutoSelectBtnH
        _FxParamStripHandlers.Add(FxH)
        '----------------- Compare Handler -------------------------------------------------
        If AutoSelect_Mode = False Then

            'init compare
            CompareAB = New FxSettingManager(SelectedFx)
            CompareAB.InitCompareAB()
            C24_Desk.Compare.IsBlinking = True
            Dim CompareBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If CompareAB.CompareAB = FxSettingManager.CompareMode.A Then
                        C24_Desk.Compare.IsBlinking = False
                        C24_Desk.Compare.Led = True
                    Else
                        C24_Desk.Compare.IsBlinking = True
                    End If

                End Sub
            FxH.CompareBtnH = CompareBtnH
            AddHandler C24_Desk.Compare.OnButtonReleased, CompareBtnH

        End If
        '----------------- MasterBypass Handler -------------------------------------------------
        If AutoSelect_Mode = False Then

            'init MasterBypass
            If SelectedFx.Enabled Then
                C24_Desk.Master_Bypass.IsBlinking = False
            Else
                C24_Desk.Master_Bypass.Led = True
            End If

            Dim Master_BypassBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    SelectedFx.Enabled = Not SelectedFx.Enabled
                    If SelectedFx.Enabled Then
                        C24_Desk.Master_Bypass.IsBlinking = False
                    Else
                        C24_Desk.Master_Bypass.IsBlinking = False
                        C24_Desk.Master_Bypass.Led = True
                    End If

                End Sub
            FxH.Master_BypassBtnH = Master_BypassBtnH
            AddHandler C24_Desk.Master_Bypass.OnButtonReleased, Master_BypassBtnH

        End If

        '----------------- Display + FxParamHandlers -------------------------------------------------

        Fx.IsOpen = True

        'reload Track state chunk
        Dim Trk_guid As String = Nothing
        If Fx.TrackIndex <> -1 Then
            Trk_guid = _ReaperProj.TrackGuids(Fx.TrackIndex)
        Else
            Trk_guid = Reaper.utility.GetTrackGUID(Fx.TrackIndex)
        End If

        If TracksChunk.ContainsKey(Trk_guid) Then
            Reaper.SetTrackStateChunk(Fx.TrackIndex, TracksChunk(Trk_guid))
        End If

        'Force UI modification
        Dim vol As Double = 0
        If Fx.TrackIndex <> -1 Then
            vol = _ReaperProj.Tracks(Fx.TrackIndex).Vol
        Else
            vol = _ReaperProj.MasterTrack.Vol
        End If
        Reaper.SetTrackUIVolume(Fx.TrackIndex, vol)
        Fx.Refresh()


        ' ── Configurer Faders/Vpots  ──────────

        If Flip_Mode Then
            AddFaderFxParamHandlers()
            'AddVpotVolumeHandlers(, False)
        Else
            AddVpotFxParamHandlers()
            AddFadersVolumeHandlers(, False)
        End If

        If AutoSelect_Mode Then
            AddAutoSelectFxParamHandlers()
        End If



    End Sub
    Sub RemoveFxParamStripHandlers()
        RemoveBankHandlers()
        RemoveFlipHandlers()
        RemovePageHandlers()
        RemoveFadersVolumeHandlers()
        RemoveVpotVolumeHandlers()
        RemoveFaderFxParamHandlers()
        RemoveVpotFxParamHandlers()
        RemoveAutoSelectFxParamHandlers()
        RemoveFxParamHandlers()

        For Each h In _FxParamStripHandlers
            RemoveHandler C24_Desk.Auto_Select.OnButtonReleased, h.AutoSelectBtnH
            RemoveHandler C24_Desk.Inserts_Param.OnButtonReleased, h.InsertParamBtnH
            If h.CompareBtnH IsNot Nothing Then
                RemoveHandler C24_Desk.Compare.OnButtonReleased, h.CompareBtnH
                C24_Desk.Compare.IsBlinking = False
                CompareAB.Dispose()
            End If
            If h.Master_BypassBtnH IsNot Nothing Then
                RemoveHandler C24_Desk.Master_Bypass.OnButtonReleased, h.Master_BypassBtnH
                C24_Desk.Master_Bypass.IsBlinking = False
            End If
            C24_Desk.Inserts_Param.IsBlinking = False
        Next




        ' ── Arrêter et libérer le monitor FxParam ─────────────────────
        If _ReaperFxParamMonitor IsNot Nothing Then
            _ReaperFxParamMonitor.ClearWatched()
            _ReaperFxParamMonitor.Stop()
            _ReaperFxParamMonitor.Dispose()
            _ReaperFxParamMonitor = Nothing
        End If
        ' ── Arrêter et libérer le monitor FxParamEnvelope ─────────────────────
        If _ReaperFxParamEnvelopeMonitor IsNot Nothing Then
            _ReaperFxParamEnvelopeMonitor.ClearWatched()
            _ReaperFxParamEnvelopeMonitor.Stop()
            _ReaperFxParamEnvelopeMonitor.Dispose()
            _ReaperFxParamEnvelopeMonitor = Nothing
        End If

        _FxParamStripHandlers.Clear()
        _C24trkPluginParamEnvelope.Clear()
    End Sub
    Sub RemoveFxParamHandlers()
        For Each fh In _FxparamEnvHandlers
            RemoveHandler _ReaperFxParamEnvelopeMonitor.On_ValueChange, AddressOf fh.FxparamEnv.SetValueFromMonitor
        Next
        _FxparamEnvHandlers.Clear()
    End Sub
#End Region

#Region "Sends"
    Private Sub AddSendsStripHandlers(SendIndex As Integer, SendLetter As String)
        RemoveSendsStripHandlers()

        If _ReaperTracksSendManager IsNot Nothing Then
            _ReaperTracksSendManager.Dispose()
            _ReaperTracksSendManager = Nothing
        End If

        _ReaperTracksSendManager = New ReaperTracksSendManager(_ReaperTracksManager.Tracks(), _ReaperProj.Tracks, SendLetter)

        If Assign_Mode = False Then
            'Force PrePost Mode
            If Flip_Mode AndAlso SendMute_Mode Then
                SendMute_Mode = False
                PrePost_Mode = True
                C24_Desk.Pre_Post.Led = True
                C24_Desk.Send_Mute.Led = False
            End If
        End If


        '------------Bank Handlers-------------------------------------------------
        AddBankHandlers()
        '--------------------------- Sends Mode----------------------------------
        Dim _sh As New SendsStripHandlers
        SetSendLetterLed(SendLetter)


        '----------------- Flip Handler -------------------------------------------------
        If Assign_Mode Then
            Flip_Mode = False
        Else
            Dim _RefreshViewMode As [Delegate] = Sub()

                                                     RefreshC24SendsViewMode()
                                                 End Sub
            AddFlipHandlers(_RefreshViewMode)
        End If



        '----------------- Pre/Post Handlers -------------------------------------------------

        If Assign_Mode = False Then
            'init
            C24_Desk.Pre_Post.Led = PrePost_Mode
            If PrePost_Mode Then
                For i = 0 To _ReaperTracksManager.Tracks.Count - 1
                    Dim ReaperTrk = _ReaperTracksManager.Tracks(i)

                    If SendIndex > _ReaperTracksSendManager.Sends.Count - 1 Then Continue For
                    Dim send = _ReaperTracksSendManager.Sends(SendIndex)
                    If send Is Nothing Then Continue For
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

        End If

        '----------------- SendMute Handlers -------------------------------------------------

        If Assign_Mode = False Then
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
        End If

        '--------------- Assign Handlers ----------------------------------------------------------




        Dim ah As New AssignHandlers

        'init leds
        If Assign_Mode = True Then
            C24_Desk.Assign.IsBlinking = True
            C24_Desk.Esc_Group.SetAllBlinking(True)

        Else
            C24_Desk.Assign.IsBlinking = False
            C24_Desk.Esc_Group.SetAllBlinking(False)
            C24_Desk.Esc_Group.SetAllOff()
        End If


        'add handlers

        If Assign_Mode = True Then

            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    'return to no assign mode
                    Assign_Mode = False
                    RefreshC24SendsViewMode()

                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH

            Dim EscBtnH As Hardware.C24_GroupLedButton.OnGroupButtonReleasedEventHandler =
                            Sub()
                                'quit Assign Mode 

                                Assign_Mode = False
                                RefreshC24SendsViewMode()

                            End Sub
            ah.EscBtnH = EscBtnH
            AddHandler C24_Desk.Esc_Group.OnGroupButtonReleased, EscBtnH

        Else

            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    'Entrer en mode Assign
                    Assign_Mode = True
                    RefreshC24SendsViewMode()

                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH



        End If


        _AssignHandlers.Add(ah)



        '----------------- Display + SendsHandlers -------------------------------------------------



        ClearC24_MiddleStrip()

        ' ── Configurer Faders/Vpots  ──────────



        If Flip_Mode Then

            AddFadersSendVolumeHandlers(True)
            AddVpotSendPanHandlers()

        Else 'Mode normal

            If Assign_Mode = False Then
                AddFadersVolumeHandlers(False)
                AddVpotSendVolumeHandlers(True)
                AddSolosHandlers()
                AddSolosClearSafeHandlers()
            Else
                AddVpotSendVolumeHandlers(True)
            End If



        End If

        If Assign_Mode = False Then
            AddSendMutesBtnHandlers() 'Flip mode géré dans le SendMutesBtnHandlers

            If PrePost_Mode Then
                AddSendPrePostBtnHandlers() 'idem
            End If
        End If


    End Sub
    Sub RemoveSendsStripHandlers()
        RemoveBankHandlers()
        RemoveFlipHandlers()
        RemoveVpotSendVolumeHandlers()
        RemoveFadersVolumeHandlers()
        RemoveMutesBtnHandlers()
        RemoveSolosHandlers()
        RemoveSolosClearSafeHandlers()
        RemoveFadersSendVolumeHandlers()
        RemoveVpotSendPanHandlers()
        RemoveSendMutesBtnHandlers()
        RemoveSendPrePostBtnHandlers()
        RemoveAssignHandlers()

        C24_Desk.Pre_Post.IsBlinking = False
        C24_Desk.Send_Mute.IsBlinking = False

        For Each h In _SendsStripHandlers
            RemoveHandler C24_Desk.Send_Mute.OnButtonReleased, h.SendMuteBtnH
            RemoveHandler C24_Desk.Pre_Post.OnButtonReleased, h.PrePostBtnH
        Next

        If _ReaperTracksSendManager IsNot Nothing Then
            _ReaperTracksSendManager.Dispose()
        End If


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



#Region "Inputs/Outputs"
    Sub AddOutputsStripHandlers(Optional ReturnToC24View As C24ViewModes = C24ViewModes.Home)
        RemoveOutputsStripHandlers()



        '------------Bank Handlers-------------------------------------------------
        AddBankHandlers()
        '--------------- Assign Handlers ----------------------------------------------------------

        Dim ah As New AssignHandlers

        'init leds
        If Assign_Mode = True Then
            C24_Desk.Assign.IsBlinking = True
            C24_Desk.Esc_Group.SetAllBlinking(True)

        Else
            C24_Desk.Assign.IsBlinking = False
            C24_Desk.Esc_Group.SetAllBlinking(False)
            C24_Desk.Esc_Group.SetAllOff()
        End If


        'add handlers

        If Assign_Mode = True Then

            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    'return to no assign mode


                    Assign_Mode = False
                    RefreshC24OutputViewMode(ReturnToC24View)

                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH

            Dim EscBtnH As Hardware.C24_GroupLedButton.OnGroupButtonReleasedEventHandler =
                            Sub()
                                'quit Assign Mode 

                                Assign_Mode = False
                                RefreshC24OutputViewMode(ReturnToC24View)

                            End Sub
            ah.EscBtnH = EscBtnH
            AddHandler C24_Desk.Esc_Group.OnGroupButtonReleased, EscBtnH

        Else

            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    'Entrer en mode Assign
                    Assign_Mode = True
                    RefreshC24OutputViewMode(ReturnToC24View)

                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH



        End If


        _AssignHandlers.Add(ah)

        '----------------- Output Handler -------------------------------------------------
        Dim hh As New OutputsStripHandler
        Dim OutputBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If C24_Desk.Output.IsBlinking Then
                        C24_Desk.Output.IsBlinking = False
                        Assign_Mode = False
                        C24_Desk.Assign.IsBlinking = False
                        C24_Desk.Esc_Group.SetAllBlinking(False)

                        RefreshC24ViewMode(ReturnToC24View)
                    End If

                End Sub
        hh.OutputBtnH = OutputBtnH
        AddHandler C24_Desk.Output.OnButtonReleased, OutputBtnH
        _OutputsStripHandlers.Add(hh)
        '----------------- Display Outputs -------------------------------------------------

        ClearC24_MiddleStrip()



        For i = 0 To 23
            Dim h As New OutputsStripHandler
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)


                Dim _ReaperTrackOutputsManager = New ReaperTrackOutputsManager(_ReaperTracksManager.Tracks)

                h._ReaperTrackOutputsManager = _ReaperTrackOutputsManager
                Dim _CurrentOutputs = _ReaperTrackOutputsManager.Get_CurrentOutputs(i)
                If Assign_Mode = False Then
                    'init 

                    If _CurrentOutputs.Count > 0 Then
                        c24Trk.ScribbleStrip.Text = _ReaperTrackOutputsManager.Get_CurrentOutput(i, 0).Name
                        If _CurrentOutputs.Count > 1 Then
                            c24Trk.SwitchActive.Led = True
                        End If
                    Else
                        c24Trk.ScribbleStrip.Text = " -  "
                    End If


                Else
                    'assign mode
                    Dim OutputList As List(Of String) = Nothing
                    Dim checkList As New List(Of String)
                    If _CurrentOutputs.Count = 0 Then
                        OutputList = _ReaperTrackOutputsManager.GetVpotSendsList(i)

                    Else
                        OutputList = _ReaperTrackOutputsManager.GetVpotSendsList(i, Nothing)

                    End If
                    checkList.Add(OutputList.First)
                    c24Trk.Vpot.ChannelScribbleScreen = C24_Desk.ScreenChannelBarScroll
                    c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.TextListWithChannelBar
                    c24Trk.Vpot.InitTextList(OutputList, checkList)
                    c24Trk.Vpot.SelectItemByText(checkList.First)
                    c24Trk.LedSwitchActive.Led = False


                    h.c24Trk = c24Trk
                    Dim VpotSelectItemH As Hardware.C24_Vpot.OnVpotSelectedItemChangeEventHandler =
                      Sub(Track_Nb As Integer, selectedIndex As Integer)
                          If c24Trk.LedSwitchActive.Led = False Then
                              'ajoute la validation si modification de choix dans la liste
                              c24Trk.SwitchActive.IsBlinking = True
                              c24Trk.LedSwitchActive.Led = True

                              Dim ssh As New OutputsStripHandler
                              ssh.c24Trk = c24Trk
                              Dim ValidSendBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                                  Sub(_TrackNb As Integer?)
                                      c24Trk.Vpot.CheckAllItems(False)
                                      c24Trk.Vpot.CheckSelectedItem(True)
                                      Dim NewSendName = c24Trk.Vpot.GetCheckedItems.First


                                      If NewSendName.StartsWith("+") Then
                                          'ajoute un output
                                          NewSendName = NewSendName.Remove(0, 1)

                                          Dim SendId As Integer = -1
                                          If _ReaperTrackOutputsManager.AllOutputsNames.Contains(NewSendName) Then
                                              SendId = _ReaperTrackOutputsManager.AllOutputsNames.IndexOf(NewSendName)
                                          End If

                                          If SendId <> -1 Then
                                              _ReaperTrackOutputsManager.AddOutput(_TrackNb - 1, SendId)

                                          End If

                                      ElseIf NewSendName.StartsWith("-") Then
                                          'retire un output
                                          NewSendName = NewSendName.Remove(0, 1)

                                          Dim SendId As Integer = _ReaperTrackOutputsManager.GetOutputId(Track_Nb - 1, NewSendName)

                                          If SendId <> -1 Then
                                              _ReaperTrackOutputsManager.RemoveOutput(_TrackNb - 1, SendId)

                                          End If
                                      Else
                                          'ne fait rien 

                                      End If



                                      c24Trk.SwitchActive.IsBlinking = False
                                      c24Trk.LedSwitchActive.Led = False

                                      RemoveHandler c24Trk.SwitchActive.OnButtonReleased, ValidSendBtnH
                                  End Sub
                              ssh.ValidSendBtnH = ValidSendBtnH
                              AddHandler c24Trk.SwitchActive.OnButtonReleased, ValidSendBtnH
                              _OutputsStripHandlers.Add(ssh)

                          End If

                      End Sub
                    h.VpotSelectItemH = VpotSelectItemH
                    AddHandler c24Trk.Vpot.OnVpotSelectedItemChange, VpotSelectItemH
                    _OutputsStripHandlers.Add(h)


                End If


            End If

        Next





    End Sub
    Sub RemoveOutputsStripHandlers()
        RemoveBankHandlers()
        RemoveAssignHandlers()

        For Each h In _OutputsStripHandlers
            If h.c24Trk IsNot Nothing Then
                RemoveHandler h.c24Trk.Vpot.OnVpotSelectedItemChange, h.VpotSelectItemH
                RemoveHandler h.c24Trk.SwitchActive.OnButtonReleased, h.ValidSendBtnH
            End If

            If h._ReaperTrackOutputsManager IsNot Nothing Then
                h._ReaperTrackOutputsManager.Dispose()
            End If

            RemoveHandler C24_Desk.Output.OnButtonReleased, h.OutputBtnH
        Next




        _OutputsStripHandlers.Clear()

    End Sub
    Sub AddInputsStripHandlers()
        RemoveInputsStripHandlers()



        '------------Bank Handlers-------------------------------------------------
        AddBankHandlers()
        '--------------- Assign Handlers ----------------------------------------------------------

        Dim ah As New AssignHandlers

        'init leds
        If Assign_Mode = True Then
            C24_Desk.Assign.IsBlinking = True
            C24_Desk.Esc_Group.SetAllBlinking(True)

        Else
            C24_Desk.Assign.IsBlinking = False
            C24_Desk.Esc_Group.SetAllBlinking(False)
            C24_Desk.Esc_Group.SetAllOff()
        End If


        'add handlers

        If Assign_Mode = True Then

            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    'return to no assign mode


                    Assign_Mode = False
                    RefreshC24InputViewMode()

                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH

            Dim EscBtnH As Hardware.C24_GroupLedButton.OnGroupButtonReleasedEventHandler =
                            Sub()
                                'quit Assign Mode 

                                Assign_Mode = False
                                RefreshC24InputViewMode()

                            End Sub
            ah.EscBtnH = EscBtnH
            AddHandler C24_Desk.Esc_Group.OnGroupButtonReleased, EscBtnH

        Else

            Dim AssignBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    'Entrer en mode Assign
                    Assign_Mode = True
                    RefreshC24InputViewMode()

                End Sub
            ah.AssignBtnH = AssignBtnH
            AddHandler C24_Desk.Assign.OnButtonReleased, AssignBtnH



        End If


        _AssignHandlers.Add(ah)

        '----------------- Input Handler -------------------------------------------------
        Dim hh As New InputsStripHandler
        Dim InputBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If C24_Desk.Input.IsBlinking Then
                        C24_Desk.Input.IsBlinking = False
                        Assign_Mode = False
                        C24_Desk.Assign.IsBlinking = False
                        C24_Desk.Esc_Group.SetAllBlinking(False)

                        RefreshC24HomeViewMode()
                    End If

                End Sub
        hh.InputBtnH = InputBtnH
        AddHandler C24_Desk.Input.OnButtonReleased, InputBtnH
        _InputsStripHandlers.Add(hh)
        '----------------- Display Outputs -------------------------------------------------

        ClearC24_MiddleStrip()



        For i = 0 To 23
            Dim h As New InputsStripHandler
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)


                Dim _ReaperTrackInputsManager = New ReaperTrackInputsManager(_ReaperTracksManager.Tracks)
                Dim InputList As New List(Of String)
                Dim Inputs = _ReaperTrackInputsManager.AllInputs
                h._ReaperTrackInputsManager = _ReaperTrackInputsManager
                Dim _CurrentInput = _ReaperTrackInputsManager.Get_CurrentInput(i)
                If Assign_Mode = False Then
                    'init 
                    c24Trk.ScribbleStrip.Text = _CurrentInput.Name
                Else
                    'assign mode

                    For Each _Input In Inputs
                        InputList.Add(_Input.Name)
                    Next
                    Dim checkList As New List(Of String)
                    checkList.Add(_CurrentInput.Name)

                    c24Trk.Vpot.ChannelScribbleScreen = C24_Desk.ScreenChannelBarScroll
                    c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.TextListWithChannelBar
                    c24Trk.Vpot.InitTextList(InputList, checkList)
                    c24Trk.Vpot.SelectItemByText(checkList.First)
                    c24Trk.LedSwitchActive.Led = False


                    h.c24Trk = c24Trk
                    Dim VpotSelectItemH As Hardware.C24_Vpot.OnVpotSelectedItemChangeEventHandler =
                      Sub(Track_Nb As Integer, selectedIndex As Integer)
                          If c24Trk.LedSwitchActive.Led = False Then
                              'ajoute la validation si modification de choix dans la liste
                              c24Trk.SwitchActive.IsBlinking = True
                              c24Trk.LedSwitchActive.Led = True

                              Dim ssh As New InputsStripHandler
                              ssh.c24Trk = c24Trk
                              Dim ValidSendBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                                  Sub(_TrackNb As Integer?)

                                      c24Trk.Vpot.CheckAllItems(False)
                                      c24Trk.Vpot.CheckSelectedItem(True)
                                      Dim NewSendName = c24Trk.Vpot.GetCheckedItems.First


                                      If NewSendName <> _CurrentInput.Name Then
                                          'modifie l input
                                          Dim InputIndex = InputList.IndexOf(NewSendName)
                                          _ReaperTrackInputsManager.Set_InputByIndex(_TrackNb - 1, InputIndex)

                                          'Force UI modification
                                          Reaper.SetTrackUIVolume(reaperTrk.TrackIndex, reaperTrk.Vol)
                                          'Save Chunk

                                          Dim Trk_guid = _ReaperProj.TrackGuids(reaperTrk.TrackIndex)
                                          If TracksChunk.ContainsKey(Trk_guid) Then
                                              TracksChunk(Trk_guid) = Reaper.GetTrackStateChunk(reaperTrk.TrackIndex)
                                          Else
                                              TracksChunk.Add(Trk_guid, Reaper.GetTrackStateChunk(reaperTrk.TrackIndex))
                                          End If
                                          c24Trk.Vpot.SelectItemByText(NewSendName)
                                      End If



                                      c24Trk.SwitchActive.IsBlinking = False
                                      c24Trk.LedSwitchActive.Led = False



                                      RemoveHandler c24Trk.SwitchActive.OnButtonReleased, ValidSendBtnH
                                  End Sub
                              ssh.ValidSendBtnH = ValidSendBtnH
                              AddHandler c24Trk.SwitchActive.OnButtonReleased, ValidSendBtnH
                              _InputsStripHandlers.Add(ssh)

                          End If

                      End Sub
                    h.VpotSelectItemH = VpotSelectItemH
                    AddHandler c24Trk.Vpot.OnVpotSelectedItemChange, VpotSelectItemH
                    _InputsStripHandlers.Add(h)


                End If


            End If

        Next





    End Sub
    Sub RemoveInputsStripHandlers()
        RemoveBankHandlers()
        RemoveAssignHandlers()

        For Each h In _InputsStripHandlers
            If h.c24Trk IsNot Nothing Then
                RemoveHandler h.c24Trk.Vpot.OnVpotSelectedItemChange, h.VpotSelectItemH
                RemoveHandler h.c24Trk.SwitchActive.OnButtonReleased, h.ValidSendBtnH
            End If

            If h._ReaperTrackInputsManager IsNot Nothing Then
                h._ReaperTrackInputsManager.Dispose()
            End If

            RemoveHandler C24_Desk.Input.OnButtonReleased, h.InputBtnH
        Next




        _InputsStripHandlers.Clear()

    End Sub
#End Region

#Region "Master"
    Private Sub AddMasterStripHandlers()


        RemoveMasterStripHandlers()

        _ReaperTracksManager.Set_MasterTrack()

        '--------------------------- Common Handlers----------------------------------
        AddCommonStripHandlers(C24ViewModes.Master)
        AddMutesBtnHandlers()
        AddSolosHandlers()
        RefreshTrackManagerPeaks()

        '----------------- Flip Handler -------------------------------------------------

        Dim _RefreshViewMode As [Delegate] = Sub()
                                                 RefreshC24HomeViewMode()
                                             End Sub
        AddFlipHandlers(_RefreshViewMode)

        ClearC24_MiddleStrip()
        '----------------- Master Handler -------------------------------------------------
        Dim h As New MasterStripHandler
        Dim MasterBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If C24_Desk.Master_Faders.IsBlinking = True Then
                        C24_Desk.Master_Faders.IsBlinking = False
                        _ReaperTracksManager.UnSet_MasterTrack()
                        RefreshC24HomeViewMode()
                    End If

                End Sub
        h.MasterBtnH = MasterBtnH
        AddHandler C24_Desk.Master_Faders.OnButtonReleased, MasterBtnH
        _MasterStripHandlers.Add(h)
        '----------------- Output Handler -------------------------------------------------
        Dim OutputBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                Sub()
                    If C24_Desk.Output.IsBlinking = False Then
                        C24_Desk.Output.IsBlinking = True
                        RefreshC24OutputViewMode(C24ViewModes.Master)
                    End If

                End Sub
        h.OutputBtnH = OutputBtnH
        AddHandler C24_Desk.Output.OnButtonReleased, OutputBtnH

        ' ── Configurer Faders/Vpots ──────────



        If Flip_Mode Then

            AddVpotVolumeHandlers(True)
            AddFadersPanHandlers()
        Else

            AddFadersVolumeHandlers(True)
            AddVpotPanHandlers()
        End If



    End Sub

    Sub RemoveMasterStripHandlers()

        RemoveFlipHandlers()
        RemoveFadersVolumeHandlers()
        RemoveVpotPanHandlers()
        RemoveVpotVolumeHandlers()
        RemoveFadersPanHandlers()

        For Each h In _MasterStripHandlers
            RemoveHandler C24_Desk.Master_Faders.OnButtonReleased, h.MasterBtnH
            RemoveHandler C24_Desk.Output.OnButtonReleased, h.OutputBtnH
        Next

        _MasterStripHandlers.Clear()
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



        ' ── Handlers communs ──────────────────────────────────────────────────

        RemoveAllCommonC24Function()
        '-----------------------------------

        'Remove Reaper all tracks Handlers
        RemoveReaperEnvArmedHandlers()
        RemoveReaperSoloHandlers()
        RemoveReaperAutoModeHandlers()

        RemoveCommonStripHandlers()
        RemoveFlipHandlers()


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
        If _ReaperProjectMonitor IsNot Nothing Then
            _ReaperProjectMonitor.Stop()
            _ReaperProjectMonitor.Dispose()
            _ReaperProjectMonitor = Nothing
        End If
        If _ReaperSession IsNot Nothing Then
            _ReaperSession.Stop()
            _ReaperSession.Dispose()
            _ReaperSession = Nothing
        End If

        RemoveProjectManagementHandler()
        RemoveSessionManagementHandler()

        ReaperPollHub.Instance.Stop()
        Reaper.Monitors.ReaperEventPipeMonitor.Stop()
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