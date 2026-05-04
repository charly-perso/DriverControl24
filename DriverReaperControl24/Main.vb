Imports System.Net.NetworkInformation
Imports System.Runtime.InteropServices
Imports DriverReaperControl24.Reaper.Monitors

Public Class Main
    <DllImport("kernel32.dll")>
    Private Shared Function GetConsoleWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    Private Const SW_HIDE As Integer = 0
    Private Const SW_SHOW As Integer = 5


    Private Shared desk As Control24.Hardware.C24_Desk = Nothing
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
                desk = New Control24.Hardware.C24_Desk(Nothing)
            Else

                Dim selectedInterface = NetworkInterface.GetAllNetworkInterfaces() _
                .FirstOrDefault(Function(n) n.Name = interfaceName)

                If selectedInterface Is Nothing Then
                    Dim message As String = $"ERROR: Interface '{interfaceName}' not found." & "Available interfaces are named in ReaperDriver.ini"
                    Reaper.CallReaperBridge("SHOW_MESSAGE_BOX|1|Control24 Driver|" & message)
                    Environment.Exit(1)
                End If

                Reaper.CallReaperBridge($"SHOW_MESSAGE_BOX|1|Control24 Driver|Starting Control24 driver on '{interfaceName}'...")

                ' ── Création du C24 Desk et démarrage réseau ──────────────────────
                desk = New Control24.Hardware.C24_Desk(selectedInterface)
                desk.StartNetworkCommunication()


                ' ── Attente de fin d'initialisation des composants C24 ────────────
                ' InitializeComponents() est appelé de façon asynchrone par le
                ' PacketManager dès réception du premier ACK du Control24.
                ' On bloque ici jusqu'à ce que tous les composants soient prêts.
                Const InitTimeoutMs As Integer = 30_000
                Dim initialized As Boolean = desk.WaitForInitialization(InitTimeoutMs)

                If Not initialized Then
                    Reaper.CallReaperBridge("SHOW_MESSAGE_BOX|1|Control24 Driver|ERROR: Control24 initialization timeout. Check hardware connection.")
                    Environment.Exit(1)
                End If

            End If
            ' ── Création du controller Reaper ↔ Control24 ─────────────────────
            Dim _C24ReaperController As New C24ReaperController(desk)

            ' ── Boucle d'attente — processus headless ─────────────────────────
            AddHandler Console.CancelKeyPress,
                Sub(s, e)
                    e.Cancel = True
                    Reaper.CallReaperBridge("SHOW_MESSAGE_BOX|1|Control24 Driver|Stopping...")
                    Environment.Exit(0)
                End Sub


            If interfaceName = "TestWithoutC24" Then
                ' ── Boucle de commandes console ───────────────────────────────────
                RunConsoleLoop(_C24ReaperController)
            Else
                'hide console
                ShowWindow(GetConsoleWindow(), SW_HIDE)
                Threading.Thread.Sleep(Threading.Timeout.Infinite)
            End If




        End If

    End Sub

    ''' <summary>
    ''' Lit et exécute les commandes tapées dans la console.
    ''' Taper <c>help</c> pour afficher la liste des commandes disponibles.
    ''' </summary>
    Private Shared Sub RunConsoleLoop(controller As C24ReaperController)
        Console.WriteLine("Driver Control24 démarré. Tapez 'help' pour la liste des commandes.")

        While True
            Console.Write("> ")
            Dim input = Console.ReadLine()
            If input Is Nothing Then Continue While

            Dim parts = input.Trim().Split(" "c, StringSplitOptions.RemoveEmptyEntries)
            If parts.Length = 0 Then Continue While

            Dim cmd = parts(0)

            Select Case cmd

                Case "help"
                    PrintHelp()

                Case "quit", "exit"
                    Console.WriteLine("Arrêt du driver...")
                    Reaper.CallReaperBridge("SHOW_MESSAGE_BOX|1|Control24 Driver|Stopping...")
                    Environment.Exit(0)

                Case "status"
                    PrintStatus(controller)

                Case "hub"
                    Console.WriteLine($"ReaperPollHub connecté : {ReaperPollHub.Instance.IsConnected}")

                Case "bridge"
                    If parts.Length < 2 Then
                        Console.WriteLine("Usage : bridge <commande>")
                        Console.WriteLine("Exemple : bridge COUNT_TRACKS")
                    Else
                        ' ── Reconstituer la commande depuis les tokens restants ─
                        Dim bridgeCmd = String.Join(" ", parts, 1, parts.Length - 1)
                        Dim result = Reaper.CallReaperBridge(bridgeCmd)
                        Console.WriteLine($"→ {If(String.IsNullOrEmpty(result), "(vide)", result)}")
                    End If

                Case "tracks"
                    PrintTracks(controller)

                Case "interfaces"
                    DisplayInterfaceName()

                Case "C24desk_OnButtonReleased"
                    If parts.Length < 2 Then
                        Console.WriteLine("Usage : C24desk_OnButtonReleased <Group> <Name>")
                        Console.WriteLine("Exemple : C24desk_OnButtonReleased ChannelStrip_1 Solo_1")
                    Else

                        Dim C24_cpn = String.Join(" ", parts, 1, parts.Length - 1)
                        Dim group = desk.GroupComponents(parts(1))
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

                        Case Else
                    Console.WriteLine($"Commande inconnue : '{cmd}'. Tapez 'help'.")

            End Select

        End While
    End Sub

    ''' <summary>Affiche la liste des commandes disponibles.</summary>
    Private Shared Sub PrintHelp()
        Console.WriteLine("Commandes disponibles :")
        Console.WriteLine("  help                  — Affiche cette aide")
        Console.WriteLine("  status                — État du controller et de la session Reaper")
        Console.WriteLine("  hub                   — État de la connexion ReaperPollHub")
        Console.WriteLine("  bridge <commande>     — Envoie une commande brute au bridge Reaper")
        Console.WriteLine("                          Exemple : bridge COUNT_TRACKS")
        Console.WriteLine("  tracks                — Liste les pistes du projet actif")
        Console.WriteLine("  interfaces            — Liste les interfaces réseau disponibles")
        Console.WriteLine("  quit / exit           — Arrête le driver")
    End Sub

    ''' <summary>Affiche l'état général du controller.</summary>
    Private Shared Sub PrintStatus(controller As C24ReaperController)
        Console.WriteLine($"Interface C24    : {controller.C24_Desk?.NetworkInterface?.Name}")
        Dim trackCount = Reaper.CallReaperBridge("COUNT_TRACKS")
        Console.WriteLine($"Pistes Reaper    : {If(String.IsNullOrEmpty(trackCount), "N/A", trackCount)}")
        Dim filePath = Reaper.CallReaperBridge("GET_PROJECT_FILE_PATH")
        Console.WriteLine($"Projet actif     : {If(String.IsNullOrEmpty(filePath), "(non sauvegardé)", filePath)}")
        Console.WriteLine($"PollHub connecté : {ReaperPollHub.Instance.IsConnected}")
    End Sub

    ''' <summary>Affiche la liste des pistes du projet actif.</summary>
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
    Private Shared Function LoadConfigInterfaceName() As String
        Dim configPath = IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ReaControl24", "control24_config.json")

        If Not IO.File.Exists(configPath) Then Return String.Empty
        Dim json = IO.File.ReadAllText(configPath)
        Dim match = Text.RegularExpressions.Regex.Match(json, """interface""\s*:\s*""([^""]+)""")
        Return If(match.Success, match.Groups(1).Value, String.Empty)
    End Function

    Public Shared Sub DisplayInterfaceName()
        For Each Net_interface In NetworkInterface.GetAllNetworkInterfaces()
            Console.WriteLine(Net_interface.Name)
        Next
    End Sub

End Class