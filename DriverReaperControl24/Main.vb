Imports System.Net.NetworkInformation
Imports System.Runtime.InteropServices
Imports DriverReaperControl24.Reaper.Monitors

Public Class Main
    Implements IDisposable

    <DllImport("kernel32.dll")>
    Private Shared Function GetConsoleWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    Private Const SW_HIDE As Integer = 0
    Private Const SW_SHOW As Integer = 5

    Private Shared _desk As Control24.Hardware.C24_Desk = Nothing
    Private Shared _controller As C24ReaperController = Nothing
    Private disposedValue As Boolean

    ''' <summary>
    ''' Entrée principale — 
    ''' </summary>
    Public Shared Sub Main()
        Dim _ReaperSessionConfig As New Reaper.ReaperSessionConfig
        Dim interfaceName As String = _ReaperSessionConfig.SelectedNetworkInterface

        If _ReaperSessionConfig.Autostart Then

            If String.IsNullOrWhiteSpace(interfaceName) Then
                Reaper.CallReaperBridge("SHOW_MESSAGE_BOX|1|Control24 Driver|ERROR: No network interface specified.")
                Environment.Exit(1)
            End If

            If interfaceName = "TestWithoutC24" Then
                _desk = New Control24.Hardware.C24_Desk(Nothing)
            Else
                Dim selectedInterface = NetworkInterface.GetAllNetworkInterfaces() _
                    .FirstOrDefault(Function(n) n.Name = interfaceName)

                If selectedInterface Is Nothing Then
                    Dim message As String = $"ERROR: Interface '{interfaceName}' not found." & "Available interfaces are named in ReaperDriver.ini"
                    Reaper.CallReaperBridge("SHOW_MESSAGE_BOX|1|Control24 Driver|" & message)
                    Environment.Exit(1)
                End If

                Reaper.CallReaperBridge($"SHOW_MESSAGE_BOX|1|Control24 Driver|Starting Control24 driver on '{interfaceName}'...")

                _desk = New Control24.Hardware.C24_Desk(selectedInterface)
                _desk.StartNetworkCommunication()

                Const InitTimeoutMs As Integer = 30_000
                Dim initialized As Boolean = _desk.WaitForInitialization(InitTimeoutMs)

                If Not initialized Then
                    Reaper.CallReaperBridge("SHOW_MESSAGE_BOX|1|Control24 Driver|ERROR: Control24 initialization timeout. Check hardware connection.")
                    Environment.Exit(1)
                End If
            End If

            _controller = New C24ReaperController(_desk)

            ' ── Nettoyage ordonné sur Ctrl+C ──────────────────────────────────
            AddHandler Console.CancelKeyPress,
                Sub(s, e)
                    e.Cancel = True
                    Reaper.CallReaperBridge("SHOW_MESSAGE_BOX|1|Control24 Driver|Stopping...")
                    Shutdown()
                    Environment.Exit(0)
                End Sub

            ' ── Nettoyage ordonné sur fermeture du processus ──────────────────
            AddHandler AppDomain.CurrentDomain.ProcessExit,
                Sub(s, e)
                    Shutdown()
                End Sub

            If interfaceName = "TestWithoutC24" Then
                RunConsoleLoop(_controller)
            Else
                If OperatingSystem.IsWindows() Then
                    HideConsoleWindow()
                End If
                Threading.Thread.Sleep(Threading.Timeout.Infinite)
            End If
        End If
    End Sub

    ''' <summary>
    ''' Libère toutes les ressources dans l'ordre correct :
    ''' controller → desk → hub.
    ''' Idempotent : sans effet si déjà appelé.
    ''' </summary>
    Private Shared Sub Shutdown()
        If _controller IsNot Nothing Then
            _controller.Dispose()
            _controller = Nothing
        End If
        If _desk IsNot Nothing Then
            _desk.Shutdown()
            _desk = Nothing
        End If
    End Sub

    Private Shared Sub RunConsoleLoop(controller As C24ReaperController)
        Console.WriteLine("Driver Control24 démarré. Tapez 'help' pour la liste des commandes.")

        While True
            Console.Write("> ")
            Dim input = Console.ReadLine()
            If input Is Nothing Then Continue While

            Dim parts = ParseCommandParts(input.Trim())
            If parts.Count = 0 Then Continue While

            Dim cmd = parts(0)

            Select Case cmd

                Case "help"
                    PrintHelp()

                Case "quit", "exit"
                    Console.WriteLine("Arrêt du driver...")
                    Reaper.CallReaperBridge("SHOW_MESSAGE_BOX|1|Control24 Driver|Stopping...")
                    Shutdown()
                    Environment.Exit(0)

                Case "status"
                    PrintStatus(controller)

                Case "hub"
                    Console.WriteLine($"ReaperPollHub connecté : {ReaperPollHub.Instance.IsConnected}")

                Case "bridge"
                    If parts.Count < 2 Then
                        Console.WriteLine("Usage : bridge <commande>")
                        Console.WriteLine("Exemple : bridge COUNT_TRACKS")
                    Else
                        Dim bridgeCmd = String.Join(" ", parts.Skip(1))
                        Dim result = Reaper.CallReaperBridge(bridgeCmd)
                        Console.WriteLine($"→ {If(String.IsNullOrEmpty(result), "(vide)", result)}")
                    End If

                Case "tracks"
                    PrintTracks(controller)

                Case "interfaces"
                    DisplayInterfaceName()

                Case "components"
                    PrintComponents(_desk)

                Case "C24desk_OnButtonReleased"
                    If parts.Count < 3 Then
                        Console.WriteLine("Usage : C24desk_OnButtonReleased <Group> <Name>")
                        Console.WriteLine("Exemple : C24desk_OnButtonReleased ChannelStrip_1 Solo_1")
                    Else
                        Dim group = _desk.GroupComponents(parts(1))
                        If group IsNot Nothing Then
                            For Each cpn In group
                                Dim C24_btn = TryCast(cpn, Control24.Hardware.C24_Button)
                                If C24_btn IsNot Nothing AndAlso C24_btn.Name = parts(2) Then
                                    C24_btn.ButtonCmd_OnButtonStatusChange(False)
                                    Console.WriteLine(parts(1) & "|" & parts(2) & "=OK")
                                    Exit Select
                                End If
                            Next
                            Console.WriteLine(parts(1) & "|" & "?")
                        Else
                            Console.WriteLine("?" & "|" & "?")
                        End If
                    End If
                Case "C24desk_OnButtonPressed"
                    If parts.Count < 3 Then
                        Console.WriteLine("Usage : C24desk_OnButtonPressed <Group> <Name>")
                        Console.WriteLine("Exemple : C24desk_OnButtonPressed ChannelStrip_1 Solo_1")
                    Else
                        Dim group = _desk.GroupComponents(parts(1))
                        If group IsNot Nothing Then
                            For Each cpn In group
                                Dim C24_btn = TryCast(cpn, Control24.Hardware.C24_Button)
                                If C24_btn IsNot Nothing AndAlso C24_btn.Name = parts(2) Then
                                    C24_btn.ButtonCmd_OnButtonStatusChange(True)
                                    Console.WriteLine(parts(1) & "|" & parts(2) & "=OK")
                                    Exit Select
                                End If
                            Next
                            Console.WriteLine(parts(1) & "|" & "?")
                        Else
                            Console.WriteLine("?" & "|" & "?")
                        End If
                    End If
                Case "C24desk_GetButtonStatus"
                    Select Case parts.Count
                        Case 2
                            Dim group = _desk.GroupComponents(parts(1))
                            If group IsNot Nothing Then
                                For Each cpn In group
                                    Dim C24_btn = TryCast(cpn, Control24.Hardware.C24_LedButton)

                                    If C24_btn IsNot Nothing Then

                                        Console.WriteLine(C24_btn.Name & "(Led:" & If(C24_btn.Led, "On", "Off") & ";" & "IsBlinking:" & If(C24_btn.IsBlinking, "On", "Off") & ")")

                                    Else

                                        Console.WriteLine(cpn.Name & "(No Led)")
                                    End If
                                Next

                            Else
                                Console.WriteLine(parts(1) & " is not a group")
                            End If
                        Case 3
                            Dim group = _desk.GroupComponents(parts(1))
                            If group IsNot Nothing Then
                                For Each cpn In group
                                    Dim C24_btn = TryCast(cpn, Control24.Hardware.C24_LedButton)

                                    If C24_btn IsNot Nothing AndAlso C24_btn.Name = parts(2) Then

                                        Console.WriteLine("Led=" & If(C24_btn.Led, "On", "Off") & "|" & "IsBlinking=" & If(C24_btn.IsBlinking, "On", "Off"))
                                        Exit Select

                                    End If
                                Next
                                Console.WriteLine(parts(1) & "|" & "?")
                            Else
                                Console.WriteLine("?" & "|" & "?")
                            End If
                        Case Else
                            Console.WriteLine("Usage : C24desk_GetButtonStatus <Group> <Name>")
                            Console.WriteLine("Exemple : C24desk_GetButtonStatus ChannelStrip_1 Solo_1")
                            Console.WriteLine("Usage : C24desk_GetButtonStatus <Group> ")
                            Console.WriteLine("Exemple : C24desk_GetButtonStatus ChannelStrip_1 ")
                    End Select

                Case "C24desk_OnVpotRotate"
                    If parts.Count < 4 Then
                        Console.WriteLine("Usage : C24desk_OnVpotRotate <Group> <Name> <Value>")
                        Console.WriteLine("Exemple : C24desk_OnVpotRotate ChannelStrip_1 Vpot_1 -1")
                    Else
                        Dim group = _desk.GroupComponents(parts(1))
                        If group IsNot Nothing Then
                            For Each cpn In group
                                Dim C24_Vpot = TryCast(cpn, Control24.Hardware.C24_Vpot)
                                Dim C24_JogWheel As Control24.Hardware.C24_JogWheel = Nothing
                                If C24_Vpot Is Nothing Then
                                    C24_JogWheel = TryCast(cpn, Control24.Hardware.C24_JogWheel)
                                End If
                                If C24_Vpot IsNot Nothing AndAlso C24_Vpot.Name = parts(2) Then
                                    Dim value As Integer = 0

                                    If Integer.TryParse(parts(3), value) Then

                                        C24_Vpot.UpdateValueFromCmd(value)
                                        Console.WriteLine(parts(1) & "|" & parts(2) & "=OK|Value=" & value)
                                        Exit Select
                                    Else
                                        Console.WriteLine("Value " & parts(3) & "is not correct value ! (ex of correct values : -2/-1/1/2 etc...")
                                    End If
                                ElseIf C24_JogWheel IsNot Nothing AndAlso C24_JogWheel.Name = parts(2) Then
                                    Dim value As Integer = 0

                                    If Integer.TryParse(parts(3), value) Then

                                        C24_JogWheel.OnJogWheelCmdChange(value)
                                        Console.WriteLine(parts(1) & "|" & parts(2) & "=OK|Value=" & value)
                                        Exit Select
                                    Else
                                        Console.WriteLine("Value " & parts(3) & "is not correct value ! (ex of correct values : -2/-1/1/2 etc...")
                                    End If

                                End If
                            Next
                            Console.WriteLine(parts(1) & "|" & "?")
                        Else
                            Console.WriteLine("?" & "|" & "?")
                        End If
                    End If
                Case "C24desk_GetFaderValue"
                    If parts.Count < 3 Then
                        Console.WriteLine("Usage : C24desk_GetFaderValue <Group> <Name> ")
                        Console.WriteLine("Exemple : C24desk_GetFaderValue ChannelStrip_1 Fader_1 ")
                    Else
                        Dim group = _desk.GroupComponents(parts(1))
                        If group IsNot Nothing Then
                            For Each cpn In group
                                Dim C24_Fader = TryCast(cpn, Control24.Hardware.C24_Fader)
                                If C24_Fader IsNot Nothing AndAlso C24_Fader.Name = parts(2) Then



                                    Console.WriteLine(C24_Fader.FaderValue)
                                    Exit Select

                                End If
                            Next
                            Console.WriteLine(parts(1) & "|" & "?")
                        Else
                            Console.WriteLine("?" & "|" & "?")
                        End If
                    End If
                Case "C24desk_OnFaderVolChange"
                    If parts.Count < 4 Then
                        Console.WriteLine("Usage : C24desk_OnFaderVolChange <Group> <Name> <VolDb>")
                        Console.WriteLine("Exemple : C24desk_OnFaderVolChange ChannelStrip_1 Fader_1 -1")
                    Else
                        Dim group = _desk.GroupComponents(parts(1))
                        If group IsNot Nothing Then
                            For Each cpn In group
                                Dim C24_Fader = TryCast(cpn, Control24.Hardware.C24_Fader)
                                If C24_Fader IsNot Nothing AndAlso C24_Fader.Name = parts(2) Then
                                    Dim value As Integer = 0
                                    Dim trackNb As Integer = CInt(C24_Fader.Name.Split("_")(1))
                                    If Integer.TryParse(parts(3), value) Then

                                        C24_Fader.OnFaderManualChange(trackNb, value)
                                        Console.WriteLine(parts(1) & "|" & parts(2) & "=OK|Value=" & value)
                                        Exit Select
                                    Else
                                        Console.WriteLine("Value " & parts(3) & "is not correct value ! (ex of correct values : -2/-1/1/2 etc...")
                                    End If

                                End If
                            Next
                            Console.WriteLine(parts(1) & "|" & "?")
                        Else
                            Console.WriteLine("?" & "|" & "?")
                        End If
                    End If
                Case "C24desk_OnFaderNormChange"
                    If parts.Count < 4 Then
                        Console.WriteLine("Usage : C24desk_OnFaderNormChange <Group> <Name> <NormValue>")
                        Console.WriteLine("Exemple : C24desk_OnFaderNormChange ChannelStrip_1 Fader_1 -1")
                    Else
                        Dim group = _desk.GroupComponents(parts(1))
                        If group IsNot Nothing Then
                            For Each cpn In group
                                Dim C24_Fader = TryCast(cpn, Control24.Hardware.C24_Fader)
                                If C24_Fader IsNot Nothing AndAlso C24_Fader.Name = parts(2) Then
                                    Dim value As Integer = 0
                                    Dim trackNb As Integer = CInt(C24_Fader.Name.Split("_")(1))
                                    If Integer.TryParse(parts(3), value) Then

                                        C24_Fader.OnFaderManualChange(trackNb, , value)
                                        Console.WriteLine(parts(1) & "|" & parts(2) & "=OK|Value=" & value)
                                        Exit Select
                                    Else
                                        Console.WriteLine("Value " & parts(3) & "is not correct value ! (ex of correct values : -2/-1/1/2 etc...")
                                    End If

                                End If
                            Next
                            Console.WriteLine(parts(1) & "|" & "?")
                        Else
                            Console.WriteLine("?" & "|" & "?")
                        End If
                    End If
                Case "ConvertTxt2Csv"
                    If parts.Count < 3 Then
                        Console.WriteLine("Usage : ConvertTxt2Csv ""<inputTxtPath>"" ""<outputCsvPath>""")
                        Console.WriteLine("Exemple : ConvertTxt2Csv ""C:\mon dossier\capture.txt"" ""C:\mon dossier\output.csv""")
                    Else
                        Dim inputTxtPath As String = parts(1)
                        Dim outputCsvPath As String = parts(2)
                        If Not IO.File.Exists(inputTxtPath) Then
                            Console.WriteLine($"Erreur : fichier introuvable '{inputTxtPath}'")
                        Else
                            Try
                                Dim parser As New WiresharkLogParser()
                                parser.ConvertToCsv(inputTxtPath, outputCsvPath)
                                Console.WriteLine($"Conversion terminée → {outputCsvPath}")
                            Catch ex As Exception
                                Console.WriteLine($"Erreur lors de la conversion : {ex.Message}")
                            End Try
                        End If
                    End If
                Case "Print", "print", "prt"
                    Console.Write(vbCrLf)
                    If parts.Count = 2 AndAlso parts(1) = "ch" Then
                        'print channel bar
                        Console.Write("|")
                        For i = 0 To _desk.ScreenChannelBarScroll.Count - 1
                            Dim txt As String = String.Format("{0}|", _desk.ScreenChannelBarScroll.Strips(i).Text)
                            Console.Write(txt)
                        Next
                    ElseIf parts.Count = 3 AndAlso parts(1) = "led" Then
                        Select Case parts(2)
                            Case "vpot"
                                'print led de Vpot
                                Console.Write("|")
                                For Each cs In _desk.ChannelStrips
                                    Dim NbLedOn As Integer = 0
                                    Dim LedIndex As Integer? = Nothing
                                    For i = 0 To cs.Vpot.VpotLedCmd.Leds_Status.Count - 1
                                        If cs.Vpot.VpotLedCmd.Leds_Status(i) Then
                                            NbLedOn += 1
                                            LedIndex = i
                                        End If
                                    Next
                                    Dim txt As String = String.Format("{0};{1}|", NbLedOn, If(LedIndex IsNot Nothing, LedIndex, " "))
                                    Console.Write(txt)
                                Next
                            Case "mute"
                                'print led de mute
                                Console.Write("|")
                                For Each cs In _desk.ChannelStrips
                                    Dim txt As String = String.Format("Led:{0};IsBlinking:{1}|", If(cs.Mute.Led, "On", "Off"), If(cs.Mute.IsBlinking, "On", "Off"))
                                    Console.Write(txt)
                                Next
                            Case "solo"
                                'print led de solo
                                Console.Write("|")
                                For Each cs In _desk.ChannelStrips
                                    Dim txt As String = String.Format("Led:{0};IsBlinking:{1}|", If(cs.Solo.Led, "On", "Off"), If(cs.Solo.IsBlinking, "On", "Off"))
                                    Console.Write(txt)
                                Next
                            Case "auto"
                                'print led de automation
                                Console.Write("|")
                                For Each cs In _desk.ChannelStrips
                                    Dim autoLed = cs.AutoModeLed

                                    Dim txt As String = String.Format(autoLed.AutoMode.ToString & ";IsBlinking:{0}|", If(autoLed.IsBlinking, "On", "Off"))
                                    Console.Write(txt)
                                Next
                            Case "switchactive"
                                'print led de solo
                                Console.Write("|")
                                For Each cs In _desk.ChannelStrips
                                    Dim txt As String = String.Format("Led:{0};IsBlinking:{1}|", If(cs.SwitchActive.Led, "On", "Off"), If(cs.SwitchActive.IsBlinking, "On", "Off"))
                                    Console.Write(txt)
                                Next
                            Case "switchactiveled"
                                'print led de solo
                                Console.Write("|")
                                For Each cs In _desk.ChannelStrips
                                    Dim txt As String = String.Format("Led:{0};IsBlinking:{1}|", If(cs.LedSwitchActive.Led, "On", "Off"), If(cs.LedSwitchActive.IsBlinking, "On", "Off"))
                                    Console.Write(txt)
                                Next


                        End Select


                    Else

                        'print all screen
                        Console.Write("|")
                        For Each cs In _desk.ChannelStrips
                            Dim txt As String = String.Format("{0}|", cs.ScribbleStrip.Text)
                            Console.Write(txt)
                        Next

                    End If
                    Console.Write(vbCrLf)

                Case "ResetC24SENDORDER"
                    If parts.Count = 2 AndAlso parts(1) = "All" Then
                        'reset all 
                        For Each track In _controller._ReaperProj.Tracks
                            Reaper.utility.SetProjectExtState("C24SENDORDER", track.TrackIndex, "")
                            Console.WriteLine("Reset C24SENDORDER for track :" & track.TrackIndex)
                        Next
                    ElseIf parts.Count = 2 AndAlso parts(1) <> "All" Then
                        Dim trkIndex As Integer = -1
                        If Integer.TryParse(parts(1), trkIndex) Then
                            Reaper.utility.SetProjectExtState("C24SENDORDER", trkIndex, "")
                            Console.WriteLine("Reset C24SENDORDER for track :" & trkIndex)
                        Else
                            Console.Write(" trackIndex ou all ")
                        End If
                    End If

                Case "ctl"
                    If parts.Count = 2 AndAlso parts(1) = "h" Then

                        _controller.C24_Desk.Ctl_Clutch.ButtonCmd_OnButtonStatusChange(True)
                        Console.Write("Ctl is hold")
                    ElseIf parts.Count = 2 AndAlso parts(1) = "r" Then
                        _controller.C24_Desk.Ctl_Clutch.ButtonCmd_OnButtonStatusChange(False)
                        Console.Write("Ctl is released")
                    Else
                        Console.Write("option non valide")
                    End If
                Case "ctrl"
                    If parts.Count = 2 AndAlso parts(1) = "h" Then
                        _controller.C24_Desk.Ctrl.ButtonCmd_OnButtonStatusChange(True)
                        Console.Write("Ctrl is hold")
                    ElseIf parts.Count = 2 AndAlso parts(1) = "r" Then
                        _controller.C24_Desk.Ctrl.ButtonCmd_OnButtonStatusChange(False)
                        Console.Write("Ctrl is released")
                    Else
                        Console.Write("option non valide")
                    End If
                Case "opt"
                    If parts.Count = 2 AndAlso parts(1) = "h" Then
                        _controller.C24_Desk.Opt.ButtonCmd_OnButtonStatusChange(True)
                        Console.Write("Opt is hold")
                    ElseIf parts.Count = 2 AndAlso parts(1) = "r" Then
                        _controller.C24_Desk.Opt.ButtonCmd_OnButtonStatusChange(False)
                        Console.Write("Opt is released")
                    Else
                        Console.Write("option non valide")
                    End If
                Case "sht"
                    If parts.Count = 2 AndAlso parts(1) = "h" Then
                        _controller.C24_Desk.Shift.ButtonCmd_OnButtonStatusChange(True)
                        Console.Write("Shift is hold")
                    ElseIf parts.Count = 2 AndAlso parts(1) = "r" Then
                        _controller.C24_Desk.Shift.ButtonCmd_OnButtonStatusChange(False)
                        Console.Write("Shift is released")
                    Else
                        Console.Write("option non valide")
                    End If
                Case "nudge"
                    If parts.Count = 2 AndAlso parts(1) = "h" Then
                        _controller.C24_Desk.Nudge.ButtonCmd_OnButtonStatusChange(True)
                        Console.Write("Nudge is hold")
                    ElseIf parts.Count = 2 AndAlso parts(1) = "r" Then
                        _controller.C24_Desk.Nudge.ButtonCmd_OnButtonStatusChange(False)
                        Console.Write("Nudge is released")
                    Else
                        Console.Write("option non valide")
                    End If
                Case "Clear", "clear", "cls"
                    Console.Clear()
                Case Else
                    Console.WriteLine($"Commande inconnue : '{cmd}'. Tapez 'help'.")

            End Select
        End While
    End Sub

    ''' <summary>
    ''' Affiche tous les groupes et composants enregistrés dans <paramref name="desk"/>.
    ''' Pour les 24 ChannelStrips, seul ChannelStrip_1 est développé ;
    ''' les suivants sont affichés sur une seule ligne condensée.
    ''' </summary>
    Private Shared Sub PrintComponents(desk As Control24.Hardware.C24_Desk)
        If desk Is Nothing Then
            Console.WriteLine("(desk non initialisé)")
            Return
        End If

        Dim groups = desk.GroupComponents
        If groups Is Nothing OrElse groups.Count = 0 Then
            Console.WriteLine("Aucun groupe enregistré.")
            Return
        End If

        Console.WriteLine($"{groups.Count} groupe(s) enregistré(s) :")
        Console.WriteLine()

        For Each kvp In groups
            Dim groupName = kvp.Key
            Dim members = kvp.Value

            ' ── ChannelStrip_2 à ChannelStrip_24 : ligne condensée ────────────
            If groupName.StartsWith("ChannelStrip_", StringComparison.OrdinalIgnoreCase) AndAlso
               groupName <> "ChannelStrip_1" Then
                Console.WriteLine($"  [{groupName}]  ({members.Count} composant(s) — voir ChannelStrip_1)")
                Continue For
            End If

            Console.WriteLine($"  [{groupName}]  — {members.Count} composant(s)")
            For Each cpn In members
                Console.WriteLine($"      {cpn.Name}  ({cpn.GetType().Name})")
            Next
            Console.WriteLine()
        Next
    End Sub

    Private Shared Sub PrintHelp()
        Console.WriteLine("Commandes disponibles :")
        Console.WriteLine("  help                                        — Affiche cette aide")
        Console.WriteLine("  status                                      — État du controller et de la session Reaper")
        Console.WriteLine("  hub                                         — État de la connexion ReaperPollHub")
        Console.WriteLine("  bridge <commande>                           — Envoie une commande brute au bridge Reaper")
        Console.WriteLine("                                                Exemple : bridge COUNT_TRACKS")
        Console.WriteLine("  tracks                                      — Liste les pistes du projet actif")
        Console.WriteLine("  ctl/ctrl/opt/sht <option>                   — active/désactive le modifier")
        Console.WriteLine("  option : h                                  — hold : active le modifier")
        Console.WriteLine("  option : r                                  — release : désactive le modifier")
        Console.WriteLine("  interfaces                                  — Liste les interfaces réseau disponibles")
        Console.WriteLine("  components                                  — Liste les composants C24 enregistrés")
        Console.WriteLine("  C24desk_OnButtonReleased <Group> <Name>     — Simule le relâchement d'un bouton C24")
        Console.WriteLine("                                                Exemple : C24desk_OnButtonReleased ChannelStrip_1 Solo_1")
        Console.WriteLine("  C24desk_OnButtonPressed <Group> <Name>      — Simule la pression d'un bouton C24")
        Console.WriteLine("                                                Exemple : C24desk_OnButtonReleased ChannelStrip_1 Solo_1")
        Console.WriteLine("  C24desk_GetButtonStatus <Group> <Name>      — Retourne l état de la led d'un bouton C24")
        Console.WriteLine("                                                Exemple : C24desk_GetButtonStatus ChannelStrip_1 Solo_1 retourne ""Led=On|IsBlinking=Off""")
        Console.WriteLine("  C24desk_GetButtonStatus <Group>             — Retourne l état des leds de tous le group")
        Console.WriteLine("                                                Exemple : C24desk_GetButtonStatus ChannelStrip_1  retourne ""Mute1:Led=On;IsBlinking=Off etc...""")
        Console.WriteLine("  C24desk_OnVpotRotate <Group> <Name> <Value> — Simule la rotation d'un composant Vpot C24")
        Console.WriteLine("                                                Exemple : C24desk_OnVpotRotate ChannelStrip_1 Vpot_1 -1")
        Console.WriteLine("  C24desk_GetFaderValue <Group> <Name>        — Retourne la valeur de la position d'un composant Fader C24")
        Console.WriteLine("                                                Exemple : C24desk_OnFaderVolChange ChannelStrip_1 Fader_1")
        Console.WriteLine("  C24desk_OnFaderVolChange <Group> <Name> <VolDb> — Simule le deplacement d'un composant Fader C24")
        Console.WriteLine("                                                Exemple : C24desk_OnFaderVolChange ChannelStrip_1 Fader_1 -1")
        Console.WriteLine("  C24desk_OnFaderNormChange <Group> <Name> <NormValue> — Simule le deplacement d'un composant Fader C24")
        Console.WriteLine("                                                Exemple : C24desk_OnFaderNormChange ChannelStrip_1 Fader_1 -1")
        Console.WriteLine("  ConvertTxt2Csv <input> <output>             — Convertit un log Wireshark TXT en CSV")
        Console.WriteLine("                                                Exemple : ConvertTxt2Csv ""C:\cap.txt"" ""C:\out.csv""")
        Console.WriteLine("  Clear                                       — Efface la console")
        Console.WriteLine("  Print [ch]                                  — Affiche le text dans tous les écrans (24). ")
        Console.WriteLine("                                               Option avec ""ch"" pour afficher le channel bar")
        Console.WriteLine("  Print [led] <options>                       — Affiche le status des leds")
        Console.WriteLine("  options: vpot                                 Option avec ""vpot"" pour afficher ""nb de led On;l'index de la led On""")
        Console.WriteLine("  options: mute/solo                            Option avec ""mute/solo"" pour afficher le status des leds On/Off et Isblinking On/Off ")
        Console.WriteLine("  options: auto                                 Option avec ""auto"" pour afficher le status des leds de l'automation avec Isblinking On/Off ")
        Console.WriteLine("  options: switchactive                         Option avec ""switchactive"" pour afficher le status des leds On/Off et Isblinking On/Off de switchactive")
        Console.WriteLine("  options: switchactiveled                      Option avec ""switchactiveled"" pour afficher le status des leds On/Off et Isblinking On/Off des leds a coté de switchactive")
        Console.WriteLine("  ResetC24SENDORDER <option>                  — Efface dans le dictionnaire C24SENDORDER de clés/valeurs de Reaper ")
        Console.WriteLine("  option: trkIndex                              Option l'index de la piste 0,1,2,etc ")
        Console.WriteLine("  option: All                                   Option toutes les pistes")
        Console.WriteLine("  quit / exit                                 — Arrête le driver")
    End Sub
    Private Shared Function ParseCommandParts(input As String) As List(Of String)
        Dim result As New List(Of String)
        Dim current As New System.Text.StringBuilder()
        Dim inQuotes As Boolean = False

        For Each c As Char In input
            If c = """"c Then
                inQuotes = Not inQuotes
            ElseIf c = " "c AndAlso Not inQuotes Then
                If current.Length > 0 Then
                    result.Add(current.ToString())
                    current.Clear()
                End If
            Else
                current.Append(c)
            End If
        Next

        If current.Length > 0 Then
            result.Add(current.ToString())
        End If

        Return result
    End Function



    Private Shared Sub PrintStatus(controller As C24ReaperController)
        Console.WriteLine($"Interface C24    : {controller.C24_Desk?.NetworkInterface?.Name}")
        Dim trackCount = Reaper.CallReaperBridge("COUNT_TRACKS")
        Console.WriteLine($"Pistes Reaper    : {If(String.IsNullOrEmpty(trackCount), "N/A", trackCount)}")
        Dim filePath = Reaper.CallReaperBridge("GET_PROJECT_FILE_PATH")
        Console.WriteLine($"Projet actif     : {If(String.IsNullOrEmpty(filePath), "(non sauvegardé)", filePath)}")
        Console.WriteLine($"PollHub connecté : {ReaperPollHub.Instance.IsConnected}")
    End Sub

    Private Shared Sub PrintTracks(controller As C24ReaperController)
        Dim rawCount = Reaper.CallReaperBridge("COUNT_TRACKS")
        Dim count As Integer = 0
        If Not Integer.TryParse(rawCount, count) OrElse count = 0 Then
            Console.WriteLine("Aucune piste dans le projet actif.")
            Return
        End If
        Console.WriteLine($"Projet : {count} piste(s)")
        For i = 0 To count - 1
            Dim name = Reaper.CallReaperBridge($"GET_TRACK_NAME|{i}")
            Console.WriteLine($"  [{i}] {If(String.IsNullOrEmpty(name), "(sans nom)", name)}")
        Next
    End Sub

    Public Shared Sub DisplayInterfaceName()
        For Each Net_interface In NetworkInterface.GetAllNetworkInterfaces()
            Console.WriteLine(Net_interface.Name)
        Next
    End Sub

    Private Shared Sub HideConsoleWindow()
        Dim hwnd As IntPtr = GetConsoleWindow()
        If hwnd <> IntPtr.Zero Then
            ShowWindow(hwnd, SW_HIDE)
        End If
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                Shutdown()
            End If
            disposedValue = True
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(disposing:=True)
        GC.SuppressFinalize(Me)
    End Sub
End Class