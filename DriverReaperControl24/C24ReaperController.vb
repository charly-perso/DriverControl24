Imports System.Security.Cryptography.X509Certificates
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
    Private _ReaperProjectMonitor As Monitors.ReaperProjectMonitor
    Public FxParamValueChangeH As [Delegate]
    Private Flip_Mode As Boolean = False
    Private FlipBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler = Nothing

    Private SoloSafeTracks As New Dictionary(Of Integer, Boolean)
    Private SoloTracks As New Dictionary(Of Integer, Boolean)
    Private Names As New Dictionary(Of Integer, String)
    Private ParamIndexToTrackIndex As New Dictionary(Of Integer, Integer)
    Private TrkIndexEqFxListManagers As New Dictionary(Of Integer, EqFxListManager)
    Private TrkIndexDynFxListManagers As New Dictionary(Of Integer, DynFxListManager)

    ' ── Stockage des delegates Home pour tous les strips ───────────────
    Private _CommonHandlers As New List(Of StripHandlers)
    Private _HomeHandlers As New List(Of StripHandlers)
    Private _PanSendHandlers As New List(Of SendHandlers)

    ' ── Stockage des delegates par fonctions ───────────────────────────────────
    Private _FaderHandlers As New List(Of FaderHandlers)
    Private _FadersOnVpotHandlers As New List(Of FaderHandlers)
    Private _InsertParamBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler = Nothing
    Private _SolosHandlers As New List(Of SolosHandlers)
    Private _BankHandlers As New List(Of BankHandlers)
    Private _PageHandlers As New List(Of PageHandlers)
    Private _AssignHandlers As New List(Of AssignHandlers)
    Private _TransportHandlers As New List(Of TransportHandlers)
    Private _InsertHandlers As New List(Of InsertHandlers)
    Private _FxParamHandlers As New List(Of FxParamHandlers)
    Private _FlipFxParamHandlers As New List(Of FxParamHandlers)

    Private _Solo_Mode As Solo_Mode = Solo_Mode.Solo
    Private _ActualC24ViewMode As C24ViewModes = C24ViewModes.Home
    Private _LastC24ViewMode As C24ViewModes = C24ViewModes.None
    Private Assign_Mode As Boolean = False

    Private ActualFxEdited As ReaperFX
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
        Public MuteChangeH As [Delegate]
        Public MuteBtnH As [Delegate]
        Public SoloChangeH As [Delegate]
        Public SoloDefeatChangeH As [Delegate]
        Public SoloBtnH As [Delegate]
        Public RecArmChangeH As [Delegate]
        Public RecArmBtnH As [Delegate]
        Public SelectedChangeH As [Delegate]
        Public SelectBtnH As [Delegate]
        Public AutoModeChangeH As [Delegate]
        Public AutoModeBtnH As [Delegate]
        Public SendPanBtnH As [Delegate]
        Public SwitchActiveBtnH As [Delegate]
        Public EqBtnH As [Delegate]
        Public DynBtnH As [Delegate]
        Public InsertBtnH As [Delegate]
    End Class
    Private Class FaderHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip

        Public VolChangeH As [Delegate]
        Public FaderHumanH As [Delegate]
        Public VpotValueChangeH As [Delegate]
    End Class

    Private Class SolosHandlers

        Public SoloClearBtnH As [Delegate]
        Public SoloSafeBtnH As [Delegate]
    End Class
    Private Class BankHandlers

        Public BankLeftBtnH As [Delegate]
        Public BankRightBtnH As [Delegate]
        Public BankNudgeBtnH As [Delegate]

    End Class

    Private Class PageHandlers

        Public PageLeftBtnH As [Delegate]
        Public PageRightBtnH As [Delegate]
    End Class
    Private Class AssignHandlers

        Public AssignBtnH As [Delegate]
        Public EscBtnH As [Delegate]
    End Class

    Private Class SendHandlers
        Public ReaperSend As Reaper.ReaperSend

        Public C24_trk_SndX As Hardware.C24_ChannelStrip
        Public C24_trk_SndName As Hardware.C24_ChannelStrip
        Public C24_trk_SndPre As Hardware.C24_ChannelStrip
        Public C24_trk_LR As Hardware.C24_ChannelStrip

        Public SendVolChangeH As [Delegate]
        Public VpotValueH As [Delegate]
        Public VpotSelectItemH As [Delegate]
        Public SendMuteChangeH As [Delegate]
        Public SendMuteBtnH As [Delegate]
        Public SendModeChangeH As [Delegate]
        Public SendModeBtnH As [Delegate]

        Public SwitchActiveBtnH As [Delegate]
        Public switchActiveBtnH_Pan As [Delegate]
        Public vpotValueH_Pan As [Delegate]
        Public switchActiveBtnH_PanEnter As [Delegate]
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

        RemoveTransportHandlers()
        _ReaperTransportManager = New Reaper.Managers.ReaperTransportManager
        _ReaperTransportManager.Start()

        ' ── Créer et démarrer le ProjectMonitor ───────────────────────────────
        _ReaperProjectMonitor = New Monitors.ReaperProjectMonitor()
        AddHandler _ReaperProjectMonitor.On_TrackCountChanged, AddressOf OnProjectMonitor_TrackCountChanged
        _ReaperProjectMonitor.Start()

        RefreshTrackManagerPage()
        AddAllCommonC24Function()
    End Sub







#Region "Common"

    Private Sub SetCommonStripHandlers()

        '-------------------------Common Handlers------------------------------
        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)



                Dim h As New StripHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


                c24Trk.Mute.Led = reaperTrk.Mute
                Dim muteChangeH As Reaper.ReaperTrack.On_MuteChangeEventHandler =
                    Sub(mute As Boolean)
                        MuteLedChange(reaperTrk.TrackIndex, mute)
                    End Sub
                h.MuteChangeH = muteChangeH
                AddHandler reaperTrk.On_MuteChange, muteChangeH

                Dim muteBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        _ReaperTracksManager.Set_MuteStatus(trackNb - 1, Not reaperTrk.Mute)
                        MuteLedChange(reaperTrk.TrackIndex, reaperTrk.Mute)
                    End Sub
                h.MuteBtnH = muteBtnH
                AddHandler c24Trk.Mute.OnButtonReleased, muteBtnH

                Dim soloChangeH As Reaper.ReaperTrack.On_SoloChangeEventHandler =
                    Sub(solo As Integer)
                        SoloTracks(reaperTrk.TrackIndex) = Not SoloTracks(reaperTrk.TrackIndex)
                        Select Case GetC24Solo(solo)
                            Case C24_SoloTypes.Solo
                                c24Trk.Solo.Led = True
                            Case C24_SoloTypes.SoloSafe
                                SoloSafeTracks(reaperTrk.TrackIndex) = True
                                c24Trk.Solo.Led = False
                            Case C24_SoloTypes.NotSolo
                                SoloSafeTracks(reaperTrk.TrackIndex) = False
                                c24Trk.Solo.Led = False
                        End Select
                        SetSoloMutesLed()
                        RefreshSoloClear()
                    End Sub
                h.SoloChangeH = soloChangeH
                AddHandler reaperTrk.On_SoloChange, soloChangeH

                Dim soloDefeatChangeH As Reaper.ReaperTrack.On_SoloDefeatChangeEventHandler =
                    Sub(solo As Boolean)
                        SoloSafeTracks(reaperTrk.TrackIndex) = solo

                        SetSoloMutesLed()
                        RefreshSoloClear()
                    End Sub
                h.SoloDefeatChangeH = soloDefeatChangeH
                AddHandler reaperTrk.On_SoloDefeatChange, soloDefeatChangeH

                Dim soloBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler = GetSoloBtnH(_Solo_Mode, reaperTrk.TrackIndex)
                h.SoloBtnH = soloBtnH
                AddHandler c24Trk.Solo.OnButtonReleased, soloBtnH

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
                Dim autoModeChangeH As Reaper.ReaperTrack.On_AutoModeChangeEventHandler =
                    Sub(autoMode As Integer)
                        Select Case autoMode
                            Case 0
                                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.None
                            Case 1
                                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Read
                            Case 2
                                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Touch
                            Case 3
                                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Write
                            Case 4
                                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Latch
                        End Select
                    End Sub
                h.AutoModeChangeH = autoModeChangeH
                AddHandler reaperTrk.On_AutoModeChange, autoModeChangeH

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
        InitSoloLed()
    End Sub
    Sub RefreshInsertModeLed()

        For Each trk In _ReaperTracksManager.Tracks
            Dim C24_cs = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(trk.TrackIndex))
            C24_cs.InsertMode.IsBlinking = False
            C24_cs.InsertMode.Led = Not _ReaperTracksManager.IsTrackGenericFxEmpty(trk.TrackIndex)
        Next
    End Sub
    Sub RefreshEqFxModeLed()

        For Each trk In _ReaperTracksManager.Tracks
            Dim C24_cs = C24_Desk.ChannelStrips(_ReaperTracksManager.GetRelativeTrackIndex(trk.TrackIndex))
            C24_cs.EqMode.IsBlinking = False
            C24_cs.EqMode.Led = Not _ReaperTracksManager.IsTrackEqFxEmpty(trk.TrackIndex)
        Next
    End Sub
    Public Sub RemoveCommonStripHandlers()

        For Each h In _CommonHandlers
            Dim r = h.ReaperTrk
            Dim c = h.C24Trk

            RemoveHandler r.On_MuteChange, h.MuteChangeH
            RemoveHandler c.Mute.OnButtonReleased, h.MuteBtnH
            RemoveHandler r.On_SoloChange, h.SoloChangeH
            RemoveHandler c.Solo.OnButtonReleased, h.SoloBtnH
            RemoveHandler r.On_RecArmChange, h.RecArmChangeH
            RemoveHandler c.RecArm.OnButtonReleased, h.RecArmBtnH
            RemoveHandler r.On_SelectedChange, h.SelectedChangeH
            RemoveHandler c.Select.OnButtonReleased, h.SelectBtnH
            RemoveHandler r.On_AutoModeChange, h.AutoModeChangeH
            RemoveHandler c.AutoMode.OnButtonReleased, h.AutoModeBtnH
            RemoveHandler c.SendPanMode.OnButtonReleased, h.SendPanBtnH

            RemoveHandler c.EqMode.OnButtonReleased, h.EqBtnH
            RemoveHandler c.DynMode.OnButtonReleased, h.DynBtnH
            RemoveHandler c.InsertMode.OnButtonReleased, h.InsertBtnH
        Next
        _CommonHandlers.Clear()
    End Sub

    Sub AddAllCommonC24Function()


        'Add Handlers
        AddSolosC24Handlers()
        AddBankC24Handlers()
        AddTransportHandlers()
        AddCounterModeHandler()

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

    Sub ClearC24_MiddleStrip()
        For Each C24_trk In C24_Desk.ChannelStrips
            C24_trk.ScribbleStrip.Text = "    "
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


    Sub SetFlipMode()
        If C24_Desk.Flip.Led = False Then
            'entre en mode flip
            C24_Desk.Flip.Led = True
            Flip_Mode = True
        Else 'retour au mode normal
            C24_Desk.Flip.Led = False
            Flip_Mode = False
        End If
    End Sub
    Sub RemoveFlipModeHandler()
        C24_Desk.Flip.Led = False
        Flip_Mode = False
        RemoveHandler C24_Desk.Flip.OnButtonReleased, FlipBtnH
    End Sub

#End Region
#Region "Faders"
    Sub SetFadersHandlers()
        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)



                Dim h As New FaderHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

                c24Trk.Fader.SetInitFaderDbVolume(Reaper.utility.SLIDER2DB(reaperTrk.Vol))

                Dim volChangeH As Reaper.ReaperTrack.On_VolChangeEventHandler =
                    Sub(vol As Double)
                        c24Trk.Fader.SetFaderDbVolume(Reaper.utility.SLIDER2DB(vol))
                    End Sub
                h.VolChangeH = volChangeH
                AddHandler reaperTrk.On_VolChange, volChangeH

                Dim faderHumanH As Hardware.C24_Fader.On_FaderHumanChangeDbValueEventHandler =
                    Sub(trackNb As Integer, dbValue As Double)
                        _ReaperTracksManager.SetVolume(trackNb - 1, Reaper.utility.DB2SLIDER(dbValue))
                        c24Trk.Fader.SetFaderDbVolume(dbValue)
                    End Sub
                h.FaderHumanH = faderHumanH
                AddHandler c24Trk.Fader.On_FaderHumanChangeDbValue, faderHumanH

                _FaderHandlers.Add(h)
            End If
        Next

    End Sub
    Sub SetFadersOnVpotHandlers()
        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)



                Dim h As New FaderHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

                c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.VolumeLinear
                c24Trk.Vpot.Value = reaperTrk.Vol

                Dim volChangeH As Reaper.ReaperTrack.On_VolChangeEventHandler =
                    Sub(vol As Double)
                        c24Trk.Vpot.Value = vol
                    End Sub
                h.VolChangeH = volChangeH
                AddHandler reaperTrk.On_VolChange, volChangeH

                Dim VpotValueChangeH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                    Sub(trackNb As Integer, Value As Double)
                        _ReaperTracksManager.SetVolume(trackNb - 1, Value)
                    End Sub
                h.VpotValueChangeH = VpotValueChangeH
                AddHandler c24Trk.Vpot.OnVpotValueChange, VpotValueChangeH

                _FadersOnVpotHandlers.Add(h)
            End If
        Next
    End Sub
    Sub RemoveFadersHandlers()
        For Each h In _FaderHandlers
            RemoveHandler h.C24Trk.Fader.On_FaderHumanChangeDbValue, h.FaderHumanH
            RemoveHandler h.ReaperTrk.On_VolChange, h.VolChangeH
        Next
        _FaderHandlers.Clear()
    End Sub
    Sub RemoveFadersOnVpotHandlers()
        For Each h In _FadersOnVpotHandlers
            RemoveHandler h.C24Trk.Vpot.OnVpotValueChange, h.VpotValueChangeH
            RemoveHandler h.ReaperTrk.On_VolChange, h.VolChangeH
        Next
        _FadersOnVpotHandlers.Clear()
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
#Region "Solo Mute"
    Sub AddSolosC24Handlers()
        RemoveSolosHandlers()
        Dim h As New SolosHandlers


        'Solo Clear
        Dim soloClearBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub(trackNb As Integer?)
                If C24_Desk.SoloClear.Led = False Then
                    Return
                End If
                For Each kvp In SoloTracks.ToList()
                    Dim absIndex As Integer = kvp.Key
                    If Not kvp.Value Then
                        Continue For
                    End If
                    Dim relIdx = _ReaperTracksManager.GetRelativeTrackIndex(absIndex)
                    If SoloSafeTracks(absIndex) = True Then
                        If relIdx IsNot Nothing Then
                            _ReaperTracksManager.Set_SoloStatus(relIdx, Reaper.SoloState.SafeSoloInPlace)
                            C24_Desk.ChannelStrips(relIdx).Solo.Led = False
                        Else
                            _ReaperProj.Tracks(absIndex).Solo = Reaper.SoloState.SafeSoloInPlace
                        End If
                    Else
                        If relIdx IsNot Nothing Then
                            _ReaperTracksManager.Set_SoloStatus(relIdx, Reaper.SoloState.Off)
                            C24_Desk.ChannelStrips(relIdx).Solo.Led = False
                        Else
                            _ReaperProj.Tracks(absIndex).Solo = Reaper.SoloState.Off
                        End If
                    End If
                    SoloTracks(absIndex) = False
                Next
                C24_Desk.SoloClear.Led = False
                SetSoloMutesLed()
            End Sub
        h.SoloClearBtnH = soloClearBtnH
        AddHandler C24_Desk.SoloClear.OnButtonReleased, soloClearBtnH

        'Solo Safe

        Dim soloSafeBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
            Sub(trackNb As Integer?)
                If C24_Desk.SoloSafe.IsBlinking = False Then
                    _Solo_Mode = Solo_Mode.SoloSafe
                    C24_Desk.SoloSafe.IsBlinking = True

                Else
                    _Solo_Mode = Solo_Mode.Solo
                    C24_Desk.SoloSafe.IsBlinking = False

                End If
                ' ── Swap uniquement le SoloBtnH sur chaque strip actif ────────
                For Each _h In _CommonHandlers
                    RemoveHandler _h.C24Trk.Solo.OnButtonReleased, _h.SoloBtnH
                    Dim newSoloBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        GetSoloBtnH(_Solo_Mode, _h.ReaperTrk.TrackIndex)
                    _h.SoloBtnH = newSoloBtnH
                    AddHandler _h.C24Trk.Solo.OnButtonReleased, newSoloBtnH
                Next

                InitSoloLed()
                RefreshSoloSafe()
                SetSoloMutesLed()

            End Sub
        h.SoloSafeBtnH = soloSafeBtnH
        AddHandler C24_Desk.SoloSafe.OnButtonReleased, soloSafeBtnH

        _SolosHandlers.Add(h)
    End Sub
    Sub InitSoloLed()
        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)

                Select Case _Solo_Mode
                    Case Solo_Mode.Solo
                        c24Trk.Solo.IsBlinking = False
                        c24Trk.Solo.Led = SoloTracks(reaperTrk.TrackIndex)
                    Case Solo_Mode.SoloSafe
                        c24Trk.Solo.Led = False
                        c24Trk.Solo.IsBlinking = SoloSafeTracks(reaperTrk.TrackIndex)
                End Select



            Else
                ClearChannelStripValues(c24Trk)
            End If
        Next

    End Sub
    Enum Solo_Mode
        Solo
        SoloSafe
    End Enum

    Private Function GetSoloBtnH(_Solo_Mode As Solo_Mode, TrackIndex As Integer) As Hardware.C24_Button.OnButtonReleasedEventHandler
        Dim soloBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler
        Select Case _Solo_Mode
            Case Solo_Mode.Solo

                soloBtnH =
                    Sub(trackNb As Integer?)
                        SoloTracks(TrackIndex) = Not SoloTracks(TrackIndex)
                        If SoloTracks(TrackIndex) = False AndAlso SoloSafeTracks(TrackIndex) = True Then
                            _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.SafeSoloInPlace)
                            C24_Desk.ChannelStrips(trackNb - 1).Solo.Led = False
                        ElseIf SoloTracks(TrackIndex) = False AndAlso SoloSafeTracks(TrackIndex) = False Then
                            _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.Off)
                            C24_Desk.ChannelStrips(trackNb - 1).Solo.Led = False
                        ElseIf SoloTracks(TrackIndex) = True Then
                            _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.SoloInPlace)
                            C24_Desk.ChannelStrips(trackNb - 1).Solo.Led = True
                        End If
                        SetSoloMutesLed()
                        RefreshSoloClear()
                    End Sub
                Return soloBtnH

            Case Solo_Mode.SoloSafe
                soloBtnH =
                    Sub(trackNb As Integer?)
                        SoloSafeTracks(TrackIndex) = Not SoloSafeTracks(TrackIndex)

                        If SoloSafeTracks(TrackIndex) = True Then
                            C24_Desk.ChannelStrips(trackNb - 1).Solo.IsBlinking = True
                            _ReaperTracksManager.Set_SoloDefeat(trackNb - 1, True)

                        Else
                            If SoloTracks(TrackIndex) Then
                                _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.SoloInPlace)
                            Else
                                _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.Off)
                            End If
                            C24_Desk.ChannelStrips(trackNb - 1).Solo.IsBlinking = False

                            _ReaperTracksManager.Set_SoloDefeat(trackNb - 1, False)
                        End If
                    End Sub
                Return soloBtnH
        End Select

        Return Nothing
    End Function

    ''' <summary>Supprime tous les handlers Solos du C24.</summary>
    Public Sub RemoveSolosHandlers()
        For Each h In _SolosHandlers
            RemoveHandler C24_Desk.SoloClear.OnButtonReleased, h.SoloClearBtnH
            RemoveHandler C24_Desk.SoloSafe.OnButtonReleased, h.SoloSafeBtnH
        Next
        _SolosHandlers.Clear()
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
        RefreshSoloClear()
        RefreshSoloSafe()
        SetSoloMutesLed()
    End Sub

    Function GetC24Solo(ReaperSolo As Integer) As C24_SoloTypes
        Select Case ReaperSolo
            Case 0
                Return C24_SoloTypes.NotSolo
            Case 1, 2
                Return C24_SoloTypes.Solo
            Case 5, 6
                Return C24_SoloTypes.SoloSafe
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

    Sub SetSoloMutesLed()
        Dim isOneSolo = SoloTracks.Values.Contains(True)
        If isOneSolo Then
            If _ReaperTracksManager.Tracks IsNot Nothing Then
                For i = 0 To _ReaperTracksManager.Tracks.Count - 1
                    Dim c24Trk = C24_Desk.ChannelStrips(i)
                    Dim reaperTrk = _ReaperTracksManager.Tracks(i)
                    If SoloSafeTracks(reaperTrk.TrackIndex) = True OrElse
                   SoloTracks(reaperTrk.TrackIndex) = True OrElse
                   reaperTrk.Mute = True Then
                        If c24Trk.Mute.IsBlinking = True Then
                            c24Trk.Mute.IsBlinking = False

                        End If
                    Else
                        c24Trk.Mute.IsBlinking = True
                    End If
                Next
            Else
                For i = 0 To _ReaperTracksManager.Tracks.Count - 1
                    Dim c24Trk = C24_Desk.ChannelStrips(i)
                    If c24Trk.Mute.IsBlinking = True Then
                        c24Trk.Mute.IsBlinking = False

                    End If
                Next
            End If

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
                End If
            End Sub
        h.BankLeftBtnH = BankLeftBtnH
        AddHandler C24_Desk.Bank_Left.OnButtonReleased, BankLeftBtnH

        Dim BankRightBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If _ReaperTracksManager.IsPageRightExist Then
                    _ReaperTracksManager.Set_Next_Page()

                    RefreshTrackManagerPage()
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

        RefreshDics(_ReaperProj)

        _ReaperPeakLevelManager.ClearWatched()
        _ReaperPeakLevelManager.WatchTracks(_ReaperTracksManager.Tracks)
        _ReaperPeakLevelManager.WatchTrack(-1)
        ClearTracksPeakLevels()

        RemoveCommonStripHandlers()
        SetCommonStripHandlers()
        RemoveFadersHandlers
        SetFadersHandlers()
        RefreshC24HomeViewMode()

    End Sub

#End Region
#Region "Transport"


    Sub AddTransportHandlers()
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

        ' ── Stopper et libérer le manager ─────────────────────────────────────
        If _ReaperTransportManager IsNot Nothing Then
            _ReaperTransportManager.Stop()
            _ReaperTransportManager.Dispose()
            _ReaperTransportManager = Nothing
        End If
    End Sub


#End Region
#Region "AutoMode"
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
            Case Commands.AutoMode.Latch
                _ReaperTracksManager.SetAutoMode(TrackNb - 1, 4)
                c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.Latch
        End Select
    End Sub
#End Region


#Region "Views"
    Public Enum C24ViewModes
        None
        Home
        PanSend
        SendX
        Pan
        Insert
        FxParam
    End Enum
#Region "Refresh View Modes"
    Sub RefreshC24HomeViewMode()
        'HOME View
        ActualC24ViewMode = C24ViewModes.Home
        RemoveViewsStripHandlers()
        C24_Desk.ScreenChannelBarScroll.SetText("L<>RPan ")
        SetHomeStripHandlers()
        AllScribbleScriptBlinking(False)
    End Sub
    Sub RefreshC24PanSendViewMode(TrackNb As Integer, Optional SendPageNb As Integer = 1)
        'PAN SEND View
        ActualC24ViewMode = C24ViewModes.PanSend
        RemoveViewsStripHandlers()
        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        C24_Desk.ScreenChannelBarScroll.SetText(reaperTrk.Name)
        SetPanSendStripHandlers(TrackNb, SendPageNb)
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
        If Fx.IsOpen = False Then
            Fx.IsOpen = True
        End If

        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(Fx.ParamCount / CDbl(24))))
        C24_Desk.ScreenChannelBarScroll.ShowTempText("Page" & SendPageNb & "/" & totalPages)
        C24_Desk.ScreenChannelBarScroll.SetText(_ReaperSessionConfig.GetPluginShortName(Fx.Name))
        SetFxParamStripHandlers(Fx, SendPageNb)
        AllScribbleScriptBlinking(False)

    End Sub

#End Region

#Region "Remove Handlers"
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
    Public Sub RemoveViewsStripHandlers()
        Select Case LastC24ViewMode
            Case C24ViewModes.None
            Case C24ViewModes.Home
                For Each h In _HomeHandlers
                    Dim r = h.ReaperTrk
                    Dim c = h.C24Trk

                    RemoveHandler r.On_PanChange, h.PanChangeH
                    RemoveHandler c.Vpot.OnVpotValueChange, h.VpotValueH
                    RemoveHandler c.SwitchActive.OnButtonReleased, h.SwitchActiveBtnH

                Next
                _HomeHandlers.Clear()
            Case C24ViewModes.PanSend, C24ViewModes.SendX, C24ViewModes.Pan
                RemovePageHandlers()
                RemoveAssignHandlers()

                For Each sh In _PanSendHandlers
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
                _PanSendHandlers.Clear()

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
                If ActualFxEdited.IsOpen = True Then
                    ActualFxEdited.IsOpen = False
                End If
                RemovePageHandlers()
                RemoveAssignHandlers()

                RemoveFxParamHandlers()
                RemoveFlipFxParamHandlers()
                RemoveFadersOnVpotHandlers()
                RemoveFadersHandlers()
                RemoveFlipModeHandler()
                SetFadersHandlers()

                ' ── Arrêter et libérer le monitor FxParam ─────────────────────
                If _ReaperFxParamMonitor IsNot Nothing Then
                    _ReaperFxParamMonitor.ClearWatched()
                    _ReaperFxParamMonitor.Stop()
                    _ReaperFxParamMonitor.Dispose()
                    _ReaperFxParamMonitor = Nothing
                End If
        End Select
    End Sub
#End Region

#Region "Home"

    Private Sub SetHomeStripHandlers()

        ClearC24_MiddleStrip()
        '--------------------------- Home Mode----------------------------------
        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)
                reaperTrk.Refresh()
                c24Trk.ScribbleStrip.Text = reaperTrk.Name

                Dim h As New StripHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

                c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.Pan
                c24Trk.Vpot.Value = reaperTrk.Pan
                Dim panChangeH As Reaper.ReaperTrack.On_PanChangeEventHandler =
                    Sub(pan As Double)
                        c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.Pan
                        c24Trk.Vpot.Value = pan
                    End Sub
                h.PanChangeH = panChangeH
                AddHandler reaperTrk.On_PanChange, panChangeH

                Dim vpotValueH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                    Sub(trackNb As Integer, value As Double)
                        _ReaperTracksManager.SetPan(trackNb - 1, value)
                    End Sub
                h.VpotValueH = vpotValueH
                AddHandler c24Trk.Vpot.OnVpotValueChange, vpotValueH

                'Dim switchActiveBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                '    Sub(trackNb As Integer?)
                '        c24Trk.SwitchActive.Led = Not c24Trk.SwitchActive.Led
                '        SwitchActive_OnButtonReleased(trackNb)
                '    End Sub
                'h.SwitchActiveBtnH = switchActiveBtnH
                'AddHandler c24Trk.SwitchActive.OnButtonReleased, switchActiveBtnH

                _HomeHandlers.Add(h)

            Else
                ClearChannelStripValues(c24Trk)
            End If
        Next

    End Sub

#End Region

#Region "PanSend"

    Private Sub SetPanSendStripHandlers(TrackNb As Integer, SendPageNb As Integer)
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
                OriginalSends(i) = " No"
            End If
        Next
        Dim ModifySends As String() = OriginalSends.Clone

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

                    ''quit Assign Mode et valide les modifs
                    'C24_Desk.Assign.IsBlinking = False
                    'C24_Desk.Esc_Group.SetAllBlinking(False)
                    'C24_Desk.Esc_Group.SetAllOff()
                    'Assign_Mode = False
                    'RefreshC24PanSendViewMode(TrackNb, SendPageNb)
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


        Dim sh As New SendHandlers

        'Pan : |L<>R|    |
        Dim C24_trk_LR = C24_Desk.ChannelStrips(0)
        sh.C24_trk_LR = C24_trk_LR
        C24_trk_LR.ScribbleStrip.Text = "L<>R"
        C24_trk_LR.LedSwitchActive.Led = True
        C24_trk_LR.SwitchActive.Led = False
        'Add Pan Handler
        Dim switchActiveBtnH_PanEnter As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(Track_Nb As Integer?)
                        If ActualC24ViewMode = C24ViewModes.PanSend Then
                            'Pan ViewMode
                            ActualC24ViewMode = C24ViewModes.Pan
                            RemoveViewsStripHandlers()
                            ClearC24_MiddleStrip()

                            'Set Pan Handlers
                            Dim _sh = New SendHandlers
                            Dim C24_trk_clear = C24_Desk.ChannelStrips(2)
                            'Back to PanSendView Mode
                            Dim C24_trk_SndX = C24_Desk.ChannelStrips(0)
                            C24_trk_SndX.ScribbleStrip.Text = "<Bck"
                            _sh.C24_trk_SndX = C24_trk_SndX
                            C24_trk_SndX.SwitchActive.IsBlinking = True
                            'SwitchActive Handler
                            Dim switchActiveBtnH_Pan As Hardware.C24_Button.OnButtonReleasedEventHandler =
                                 Sub(SendModeBtn_Tracknb As Integer)
                                     RefreshC24PanSendViewMode(TrackNb, SendPageNb)
                                 End Sub
                            _sh.switchActiveBtnH_Pan = switchActiveBtnH_Pan
                            AddHandler C24_trk_SndX.SwitchActive.OnButtonReleased, switchActiveBtnH_Pan


                            'To Master Vol
                            Dim C24_trk_SndName = C24_Desk.ChannelStrips(1)
                            C24_trk_SndName.ScribbleStrip.Text = "Mast"
                            C24_trk_SndName.Vpot.VpotMode = C24_Vpot.VpotModeType.VolumeLinear
                            C24_trk_SndName.Vpot.Value = reaperTrk.Vol
                            _sh.C24_trk_SndName = C24_trk_SndName
                            'Vpot handler
                            Dim vpotValueH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                                 Sub(Vpot_Tracknb As Integer, value As Double)
                                     '_ReaperTracksManager.SuspendTrack(_ReaperTracksManager.GetRelativeTrackIndex(reaperTrk.TrackIndex))
                                     reaperTrk.Vol = value
                                     Dim C24_trk_Index = _ReaperTracksManager.GetRelativeTrackIndex(reaperTrk.TrackIndex)
                                     If C24_trk_Index IsNot Nothing Then

                                         C24_Desk.ChannelStrips(C24_trk_Index).Fader.SetInitFaderDbVolume(Reaper.SLIDER2DB(value))
                                     End If

                                     '_ReaperTracksManager.Tracks(_ReaperTracksManager.GetRelativeTrackIndex(reaperTrk.TrackIndex)).Vol = value
                                     '_ReaperTracksManager.ResumeTrack(_ReaperTracksManager.GetRelativeTrackIndex(reaperTrk.TrackIndex))
                                 End Sub
                            _sh.VpotValueH = vpotValueH
                            AddHandler C24_trk_SndName.Vpot.OnVpotValueChange, vpotValueH

                            'Pan
                            Dim C24_trk_SndPre = C24_Desk.ChannelStrips(3)
                            C24_trk_SndPre.ScribbleStrip.Text = "L<>R"
                            _sh.C24_trk_SndPre = C24_trk_SndPre
                            C24_trk_SndPre.Vpot.VpotMode = C24_Vpot.VpotModeType.Pan
                            C24_trk_SndPre.Vpot.Value = reaperTrk.Pan
                            'Vpot handler
                            Dim vpotValueH_Pan As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                                 Sub(Vpot_Tracknb As Integer, value As Double)
                                     _ReaperTracksManager.SuspendTrack(_ReaperTracksManager.GetRelativeTrackIndex(reaperTrk.TrackIndex))
                                     reaperTrk.Pan = value
                                     _ReaperTracksManager.ResumeTrack(_ReaperTracksManager.GetRelativeTrackIndex(reaperTrk.TrackIndex))
                                 End Sub
                            _sh.vpotValueH_Pan = vpotValueH_Pan
                            AddHandler C24_trk_SndPre.Vpot.OnVpotValueChange, vpotValueH_Pan

                            _PanSendHandlers.Add(_sh)
                        Else
                            'Back to PanSend ViewMode
                            RefreshC24PanSendViewMode(TrackNb, SendPageNb)
                        End If
                    End Sub
        sh.switchActiveBtnH_PanEnter = switchActiveBtnH_PanEnter
        AddHandler C24_trk_LR.SwitchActive.OnButtonReleased, switchActiveBtnH_PanEnter


        'Sends : |    |SndX|Name|Pre |
        For i = 0 To 4
            SetSndX(Sends, PageSendsLetter(SendPageNb)(i), SendPageNb, i, 1, _PanSendHandlers, TrackNb, _ReaperTrackSendsEditor, ModifySends)
        Next


        _PanSendHandlers.Add(sh)


        '----------------------------------------------------------------------

    End Sub
    Private Sub SetSndX(Sends As Reaper.ReaperTrackSends, SendLetter As String, SendPageNb As Integer, SendXindex As Integer, SendXOffset As Integer, _PanSendHandlers As List(Of SendHandlers), TrackNb As Integer, _ReaperTrackSendsEditor As Reaper.PageManagers.ReaperTrackSendsEditor, ModifySends As String())

        Dim sh As New SendHandlers

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
                'Vpot handler
                Dim vpotValueH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                        Sub(Track_Nb As Integer, value As Double)
                            send.Vol = value
                        End Sub
                sh.VpotValueH = vpotValueH
                AddHandler C24_trk_SndName.Vpot.OnVpotValueChange, vpotValueH

                C24_trk_SndName.LedSwitchActive.Led = True
                C24_trk_SndName.SwitchActive.Led = send.Mute
                'Mute Handle              
                Dim SendMuteBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        Sub(Track_Nb As Integer?)
                            send.Mute = Not send.Mute
                            C24_trk_SndName.SwitchActive.Led = Not C24_trk_SndName.SwitchActive.Led
                            If send.Mute Then
                                C24_trk_SndName.ScribbleStrip.ShowTempText("Mute")
                            Else
                                C24_trk_SndName.ScribbleStrip.ShowTempText("----")
                            End If
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
        Else
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



#End Region

#Region "FxParam"
    Private Sub SetFxParamStripHandlers(Fx As ReaperFX, FxParamPageNb As Integer)
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

        FlipBtnH =
                    Sub(trackNb As Integer?)
                        SetFlipMode()
                        RefreshC24FxParamViewMode(Fx, FxParamPageNb)
                    End Sub

        AddHandler C24_Desk.Flip.OnButtonReleased, FlipBtnH

        '----------------- Display + FxParamHandlers -------------------------------------------------

        ClearC24_MiddleStrip()

        ' ── Configurer Faders/Vpots UNE SEULE FOIS avant la boucle ──────────
        If Flip_Mode Then
            RemoveFadersHandlers()
            SetFadersOnVpotHandlers()
            RemoveFxParamHandlers()
        Else
            RemoveFadersOnVpotHandlers()
            SetFadersHandlers()
        End If

        Dim _fxParamConfig = _ReaperSessionConfig.fxParamConfigs(Fx.Name)
        Console.WriteLine("")
        '|Param1|Param2|etc...
        For i = 0 To Math.Min(23, (_fxParamConfig.Count - (24 * (FxParamPageNb - 1))) - 1)
            Dim cs = C24_Desk.ChannelStrips(i)
            Dim FxParamConfigIndex As Integer = i + (24 * (FxParamPageNb - 1))
            Dim fpH As New FxParamHandlers
            fpH.C24Trk = cs

            'text separator
            If _fxParamConfig(FxParamConfigIndex).TextOnly IsNot Nothing Then
                cs.ScribbleStrip.Text = _fxParamConfig(FxParamConfigIndex).TextOnly
                'fxparam
            ElseIf _fxParamConfig(FxParamConfigIndex).Visible Then

                cs.ScribbleStrip.Text = _fxParamConfig(FxParamConfigIndex).ShortName
                Dim FxParam = Fx.Params(_fxParamConfig(FxParamConfigIndex).ParamIndex)
                ParamIndexToTrackIndex.Add(FxParam.ParamIndex, i)
                fpH.ReaperFxParam = FxParam

                'le Fxparam est géré comme un booléen
                If FxParam.IsToggle Then
                    cs.LedSwitchActive.Led = True
                    cs.SwitchActive.Led = If(FxParam.RawValue = 0, False, True)

                    Dim SelectBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        Sub(TrackNb As Integer?)
                            If FxParam.RawValue = 0 Then
                                FxParam.RawValue = 1
                                cs.ScribbleStrip.ShowTempText("  On")
                            Else
                                FxParam.RawValue = 0
                                cs.ScribbleStrip.ShowTempText(" Off")
                            End If
                            cs.SwitchActive.Led = If(FxParam.RawValue = 0, False, True)
                        End Sub
                    fpH.SelectBtnH = SelectBtnH
                    AddHandler cs.SwitchActive.OnButtonReleased, SelectBtnH




                Else 'Le Fx param est géré comme un param dont les valeurs sont brutes.
                    If Flip_Mode = False Then


                        cs.Vpot.VpotMode = C24_Vpot.VpotModeType.FxParamLinear
                        If FxParam.Step IsNot Nothing Then
                            If FxParam.LargeStep IsNot Nothing Then
                                cs.Vpot.FxParam_LargeStepValue = FxParam.LargeStep
                            Else
                                cs.Vpot.FxParam_LargeStepValue = FxParam.Step
                            End If
                            If FxParam.SmallStep IsNot Nothing Then
                                cs.Vpot.FxParam_SmallStepValue = FxParam.SmallStep
                            Else
                                cs.Vpot.FxParam_SmallStepValue = FxParam.Step
                            End If
                        End If


                        Dim VpotValueChangeH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                        Sub(Track_Nb As Integer, Value As Double)
                            FxParam.NormValue = Value
                            cs.Vpot.FxParamValueToDisplay = FxParam.Formatted

                        End Sub
                        fpH.VpotValueChangeH = VpotValueChangeH
                        AddHandler cs.Vpot.OnVpotValueChange, VpotValueChangeH



                        Dim FxParamMonitorChangeH As Reaper.Monitors.ReaperFxParamMonitor.On_ParamChangeEventHandler =
                         Sub(ParamIndex As Integer, NormValue As Double)
                             If Not ParamIndexToTrackIndex.ContainsKey(ParamIndex) Then Return
                             Dim Fxp = Fx.Params(ParamIndex)
                             Dim _cs = C24_Desk.ChannelStrips(ParamIndexToTrackIndex(ParamIndex))
                             If Fxp.IsToggle Then
                                 _cs.ScribbleStrip.ShowTempText(If(CInt(NormValue) = 1, "  On", " Off"))
                                 _cs.SwitchActive.Led = Fxp.RawValue <> 0
                             Else
                                 _cs.Vpot.Value = NormValue
                                 Fxp.Refresh()
                                 _cs.Vpot.FxParamValueToDisplay = Fxp.Formatted
                             End If
                         End Sub
                        fpH.ParamMonitorChangeH = FxParamMonitorChangeH
                        AddHandler _ReaperFxParamMonitor.On_ParamChange, FxParamMonitorChangeH



                    Else 'Flip mode


                        cs.Fader.FaderNormValue = FxParam.NormValue
                        cs.Vpot.FxParamValueToDisplay = FxParam.Formatted

                        Dim FaderValueChangeH As Hardware.C24_Fader.On_FaderChangeNormValueEventHandler =
                        Sub(Track_Nb As Integer, Value As Double)
                            FxParam.NormValue = Value
                            cs.Vpot.FxParamValueToDisplay = FxParam.Formatted

                        End Sub
                        fpH.FaderValueChangeH = FaderValueChangeH
                        AddHandler cs.Fader.On_FaderChangeNormValue, FaderValueChangeH



                        Dim FxParamMonitorChangeH As Reaper.Monitors.ReaperFxParamMonitor.On_ParamChangeEventHandler =
                         Sub(ParamIndex As Integer, NormValue As Double)
                             If Not ParamIndexToTrackIndex.ContainsKey(ParamIndex) Then Return
                             Dim Fxp = Fx.Params(ParamIndex)
                             Dim _cs = C24_Desk.ChannelStrips(ParamIndexToTrackIndex(ParamIndex))
                             If Fxp.IsToggle Then
                                 _cs.ScribbleStrip.ShowTempText(If(CInt(NormValue) = 1, "  On", " Off"))
                                 _cs.SwitchActive.Led = Fxp.RawValue <> 0
                             Else
                                 _cs.Fader.FaderNormValue = NormValue
                                 Fxp.Refresh()
                                 _cs.Vpot.FxParamValueToDisplay = Fxp.Formatted
                             End If
                         End Sub
                        fpH.ParamMonitorChangeH = FxParamMonitorChangeH
                        AddHandler _ReaperFxParamMonitor.On_ParamChange, FxParamMonitorChangeH



                    End If

                End If
                _FlipFxParamHandlers.Add(fpH)
                _FxParamHandlers.Add(fpH)
            End If


            Console.Write("|" & cs.ScribbleStrip.Text)
        Next









    End Sub


    Sub RemoveFxParamHandlers()
        For Each fpH In _FxParamHandlers
            Dim cs = fpH.C24Trk
            RemoveHandler cs.SwitchActive.OnButtonReleased, fpH.SelectBtnH
            RemoveHandler cs.Vpot.OnVpotValueChange, fpH.VpotValueChangeH
            If fpH.ParamMonitorChangeH IsNot Nothing AndAlso _ReaperFxParamMonitor IsNot Nothing Then
                RemoveHandler _ReaperFxParamMonitor.On_ParamChange, fpH.ParamMonitorChangeH
            End If
        Next
        _FxParamHandlers.Clear()
    End Sub
    Sub RemoveFlipFxParamHandlers()
        For Each fpH In _FlipFxParamHandlers
            Dim cs = fpH.C24Trk
            ' ── En mode Flip : c'est FaderValueChangeH qui est branché, pas VpotValueChangeH ──
            If fpH.FaderValueChangeH IsNot Nothing Then
                RemoveHandler cs.Fader.On_FaderChangeNormValue, fpH.FaderValueChangeH
            End If
            If fpH.SelectBtnH IsNot Nothing Then
                RemoveHandler cs.SwitchActive.OnButtonReleased, fpH.SelectBtnH
            End If
            If fpH.ParamMonitorChangeH IsNot Nothing AndAlso _ReaperFxParamMonitor IsNot Nothing Then
                RemoveHandler _ReaperFxParamMonitor.On_ParamChange, fpH.ParamMonitorChangeH
            End If
        Next
        _FlipFxParamHandlers.Clear()
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
        RemoveSolosHandlers()
        RemoveBankHandlers()
        RemoveCounterModeHandler()

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