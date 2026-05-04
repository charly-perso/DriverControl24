Imports DriverReaperControl24.Control24
Imports DriverReaperControl24.Control24.Commands
Imports DriverReaperControl24.Control24.Hardware
Imports DriverReaperControl24.Reaper
Imports DriverReaperControl24.Reaper.Monitors
Imports DriverReaperControl24.Reaper.PageManagers

Public Class C24ReaperController

    Private _ReaperSessionConfig As Reaper.ReaperSessionConfig
    Private WithEvents _C24_Desk As C24_Desk
    Private WithEvents _ReaperSession As Reaper.ReaperSession
    Private WithEvents _ReaperProj As Reaper.ReaperProject
    Private WithEvents _ReaperTracksManager As Reaper.PageManagers.ReaperTracksManager

    Private ActualC24ViewMode As C24ViewModes
    Private SoloSafeTracks As New Dictionary(Of Integer, Boolean)
    Private SoloTracks As New Dictionary(Of Integer, Boolean)
    Private Names As New Dictionary(Of Integer, String)

    ' ── Stockage des delegates Home pour tous les strips ───────────────
    Private _HomeHandlers As New List(Of StripHandlers)

    ' ── Stockage des delegates par fonctions ───────────────────────────────────
    Private _SolosHandlers As New List(Of SolosHandlers)
    Private _BankHandlers As New List(Of BankHandlers)
    Private _Solo_Mode As Solo_Mode = Solo_Mode.Solo

    ''' <summary>
    ''' Regroupe tous les delegates d'un channel strip en mode PanSend.
    ''' Permet AddHandler/RemoveHandler symétriques.
    ''' </summary>
    Private Class StripHandlers
        Public ReaperTrk As Reaper.ReaperTrack
        Public C24Trk As Hardware.C24_ChannelStrip

        Public VolChangeH As [Delegate]
        Public FaderHumanH As [Delegate]
        Public PanChangeH As [Delegate]
        Public VpotValueH As [Delegate]
        Public MuteChangeH As [Delegate]
        Public MuteBtnH As [Delegate]
        Public SoloChangeH As [Delegate]
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

    Private Class SolosHandlers
        Public SoloClearBtnH As [Delegate]
        Public SoloSafeBtnH As [Delegate]
    End Class
    Private Class BankHandlers

        Public BankLeftBtnH As [Delegate]
        Public BankRightBtnH As [Delegate]
        Public BankNudgeBtnH As [Delegate]

    End Class
    Public Enum C24ViewModes
        Home
        PanSend
    End Enum

    ReadOnly Property C24_Desk As Control24.Hardware.C24_Desk
        Get
            Return _C24_Desk
        End Get
    End Property

    Sub New(C24_Desk As Control24.Hardware.C24_Desk)
        _C24_Desk = C24_Desk
        ReaperPollHub.Instance.Start()
        _ReaperSession = New Reaper.ReaperSession
        _ReaperSession.Start()
        _ReaperSessionConfig = New Reaper.ReaperSessionConfig
    End Sub

    Sub ReaperSession_On_ProjectChanged(ReaperProj As Reaper.ReaperProject) Handles _ReaperSession.On_ProjectChanged
        _ReaperProj = ReaperProj
        _ReaperTracksManager = New Reaper.PageManagers.ReaperTracksManager(_ReaperProj,, _ReaperSessionConfig)




        RefreshDics(ReaperProj)
        AddAllCommonC24Function()
        ActualC24ViewMode = C24ViewModes.Home
        RefreshC24HomeViewMode()
    End Sub

    ' ── Reconstruction au changement de page ──────────────────────────
    Sub RefreshTrackManagerPage()
        RefreshDics(_ReaperProj)
        RefreshC24HomeViewMode()
    End Sub

    Sub AddAllCommonC24Function()

        'Init Values
        RefreshBanksStateLed()


        'Add Handlers
        AddSolosC24Handlers()
        AddBankC24Handlers()

    End Sub
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
                    C24_Desk.SoloSafe.Led = False
                End If
                ' ── Swap uniquement le SoloBtnH sur chaque strip actif ────────
                For Each _h In _HomeHandlers
                    RemoveHandler _h.C24Trk.Solo.OnButtonReleased, _h.SoloBtnH
                    Dim newSoloBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                        GetSoloBtnH(_Solo_Mode, _h.ReaperTrk.TrackIndex)
                    _h.SoloBtnH = newSoloBtnH
                    AddHandler _h.C24Trk.Solo.OnButtonReleased, newSoloBtnH
                Next

                InitSoloLed()
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
                            _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.SafeSolo)
                        Else
                            If SoloTracks(TrackIndex) Then
                                _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.SoloInPlace)
                            Else
                                _ReaperTracksManager.Set_SoloStatus(trackNb - 1, Reaper.SoloState.Off)
                            End If
                            C24_Desk.ChannelStrips(trackNb - 1).Solo.IsBlinking = False
                            C24_Desk.ChannelStrips(trackNb - 1).Solo.Led = False

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
    Sub AddBankC24Handlers()
        RemoveBankHandlers()
        'Bank Navigation
        Dim h As New BankHandlers

        Dim BankLeftBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If _ReaperTracksManager.IsPageLeftExist Then
                    _ReaperTracksManager.Set_Prev_Page()
                    RefreshBanksStateLed()
                    RefreshTrackManagerPage()
                End If
            End Sub
        h.BankLeftBtnH = BankLeftBtnH
        AddHandler C24_Desk.Bank_Left.OnButtonReleased, BankLeftBtnH

        Dim BankRightBtnH As Hardware.C24_LedButton.OnButtonReleasedEventHandler =
            Sub(TrackNb As Integer?)
                If _ReaperTracksManager.IsPageRightExist Then
                    _ReaperTracksManager.Set_Next_Page()
                    RefreshBanksStateLed()
                    RefreshTrackManagerPage()
                End If
            End Sub
        h.BankRightBtnH = BankRightBtnH
        AddHandler C24_Desk.Bank_Right.OnButtonReleased, BankRightBtnH

        _BankHandlers.Add(h)
    End Sub
    Sub RefreshBanksStateLed()
        'Banks
        If _ReaperTracksManager.IsPageLeftExist Then
            C24_Desk.Bank_Left.Led = True
        Else
            C24_Desk.Bank_Left.Led = False
        End If
        If _ReaperTracksManager.IsPageRightExist Then
            C24_Desk.Bank_Right.Led = True
        Else
            C24_Desk.Bank_Right.Led = False
        End If
    End Sub
    ''' <summary>Supprime tous les handlers Banks du C24.</summary>
    Public Sub RemoveBankHandlers()
        For Each h In _BankHandlers
            RemoveHandler C24_Desk.Bank_Left.OnButtonReleased, h.BankLeftBtnH
            RemoveHandler C24_Desk.Bank_Right.OnButtonReleased, h.BankRightBtnH
            RemoveHandler C24_Desk.Nudge.OnButtonReleased, h.BankNudgeBtnH
        Next
        _SolosHandlers.Clear()
    End Sub
    Sub RefreshDics(ReaperProj As Reaper.ReaperProject)
        SoloTracks = New Dictionary(Of Integer, Boolean)
        SoloSafeTracks = New Dictionary(Of Integer, Boolean)
        Names = New Dictionary(Of Integer, String)
        For Each trk In ReaperProj.Tracks
            SoloTracks.Add(trk.TrackIndex, If(GetC24Solo(trk.Solo) = C24_SoloTypes.Solo, True, False))
            SoloSafeTracks.Add(trk.TrackIndex, If(GetC24Solo(trk.Solo) = C24_SoloTypes.SoloSafe, True, False))
            Names.Add(trk.TrackIndex, trk.Name)
        Next
        RefreshSoloClear()
        RefreshSoloSafe()
        SetSoloMutesLed()
    End Sub

    Sub RefreshC24HomeViewMode()
        'HOME View
        C24_Desk.ScreenChannelBarScroll.SetText("L<>RPan ")
        InitHomeModeValues()
        SetHomeStripHandlers()

    End Sub
    Sub RefreshC24PanSendViewMode(TrackNb As Integer)
        'PAN SEND View
        Dim reaperTrk = _ReaperTracksManager.Tracks(TrackNb - 1)
        C24_Desk.ScreenChannelBarScroll.SetText(reaperTrk.Name)
        InitPanSendModeValues
        SetPanSendStripHandlers
    End Sub

#Region "Home"
    Private Sub InitHomeModeValues()
        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)

                c24Trk.ScribbleStrip.Text = reaperTrk.Name
                c24Trk.Mute.Led = reaperTrk.Mute
                c24Trk.Select.Led = reaperTrk.Selected
                c24Trk.RecArm.Led = If(reaperTrk.RecArm = 0, False, True)

                'c24Trk.Solo.Led = SoloTracks(reaperTrk.TrackIndex)
                c24Trk.Fader.SetInitFaderDbVolume(Reaper.utility.SLIDER2DB(reaperTrk.Vol))
                c24Trk.AutoModeLed.AutoMode = GetC24AutoMode(reaperTrk.AutoMode)
                c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.Pan
                c24Trk.Vpot.Value = reaperTrk.Pan
                c24Trk.SendPanMode.Led = False
                c24Trk.EqMode.Led = Not _ReaperTracksManager.IsTrackEqFxEmpty(reaperTrk.TrackIndex)
                c24Trk.DynMode.Led = Not _ReaperTracksManager.IsTrackDynFxEmpty(reaperTrk.TrackIndex)
                c24Trk.InsertMode.Led = Not _ReaperTracksManager.IsTrackGenericFxEmpty(reaperTrk.TrackIndex)



            Else
                ClearChannelStripValues(c24Trk)
            End If
        Next

        InitSoloLed()

    End Sub

    Private Sub SetHomeStripHandlers()
        RemoveHomeStripHandlers()

        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)

                Dim h As New StripHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }

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



                Dim soloBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler = GetSoloBtnH(_Solo_Mode, reaperTrk.TrackIndex)
                h.SoloBtnH = soloBtnH
                AddHandler c24Trk.Solo.OnButtonReleased, soloBtnH

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

                Dim sendPanBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        c24Trk.SendPanMode.Led = Not c24Trk.SendPanMode.Led
                        RefreshC24PanSendViewMode(trackNb)
                    End Sub
                h.SendPanBtnH = sendPanBtnH
                AddHandler c24Trk.SendPanMode.OnButtonReleased, sendPanBtnH

                Dim switchActiveBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        c24Trk.SwitchActive.Led = Not c24Trk.SwitchActive.Led
                        TrackEqMode_OnButtonReleased(trackNb)
                    End Sub
                h.SwitchActiveBtnH = switchActiveBtnH
                AddHandler c24Trk.SwitchActive.OnButtonReleased, switchActiveBtnH

                Dim eqBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        c24Trk.EqMode.Led = Not c24Trk.EqMode.Led
                        TrackEqMode_OnButtonReleased(trackNb)
                    End Sub
                h.EqBtnH = eqBtnH
                AddHandler c24Trk.EqMode.OnButtonReleased, eqBtnH

                Dim dynBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        c24Trk.DynMode.Led = Not c24Trk.DynMode.Led
                        TrackDynMode_OnButtonReleased(trackNb)
                    End Sub
                h.DynBtnH = dynBtnH
                AddHandler c24Trk.DynMode.OnButtonReleased, dynBtnH

                Dim insertBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        c24Trk.InsertMode.Led = Not c24Trk.InsertMode.Led
                        TrackInsertMode_OnButtonReleased(trackNb)
                    End Sub
                h.InsertBtnH = insertBtnH
                AddHandler c24Trk.InsertMode.OnButtonReleased, insertBtnH

                _HomeHandlers.Add(h)

            End If
        Next

    End Sub

    ''' <summary>Supprime tous les handlers Home de toutes les pistes.</summary>
    Public Sub RemoveHomeStripHandlers()

        For Each h In _HomeHandlers
            Dim r = h.ReaperTrk
            Dim c = h.C24Trk
            RemoveHandler r.On_VolChange, h.VolChangeH
            RemoveHandler c.Fader.On_FaderHumanChangeDbValue, h.FaderHumanH
            RemoveHandler r.On_PanChange, h.PanChangeH
            RemoveHandler c.Vpot.OnVpotValueChange, h.VpotValueH
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
            RemoveHandler c.SwitchActive.OnButtonReleased, h.SwitchActiveBtnH
            RemoveHandler c.EqMode.OnButtonReleased, h.EqBtnH
            RemoveHandler c.DynMode.OnButtonReleased, h.DynBtnH
            RemoveHandler c.InsertMode.OnButtonReleased, h.InsertBtnH
        Next
        _HomeHandlers.Clear()
    End Sub
#End Region

#Region "PanSend"
    Private Sub InitPanSendModeValues()
        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)

                c24Trk.ScribbleStrip.Text = reaperTrk.Name
                c24Trk.Mute.Led = reaperTrk.Mute
                c24Trk.Select.Led = reaperTrk.Selected
                c24Trk.RecArm.Led = If(reaperTrk.RecArm = 0, False, True)

                'c24Trk.Solo.Led = SoloTracks(reaperTrk.TrackIndex)
                c24Trk.Fader.SetInitFaderDbVolume(Reaper.utility.SLIDER2DB(reaperTrk.Vol))
                c24Trk.AutoModeLed.AutoMode = GetC24AutoMode(reaperTrk.AutoMode)
                c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.Pan
                c24Trk.Vpot.Value = reaperTrk.Pan
                c24Trk.SendPanMode.Led = False
                c24Trk.EqMode.Led = Not _ReaperTracksManager.IsTrackEqFxEmpty(reaperTrk.TrackIndex)
                c24Trk.DynMode.Led = Not _ReaperTracksManager.IsTrackDynFxEmpty(reaperTrk.TrackIndex)
                c24Trk.InsertMode.Led = Not _ReaperTracksManager.IsTrackGenericFxEmpty(reaperTrk.TrackIndex)



            Else
                ClearChannelStripValues(c24Trk)
            End If
        Next

        InitSoloLed()

    End Sub

    Private Sub SetPanSendStripHandlers()
        RemovePanSendStripHandlers()

        For i = 0 To 23
            Dim c24Trk = C24_Desk.ChannelStrips(i)

            If i < _ReaperTracksManager.Tracks.Count Then
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)

                Dim h As New StripHandlers With {
                    .ReaperTrk = reaperTrk,
                    .C24Trk = c24Trk
                }


                '---------------------------to change----------------------------------
                Dim panChangeH As Reaper.ReaperTrack.On_PanChangeEventHandler =
                    Sub(pan As Double)
                        c24Trk.Vpot.VpotMode = C24_Vpot.VpotModeType.Pan
                        c24Trk.Vpot.Value = pan
                    End Sub
                h.PanChangeH = panChangeH
                AddHandler reaperTrk.On_PanChange, panChangeH
                'to change
                Dim vpotValueH As Hardware.C24_Vpot.OnVpotValueChangeEventHandler =
                    Sub(trackNb As Integer, value As Double)
                        _ReaperTracksManager.SetPan(trackNb - 1, value)
                    End Sub
                h.VpotValueH = vpotValueH
                AddHandler c24Trk.Vpot.OnVpotValueChange, vpotValueH


                'to change
                Dim switchActiveBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        c24Trk.SwitchActive.Led = Not c24Trk.SwitchActive.Led
                        TrackEqMode_OnButtonReleased(trackNb)
                    End Sub
                h.SwitchActiveBtnH = switchActiveBtnH
                AddHandler c24Trk.SwitchActive.OnButtonReleased, switchActiveBtnH
                '----------------------------------------------------------------------

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



                Dim soloBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler = GetSoloBtnH(_Solo_Mode, reaperTrk.TrackIndex)
                h.SoloBtnH = soloBtnH
                AddHandler c24Trk.Solo.OnButtonReleased, soloBtnH

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

                Dim sendPanBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        c24Trk.SendPanMode.Led = Not c24Trk.SendPanMode.Led
                        RefreshC24PanSendViewMode(trackNb)
                    End Sub
                h.SendPanBtnH = sendPanBtnH
                AddHandler c24Trk.SendPanMode.OnButtonReleased, sendPanBtnH



                Dim eqBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        c24Trk.EqMode.Led = Not c24Trk.EqMode.Led
                        TrackEqMode_OnButtonReleased(trackNb)
                    End Sub
                h.EqBtnH = eqBtnH
                AddHandler c24Trk.EqMode.OnButtonReleased, eqBtnH

                Dim dynBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        c24Trk.DynMode.Led = Not c24Trk.DynMode.Led
                        TrackDynMode_OnButtonReleased(trackNb)
                    End Sub
                h.DynBtnH = dynBtnH
                AddHandler c24Trk.DynMode.OnButtonReleased, dynBtnH

                Dim insertBtnH As Hardware.C24_Button.OnButtonReleasedEventHandler =
                    Sub(trackNb As Integer?)
                        c24Trk.InsertMode.Led = Not c24Trk.InsertMode.Led
                        TrackInsertMode_OnButtonReleased(trackNb)
                    End Sub
                h.InsertBtnH = insertBtnH
                AddHandler c24Trk.InsertMode.OnButtonReleased, insertBtnH

                _HomeHandlers.Add(h)

            End If
        Next

    End Sub

    ''' <summary>Supprime tous les handlers Home de toutes les pistes.</summary>
    Public Sub RemovePanSendStripHandlers()

        For Each h In _HomeHandlers
            Dim r = h.ReaperTrk
            Dim c = h.C24Trk
            RemoveHandler r.On_VolChange, h.VolChangeH
            RemoveHandler c.Fader.On_FaderHumanChangeDbValue, h.FaderHumanH
            RemoveHandler r.On_PanChange, h.PanChangeH
            RemoveHandler c.Vpot.OnVpotValueChange, h.VpotValueH
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
            RemoveHandler c.SwitchActive.OnButtonReleased, h.SwitchActiveBtnH
            RemoveHandler c.EqMode.OnButtonReleased, h.EqBtnH
            RemoveHandler c.DynMode.OnButtonReleased, h.DynBtnH
            RemoveHandler c.InsertMode.OnButtonReleased, h.InsertBtnH
        Next
        _HomeHandlers.Clear()
    End Sub
#End Region


    ''' <summary>Remet un channel strip à l'état vide (aucune piste assignée).</summary>
    Private Sub ClearChannelStripValues(c24Trk As Hardware.C24_ChannelStrip)
        c24Trk.ScribbleStrip.Text = ""
        c24Trk.Fader.SetInitFaderDbVolume(-150.0)
        c24Trk.Vpot.SetLedsOff()
        c24Trk.Mute.Led = False
        c24Trk.Mute.IsBlinking = False
        c24Trk.Solo.Led = False
        c24Trk.RecArm.Led = False
        c24Trk.Select.Led = False
        c24Trk.SendPanMode.Led = False
        c24Trk.EqMode.Led = False
        c24Trk.DynMode.Led = False
        c24Trk.InsertMode.Led = False
        c24Trk.AutoModeLed.AutoMode = Commands.AutoMode.None
    End Sub


    Sub TrackEqMode_OnButtonReleased(TrackNb As Integer)
    End Sub
    Sub TrackDynMode_OnButtonReleased(TrackNb As Integer)
    End Sub
    Sub TrackInsertMode_OnButtonReleased(TrackNb As Integer)
    End Sub

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
        If SoloTracks.Values.Contains(True) Then
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
            For i = 0 To _ReaperTracksManager.Tracks.Count - 1
                Dim c24Trk = C24_Desk.ChannelStrips(i)
                Dim reaperTrk = _ReaperTracksManager.Tracks(i)
                If SoloSafeTracks(reaperTrk.TrackIndex) = True OrElse
                   SoloTracks(reaperTrk.TrackIndex) = True OrElse
                   reaperTrk.Mute = True Then
                    If c24Trk.Mute.IsBlinking = True Then
                        c24Trk.Mute.IsBlinking = False
                        c24Trk.Mute.Led = False
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
                    c24Trk.Mute.Led = False
                End If
            Next
        End If
    End Sub

    Sub RefreshSoloSafe()
        C24_Desk.SoloSafe.Led = SoloSafeTracks.Values.Contains(True)
    End Sub

    Sub RefreshSoloClear()
        C24_Desk.SoloClear.Led = SoloTracks.Values.Contains(True)
    End Sub

    Sub RefreshC24Tracks()
    End Sub

    Sub ClearC24_Track()
    End Sub

End Class