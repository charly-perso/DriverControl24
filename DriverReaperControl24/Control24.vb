Imports System.Collections.Concurrent
Imports System.Diagnostics.CodeAnalysis
Imports System.Globalization
Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Security.Cryptography
Imports System.Text.RegularExpressions
Imports System.Threading
Imports DriverReaperControl24.Control24.Commands
Imports DriverReaperControl24.Utility.Bit
Imports DriverReaperControl24.Utility


Namespace Control24
    Public Enum C24_SoloTypes
        Solo
        NotSolo
        SoloSafe
    End Enum
    Namespace Hardware

        Module Utility
            Public OrderToAutoModeButton As New Dictionary(Of Integer, Commands.AutoMode) From {
    {0, Commands.AutoMode.Write},
    {1, Commands.AutoMode.Touch},
    {2, Commands.AutoMode.Latch},
    {3, Commands.AutoMode.Trim},
    {4, Commands.AutoMode.Read},
    {5, Commands.AutoMode.None}
}
            Public AutoModeButtonToOrder As New Dictionary(Of Commands.AutoMode, Integer) From {
    {Commands.AutoMode.Write, 0},
    {Commands.AutoMode.Touch, 1},
    {Commands.AutoMode.Latch, 2},
    {Commands.AutoMode.Trim, 3},
    {Commands.AutoMode.Read, 4},
    {Commands.AutoMode.None, 5}
}
            Public Function GetScribbleTextfromDouble(Value As Double) As String
                Dim ic = CultureInfo.InvariantCulture
                Dim ValueAbs = Math.Abs(Value)
                Dim Text As String = ""
                ' ── [1 000 000 000+] → ±inf ───────────────────────────────────
                If ValueAbs >= 1000000000 Then
                    If Value < 0 Then
                        Text = "-inf"
                    Else
                        Text = "+inf"
                    End If

                    ' ── [1 000 000 .. 999 999 999] → ex : "12m3" ──────────────────
                ElseIf ValueAbs >= 1000000 Then
                    Dim newVal As Double = Math.Round(Value / 1000000.0, 1)
                    Text = newVal.ToString("F0", ic) & "m" & GetCombineGlyph(newVal)

                    ' ── [1 000 .. 999 999] → ex : "1k5" ──────────────────────────
                ElseIf ValueAbs >= 1000 Then
                    Dim newVal As Double = Math.Round(Value / 1000.0, 1)
                    Text = newVal.ToString("F0", ic) & "k" & GetCombineGlyph(newVal)

                    ' ── [100 .. 999] → entier sans décimale ───────────────────────
                ElseIf ValueAbs >= 100 Then
                    Text = Value.ToString("F0", ic)

                    ' ── [0 .. 99] → entier + décimale combinée ────────────────────
                Else
                    Text = Value.ToString("F0", ic) & GetCombineGlyph(Value)
                End If

                Return Text
            End Function
            Private Function GetCombineGlyph(Number As Double) As String
                Dim parts = Number.ToString("G", CultureInfo.InvariantCulture).Split("."c)

                If parts.Length < 2 OrElse parts(1).Length = 0 Then
                    Return "."
                End If

                Dim nco As Integer = AscW(parts(1)(0))

                If nco <> 48 Then
                    Return ChrW(nco - 26).ToString()
                End If

                Return "."
            End Function
        End Module




        Public Class C24_Components
            Private ReadOnly _IdsToC24Components As Concurrent.ConcurrentDictionary(Of String, C24_Component) = New Concurrent.ConcurrentDictionary(Of String, C24_Component)
            Private ReadOnly _NameToC24Components As Dictionary(Of String, C24_Component) = New Dictionary(Of String, C24_Component)
            Private ReadOnly _AutoRefresh_C24_Components As List(Of C24_Component) = New List(Of C24_Component)
            Private ReadOnly _CmdToSendLatest As New ConcurrentDictionary(Of String, Byte())
            Private ReadOnly _GroupComponents As Dictionary(Of String, List(Of C24_Component)) = New Dictionary(Of String, List(Of C24_Component))
            Public Event BlinkStateChange(State As Boolean)


            Public ReadOnly Property IdsToC24Components As Concurrent.ConcurrentDictionary(Of String, C24_Component)
                Get
                    Return _IdsToC24Components
                End Get
            End Property
            Public ReadOnly Property NameToC24Components As Dictionary(Of String, C24_Component)
                Get
                    Return _NameToC24Components
                End Get
            End Property
            Public ReadOnly Property AutoRefresh_C24_Components As List(Of C24_Component)
                Get
                    Return _AutoRefresh_C24_Components
                End Get
            End Property

            Public ReadOnly Property GroupComponents As Dictionary(Of String, List(Of C24_Component))
                Get
                    Return _GroupComponents
                End Get
            End Property

            ''' <summary>
            ''' Components which are refresh on C24 when component properties are changed and component with C24received commands.
            ''' </summary>
            ''' <param name="C24_Component"></param>
            Sub Add_C24_Component(C24_Component As C24_Component, Optional GroupName As String = Nothing)
                IdsToC24Components(C24_Component.Component_Id) = C24_Component
                NameToC24Components.Add(C24_Component.Name, C24_Component)
                For Each C24_Command In C24_Component.C24_SendingCommands
                    AddHandler C24_Command.NeedToSendCmd, AddressOf EnqueueCmd
                Next

                AddTogroupName(GroupName, C24_Component)

                'Register Blinking buttons
                Dim C24_button As C24_LedButton = TryCast(C24_Component, C24_LedButton)
                If C24_button IsNot Nothing Then
                    AddHandler Me.BlinkStateChange, AddressOf C24_button.BlinkingLed
                End If

                ' ── ScribbleStrip clignotant ───────────────────────────────────
                Dim C24_scribble As C24_ScribbleStrip = TryCast(C24_Component, C24_ScribbleStrip)
                If C24_scribble IsNot Nothing Then
                    AddHandler Me.BlinkStateChange, AddressOf C24_scribble.BlinkingScribble
                End If

                'Register Blinking Led
                Dim C24_AutoModeLed As C24_AutoModeLed = TryCast(C24_Component, C24_AutoModeLed)
                If C24_AutoModeLed IsNot Nothing Then
                    AddHandler Me.BlinkStateChange, AddressOf C24_AutoModeLed.BlinkingLed
                End If

            End Sub
            ''' <summary>
            ''' Components which are refreshed on C24 only with the Desk SendingTimingQueueThread you can set on the Desk component.
            ''' </summary>
            ''' <param name="C24_Component"></param>
            Sub Add_AutoRefresh_C24_Component(C24_Component As C24_Component, Optional GroupName As String = Nothing)
                IdsToC24Components(C24_Component.Component_Id) = C24_Component
                NameToC24Components.Add(C24_Component.Name, C24_Component)
                AutoRefresh_C24_Components.Add(C24_Component)
                AddTogroupName(GroupName, C24_Component)

            End Sub


            Public Sub AddTogroupName(GroupName As String, C24_Component As C24_Component)
                If GroupName IsNot Nothing AndAlso GroupName <> "" Then
                    If GroupComponents.ContainsKey(GroupName) = False Then
                        GroupComponents.Add(GroupName, New List(Of C24_Component))
                    End If
                    GroupComponents(GroupName).Add(C24_Component)
                End If
            End Sub

            Public Sub OnBlinkStateChange(state As Boolean)
                RaiseEvent BlinkStateChange(state)
            End Sub
            Private Sub EnqueueCmd(Cmd As Commands.Cmd)
                Dim _bytes(Cmd.Cmd.Length - 1) As Byte
                Cmd.Cmd.CopyTo(_bytes, 0)
                ' ── AddOrUpdate : le dernier état gagne toujours ──────────────
                _CmdToSendLatest(Cmd.Cmd_Id) = _bytes
            End Sub

            ''' <summary>
            ''' Draine toutes les dernières valeurs en attente et retourne la liste.
            ''' Atomic : vide le dictionnaire en une seule opération.
            ''' </summary>
            Public Function DrainLatestCmds() As List(Of Byte())
                Dim result As New List(Of Byte())
                For Each key In _CmdToSendLatest.Keys
                    Dim bytes As Byte() = Nothing
                    If _CmdToSendLatest.TryRemove(key, bytes) Then
                        result.Add(bytes)
                    End If
                Next
                Return result
            End Function
        End Class
        Public MustInherit Class C24_Component

            Public _Component_Id As String
            Public _C24_SendingCommands As New List(Of Commands.Cmd)
            Public _Name As String
            Public _C24_ReceivingCommand As Cmd

            Public ReadOnly Property Name As String
                Get
                    Return _Name
                End Get

            End Property

            Public ReadOnly Property Component_Id As String
                Get
                    Return _Component_Id
                End Get

            End Property

            Public ReadOnly Property C24_ReceivingCommand As Commands.Cmd
                Get
                    Return _C24_ReceivingCommand
                End Get
            End Property

            Public ReadOnly Property C24_SendingCommands As List(Of Commands.Cmd)
                Get
                    Return _C24_SendingCommands
                End Get
            End Property


        End Class
        ''' <summary>
        ''' Représente un bouton physique du Control24 sans LED.
        ''' Détecte uniquement les appuis/relâchements provenant du hardware.
        ''' Ne génère aucune commande vers le C24 (pas de <see cref="C24_Component.C24_SendingCommands"/>).
        ''' </summary>
        ''' <summary>
        ''' Représente un bouton physique du Control24 sans LED.
        ''' Détecte uniquement les appuis/relâchements provenant du hardware.
        ''' Ne génère aucune commande vers le C24 (pas de <see cref="C24_Component.C24_SendingCommands"/>).
        ''' </summary>
        Public Class C24_Button
            Inherits C24_Component
            Private _TrackNb As Integer? = Nothing
            Private WithEvents _ButtonCmd As Commands.Button_Cmd = Nothing

            ''' <summary>Déclenché quand le bouton est pressé sur le C24.</summary>
            Public Event OnButtonPressed(TrackNb As Integer?)

            ''' <summary>Déclenché quand le bouton est relâché sur le C24.</summary>
            Public Event OnButtonReleased(TrackNb As Integer?)


            ''' <summary>True si le bouton est actuellement pressé.</summary>
            Public ReadOnly Property IsPressed As Boolean
                Get
                    Return _ButtonCmd.Is_On
                End Get
            End Property

            ''' <summary>Commande sous-jacente (accès aux bytes reçus).</summary>
            Public ReadOnly Property ButtonCmd As Commands.Button_Cmd
                Get
                    Return _ButtonCmd
                End Get
            End Property

            ''' <summary>
            ''' Crée un bouton de piste sans LED.
            ''' </summary>
            ''' <param name="Name">Nom unique du composant.</param>
            ''' <param name="Button_Nb">Byte identifiant le bouton sur la piste.</param>
            ''' <param name="Track_Nb">Numéro de piste 1-based.</param>
            Public Sub New(Name As String, Button_Nb As Byte, Track_Nb As Integer)
                _Name = Name
                _ButtonCmd = New Commands.Track_Button_Cmd(Button_Nb, Track_Nb)
                _TrackNb = Track_Nb
                _C24_ReceivingCommand = _ButtonCmd
                Me._Component_Id = _ButtonCmd.Cmd_Id
            End Sub

            ''' <summary>
            ''' Crée un bouton de région sans LED.
            ''' </summary>
            ''' <param name="Name">Nom unique du composant.</param>
            ''' <param name="Region">Région du panneau Control24.</param>
            ''' <param name="Button_Nb">Byte identifiant le bouton dans la région.</param>
            Public Sub New(Name As String, Region As Commands.Btn_Region, Button_Nb As Byte)
                _Name = Name
                _ButtonCmd = New Commands.Region_Button_Cmd(Region, Button_Nb)
                _C24_ReceivingCommand = _ButtonCmd
                Me._Component_Id = _ButtonCmd.Cmd_Id
            End Sub

            Public Sub ButtonCmd_OnButtonStatusChange(Status As Boolean) Handles _ButtonCmd.OnButtonStatusChange
                _ButtonCmd.Is_On = Status
                If Status Then

                    RaiseEvent OnButtonPressed(_TrackNb)
                Else

                    RaiseEvent OnButtonReleased(_TrackNb)
                End If
            End Sub

        End Class

        ''' <summary>
        ''' Représente un bouton avec LED du Control24.
        ''' Hérite de <see cref="C24_Button"/> pour la détection d'appuis hardware
        ''' et ajoute la gestion de l'état LED (envoi vers le C24) ainsi que le clignotement.
        ''' </summary>
        Public Class C24_LedButton
            Inherits C24_Button
            Private _Led As Boolean = False
            Private _IsBlinking As Boolean

            ''' <summary>Active ou désactive le clignotement de la LED.</summary>
            Public Property IsBlinking As Boolean
                Get
                    Return _IsBlinking
                End Get
                Set
                    If Value = False Then
                        Led = False
                    End If

                    If Value <> _IsBlinking Then

                        _IsBlinking = Value




                        'If Me.Name.Contains("Mute") Or Me.Name.Contains("Solo") Then
                        '    If _IsBlinking Then
                        '        Console.WriteLine(Me.Name & "|Blinking")
                        '    Else
                        '        Console.WriteLine(Me.Name & "|Not Blinking")
                        '    End If
                        'End If

                    End If
                End Set
            End Property

            Public Property Led As Boolean
                Get
                    Return _Led
                End Get
                Set
                    If Value <> _Led Then

                        _Led = Value
                        ButtonCmd.Is_On = _Led
                        ButtonCmd.SendCmd()

                        'If Me.Name.Contains("Mute") Or Me.Name.Contains("Solo") Then
                        '    If _Led Then
                        '        Console.WriteLine(Me.Name & "|On")
                        '    Else
                        '        Console.WriteLine(Me.Name & "|Off")
                        '    End If

                        'End If

                    End If

                End Set
            End Property

            ''' <summary>
            ''' Crée un bouton LED de piste.
            ''' </summary>
            ''' <param name="Name">Nom unique du composant.</param>
            ''' <param name="Button_Nb">Byte identifiant le bouton sur la piste.</param>
            ''' <param name="Track_Nb">Numéro de piste 1-based.</param>
            Public Sub New(Name As String, Button_Nb As Byte, Track_Nb As Integer)
                MyBase.New(Name, Button_Nb, Track_Nb)
                C24_SendingCommands.Add(ButtonCmd)
            End Sub

            ''' <summary>
            ''' Crée un bouton LED de région.
            ''' </summary>
            ''' <param name="Name">Nom unique du composant.</param>
            ''' <param name="Region">Région du panneau Control24.</param>
            ''' <param name="Button_Nb">Byte identifiant le bouton dans la région.</param>
            Public Sub New(Name As String, Region As Commands.Btn_Region, Button_Nb As Byte)
                MyBase.New(Name, Region, Button_Nb)
                C24_SendingCommands.Add(ButtonCmd)
            End Sub

            ''' <summary>
            ''' Appelé par le timer de blink du <see cref="C24_Desk"/>.
            ''' Fait clignoter la LED si <see cref="IsBlinking"/> est activé.
            ''' </summary>
            Public Sub BlinkingLed(Blink_State As Boolean)
                If IsBlinking Then
                    Led = Blink_State
                End If
            End Sub

        End Class
        ''' <summary>
        ''' Pilote les LEDs du mode d'automation d'un channel strip Control24.
        ''' Composant d'affichage uniquement — envoi vers le C24 via
        ''' <see cref="Commands.AutoModeLed_Cmd"/>.
        ''' Ne reçoit aucune commande depuis le hardware.
        ''' </summary>
        Public Class C24_AutoModeLed
            Inherits C24_Component

            Private ReadOnly _AutoModeLedCmd As Commands.AutoModeLed_Cmd
            Private _AutoMode As Commands.AutoMode = Commands.AutoMode.None
            Private _IsBlinking As Boolean

            Public Event On_AutomodeChange(AutoMode As AutoMode)

            ' ── Propriétés ────────────────────────────────────────────────────
            ''' <summary>Active ou désactive le clignotement de la LED.</summary>
            Public Property IsBlinking As Boolean
                Get
                    Return _IsBlinking
                End Get
                Set
                    If Value <> _IsBlinking Then
                        _IsBlinking = Value
                        'remet la led on a l'arret du blinking
                        If Value = False Then
                            _AutoModeLedCmd.AutoModeLed = _AutoMode
                        End If
                    End If
                End Set
            End Property
            ''' <summary>
            ''' Mode d'automation affiché sur les LEDs.
            ''' Envoie immédiatement la commande vers le C24.
            ''' </summary>
            Public Property AutoMode As Commands.AutoMode
                Get
                    Return _AutoMode
                End Get
                Set
                    If Value <> _AutoMode Then
                        _AutoMode = Value
                        _AutoModeLedCmd.AutoModeLed = _AutoMode
                        RaiseEvent On_AutomodeChange(_AutoMode)
                    End If
                End Set
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Crée le composant AutoMode LED pour la piste <paramref name="Track_Nb"/>.
            ''' </summary>
            ''' <param name="Name">Nom unique du composant.</param>
            ''' <param name="Track_Nb">Numéro de piste 1-based.</param>
            Public Sub New(Name As String, Track_Nb As Integer)
                _Name = Name
                _AutoModeLedCmd = New Commands.AutoModeLed_Cmd(Track_Nb)
                C24_SendingCommands.Add(_AutoModeLedCmd)
                Me._Component_Id = _AutoModeLedCmd.Cmd_Id
            End Sub

            ' ── Helpers ───────────────────────────────────────────────────────

            ''' <summary>
            ''' Éteint toutes les LEDs AutoMode.
            ''' </summary>
            Public Sub ClearLeds()
                AutoMode = Commands.AutoMode.None
            End Sub

            ''' <summary>
            ''' Applique le mode d'automation Reaper (I_AUTOMODE) sur les LEDs.
            ''' </summary>
            ''' <param name="reaperAutoMode">0=Trim/Off  1=Read  2=Touch  3=Write  4=Latch</param>
            Public Sub SetFromReaperAutoMode(reaperAutoMode As Integer)
                Select Case reaperAutoMode
                    Case 0
                        AutoMode = Commands.AutoMode.Trim
                    Case 1
                        AutoMode = Commands.AutoMode.Read
                    Case 2
                        AutoMode = Commands.AutoMode.Touch
                    Case 3
                        AutoMode = Commands.AutoMode.Write
                    Case 4
                        AutoMode = Commands.AutoMode.Latch
                    Case Else
                        AutoMode = Commands.AutoMode.None
                End Select
            End Sub

            ''' <summary>
            ''' Appelé par le timer de blink du <see cref="C24_Desk"/>.
            ''' Fait clignoter la LED si <see cref="IsBlinking"/> est activé.
            ''' </summary>
            Public Sub BlinkingLed(Blink_State As Boolean)
                If IsBlinking Then
                    If Blink_State Then
                        _AutoModeLedCmd.AutoModeLed = _AutoMode
                    Else
                        _AutoModeLedCmd.AutoModeLed = AutoMode.None
                    End If
                End If
            End Sub
        End Class

        ''' <summary>
        ''' Pilote l'affichage horloge du Control24.
        ''' Regroupe les LEDs de mode (<see cref="Commands.ClockLed_Cmd"/>)
        ''' et l'affichage 7 segments (<see cref="Commands.Clock_Cmd"/>).
        ''' Composant d'affichage uniquement — aucune commande reçue depuis le hardware.
        ''' </summary>
        ''' <summary>
        ''' Pilote l'affichage horloge du Control24.
        ''' Regroupe les LEDs de mode (<see cref="Commands.ClockLed_Cmd"/>)
        ''' et l'affichage 7 segments (<see cref="Commands.Clock_Cmd"/>).
        ''' Composant d'affichage uniquement — aucune commande reçue depuis le hardware.
        ''' </summary>
        Public Class C24_Clock
            Inherits C24_Component

            Private ReadOnly _ClockLedCmd As Commands.ClockLed_Cmd
            Private ReadOnly _ClockCmd As Commands.Clock_Cmd
            Private _ClockMode As Commands.ClockModes = Commands.ClockModes.HourMinuteSecondMS
            Private _Clock_Value As String = ""
            Private _bpm As Integer = 120
            Private _beatsPerBar As Integer = 4

            ' ── Constantes FeetFrames ─────────────────────────────────────────
            ''' <summary>Fréquence d'images pour le mode FeetFrames (35mm = 24 fps).</summary>
            Public Const FeetFramesFps As Integer = 24
            ''' <summary>Nombre de frames par foot (35mm = 16 frames/foot).</summary>
            Public Const FramesPerFoot As Integer = 16

            ' ── Constantes HourMinuteSecondFrames ─────────────────────────────
            ''' <summary>Fréquence d'images pour le mode HourMinuteSecondFrames.</summary>
            Public Const HmsfFps As Integer = 30

            ' ── Propriétés ────────────────────────────────────────────────────
            Public Property beatsPerBar As Integer
                Get
                    Return _beatsPerBar
                End Get
                Set
                    If Value <> _beatsPerBar Then
                        _beatsPerBar = Value
                    End If
                End Set
            End Property

            Public Property bpm As Integer
                Get
                    Return _bpm
                End Get
                Set
                    If Value <> _bpm Then
                        _bpm = Value
                    End If

                End Set
            End Property

            ''' <summary>
            ''' Mode d'affichage de l'horloge.
            ''' Met à jour simultanément les LEDs de mode et les points des séparateurs.
            ''' </summary>
            Public Property ClockMode As Commands.ClockModes
                Get
                    Return _ClockMode
                End Get
                Set
                    If Value <> _ClockMode Then
                        _ClockMode = Value
                        _ClockLedCmd.ClockMode = _ClockMode
                        _ClockCmd.ClockMode = _ClockMode
                        ' ── Renvoie la valeur courante avec le nouveau mode ────────
                        If _Clock_Value IsNot Nothing AndAlso _Clock_Value.Length > 0 Then
                            _ClockCmd.Clock_Value = _Clock_Value
                        End If
                    End If
                End Set
            End Property

            ''' <summary>
            ''' Valeur affichée sur les 8 digits 7-segments.
            ''' Envoie immédiatement la commande vers le C24.
            ''' </summary>
            Public Property Clock_Value As String
                Get
                    Return _Clock_Value
                End Get
                Set
                    If Value <> _Clock_Value Then
                        _Clock_Value = Value
                        _ClockCmd.Clock_Value = _Clock_Value
                    End If
                End Set
            End Property

            ''' <summary>Accès direct à la commande LED de mode horloge.</summary>
            Public ReadOnly Property ClockLedCmd As Commands.ClockLed_Cmd
                Get
                    Return _ClockLedCmd
                End Get
            End Property

            ''' <summary>Accès direct à la commande d'affichage 7 segments.</summary>
            Public ReadOnly Property ClockCmd As Commands.Clock_Cmd
                Get
                    Return _ClockCmd
                End Get
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>Crée le composant horloge du Control24.</summary>
            ''' <param name="Name">Nom unique du composant.</param>
            Public Sub New(Name As String)
                _Name = Name
                _ClockLedCmd = New Commands.ClockLed_Cmd()
                _ClockCmd = New Commands.Clock_Cmd()
                C24_SendingCommands.Add(_ClockLedCmd)
                C24_SendingCommands.Add(_ClockCmd)
                Me._Component_Id = _ClockLedCmd.Cmd_Id
            End Sub

            ' ── Helpers ───────────────────────────────────────────────────────

            ''' <summary>
            ''' Met à jour simultanément le mode et la valeur affichée.
            ''' </summary>
            ''' <param name="mode">Mode d'affichage.</param>
            ''' <param name="value">Valeur à afficher sur les 8 digits.</param>
            Public Sub SetClock(mode As Commands.ClockModes, value As String)
                ClockMode = mode
                Clock_Value = value
            End Sub
            ''' <summary>
            ''' Remplace les zéros de tête dans chaque groupe de digits par des espaces,
            ''' .
            ''' </summary>
            ''' <param name="value">Chaîne de 8 digits.</param>
            ''' <param name="groups">Tailles des groupes (somme doit être = 8).</param>
            Private Shared Function TrimLeadingZerosInGroups(value As String, groups As Integer()) As String
                Dim result As Char() = value.ToCharArray()
                Dim pos As Integer = 0
                For Each groupSize In groups
                    ' ── Remplace les zéros de tête ──
                    For i = pos To pos + groupSize - 1
                        If result(i) = "0"c Then
                            result(i) = " "c
                        Else
                            Exit For
                        End If
                    Next
                    pos += groupSize
                Next
                Return New String(result)
            End Function
            ''' <summary>
            ''' Convertit un temps en millisecondes dans le format sélectionné par
            ''' <see cref="ClockMode"/> et met à jour l'affichage via <see cref="Clock_Value"/>.
            ''' <para>
            ''' Formats produits (8 digits, séparateurs gérés par le hardware) :
            ''' <list type="table">
            ''' <item><term>HourMinuteSecondMS</term>
            '''       <description>HHMMSSCC — CC = centisecondes (ms ÷ 10)</description></item>
            ''' <item><term>HourMinuteSecondFrames</term>
            '''       <description>HHMMSSFF — FF = frames à <see cref="HmsfFps"/> fps (défaut 30)</description></item>
            ''' <item><term>FeetFrames</term>
            '''       <description>FFFFFFNN — FFFFFF = feet, NN = frames à <see cref="FeetFramesFps"/> fps / <see cref="FramesPerFoot"/> frames/foot</description></item>
            ''' <item><term>BarBeats</term>
            '''       <description>BBBBBbTT — BBBBB = mesures, b = temps (1-based), TT = ticks 0-99</description></item>
            ''' </list>
            ''' </para>
            ''' </summary>
            ''' <param name="ms">Position en millisecondes (valeur positive).</param>
            ''' <param name="bpm">
            ''' Tempo en BPM, utilisé uniquement en mode <see cref="Commands.ClockModes.BarBeats"/>.
            ''' Défaut : 120.
            ''' </param>
            ''' <param name="beatsPerBar">
            ''' Nombre de temps par mesure, utilisé uniquement en mode <see cref="Commands.ClockModes.BarBeats"/>.
            ''' Défaut : 4.
            ''' </param>
            Public Sub Set_Time_Ms(ms As Long)
                Dim totalMs As Long = Math.Max(0, ms)
                Dim value As String

                Select Case _ClockMode

                    Case Commands.ClockModes.HourMinuteSecondMS
                        ' ── H MM SS mmm ─────────────────────────────────────────────
                        Dim h As Integer = CInt(totalMs \ 3600000L)
                        Dim m As Integer = CInt((totalMs Mod 3600000L) \ 60000L)
                        Dim s As Integer = CInt((totalMs Mod 60000L) \ 1000L)
                        Dim ms3 As Integer = CInt(totalMs Mod 1000L)
                        value = h.ToString("D1") & m.ToString("D2") & s.ToString("D2") & ms3.ToString("D3")

                    Case Commands.ClockModes.HourMinuteSecondFrames
                        ' ── HH MM SS FF (HmsfFps fps) ────────────────────────────────
                        Dim h As Integer = CInt(totalMs \ 3600000L)
                        Dim m As Integer = CInt((totalMs Mod 3600000L) \ 60000L)
                        Dim s As Integer = CInt((totalMs Mod 60000L) \ 1000L)
                        Dim ff As Integer = CInt((totalMs Mod 1000L) * CLng(HmsfFps) \ 1000L)
                        value = h.ToString("D2") & m.ToString("D2") & s.ToString("D2") & ff.ToString("D2")

                    Case Commands.ClockModes.FeetFrames
                        ' ── FFFFFF NN (35mm : FeetFramesFps fps, FramesPerFoot frames/foot) ──
                        Dim totalFrames As Long = totalMs * CLng(FeetFramesFps) \ 1000L
                        Dim feet As Long = totalFrames \ CLng(FramesPerFoot)
                        Dim frames As Integer = CInt(totalFrames Mod CLng(FramesPerFoot))
                        value = (feet Mod 1000000L).ToString("D6") & frames.ToString("D2")

                    Case Commands.ClockModes.BarBeats
                        ' ── BBBBB b TT (bpm, beatsPerBar) ───────────────────────────
                        Dim safeBpm As Integer = Math.Max(1, bpm)
                        Dim safeBpB As Integer = Math.Max(1, beatsPerBar)
                        Dim msPerBeat As Long = 60000L \ CLng(safeBpm)
                        Dim totalBeats As Long = totalMs \ msPerBeat
                        Dim bar As Long = totalBeats \ CLng(safeBpB) + 1L
                        Dim beat As Integer = CInt(totalBeats Mod CLng(safeBpB)) + 1
                        Dim tick As Integer = CInt((totalMs Mod msPerBeat) * 100L \ msPerBeat)
                        value = (bar Mod 100000L).ToString("D5") & beat.ToString("D1") & tick.ToString("D2")

                    Case Else
                        value = "        "

                End Select

                ' ── Tronque par la gauche si débordement (ex : HH > 99) ──────
                If value.Length > 8 Then
                    value = value.Substring(value.Length - 8, 8)
                Else
                    value = value.PadLeft(8)
                End If

                ' ── Remplace les zéros de tête par des espaces dans chaque groupe ──
                Select Case _ClockMode
                    Case Commands.ClockModes.HourMinuteSecondMS
                        value = TrimLeadingZerosInGroups(value, {1, 2, 2, 3})
                    Case Commands.ClockModes.HourMinuteSecondFrames
                        value = TrimLeadingZerosInGroups(value, {2, 2, 2, 2})
                    Case Commands.ClockModes.FeetFrames
                        value = TrimLeadingZerosInGroups(value, {6, 2})
                    Case Commands.ClockModes.BarBeats
                        value = TrimLeadingZerosInGroups(value, {5, 1, 2})
                End Select

                Clock_Value = value
            End Sub
        End Class
        Public Class C24_ScribbleStrip
            Inherits C24_Component

            Private _Text As String
            Private _ScribbleStripCmd As ScribbleStrip_Cmd
            Private _Value As Double

            ' ── Affichage temporaire ──────────────────────────────────────────
            Private _OriginalText As String = String.Empty
            Private _IsShowingTemp As Boolean = False
            Private _TempTimer As System.Threading.Timer
            Private _pendingTempText As String = Nothing
            Public TempDisplayMs As Integer = 2000
            Event OnOriginaltextBack()
            ' ── Clignotement ──────────────────────────────────────────────────
            Private _IsBlinking As Boolean = False
            Private _BlinkOriginalText As String = String.Empty
            Private _prevBlinkingState As Boolean = False

            Public Event OnScribbleTextDisplay(Track_Nb As Integer, Text As String)


            ''' <summary>
            ''' Active ou désactive le clignotement du texte du ScribbleStrip.
            ''' Quand activé, le texte alterne entre le texte courant et des espaces
            ''' au rythme du timer blink du <see cref="C24_Desk"/>.
            ''' Quand désactivé, restaure immédiatement le texte original.
            ''' </summary>
            Public Property IsBlinking As Boolean
                Get
                    Return _IsBlinking
                End Get
                Set
                    If _IsBlinking = Value Then
                        Return
                    End If
                    _IsBlinking = Value
                    If _IsBlinking Then
                        _BlinkOriginalText = _ScribbleStripCmd.Text
                    Else
                        _ScribbleStripCmd.Text = _BlinkOriginalText
                    End If
                End Set
            End Property


            ''' <summary>
            ''' Appelé par le timer de blink du <see cref="C24_Desk"/>.
            ''' Fait clignoter le texte si <see cref="IsBlinking"/> est activé.
            ''' </summary>
            Public Sub BlinkingScribble(Blink_State As Boolean)
                If _IsBlinking Then
                    If Blink_State Then
                        _ScribbleStripCmd.Text = _BlinkOriginalText
                    Else
                        _ScribbleStripCmd.Text = "    "
                    End If
                End If
            End Sub

            Public Property Text As String
                Get
                    _Text = _ScribbleStripCmd.Text
                    Return _Text
                End Get
                Set
                    If Value <> _Text Then
                        _Text = Value
                        _OriginalText = Value
                        _ScribbleStripCmd.Text = _Text

                        RaiseEvent OnScribbleTextDisplay(_ScribbleStripCmd.Track_Number, Text)
                        ' ── Met à jour le texte de référence du blink ─────────────
                        If _IsBlinking Then
                            _BlinkOriginalText = _Text
                        End If
                        'Console.WriteLine(Me.Name & " SetTxt:" & Value)
                    End If
                End Set
            End Property

            Public Property Value As Double
                Get
                    _Value = _ScribbleStripCmd.Value
                    Return _Value
                End Get
                Set
                    If Value <> _Value Then
                        _Value = Value
                        _Text = Value

                        RaiseEvent OnScribbleTextDisplay(_ScribbleStripCmd.Track_Number, _Text)
                        _ScribbleStripCmd.Value = _Value
                    End If
                End Set
            End Property

            Public ReadOnly Property ScribbleStripCmd As ScribbleStrip_Cmd
                Get
                    Return _ScribbleStripCmd
                End Get
            End Property

            ''' <summary>
            ''' Affiche un texte temporaire pendant <see cref="TempDisplayMs"/> ms,
            ''' puis restaure automatiquement le texte précédent.
            ''' Si un affichage temporaire est déjà en cours, le timer est réarmé
            ''' et le texte temporaire est mis à jour.
            ''' </summary>
            ''' <param name="tempText">Texte à afficher temporairement (max 4 caractères).</param>
            Public Sub ShowTempText(tempText As String, Optional _TempDisplayMs As Integer = 2000)
                TempDisplayMs = _TempDisplayMs
                If Not _IsShowingTemp Then
                    _OriginalText = _ScribbleStripCmd.Text
                    _IsShowingTemp = True
                End If
                _prevBlinkingState = IsBlinking
                IsBlinking = False
                _pendingTempText = Nothing
                _ScribbleStripCmd.Text = tempText
                _TempTimer.Change(TempDisplayMs, System.Threading.Timeout.Infinite)
            End Sub

            ''' <summary>
            ''' Affiche <paramref name="text1"/> pendant <see cref="TempDisplayMs"/> ms,
            ''' puis <paramref name="text2"/> pendant <see cref="TempDisplayMs"/> ms,
            ''' avant de restaurer automatiquement le texte précédent.
            ''' Si un affichage temporaire est déjà en cours, la séquence repart depuis le début.
            ''' </summary>
            ''' <param name="text1">Premier texte à afficher (max 4 caractères).</param>
            ''' <param name="text2">Deuxième texte à afficher à la suite (max 4 caractères).</param>
            Public Sub ShowTempText(text1 As String, text2 As String, Optional _TempDisplayMs As Integer = 2000)
                TempDisplayMs = _TempDisplayMs
                If Not _IsShowingTemp Then
                    _OriginalText = _ScribbleStripCmd.Text
                    _IsShowingTemp = True
                End If
                _prevBlinkingState = IsBlinking
                IsBlinking = False
                _pendingTempText = text2
                _ScribbleStripCmd.Text = text1
                _TempTimer.Change(TempDisplayMs, System.Threading.Timeout.Infinite)
            End Sub

            ''' <summary>Restaure le texte original après expiration du timer.
            ''' Enchaîne sur le deuxième texte si <c>_pendingTempText</c> est défini.</summary>
            Private Sub OnTempTimer(state As Object)
                If _pendingTempText IsNot Nothing Then
                    Dim nextText As String = _pendingTempText
                    _pendingTempText = Nothing
                    _ScribbleStripCmd.Text = nextText
                    _TempTimer.Change(TempDisplayMs, System.Threading.Timeout.Infinite)
                Else
                    _IsShowingTemp = False
                    _ScribbleStripCmd.Text = _OriginalText
                    IsBlinking = _prevBlinkingState
                    RaiseEvent OnOriginaltextBack()
                End If
            End Sub
            Public Sub ResumeText()
                If _IsShowingTemp Then
                    OnTempTimer(Nothing)
                End If
            End Sub
            Sub New(Name As String, Track_Nb As Integer)
                _Name = Name
                _ScribbleStripCmd = New ScribbleStrip_Cmd(Track_Nb)
                C24_SendingCommands.Add(_ScribbleStripCmd)
                Me._Component_Id = _ScribbleStripCmd.Cmd_Id

                _TempTimer = New System.Threading.Timer(
                    AddressOf OnTempTimer,
                    Nothing,
                    System.Threading.Timeout.Infinite,
                    System.Threading.Timeout.Infinite)
            End Sub

        End Class
        Public Class C24_Vpot
            Inherits C24_Component
            Const Large_Step As Integer = 15
            Const Fine_Step As Integer = 100
            Const LedNumber As Integer = 15
            Private WithEvents _VpotCmd As Commands.Vpot_Cmd
            Private WithEvents _VpotLedCmd As Commands.VpotLed_Cmd
            Private _ScribbleStrip As C24_ScribbleStrip
            Private _Min_Value As Double = 0
            Private _Max_Value As Double = 1
            Private _Step_Value As Double
            Private _Value As Double? = Nothing
            Private _Velocity_sensible As Boolean = False
            Private _LedMode As LedModeType
            Private _Value2 As Double? = Nothing
            Private _VpotMode As VpotModeType = VpotModeType.Value
            Private _Adjust_Value2 As Boolean = False
            Private _Value_TextList As List(Of (item As String, Checked As Boolean))
            Private _SelectedItem_Index As Integer = 0
            Private _TrackNb As Integer? = Nothing

            Private ActualLedStatus As BitArray = New BitArray(LedNumber)

            ' ── Strips étendus pour l'affichage long (4 + n×4 caractères) ────
            Private _ExtendedScribbles As New List(Of C24_ScribbleStrip)

            ' ── Temporisation ScribbleStrip ───────────────────────────────────
            Private _OriginalScribbleText As String = ""
            Private _IsShowingValue As Boolean = False
            Private _RestoreTimer As System.Threading.Timer
            Private _ScribbleScriptsBlinking As Boolean = False
            Private _FxParam_SmallStepValue As Double? = Nothing
            Private _FxParam_LargeStepValue As Double? = Nothing
            Private _FxParamValueToDisplay As String
            Private _FxParam_StepValue As Double?
            Private _DisableVpot As Boolean
            Private _ChannelScribbleScreen As C24_ScribbleScreen

            Private Const RestoreDelayMs As Integer = 2000

            Public Event OnVpotValueChange(TrackNb As Integer, Value As Double)
            Public Event OnVpotValue2Change(TrackNb As Integer, Value2 As Double)
            Public Event OnVpotSelectedItemChange(TrackNb As Integer, SelectedItem_Index As Integer)
            Public Event OnScribbleScriptBackToOriginalText()


            Public Property DisableVpot As Boolean
                Get
                    Return _DisableVpot
                End Get
                Set
                    _DisableVpot = Value
                End Set
            End Property

            Public Property FxParamValueToDisplay As String
                Get
                    Return _FxParamValueToDisplay
                End Get
                Set
                    If Value <> _FxParamValueToDisplay Then
                        _FxParamValueToDisplay = Value
                        RefreshScribbleStrip(Nothing)
                    End If
                End Set
            End Property

            Public Property ScribbleScriptsBlinking As Boolean
                Get
                    Return _ScribbleScriptsBlinking
                End Get
                Set
                    If Value <> _ScribbleScriptsBlinking Then
                        _ScribbleScriptsBlinking = Value

                        ScribbleStrip.IsBlinking = Value
                        For Each es In ExtendedScribbles
                            es.IsBlinking = Value
                        Next
                    End If
                End Set
            End Property
            Public Property ChannelScribbleScreen As C24_ScribbleScreen
                Get
                    Return _ChannelScribbleScreen
                End Get
                Set
                    _ChannelScribbleScreen = Value
                End Set
            End Property

            Public ReadOnly Property VpotCmd As Commands.Vpot_Cmd
                Get
                    Return _VpotCmd
                End Get
            End Property

            Public ReadOnly Property VpotLedCmd As Commands.VpotLed_Cmd
                Get
                    Return _VpotLedCmd
                End Get
            End Property

            Public ReadOnly Property ScribbleStrip As C24_ScribbleStrip
                Get
                    Return _ScribbleStrip
                End Get
            End Property

            ''' <summary>
            ''' Strips supplémentaires pour étendre l'affichage du Vpot au-delà de 4 caractères.
            ''' La capacité totale devient (1 + Count) × 4 caractères.
            ''' Le texte est réparti automatiquement sur le strip principal puis sur chaque strip étendu.
            ''' </summary>
            Public Property ExtendedScribbles As List(Of C24_ScribbleStrip)
                Get
                    Return _ExtendedScribbles
                End Get
                Set
                    _ExtendedScribbles = Value
                End Set
            End Property

            ''' <summary>
            ''' Capacité totale d'affichage en caractères : 4 + (ExtendedScribbles.Count × 4).
            ''' </summary>
            Public ReadOnly Property ScribbleCapacity As Integer
                Get
                    Return 4 + _ExtendedScribbles.Count * 4
                End Get
            End Property

            Sub New(Name As String, Track_Nb As Integer, ScribbleStrip As C24_ScribbleStrip)
                _Name = Name
                _TrackNb = Track_Nb
                _VpotCmd = New Vpot_Cmd(Track_Nb)
                _C24_ReceivingCommand = VpotCmd
                _VpotLedCmd = New VpotLed_Cmd(Track_Nb)
                C24_SendingCommands.Add(VpotLedCmd)
                _ScribbleStrip = ScribbleStrip
                Me._Component_Id = _VpotCmd.Cmd_Id

                ' ── Timer de restauration du ScribbleStrip ────────────────────
                _RestoreTimer = New System.Threading.Timer(
                    AddressOf OnRestoreTimer,
                    Nothing,
                    System.Threading.Timeout.Infinite,
                    System.Threading.Timeout.Infinite)
            End Sub

            Public Enum VpotModeType
                Pan
                VolumeLinear
                Value
                TextList
                TextListWithChannelBar
                FxParamLinear
            End Enum



            Public Property Adjust_Value2 As Boolean
                Get
                    Return _Adjust_Value2
                End Get
                Set
                    If Value <> _Adjust_Value2 Then
                        _Adjust_Value2 = Value
                    End If
                End Set
            End Property

            Public Property VpotMode As VpotModeType
                Get
                    Return _VpotMode
                End Get
                Set
                    If Value <> _VpotMode Then
                        _VpotMode = Value
                        Select Case VpotMode
                            Case VpotModeType.Pan
                                LedMode = LedModeType.Central_Dot
                                _Min_Value = -1
                                _Max_Value = 1
                            Case VpotModeType.VolumeLinear
                                LedMode = LedModeType.Gauge
                                _Min_Value = 0
                                _Max_Value = 2
                            Case VpotModeType.TextList, VpotModeType.TextListWithChannelBar
                                LedMode = LedModeType.Dot
                                _Min_Value = 0
                                If Value_TextList IsNot Nothing AndAlso Value_TextList.Count > 0 Then
                                    _Max_Value = Value_TextList.Count - 1
                                Else
                                    _Max_Value = 0
                                End If
                            Case VpotModeType.FxParamLinear
                                _Min_Value = 0
                                _Max_Value = 1

                                LedMode = LedModeType.Gauge
                            Case Else
                                LedMode = LedModeType.Dot
                                _Min_Value = 0
                                _Max_Value = 1
                        End Select
                    End If
                End Set
            End Property

            Public Property Min_Value As Double
                Get
                    Return _Min_Value
                End Get
                Set
                    If Value <> _Min_Value Then
                        _Min_Value = Value
                    End If
                End Set
            End Property

            Public Property Max_Value As Double
                Get
                    Return _Max_Value
                End Get
                Set
                    If Value <> _Max_Value Then
                        _Max_Value = Value
                    End If
                End Set
            End Property
            Public Property FxParam_SmallStepValue As Double?
                Get
                    Return _FxParam_SmallStepValue
                End Get
                Set
                    If Value <> _FxParam_SmallStepValue Then
                        _FxParam_SmallStepValue = Value
                    End If
                End Set
            End Property
            Public Property FxParam_LargeStepValue As Double?
                Get
                    Return _FxParam_LargeStepValue
                End Get
                Set
                    If Value <> _FxParam_LargeStepValue Then
                        _FxParam_LargeStepValue = Value
                    End If
                End Set
            End Property
            Public Property FxParam_StepValue As Double?
                Get
                    Return _FxParam_StepValue
                End Get
                Set
                    If Value <> _FxParam_StepValue Then
                        _FxParam_StepValue = Value
                    End If
                End Set
            End Property

            Public ReadOnly Property Step_Value As Double
                Get
                    If Velocity_sensible Then
                        If _FxParam_SmallStepValue IsNot Nothing AndAlso VpotMode = VpotModeType.FxParamLinear Then
                            _Step_Value = _FxParam_SmallStepValue
                        ElseIf _FxParam_StepValue IsNot Nothing AndAlso VpotMode = VpotModeType.FxParamLinear Then
                            _Step_Value = _FxParam_StepValue
                        Else
                            _Step_Value = 1 / Fine_Step
                        End If

                    Else
                        If _FxParam_LargeStepValue IsNot Nothing AndAlso VpotMode = VpotModeType.FxParamLinear Then
                            _Step_Value = _FxParam_LargeStepValue
                        ElseIf _FxParam_StepValue IsNot Nothing AndAlso VpotMode = VpotModeType.FxParamLinear Then
                            _Step_Value = _FxParam_StepValue
                        Else
                            _Step_Value = 1 / Large_Step
                        End If

                    End If

                    Return _Step_Value
                End Get
            End Property

            Public Property Value As Double?
                Get
                    Return _Value
                End Get
                Set
                    If Value IsNot Nothing AndAlso Not DisableVpot Then
                        'Console.WriteLine(Value)
                        Dim newValue = Math.Round(Math.Min(Math.Max(Value.Value, Min_Value), Max_Value), 2)
                        If _Value Is Nothing OrElse newValue <> _Value.Value Then
                            _Value = newValue
                            RefreshLedStatus()
                        End If
                    End If

                End Set
            End Property

            Public Property Value2 As Double?
                Get
                    Return _Value2
                End Get
                Set
                    If Value IsNot Nothing AndAlso Not DisableVpot Then
                        Dim newValue2 = Math.Round(Math.Min(Math.Max(Value.Value, Min_Value), Max_Value), 2)
                        If _Value Is Nothing OrElse newValue2 <> _Value.Value Then
                            _Value2 = newValue2
                            RefreshLedStatus()
                        End If
                    End If

                End Set
            End Property

            Public Property Velocity_sensible As Boolean
                Get
                    Return _Velocity_sensible
                End Get
                Set
                    If Value <> _Velocity_sensible Then
                        _Velocity_sensible = Value
                    End If
                End Set
            End Property

            Public Property Value_TextList As List(Of (item As String, Checked As Boolean))
                Get
                    Return _Value_TextList
                End Get
                Set
                    If Value IsNot _Value_TextList Then
                        _Value_TextList = Value
                    End If
                End Set
            End Property


            Public Enum LedModeType

                Dot
                Central_Dot
                Gauge
                Central_Gauge
                Q
            End Enum

            Public Property LedMode As LedModeType
                Get
                    Return _LedMode
                End Get
                Set
                    If Value <> _LedMode Then
                        _LedMode = Value
                    End If
                End Set
            End Property

            Public Property SelectedItem_Index As Integer
                Get
                    Return _SelectedItem_Index
                End Get
                Set
                    If Value <> _SelectedItem_Index Then
                        _SelectedItem_Index = Value
                        RefreshSelectedItem()
                        If Value_TextList IsNot Nothing AndAlso Value_TextList.Count > 0 Then
                            _Min_Value = 0
                            _Max_Value = Value_TextList.Count - 1
                            _Value = _SelectedItem_Index
                            RefreshLedStatus()
                        End If
                    End If
                End Set
            End Property

            ' ── Affichage réparti sur tous les strips disponibles ─────────────

            ''' <summary>
            ''' Affiche un texte sur le strip principal et les strips étendus.
            ''' Le texte est découpé en tranches de 4 caractères.
            ''' La capacité totale est <see cref="ScribbleCapacity"/>.
            ''' </summary>
            ''' <param name="text">Texte à afficher (tronqué à <see cref="ScribbleCapacity"/> caractères).</param>
            Public Sub SetAllScribbleText(text As String)
                Dim padded As String = text.PadRight(ScribbleCapacity).Substring(0, ScribbleCapacity)
                _ScribbleStrip.Text = padded.Substring(0, 4)
                For i As Integer = 0 To _ExtendedScribbles.Count - 1
                    _ExtendedScribbles(i).Text = padded.Substring(4 + i * 4, 4)
                Next

            End Sub
            ''' <summary>
            ''' Affiche un texte temporaire sur le strip principal et tous les strips étendus
            ''' pendant <see cref="RestoreDelayMs"/> ms, puis restaure le texte précédent.
            ''' La capacité totale est <see cref="ScribbleCapacity"/> caractères.
            ''' </summary>
            ''' <param name="tempText">Texte temporaire à afficher (tronqué à <see cref="ScribbleCapacity"/> caractères).</param>
            Public Sub ShowTempAllScribbleText(tempText As String)
                Dim padded As String = tempText.PadRight(ScribbleCapacity).Substring(0, ScribbleCapacity)
                _ScribbleStrip.ShowTempText(padded.Substring(0, 4))
                For i As Integer = 0 To _ExtendedScribbles.Count - 1
                    _ExtendedScribbles(i).ShowTempText(padded.Substring(4 + i * 4, 4))
                Next
            End Sub
            ''' <summary>
            ''' Efface le strip principal et tous les strips étendus.
            ''' </summary>
            Public Sub ClearAllScribbles()
                _ScribbleStrip.Text = "    "
                For Each strip In _ExtendedScribbles
                    strip.Text = "    "
                Next
            End Sub
            Public Sub ResetStepValues()
                FxParam_SmallStepValue = Nothing
                FxParam_LargeStepValue = Nothing
            End Sub

            Private Sub RefreshSelectedItem()
                If Value_TextList IsNot Nothing AndAlso
                   _SelectedItem_Index >= 0 AndAlso
                   _SelectedItem_Index < Value_TextList.Count Then
                    Dim item_text = Value_TextList.Item(_SelectedItem_Index).item
                    Dim item_checked = Value_TextList.Item(_SelectedItem_Index).Checked

                    If _ExtendedScribbles.Count = 0 Then
                        ' ── Mode compact (4 car. uniquement) ─────────────────
                        If item_checked Then
                            _ScribbleStrip.Text = ">" & item_text.Substring(0, Math.Min(3, item_text.Length))
                        Else
                            _ScribbleStrip.Text = item_text.Substring(0, Math.Min(4, item_text.Length))
                        End If
                    Else
                        ' ── Mode étendu : préfixe ">" puis texte complet ─────
                        If item_checked Then
                            SetAllScribbleText(">" & item_text)
                        Else
                            SetAllScribbleText(item_text)
                        End If
                    End If

                    If VpotMode = VpotModeType.TextListWithChannelBar AndAlso ChannelScribbleScreen IsNot Nothing Then
                        ChannelScribbleScreen.ShowTempText(item_text)
                    End If

                End If
            End Sub

            Public Sub CheckSelectedItem(Status As Boolean)
                Dim item = Value_TextList.Item(SelectedItem_Index)
                item.Checked = Status
                Value_TextList.Item(SelectedItem_Index) = item
                RefreshSelectedItem()
            End Sub

            Public Sub CheckAllItems(Status As Boolean)
                For i = 0 To Value_TextList.Count - 1
                    Dim item = Value_TextList.Item(i)
                    item.Checked = Status
                    Value_TextList.Item(i) = item
                Next
            End Sub

            ''' <summary>
            ''' Retourne la liste des textes de tous les éléments cochés dans <see cref="Value_TextList"/>.
            ''' Retourne une liste vide si la liste n'est pas initialisée.
            ''' </summary>
            Public Function GetCheckedItems() As List(Of String)
                Dim result As New List(Of String)
                If Value_TextList Is Nothing Then
                    Return result
                End If
                For Each item In Value_TextList
                    If item.Checked Then
                        result.Add(item.item)
                    End If
                Next
                Return result
            End Function

            ''' <summary>
            ''' Sélectionne l'élément dont le texte correspond à <paramref name="itemText"/>
            ''' et met à jour les LEDs et le ScribbleStrip.
            ''' Sans effet si le texte n'est pas trouvé ou si la liste est vide.
            ''' </summary>
            ''' <param name="itemText">Texte de l'élément à sélectionner.</param>
            ''' <returns>True si l'élément a été trouvé et sélectionné.</returns>
            Public Function SelectItemByText(itemText As String) As Boolean
                If Value_TextList Is Nothing OrElse Value_TextList.Count = 0 Then
                    Return False
                End If

                For i = 0 To Value_TextList.Count - 1
                    If Value_TextList(i).item = itemText Then
                        If i <> _SelectedItem_Index Then
                            SelectedItem_Index = i
                        Else
                            RefreshSelectedItem()
                        End If
                        Return True
                    End If
                Next

                Return False
            End Function

            Public Sub UpdateValueFromCmd(Optional Manual_Rotate_Velocity As Integer? = Nothing) Handles _VpotCmd.OnCmdChange
                If DisableVpot = False Then

                    If Manual_Rotate_Velocity IsNot Nothing Then
                        Dim Value As Integer = Manual_Rotate_Velocity

                        _VpotCmd.Rotate_Velocity = Value
                    End If
                    Select Case VpotMode
                        Case VpotModeType.Pan

                            If Adjust_Value2 Then
                                If _Value2 IsNot Nothing Then
                                    If Math.Abs(_Value2.Value) <> 0 AndAlso Math.Abs(_Value2.Value) <= 0.05 Then
                                        _Value2 = 0
                                    Else
                                        _Value2 += _VpotCmd.Rotate_Velocity * Step_Value
                                        _Value2 = Math.Round(Math.Min(Math.Max(_Value2.Value, Min_Value), Max_Value), 2)
                                    End If
                                    RefreshLedStatus()
                                    RefreshScribbleStrip(_Value2 * 100)
                                    RaiseEvent OnVpotValue2Change(_TrackNb, _Value2)
                                End If

                            Else
                                If _Value IsNot Nothing Then

                                    If Math.Abs(_Value.Value) <> 0 AndAlso Math.Abs(_Value.Value) <= 0.05 Then
                                        _Value = 0
                                    Else

                                        _Value += _VpotCmd.Rotate_Velocity * Step_Value

                                        _Value = Math.Round(Math.Min(Math.Max(_Value.Value, Min_Value), Max_Value), 2)

                                    End If
                                    RefreshLedStatus()
                                    RefreshScribbleStrip(_Value * 100)
                                    RaiseEvent OnVpotValueChange(_TrackNb, _Value)
                                End If


                            End If

                        Case VpotModeType.VolumeLinear
                            If _Value IsNot Nothing Then

                                _Value += _VpotCmd.Rotate_Velocity * Step_Value
                                _Value = Math.Round(Math.Min(Math.Max(_Value.Value, Min_Value), Max_Value), 2)
                                RefreshLedStatus()
                                RefreshScribbleStrip(Reaper.SLIDER2DB(_Value))
                                RaiseEvent OnVpotValueChange(_TrackNb, _Value)
                            End If


                        Case VpotModeType.FxParamLinear
                            If _Value IsNot Nothing Then
                                _Value += _VpotCmd.Rotate_Velocity * Step_Value
                                _Value = Math.Round(Math.Min(Math.Max(_Value.Value, Min_Value), Max_Value), 4)
                                RefreshLedStatus()
                                RaiseEvent OnVpotValueChange(_TrackNb, _Value)
                            End If



                        Case VpotModeType.TextList, VpotModeType.TextListWithChannelBar
                            LedMode = LedModeType.Dot
                            If Value_TextList IsNot Nothing AndAlso Value_TextList.Count > 0 Then
                                Min_Value = 0
                                Max_Value = Value_TextList.Count - 1
                                Dim [step] As Integer = Math.Sign(_VpotCmd.Rotate_Velocity)
                                Dim newIdx As Integer = Math.Min(
                                    Math.Max(_SelectedItem_Index + [step], 0),
                                    Value_TextList.Count - 1)
                                If newIdx <> _SelectedItem_Index Then
                                    SelectedItem_Index = newIdx
                                    _Value = newIdx
                                    RefreshLedStatus()
                                    RaiseEvent OnVpotSelectedItemChange(_TrackNb, SelectedItem_Index)
                                End If
                            End If

                    End Select
                End If

            End Sub



            ' ── Temporisation ScribbleStrip ───────────────────────────────────

            ''' <summary>
            ''' Définit le texte permanent du ScribbleStrip principal.
            ''' Ce texte est restauré automatiquement après <see cref="RestoreDelayMs"/> ms
            ''' d'inactivité du Vpot.
            ''' </summary>
            Public Sub SetScribbleText(text As String)
                _OriginalScribbleText = text
                If Not _IsShowingValue Then
                    _ScribbleStrip.Text = _OriginalScribbleText
                End If
            End Sub

            Private Sub OnRestoreTimer(state As Object)
                _IsShowingValue = False
                _ScribbleStrip.Text = _OriginalScribbleText
                RaiseEvent OnScribbleScriptBackToOriginalText()
            End Sub

            Public Sub RefreshScribbleStrip(Value As Double?)
                If Not _IsShowingValue Then
                    _OriginalScribbleText = _ScribbleStrip.Text
                    _IsShowingValue = True
                End If

                Select Case VpotMode
                    Case VpotModeType.Value, VpotModeType.VolumeLinear
                        If Value Is Nothing Then
                            Value = 0
                        End If
                        _ScribbleStrip.Value = Value
                    Case VpotModeType.Pan
                        LedMode = LedModeType.Central_Dot
                        Dim Value_int As Integer = CInt(Value)
                        If Value_int = 0 Then
                            _ScribbleStrip.Text = "<0>"
                        ElseIf Value_int > 0 Then
                            _ScribbleStrip.Text = Value_int & ">"
                        ElseIf Value_int < 0 Then
                            _ScribbleStrip.Text = "<" & Math.Abs(Value_int)
                        End If
                    Case VpotModeType.FxParamLinear
                        _ScribbleStrip.Text = FxParamValueToDisplay

                End Select


                _RestoreTimer.Change(RestoreDelayMs, System.Threading.Timeout.Infinite)
            End Sub

            ''' <summary>
            ''' Initialise <see cref="Value_TextList"/> à partir de deux listes parallèles.
            ''' </summary>
            ''' <param name="items">Textes des éléments.</param>
            ''' <param name="checkedItems">Textes des éléments cochés.</param>
            Public Sub InitTextList(items As IReadOnlyList(Of String), checkedItems As IReadOnlyList(Of String))
                Dim list As New List(Of (item As String, Checked As Boolean))
                For Each txt In items
                    If checkedItems IsNot Nothing AndAlso checkedItems.Count > 0 Then
                        list.Add((txt, checkedItems.Contains(txt)))
                    Else
                        list.Add((txt, False))
                    End If

                Next
                Value_TextList = list
            End Sub

#Region "Led"
            Public Sub RefreshLedStatus()
                'Console.Write(Me.Name & " [")
                Dim NewLedStatus As BitArray = New BitArray(LedNumber)
                If _Value IsNot Nothing Then
                    'Console.Write("(" & _Value.Value & ")")
                    NewLedStatus = GetLedsStatus(_Value.Value)
                    If _Value2 IsNot Nothing Then
                        Dim Value2LedStatus As BitArray = GetLedsStatus(_Value2)
                        NewLedStatus = NewLedStatus.Or(Value2LedStatus)
                    End If


                Else
                    'Console.Write("(Null)")


                End If

                Dim BitChanged As Boolean = False
                For i = 0 To NewLedStatus.Count - 1
                    If NewLedStatus(i) <> ActualLedStatus(i) Then
                        BitChanged = True
                        Exit For
                    End If
                Next

                If BitChanged Then
                    VpotLedCmd.Leds_Status = NewLedStatus
                    ActualLedStatus = DirectCast(NewLedStatus.Clone(), BitArray)

                    ''display led status 
                    'Console.Write(" [")
                    'For Each bit In VpotLedCmd.Leds_Status
                    '    Console.Write(If(bit, "1", "0") & ",")
                    'Next

                End If


                'Console.Write("]" & vbCrLf)

            End Sub
            Public Sub SetLedFromNormValue(NormValue As Double)
                Dim NewLedStatus As BitArray = New BitArray(LedNumber)

                NewLedStatus = GetLedsStatus(NormValue)



                Dim BitChanged As Boolean = False
                For i = 0 To NewLedStatus.Count - 1
                    If NewLedStatus(i) <> ActualLedStatus(i) Then
                        BitChanged = True
                        Exit For
                    End If
                Next

                If BitChanged Then
                    VpotLedCmd.Leds_Status = NewLedStatus
                    ActualLedStatus = DirectCast(NewLedStatus.Clone(), BitArray)

                End If


            End Sub

            Public Sub SetLedsOff()

                _Value = Nothing
                _Value2 = Nothing
                RefreshLedStatus()


            End Sub

            Private Function GetLedsStatus(Value As Double) As BitArray
                Dim LedsStatus As New BitArray(LedNumber)


                If LedMode = LedModeType.Dot Then
                    LedsStatus.Item(GetLedIndex(Value, Min_Value, Max_Value, LedNumber)) = True
                ElseIf LedMode = LedModeType.Gauge Then
                    Dim LedIndex As Integer = GetLedIndex(Value, Min_Value, Max_Value, LedNumber)
                    For i = 0 To LedIndex
                            LedsStatus.Item(i) = True
                        Next
                    ElseIf LedMode = LedModeType.Central_Dot Then
                        Dim roundedValue As Double = Math.Round(Value, 2)
                        Dim Middle As Double = Math.Round((Min_Value + Max_Value) / 2.0, 2)
                        Dim LedIndex As Integer = 0

                        If roundedValue <= Math.Round(Min_Value, 2) Then
                            LedIndex = 0
                        ElseIf roundedValue = Middle Then
                            LedIndex = 7
                        ElseIf roundedValue >= Math.Round(Max_Value, 2) Then
                            LedIndex = 14
                        ElseIf roundedValue < Middle Then
                            Dim span As Double = Middle - Min_Value
                            If span = 0.0 Then
                                LedIndex = 1
                            Else
                                Dim norm As Double = (roundedValue - Min_Value) / span
                                LedIndex = Math.Min(1 + CInt(Math.Floor(norm * 6)), 6)
                            End If
                        Else
                            Dim span As Double = Max_Value - Middle
                            If span = 0.0 Then
                                LedIndex = 8
                            Else
                                Dim norm As Double = (roundedValue - Middle) / span
                                LedIndex = Math.Min(8 + CInt(Math.Floor(norm * 6)), 13)
                            End If
                        End If

                        LedsStatus.Item(LedIndex) = True
                    ElseIf LedMode = LedModeType.Central_Gauge Then
                        Dim LedIndex As Integer = 0
                        Dim Middle = (Min_Value + Max_Value) / 2
                        If Value <= Middle Then
                        LedIndex = GetLedIndex(Value, Min_Value, Middle, 8)
                    Else
                        LedIndex = GetLedIndex(Value, Middle, Max_Value, 8)
                    End If
                        For i = 0 To LedIndex - 1
                            If i >= LedIndex AndAlso i <= 7 Then
                                LedsStatus.Item(i) = True
                            ElseIf i >= 7 AndAlso i <= LedIndex Then
                                LedsStatus.Item(i) = True
                            End If
                        Next
                    ElseIf LedMode = LedModeType.Q Then
                    Dim LedIndex As Integer = GetLedIndex(Value, Min_Value, Max_Value, 8)
                    For i = 0 To LedIndex - 1
                        LedsStatus.Item(7 + i) = True
                        LedsStatus.Item(7 - i) = True
                    Next
                End If

                Return LedsStatus
            End Function
#End Region

        End Class
        Public Class C24_VueMeter
            Inherits C24_Component

            Const LedNumber As Integer = 14

            Private _VueMeterCmdLeft As VueMeter_Cmd
            Private _VueMeterCmdRight As VueMeter_Cmd

            ' ── Valeurs initialisées à Min pour que 0 LED soit allumée au départ ──
            Private _Min_Value As Double = -62
            Private _Max_Value As Double = 6
            Private _Value_Left As Double = -62
            Private _Value_Right As Double = -62
            Private _Hold_Value_Left As Double = -62
            Private _Hold_Value_Right As Double = -62
            Private _Peak_Left As Boolean = False
            Private _Peak_Right As Boolean = False
            Private _Reverse As Boolean = False

            ' ── Propriétés ────────────────────────────────────────────────────

            ReadOnly Property VueMeterCmdLeft As VueMeter_Cmd
                Get
                    Return _VueMeterCmdLeft
                End Get
            End Property

            ReadOnly Property VueMeterCmdRight As VueMeter_Cmd
                Get
                    Return _VueMeterCmdRight
                End Get
            End Property

            Public Property Min_Value As Double
                Get
                    Return _Min_Value
                End Get
                Set
                    If Value <> _Min_Value Then
                        _Min_Value = Value
                        ' ── Réinitialise au min pour éviter des LEDs parasites ─────
                        _Value_Left = _Min_Value
                        _Value_Right = _Min_Value
                        _Hold_Value_Left = _Min_Value
                        _Hold_Value_Right = _Min_Value
                    End If
                End Set
            End Property

            Public Property Max_Value As Double
                Get
                    Return _Max_Value
                End Get
                Set
                    If Value <> _Max_Value Then
                        _Max_Value = Value
                    End If
                End Set
            End Property

            Public Property Reverse As Boolean
                Get
                    Return _Reverse
                End Get
                Set
                    If Value <> _Reverse Then
                        _Reverse = Value
                    End If
                End Set
            End Property

            Public Property Hold_Value_Left As Double
                Get
                    Return _Hold_Value_Left
                End Get
                Set
                    If Value <> _Hold_Value_Left Then
                        _Hold_Value_Left = Value
                        RefreshLedStatus(_Value_Left, _Hold_Value_Left, StereoChannel.Left)
                    End If
                End Set
            End Property

            Public Property Hold_Value_Right As Double
                Get
                    Return _Hold_Value_Right
                End Get
                Set
                    If Value <> _Hold_Value_Right Then
                        _Hold_Value_Right = Value
                        RefreshLedStatus(_Value_Right, _Hold_Value_Right, StereoChannel.Right)
                    End If
                End Set
            End Property

            ''' <summary>
            ''' Niveau gauche. Met à jour les LEDs immédiatement.
            ''' Passe le hold courant pour afficher la LED hold en même temps.
            ''' </summary>
            Public Property Value_Left As Double
                Get
                    Return _Value_Left
                End Get
                Set
                    If Value <> _Value_Left Then
                        _Value_Left = Value
                        If _Value_Left >= Max_Value Then
                            _Peak_Left = True
                        End If
                        RefreshLedStatus(_Value_Left, _Hold_Value_Left, StereoChannel.Left)
                    End If
                End Set
            End Property

            ''' <summary>
            ''' Niveau droit. Met à jour les LEDs immédiatement.
            ''' Passe le hold courant pour afficher la LED hold en même temps.
            ''' </summary>
            Public Property Value_Right As Double
                Get
                    Return _Value_Right
                End Get
                Set
                    If Value <> _Value_Right Then
                        _Value_Right = Value
                        If _Value_Right >= Max_Value Then
                            _Peak_Right = True
                        End If
                        RefreshLedStatus(_Value_Right, _Hold_Value_Right, StereoChannel.Right)
                    End If
                End Set
            End Property

            Public Property Peak_Left As Boolean
                Get
                    Return _Peak_Left
                End Get
                Set
                    If Value <> _Peak_Left Then
                        _Peak_Left = Value
                    End If
                End Set
            End Property

            Public Property Peak_Right As Boolean
                Get
                    Return _Peak_Right
                End Get
                Set
                    If Value <> _Peak_Right Then
                        _Peak_Right = Value
                    End If
                End Set
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            Sub New(Name As String, Track_Nb As Integer)
                _Name = Name

                _VueMeterCmdLeft = New VueMeter_Cmd(Track_Nb)
                _VueMeterCmdLeft.Channel = StereoChannel.Left
                C24_SendingCommands.Add(_VueMeterCmdLeft)

                _VueMeterCmdRight = New VueMeter_Cmd(Track_Nb)
                _VueMeterCmdRight.Channel = StereoChannel.Right
                C24_SendingCommands.Add(_VueMeterCmdRight)

                Me._Component_Id = _VueMeterCmdLeft.Cmd_Id
            End Sub

            ' ── Réinitialisation ──────────────────────────────────────────────

            Public Sub ResetPeaks()
                _Peak_Left = False
                _Peak_Right = False
                RefreshLedStatus(_Value_Left, _Hold_Value_Left, StereoChannel.Left)
                RefreshLedStatus(_Value_Right, _Hold_Value_Right, StereoChannel.Right)
            End Sub

            ' ── Refresh ───────────────────────────────────────────────────────

            Private Sub RefreshLedStatus(value As Double, holdValue As Double, channel As StereoChannel)
                Dim ledsStatus = GetLedsStatus(value, holdValue)

                If _Reverse Then
                    ledsStatus = Bit.ReverseBitArray(ledsStatus)
                End If

                ' ── LED de clip (dernière) ─────────────────────────────────────
                If _Peak_Left OrElse _Peak_Right Then
                    ledsStatus.Item(LedNumber - 1) = True
                End If

                If channel = StereoChannel.Left Then
                    _VueMeterCmdLeft.Leds_Status = ledsStatus
                Else
                    _VueMeterCmdRight.Leds_Status = ledsStatus
                End If
            End Sub

            ''' <summary>
            ''' Calcule les LEDs à allumer de façon strictement linéaire :
            ''' <list type="bullet">
            ''' <item>value ≤ Min → 0 LED allumée</item>
            ''' <item>Min &lt; value &lt; Max → linéaire, 1ère LED dès le premier pas</item>
            ''' <item>value ≥ Max → toutes les <see cref="LedNumber"/> LEDs allumées</item>
            ''' </list>
            ''' </summary>
            Private Function GetLedsStatus(value As Double, holdValue As Double) As BitArray
                Dim ledsStatus As New BitArray(LedNumber)
                Dim range As Double = _Max_Value - _Min_Value

                ' ── Niveau courant ─────────────────────────────────────────────
                Dim ledIdx As Integer
                If range <= 0 OrElse value <= _Min_Value Then
                    ledIdx = 0
                ElseIf value >= _Max_Value Then
                    ledIdx = LedNumber
                Else
                    ' Linéaire : chaque LED couvre un segment égal de la plage
                    ' Floor garantit que min+epsilon allume exactement 1 LED
                    ledIdx = CInt(Math.Floor((value - _Min_Value) / range * LedNumber))
                    ledIdx = Math.Max(0, Math.Min(LedNumber, ledIdx))
                End If

                For i = 0 To ledIdx - 1
                    ledsStatus.Item(i) = True
                Next

                ' ── Hold : 1 LED indépendante ──────────────────────────────────
                If holdValue > _Min_Value Then
                    Dim hIdx As Integer
                    If holdValue >= _Max_Value Then
                        hIdx = LedNumber - 1
                    Else
                        hIdx = CInt(Math.Floor((holdValue - _Min_Value) / range * LedNumber))
                        hIdx = Math.Max(0, Math.Min(LedNumber - 1, hIdx))
                    End If
                    ledsStatus.Item(hIdx) = True
                End If

                Return ledsStatus
            End Function

        End Class        ''' <summary>
        ''' Représente la molette Jog du Control24.
        ''' Composant de réception uniquement — détecte les rotations hardware
        ''' via <see cref="Commands.JogWheel_Cmd"/>.
        ''' Ne génère aucune commande vers le C24.
        ''' </summary>
        Public Class C24_JogWheel
            Inherits C24_Component

            Private WithEvents _JogWheelCmd As Commands.JogWheel_Cmd

            ' ── Événement ─────────────────────────────────────────────────────

            ''' <summary>
            ''' Déclenché à chaque rotation de la molette Jog.
            ''' </summary>
            ''' <param name="direction">Direction de rotation (Left / Right).</param>
            ''' <param name="velocity">Vélocité de rotation.</param>
            Public Event OnJogWheelChange(direction As Commands.Direction, velocity As Integer)

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Direction courante de la molette.</summary>
            Public ReadOnly Property Rotate_Direction As Commands.Direction
                Get
                    Return _JogWheelCmd.Rotate_Direction
                End Get
            End Property

            ''' <summary>Vélocité courante de la molette.</summary>
            Public ReadOnly Property Rotate_Velocity As Integer
                Get
                    Return _JogWheelCmd.Rotate_Velocity
                End Get
            End Property

            ''' <summary>Accès direct à la commande sous-jacente.</summary>
            Public ReadOnly Property JogWheelCmd As Commands.JogWheel_Cmd
                Get
                    Return _JogWheelCmd
                End Get
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Crée le composant molette Jog du Control24.
            ''' </summary>
            ''' <param name="Name">Nom unique du composant.</param>
            Public Sub New(Name As String)
                _Name = Name
                _JogWheelCmd = New Commands.JogWheel_Cmd()
                _C24_ReceivingCommand = _JogWheelCmd
                Me._Component_Id = _JogWheelCmd.Cmd_Id
            End Sub

            ' ── Réception hardware ────────────────────────────────────────────

            ''' <summary>Appelé à chaque mouvement de la molette reçu depuis le C24.</summary>
            Public Sub OnJogWheelCmdChange(Optional Manual_Rotate_Velocity As Integer? = Nothing) Handles _JogWheelCmd.OnCmdChange
                If Manual_Rotate_Velocity IsNot Nothing Then
                    Dim Value As Integer = Manual_Rotate_Velocity

                    _JogWheelCmd.Rotate_Velocity = Value
                End If
                RaiseEvent OnJogWheelChange(_JogWheelCmd.Rotate_Direction, Math.Abs(_JogWheelCmd.Rotate_Velocity))
            End Sub

        End Class
        ''' <summary>
        ''' Représente un écran virtuel composé de plusieurs <see cref="C24_ScribbleStrip"/> alignés.
        ''' Permet d'afficher un texte long réparti sur l'ensemble des strips dans l'ordre de la liste.
        ''' Chaque strip affiche 4 caractères — la capacité totale est donc <c>Count × 4</c> caractères.
        ''' </summary>
        Public Class C24_ScribbleScreen

            Private ReadOnly _Strips As List(Of C24_ScribbleStrip)
            Private _OriginalTextBack As Boolean = True
            Public Event OnScribblescreenTextDisplay(Text As String)
            Public Event OnOriginaltextBack()
            Private _StripNbOriginaltextBack As Integer = 0

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Nombre de strips composant l'écran.</summary>
            Public ReadOnly Property Count As Integer
                Get
                    Return _Strips.Count
                End Get
            End Property

            ''' <summary>Capacité totale en caractères (Count × 4).</summary>
            Public ReadOnly Property Capacity As Integer
                Get
                    Return _Strips.Count * 4
                End Get
            End Property

            ''' <summary>Accès indexé à un strip individuel.</summary>
            Public ReadOnly Property Strips(index As Integer) As C24_ScribbleStrip
                Get
                    Return _Strips(index)
                End Get
            End Property
            Public ReadOnly Property OriginalTextBack As Boolean
                Get
                    Return _OriginalTextBack
                End Get
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Crée un écran virtuel à partir d'une liste de strips déjà instanciés.
            ''' L'ordre de la liste détermine l'ordre d'affichage de gauche à droite.
            ''' </summary>
            ''' <param name="strips">Liste ordonnée de <see cref="C24_ScribbleStrip"/> composant l'écran.</param>
            Public Sub New(strips As List(Of C24_ScribbleStrip))
                _Strips = strips
                If strips.Count > 0 Then
                    Dim StripNbOriginaltextBack As Integer = 0
                    Dim OnOriginaltextBackH = Sub()
                                                  Dim nb As Integer = System.Threading.Interlocked.Increment(_StripNbOriginaltextBack)

                                                  If nb >= _Strips.Count Then

                                                      System.Threading.Interlocked.Exchange(_StripNbOriginaltextBack, 0)
                                                      _OriginalTextBack = True
                                                      RaiseEvent OnOriginaltextBack()
                                                  End If
                                              End Sub
                    For Each strip In _Strips
                        AddHandler strip.OnOriginaltextBack, OnOriginaltextBackH
                    Next

                End If

            End Sub

            ' ── API publique ──────────────────────────────────────────────────

            ''' <summary>
            ''' Affiche un texte sur l'ensemble des strips.
            ''' Le texte est découpé en tranches de 4 caractères, une par strip.
            ''' Le texte est paddé à droite avec des espaces si nécessaire.
            ''' Les strips excédentaires sont effacés.
            ''' </summary>
            ''' <param name="text">Texte à afficher (jusqu'à <see cref="Capacity"/> caractères).</param>
            Public Sub SetText(text As String)
                Dim padded As String = text.PadRight(Capacity).Substring(0, Capacity)
                For i = 0 To _Strips.Count - 1
                    _Strips(i).Text = padded.Substring(i * 4, 4)
                Next
                RaiseEvent OnScribblescreenTextDisplay(text)
            End Sub
            ''' <summary>
            ''' Affiche un texte temporaire sur l'ensemble des strips pendant la durée
            ''' définie par chaque <see cref="C24_ScribbleStrip"/>, puis restaure
            ''' automatiquement le texte précédent de chaque strip.
            ''' Le texte est découpé en tranches de 4 caractères, une par strip.
            ''' </summary>
            ''' <param name="tempText">Texte temporaire à afficher (jusqu'à <see cref="Capacity"/> caractères).</param>
            Public Sub ShowTempText(tempText As String, Optional TempTime As Integer = 2000)
                Dim padded As String = tempText.PadRight(Capacity).Substring(0, Capacity)
                System.Threading.Interlocked.Exchange(_StripNbOriginaltextBack, 0)
                _OriginalTextBack = False
                For i = 0 To _Strips.Count - 1

                    _Strips(i).ShowTempText(padded.Substring(i * 4, 4), TempTime)
                Next

                RaiseEvent OnScribblescreenTextDisplay(tempText)
            End Sub
            Public Sub ResumeText()
                For i = 0 To _Strips.Count - 1
                    _Strips(i).ResumeText()
                Next
            End Sub
            ''' <summary>
            ''' Efface tous les strips (remplit d'espaces).
            ''' </summary>
            Public Sub Clear()
                For Each strip In _Strips
                    strip.Text = "    "
                Next
            End Sub

            ''' <summary>
            ''' Affiche un texte centré sur l'écran.
            ''' </summary>
            ''' <param name="text">Texte à centrer.</param>
            Public Sub SetTextCentered(text As String)
                Dim centered As String = text.PadLeft((Capacity + text.Length) \ 2).PadRight(Capacity)
                SetText(centered.Substring(0, Capacity))
            End Sub

        End Class
        ''' <summary>
        ''' Regroupe plusieurs <see cref="C24_LedButton"/> pour les piloter simultanément.
        ''' Permet d'allumer, éteindre ou faire clignoter un ensemble de boutons en un seul appel.
        ''' Supporte un mode exclusif (un seul bouton actif à la fois dans le groupe).
        ''' </summary>
        Public Class C24_GroupLedButton

            Private ReadOnly _Buttons As New List(Of C24_LedButton)
            Private _ExclusiveMode As Boolean = False

            ' ── Événement ─────────────────────────────────────────────────────

            ''' <summary>
            ''' Déclenché quand un bouton du groupe est relâché.
            ''' Retourne le bouton source et son index dans le groupe.
            ''' </summary>
            Public Event OnGroupButtonReleased(button As C24_LedButton, index As Integer)

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Nombre de boutons dans le groupe.</summary>
            Public ReadOnly Property Count As Integer
                Get
                    Return _Buttons.Count
                End Get
            End Property

            ''' <summary>Accès indexé à un bouton du groupe.</summary>
            Public ReadOnly Property Buttons(index As Integer) As C24_LedButton
                Get
                    Return _Buttons(index)
                End Get
            End Property

            ''' <summary>
            ''' Active le mode exclusif : un seul bouton peut être allumé à la fois.
            ''' L'activation d'un bouton éteint automatiquement les autres.
            ''' </summary>
            Public Property ExclusiveMode As Boolean
                Get
                    Return _ExclusiveMode
                End Get
                Set
                    If Value <> _ExclusiveMode Then
                        _ExclusiveMode = Value
                    End If
                End Set
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Crée un groupe vide. Utiliser <see cref="Add"/> pour ajouter des boutons.
            ''' </summary>
            Public Sub New()
            End Sub

            ''' <summary>
            ''' Crée un groupe à partir d'une liste de boutons existants.
            ''' </summary>
            ''' <param name="buttons">Boutons à regrouper.</param>
            Public Sub New(buttons As IEnumerable(Of C24_LedButton))
                For Each btn In buttons
                    Add(btn)
                Next
            End Sub

            ' ── Gestion des boutons ───────────────────────────────────────────

            ''' <summary>
            ''' Ajoute un bouton au groupe et branche l'événement de relâchement.
            ''' </summary>
            ''' <param name="button">Bouton à ajouter.</param>
            Public Sub Add(button As C24_LedButton)
                Dim idx As Integer = _Buttons.Count
                _Buttons.Add(button)
                Dim capturedBtn = button
                Dim capturedIdx = idx
                AddHandler button.OnButtonReleased,
                    Sub(trackNb As Integer?)
                        If _ExclusiveMode Then
                            SetExclusive(capturedIdx)
                        End If
                        RaiseEvent OnGroupButtonReleased(capturedBtn, capturedIdx)
                    End Sub
            End Sub

            ''' <summary>
            ''' Retire un bouton du groupe par index.
            ''' </summary>
            ''' <param name="index">Index 0-based du bouton à retirer.</param>
            Public Sub RemoveAt(index As Integer)
                If index >= 0 AndAlso index < _Buttons.Count Then
                    _Buttons.RemoveAt(index)
                End If
            End Sub

            ' ── Contrôle de groupe ────────────────────────────────────────────

            ''' <summary>Allume toutes les LEDs du groupe.</summary>
            Public Sub SetAllOn()
                For Each btn In _Buttons
                    btn.Led = True
                Next
            End Sub

            ''' <summary>Éteint toutes les LEDs du groupe.</summary>
            Public Sub SetAllOff()
                For Each btn In _Buttons
                    btn.Led = False
                    btn.IsBlinking = False
                Next
            End Sub

            ''' <summary>Active ou désactive le clignotement sur tous les boutons.</summary>
            ''' <param name="blinking">True pour activer le clignotement.</param>
            Public Sub SetAllBlinking(blinking As Boolean)
                For Each btn In _Buttons
                    btn.IsBlinking = blinking
                Next
            End Sub

            ''' <summary>
            ''' Allume uniquement le bouton à l'index spécifié et éteint tous les autres.
            ''' </summary>
            ''' <param name="index">Index 0-based du bouton à activer.</param>
            Public Sub SetExclusive(index As Integer)
                For i = 0 To _Buttons.Count - 1
                    _Buttons(i).IsBlinking = False
                    _Buttons(i).Led = (i = index)
                Next
            End Sub

            ''' <summary>
            ''' Définit l'état LED de chaque bouton selon un tableau de booléens.
            ''' Les entrées excédentaires sont ignorées.
            ''' </summary>
            ''' <param name="states">Tableau d'états (True = allumé).</param>
            Public Sub SetStates(states As IReadOnlyList(Of Boolean))
                For i = 0 To Math.Min(_Buttons.Count, states.Count) - 1
                    _Buttons(i).Led = states(i)
                Next
            End Sub

            ''' <summary>
            ''' Retourne l'index du premier bouton dont la LED est allumée.
            ''' Retourne -1 si aucun bouton n'est allumé.
            ''' </summary>
            Public Function GetActiveIndex() As Integer
                For i = 0 To _Buttons.Count - 1
                    If _Buttons(i).Led Then
                        Return i
                    End If
                Next
                Return -1
            End Function

        End Class



        Public Class C24_Desk
            Inherits C24_Components
            ' ── Signal d'initialisation des composants ─────────────────────────
            Private ReadOnly _initEvent As New Threading.ManualResetEventSlim(False)

            Const C24_port As Integer = 9123
            Private _NetworkInterface As NetworkInterface

            Private _BlinkTimer As System.Threading.Timer
            Private _BlinkInterval As Integer = 500   ' ms — modifiable avant/après démarrage


            Dim WithEvents cs1 As C24_ChannelStrip

            Private _isCapturing As Boolean = False
            Private _IsRunning As Boolean
            Private SendingInstantQueueThread As Threading.Thread
            Private ReceivedC24PacketThread As Thread
            Private _packetCapture As NativePcapAdapter
            Private _TimingQueueTimeout As Integer = 50
            Private SendingTimingQueueThread As Threading.Thread
            Private SendingAllCmdBytesQueueThread As Threading.Thread
            Private _OSCreceiveThread As Thread
            Private _ComputerMacAddr As Byte()
            Private _PacketManager As Packet.PacketManager
            Private _ReceivedC24PacketQueue As ConcurrentQueue(Of Packet.C24_Packet)
            Private _AllCmdBytesQueue As ConcurrentQueue(Of List(Of Byte()))
            Private _BlinkState As Boolean = False
            Private _InstantQueueTimeout As Integer = 5

            ' ── Channel Strip ──────────────────────────────────────────────
            Private ReadOnly _ChannelStrips As New List(Of C24_ChannelStrip)
            Private ReadOnly _Solos As New List(Of C24_LedButton)
            Private ReadOnly _Mutes As New List(Of C24_LedButton)
            Private ReadOnly _ScribbleStrips As New List(Of C24_ScribbleStrip)
            Private ReadOnly _Vpots As New List(Of C24_Vpot)
            Private ReadOnly _Selects As New List(Of C24_LedButton)
            Private ReadOnly _Automodes As New List(Of C24_Button)
            Private ReadOnly _Faders As New List(Of C24_Fader)
            Private ReadOnly _RecArms As New List(Of C24_LedButton)

            ' ── Solo Settings ──────────────────────────────────────────────
            Private _SoloSafe As C24_LedButton
            Private _SoloClear As C24_LedButton

            ' ── Channel Settings ──────────────────────────────────────────────
            Private _Copy_Settings As C24_LedButton
            Private _Specify_Settings As C24_LedButton
            Private _Edit_Byp As C24_LedButton
            Private _Paste_Setting As C24_LedButton
            Private _Ins_Byp As C24_LedButton
            Private _Apply_To_All_Channels As C24_LedButton
            Private _Apply_To_All_Selected_Channels As C24_LedButton
            Private _Default As C24_LedButton

            ' ── Automation ────────────────────────────────────────────────────
            Private _Auto_Write As C24_LedButton
            Private _Auto_Fader As C24_LedButton
            Private _Auto_Touch As C24_LedButton
            Private _Auto_Pan As C24_LedButton
            Private _Auto_Latch As C24_LedButton
            Private _Auto_Mute As C24_LedButton
            Private _Auto_Trim As C24_LedButton
            Private _Auto_Send_Level As C24_LedButton
            Private _Auto_Read As C24_LedButton
            Private _Auto_Send_Mute As C24_LedButton
            Private _Auto_Off As C24_LedButton
            Private _Auto_Plug_In As C24_LedButton
            Private _Auto_Suspend As C24_LedButton
            Private _Write_Auto_To_End As C24_LedButton
            Private _Write_Auto_To_Start As C24_LedButton
            Private _Write_Auto_To_All As C24_LedButton

            ' ── Modifiers ─────────────────────────────────────────────────────
            Private _Shift As C24_Button
            Private _Opt As C24_Button
            Private _Ctl_Clutch As C24_Button
            Private _Ctrl As C24_Button

            ' ── Monitor Section ───────────────────────────────────────────────
            Private _Ms_Input As C24_LedButton
            Private _Ms_Pre_Fader As C24_LedButton
            Private _Ms_Post_Fader As C24_LedButton
            Private _Ms_Channel As C24_LedButton
            Private _Ms_Clear_Peaks As C24_LedButton

            ' ── Function Keys ─────────────────────────────────────────────────
            Private _F1 As C24_LedButton
            Private _F2 As C24_LedButton
            Private _F3 As C24_LedButton
            Private _F4 As C24_LedButton
            Private _F5 As C24_LedButton
            Private _F6 As C24_LedButton
            Private _F7 As C24_LedButton
            Private _F8 As C24_LedButton
            Private _F9 As C24_LedButton
            Private _F10 As C24_LedButton

            ' ── Display / Misc ────────────────────────────────────────────────
            Private _Rec_Safe As C24_LedButton
            Private _Master_Rec As C24_LedButton
            Private _Show_Values As C24_LedButton
            Private _Show_Group As C24_LedButton
            Private _Show_Channel_Names As C24_LedButton
            Private _Show_Info As C24_LedButton
            Private _Vel_Sens_Encoders As C24_LedButton
            Private _Auto_Select As C24_LedButton
            Private _Pre_Post As C24_LedButton
            Private _Send_Mute As C24_LedButton

            ' ── Plugin / Channel Controls ─────────────────────────────────────
            Private _Master_Bypass As C24_LedButton
            Private _Plugin_Safe As C24_LedButton
            Private _Compare As C24_LedButton
            Private _Inserts_Param As C24_LedButton
            Private _Assign As C24_LedButton
            Private _Input As C24_LedButton
            Private _Monitor As C24_LedButton
            Private _Remote_Mic_Pre As C24_LedButton

            ' ── Navigation / Send ─────────────────────────────────────────────
            Private _Esc_Cancel As C24_LedButton
            Private _Flip As C24_LedButton
            Private _Output As C24_LedButton
            Private _Send_A_F As C24_LedButton
            Private _Send_B_G As C24_LedButton
            Private _Send_C_H As C24_LedButton
            Private _Send_D_I As C24_LedButton
            Private _Send_E_J As C24_LedButton

            ' ── Pan Controls ──────────────────────────────────────────────────
            Private _Pan_L_C_R As C24_LedButton
            Private _Pan_Rear As C24_LedButton
            Private _Pan_F_R As C24_LedButton
            Private _Pan_Cent As C24_LedButton
            Private _Pan_Lfe As C24_LedButton
            Private _Pan_Left_Right As C24_LedButton
            ' ── Channel Bank Select ───────────────────────────────────────────
            Private _Cbs_Left As C24_LedButton
            Private _Cbs_Right As C24_LedButton
            ' ── Groups ───────────────────────────────────────────────────────
            Private _Create As C24_LedButton
            Private _Enable As C24_LedButton
            Private _Edit As C24_LedButton
            Private _Delete As C24_LedButton
            Private _Suspend As C24_LedButton
            ' ── Edit ─────────────────────────────────────────────────────────
            Private _Cut As C24_Button
            Private _Copy As C24_Button
            Private _Paste As C24_Button
            Private _Edit_Delete As C24_Button
            Private _Separate As C24_Button
            Private _Capture As C24_Button
            Private _Duplicate As C24_Button
            Private _Repeat As C24_Button
            ' ── ZoomPreset ───────────────────────────────────────────────────
            Private _Preset_1 As C24_LedButton
            Private _Preset_2 As C24_LedButton
            Private _Preset_3 As C24_LedButton
            Private _Preset_4 As C24_LedButton
            Private _Preset_5 As C24_LedButton
            ' ── EditMode ─────────────────────────────────────────────────────
            Private _Spot As C24_LedButton
            Private _Grid As C24_LedButton
            Private _Shuffle As C24_LedButton
            Private _Slip As C24_LedButton
            ' ── Window ───────────────────────────────────────────────────────
            Private _W_Status As C24_LedButton
            Private _W_Pan As C24_LedButton
            Private _W_Plugin As C24_LedButton
            Private _W_Mix As C24_LedButton
            Private _W_Edit As C24_LedButton
            Private _W_Mem_Loc As C24_LedButton
            Private _W_Trans As C24_LedButton
            Private _W_Alt As C24_LedButton
            ' ── EditTools ────────────────────────────────────────────────────
            Private _Tools_Trim As C24_LedButton
            Private _Tools_Select As C24_LedButton
            Private _Tools_Grab As C24_LedButton
            Private _Tools_Pencil As C24_LedButton
            Private _Tools_Midi As C24_LedButton
            Private _Tools_Smart As C24_LedButton
            Private _Tools_Link_Edit As C24_LedButton
            Private _Tools_Auto_To_Cur As C24_LedButton
            Private _Tools_Auto_To_All As C24_LedButton
            ' ── Navigation ───────────────────────────────────────────────────
            Private _Zoom As C24_LedButton
            Private _Sel_Adj As C24_LedButton
            Private _Up As C24_Button
            Private _Next As C24_Button
            Private _Down As C24_Button
            Private _Previous As C24_Button
            Private _Nav As C24_LedButton
            ' ── Transport ────────────────────────────────────────────────────
            Private _Loop_Play As C24_LedButton
            Private _Loop_Rec As C24_LedButton
            Private _Audition As C24_LedButton
            Private _Pre_Roll As C24_LedButton
            Private _In As C24_LedButton
            Private _Out As C24_LedButton
            Private _Post_Roll As C24_LedButton
            Private _Goto_Start As C24_LedButton
            Private _Goto_End As C24_LedButton
            Private _Ext_Trans As C24_LedButton
            Private _Quick_Punch As C24_LedButton
            Private _Online As C24_LedButton
            Private _Talk_Back As C24_LedButton
            Private _Play As C24_LedButton
            Private _Rec As C24_LedButton
            Private _Rew As C24_LedButton
            Private _Fwd As C24_LedButton
            Private _Stop As C24_LedButton
            ' ── Save ─────────────────────────────────────────────────────────
            Private _Save As C24_LedButton
            Private _Undo As C24_LedButton
            ' ── Numpad ───────────────────────────────────────────────────────
            Private _Num_0 As C24_Button
            Private _Num_1 As C24_Button
            Private _Num_2 As C24_Button
            Private _Num_3 As C24_Button
            Private _Num_4 As C24_Button
            Private _Num_5 As C24_Button
            Private _Num_6 As C24_Button
            Private _Num_7 As C24_Button
            Private _Num_8 As C24_Button
            Private _Num_9 As C24_Button
            Private _Num_Point As C24_Button
            Private _Num_Clr As C24_Button
            Private _Num_Egal As C24_Button
            Private _Num_Div As C24_Button
            Private _Num_Mult As C24_Button
            Private _Num_Sous As C24_Button
            Private _Num_Add As C24_Button
            Private _Num_Enter As C24_Button
            ' ── Utility ──────────────────────────────────────────────────────

            Private _Master_Faders As C24_LedButton
            Private _Esc_Cancel_2 As C24_LedButton
            Private _Scrub As C24_LedButton
            Private _Shuttle As C24_LedButton

            ' ── Counter ──────────────────────────────────────────────────────
            Private _Counter_Mode As C24_LedButton
            ' ── Clock ────────────────────────────────────────────────────────
            Private _Clock As C24_Clock
            ' ── JogWheel ─────────────────────────────────────────────────────
            Private _JogWheel As C24_JogWheel
            ' ── Bank ─────────────────────────────────────────────────────────
            Private _Bank_Left As C24_Button
            Private _Nudge As C24_LedButton
            Private _Bank_Right As C24_Button
            ' ── ScribbleScreen ───────────────────────────────────────────────
            Private _ScreenLine As C24_ScribbleScreen
            Private _ScreenChannelBarScroll As C24_ScribbleScreen
            ' ── Group Buttons  ───────────────────────────────────────────────
            Private _Esc_Group As C24_GroupLedButton
            ' ── Masters VuMeters  ────────────────────────────────────────────
            Private _VueMeterMaster1 As C24_VueMeter
            Private _VueMeterMaster2 As C24_VueMeter
            Private _VueMeterMaster3 As C24_VueMeter


            ' ── Masters VuMeters  ────────────────────────────────────────────
            Public ReadOnly Property VueMeterMaster1 As C24_VueMeter
                Get
                    Return _VueMeterMaster1
                End Get
            End Property
            Public ReadOnly Property VueMeterMaster2 As C24_VueMeter
                Get
                    Return _VueMeterMaster2
                End Get
            End Property
            Public ReadOnly Property VueMeterMaster3 As C24_VueMeter
                Get
                    Return _VueMeterMaster3
                End Get
            End Property
            ' ── Group Buttons  ───────────────────────────────────────────────
            Public ReadOnly Property Esc_Group As C24_GroupLedButton
                Get
                    Return _Esc_Group
                End Get
            End Property

            ' ── ScreenChannelBarScroll ───────────────────────────────────────────────
            ''' <summary>
            ''' Écran virtuel composé des 2 ScribbleStrips.
            ''' Capacité totale : 8 caractères (2 × 4).
            ''' </summary>
            Public ReadOnly Property ScreenChannelBarScroll As C24_ScribbleScreen
                Get
                    Return _ScreenChannelBarScroll
                End Get
            End Property

            ' ── ScreenLine ───────────────────────────────────────────────
            ''' <summary>
            ''' Écran virtuel composé des 24 ScribbleStrips des channel strips dans l'ordre.
            ''' Capacité totale : 96 caractères (24 × 4).
            ''' </summary>
            Public ReadOnly Property ScreenLine As C24_ScribbleScreen
                Get
                    Return _ScreenLine
                End Get
            End Property
            ' ── Bank ─────────────────────────────────────────────────────────
            ''' <summary>Bouton Bank Left.</summary>
            Public ReadOnly Property Bank_Left As C24_Button
                Get
                    Return _Bank_Left
                End Get
            End Property

            ''' <summary>Bouton Nudge.</summary>
            Public ReadOnly Property Nudge As C24_LedButton
                Get
                    Return _Nudge
                End Get
            End Property

            ''' <summary>Bouton Bank Right.</summary>
            Public ReadOnly Property Bank_Right As C24_Button
                Get
                    Return _Bank_Right
                End Get
            End Property
            ''' <summary>Molette Jog du Control24.</summary>
            Public ReadOnly Property JogWheel As C24_JogWheel
                Get
                    Return _JogWheel
                End Get
            End Property
            ' ── Clock ────────────────────────────────────────────────────────
            ''' <summary>Composant horloge (LEDs de mode + affichage 7 segments).</summary>
            Public ReadOnly Property Clock As C24_Clock
                Get
                    Return _Clock
                End Get
            End Property
            ' ── Counter ──────────────────────────────────────────────────────
            ''' <summary>Bouton Counter Mode.</summary>
            Public ReadOnly Property Counter_Mode As C24_LedButton
                Get
                    Return _Counter_Mode
                End Get
            End Property
            ' ── Utility ──────────────────────────────────────────────────────


            ''' <summary>Bouton Master Faders.</summary>
            Public ReadOnly Property Master_Faders As C24_LedButton
                Get
                    Return _Master_Faders
                End Get
            End Property

            ''' <summary>Bouton Esc Cancel 2.</summary>
            Public ReadOnly Property Esc_Cancel_2 As C24_LedButton
                Get
                    Return _Esc_Cancel_2
                End Get
            End Property

            ''' <summary>Bouton Scrub.</summary>
            Public ReadOnly Property Scrub As C24_LedButton
                Get
                    Return _Scrub
                End Get
            End Property

            ''' <summary>Bouton Shuttle.</summary>
            Public ReadOnly Property Shuttle As C24_LedButton
                Get
                    Return _Shuttle
                End Get
            End Property
            ' ── Numpad ───────────────────────────────────────────────────────
            ''' <summary>Bouton Num 0.</summary>
            Public ReadOnly Property Num_0 As C24_Button
                Get
                    Return _Num_0
                End Get
            End Property

            ''' <summary>Bouton Num 1.</summary>
            Public ReadOnly Property Num_1 As C24_Button
                Get
                    Return _Num_1
                End Get
            End Property

            ''' <summary>Bouton Num 2.</summary>
            Public ReadOnly Property Num_2 As C24_Button
                Get
                    Return _Num_2
                End Get
            End Property

            ''' <summary>Bouton Num 3.</summary>
            Public ReadOnly Property Num_3 As C24_Button
                Get
                    Return _Num_3
                End Get
            End Property

            ''' <summary>Bouton Num 4.</summary>
            Public ReadOnly Property Num_4 As C24_Button
                Get
                    Return _Num_4
                End Get
            End Property

            ''' <summary>Bouton Num 5.</summary>
            Public ReadOnly Property Num_5 As C24_Button
                Get
                    Return _Num_5
                End Get
            End Property

            ''' <summary>Bouton Num 6.</summary>
            Public ReadOnly Property Num_6 As C24_Button
                Get
                    Return _Num_6
                End Get
            End Property

            ''' <summary>Bouton Num 7.</summary>
            Public ReadOnly Property Num_7 As C24_Button
                Get
                    Return _Num_7
                End Get
            End Property

            ''' <summary>Bouton Num 8.</summary>
            Public ReadOnly Property Num_8 As C24_Button
                Get
                    Return _Num_8
                End Get
            End Property

            ''' <summary>Bouton Num 9.</summary>
            Public ReadOnly Property Num_9 As C24_Button
                Get
                    Return _Num_9
                End Get
            End Property

            ''' <summary>Bouton Num Point.</summary>
            Public ReadOnly Property Num_Point As C24_Button
                Get
                    Return _Num_Point
                End Get
            End Property

            ''' <summary>Bouton Num Clr.</summary>
            Public ReadOnly Property Num_Clr As C24_Button
                Get
                    Return _Num_Clr
                End Get
            End Property

            ''' <summary>Bouton Num Egal.</summary>
            Public ReadOnly Property Num_Egal As C24_Button
                Get
                    Return _Num_Egal
                End Get
            End Property

            ''' <summary>Bouton Num Div.</summary>
            Public ReadOnly Property Num_Div As C24_Button
                Get
                    Return _Num_Div
                End Get
            End Property

            ''' <summary>Bouton Num Mult.</summary>
            Public ReadOnly Property Num_Mult As C24_Button
                Get
                    Return _Num_Mult
                End Get
            End Property

            ''' <summary>Bouton Num Sous.</summary>
            Public ReadOnly Property Num_Sous As C24_Button
                Get
                    Return _Num_Sous
                End Get
            End Property

            ''' <summary>Bouton Num Add.</summary>
            Public ReadOnly Property Num_Add As C24_Button
                Get
                    Return _Num_Add
                End Get
            End Property

            ''' <summary>Bouton Num Enter.</summary>
            Public ReadOnly Property Num_Enter As C24_Button
                Get
                    Return _Num_Enter
                End Get
            End Property
            ' ── Save ─────────────────────────────────────────────────────────
            ''' <summary>Bouton Save.</summary>
            Public ReadOnly Property Save As C24_LedButton
                Get
                    Return _Save
                End Get
            End Property

            ''' <summary>Bouton Undo.</summary>
            Public ReadOnly Property Undo As C24_LedButton
                Get
                    Return _Undo
                End Get
            End Property
            ' ── Transport ────────────────────────────────────────────────────
            ''' <summary>Bouton Loop Play.</summary>
            Public ReadOnly Property Loop_Play As C24_LedButton
                Get
                    Return _Loop_Play
                End Get
            End Property
            ''' <summary>Bouton Audition.</summary>
            Public ReadOnly Property Audition As C24_LedButton
                Get
                    Return _Audition
                End Get
            End Property
            ''' <summary>Bouton Loop Rec.</summary>
            Public ReadOnly Property Loop_Rec As C24_LedButton
                Get
                    Return _Loop_Rec
                End Get
            End Property

            ''' <summary>Bouton Pre Roll.</summary>
            Public ReadOnly Property Pre_Roll As C24_LedButton
                Get
                    Return _Pre_Roll
                End Get
            End Property

            ''' <summary>Bouton In.</summary>
            Public ReadOnly Property [In] As C24_LedButton
                Get
                    Return _In
                End Get
            End Property

            ''' <summary>Bouton Out.</summary>
            Public ReadOnly Property Out As C24_LedButton
                Get
                    Return _Out
                End Get
            End Property

            ''' <summary>Bouton Post Roll.</summary>
            Public ReadOnly Property Post_Roll As C24_LedButton
                Get
                    Return _Post_Roll
                End Get
            End Property

            ''' <summary>Bouton Goto Start.</summary>
            Public ReadOnly Property Goto_Start As C24_LedButton
                Get
                    Return _Goto_Start
                End Get
            End Property

            ''' <summary>Bouton Goto End.</summary>
            Public ReadOnly Property Goto_End As C24_LedButton
                Get
                    Return _Goto_End
                End Get
            End Property

            ''' <summary>Bouton Ext Trans.</summary>
            Public ReadOnly Property Ext_Trans As C24_LedButton
                Get
                    Return _Ext_Trans
                End Get
            End Property

            ''' <summary>Bouton Quick Punch.</summary>
            Public ReadOnly Property Quick_Punch As C24_LedButton
                Get
                    Return _Quick_Punch
                End Get
            End Property

            ''' <summary>Bouton Online.</summary>
            Public ReadOnly Property Online As C24_LedButton
                Get
                    Return _Online
                End Get
            End Property

            ''' <summary>Bouton Talk Back.</summary>
            Public ReadOnly Property Talk_Back As C24_LedButton
                Get
                    Return _Talk_Back
                End Get
            End Property
            ''' <summary>Bouton Play.</summary>
            Public ReadOnly Property Play As C24_LedButton
                Get
                    Return _Play
                End Get
            End Property

            ''' <summary>Bouton Rec.</summary>
            Public ReadOnly Property Rec As C24_LedButton
                Get
                    Return _Rec
                End Get
            End Property

            ''' <summary>Bouton Rew.</summary>
            Public ReadOnly Property Rew As C24_LedButton
                Get
                    Return _Rew
                End Get
            End Property

            ''' <summary>Bouton Fwd.</summary>
            Public ReadOnly Property Fwd As C24_LedButton
                Get
                    Return _Fwd
                End Get
            End Property

            ''' <summary>Bouton Stop.</summary>
            Public ReadOnly Property [Stop] As C24_LedButton
                Get
                    Return _Stop
                End Get
            End Property
            ' ── Navigation ───────────────────────────────────────────────────
            ''' <summary>Bouton Zoom.</summary>
            Public ReadOnly Property Zoom As C24_LedButton
                Get
                    Return _Zoom
                End Get
            End Property

            ''' <summary>Bouton Sel Adj.</summary>
            Public ReadOnly Property Sel_Adj As C24_LedButton
                Get
                    Return _Sel_Adj
                End Get
            End Property

            ''' <summary>Bouton Up.</summary>
            Public ReadOnly Property Up As C24_Button
                Get
                    Return _Up
                End Get
            End Property

            ''' <summary>Bouton Next.</summary>
            Public ReadOnly Property [Next] As C24_Button
                Get
                    Return _Next
                End Get
            End Property

            ''' <summary>Bouton Down.</summary>
            Public ReadOnly Property Down As C24_Button
                Get
                    Return _Down
                End Get
            End Property

            ''' <summary>Bouton Previous.</summary>
            Public ReadOnly Property Previous As C24_Button
                Get
                    Return _Previous
                End Get
            End Property

            ''' <summary>Bouton Nav.</summary>
            Public ReadOnly Property Nav As C24_LedButton
                Get
                    Return _Nav
                End Get
            End Property
            ' ── EditTools ────────────────────────────────────────────────────
            ''' <summary>Bouton Tools Trim.</summary>
            Public ReadOnly Property Tools_Trim As C24_LedButton
                Get
                    Return _Tools_Trim
                End Get
            End Property

            ''' <summary>Bouton Tools Select.</summary>
            Public ReadOnly Property Tools_Select As C24_LedButton
                Get
                    Return _Tools_Select
                End Get
            End Property

            ''' <summary>Bouton Tools Grab.</summary>
            Public ReadOnly Property Tools_Grab As C24_LedButton
                Get
                    Return _Tools_Grab
                End Get
            End Property

            ''' <summary>Bouton Tools Pencil.</summary>
            Public ReadOnly Property Tools_Pencil As C24_LedButton
                Get
                    Return _Tools_Pencil
                End Get
            End Property

            ''' <summary>Bouton Tools Midi.</summary>
            Public ReadOnly Property Tools_Midi As C24_LedButton
                Get
                    Return _Tools_Midi
                End Get
            End Property

            ''' <summary>Bouton Tools Smart.</summary>
            Public ReadOnly Property Tools_Smart As C24_LedButton
                Get
                    Return _Tools_Smart
                End Get
            End Property

            ''' <summary>Bouton Tools Link Edit.</summary>
            Public ReadOnly Property Tools_Link_Edit As C24_LedButton
                Get
                    Return _Tools_Link_Edit
                End Get
            End Property

            ''' <summary>Bouton Tools Auto To Cur.</summary>
            Public ReadOnly Property Tools_Auto_To_Cur As C24_LedButton
                Get
                    Return _Tools_Auto_To_Cur
                End Get
            End Property

            ''' <summary>Bouton Tools Auto To All.</summary>
            Public ReadOnly Property Tools_Auto_To_All As C24_LedButton
                Get
                    Return _Tools_Auto_To_All
                End Get
            End Property
            ' ── Window ───────────────────────────────────────────────────────
            ''' <summary>Bouton W Status.</summary>
            Public ReadOnly Property W_Status As C24_LedButton
                Get
                    Return _W_Status
                End Get
            End Property

            ''' <summary>Bouton W Pan.</summary>
            Public ReadOnly Property W_Pan As C24_LedButton
                Get
                    Return _W_Pan
                End Get
            End Property

            ''' <summary>Bouton W Plugin.</summary>
            Public ReadOnly Property W_Plugin As C24_LedButton
                Get
                    Return _W_Plugin
                End Get
            End Property

            ''' <summary>Bouton W Mix.</summary>
            Public ReadOnly Property W_Mix As C24_LedButton
                Get
                    Return _W_Mix
                End Get
            End Property

            ''' <summary>Bouton W Edit.</summary>
            Public ReadOnly Property W_Edit As C24_LedButton
                Get
                    Return _W_Edit
                End Get
            End Property

            ''' <summary>Bouton W Mem Loc.</summary>
            Public ReadOnly Property W_Mem_Loc As C24_LedButton
                Get
                    Return _W_Mem_Loc
                End Get
            End Property

            ''' <summary>Bouton W Trans.</summary>
            Public ReadOnly Property W_Trans As C24_LedButton
                Get
                    Return _W_Trans
                End Get
            End Property

            ''' <summary>Bouton W Alt.</summary>
            Public ReadOnly Property W_Alt As C24_LedButton
                Get
                    Return _W_Alt
                End Get
            End Property
            ' ── EditMode ─────────────────────────────────────────────────────
            ''' <summary>Bouton Spot.</summary>
            Public ReadOnly Property Spot As C24_LedButton
                Get
                    Return _Spot
                End Get
            End Property

            ''' <summary>Bouton Grid.</summary>
            Public ReadOnly Property Grid As C24_LedButton
                Get
                    Return _Grid
                End Get
            End Property

            ''' <summary>Bouton Shuffle.</summary>
            Public ReadOnly Property Shuffle As C24_LedButton
                Get
                    Return _Shuffle
                End Get
            End Property

            ''' <summary>Bouton Slip.</summary>
            Public ReadOnly Property Slip As C24_LedButton
                Get
                    Return _Slip
                End Get
            End Property
            ' ── ZoomPreset ───────────────────────────────────────────────────
            ''' <summary>Bouton Preset 1.</summary>
            Public ReadOnly Property Preset_1 As C24_LedButton
                Get
                    Return _Preset_1
                End Get
            End Property

            ''' <summary>Bouton Preset 2.</summary>
            Public ReadOnly Property Preset_2 As C24_LedButton
                Get
                    Return _Preset_2
                End Get
            End Property

            ''' <summary>Bouton Preset 3.</summary>
            Public ReadOnly Property Preset_3 As C24_LedButton
                Get
                    Return _Preset_3
                End Get
            End Property

            ''' <summary>Bouton Preset 4.</summary>
            Public ReadOnly Property Preset_4 As C24_LedButton
                Get
                    Return _Preset_4
                End Get
            End Property

            ''' <summary>Bouton Preset 5.</summary>
            Public ReadOnly Property Preset_5 As C24_LedButton
                Get
                    Return _Preset_5
                End Get
            End Property
            ' ── Edit ─────────────────────────────────────────────────────────
            ''' <summary>Bouton Cut.</summary>
            Public ReadOnly Property Cut As C24_Button
                Get
                    Return _Cut
                End Get
            End Property

            ''' <summary>Bouton Copy.</summary>
            Public ReadOnly Property [Copy] As C24_Button
                Get
                    Return _Copy
                End Get
            End Property

            ''' <summary>Bouton Paste.</summary>
            Public ReadOnly Property Paste As C24_Button
                Get
                    Return _Paste
                End Get
            End Property

            ''' <summary>Bouton Delete (Edit).</summary>
            Public ReadOnly Property Edit_Delete As C24_Button
                Get
                    Return _Edit_Delete
                End Get
            End Property

            ''' <summary>Bouton Separate.</summary>
            Public ReadOnly Property Separate As C24_Button
                Get
                    Return _Separate
                End Get
            End Property

            ''' <summary>Bouton Capture.</summary>
            Public ReadOnly Property Capture As C24_Button
                Get
                    Return _Capture
                End Get
            End Property

            ''' <summary>Bouton Duplicate.</summary>
            Public ReadOnly Property Duplicate As C24_Button
                Get
                    Return _Duplicate
                End Get
            End Property

            ''' <summary>Bouton Repeat.</summary>
            Public ReadOnly Property Repeat As C24_Button
                Get
                    Return _Repeat
                End Get
            End Property
            ' ── Groups ───────────────────────────────────────────────────────
            ''' <summary>Bouton Create.</summary>
            Public ReadOnly Property Create As C24_LedButton
                Get
                    Return _Create
                End Get
            End Property

            ''' <summary>Bouton Enable.</summary>
            Public ReadOnly Property Enable As C24_LedButton
                Get
                    Return _Enable
                End Get
            End Property

            ''' <summary>Bouton Edit.</summary>
            Public ReadOnly Property [Edit] As C24_LedButton
                Get
                    Return _Edit
                End Get
            End Property

            ''' <summary>Bouton Delete.</summary>
            Public ReadOnly Property Delete As C24_LedButton
                Get
                    Return _Delete
                End Get
            End Property

            ''' <summary>Bouton Suspend.</summary>
            Public ReadOnly Property Suspend As C24_LedButton
                Get
                    Return _Suspend
                End Get
            End Property
            ' ── Channel Bank Select ───────────────────────────────────────────
            ''' <summary>Bouton Cbs Left.</summary>
            Public ReadOnly Property Cbs_Left As C24_LedButton
                Get
                    Return _Cbs_Left
                End Get
            End Property

            ''' <summary>Bouton Cbs Right.</summary>
            Public ReadOnly Property Cbs_Right As C24_LedButton
                Get
                    Return _Cbs_Right
                End Get
            End Property

            ' ── Pan Controls ──────────────────────────────────────────────────
            ''' <summary>Bouton Pan L C R.</summary>
            Public ReadOnly Property Pan_L_C_R As C24_LedButton
                Get
                    Return _Pan_L_C_R
                End Get
            End Property

            ''' <summary>Bouton Pan Rear.</summary>
            Public ReadOnly Property Pan_Rear As C24_LedButton
                Get
                    Return _Pan_Rear
                End Get
            End Property

            ''' <summary>Bouton Pan F R.</summary>
            Public ReadOnly Property Pan_F_R As C24_LedButton
                Get
                    Return _Pan_F_R
                End Get
            End Property

            ''' <summary>Bouton Pan Cent.</summary>
            Public ReadOnly Property Pan_Cent As C24_LedButton
                Get
                    Return _Pan_Cent
                End Get
            End Property

            ''' <summary>Bouton Pan Lfe.</summary>
            Public ReadOnly Property Pan_Lfe As C24_LedButton
                Get
                    Return _Pan_Lfe
                End Get
            End Property

            ''' <summary>Bouton Pan Left Right.</summary>
            Public ReadOnly Property Pan_Left_Right As C24_LedButton
                Get
                    Return _Pan_Left_Right
                End Get
            End Property
            ' ── Navigation / Send ─────────────────────────────────────────────
            ''' <summary>Bouton Esc Cancel.</summary>
            Public ReadOnly Property Esc_Cancel As C24_LedButton
                Get
                    Return _Esc_Cancel
                End Get
            End Property

            ''' <summary>Bouton Flip.</summary>
            Public ReadOnly Property Flip As C24_LedButton
                Get
                    Return _Flip
                End Get
            End Property

            ''' <summary>Bouton Output.</summary>
            Public ReadOnly Property [Output] As C24_LedButton
                Get
                    Return _Output
                End Get
            End Property

            ''' <summary>Bouton Send A F.</summary>
            Public ReadOnly Property Send_A_F As C24_LedButton
                Get
                    Return _Send_A_F
                End Get
            End Property

            ''' <summary>Bouton Send B G.</summary>
            Public ReadOnly Property Send_B_G As C24_LedButton
                Get
                    Return _Send_B_G
                End Get
            End Property

            ''' <summary>Bouton Send C H.</summary>
            Public ReadOnly Property Send_C_H As C24_LedButton
                Get
                    Return _Send_C_H
                End Get
            End Property

            ''' <summary>Bouton Send D I.</summary>
            Public ReadOnly Property Send_D_I As C24_LedButton
                Get
                    Return _Send_D_I
                End Get
            End Property

            ''' <summary>Bouton Send E J.</summary>
            Public ReadOnly Property Send_E_J As C24_LedButton
                Get
                    Return _Send_E_J
                End Get
            End Property

            ' ── Plugin / Channel Controls ─────────────────────────────────────
            ''' <summary>Bouton Master Bypass.</summary>
            Public ReadOnly Property Master_Bypass As C24_LedButton
                Get
                    Return _Master_Bypass
                End Get
            End Property

            ''' <summary>Bouton Plugin Safe.</summary>
            Public ReadOnly Property Plugin_Safe As C24_LedButton
                Get
                    Return _Plugin_Safe
                End Get
            End Property

            ''' <summary>Bouton Compare.</summary>
            Public ReadOnly Property Compare As C24_LedButton
                Get
                    Return _Compare
                End Get
            End Property

            ''' <summary>Bouton Inserts Param.</summary>
            Public ReadOnly Property Inserts_Param As C24_LedButton
                Get
                    Return _Inserts_Param
                End Get
            End Property

            ''' <summary>Bouton Assign.</summary>
            Public ReadOnly Property Assign As C24_LedButton
                Get
                    Return _Assign
                End Get
            End Property

            ''' <summary>Bouton Input.</summary>
            Public ReadOnly Property [Input] As C24_LedButton
                Get
                    Return _Input
                End Get
            End Property

            ''' <summary>Bouton Monitor.</summary>
            Public ReadOnly Property Monitor As C24_LedButton
                Get
                    Return _Monitor
                End Get
            End Property

            ''' <summary>Bouton Remote Mic Pre.</summary>
            Public ReadOnly Property Remote_Mic_Pre As C24_LedButton
                Get
                    Return _Remote_Mic_Pre
                End Get
            End Property

            ' ── Monitor Section ───────────────────────────────────────────────
            ''' <summary>Bouton Ms Input.</summary>
            Public ReadOnly Property Ms_Input As C24_LedButton
                Get
                    Return _Ms_Input
                End Get
            End Property

            ''' <summary>Bouton Ms Pre Fader.</summary>
            Public ReadOnly Property Ms_Pre_Fader As C24_LedButton
                Get
                    Return _Ms_Pre_Fader
                End Get
            End Property

            ''' <summary>Bouton Ms Post Fader.</summary>
            Public ReadOnly Property Ms_Post_Fader As C24_LedButton
                Get
                    Return _Ms_Post_Fader
                End Get
            End Property

            ''' <summary>Bouton Ms Channel.</summary>
            Public ReadOnly Property Ms_Channel As C24_LedButton
                Get
                    Return _Ms_Channel
                End Get
            End Property

            ''' <summary>Bouton Ms Clear Peaks.</summary>
            Public ReadOnly Property Ms_Clear_Peaks As C24_LedButton
                Get
                    Return _Ms_Clear_Peaks
                End Get
            End Property

            ' ── Function Keys ─────────────────────────────────────────────────
            ''' <summary>Bouton F1.</summary>
            Public ReadOnly Property F1 As C24_LedButton
                Get
                    Return _F1
                End Get
            End Property

            ''' <summary>Bouton F2.</summary>
            Public ReadOnly Property F2 As C24_LedButton
                Get
                    Return _F2
                End Get
            End Property

            ''' <summary>Bouton F3.</summary>
            Public ReadOnly Property F3 As C24_LedButton
                Get
                    Return _F3
                End Get
            End Property

            ''' <summary>Bouton F4.</summary>
            Public ReadOnly Property F4 As C24_LedButton
                Get
                    Return _F4
                End Get
            End Property

            ''' <summary>Bouton F5.</summary>
            Public ReadOnly Property F5 As C24_LedButton
                Get
                    Return _F5
                End Get
            End Property

            ''' <summary>Bouton F6.</summary>
            Public ReadOnly Property F6 As C24_LedButton
                Get
                    Return _F6
                End Get
            End Property

            ''' <summary>Bouton F7.</summary>
            Public ReadOnly Property F7 As C24_LedButton
                Get
                    Return _F7
                End Get
            End Property

            ''' <summary>Bouton F8.</summary>
            Public ReadOnly Property F8 As C24_LedButton
                Get
                    Return _F8
                End Get
            End Property

            ''' <summary>Bouton F9.</summary>
            Public ReadOnly Property F9 As C24_LedButton
                Get
                    Return _F9
                End Get
            End Property

            ''' <summary>Bouton F10.</summary>
            Public ReadOnly Property F10 As C24_LedButton
                Get
                    Return _F10
                End Get
            End Property

            ' ── Display / Misc ────────────────────────────────────────────────
            ''' <summary>Bouton Rec Safe.</summary>
            Public ReadOnly Property Rec_Safe As C24_LedButton
                Get
                    Return _Rec_Safe
                End Get
            End Property

            ''' <summary>Bouton Master Rec.</summary>
            Public ReadOnly Property Master_Rec As C24_LedButton
                Get
                    Return _Master_Rec
                End Get
            End Property

            ''' <summary>Bouton Show Values.</summary>
            Public ReadOnly Property Show_Values As C24_LedButton
                Get
                    Return _Show_Values
                End Get
            End Property

            ''' <summary>Bouton Show Group.</summary>
            Public ReadOnly Property Show_Group As C24_LedButton
                Get
                    Return _Show_Group
                End Get
            End Property

            ''' <summary>Bouton Show Channel Names.</summary>
            Public ReadOnly Property Show_Channel_Names As C24_LedButton
                Get
                    Return _Show_Channel_Names
                End Get
            End Property

            ''' <summary>Bouton Show Info.</summary>
            Public ReadOnly Property Show_Info As C24_LedButton
                Get
                    Return _Show_Info
                End Get
            End Property

            ''' <summary>Bouton Vel Sens Encoders.</summary>
            Public ReadOnly Property Vel_Sens_Encoders As C24_LedButton
                Get
                    Return _Vel_Sens_Encoders
                End Get
            End Property

            ''' <summary>Bouton Auto Select.</summary>
            Public ReadOnly Property Auto_Select As C24_LedButton
                Get
                    Return _Auto_Select
                End Get
            End Property

            ''' <summary>Bouton Pre Post.</summary>
            Public ReadOnly Property Pre_Post As C24_LedButton
                Get
                    Return _Pre_Post
                End Get
            End Property

            ''' <summary>Bouton Send Mute.</summary>
            Public ReadOnly Property Send_Mute As C24_LedButton
                Get
                    Return _Send_Mute
                End Get
            End Property

            ' ── Channel Settings ──────────────────────────────────────────────
            ''' <summary>Bouton Copy Settings.</summary>
            Public ReadOnly Property Copy_Settings As C24_LedButton
                Get
                    Return _Copy_Settings
                End Get
            End Property

            ''' <summary>Bouton Specify Settings.</summary>
            Public ReadOnly Property Specify_Settings As C24_LedButton
                Get
                    Return _Specify_Settings
                End Get
            End Property

            ''' <summary>Bouton Edit Byp.</summary>
            Public ReadOnly Property Edit_Byp As C24_LedButton
                Get
                    Return _Edit_Byp
                End Get
            End Property

            ''' <summary>Bouton Paste Setting.</summary>
            Public ReadOnly Property Paste_Setting As C24_LedButton
                Get
                    Return _Paste_Setting
                End Get
            End Property

            ''' <summary>Bouton Ins Byp.</summary>
            Public ReadOnly Property Ins_Byp As C24_LedButton
                Get
                    Return _Ins_Byp
                End Get
            End Property

            ''' <summary>Bouton Apply To All Channels.</summary>
            Public ReadOnly Property Apply_To_All_Channels As C24_LedButton
                Get
                    Return _Apply_To_All_Channels
                End Get
            End Property

            ''' <summary>Bouton Apply To All Selected Channels.</summary>
            Public ReadOnly Property Apply_To_All_Selected_Channels As C24_LedButton
                Get
                    Return _Apply_To_All_Selected_Channels
                End Get
            End Property

            ''' <summary>Bouton Default.</summary>
            Public ReadOnly Property [Default] As C24_LedButton
                Get
                    Return _Default
                End Get
            End Property

            ' ── Automation ────────────────────────────────────────────────────
            ''' <summary>Bouton Auto Write.</summary>
            Public ReadOnly Property Auto_Write As C24_LedButton
                Get
                    Return _Auto_Write
                End Get
            End Property

            ''' <summary>Bouton Auto Fader.</summary>
            Public ReadOnly Property Auto_Fader As C24_LedButton
                Get
                    Return _Auto_Fader
                End Get
            End Property

            ''' <summary>Bouton Auto Touch.</summary>
            Public ReadOnly Property Auto_Touch As C24_LedButton
                Get
                    Return _Auto_Touch
                End Get
            End Property

            ''' <summary>Bouton Auto Pan.</summary>
            Public ReadOnly Property Auto_Pan As C24_LedButton
                Get
                    Return _Auto_Pan
                End Get
            End Property

            ''' <summary>Bouton Auto Latch.</summary>
            Public ReadOnly Property Auto_Latch As C24_LedButton
                Get
                    Return _Auto_Latch
                End Get
            End Property

            ''' <summary>Bouton Auto Mute.</summary>
            Public ReadOnly Property Auto_Mute As C24_LedButton
                Get
                    Return _Auto_Mute
                End Get
            End Property

            ''' <summary>Bouton Auto Trim.</summary>
            Public ReadOnly Property Auto_Trim As C24_LedButton
                Get
                    Return _Auto_Trim
                End Get
            End Property

            ''' <summary>Bouton Auto Send Level.</summary>
            Public ReadOnly Property Auto_Send_Level As C24_LedButton
                Get
                    Return _Auto_Send_Level
                End Get
            End Property

            ''' <summary>Bouton Auto Read.</summary>
            Public ReadOnly Property Auto_Read As C24_LedButton
                Get
                    Return _Auto_Read
                End Get
            End Property

            ''' <summary>Bouton Auto Send Mute.</summary>
            Public ReadOnly Property Auto_Send_Mute As C24_LedButton
                Get
                    Return _Auto_Send_Mute
                End Get
            End Property

            ''' <summary>Bouton Auto Off.</summary>
            Public ReadOnly Property Auto_Off As C24_LedButton
                Get
                    Return _Auto_Off
                End Get
            End Property

            ''' <summary>Bouton Auto Plug In.</summary>
            Public ReadOnly Property Auto_Plug_In As C24_LedButton
                Get
                    Return _Auto_Plug_In
                End Get
            End Property

            ''' <summary>Bouton Auto Suspend.</summary>
            Public ReadOnly Property Auto_Suspend As C24_LedButton
                Get
                    Return _Auto_Suspend
                End Get
            End Property

            ''' <summary>Bouton Write Auto To End.</summary>
            Public ReadOnly Property Write_Auto_To_End As C24_LedButton
                Get
                    Return _Write_Auto_To_End
                End Get
            End Property

            ''' <summary>Bouton Write Auto To Start.</summary>
            Public ReadOnly Property Write_Auto_To_Start As C24_LedButton
                Get
                    Return _Write_Auto_To_Start
                End Get
            End Property

            ''' <summary>Bouton Write Auto To All.</summary>
            Public ReadOnly Property Write_Auto_To_All As C24_LedButton
                Get
                    Return _Write_Auto_To_All
                End Get
            End Property

            ' ── Modifiers ─────────────────────────────────────────────────────
            ''' <summary>Bouton Shift.</summary>
            Public ReadOnly Property Shift As C24_Button
                Get
                    Return _Shift
                End Get
            End Property

            ''' <summary>Bouton Opt.</summary>
            Public ReadOnly Property Opt As C24_Button
                Get
                    Return _Opt
                End Get
            End Property

            ''' <summary>Bouton Ctl Clutch.</summary>
            Public ReadOnly Property Ctl_Clutch As C24_Button
                Get
                    Return _Ctl_Clutch
                End Get
            End Property

            ''' <summary>Bouton Ctrl.</summary>
            Public ReadOnly Property Ctrl As C24_Button
                Get
                    Return _Ctrl
                End Get
            End Property

            Public ReadOnly Property SoloClear As C24_LedButton
                Get
                    Return _SoloClear
                End Get
            End Property

            Public ReadOnly Property SoloSafe As C24_LedButton
                Get
                    Return _SoloSafe
                End Get
            End Property

            Public ReadOnly Property ChannelStrips As List(Of C24_ChannelStrip)
                Get
                    Return _ChannelStrips
                End Get
            End Property
            Public ReadOnly Property Solos As List(Of C24_LedButton)
                Get
                    Return _Solos
                End Get
            End Property
            Public ReadOnly Property Mutes As List(Of C24_LedButton)
                Get
                    Return _Mutes
                End Get
            End Property
            Public ReadOnly Property ScribbleStrips As List(Of C24_ScribbleStrip)
                Get
                    Return _ScribbleStrips
                End Get
            End Property
            Public ReadOnly Property Vpots As List(Of C24_Vpot)
                Get
                    Return _Vpots
                End Get
            End Property
            Public ReadOnly Property Selects As List(Of C24_LedButton)
                Get
                    Return _Selects
                End Get
            End Property
            Public ReadOnly Property Automodes As List(Of C24_Button)
                Get
                    Return _Automodes
                End Get
            End Property
            Public ReadOnly Property Faders As List(Of C24_Fader)
                Get
                    Return _Faders
                End Get
            End Property
            Public ReadOnly Property RecArms As List(Of C24_LedButton)
                Get
                    Return _RecArms
                End Get
            End Property


            ''' <summary>Intervalle de clignotement en ms (défaut : 500ms).</summary>
            ''' <summary>Intervalle de clignotement en ms (défaut : 500ms).</summary>
            Public Property BlinkInterval As Integer
                Get
                    Return _BlinkInterval
                End Get
                Set
                    If Value <> _BlinkInterval Then
                        _BlinkInterval = Value
                        _BlinkTimer?.Change(0, _BlinkInterval)   ' ✅ appliqué à chaud
                    End If
                End Set
            End Property



            ReadOnly Property ReceivedC24PacketQueue As Concurrent.ConcurrentQueue(Of Packet.C24_Packet)
                Get
                    Return _ReceivedC24PacketQueue
                End Get
            End Property

            ReadOnly Property PacketCapture As NativePcapAdapter
                Get
                    Return _packetCapture
                End Get
            End Property

            ReadOnly Property ComputerMacAddr As Byte()
                Get
                    Return _ComputerMacAddr
                End Get
            End Property

            Public ReadOnly Property NetworkInterface As Net.NetworkInformation.NetworkInterface
                Get
                    Return _NetworkInterface
                End Get
            End Property



            Public ReadOnly Property IsRunning As Boolean
                Get
                    Return Threading.Volatile.Read(_IsRunning)
                End Get
            End Property

            Public Property TimingQueueTimeout As Integer
                Get
                    Return _TimingQueueTimeout
                End Get
                Set
                    If Value <> _TimingQueueTimeout Then
                        _TimingQueueTimeout = Value
                    End If
                End Set
            End Property

            Public Property InstantQueueTimeout As Integer
                Get
                    Return _InstantQueueTimeout
                End Get
                Set
                    If Value <> _InstantQueueTimeout Then
                        _InstantQueueTimeout = Value
                    End If
                End Set
            End Property

            Sub New(NetworkInterface As Net.NetworkInformation.NetworkInterface)
                If NetworkInterface Is Nothing Then
                    InitializeComponents()
                Else
                    _NetworkInterface = NetworkInterface
                End If




            End Sub
            ''' <summary>True quand <see cref="InitializeComponents"/> a terminé.</summary>
            Public ReadOnly Property IsInitialized As Boolean
                Get
                    Return _initEvent.IsSet
                End Get
            End Property

            ''' <summary>
            ''' Bloque le thread appelant jusqu'à ce que l'initialisation des
            ''' composants C24 soit terminée, ou jusqu'à l'expiration du délai.
            ''' </summary>
            ''' <param name="timeoutMs">
            ''' Délai maximum en ms. -1 (défaut) = attente indéfinie.
            ''' </param>
            ''' <returns>True si l'initialisation est terminée dans le délai imparti.</returns>
            Public Function WaitForInitialization(Optional timeoutMs As Integer = -1) As Boolean
                Return _initEvent.Wait(timeoutMs)
            End Function


            Sub InitializeComponents()



                '*----------------------------------*
                '*      init all channelStrips      *
                '*----------------------------------*

                For i = 1 To 24
                    'Create and register ChannelStrips
                    Dim cs As New C24_ChannelStrip(i)
                    cs.RegisterTo(Me)

                    'add to lists
                    ChannelStrips.Add(cs)
                    Solos.Add(cs.Solo)
                    Mutes.Add(cs.Mute)
                    Vpots.Add(cs.Vpot)
                    Faders.Add(cs.Fader)
                    RecArms.Add(cs.RecArm)
                    Selects.Add(cs.Select)
                    Automodes.Add(cs.AutoMode)
                    ScribbleStrips.Add(cs.ScribbleStrip)

                Next

                '*----------------------------------*
                '*      init Solo Safe Clear        *
                '*----------------------------------*

                _SoloSafe = New C24_LedButton("SoloSafe", Btn_Region.Left_Strip, &H2E)
                Me.Add_C24_Component(_SoloSafe, "Solo")
                _SoloClear = New C24_LedButton("SoloClear", Btn_Region.Left_Strip, &H14)
                Me.Add_C24_Component(_SoloClear, "Solo")

                '*----------------------------------*
                '*      init Channel Settings       *
                '*----------------------------------*


                _Copy_Settings = New C24_LedButton("Copy_Settings", Btn_Region.Left_Strip, &H2B)
                Me.Add_C24_Component(_Copy_Settings, "ChannelSettings")
                _Specify_Settings = New C24_LedButton("Specify_Settings", Btn_Region.Left_Strip, &H2C)
                Me.Add_C24_Component(_Specify_Settings, "ChannelSettings")
                _Edit_Byp = New C24_LedButton("Edit_Byp", Btn_Region.Left_Strip, &H5)
                Me.Add_C24_Component(_Edit_Byp, "ChannelSettings")
                _Paste_Setting = New C24_LedButton("Paste_Setting", Btn_Region.Left_Strip, &H2D)
                Me.Add_C24_Component(_Paste_Setting, "ChannelSettings")
                _Ins_Byp = New C24_LedButton("Ins_Byp", Btn_Region.Left_Strip, &H3)
                Me.Add_C24_Component(_Ins_Byp, "ChannelSettings")
                _Apply_To_All_Channels = New C24_LedButton("Apply_To_All_Channels", Btn_Region.Left_Strip, &H29)
                Me.Add_C24_Component(_Apply_To_All_Channels, "ChannelSettings")
                _Apply_To_All_Selected_Channels = New C24_LedButton("Apply_To_All_Selected_Channels", Btn_Region.Left_Strip, &H2A)
                Me.Add_C24_Component(_Apply_To_All_Selected_Channels, "ChannelSettings")
                _Default = New C24_LedButton("Default", Btn_Region.Left_Strip, &H7)
                Me.Add_C24_Component(_Default, "ChannelSettings")

                '*----------------------------------*
                '*      init Automation Buttons     *
                '*----------------------------------*

                _Auto_Write = New C24_LedButton("Auto_Write", Btn_Region.Left_Strip, &H17)
                Me.Add_C24_Component(_Auto_Write, "Automation")
                _Auto_Fader = New C24_LedButton("Auto_Fader", Btn_Region.Left_Strip, &H18)
                Me.Add_C24_Component(_Auto_Fader, "Automation")
                _Auto_Touch = New C24_LedButton("Auto_Touch", Btn_Region.Left_Strip, &H19)
                Me.Add_C24_Component(_Auto_Touch, "Automation")
                _Auto_Pan = New C24_LedButton("Auto_Pan", Btn_Region.Left_Strip, &H1A)
                Me.Add_C24_Component(_Auto_Pan, "Automation")
                _Auto_Latch = New C24_LedButton("Auto_Latch", Btn_Region.Left_Strip, &H1B)
                Me.Add_C24_Component(_Auto_Latch, "Automation")
                _Auto_Mute = New C24_LedButton("Auto_Mute", Btn_Region.Left_Strip, &H1C)
                Me.Add_C24_Component(_Auto_Mute, "Automation")
                _Auto_Trim = New C24_LedButton("Auto_Trim", Btn_Region.Left_Strip, &H1D)
                Me.Add_C24_Component(_Auto_Trim, "Automation")
                _Auto_Send_Level = New C24_LedButton("Auto_Send_Level", Btn_Region.Left_Strip, &H1E)
                Me.Add_C24_Component(_Auto_Send_Level, "Automation")
                _Auto_Read = New C24_LedButton("Auto_Read", Btn_Region.Left_Strip, &H1F)
                Me.Add_C24_Component(_Auto_Read, "Automation")
                _Auto_Send_Mute = New C24_LedButton("Auto_Send_Mute", Btn_Region.Left_Strip, &H20)
                Me.Add_C24_Component(_Auto_Send_Mute, "Automation")
                _Auto_Off = New C24_LedButton("Auto_Off", Btn_Region.Left_Strip, &H21)
                Me.Add_C24_Component(_Auto_Off, "Automation")
                _Auto_Plug_In = New C24_LedButton("Auto_Plug_In", Btn_Region.Left_Strip, &H22)
                Me.Add_C24_Component(_Auto_Plug_In, "Automation")
                _Auto_Suspend = New C24_LedButton("Auto_Suspend", Btn_Region.Left_Strip, &H15)
                Me.Add_C24_Component(_Auto_Suspend, "Automation")
                _Write_Auto_To_End = New C24_LedButton("Write_Auto_To_End", Btn_Region.Left_Strip, &H31)
                Me.Add_C24_Component(_Write_Auto_To_End, "Automation")
                _Write_Auto_To_Start = New C24_LedButton("Write_Auto_To_Start", Btn_Region.Left_Strip, &H2F)
                Me.Add_C24_Component(_Write_Auto_To_Start, "Automation")
                _Write_Auto_To_All = New C24_LedButton("Write_Auto_To_All", Btn_Region.Left_Strip, &H30)
                Me.Add_C24_Component(_Write_Auto_To_All, "Automation")

                '*----------------------------------*
                '*      init Modifiers              *
                '*----------------------------------*

                _Shift = New C24_Button("Shift", Btn_Region.Left_Strip, &H23)
                Me.Add_C24_Component(_Shift, "Modifiers")
                _Opt = New C24_Button("Opt", Btn_Region.Left_Strip, &H24)
                Me.Add_C24_Component(_Opt, "Modifiers")
                _Ctl_Clutch = New C24_Button("Ctl_Clutch", Btn_Region.Left_Strip, &H25)
                Me.Add_C24_Component(_Ctl_Clutch, "Modifiers")
                _Ctrl = New C24_Button("Ctrl", Btn_Region.Left_Strip, &H26)
                Me.Add_C24_Component(_Ctrl, "Modifiers")

                '*----------------------------------*
                '*      init Monitor Section        *
                '*----------------------------------*


                _Ms_Input = New C24_LedButton("Ms_Input", Btn_Region.Left_Strip, &HA)
                Me.Add_C24_Component(_Ms_Input, "MonitorSection")
                _Ms_Pre_Fader = New C24_LedButton("Ms_Pre_Fader", Btn_Region.Left_Strip, &HB)
                Me.Add_C24_Component(_Ms_Pre_Fader, "MonitorSection")
                _Ms_Post_Fader = New C24_LedButton("Ms_Post_Fader", Btn_Region.Left_Strip, &HC)
                Me.Add_C24_Component(_Ms_Post_Fader, "MonitorSection")
                _Ms_Channel = New C24_LedButton("Ms_Channel", Btn_Region.Left_Strip, &H9)
                Me.Add_C24_Component(_Ms_Channel, "MonitorSection")
                _Ms_Clear_Peaks = New C24_LedButton("Ms_Clear_Peaks", Btn_Region.Left_Strip, &HE)
                Me.Add_C24_Component(_Ms_Clear_Peaks, "MonitorSection")

                '*----------------------------------*
                '*      init Function Keys          *
                '*----------------------------------*

                _F1 = New C24_LedButton("F1", Btn_Region.Left_Strip, &H0)
                Me.Add_C24_Component(_F1, "FunctionKeys")
                _F2 = New C24_LedButton("F2", Btn_Region.Left_Strip, &H2)
                Me.Add_C24_Component(_F2, "FunctionKeys")
                _F3 = New C24_LedButton("F3", Btn_Region.Left_Strip, &H4)
                Me.Add_C24_Component(_F3, "FunctionKeys")
                _F4 = New C24_LedButton("F4", Btn_Region.Left_Strip, &H6)
                Me.Add_C24_Component(_F4, "FunctionKeys")
                _F5 = New C24_LedButton("F5", Btn_Region.Left_Strip, &H8)
                Me.Add_C24_Component(_F5, "FunctionKeys")
                _F6 = New C24_LedButton("F6", Btn_Region.Left_Strip, &H32)
                Me.Add_C24_Component(_F6, "FunctionKeys")
                _F7 = New C24_LedButton("F7", Btn_Region.Left_Strip, &H33)
                Me.Add_C24_Component(_F7, "FunctionKeys")
                _F8 = New C24_LedButton("F8", Btn_Region.Left_Strip, &H34)
                Me.Add_C24_Component(_F8, "FunctionKeys")
                _F9 = New C24_LedButton("F9", Btn_Region.Left_Strip, &H35)
                Me.Add_C24_Component(_F9, "FunctionKeys")
                _F10 = New C24_LedButton("F10", Btn_Region.Left_Strip, &H36)
                Me.Add_C24_Component(_F10, "FunctionKeys")

                '*----------------------------------*
                '*      init Display / Misc         *
                '*----------------------------------*

                _Rec_Safe = New C24_LedButton("Rec_Safe", Btn_Region.Left_Strip, &HF)
                Me.Add_C24_Component(_Rec_Safe, "DisplayMisc")
                _Master_Rec = New C24_LedButton("Master_Rec", Btn_Region.Left_Strip, &H1)
                Me.Add_C24_Component(_Master_Rec, "DisplayMisc")
                _Show_Values = New C24_LedButton("Show_Values", Btn_Region.Left_Strip, &H10)
                Me.Add_C24_Component(_Show_Values, "DisplayMisc")
                _Show_Group = New C24_LedButton("Show_Group", Btn_Region.Left_Strip, &H11)
                Me.Add_C24_Component(_Show_Group, "DisplayMisc")
                _Show_Channel_Names = New C24_LedButton("Show_Channel_Names", Btn_Region.Left_Strip, &H12)
                Me.Add_C24_Component(_Show_Channel_Names, "DisplayMisc")
                _Show_Info = New C24_LedButton("Show_Info", Btn_Region.Left_Strip, &H13)
                Me.Add_C24_Component(_Show_Info, "DisplayMisc")
                _Vel_Sens_Encoders = New C24_LedButton("Vel_Sens_Encoders", Btn_Region.Left_Strip, &H16)
                Me.Add_C24_Component(_Vel_Sens_Encoders, "DisplayMisc")
                _Auto_Select = New C24_LedButton("Auto_Select", Btn_Region.Left_Strip, &H27)
                Me.Add_C24_Component(_Auto_Select, "DisplayMisc")
                _Pre_Post = New C24_LedButton("Pre_Post", Btn_Region.Left_Strip, &H28)
                Me.Add_C24_Component(_Pre_Post, "DisplayMisc")
                _Send_Mute = New C24_LedButton("Send_Mute", Btn_Region.Left_Strip, &HD)
                Me.Add_C24_Component(_Send_Mute, "DisplayMisc")


                '*----------------------------------*
                '*   init INSERTS                   *
                '*----------------------------------*


                _Master_Bypass = New C24_LedButton("Master_Bypass", Btn_Region.Middle_Strip, &H3)
                Me.Add_C24_Component(_Master_Bypass, "Inserts")
                _Plugin_Safe = New C24_LedButton("Plugin_Safe", Btn_Region.Middle_Strip, &H9)
                Me.Add_C24_Component(_Plugin_Safe, "Inserts")
                _Compare = New C24_LedButton("Compare", Btn_Region.Middle_Strip, &H2)
                Me.Add_C24_Component(_Compare, "Inserts")
                _Inserts_Param = New C24_LedButton("Inserts_Param", Btn_Region.Middle_Strip, &H4)
                Me.Add_C24_Component(_Inserts_Param, "Inserts")

                '*----------------------------------*
                '*   init MONITOR                   *
                '*----------------------------------*

                _Monitor = New C24_LedButton("Monitor", Btn_Region.Middle_Strip, &H0)
                Me.Add_C24_Component(_Monitor, "Monitor")
                _Remote_Mic_Pre = New C24_LedButton("Remote_Mic_Pre", Btn_Region.Middle_Strip, &H1)
                Me.Add_C24_Component(_Remote_Mic_Pre, "Monitor")

                '*----------------------------------*
                '*   init ASSIGNMENT              *
                '*----------------------------------*

                _Assign = New C24_LedButton("Assign", Btn_Region.Middle_Strip, &H7)
                Me.Add_C24_Component(_Assign, "Assignment")
                _Input = New C24_LedButton("Input", Btn_Region.Middle_Strip, &H5)
                Me.Add_C24_Component(_Input, "Assignment")
                _Esc_Cancel = New C24_LedButton("Esc_Cancel", Btn_Region.Middle_Strip, &H8)
                Me.Add_C24_Component(_Esc_Cancel, "Assignment")
                _Output = New C24_LedButton("Output", Btn_Region.Middle_Strip, &H6)
                Me.Add_C24_Component(_Output, "Assignment")

                '*----------------------------------*
                '*      init SENDS                  *
                '*----------------------------------*



                _Flip = New C24_LedButton("Flip", Btn_Region.Middle_Strip, &HA)
                Me.Add_C24_Component(_Flip, "Sends")

                _Send_A_F = New C24_LedButton("Send_A_F", Btn_Region.Middle_Strip, &HB)
                Me.Add_C24_Component(_Send_A_F, "Sends")
                _Send_B_G = New C24_LedButton("Send_B_G", Btn_Region.Middle_Strip, &HC)
                Me.Add_C24_Component(_Send_B_G, "Sends")
                _Send_C_H = New C24_LedButton("Send_C_H", Btn_Region.Middle_Strip, &HD)
                Me.Add_C24_Component(_Send_C_H, "Sends")
                _Send_D_I = New C24_LedButton("Send_D_I", Btn_Region.Middle_Strip, &HE)
                Me.Add_C24_Component(_Send_D_I, "Sends")
                _Send_E_J = New C24_LedButton("Send_E_J", Btn_Region.Middle_Strip, &HF)
                Me.Add_C24_Component(_Send_E_J, "Sends")


                '*----------------------------------*
                '*      init PANS                   *
                '*----------------------------------*


                _Pan_L_C_R = New C24_LedButton("Pan_L_C_R", Btn_Region.Middle_Strip, &H10)
                Me.Add_C24_Component(_Pan_L_C_R, "Pans")
                _Pan_Rear = New C24_LedButton("Pan_Rear", Btn_Region.Middle_Strip, &H11)
                Me.Add_C24_Component(_Pan_Rear, "Pans")
                _Pan_F_R = New C24_LedButton("Pan_F_R", Btn_Region.Middle_Strip, &H12)
                Me.Add_C24_Component(_Pan_F_R, "Pans")
                _Pan_Cent = New C24_LedButton("Pan_Cent", Btn_Region.Middle_Strip, &H13)
                Me.Add_C24_Component(_Pan_Cent, "Pans")
                _Pan_Lfe = New C24_LedButton("Pan_Lfe", Btn_Region.Middle_Strip, &H16)
                Me.Add_C24_Component(_Pan_Lfe, "Pans")
                _Pan_Left_Right = New C24_LedButton("Pan_Left_Right", Btn_Region.Middle_Strip, &H17)
                Me.Add_C24_Component(_Pan_Left_Right, "Pans")

                '*----------------------------------*
                '*   init Channel Bar Scroll        *
                '*----------------------------------*


                _Cbs_Left = New C24_LedButton("Cbs_Left", Btn_Region.Middle_Strip, &H14)
                Me.Add_C24_Component(_Cbs_Left, "ChannelBarScroll")
                _Cbs_Right = New C24_LedButton("Cbs_Right", Btn_Region.Middle_Strip, &H15)
                Me.Add_C24_Component(_Cbs_Right, "ChannelBarScroll")

                '*----------------------------------*
                '*      init Groups                 *
                '*----------------------------------*


                _Create = New C24_LedButton("Create", Btn_Region.Edit, &H17)
                Me.Add_C24_Component(_Create, "Groups")
                _Enable = New C24_LedButton("Enable", Btn_Region.Edit, &H18)
                Me.Add_C24_Component(_Enable, "Groups")
                _Edit = New C24_LedButton("Edit", Btn_Region.Edit, &H19)
                Me.Add_C24_Component(_Edit, "Groups")
                _Delete = New C24_LedButton("Delete", Btn_Region.Edit, &H1A)
                Me.Add_C24_Component(_Delete, "Groups")
                _Suspend = New C24_LedButton("Suspend", Btn_Region.Edit, &H1B)
                Me.Add_C24_Component(_Suspend, "Groups")

                '*----------------------------------*
                '*      init Edit Fonction                  *
                '*----------------------------------*
                ' ⚠️ Valeurs &Hxx à vérifier sur dump hardware

                _Cut = New C24_Button("Cut", Btn_Region.Edit, &H4)
                Me.Add_C24_Component(_Cut, "EditFonction")
                _Copy = New C24_Button("Copy", Btn_Region.Edit, &H5)
                Me.Add_C24_Component(_Copy, "EditFonction")
                _Paste = New C24_Button("Paste", Btn_Region.Edit, &H6)
                Me.Add_C24_Component(_Paste, "EditFonction")
                _Edit_Delete = New C24_Button("Edit_Delete", Btn_Region.Edit, &H7)
                Me.Add_C24_Component(_Edit_Delete, "EditFonction")
                _Separate = New C24_Button("Separate", Btn_Region.Edit, &H8)
                Me.Add_C24_Component(_Separate, "EditFonction")
                _Capture = New C24_Button("Capture", Btn_Region.Edit, &H9)
                Me.Add_C24_Component(_Capture, "EditFonction")
                _Duplicate = New C24_Button("Duplicate", Btn_Region.Edit, &H1C)
                Me.Add_C24_Component(_Duplicate, "EditFonction")
                _Repeat = New C24_Button("Repeat", Btn_Region.Edit, &H1D)
                Me.Add_C24_Component(_Repeat, "EditFonction")

                '*----------------------------------*
                '*      init ZoomPreset             *
                '*----------------------------------*


                _Preset_1 = New C24_LedButton("Preset_1", Btn_Region.Navigation, &H8)
                Me.Add_C24_Component(_Preset_1, "ZoomPreset")
                _Preset_2 = New C24_LedButton("Preset_2", Btn_Region.Navigation, &H9)
                Me.Add_C24_Component(_Preset_2, "ZoomPreset")
                _Preset_3 = New C24_LedButton("Preset_3", Btn_Region.Navigation, &HA)
                Me.Add_C24_Component(_Preset_3, "ZoomPreset")
                _Preset_4 = New C24_LedButton("Preset_4", Btn_Region.Navigation, &HB)
                Me.Add_C24_Component(_Preset_4, "ZoomPreset")
                _Preset_5 = New C24_LedButton("Preset_5", Btn_Region.Navigation, &H14)
                Me.Add_C24_Component(_Preset_5, "ZoomPreset")

                '*----------------------------------*
                '*      init EditMode               *
                '*----------------------------------*


                _Shuffle = New C24_LedButton("Shuffle", Btn_Region.Edit, &H0)
                Me.Add_C24_Component(_Shuffle, "EditMode")
                _Slip = New C24_LedButton("Slip", Btn_Region.Edit, &H1)
                Me.Add_C24_Component(_Slip, "EditMode")
                _Spot = New C24_LedButton("Spot", Btn_Region.Edit, &H2)
                Me.Add_C24_Component(_Spot, "EditMode")
                _Grid = New C24_LedButton("Grid", Btn_Region.Edit, &H3)
                Me.Add_C24_Component(_Grid, "EditMode")


                '*----------------------------------*
                '*      init Window                 *
                '*----------------------------------*


                _W_Mix = New C24_LedButton("W_Mix", Btn_Region.Navigation, &H0)
                Me.Add_C24_Component(_W_Mix, "Window")
                _W_Status = New C24_LedButton("W_Status", Btn_Region.Navigation, &H2)
                Me.Add_C24_Component(_W_Status, "Window")
                _W_Plugin = New C24_LedButton("W_Plugin", Btn_Region.Navigation, &H6)
                Me.Add_C24_Component(_W_Plugin, "Window")
                _W_Pan = New C24_LedButton("W_Pan", Btn_Region.Navigation, &H4)
                Me.Add_C24_Component(_W_Pan, "Window")

                _W_Edit = New C24_LedButton("W_Edit", Btn_Region.Navigation, &H1)
                Me.Add_C24_Component(_W_Edit, "Window")
                _W_Trans = New C24_LedButton("W_Trans", Btn_Region.Navigation, &H3)
                Me.Add_C24_Component(_W_Trans, "Window")
                _W_Mem_Loc = New C24_LedButton("W_Mem_Loc", Btn_Region.Navigation, &H7)
                Me.Add_C24_Component(_W_Mem_Loc, "Window")
                _W_Alt = New C24_LedButton("W_Alt", Btn_Region.Navigation, &H5)
                Me.Add_C24_Component(_W_Alt, "Window")

                '*----------------------------------*
                '*      init EditTools              *
                '*----------------------------------*


                _Tools_Trim = New C24_LedButton("Tools_Trim", Btn_Region.Edit, &HD)
                Me.Add_C24_Component(_Tools_Trim, "EditTools")
                _Tools_Select = New C24_LedButton("Tools_Select", Btn_Region.Edit, &HE)
                Me.Add_C24_Component(_Tools_Select, "EditTools")
                _Tools_Grab = New C24_LedButton("Tools_Grab", Btn_Region.Edit, &HF)
                Me.Add_C24_Component(_Tools_Grab, "EditTools")
                _Tools_Pencil = New C24_LedButton("Tools_Pencil", Btn_Region.Edit, &H10)
                Me.Add_C24_Component(_Tools_Pencil, "EditTools")

                _Tools_Midi = New C24_LedButton("Tools_Midi", Btn_Region.Edit, &H11)
                Me.Add_C24_Component(_Tools_Midi, "EditTools")

                _Tools_Smart = New C24_LedButton("Tools_Smart", Btn_Region.Edit, &H12)
                Me.Add_C24_Component(_Tools_Smart, "EditTools")
                _Tools_Link_Edit = New C24_LedButton("Tools_Link_Edit", Btn_Region.Edit, &H1E)
                Me.Add_C24_Component(_Tools_Link_Edit, "EditTools")
                _Tools_Auto_To_Cur = New C24_LedButton("Tools_Auto_To_Cur", Btn_Region.Edit, &H1F)
                Me.Add_C24_Component(_Tools_Auto_To_Cur, "EditTools")
                _Tools_Auto_To_All = New C24_LedButton("Tools_Auto_To_All", Btn_Region.Edit, &H20)
                Me.Add_C24_Component(_Tools_Auto_To_All, "EditTools")

                '*----------------------------------*
                '*      init Navigation             *
                '*----------------------------------*


                _Nav = New C24_LedButton("Nav", Btn_Region.Navigation, &HC)
                Me.Add_C24_Component(_Nav, "Navigation")
                _Zoom = New C24_LedButton("Zoom", Btn_Region.Navigation, &HD)
                Me.Add_C24_Component(_Zoom, "Navigation")
                _Sel_Adj = New C24_LedButton("Sel_Adj", Btn_Region.Navigation, &HE)
                Me.Add_C24_Component(_Sel_Adj, "Navigation")

                _Up = New C24_Button("Up", Btn_Region.Navigation, &HF)
                Me.Add_C24_Component(_Up, "Navigation")
                _Previous = New C24_Button("Previous", Btn_Region.Navigation, &H10)
                Me.Add_C24_Component(_Previous, "Navigation")
                _Next = New C24_Button("Next", Btn_Region.Navigation, &H11)
                Me.Add_C24_Component(_Next, "Navigation")
                _Down = New C24_Button("Down", Btn_Region.Navigation, &H12)
                Me.Add_C24_Component(_Down, "Navigation")



                '*----------------------------------*
                '*      init Transport              *
                '*----------------------------------*


                _Loop_Play = New C24_LedButton("Loop_Play", Btn_Region.Transport, &H9)
                Me.Add_C24_Component(_Loop_Play, "Transport")
                _Loop_Rec = New C24_LedButton("Loop_Rec", Btn_Region.Transport, &HA)
                Me.Add_C24_Component(_Loop_Rec, "Transport")

                _Audition = New C24_LedButton("Audition", Btn_Region.Transport, &H0)
                Me.Add_C24_Component(_Audition, "Transport")
                _Pre_Roll = New C24_LedButton("Pre_Roll", Btn_Region.Transport, &H1)
                Me.Add_C24_Component(_Pre_Roll, "Transport")
                _In = New C24_LedButton("In", Btn_Region.Transport, &H2)
                Me.Add_C24_Component(_In, "Transport")
                _Out = New C24_LedButton("Out", Btn_Region.Transport, &H3)
                Me.Add_C24_Component(_Out, "Transport")
                _Post_Roll = New C24_LedButton("Post_Roll", Btn_Region.Transport, &H4)
                Me.Add_C24_Component(_Post_Roll, "Transport")

                _Online = New C24_LedButton("Online", Btn_Region.Transport, &H7)
                Me.Add_C24_Component(_Online, "Transport")
                _Goto_Start = New C24_LedButton("Goto_Start", Btn_Region.Transport, &H5)
                Me.Add_C24_Component(_Goto_Start, "Transport")
                _Goto_End = New C24_LedButton("Goto_End", Btn_Region.Transport, &H6)
                Me.Add_C24_Component(_Goto_End, "Transport")
                _Ext_Trans = New C24_LedButton("Ext_Trans", Btn_Region.Transport, &H8)
                Me.Add_C24_Component(_Ext_Trans, "Transport")
                _Quick_Punch = New C24_LedButton("Quick_Punch", Btn_Region.Transport, &HB)
                Me.Add_C24_Component(_Quick_Punch, "Transport")



                _Talk_Back = New C24_LedButton("Talk_Back", Btn_Region.Transport, &HC)
                Me.Add_C24_Component(_Talk_Back, "Transport")
                _Rew = New C24_LedButton("Rew", Btn_Region.Transport, &HD)
                Me.Add_C24_Component(_Rew, "Transport")
                _Fwd = New C24_LedButton("Fwd", Btn_Region.Transport, &HE)
                Me.Add_C24_Component(_Fwd, "Transport")
                _Stop = New C24_LedButton("Stop", Btn_Region.Transport, &HF)
                Me.Add_C24_Component(_Stop, "Transport")
                _Play = New C24_LedButton("Play", Btn_Region.Transport, &H10)
                Me.Add_C24_Component(_Play, "Transport")
                _Rec = New C24_LedButton("Rec", Btn_Region.Transport, &H11)
                Me.Add_C24_Component(_Rec, "Transport")

                '*----------------------------------*
                '*      init Save                   *
                '*----------------------------------*


                _Save = New C24_LedButton("Save", Btn_Region.Edit, &H15)
                Me.Add_C24_Component(_Save, "Save")
                _Undo = New C24_LedButton("Undo", Btn_Region.Edit, &H14)
                Me.Add_C24_Component(_Undo, "Save")

                '*----------------------------------*
                '*      init Numpad                 *
                '*----------------------------------*



                _Num_Clr = New C24_Button("Num_Clr", Btn_Region.Num_Keyboard, &HA)
                Me.Add_C24_Component(_Num_Clr, "Numpad")
                _Num_Egal = New C24_Button("Num_Egal", Btn_Region.Num_Keyboard, &HB)
                Me.Add_C24_Component(_Num_Egal, "Numpad")
                _Num_Div = New C24_Button("Num_Div", Btn_Region.Num_Keyboard, &HC)
                Me.Add_C24_Component(_Num_Div, "Numpad")
                _Num_Mult = New C24_Button("Num_Mult", Btn_Region.Num_Keyboard, &HD)
                Me.Add_C24_Component(_Num_Mult, "Numpad")

                _Num_7 = New C24_Button("Num_7", Btn_Region.Num_Keyboard, &H7)
                Me.Add_C24_Component(_Num_7, "Numpad")
                _Num_8 = New C24_Button("Num_8", Btn_Region.Num_Keyboard, &H8)
                Me.Add_C24_Component(_Num_8, "Numpad")
                _Num_9 = New C24_Button("Num_9", Btn_Region.Num_Keyboard, &H9)
                Me.Add_C24_Component(_Num_9, "Numpad")
                _Num_Sous = New C24_Button("Num_Sous", Btn_Region.Num_Keyboard, &HE)
                Me.Add_C24_Component(_Num_Sous, "Numpad")

                _Num_4 = New C24_Button("Num_4", Btn_Region.Num_Keyboard, &H4)
                Me.Add_C24_Component(_Num_4, "Numpad")
                _Num_5 = New C24_Button("Num_5", Btn_Region.Num_Keyboard, &H5)
                Me.Add_C24_Component(_Num_5, "Numpad")
                _Num_6 = New C24_Button("Num_6", Btn_Region.Num_Keyboard, &H6)
                Me.Add_C24_Component(_Num_6, "Numpad")
                _Num_Add = New C24_Button("Num_Add", Btn_Region.Num_Keyboard, &HF)
                Me.Add_C24_Component(_Num_Add, "Numpad")


                _Num_1 = New C24_Button("Num_1", Btn_Region.Num_Keyboard, &H1)
                Me.Add_C24_Component(_Num_1, "Numpad")
                _Num_2 = New C24_Button("Num_2", Btn_Region.Num_Keyboard, &H2)
                Me.Add_C24_Component(_Num_2, "Numpad")
                _Num_3 = New C24_Button("Num_3", Btn_Region.Num_Keyboard, &H3)
                Me.Add_C24_Component(_Num_3, "Numpad")

                _Num_0 = New C24_Button("Num_0", Btn_Region.Num_Keyboard, &H0)
                Me.Add_C24_Component(_Num_0, "Numpad")
                _Num_Point = New C24_Button("Num_Point", Btn_Region.Num_Keyboard, &H10)
                Me.Add_C24_Component(_Num_Point, "Numpad")
                _Num_Enter = New C24_Button("Num_Enter", Btn_Region.Num_Keyboard, &H11)
                Me.Add_C24_Component(_Num_Enter, "Numpad")



                '*----------------------------------*
                '*      init Utility                *
                '*----------------------------------*


                _Master_Faders = New C24_LedButton("Master_Faders", Btn_Region.Edit, &H13)
                Me.Add_C24_Component(_Master_Faders, "Utility")
                _Esc_Cancel_2 = New C24_LedButton("Esc_Cancel_2", Btn_Region.Edit, &H21)
                Me.Add_C24_Component(_Esc_Cancel_2, "Utility")


                _Scrub = New C24_LedButton("Scrub", Btn_Region.Transport, &H12)
                Me.Add_C24_Component(_Scrub, "Utility")
                _Shuttle = New C24_LedButton("Shuttle", Btn_Region.Transport, &H13)
                Me.Add_C24_Component(_Shuttle, "Utility")

                '*----------------------------------*
                '*      init Counter                *
                '*----------------------------------*


                _Counter_Mode = New C24_LedButton("Counter_Mode", Btn_Region.Navigation, &H13)
                Me.Add_C24_Component(_Counter_Mode, "Counter")

                '*----------------------------------*
                '*      init Clock                  *
                '*----------------------------------*

                _Clock = New C24_Clock("Clock")
                Me.Add_C24_Component(_Clock, "Clock")
                ' ── Force l'envoi de ClockLed_Cmd + Clock_Cmd après enregistrement ──
                _Clock.ClockMode = Commands.ClockModes.HourMinuteSecondMS

                '*----------------------------------*
                '*      init JogWheel               *
                '*----------------------------------*

                _JogWheel = New C24_JogWheel("JogWheel")
                Me.Add_C24_Component(_JogWheel, "JogWheel")


                '*----------------------------------*
                '*      init Bank                   *
                '*----------------------------------*


                _Bank_Left = New C24_Button("Bank_Left", Btn_Region.Edit, &HA)
                Me.Add_C24_Component(_Bank_Left, "Bank")
                _Nudge = New C24_LedButton("Nudge", Btn_Region.Edit, &HB)
                Me.Add_C24_Component(_Nudge, "Bank")
                _Bank_Right = New C24_Button("Bank_Right", Btn_Region.Edit, &HC)
                Me.Add_C24_Component(_Bank_Right, "Bank")

                '*----------------------------------*
                '*      init ScreenLine             *
                '*----------------------------------*

                _ScreenLine = New C24_ScribbleScreen(_ScribbleStrips)
                Dim Sb1_ScribbleStrip As New C24_ScribbleStrip("Sb1_ScribbleStrip", 30)
                Me.Add_C24_Component(Sb1_ScribbleStrip, "ChannelBarScroll")
                Dim Sb2_ScribbleStrip As New C24_ScribbleStrip("Sb2_ScribbleStrip", 31)
                Me.Add_C24_Component(Sb2_ScribbleStrip, "ChannelBarScroll")
                _ScreenChannelBarScroll = New C24_ScribbleScreen({Sb1_ScribbleStrip, Sb2_ScribbleStrip}.ToList)

                '*----------------------------------*
                '*      Group Buttons               *
                '*----------------------------------*

                _Esc_Group = New C24_GroupLedButton({Esc_Cancel, Esc_Cancel_2})



                '*----------------------------------*
                '*      Masters VueMeters           *
                '*----------------------------------*

                _VueMeterMaster1 = New C24_VueMeter("VueMeterMaster1", 25)
                Me.Add_AutoRefresh_C24_Component(VueMeterMaster1, "VueMeterMasters")
                _VueMeterMaster2 = New C24_VueMeter("VueMeterMaster2", 26)
                Me.Add_AutoRefresh_C24_Component(VueMeterMaster2, "VueMeterMasters")
                _VueMeterMaster3 = New C24_VueMeter("VueMeterMaster3", 27)
                Me.Add_AutoRefresh_C24_Component(VueMeterMaster3, "VueMeterMasters")
                'init other VueMaster2/3 on demand


                ' ── Signal : initialisation terminée ──────────────────────────
                _initEvent.Set()

            End Sub



            Private Sub Test_reaper_utility()

                ' Utilisation :




                'Dim result = Reaper.utility.CallReaperBridge("PING")
                Console.WriteLine(Reaper.utility.CallReaperBridge("GET_TRACK_COUNT"))
                Console.WriteLine(Reaper.utility.CallReaperBridge("GET_TRACK_INFO|0|D_VOL"))
                Console.WriteLine(Reaper.utility.CallReaperBridge("GET_TRACK_ALL_INFO|0"))
                Console.WriteLine(Reaper.utility.CallReaperBridge("GET_TRACK_INFO|0|I_RECINPUT"))

                Dim _ReaperSessionConfig As New Reaper.ReaperSessionConfig
                _ReaperSessionConfig.RefreshPlugins()

                Dim trk As New Reaper.ReaperTrack(0)
                'For Each env In trk.Track_Envs.Envelopes
                '    Console.WriteLine(env?.EnvIndex & ":" & env?.Name & ":" & env?.SendId)
                '    Console.WriteLine(Reaper.utility.GetEnvelopeStateChunk(0, env?.EnvIndex))
                'Next

                Dim _ReaperTrackOutputsManager As New Reaper.PageManagers.ReaperTrackOutputsManager({trk}.ToList)
                'For j = 0 To 2
                '    Console.WriteLine(_ReaperTrackOutputsManager.AllOutputs(j).SrcChanFlag & "|" & _ReaperTrackOutputsManager.AllOutputs(j).DstChan)
                '    _ReaperTrackOutputsManager.AddOutput(0, j)
                'Next
                _ReaperTrackOutputsManager.Set_Next_Page()


                Console.WriteLine("send info :")
                Console.WriteLine(Reaper.utility.CallReaperBridge(
                $"GET_SEND_ALL_INFO|0|1|0"))
                Console.WriteLine(Reaper.utility.CallReaperBridge(
                $"GET_SEND_ALL_INFO|0|1|1"))
                Console.WriteLine(Reaper.utility.CallReaperBridge(
                $"GET_SEND_ALL_INFO|0|1|0"))

                Console.WriteLine("All inputs :")
                ' ── Toutes les entrées en 1 seul appel pipe ──────────────────────────────────
                Dim inputs = Reaper.utility.GetAllAudioInputs()
                Dim outputs = Reaper.utility.GetAllAudioOutputs
                ' ── Nombre d'entrées seulement ────────────────────────────────────────────────
                Dim count_in = Reaper.utility.GetNumAudioInputs()
                Dim count_out = Reaper.utility.GetNumAudioOutputs()

                For Each inp In inputs
                    Console.WriteLine($"[{inp.Index}] {inp.Name}")
                Next

                For Each outp In outputs
                    Console.WriteLine($"[{outp.Index}] {outp.Name}")
                Next

                'GET_PROJECT_STATE_COUNT
                Dim i As Double = 0
                Dim add As Boolean = True
                Dim state As Boolean = True
                Do While True
                    Console.WriteLine(Reaper.utility.CallReaperBridge("GET_TRACK_INFO|0|D_VOL"))
                    Console.WriteLine(Reaper.utility.SetTrackInfoD(0, "D_VOL", i))
                    'Console.WriteLine(Reaper.utility.SetFxParamNormalizedUI(0, 1, 2, i))
                    'Console.WriteLine(Reaper.utility.InsertFxEnvelopePointNow(0, 1, 2, i))
                    'Console.WriteLine(Reaper.utility.SetFxParamValue(0, 1, 2, i))
                    'Console.WriteLine(Reaper.utility.GetFxParamNormalized(0, 1, 0))
                    'Console.WriteLine(Reaper.utility.GetTrackSendUIMute(0, 1))
                    'Console.WriteLine(Reaper.utility.SetTrackSendUIVol(0, 0, i, -1))
                    'Console.WriteLine(Reaper.utility.SetTrackUIPan(0, i, -1))
                    'Console.WriteLine(Reaper.utility.SetTrackUIMute(0, state, -1))
                    'Console.WriteLine(Reaper.utility.ToggleTrackSendUIMute(0, 0, -1))
                    'Console.WriteLine(Reaper.utility.GetProjectStateChangeCount)

                    state = Not state

                    'If i >= 1 Then
                    '    add = False
                    'ElseIf i <= 0 Then
                    '    add = True
                    'End If
                    'If add Then
                    '    i += 0.01
                    'Else
                    '    i -= 0.01
                    'End If

                    Thread.Sleep(300)
                Loop

                Exit Sub
            End Sub

            Sub StartNetworkCommunication()
                ' ✅ Réduit les pauses GC Gen2 (10-80ms → < 2ms)
                System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency

                'Appel Test Reaper Utility pour faire des tests :
                'Test_reaper_utility()

                'init packet handler
                ' Get IP address
                Dim firstIp As IPAddress = If(NetworkInterface.GetIPProperties.UnicastAddresses?.Count > 0, NetworkInterface.GetIPProperties.UnicastAddresses(0).Address, New IPAddress({&H7F, &H0, &H0, &H1}))

                Dim listenAddress As New IPEndPoint(firstIp, C24_port)
                _ComputerMacAddr = NetworkInterface.GetPhysicalAddress.GetAddressBytes
                Dim mac_addr_str As String = BitConverter.ToString(_ComputerMacAddr).Replace("-", ":")
                Dim Interface_Name As String = NetworkInterface.Name

                'Set is running
                _IsRunning = True

                'Set Blinking timer
                _BlinkTimer = New System.Threading.Timer(
        AddressOf OnBlinkTimer,
        Nothing,
        _BlinkInterval,
        _BlinkInterval)



                'init Packet Manager 
                _PacketManager = New Packet.PacketManager(Me)
                _AllCmdBytesQueue = New ConcurrentQueue(Of List(Of Byte()))

                ' Initialize packet capture
                ' ✅ Native Pcap P/Invoke - Direct wpcap.dll access (NO NuGet dependencies!)
                _ReceivedC24PacketQueue = New Concurrent.ConcurrentQueue(Of Packet.C24_Packet)
                _AllCmdBytesQueue = New ConcurrentQueue(Of List(Of Byte()))
                Try
                    '_log.Info("🚀 Initializing NativePcapAdapter (P/Invoke direct to wpcap.dll)...")
                    _packetCapture = New NativePcapAdapter()
                    '_log.Info("✅ Native Pcap initialized successfully - Zero NuGet dependencies!")
                    _packetCapture.OpenDevice(Interface_Name, mac_addr_str)

                    ' Set filter for Control24 packets
                    _packetCapture.SetFilter(mac_addr_str)

                    ' Start C24packet Capture process thread
                    ' 1:Capture de tous les bytes
                    ' 2:Orientation vers le bon traitement
                    '       Commandes -> Envois Ultra fast d'un Ack de réception puis Queue de traitement de commandes
                    '       Broadcast -> Init Procédure
                    '       Ack       -> Déblocage de Ackreceived pour permettre de nouveau des envois

                    _packetCapture.StartCapture(AddressOf PacketHandler)
                    _isCapturing = True

                    'Start C24ReceivedCmd process
                    ReceivedC24PacketThread = New Thread(AddressOf ReceivedC24PacketLoop) With {
                        .Name = "ReceivedC24Packet",
                        .IsBackground = True,
                        .Priority = ThreadPriority.Highest
                    }
                    ReceivedC24PacketThread.Start()


                    'Start C24packet Sending process thread
                    SendingInstantQueueThread = New Thread(AddressOf SendingInstantQueueLoop) With {
                        .Name = "SendingInstantQueue",
                        .IsBackground = True,
                        .Priority = ThreadPriority.Normal
                    }
                    SendingInstantQueueThread.Start()


                    'Start Sending Timing Queue Thread

                    SendingTimingQueueThread = New Thread(AddressOf SendingTimingQueueLoop) With {
                        .Name = "SendingTimingQueue",
                .IsBackground = True,
                .Priority = ThreadPriority.Normal
                    }
                    SendingTimingQueueThread.Start()

                    'Start Sending All Commands bytes Queue Thread

                    SendingAllCmdBytesQueueThread = New Thread(AddressOf SendingAllCmdBytesQueueLoop) With {
                        .Name = "SendingAllCmdBytesQueue",
                .IsBackground = True,
                .Priority = ThreadPriority.Normal
                    }
                    SendingAllCmdBytesQueueThread.Start()



                Catch ex As Exception
                    Dim message As String
                    If OperatingSystem.IsWindows() Then
                        message = "Cannot initialize packet capture. Npcap is required for Control24."
                    Else
                        message = "Cannot initialize packet capture. libpcap and access to /dev/bpf* are required for Control24."
                    End If
                    Throw New Exception($"{message} {ex.Message}", ex)
                End Try


            End Sub


            Private Sub OnBlinkTimer(state As Object)
                _BlinkState = Not _BlinkState
                MyBase.OnBlinkStateChange(_BlinkState)
            End Sub



            Private Sub PacketHandler(pktData As Byte())
                _PacketManager._ReceiveBytes(pktData)   ' ✅ version optimisée : ACK avant parse
            End Sub

            Private Sub SendingTimingQueueLoop()
                While IsRunning
                    Dim SendCmdBytes As New List(Of Byte())
                    For Each _AutoRefresh_C24_Component In Me.AutoRefresh_C24_Components
                        For Each C24_Command In _AutoRefresh_C24_Component.C24_SendingCommands
                            SendCmdBytes.Add(DirectCast(C24_Command.Cmd.Clone(), Byte()))
                        Next

                    Next
                    _AllCmdBytesQueue.Enqueue(SendCmdBytes)
                    Thread.Sleep(TimingQueueTimeout)
                End While
            End Sub

            Private Sub SendingInstantQueueLoop()
                While IsRunning
                    ' ── Fenêtre de coalescence : 5ms garantis même sous charge ──
                    ' Permet d'accumuler plusieurs EnqueueCmd du même composant
                    ' avant de drainer, réduisant le nombre de paquets envoyés.
                    Thread.Sleep(InstantQueueTimeout)
                    ' ── Draine les dernières valeurs par composant (coalescence) ──
                    Dim SendCmdBytes = DrainLatestCmds()

                    If SendCmdBytes.Count > 0 Then
                        _AllCmdBytesQueue.Enqueue(SendCmdBytes)
                    Else
                        Thread.Sleep(InstantQueueTimeout)
                    End If
                End While
            End Sub

            Private Sub SendingAllCmdBytesQueueLoop()
                While IsRunning
                    Dim allCmds As New List(Of Byte())
                    Dim cmdBatch As List(Of Byte()) = Nothing

                    ' ── Fusionne TOUS les batches en attente en une seule liste ──
                    ' SendCmdBytes se charge de les grouper en paquets de Max_PacketData_Length.
                    ' Résultat : 1 à 2 paquets max au lieu de N paquets séquentiels.
                    While _AllCmdBytesQueue.TryDequeue(cmdBatch)
                        If cmdBatch IsNot Nothing Then
                            allCmds.AddRange(cmdBatch)
                        End If
                    End While

                    If allCmds.Count > 0 Then
                        _PacketManager.SendCmdBytes(allCmds)
                    Else
                        Thread.Sleep(1)
                    End If
                End While
            End Sub



            Private Sub ReceivedC24PacketLoop()
                While IsRunning
                    Dim hasReceived As Boolean = False
                    While Not ReceivedC24PacketQueue.IsEmpty
                        Dim packet As Packet.C24_Packet = Nothing
                        If ReceivedC24PacketQueue.TryDequeue(packet) Then
                            _PacketManager.ReceivedC24Packet(packet)
                            hasReceived = True   ' ✅
                        End If
                    End While
                    If Not hasReceived Then Thread.Sleep(1)   ' ✅ évite le spin à 100% CPU
                End While
            End Sub

            Public Sub Send_Cmd_C24Packet(CmdC24Packet As Byte())
                _packetCapture.SendPacket(CmdC24Packet)
            End Sub

            ''' <summary>
            ''' Arrête proprement tous les threads, timers et la capture réseau.
            ''' Idempotent : sans effet si déjà appelé.
            ''' </summary>
            Public Sub Shutdown()
                If Not _IsRunning Then Return
                _IsRunning = False

                ' ── Arrêt du timer de clignotement ───────────────────────────────
                If _BlinkTimer IsNot Nothing Then
                    _BlinkTimer.Change(Threading.Timeout.Infinite, Threading.Timeout.Infinite)
                    _BlinkTimer.Dispose()
                    _BlinkTimer = Nothing
                End If

                ' ── Arrêt de la capture réseau ────────────────────────────────────
                If _isCapturing Then
                    _isCapturing = False
                    If _packetCapture IsNot Nothing Then
                        _packetCapture.StopCapture()
                        _packetCapture.Dispose()
                        _packetCapture = Nothing
                    End If
                End If

                ' ── Attente de fin des threads (500 ms max chacun) ────────────────
                Dim threads As Threading.Thread() = {
                    ReceivedC24PacketThread,
                    SendingInstantQueueThread,
                    SendingTimingQueueThread,
                    SendingAllCmdBytesQueueThread
                }
                For Each t In threads
                    If t IsNot Nothing AndAlso t.IsAlive Then
                        t.Join(500)
                    End If
                Next

                ReceivedC24PacketThread = Nothing
                SendingInstantQueueThread = Nothing
                SendingTimingQueueThread = Nothing
                SendingAllCmdBytesQueueThread = Nothing
            End Sub


        End Class

        ''' <summary>
        ''' OBSOLETTE : Représente un fader motorisé du Control24.
        ''' Bidirectionnel : reçoit les mouvements manuels et envoie la position au moteur.
        ''' Intègre un <see cref="FaderTouch"/>, un <see cref="ScribbleStrip"/> et
        ''' l'affichage temporaire du volume en dB (restauration automatique après 2 s).
        ''' </summary>
        'Public Class OldC24_Fader
        '    Inherits C24_Component

        '    Private WithEvents _FaderCmd As Commands.Fader_Cmd
        '    Private ReadOnly _FaderTouch As C24_Button
        '    Private ReadOnly _ScribbleStrip As C24_ScribbleStrip
        '    Private _NormValue As Double
        '    Private _NormFormatedValues As Tuple(Of Double, String)

        '    Private _DbVolume As Double = 0

        '    ' ── Restauration ScribbleStrip ─────────────────────────────────────
        '    Private _OriginalScribbleText As String = ""
        '    Private _IsShowingValue As Boolean = False
        '    Private _RestoreTimer As System.Threading.Timer
        '    Private Const RestoreDelayMs As Integer = 2000

        '    ' ── Vérification de position fader ────────────────────────────────
        '    Private _TargetFaderValue As Integer = 0
        '    Private _VerifyRetryCount As Integer = 0
        '    Private _VerifyTimer As System.Threading.Timer
        '    Private _Fader_Mode As Mode = Mode.Fader
        '    Private _PanValue As Double
        '    Private _PanValueText As String
        '    Private Const VerifyDelayMs As Integer = 150
        '    Private Const MaxVerifyRetry As Integer = 5

        '    ' ── Constantes ────────────────────────────────────────────────────
        '    Private Const FaderMin As Integer = 0
        '    Private Const FaderMax As Integer = 1023
        '    Private Const BtnFaderTouch As Byte = &H9

        '    ' ── Événements ────────────────────────────────────────────────────

        '    ''' <summary>Déclenché quand un humain pose la main sur le fader.</summary>
        '    Public Event On_FaderTouched(Component_Id As String)

        '    ''' <summary>Déclenché quand la main quitte le fader.</summary>
        '    Public Event On_FaderReleased(Component_Id As String)

        '    ''' <summary>
        '    ''' Déclenché quand le hardware envoie une nouvelle position de fader
        '    ''' pendant que le fader est touché par un humain.
        '    ''' </summary>
        '    Public Event On_FaderHumanChangeDbValue(TrackNb As Integer, DbValue As Double)
        '    Public Event On_FaderHumanChangeNormValue(TrackNb As Integer, _NormFormatedValues As Tuple(Of Double, String))
        '    Public Event On_FaderHumanChangePanValue(TrackNb As Integer, _PanValue As Double)

        '    Enum Mode
        '        Fader
        '        NormValue
        '        Pan
        '    End Enum
        '    ' ── Propriétés ────────────────────────────────────────────────────
        '    Public Property Fader_Mode As Mode
        '        Get
        '            Return _Fader_Mode
        '        End Get
        '        Set
        '            _Fader_Mode = Value
        '        End Set
        '    End Property



        '    ''' <summary>Commande fader sous-jacente (accès direct aux bytes).</summary>
        '    Public ReadOnly Property FaderCmd As Commands.Fader_Cmd
        '        Get
        '            Return _FaderCmd
        '        End Get
        '    End Property

        '    ''' <summary>Bouton de détection de contact humain sur le fader.</summary>
        '    Public ReadOnly Property FaderTouch As C24_Button
        '        Get
        '            Return _FaderTouch
        '        End Get
        '    End Property

        '    ''' <summary>ScribbleStrip associé à ce fader.</summary>
        '    Public ReadOnly Property ScribbleStrip As C24_ScribbleStrip
        '        Get
        '            Return _ScribbleStrip
        '        End Get
        '    End Property

        '    ''' <summary>True si un humain touche actuellement le fader.</summary>
        '    Public ReadOnly Property IsTouched As Boolean
        '        Get
        '            Return _FaderTouch.IsPressed
        '        End Get
        '    End Property

        '    ''' <summary>Valeur brute du fader de 0 à 1023.</summary>
        '    Public Property FaderValue As Integer
        '        Get
        '            Return _FaderCmd.FaderValue
        '        End Get
        '        Set
        '            _FaderCmd.FaderValue = Math.Min(Math.Max(Value, FaderMin), FaderMax)
        '        End Set
        '    End Property
        '    ''' <summary>
        '    ''' Valeur normalisée du fader entre 0.0 et 1.0.
        '    ''' Convertit automatiquement vers/depuis la valeur brute [0..1023].
        '    ''' </summary>
        '    Public Property NormFormatedValues As Tuple(Of Double, String)
        '        Get
        '            Return _NormFormatedValues
        '        End Get
        '        Set

        '            _NormValue = Value.Item1
        '            _NormFormatedValues = Value

        '            If _IsShowingValue = False Then
        '                _OriginalScribbleText = _ScribbleStrip.Text
        '                _IsShowingValue = True
        '            End If

        '            Dim DblValue As Double = 0
        '            If Double.TryParse(Value.Item2, Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, DblValue) Then
        '                _ScribbleStrip.Value = DblValue
        '            ElseIf Value.Item2 IsNot Nothing AndAlso Value.Item2 <> "" Then
        '                _ScribbleStrip.Text = Value.Item2
        '            Else
        '                _ScribbleStrip.Value = Value.Item1
        '            End If



        '            _RestoreTimer.Change(RestoreDelayMs, System.Threading.Timeout.Infinite)





        '        End Set
        '    End Property


        '    Public Property PanValue As Double
        '        Get
        '            Return _PanValue
        '        End Get
        '        Set
        '            _PanValue = Value


        '            Dim Value_int As Integer = CInt(_PanValue)
        '            If Value_int = 0 Then
        '                _PanValueText = "<0>"
        '            ElseIf Value_int > 0 Then
        '                _PanValueText = Value_int & ">"
        '            ElseIf Value_int < 0 Then
        '                _PanValueText = "<" & Math.Abs(Value_int)
        '            End If



        '            If _IsShowingValue = False Then
        '                _OriginalScribbleText = _ScribbleStrip.Text
        '                _IsShowingValue = True
        '            End If


        '            _ScribbleStrip.Text = _PanValueText


        '            _RestoreTimer.Change(RestoreDelayMs, System.Threading.Timeout.Infinite)

        '        End Set
        '    End Property
        '    Public Property PanValueText As String
        '        Get
        '            Return _PanValueText
        '        End Get
        '        Set
        '            _PanValueText = Value
        '        End Set
        '    End Property

        '    ''' <summary>
        '    ''' Volume en dB.
        '    ''' Affiche la valeur sur le <see cref="ScribbleStrip"/> uniquement si elle change.
        '    ''' Le texte original est restauré automatiquement <see cref="RestoreDelayMs"/> ms
        '    ''' après la dernière modification.
        '    ''' </summary>
        '    Public Property DbVolume As Double
        '        Get
        '            Return _DbVolume
        '        End Get
        '        Set
        '            If _DbVolume = Value Then
        '                Return
        '            End If

        '            _DbVolume = Value

        '            If _IsShowingValue = False Then
        '                _OriginalScribbleText = _ScribbleStrip.Text
        '                _IsShowingValue = True
        '            End If

        '            _ScribbleStrip.Value = _DbVolume


        '            _RestoreTimer.Change(RestoreDelayMs, System.Threading.Timeout.Infinite)
        '        End Set
        '    End Property

        '    ' ── Restauration ScribbleStrip ─────────────────────────────────────

        '    ''' <summary>Restaure le texte original du ScribbleStrip après 2 s d'inactivité.</summary>
        '    Private Sub OnRestoreTimer(state As Object)
        '        _IsShowingValue = False
        '        _ScribbleStrip.Text = _OriginalScribbleText
        '    End Sub

        '    ''' <summary>
        '    ''' Envoie la commande fader vers <paramref name="rawValue"/>
        '    ''' et arme le timer de vérification.
        '    ''' Une nouvelle valeur demandée remplace automatiquement la cible
        '    ''' et redémarre le cycle de vérification.
        '    ''' </summary>
        '    ''' <param name="rawValue">Valeur brute cible [0..1023].</param>
        '    Private Sub SendAndArmVerify(rawValue As Integer)
        '        _TargetFaderValue = Math.Min(Math.Max(rawValue, FaderMin), FaderMax)
        '        FaderValue = _TargetFaderValue
        '        _VerifyTimer.Change(VerifyDelayMs, System.Threading.Timeout.Infinite)
        '    End Sub

        '    ''' <summary>
        '    ''' Vérifie que le fader a atteint la position cible.
        '    ''' Si la cible n'est pas atteinte et que le fader n'est pas touché,
        '    ''' renvoie la commande et se réarme indéfiniment jusqu'à succès
        '    ''' ou jusqu'à ce qu'une nouvelle valeur soit demandée via
        '    ''' <see cref="SendAndArmVerify"/>.
        '    ''' </summary>
        '    Private Sub OnVerifyTimer(state As Object)
        '        If IsTouched Then
        '            Return
        '        End If

        '        If _FaderCmd.FaderValue = _TargetFaderValue Then
        '            Return
        '        End If

        '        FaderValue = _TargetFaderValue
        '        _VerifyTimer.Change(VerifyDelayMs, System.Threading.Timeout.Infinite)
        '    End Sub

        '    ' ── API publique ──────────────────────────────────────────────────

        '    ''' <summary>
        '    ''' Pilote le fader depuis Reaper.
        '    ''' 
        '    ''' Lance la vérification automatique de position.
        '    ''' </summary>
        '    ''' <param name="Db_Volume">Volume cible en dB.</param>
        '    Public Sub SetFaderDbVolume(Db_Volume As Double)

        '        DbVolume = Db_Volume
        '        SendAndArmVerify(DbToRawFaderCmdValue(Db_Volume))
        '    End Sub
        '    Public Sub SetFaderNormValue(_NormFormatedValues As Tuple(Of Double, String))

        '        NormFormatedValues = _NormFormatedValues
        '        SendAndArmVerify(NormValueToRawFaderCmdValue(_NormFormatedValues.Item1))
        '    End Sub
        '    Public Sub SetFaderPanValue(_PanValue As Double)

        '        PanValue = _PanValue
        '        SendAndArmVerify(NormValueToRawFaderCmdValue((_PanValue + 1) / 2))
        '    End Sub
        '    ''' <summary>
        '    ''' Initialise la position du fader sans vérification d'affichage.
        '    ''' Lance quand même la vérification de position hardware.
        '    ''' </summary>
        '    ''' <param name="Db_Volume">Volume initial en dB.</param>
        '    Public Sub SetInitFaderDbVolume(Db_Volume As Double)
        '        _DbVolume = Db_Volume
        '        SendAndArmVerify(DbToRawFaderCmdValue(Db_Volume))
        '    End Sub

        '    ' ── Synchronisation depuis le hardware ────────────────────────────

        '    ''' <summary>
        '    ''' Appelé quand le hardware envoie un mouvement fader (fader touché uniquement).
        '    ''' </summary>
        '    Private Sub OnFaderCmdChange() Handles _FaderCmd.OnCmdChange
        '        If IsTouched Then
        '            Select Case Fader_Mode
        '                Case Mode.Fader
        '                    DbVolume = RawFaderCmdValueToDb(_FaderCmd.FaderValue)
        '                    RaiseEvent On_FaderHumanChangeDbValue(FaderCmd.Track_Number, DbVolume)
        '                Case Mode.NormValue
        '                    _NormValue = RawFaderCmdValueToNormValue(_FaderCmd.FaderValue)
        '                    _NormFormatedValues = New Tuple(Of Double, String)(_NormValue, Nothing)
        '                    RaiseEvent On_FaderHumanChangeNormValue(FaderCmd.Track_Number, _NormFormatedValues)
        '                Case Mode.Pan
        '                    _PanValue = (2 * RawFaderCmdValueToNormValue(_FaderCmd.FaderValue)) - 1
        '                    RaiseEvent On_FaderHumanChangePanValue(FaderCmd.Track_Number, _PanValue)
        '            End Select


        '        End If
        '    End Sub
        '    Public Sub OnFaderManualChange(Track_Number As Integer, Optional DbVol As Double? = Nothing, Optional NormValue As Double? = Nothing)

        '        Select Case Fader_Mode
        '            Case Mode.Fader
        '                If DbVol IsNot Nothing Then
        '                    DbVolume = DbVol
        '                End If
        '                RaiseEvent On_FaderHumanChangeDbValue(Track_Number, DbVolume)
        '            Case Mode.NormValue
        '                If NormValue IsNot Nothing Then
        '                    _NormValue = NormValue
        '                    _NormFormatedValues = New Tuple(Of Double, String)(_NormValue, Nothing)
        '                    RaiseEvent On_FaderHumanChangeNormValue(Track_Number, _NormFormatedValues)
        '                End If


        '        End Select



        '    End Sub

        '    Private Sub OnFaderTouchPressed(componentId As String)
        '        _VerifyTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite)
        '        RaiseEvent On_FaderTouched(_Component_Id)
        '    End Sub

        '    Private Sub OnFaderTouchReleased(componentId As String)
        '        RaiseEvent On_FaderReleased(_Component_Id)
        '    End Sub

        '    ' ── Constructeur ──────────────────────────────────────────────────

        '    ''' <summary>
        '    ''' Crée un fader motorisé avec détection de contact et affichage dB
        '    ''' pour la piste <paramref name="trackNumber"/>.
        '    ''' </summary>
        '    ''' <param name="name">Nom unique du composant.</param>
        '    ''' <param name="trackNumber">Numéro de piste 1-based.</param>
        '    ''' <param name="scribbleStrip">ScribbleStrip pour l'affichage du volume en dB.</param>
        '    Public Sub New(name As String, trackNumber As Integer, scribbleStrip As C24_ScribbleStrip)
        '        _Name = name
        '        _FaderCmd = New Commands.Fader_Cmd(trackNumber)
        '        _C24_ReceivingCommand = _FaderCmd
        '        C24_SendingCommands.Add(_FaderCmd)
        '        _Component_Id = _FaderCmd.Cmd_Id
        '        _ScribbleStrip = scribbleStrip

        '        _FaderTouch = New C24_Button($"FaderTouch_{trackNumber}", BtnFaderTouch, trackNumber)
        '        AddHandler _FaderTouch.OnButtonPressed, AddressOf OnFaderTouchPressed
        '        AddHandler _FaderTouch.OnButtonReleased, AddressOf OnFaderTouchReleased

        '        _RestoreTimer = New System.Threading.Timer(
        '            AddressOf OnRestoreTimer,
        '            Nothing,
        '            System.Threading.Timeout.Infinite,
        '            System.Threading.Timeout.Infinite)

        '        _VerifyTimer = New System.Threading.Timer(
        '            AddressOf OnVerifyTimer,
        '            Nothing,
        '            System.Threading.Timeout.Infinite,
        '            System.Threading.Timeout.Infinite)
        '    End Sub

        '    ' ── Conversion dB ↔ raw ───────────────────────────────────────────

        '    Private Function DbToRawFaderCmdValue(Db As Double) As Integer
        '        If Db <= -150 Then
        '            Return 0
        '        ElseIf Db > -150 AndAlso Db <= -90 Then
        '            Return CInt(Math.Round((1.1811 * Db) - 43.701, 0))
        '        ElseIf Db > -90 AndAlso Db <= -20 Then
        '            Return CInt(Math.Round((563.83 * Math.Exp(0.0298 * Db))))
        '        ElseIf Db > -20 AndAlso Db < 6 Then
        '            Return CInt(Math.Round((27.308 * Db) + 861.66))
        '        ElseIf Db >= 6 Then
        '            Return 1023
        '        End If
        '        Return 0
        '    End Function
        '    Private Function RawFaderCmdValueToNormValue(raw As Integer) As Double
        '        Return Math.Round(_FaderCmd.FaderValue / CDbl(FaderMax), 4)
        '    End Function
        '    Private Function NormValueToRawFaderCmdValue(NormValue As Double) As Integer
        '        Return CInt(Math.Round(Math.Min(Math.Max(NormValue, 0.0), 1.0) * FaderMax))
        '    End Function
        '    ''' <summary>
        '    ''' Convertit une valeur brute de commande fader (0–1023) en volume en dB.
        '    ''' Fonction inverse de <see cref="DbToRawFaderCmdValue"/>.
        '    ''' </summary>
        '    Private Function RawFaderCmdValueToDb(raw As Integer) As Double
        '        If raw <= 0 Then
        '            Return -150.0
        '        ElseIf raw >= 1023 Then
        '            Return 6.0
        '        ElseIf raw <= 313 Then
        '            Return Math.Log(raw / 563.83) / 0.0298
        '        Else
        '            Return (raw - 861.66) / 27.308
        '        End If
        '    End Function

        'End Class

        ''' <summary>
        ''' Représente un fader motorisé du Control24.
        ''' Bidirectionnel : reçoit les mouvements manuels et envoie la position au moteur.
        ''' Intègre un <see cref="FaderTouch"/>, un <see cref="ScribbleStrip"/> et
        ''' l'affichage temporaire du volume en dB (restauration automatique après 2 s).
        ''' </summary>
        Public Class C24_Fader
            Inherits C24_Component

            Private WithEvents _FaderCmd As Commands.Fader_Cmd
            Private ReadOnly _FaderTouch As C24_Button
            Private ReadOnly _ScribbleStrip As C24_ScribbleStrip
            Private _NormValue As Double
            Private _NormFormatedValues As Tuple(Of Double, String)
            Private _DbVolume As Double = 0
            Private _LastFaderValue As Integer = 0

            ' ── Restauration ScribbleStrip ─────────────────────────────────────
            Private _OriginalScribbleText As String = ""
            Private _IsShowingValue As Boolean = False
            Private _RestoreTimer As System.Threading.Timer
            Private Const RestoreDelayMs As Integer = 2000

            ' ── Cible moteur (jamais envoyée si IsTouched) ─────────────────────
            Private _TargetFaderValue As Integer = 0
            Private _Fader_Mode As Mode = Mode.Db
            Private _PanValue As Double
            Private _PanValueText As String
            Private _FormatedValue As String

            ' ── Constantes ────────────────────────────────────────────────────
            Private Const FaderMin As Integer = 0
            Private Const FaderMax As Integer = 1023
            Private Const BtnFaderTouch As Byte = &H9

            ' ── Événements ────────────────────────────────────────────────────
            ''' <summary>Déclenché quand un humain pose la main sur le fader.</summary>
            Public Event On_FaderTouched(Component_Id As String)
            ''' <summary>Déclenché quand la main quitte le fader.</summary>
            Public Event On_FaderReleased(Component_Id As String)
            ''' <summary>Déclenché quand le hardware envoie une nouvelle position pendant que le fader est touché.</summary>
            Public Event On_FaderHumanChangeDbValue(TrackNb As Integer, DbValue As Double)
            Public Event On_FaderHumanChangeNormValue(TrackNb As Integer, _NormFormatedValues As Tuple(Of Double, String))
            Public Event On_FaderHumanChangePanValue(TrackNb As Integer, _PanValue As Double)

            Enum Mode
                Db
                NormValue
                Pan
            End Enum

            ' ── Propriétés ────────────────────────────────────────────────────
            ''' <summary>Mode de fonctionnement du fader.</summary>
            Public Property Fader_Mode As Mode
                Get
                    Return _Fader_Mode
                End Get
                Set
                    If Value <> _Fader_Mode Then
                        _Fader_Mode = Value
                    End If
                End Set
            End Property

            ''' <summary>Commande fader sous-jacente.</summary>
            Public ReadOnly Property FaderCmd As Commands.Fader_Cmd
                Get
                    Return _FaderCmd
                End Get
            End Property

            ''' <summary>Bouton de détection de contact humain.</summary>
            Public ReadOnly Property FaderTouch As C24_Button
                Get
                    Return _FaderTouch
                End Get
            End Property

            ''' <summary>ScribbleStrip associé.</summary>
            Public ReadOnly Property ScribbleStrip As C24_ScribbleStrip
                Get
                    Return _ScribbleStrip
                End Get
            End Property

            ''' <summary>True si un humain touche actuellement le fader.</summary>
            Public ReadOnly Property IsTouched As Boolean
                Get
                    Return _FaderTouch.IsPressed
                End Get
            End Property

            ''' <summary>Valeur brute du fader de 0 à 1023.</summary>
            Public Property FaderValue As Integer
                Get
                    Return _FaderCmd.FaderValue
                End Get
                Set
                    Dim newValue = Math.Min(Math.Max(Value, FaderMin), FaderMax)
                    'Console.WriteLine("NewValue:" & newValue)
                    'Console.WriteLine("FaderCmd:" & _FaderCmd.FaderValue)

                    If newValue <> _LastFaderValue Then
                        'Console.WriteLine("Send:" & newValue)
                        _LastFaderValue = newValue
                        _FaderCmd.FaderValue = newValue
                    End If
                End Set
            End Property
            Public Property FormatedValue As String
                Get
                    Return _FormatedValue
                End Get
                Set
                    If Value <> _FormatedValue Then
                        _FormatedValue = Value

                        If _IsShowingValue = False Then
                            _OriginalScribbleText = _ScribbleStrip.Text
                            _IsShowingValue = True
                        End If

                        _ScribbleStrip.Text = _FormatedValue
                        _RestoreTimer.Change(RestoreDelayMs, System.Threading.Timeout.Infinite)
                    End If

                End Set
            End Property

            ''' <summary>Valeur normalisée + texte formaté.</summary>
            Public Property NormFormatedValues As Tuple(Of Double, String)
                Get
                    Return _NormFormatedValues
                End Get
                Set
                    If Value IsNot _NormFormatedValues Then
                        _NormValue = Value.Item1
                        _NormFormatedValues = Value

                        If _IsShowingValue = False Then
                            _OriginalScribbleText = _ScribbleStrip.Text
                            _IsShowingValue = True
                        End If

                        Dim DblValue As Double = 0
                        If Double.TryParse(Value.Item2, Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, DblValue) Then
                            _ScribbleStrip.Value = DblValue
                        ElseIf Value.Item2 IsNot Nothing AndAlso Value.Item2 <> "" Then
                            _ScribbleStrip.Text = Value.Item2
                        Else
                            _ScribbleStrip.Value = Value.Item1
                        End If

                        _RestoreTimer.Change(RestoreDelayMs, System.Threading.Timeout.Infinite)
                    End If
                End Set
            End Property

            ''' <summary>Valeur pan (-1.0 à +1.0).</summary>
            Public Property PanValue As Double
                Get
                    Return _PanValue
                End Get
                Set
                    If Value <> _PanValue Then
                        _PanValue = Value

                        Dim Value_int As Integer = CInt(_PanValue)
                        If Value_int = 0 Then
                            _PanValueText = "<0>"
                        ElseIf Value_int > 0 Then
                            _PanValueText = Value_int & ">"
                        ElseIf Value_int < 0 Then
                            _PanValueText = "<" & Math.Abs(Value_int)
                        End If

                        If _IsShowingValue = False Then
                            _OriginalScribbleText = _ScribbleStrip.Text
                            _IsShowingValue = True
                        End If

                        _ScribbleStrip.Text = _PanValueText
                        _RestoreTimer.Change(RestoreDelayMs, System.Threading.Timeout.Infinite)
                    End If
                End Set
            End Property

            ''' <summary>Texte pan affiché.</summary>
            Public Property PanValueText As String
                Get
                    Return _PanValueText
                End Get
                Set
                    If Value <> _PanValueText Then
                        _PanValueText = Value
                    End If
                End Set
            End Property

            ''' <summary>
            ''' Volume en dB. Affiche la valeur sur le ScribbleStrip.
            ''' Restauration automatique après <see cref="RestoreDelayMs"/> ms.
            ''' </summary>
            Public Property DbVolume As Double
                Get
                    Return _DbVolume
                End Get
                Set
                    If _DbVolume = Value Then
                        Return
                    End If

                    _DbVolume = Value

                    If _IsShowingValue = False Then
                        _OriginalScribbleText = _ScribbleStrip.Text
                        'Console.WriteLine("org txt:" & _OriginalScribbleText)
                        _IsShowingValue = True
                    End If

                    _ScribbleStrip.Value = _DbVolume
                    _RestoreTimer.Change(RestoreDelayMs, System.Threading.Timeout.Infinite)
                End Set
            End Property

            ' ── Restauration ScribbleStrip ─────────────────────────────────────
            Private Sub OnRestoreTimer(state As Object)
                _IsShowingValue = False
                'Console.WriteLine("restore txt:" & _OriginalScribbleText)
                _ScribbleStrip.Text = _OriginalScribbleText
            End Sub

            ' ── Envoi moteur avec gate tactile ─────────────────────────────────
            ''' <summary>
            ''' Mémorise la cible et envoie la commande moteur UNIQUEMENT si le fader
            ''' n'est pas touché par un humain.
            ''' Si touché : la cible est stockée et sera envoyée à la libération.
            ''' Aucun timer de vérification — pas d'interférence avec l'écriture d'enveloppe.
            ''' </summary>
            Private Sub SendMotorIfNotTouched(rawValue As Integer)
                _TargetFaderValue = Math.Min(Math.Max(rawValue, FaderMin), FaderMax)
                ' Console.WriteLine("target:" & _TargetFaderValue)
                If Not IsTouched Then
                    FaderValue = _TargetFaderValue
                End If
            End Sub

            ' ── API publique ──────────────────────────────────────────────────
            ''' <summary>Pilote le fader depuis Reaper (volume en dB).</summary>
            Public Sub SetFaderDbVolume(Db_Volume As Double)
                DbVolume = Db_Volume
                SendMotorIfNotTouched(DbToRawFaderCmdValue(Db_Volume))
            End Sub

            ''' <summary>Pilote le fader depuis Reaper (valeur normalisée).</summary>
            Public Sub SetFaderNormValue(NormValue As Double)
                SendMotorIfNotTouched(NormValueToRawFaderCmdValue(NormValue))
            End Sub

            ''' <summary>Pilote le fader depuis Reaper (pan).</summary>
            Public Sub SetFaderPanValue(_PanValue As Double)
                PanValue = _PanValue
                SendMotorIfNotTouched(NormValueToRawFaderCmdValue((_PanValue + 1) / 2))
            End Sub

            ''' <summary>Initialise la position sans affichage scribble.</summary>
            Public Sub SetInitFaderDbVolume(Db_Volume As Double)
                _DbVolume = Db_Volume
                SendMotorIfNotTouched(DbToRawFaderCmdValue(Db_Volume))
            End Sub

            ' ── Lecture hardware ──────────────────────────────────────────────
            ''' <summary>
            ''' Appelé à chaque mouvement fader reçu du hardware.
            ''' Ne lève les événements humains que si le fader est touché.
            ''' </summary>
            Private Sub OnFaderCmdChange() Handles _FaderCmd.OnCmdChange
                If IsTouched Then
                    _TargetFaderValue = _FaderCmd.FaderValue
                    'Console.WriteLine("SetTarget:" & _TargetFaderValue)
                    Select Case Fader_Mode
                        Case Mode.Db
                            DbVolume = RawFaderCmdValueToDb(_FaderCmd.FaderValue)
                            RaiseEvent On_FaderHumanChangeDbValue(FaderCmd.Track_Number, DbVolume)
                        Case Mode.NormValue
                            _NormValue = RawFaderCmdValueToNormValue(_FaderCmd.FaderValue)
                            _NormFormatedValues = New Tuple(Of Double, String)(_NormValue, Nothing)
                            RaiseEvent On_FaderHumanChangeNormValue(FaderCmd.Track_Number, _NormFormatedValues)
                        Case Mode.Pan
                            _PanValue = (2 * RawFaderCmdValueToNormValue(_FaderCmd.FaderValue)) - 1
                            RaiseEvent On_FaderHumanChangePanValue(FaderCmd.Track_Number, _PanValue)
                    End Select
                End If
            End Sub

            ''' <summary>Manipulation manuelle simulée (hors hardware).</summary>
            Public Sub OnFaderManualChange(Track_Number As Integer, Optional DbVol As Double? = Nothing, Optional NormValue As Double? = Nothing)
                Select Case Fader_Mode
                    Case Mode.Db
                        If DbVol IsNot Nothing Then
                            DbVolume = DbVol
                        End If
                        RaiseEvent On_FaderHumanChangeDbValue(Track_Number, DbVolume)
                    Case Mode.NormValue
                        If NormValue IsNot Nothing Then
                            _NormValue = NormValue
                            _NormFormatedValues = New Tuple(Of Double, String)(_NormValue, Nothing)
                            RaiseEvent On_FaderHumanChangeNormValue(Track_Number, _NormFormatedValues)
                        End If
                End Select
            End Sub

            ''' <summary>
            ''' Appui détecté — suspend les envois moteur (déjà géré par IsTouched).
            ''' </summary>
            Private Sub OnFaderTouchPressed(componentId As String)
                RaiseEvent On_FaderTouched(_Component_Id)
            End Sub

            ''' <summary>
            ''' Relâchement détecté — resynchronise le moteur sur la dernière cible Reaper.
            ''' Garantit que le fader rejoint la position correcte après manipulation humaine.
            ''' </summary>
            Private Sub OnFaderTouchReleased(componentId As String)

                FaderValue = _TargetFaderValue
                'Console.WriteLine("On release:" & FaderValue)
                RaiseEvent On_FaderReleased(_Component_Id)
            End Sub

            ' ── Constructeur ──────────────────────────────────────────────────
            ''' <summary>
            ''' Crée un fader motorisé.
            ''' </summary>
            Public Sub New(name As String, trackNumber As Integer, scribbleStrip As C24_ScribbleStrip)
                _Name = name
                _FaderCmd = New Commands.Fader_Cmd(trackNumber)
                _C24_ReceivingCommand = _FaderCmd
                C24_SendingCommands.Add(_FaderCmd)
                _Component_Id = _FaderCmd.Cmd_Id
                _ScribbleStrip = scribbleStrip

                _FaderTouch = New C24_Button($"FaderTouch_{trackNumber}", BtnFaderTouch, trackNumber)
                AddHandler _FaderTouch.OnButtonPressed, AddressOf OnFaderTouchPressed
                AddHandler _FaderTouch.OnButtonReleased, AddressOf OnFaderTouchReleased

                _RestoreTimer = New System.Threading.Timer(
                    AddressOf OnRestoreTimer,
                    Nothing,
                    System.Threading.Timeout.Infinite,
                    System.Threading.Timeout.Infinite)
            End Sub

            ' ── Conversions dB ↔ raw ──────────────────────────────────────────
            Private Function DbToRawFaderCmdValue(Db As Double) As Integer
                If Db <= -150 Then
                    Return 0
                ElseIf Db > -150 AndAlso Db <= -90 Then
                    Return CInt(Math.Round((1.1811 * Db) - 43.701, 0))
                ElseIf Db > -90 AndAlso Db <= -20 Then
                    Return CInt(Math.Round((563.83 * Math.Exp(0.0298 * Db))))
                ElseIf Db > -20 AndAlso Db < 6 Then
                    Return CInt(Math.Round((27.308 * Db) + 861.66))
                ElseIf Db >= 6 Then
                    Return 1023
                End If
                Return 0
            End Function

            Private Function RawFaderCmdValueToNormValue(raw As Integer) As Double
                Return Math.Round(_FaderCmd.FaderValue / CDbl(FaderMax), 4)
            End Function

            Private Function NormValueToRawFaderCmdValue(NormValue As Double) As Integer
                Return CInt(Math.Round(Math.Min(Math.Max(NormValue, 0.0), 1.0) * FaderMax))
            End Function

            Private Function RawFaderCmdValueToDb(raw As Integer) As Double
                If raw <= 0 Then
                    Return -150.0
                ElseIf raw >= 1023 Then
                    Return 6.0
                ElseIf raw <= 313 Then
                    Return Math.Log(raw / 563.83) / 0.0298
                Else
                    Return (raw - 861.66) / 27.308
                End If
            End Function

        End Class

        ''' <summary>
        ''' Représente un channel strip complet du Control24.
        ''' Regroupe : 1 Fader, 1 ScribbleStrip, 1 Vpot, 1 VueMeter et 11 LedButtons.
        ''' </summary>
        Public Class C24_ChannelStrip

            Private ReadOnly _TrackNumber As Integer
            Private ReadOnly _Fader As C24_Fader
            Private ReadOnly _ScribbleStrip As C24_ScribbleStrip
            Private ReadOnly _Vpot As C24_Vpot
            Private ReadOnly _VueMeter As C24_VueMeter
            Private ReadOnly _RecArm As C24_LedButton
            Private ReadOnly _Solo As C24_LedButton
            Private ReadOnly _Mute As C24_LedButton
            Private ReadOnly _Select As C24_LedButton
            Private ReadOnly _SwitchActive As C24_LedButton
            Private ReadOnly _LedSwitchActive As C24_LedButton
            Private ReadOnly _AutoMode As C24_Button
            Private ReadOnly _AutoModeLed As C24_AutoModeLed
            Private ReadOnly _SendPanMode As C24_LedButton
            Private ReadOnly _EqMode As C24_LedButton
            Private ReadOnly _DynMode As C24_LedButton
            Private ReadOnly _InsertMode As C24_LedButton




            ' ── Byte IDs boutons Control24 standard par piste ─────────────────
            Private Const BtnRecArm As Byte = &H0
            Private Const BtnSolo As Byte = &H7
            Private Const BtnMute As Byte = &H8
            Private Const BtnSelect As Byte = &H6
            Private Const BtnSwitchActive As Byte = &H4
            Private Const BtnLedSwitchActive As Byte = &HB
            Private Const BtnAutoMode As Byte = &H5
            Private Const BtnSendMode As Byte = &H1
            Private Const BtnEqMode As Byte = &H2
            Private Const BtnDynMode As Byte = &H3
            Private Const BtnInsertMode As Byte = &HA

            ' ── Propriétés ────────────────────────────────────────────────────



            ''' <summary>Numéro de piste 1-based de ce channel strip.</summary>
            Public ReadOnly Property TrackNumber As Integer
                Get
                    Return _TrackNumber
                End Get
            End Property

            ''' <summary>Fader motorisé du channel strip.</summary>
            Public ReadOnly Property Fader As C24_Fader
                Get
                    Return _Fader
                End Get
            End Property

            ''' <summary>Affichage texte/valeur du channel strip.</summary>
            Public ReadOnly Property ScribbleStrip As C24_ScribbleStrip
                Get
                    Return _ScribbleStrip
                End Get
            End Property

            ''' <summary>Encodeur rotatif + LEDs en anneau.</summary>
            Public ReadOnly Property Vpot As C24_Vpot
                Get
                    Return _Vpot
                End Get
            End Property

            ''' <summary>VU-mètre stéréo.</summary>
            Public ReadOnly Property VueMeter As C24_VueMeter
                Get
                    Return _VueMeter
                End Get
            End Property

            ''' <summary>Bouton Rec Arm.</summary>
            Public ReadOnly Property RecArm As C24_LedButton
                Get
                    Return _RecArm
                End Get
            End Property

            ''' <summary>Bouton Solo.</summary>
            Public ReadOnly Property Solo As C24_LedButton
                Get
                    Return _Solo
                End Get
            End Property

            ''' <summary>Bouton Mute.</summary>
            Public ReadOnly Property Mute As C24_LedButton
                Get
                    Return _Mute
                End Get
            End Property

            ''' <summary>Bouton Select.</summary>
            Public ReadOnly Property [Select] As C24_LedButton
                Get
                    Return _Select
                End Get
            End Property

            ''' <summary>Bouton SwitchActive.</summary>
            Public ReadOnly Property SwitchActive As C24_LedButton
                Get
                    Return _SwitchActive
                End Get
            End Property
            ''' <summary>Led SwitchActive.</summary>
            Public ReadOnly Property LedSwitchActive As C24_LedButton
                Get
                    Return _LedSwitchActive
                End Get
            End Property


            ''' <summary>Bouton physique AutoMode (détection d'appui uniquement).</summary>
            Public ReadOnly Property AutoMode As C24_Button
                Get
                    Return _AutoMode
                End Get
            End Property

            ''' <summary>LEDs AutoMode (Write / Touch / Latch / Trim / Read).</summary>
            Public ReadOnly Property AutoModeLed As C24_AutoModeLed
                Get
                    Return _AutoModeLed
                End Get
            End Property

            ''' <summary>Bouton Send &amp; Pan Mode.</summary>
            Public ReadOnly Property SendPanMode As C24_LedButton
                Get
                    Return _SendPanMode
                End Get
            End Property

            ''' <summary>Bouton EQ Mode.</summary>
            Public ReadOnly Property EqMode As C24_LedButton
                Get
                    Return _EqMode
                End Get
            End Property

            ''' <summary>Bouton Dynamics Mode.</summary>
            Public ReadOnly Property DynMode As C24_LedButton
                Get
                    Return _DynMode
                End Get
            End Property

            ''' <summary>Bouton Insert Mode.</summary>
            Public ReadOnly Property InsertMode As C24_LedButton
                Get
                    Return _InsertMode
                End Get
            End Property

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Crée un channel strip complet pour la piste <paramref name="trackNumber"/>.
            ''' </summary>
            ''' <param name="trackNumber">Numéro de piste 1-based.</param>
            Public Sub New(trackNumber As Integer)
                _TrackNumber = trackNumber
                Dim n = trackNumber.ToString()

                _ScribbleStrip = New C24_ScribbleStrip($"ScribbleStrip_{n}", trackNumber)
                _Fader = New C24_Fader($"Fader_{n}", trackNumber, _ScribbleStrip)
                _Vpot = New C24_Vpot($"Vpot_{n}", trackNumber, _ScribbleStrip)
                _VueMeter = New C24_VueMeter($"VueMeter_{n}", trackNumber)

                _RecArm = New C24_LedButton($"RecArm_{n}", BtnRecArm, trackNumber)
                _Solo = New C24_LedButton($"Solo_{n}", BtnSolo, trackNumber)
                _Mute = New C24_LedButton($"Mute_{n}", BtnMute, trackNumber)
                _Select = New C24_LedButton($"Select_{n}", BtnSelect, trackNumber)
                _SwitchActive = New C24_LedButton($"SwitchActive_{n}", BtnSwitchActive, trackNumber)
                _LedSwitchActive = New C24_LedButton($"LedSwitchActive_{n}", BtnLedSwitchActive, trackNumber)
                _AutoMode = New C24_Button($"AutoMode_{n}", BtnAutoMode, trackNumber)
                _AutoModeLed = New C24_AutoModeLed($"AutoModeLed_{n}", trackNumber)
                _SendPanMode = New C24_LedButton($"SendPanMode_{n}", BtnSendMode, trackNumber)
                _EqMode = New C24_LedButton($"EqMode_{n}", BtnEqMode, trackNumber)
                _DynMode = New C24_LedButton($"DynMode_{n}", BtnDynMode, trackNumber)
                _InsertMode = New C24_LedButton($"InsertMode_{n}", BtnInsertMode, trackNumber)
            End Sub

            ' ── Enregistrement ────────────────────────────────────────────────

            ''' <summary>
            ''' Enregistre tous les composants du channel strip dans le
            ''' <see cref="C24_Components"/> fourni.
            ''' </summary>
            ''' <param name="components">Instance parente dans laquelle enregistrer les composants.</param>
            Public Sub RegisterTo(components As C24_Components)
                Dim group = $"ChannelStrip_{_TrackNumber}"

                components.Add_C24_Component(_Fader, group)
                components.Add_C24_Component(_Fader.FaderTouch, group)
                components.Add_C24_Component(_ScribbleStrip, group)
                components.Add_C24_Component(_Vpot, group)
                components.Add_AutoRefresh_C24_Component(_VueMeter, group)
                components.Add_C24_Component(_RecArm, group)
                components.Add_C24_Component(_Solo, group)
                components.Add_C24_Component(_Mute, group)
                components.Add_C24_Component(_Select, group)
                components.Add_C24_Component(_SwitchActive, group)
                components.Add_C24_Component(_LedSwitchActive, group)
                components.Add_C24_Component(_AutoMode, group)
                components.Add_C24_Component(_AutoModeLed, group)
                components.Add_C24_Component(_SendPanMode, group)
                components.Add_C24_Component(_EqMode, group)
                components.Add_C24_Component(_DynMode, group)
                components.Add_C24_Component(_InsertMode, group)
            End Sub

        End Class

    End Namespace

    Namespace Packet

        Public Class Eth_Header
            Private _Source_MacAddress As Byte()
            Private _Destination_MacAddress As Byte()
            Dim Protocol As Byte() = {&H88, &H5F}
            Private _IsBroadcast As Boolean = False
            Private _EthHeader As Byte()

            ReadOnly Property EthHeader As Byte()
                Get
                    Return _EthHeader
                End Get
            End Property

            ReadOnly Property Source_MacAddress As Byte()
                Get
                    Return _Source_MacAddress
                End Get
            End Property

            ReadOnly Property Destination_MacAddress As Byte()
                Get
                    Return _Destination_MacAddress
                End Get
            End Property
            ReadOnly Property IsBroadcast As Boolean
                Get
                    Return _Destination_MacAddress IsNot Nothing AndAlso
               _Destination_MacAddress.SequenceEqual({&HFF, &HFF, &HFF, &HFF, &HFF, &HFF})
                End Get
            End Property



            Function Get_Header_Copy()
                _EthHeader = Utility.Bit.ByteManager.ConcatBytes(_Destination_MacAddress, _Source_MacAddress, Protocol)
                Return _EthHeader
            End Function

            Sub New(Header As Byte())
                _Destination_MacAddress = ByteManager.SliceBytes(Header, 0, 6)
                _Source_MacAddress = ByteManager.SliceBytes(Header, 6, 6)
                Get_Header_Copy()
            End Sub

            Sub New(SourceMacAddr As Byte(), DestinationMacAddr As Byte())
                _Source_MacAddress = SourceMacAddr
                _Destination_MacAddress = DestinationMacAddr
                Get_Header_Copy()
            End Sub

        End Class
        Public Class C24_Header
            Private _NbBytes As Byte() = {&H0, &H0}
            Private _CheckSum As Byte() = {&H0, &H0}
            Private _SendCounter As Byte() = {&H0, &H0, &H0, &H0}
            Private _ReceivedCounter As Byte() = {&H0, &H0, &H0, &H0}
            Private _Retry As Byte() = {&H0, &H0}
            Private _CmdType As Byte() = {&H0}
            Private _CmdNb As Byte() = {&H0}
            Private _C24Header As Byte()
            Private _SendCounterInt As Integer

            Public ReadOnly Property C24Header As Byte()
                Get
                    Return _C24Header
                End Get
            End Property

            Public Property NbBytes As Byte()
                Get
                    Return _NbBytes
                End Get
                Set
                    _NbBytes = Value
                End Set

            End Property

            Public Property CheckSum As Byte()
                Get
                    Return _CheckSum
                End Get
                Set
                    _CheckSum = Value
                End Set

            End Property

            Public Property SendCounter As Byte()
                Get
                    Return _SendCounter
                End Get
                Set
                    _SendCounter = Value
                End Set

            End Property
            Public Property SendCounterInt As Integer
                Get
                    Return _SendCounterInt
                End Get
                Set
                    _SendCounterInt = Value
                End Set
            End Property

            Public Property ReceivedCounter As Byte()
                Get
                    Return _ReceivedCounter
                End Get
                Set
                    _ReceivedCounter = Value
                End Set

            End Property

            Public Property Retry As Byte()
                Get
                    Return _Retry
                End Get
                Set
                    _Retry = Value

                End Set

            End Property

            Public Property CmdType As Byte()
                Get
                    Return _CmdType
                End Get
                Set
                    _CmdType = Value
                End Set


            End Property

            Public Property CmdNb As Byte()
                Get
                    Return _CmdNb
                End Get
                Set
                    _CmdNb = Value
                End Set

            End Property
            Sub New()

            End Sub
            Public Sub New(C24Header As Byte())

                _NbBytes = ByteManager.SliceBytes(C24Header, 0, 2)
                _CheckSum = ByteManager.SliceBytes(C24Header, 2, 2)
                _SendCounter = ByteManager.SliceBytes(C24Header, 4, 4)
                _ReceivedCounter = ByteManager.SliceBytes(C24Header, 8, 4)
                _Retry = ByteManager.SliceBytes(C24Header, 12, 2)
                _CmdType = ByteManager.SliceBytes(C24Header, 14, 1)
                _CmdNb = ByteManager.SliceBytes(C24Header, 15, 1)
                Get_C4Header_Copy()
            End Sub

            Public Function Get_C4Header_Copy() As Byte()
                _C24Header = ByteManager.ConcatBytes(_NbBytes, _CheckSum, _SendCounter, _ReceivedCounter, _Retry, _CmdType, _CmdNb)
                Return _C24Header
            End Function

        End Class
        Public Enum CmdType As Byte
            Command = &H0
            Ack = &HA0
            Init = &HE2
            KeepAlive = &HE1
        End Enum
        Public Class C24_Packet
            Private _Cmd_Packet As Byte()
            Private _C24Header As C24_Header
            Private _EthHeader As Eth_Header
            Private _C24Packet As Byte()

            ReadOnly Property C24Packet As Byte()
                Get
                    Return _C24Packet
                End Get
            End Property

            ReadOnly Property EthHeader As Eth_Header
                Get
                    Return _EthHeader
                End Get
            End Property
            ReadOnly Property C24Header As C24_Header
                Get
                    Return _C24Header
                End Get
            End Property

            ReadOnly Property Cmd_Packet As Byte()
                Get
                    Return _Cmd_Packet
                End Get
            End Property

            Sub New(All_Packet_Bytes As Byte())

                _EthHeader = New Eth_Header(ByteManager.SliceBytes(All_Packet_Bytes, 0, 14))
                _C24Header = New C24_Header(ByteManager.SliceBytes(All_Packet_Bytes, 14, 16))
                _Cmd_Packet = ByteManager.SliceBytes(All_Packet_Bytes, 30, All_Packet_Bytes.Length - 30)
                _C24Packet = All_Packet_Bytes
            End Sub

            Sub New(Eth_Header As Byte(), C24Header_SendCounter As Integer, CmdType As CmdType, Cmds As List(Of Byte()))
                _EthHeader = New Eth_Header(Eth_Header)
                'building C24Header
                _C24Header = New C24_Header()
                _C24Header.CmdType = {CByte(CmdType)}
                _C24Header.SendCounter = ByteManager.UInt32ToBytesLErev(C24Header_SendCounter)

                If Cmds Is Nothing OrElse Cmds.Count = 0 Then
                    _C24Header.NbBytes = {&H0, CByte(16)}
                    _C24Header.CmdNb = {&H0}
                Else

                    _Cmd_Packet = ByteManager.ConcatBytes(Cmds.ToArray)
                    _C24Header.NbBytes = ByteManager.UInt16ToBytesLErev(16 + _Cmd_Packet.Count)
                    _C24Header.CmdNb = {CByte(Cmds.Count)}   ' ✅ 1 byte
                    _C24Header.CheckSum = {&H0, &H0} 'ByteManager.CalcChecksum(_Cmd_Packet)

                End If
                RebuildPacket()


            End Sub

            Public Sub RebuildPacket()
                _C24Packet = ByteManager.ConcatBytes(
                    _EthHeader.EthHeader,
                    _C24Header.Get_C4Header_Copy(),   ' ← reconstruit les bytes du header
                    If(_Cmd_Packet IsNot Nothing, _Cmd_Packet, Array.Empty(Of Byte)()))
            End Sub

        End Class
        Public Class AckC24_Packet
            Private ReadOnly _C24Header As Byte() = {&H0, &H10, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &HA0, &H0}
            Private ReadOnly _AckPacket As Byte()

            Public ReadOnly Property AckPacket As Byte()
                Get
                    Return _AckPacket
                End Get
            End Property

            Public Sub New(EthHeader As Byte())
                ' ✅ Minimum Ethernet : 14 (header) + 46 (payload) = 60 bytes
                ' Le C24 pad aussi ses ACKs à 60 bytes (visible frame 452 du log)
                Const MinEthernetPayload As Integer = 46
                Dim payloadSize As Integer = Math.Max(MinEthernetPayload, _C24Header.Length)   ' = 46

                _AckPacket = New Byte(EthHeader.Length + payloadSize - 1) {}   ' 60 bytes, zéro-paddé par default
                Buffer.BlockCopy(EthHeader, 0, _AckPacket, 0, EthHeader.Length)
                Buffer.BlockCopy(_C24Header, 0, _AckPacket, EthHeader.Length, _C24Header.Length)
                ' Bytes 30-59 : padding zéro automatique (New Byte) ✅
            End Sub

            Public Function Get_Ack(pktData As Byte(), sendCounterOffset As Integer) As Byte()
                ' ReceivedCounter toujours aux offsets 22-25 ✅ (dans les 16 premiers bytes de payload)
                _AckPacket(22) = pktData(sendCounterOffset)
                _AckPacket(23) = pktData(sendCounterOffset + 1)
                _AckPacket(24) = pktData(sendCounterOffset + 2)
                _AckPacket(25) = pktData(sendCounterOffset + 3)
                Return _AckPacket
            End Function
        End Class
        Public Class PacketManager
            Private _Sending_Eth_Header As Byte() = Nothing
            Private _Received_C24Packet As C24_Packet
            Private _Desk As Control24.Hardware.C24_Desk
            Private _IsInit As Boolean = False
            Private _AckReceived As Boolean = True
            Private _C24Header_SendCounter As Integer = 0
            Private _AckTimeoutBeforeRetry As Integer = 1000
            Private _Retry_Counter As Integer = 0
            Private _Max_Retry As Integer = 10
            Private _Send_Cmd_Bytes As List(Of Byte())
            Private _Max_PacketData_Length As Integer = 300
            Private _Pending_C24_Packets As Concurrent.ConcurrentQueue(Of C24_Packet) = New ConcurrentQueue(Of C24_Packet)
            Private Pending_C24_PacketsThread As Thread
            Private Pending_C24_Packet As C24_Packet = Nothing
            Private _AckC24_Packet As AckC24_Packet = Nothing
            Private _AckEvent As New AutoResetEvent(False)   ' ← signal d'ACK reçu
            Private _PendingPacketCounter As Integer = 0   ' ✅ counter du paquet EN ATTENTE d'ACK


            Property Max_PacketData_Length As Integer
                Get
                    Return _Max_PacketData_Length
                End Get
                Set
                    _Max_PacketData_Length = Value
                End Set
            End Property

            Property Max_Retry As Integer
                Get
                    Return _Max_Retry
                End Get
                Set
                    _Max_Retry = Value
                End Set
            End Property

            Property Retry_Counter As Integer
                Get
                    Return Interlocked.CompareExchange(_Retry_Counter, 0, 0)
                End Get
                Set
                    Interlocked.Exchange(_Retry_Counter, Value)
                End Set
            End Property

            Property AckTimeoutBeforeRetry As Integer
                Get
                    Return _AckTimeoutBeforeRetry
                End Get
                Set
                    _AckTimeoutBeforeRetry = Value

                End Set
            End Property



            ReadOnly Property C24Header_SendCounter As Integer
                Get
                    Return Interlocked.CompareExchange(_C24Header_SendCounter, 0, 0)   ' ✅ lecture atomique
                End Get
            End Property

            Public Property IsInit As Boolean
                Get
                    Return Threading.Volatile.Read(_IsInit)

                End Get
                Set
                    Threading.Volatile.Write(_IsInit, Value)
                End Set

            End Property

            ReadOnly Property Received_C24Packet As C24_Packet
                Get
                    Return _Received_C24Packet
                End Get

            End Property
            ReadOnly Property Send_Cmd_Bytes As List(Of Byte())
                Get
                    Return _Send_Cmd_Bytes
                End Get

            End Property

            ReadOnly Property Sending_Eth_Header As Byte()
                Get
                    Return _Sending_Eth_Header
                End Get
            End Property

            ReadOnly Property Desk As Control24.Hardware.C24_Desk
                Get
                    Return _Desk
                End Get
            End Property

            Property AckReceived As Boolean
                Get
                    Return Threading.Volatile.Read(_AckReceived)
                End Get
                Set
                    Threading.Volatile.Write(_AckReceived, Value)
                    If Value Then _AckEvent.Set()   ' ✅ réveille immédiatement la boucle
                End Set
            End Property


            Sub New(Desk As Control24.Hardware.C24_Desk)
                _Desk = Desk
                'Start Sending Timing Queue Thread

                Pending_C24_PacketsThread = New Thread(AddressOf Pending_C24_Packets_QueueLoop) With {
                        .Name = "Pending_C24_Packets_QueueLoop",
                .IsBackground = True,
                .Priority = ThreadPriority.Normal
                    }
                Pending_C24_PacketsThread.Start()


            End Sub

            'Gestion des retry
            ''' <summary>
            ''' Envoie les paquets en pipeline : ne bloque PAS l'envoi du paquet suivant
            ''' en attendant l'ACK. Le retry n'intervient que si la queue est vide et
            ''' que le dernier paquet n'a pas été acquitté dans les 50 ms.
            ''' Garantit que les commandes d'affichage (VuMeter, Clock) partent
            ''' toutes les 50 ms même sous charge jogwheel.
            ''' </summary>
            Private Sub Pending_C24_Packets_QueueLoop()
                While Desk.IsRunning
                    Dim nextPacket As C24_Packet = Nothing

                    If _Pending_C24_Packets.TryDequeue(nextPacket) Then
                        ' ── Pipeline : envoi immédiat sans attendre l'ACK du précédent ──
                        ' Une brève pause (5 ms max) évite de noyer le C24 si l'ACK
                        ' du paquet précédent n'est pas encore arrivé.
                        If Not AckReceived Then
                            _AckEvent.WaitOne(5)
                        End If

                        Retry_Counter = 0
                        Pending_C24_Packet = nextPacket
                        _PendingPacketCounter = nextPacket.C24Header.SendCounterInt
                        _AckEvent.Reset()
                        AckReceived = False
                        Desk.Send_Cmd_C24Packet(nextPacket.C24Packet)

                    ElseIf Pending_C24_Packet IsNot Nothing AndAlso Not AckReceived Then
                        ' ── Queue vide : attendre l'ACK du dernier paquet (50 ms max) ──
                        Dim ackInTime As Boolean = _AckEvent.WaitOne(50)
                        If ackInTime Then
                            Pending_C24_Packet = Nothing
                            Retry_Counter = 0
                        ElseIf Retry_Counter < Max_Retry Then
                            Interlocked.Increment(_Retry_Counter)
                            Pending_C24_Packet.C24Header.Retry = ByteManager.UInt16ToBytesLErev(Retry_Counter)
                            Pending_C24_Packet.RebuildPacket()
                            Desk.Send_Cmd_C24Packet(Pending_C24_Packet.C24Packet)
                        Else
                            ' ── Abandon après Max_Retry ───────────────────────────────
                            Pending_C24_Packet = Nothing
                            AckReceived = True
                            Retry_Counter = 0
                        End If
                    Else
                        Thread.Sleep(0)
                    End If
                End While
            End Sub
            Public Sub _ReceiveBytes(ReceivedBytes As Byte())
                If ReceivedBytes Is Nothing OrElse ReceivedBytes.Length < 30 Then Return

                Dim cmdType As Byte = ReceivedBytes(28)

                If cmdType = CByte(Packet.CmdType.Command) AndAlso _AckC24_Packet IsNot Nothing Then
                    _Desk.ReceivedC24PacketQueue.Enqueue(New C24_Packet(ReceivedBytes))

                ElseIf cmdType = CByte(Packet.CmdType.Ack) Then
                    Dim receivedCounter = CInt(ByteManager.BytesLEToUInt32(ReceivedBytes.AsSpan(22, 4).ToArray()))
                    If receivedCounter = _PendingPacketCounter Then   ' ✅ compare contre le paquet ENVOYÉ, pas le counter global
                        AckReceived = True
                        Retry_Counter = 0
                        If Not IsInit Then
                            IsInit = True
                            Task.Run(Sub() _Desk.InitializeComponents())
                        End If
                    End If

                ElseIf ReceivedBytes(0) = &HFF AndAlso ReceivedBytes(1) = &HFF AndAlso
               ReceivedBytes(2) = &HFF AndAlso ReceivedBytes(3) = &HFF AndAlso
               ReceivedBytes(4) = &HFF AndAlso ReceivedBytes(5) = &HFF AndAlso
               Not IsInit Then
                    Dim ethHdr = New Eth_Header(_Desk.ComputerMacAddr, ReceivedBytes.AsSpan(6, 6).ToArray())
                    _Sending_Eth_Header = ethHdr.Get_Header_Copy()
                    _AckC24_Packet = New AckC24_Packet(_Sending_Eth_Header)
                    _Desk.PacketCapture.SetAckBuffer(_AckC24_Packet.AckPacket)
                    Dim initPacket As New C24_Packet(_Sending_Eth_Header, C24Header_SendCounter, Packet.CmdType.Init, Nothing)
                    Send_Cmd_C24Packet(initPacket)
                End If
            End Sub

            ''' <summary>
            ''' Entering point for all received C24 Packets
            ''' </summary>
            ''' <param name="Received_C24Packet"></param>
            Public Sub ReceiveBytes(ReceivedBytes As Byte())
                _Received_C24Packet = New C24_Packet(ReceivedBytes)

                'Commands
                If _Received_C24Packet.C24Header.CmdType(0) = CByte(CmdType.Command) AndAlso _AckC24_Packet IsNot Nothing Then
                    'Cmd received from C24 --> ultra fast ACK send directinject via PcapCapture
                    Send_Ack_C24Packet(_AckC24_Packet.Get_Ack(ReceivedBytes, 18))

                    'Envois de toutes les commandes vers la queue qui va gérer le parsing et l'envois vers les components  
                    _Desk.ReceivedC24PacketQueue.Enqueue(_Received_C24Packet)


                    'Broadcast --> init sequence
                ElseIf _Received_C24Packet.EthHeader.IsBroadcast AndAlso _IsInit = False Then
                    Dim _Eth_Header = New Eth_Header(_Desk.ComputerMacAddr, _Received_C24Packet.EthHeader.Source_MacAddress)
                    _Sending_Eth_Header = _Eth_Header.Get_Header_Copy()
                    _AckC24_Packet = New AckC24_Packet(_Sending_Eth_Header)

                    Dim init_C24Packet As New C24_Packet(Sending_Eth_Header, C24Header_SendCounter, CmdType.Init, Nothing)
                    Dim init_str = BitConverter.ToString(init_C24Packet.C24Packet)
                    Send_Cmd_C24Packet(init_C24Packet)
                    Interlocked.Exchange(_C24Header_SendCounter, 1)

                    ' ACK reçu
                ElseIf _Received_C24Packet.C24Header.CmdType(0) = CByte(CmdType.Ack) Then
                    If Not _IsInit Then              ' ✅ garde-fou supplémentaire
                        _IsInit = True
                        Task.Run(Sub() _Desk.InitializeComponents())   ' ✅ hors thread capture
                    End If

                    AckReceived = True
                    Retry_Counter = 0
                    'ByteManager.BytesLEToUInt32(_Received_C24Packet.C24Header.ReceivedCounter) = C24Header_SendCounter
                End If
            End Sub
            ''' <summary>
            ''' Entering point for sending Cmds as list of Byte() with Max_Datalenght Limit
            ''' </summary>
            ''' <param name="Cmds_Bytes"></param>
            Public Sub SendCmdBytes(Cmds_Bytes As List(Of Byte()))
                _Send_Cmd_Bytes = Cmds_Bytes
                If _IsInit AndAlso Sending_Eth_Header IsNot Nothing Then
                    Dim Cmd_to_send As New List(Of Byte())
                    Dim Cmd_to_send_Length As Integer = 0
                    For i = 0 To Cmds_Bytes.Count - 1
                        Dim Cmd = Cmds_Bytes(i)
                        Cmd_to_send.Add(Cmd)
                        Cmd_to_send_Length += Cmd.Length
                        Dim Cmd_to_send_Length_to_Test = Cmd_to_send_Length
                        If i < Cmds_Bytes.Count - 1 Then
                            Cmd_to_send_Length_to_Test += Cmds_Bytes(i + 1).Length
                        End If
                        If Cmd_to_send_Length_to_Test >= Max_PacketData_Length OrElse i = Cmds_Bytes.Count - 1 Then

                            Dim _C24Packet As New C24_Packet(Sending_Eth_Header, C24Header_SendCounter, CmdType.Command, Cmd_to_send)
                            Send_Cmd_C24Packet(_C24Packet)
                            Cmd_to_send = New List(Of Byte())
                            Cmd_to_send_Length = 0

                        End If
                    Next

                End If
            End Sub
            ''' <summary>
            ''' Entering point for sending Packet
            ''' </summary>
            ''' <param name="Cmd_C24Packet"></param>
            Public Sub Send_Cmd_C24Packet(Cmd_C24Packet As C24_Packet)
                ' ✅ Incrémenter atomiquement de N (nombre de commandes du paquet)
                Dim cmdCount As Integer = Math.Max(1, CInt(Cmd_C24Packet.C24Header.CmdNb(0)))
                Dim counter = Interlocked.Add(_C24Header_SendCounter, cmdCount)
                Cmd_C24Packet.C24Header.SendCounterInt = counter
                Cmd_C24Packet.C24Header.SendCounter = ByteManager.UInt32ToBytesLErev(counter)
                Cmd_C24Packet.RebuildPacket()
                _Pending_C24_Packets.Enqueue(Cmd_C24Packet)
            End Sub


            Public Sub Send_Ack_C24Packet(AckC24Packet As Byte())
                _Desk.PacketCapture.SendAckPacket(AckC24Packet)  ' ✅ handle dédié → 0 attente
            End Sub                                       '    0 queue, 0 context switch → ~0.1ms
            ''' <summary>
            ''' Entering Point for receiving packets
            ''' </summary>
            ''' <param name="_C24_Packet"></param>
            Public Sub ReceivedC24Packet(_C24_Packet As C24_Packet)
                Dim ParsedCmds = Control24.Commands.CmdParser.Parse(_C24_Packet.Cmd_Packet)
                For Each Kv In ParsedCmds
                    Dim component As Hardware.C24_Component = Nothing
                    If Desk.IdsToC24Components.TryGetValue(Kv.Key, component) Then
                        If component._C24_ReceivingCommand IsNot Nothing Then
                            component._C24_ReceivingCommand.Cmd = Kv.Cmd
                        End If
                    End If
                Next
            End Sub


        End Class


    End Namespace

    Namespace Commands

        Public Enum CmdType
            ButtonType = &H90
            DisplayType = &HF0
            ActuatorType = &HB0
        End Enum

        Public MustInherit Class Cmd
            Public _Cmd As Byte()
            Private _Cmd_Id As String
            Public Event OnCmdChange()
            Public Event NeedToSendCmd(Cmd As Cmd)

            Public Property Cmd As Byte()
                Get
                    Return _Cmd
                End Get
                Set
                    _Cmd = Value
                    RaiseEvent OnCmdChange()
                End Set
            End Property

            Public ReadOnly Property Cmd_Id As String
                Get
                    Return _Cmd_Id
                End Get

            End Property


            Public Sub SetCmdId(Mask_Id As Byte())
                _Cmd_Id = CmdParser.MaskedKey(Cmd, Mask_Id)


            End Sub
            Public Sub SendCmd()
                RaiseEvent NeedToSendCmd(Me)
            End Sub
        End Class
        Public MustInherit Class Button_Cmd
            Inherits Cmd

            Private ReadOnly _Button As Byte() = {&H90, &H0, &H0}
            Private ReadOnly _ButtonMask As Byte() = {&HFF, &H0, &H0}
            Private ReadOnly _ButtonNbMask As Byte() = {&H0, &HFF, &H0}
            Private _Is_On As Boolean
            Private _ButtonNb As Byte
            Public Event OnButtonStatusChange(Status As Boolean)

            Public Property ButtonNb As Byte
                Get
                    Return _ButtonNb
                End Get
                Set
                    _ButtonNb = Value
                    SetButtonNb()
                End Set
            End Property

            Public ReadOnly Property ButtonNbMask As Byte()
                Get
                    Return _ButtonNbMask
                End Get
            End Property

            Public ReadOnly Property Button As Byte()
                Get
                    Return _Button
                End Get
            End Property
            Public ReadOnly Property ButtonMask As Byte()
                Get
                    Return _ButtonMask
                End Get
            End Property

            Public Property Is_On As Boolean
                Get
                    Return _Is_On
                End Get
                Set
                    _Is_On = Value
                    SetButtonStatus()
                End Set
            End Property

            Private Sub readButtonStatus() Handles MyBase.OnCmdChange
                If (Cmd(2) And &H40) = &H40 Then
                    Is_On = True
                Else
                    Is_On = False
                End If
                RaiseEvent OnButtonStatusChange(Is_On)
            End Sub
            Private Sub SetButtonStatus()
                If Is_On Then
                    Cmd(2) = Cmd(2) Or &H40
                Else
                    Cmd(2) = Cmd(2) And Not CByte(&H40)
                End If
            End Sub
            Private Sub SetButtonNb()
                Cmd(1) = ButtonNb
                SendCmd()
            End Sub
            Private Sub ReadButtonId() Handles MyBase.OnCmdChange
                _ButtonNb = Cmd(1)
            End Sub

        End Class
        Public Class Track_Button_Cmd
            Inherits Button_Cmd

            Private ReadOnly _TrackMask As Byte() = {&H0, &H0, &H1F}
            Private _Track_Number As Integer
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &H1F}

            ReadOnly Property TrackMask As Byte()
                Get
                    Return _TrackMask
                End Get
            End Property

            Public Property Track_Number As Integer
                Get
                    Return _Track_Number
                End Get
                Set
                    _Track_Number = Value
                    Set_TrackNb()
                End Set
            End Property

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property

            Private Sub Read_TrackNb() Handles MyBase.OnCmdChange
                _Track_Number = CInt(Cmd(2) And TrackMask(2)) + 1

            End Sub

            Private Sub Set_TrackNb()
                Cmd(2) = CByte((Cmd(2) And Not TrackMask(2)) Or (CByte(_Track_Number - 1) And TrackMask(2)))
            End Sub

            Sub New(Button_Nb As Byte, Track_Nb As Integer)
                'set defaut cmd
                Cmd = Button
                ButtonNb = Button_Nb
                Track_Number = Track_Nb
                Is_On = False
                'set Id                        

                SetCmdId(Mask_Id)
            End Sub
        End Class
        Public Enum Btn_Region
            Left_Strip = &H18
            Navigation = &H19
            Num_Keyboard = &H1A
            Edit = &H1B
            Transport = &H1C
            Middle_Strip = &H1D
        End Enum
        Public Class Region_Button_Cmd
            Inherits Button_Cmd

            Private ReadOnly _RegionMask As Byte() = {&H0, &H0, &H1F}
            Private _Region As Btn_Region
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &H1F}

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property

            ReadOnly Property RegionMask As Byte()
                Get
                    Return _RegionMask
                End Get
            End Property
            Public Property Region As Btn_Region
                Get
                    Return _Region
                End Get
                Set
                    _Region = Value
                    SetRegion()
                End Set
            End Property
            Private Sub ReadRegion() Handles MyBase.OnCmdChange
                _Region = Cmd(2) And RegionMask(2)
            End Sub
            Private Sub SetRegion()
                Cmd(2) = CByte((Cmd(2) And Not RegionMask(2)) Or (Region And RegionMask(2)))
            End Sub
            Sub New(Button_Region As Btn_Region, Button_Nb As Byte)
                Cmd = Button
                Region = Button_Region
                ButtonNb = Button_Nb
                Is_On = False
                'set Id             

                SetCmdId(Mask_Id)
            End Sub


        End Class
        Public Enum DisplayCommands
            VpotLed = &H0
            VueMeter = &H10
            AutoModeLed = &H20
            Clock = &H30
            ScribbleStrip = &H40
        End Enum
        Public MustInherit Class Track_Display_Cmd
            Inherits Cmd

            Private ReadOnly _Display_Header As Byte() = {&HF0, &H13, &H1}
            Private ReadOnly _Display_Ender As Byte = &HF7
            Private _DisplayCommand As DisplayCommands
            Private _Track_Number As Integer



            Public Property DisplayCommand As DisplayCommands
                Get

                    Return _DisplayCommand
                End Get
                Set
                    _DisplayCommand = Value
                    SetDisplayCommand()
                End Set
            End Property

            Public ReadOnly Property Display_Header As Byte()
                Get
                    Return _Display_Header
                End Get
            End Property
            Public ReadOnly Property Display_Ender As Byte
                Get
                    Return _Display_Ender
                End Get
            End Property
            Public Property Track_Number As Integer
                Get
                    Return _Track_Number
                End Get
                Set
                    _Track_Number = Value
                    Set_TrackNb()
                End Set
            End Property

            Private Sub Read_TrackNb() Handles MyBase.OnCmdChange
                _Track_Number = CInt(Cmd(4) And &H1F) + 1

            End Sub

            Private Sub Set_TrackNb()
                Cmd(4) = CByte((Cmd(4) And Not CByte(&H1F)) Or (CByte(_Track_Number - 1) And &H1F))
            End Sub

            Private Sub ReadDisplayCommand() Handles MyBase.OnCmdChange
                _DisplayCommand = Cmd(3)
            End Sub
            Private Sub SetDisplayCommand()
                Cmd(3) = _DisplayCommand
            End Sub


        End Class
        Public Class VpotLed_Cmd
            Inherits Track_Display_Cmd

            Private _Leds_Status As BitArray = New BitArray({False, False, False, False, False, False, False, False, False, False, False, False, False, False, False})
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &HFF, &HFF, &H1F, &H0, &H0, &HFF}

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property
            Sub New(Track_Nb As Integer)
                'set defaut cmd
                Cmd = ByteManager.ConcatBytes(Display_Header, {&H0, &H0}, {&H0, &H0}, {Display_Ender})
                DisplayCommand = DisplayCommands.VpotLed
                Track_Number = Track_Nb
                Leds_Status = _Leds_Status
                'set Id             

                SetCmdId(Mask_Id)
            End Sub

            Property Leds_Status As BitArray
                Get
                    Return _Leds_Status
                End Get
                Set

                    _Leds_Status = Value
                    SetLeds_Status()
                End Set
            End Property

            Private Sub ReadLeds_Status() Handles MyBase.OnCmdChange
                If Cmd Is Nothing OrElse Cmd.Length < 8 Then Return

                ' DisplayDatas = {Cmd[4], Cmd[5], Cmd[6]} positionné par SetLeds_Status
                ' Cmd[4] = Cmd(4) | LED0        → bit 6
                ' Cmd[5] = LEDs 1-7             → bits 6:0
                ' Cmd[6] = LEDs 8-14            → bits 6:0
                Dim d0 As Byte = Cmd(4)
                Dim d1 As Byte = Cmd(5)
                Dim d2 As Byte = Cmd(6)

                _Leds_Status = New BitArray(New Boolean() {
                    (d0 And &H40) <> 0,   ' LED 0  → byte[0] bit 6
                    (d1 And &H40) <> 0,   ' LED 1  → byte[1] bit 6
                    (d1 And &H20) <> 0,   ' LED 2  → byte[1] bit 5
                    (d1 And &H10) <> 0,   ' LED 3  → byte[1] bit 4
                    (d1 And &H8) <> 0,   ' LED 4  → byte[1] bit 3
                    (d1 And &H4) <> 0,   ' LED 5  → byte[1] bit 2
                    (d1 And &H2) <> 0,   ' LED 6  → byte[1] bit 1
                    (d1 And &H1) <> 0,   ' LED 7  → byte[1] bit 0
                    (d2 And &H40) <> 0,   ' LED 8  → byte[2] bit 6
                    (d2 And &H20) <> 0,   ' LED 9  → byte[2] bit 5
                    (d2 And &H10) <> 0,   ' LED 10 → byte[2] bit 4
                    (d2 And &H8) <> 0,   ' LED 11 → byte[2] bit 3
                    (d2 And &H4) <> 0,   ' LED 12 → byte[2] bit 2
                    (d2 And &H2) <> 0,   ' LED 13 → byte[2] bit 1
                    (d2 And &H1) <> 0    ' LED 14 → byte[2] bit 0
                })
            End Sub
            Private Sub SetLeds_Status()
                Dim d0 As Byte = CByte(Cmd(4) And &H1F) ' Conserve les bits du numéro de piste
                Dim d1 As Byte = 0
                Dim d2 As Byte = 0

                ' LED 0 → byte[0] bit 6
                If _Leds_Status(0) Then d0 = d0 Or &H40

                ' LEDs 1-7 → byte[1] bits 6:0
                For i = 1 To 7
                    If _Leds_Status(i) Then
                        d1 = d1 Or CByte(CInt(&H40) >> (i - 1))
                    End If
                Next

                ' LEDs 8-14 → byte[2] bits 6:0
                For i = 8 To 14
                    If _Leds_Status(i) Then
                        d2 = d2 Or CByte(CInt(&H40) >> (i - 8))
                    End If
                Next

                Cmd(4) = d0
                Cmd(5) = d1
                Cmd(6) = d2
                SendCmd()
            End Sub


        End Class
        Public Enum StereoChannel
            Left = &H0
            Right = &H20
        End Enum
        Public Class VueMeter_Cmd
            Inherits Track_Display_Cmd
            Private _Leds_Status As BitArray = New BitArray({False, False, False, False, False, False, False, False, False, False, False, False, False, False})
            Private _Channel As StereoChannel
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &HFF, &HFF, &H1F, &H0, &H0, &HFF}

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property
            Sub New(Track_Nb As Integer)
                'set defaut cmd
                Cmd = ByteManager.ConcatBytes(Display_Header, {&H0, &H0}, {&H0, &H0}, {Display_Ender})
                DisplayCommand = DisplayCommands.VueMeter
                Track_Number = Track_Nb
                Leds_Status = _Leds_Status
                'set Id             

                SetCmdId(Mask_Id)
            End Sub
            Property Leds_Status As BitArray
                Get
                    Return _Leds_Status
                End Get
                Set
                    _Leds_Status = Value
                    SetLeds_Status()
                End Set
            End Property
            Private Sub ReadLeds_Status() Handles MyBase.OnCmdChange
                If Cmd Is Nothing OrElse Cmd.Length < 8 Then Return

                ' Inverse de SetLeds_Status :
                ' Cmd(5) = d1 = LEDs 7-13  → bits 6:0
                ' Cmd(6) = d2 = LEDs 0-6   → bits 6:0
                Dim d1 As Byte = Cmd(5)
                Dim d2 As Byte = Cmd(6)

                _Leds_Status = New BitArray(New Boolean() {
        (d2 And &H1) <> 0,   ' LED 0  → d2 bit 0
        (d2 And &H2) <> 0,   ' LED 1  → d2 bit 1
        (d2 And &H4) <> 0,   ' LED 2  → d2 bit 2
        (d2 And &H8) <> 0,   ' LED 3  → d2 bit 3
        (d2 And &H10) <> 0,   ' LED 4  → d2 bit 4
        (d2 And &H20) <> 0,   ' LED 5  → d2 bit 5
        (d2 And &H40) <> 0,   ' LED 6  → d2 bit 6
        (d1 And &H1) <> 0,   ' LED 7  → d1 bit 0
        (d1 And &H2) <> 0,   ' LED 8  → d1 bit 1
        (d1 And &H4) <> 0,   ' LED 9  → d1 bit 2
        (d1 And &H8) <> 0,   ' LED 10 → d1 bit 3
        (d1 And &H10) <> 0,   ' LED 11 → d1 bit 4
        (d1 And &H20) <> 0,   ' LED 12 → d1 bit 5
        (d1 And &H40) <> 0    ' LED 13 → d1 bit 6
    })
            End Sub
            Private Sub SetLeds_Status()
                Dim d0 As Byte = Cmd(4)   ' Conserve les bits du numéro de piste
                Dim d1 As Byte = 0
                Dim d2 As Byte = 0



                ' LEDs 0-6 → byte[2] bits 6:0
                For i = 0 To 6
                    If _Leds_Status(i) Then
                        d2 = d2 Or CByte(CInt(&H1) << i)
                    End If
                Next

                ' LEDs 7-14 → byte[2] bits 6:0
                For i = 7 To 13
                    If _Leds_Status(i) Then
                        d1 = d1 Or CByte(CInt(&H1) << (i - 7))
                    End If
                Next

                Cmd(4) = d0
                Cmd(5) = d1
                Cmd(6) = d2

                SendCmd()

            End Sub
            Public Property Channel As StereoChannel
                Get
                    Return _Channel
                End Get
                Set
                    _Channel = Value
                    SetChannel()
                End Set
            End Property

            Private Sub ReadChannel() Handles MyBase.OnCmdChange
                Channel = Cmd(4) And &H20
            End Sub
            Private Sub SetChannel()
                Cmd(4) = CByte((Cmd(4) And Not &H20) Or Channel)   ' ✅ clear + set
            End Sub
        End Class
        Public Enum AutoMode
            Write = &H40
            Touch = &H20
            Latch = &H10
            Trim = &H8
            Read = &H4
            None = &H0
            All = &H7C
        End Enum
        Public Class AutoModeLed_Cmd
            Inherits Track_Display_Cmd

            Private _AutoModeLed As AutoMode
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &HFF, &HFF, &H1F, &H0, &HFF}

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property
            Sub New(Track_Nb As Integer)
                'set defaut cmd
                Cmd = ByteManager.ConcatBytes(Display_Header, {&H0, &H0}, {&H0}, {Display_Ender})
                DisplayCommand = DisplayCommands.AutoModeLed
                Track_Number = Track_Nb
                AutoModeLed = AutoMode.None
                'set Id             

                SetCmdId(Mask_Id)
            End Sub

            Public Property AutoModeLed As AutoMode
                Get
                    Return _AutoModeLed
                End Get
                Set
                    _AutoModeLed = Value
                    SetAutomodeLed()
                End Set
            End Property

            Private Sub ReadAutoModeLed() Handles MyBase.OnCmdChange
                AutoModeLed = Cmd(5)
            End Sub

            Private Sub SetAutomodeLed()
                Cmd(5) = AutoModeLed
                SendCmd()
            End Sub
        End Class
        Public Enum ClockModes
            HourMinuteSecondMS
            HourMinuteSecondFrames
            FeetFrames
            BarBeats
            None
        End Enum
        Public Class ClockLed_Cmd
            Inherits Track_Display_Cmd
            Private ClockLedBytes = New Dictionary(Of ClockModes, Byte) From
                    {{ClockModes.HourMinuteSecondMS, &H40},
                    {ClockModes.HourMinuteSecondFrames, &H20},
                    {ClockModes.FeetFrames, &H10},
                    {ClockModes.BarBeats, &H8},
                    {ClockModes.None, &H0}
                    }
            Private ClockLedBytesRev As Dictionary(Of Byte, ClockModes) = Utility.ReverseDictionary(Of ClockModes, Byte)(ClockLedBytes)

            Private _ClockMode As ClockModes
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &HFF, &HFF, &HFF, &H0, &HFF}

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property
            Sub New()

                'set defaut cmd
                Cmd = ByteManager.ConcatBytes(Display_Header, {&H0}, {&H0}, {&H0}, {Display_Ender})
                DisplayCommand = DisplayCommands.AutoModeLed
                Track_Number = 26
                ClockMode = ClockModes.HourMinuteSecondMS
                'set Id             

                SetCmdId(Mask_Id)
            End Sub

            Public Property ClockMode As ClockModes
                Get
                    Return _ClockMode
                End Get
                Set
                    _ClockMode = Value
                    SetClockMode()
                End Set
            End Property

            Private Sub ReadClockMode() Handles MyBase.OnCmdChange
                _ClockMode = ClockLedBytesRev(Cmd(5))
            End Sub
            Private Sub SetClockMode()
                Cmd(5) = ClockLedBytes(ClockMode)
                SendCmd()
            End Sub
        End Class
        Public Class Clock_Cmd
            Inherits Track_Display_Cmd

            Private ClockModesDots As New Dictionary(Of ClockModes, Byte) From {
                {ClockModes.HourMinuteSecondMS, &H15},
                {ClockModes.HourMinuteSecondFrames, &H2A},
                {ClockModes.FeetFrames, &H8},
                {ClockModes.BarBeats, &H14}
            }
            Private ClockModesDotsRev As Dictionary(Of Byte, ClockModes) = Utility.ReverseDictionary(Of ClockModes, Byte)(ClockModesDots)

            Private ReadOnly SevenSeg As New Dictionary(Of Char, Byte) From {
                {"0"c, &H7E}, {"1"c, &H30}, {"2"c, &H6D}, {"3"c, &H79},
                {"4"c, &H33}, {"5"c, &H5B}, {"6"c, &H5F}, {"7"c, &H70},
                {"8"c, &H7F}, {"9"c, &H7B}, {"-"c, &H1}, {" "c, &H0},
                {"L"c, &HE}, {"h"c, &H17}, {"o"c, &H1D}, {"b"c, &H1F},
                {"H"c, &H37}, {"J"c, &H38}, {"Y"c, &H3B}, {"d"c, &H3D},
                {"U"c, &H3E}, {"R"c, &H46}, {"F"c, &H47}, {"C"c, &H4E},
                {"E"c, &H4F}, {"S"c, &H5B}, {"P"c, &H67}, {"Z"c, &H6D},
                {"A"c, &H77}
            }
            Private ReadOnly SevenSegRev As Dictionary(Of Byte, Char) = Utility.ReverseDictionary(Of Char, Byte)(SevenSeg)

            Private _ClockMode As ClockModes
            Private _Clock_Value As String

            ''' <summary>
            ''' Cache des 8 bytes seven-segment précédemment envoyés (index 0 = leftmost).
            ''' Initialisé à zéro (= 8 espaces) pour forcer l'envoi au premier appel.
            ''' </summary>
            Private _prevSevenSegFull As Byte() = New Byte(7) {}

            ''' <summary>Masque sur les 5 premiers bytes (F0 13 01 30 19) — ID unique du composant horloge.</summary>
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &HFF, &HFF, &HFF}

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property

            Sub New()
                ' Format minimal : F0 13 01 [cmd] [track] [mode] F7 (7 bytes, aucun digit)
                _Cmd = ByteManager.ConcatBytes(Display_Header, {&H0, &H0, &H0, Display_Ender})
                Me.DisplayCommand = DisplayCommands.Clock
                Track_Number = 26
                ClockMode = ClockModes.HourMinuteSecondMS
                SetCmdId(_Mask_Id)
            End Sub

            ''' <summary>Mode d'affichage (dots séparateurs).</summary>
            Public Property ClockMode As ClockModes
                Get
                    Return _ClockMode
                End Get
                Set
                    _ClockMode = Value
                    SetClockMode()
                End Set
            End Property

            Private Sub ReadClockMode() Handles MyBase.OnCmdChange
                If _Cmd Is Nothing OrElse _Cmd.Length < 6 Then Return
                Dim mode As ClockModes = ClockModes.None
                If ClockModesDotsRev.TryGetValue(_Cmd(5), mode) Then
                    _ClockMode = mode
                End If
            End Sub

            Private Sub SetClockMode()
                If _Cmd Is Nothing OrElse _Cmd.Length < 6 Then Return
                _Cmd(5) = ClockModesDots(ClockMode)

                ' ── Invalide le cache pour forcer le renvoi complet des 8 digits ──
                ' au prochain Set_Time_Ms, tous les digits seront inclus avec le nouveau mode.
                Array.Clear(_prevSevenSegFull, 0, _prevSevenSegFull.Length)

                SendCmd()
            End Sub

            ''' <summary>Valeur texte affichée sur les 8 digits 7-segments.</summary>
            Public Property Clock_Value As String
                Get
                    Return _Clock_Value
                End Get
                Set
                    _Clock_Value = Value

                    SetClockValue()
                End Set
            End Property

            ''' <summary>
            ''' Relit la valeur depuis un Cmd reçu (variable-length, digits right-to-left à partir de Cmd[6]).
            ''' </summary>
            Private Sub ReadClockValue() Handles MyBase.OnCmdChange
                If _Cmd Is Nothing OrElse _Cmd.Length < 7 Then Return

                Dim digitCount As Integer = _Cmd.Length - 7
                Dim chars(7) As Char
                For i = 0 To 7
                    chars(i) = " "c
                Next

                For i = 0 To Math.Min(digitCount, 8) - 1
                    Dim b As Byte = _Cmd(6 + i)
                    Dim c As Char = " "c
                    If SevenSegRev.ContainsKey(b) Then
                        c = SevenSegRev(b)
                    End If
                    chars(7 - i) = c
                Next

                _Clock_Value = New String(chars)
            End Sub

            ''' <summary>
            ''' Construit et envoie un Cmd de longueur minimale.
            ''' Format : F0 13 01 30 [track] [mode] [d_rightmost .. d_firstChanged] F7.
            ''' <para>
            ''' Mémorisation : compare les 8 bytes seven-seg avec le dernier envoi.
            ''' Seule la tranche [firstChangedIndex..7] (de droite à gauche) est envoyée,
            ''' ce qui évite de rafraîchir les digits stables et réduit le clignotement.
            ''' </para>
            ''' </summary>
            Private Sub SetClockValue()
                _Clock_Value = _Clock_Value.PadLeft(8)
                Dim charValues = _Clock_Value.ToCharArray()

                ' ── Calcule la représentation complète sur 8 positions ─────────
                Dim newFull(7) As Byte
                For i = 0 To 7
                    Dim c As Char = charValues(i)
                    If SevenSeg.ContainsKey(c) Then
                        newFull(i) = SevenSeg(c)
                    Else
                        newFull(i) = &H0
                    End If
                Next

                ' ── Trouve le premier byte changé depuis la gauche ────────────
                Dim firstChanged As Integer = -1
                For i = 0 To 7
                    If newFull(i) <> _prevSevenSegFull(i) Then
                        firstChanged = i
                        Exit For
                    End If
                Next

                ' ── Rien n'a changé : pas d'envoi ────────────────────────────
                If firstChanged = -1 Then Return

                ' ── Met à jour le cache ───────────────────────────────────────
                Array.Copy(newFull, _prevSevenSegFull, 8)

                ' ── Collecte les bytes de droite à gauche jusqu'à firstChanged ─
                ' Exemple "      43" (ancien "      42") : firstChanged=7 → 1 seul byte [79]
                ' Exemple "      42" (ancien "    1234") : firstChanged=4 → 4 bytes [6D,33,00,00]
                Dim digitBytes As New List(Of Byte)
                For i = 7 To firstChanged Step -1
                    digitBytes.Add(newFull(i))
                Next

                ' ── Reconstruction du Cmd variable ────────────────────────────
                Dim parts As New List(Of Byte())
                parts.Add(Display_Header)
                parts.Add({_Cmd(3), _Cmd(4), _Cmd(5)})
                parts.Add(digitBytes.ToArray())
                parts.Add({Display_Ender})

                _Cmd = ByteManager.ConcatBytes(parts.ToArray())
                SendCmd()
            End Sub

        End Class
        Public Class ScribbleStrip_Cmd
            Inherits Track_Display_Cmd

            Private _Text As String
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &HFF, &HFF, &HFF, &HFF, &H0, &H0, &H0, &H0, &HFF}
            Private _Value As Double
            Private _LastText As String
            Private _LastTextDoubleValue As String

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property
            Public ReadOnly Property LastTextDoubleValue As String
                Get
                    Return _LastTextDoubleValue
                End Get
            End Property

            Sub New(Track_Nb As Integer)

                'set defaut cmd

                Cmd = ByteManager.ConcatBytes(Display_Header, {&H0, &H0}, {&H0}, {&H0, &H0, &H0, &H0}, {Display_Ender})
                DisplayCommand = DisplayCommands.ScribbleStrip
                Track_Number = Track_Nb

                'set Id             

                SetCmdId(Mask_Id)
            End Sub

            Public Property Text As String
                Get
                    Return _Text
                End Get
                Set
                    _Text = Value
                    _LastText = Value
                    SetTextValue()
                End Set
            End Property
            Public Property Value As Double
                Get
                    Return _Value
                End Get
                Set
                    _Value = Value
                    Text = Hardware.Utility.GetScribbleTextfromDouble(Value)
                    _LastTextDoubleValue = Text
                End Set
            End Property

            Private Sub ReadScribbleValue() Handles MyBase.OnCmdChange
                _Text = New String(System.Text.ASCIIEncoding.ASCII.GetChars({Cmd(6), Cmd(7), Cmd(8), Cmd(9)}))
            End Sub

            Private Sub SetTextValue()
                _Text = _Text.PadRight(4).Substring(0, 4)

                Dim _Scribble_Bytes = System.Text.ASCIIEncoding.ASCII.GetBytes(_Text)
                Cmd(6) = _Scribble_Bytes(0)
                Cmd(7) = _Scribble_Bytes(1)
                Cmd(8) = _Scribble_Bytes(2)
                Cmd(9) = _Scribble_Bytes(3)
                SendCmd()
            End Sub

        End Class
        Public MustInherit Class Track_VariableActuator_Cmd
            Inherits Cmd

            Private ReadOnly _VariableActuator_Header As Byte() = {&HB0}
            Private _Track_Number As Integer

            Public ReadOnly Property VariableActuator_Header As Byte()
                Get
                    Return _VariableActuator_Header
                End Get
            End Property

            Public Property Track_Number As Integer
                Get
                    Return _Track_Number
                End Get
                Set
                    _Track_Number = Value
                    SetTrackNumber()
                End Set
            End Property

            Public Overridable Sub ReadTrackNumber() Handles MyBase.OnCmdChange
                _Track_Number = CInt(Cmd(1) And &H1F) + 1
            End Sub

            Public Overridable Sub SetTrackNumber()
                Cmd(1) = CByte((Cmd(1) And Not CByte(&H1F)) Or (CByte(_Track_Number - 1) And &H1F))   ' ✅
            End Sub
        End Class
        Public Enum Direction
            Right = &H40
            Left = &H0
        End Enum
        Public MustInherit Class Track_Rotary_Cmd
            Inherits Track_VariableActuator_Cmd

            Private _Rotate_Direction As Direction
            Private _Rotate_Velocity As Integer

            Public Property Rotate_Direction As Direction
                Get
                    Return _Rotate_Direction
                End Get
                Set
                    _Rotate_Direction = Value
                    SetRotateDirection()
                End Set
            End Property
            Private Sub ReadRotateDirection() Handles MyBase.OnCmdChange
                _Rotate_Direction = Cmd(2) And &HC0

            End Sub
            Private Sub SetRotateDirection()
                Cmd(2) = CByte((Cmd(2) And Not CByte(&HC0)) Or (_Rotate_Direction And &HC0))   ' ✅
                SendCmd()
            End Sub

            Public Property Rotate_Velocity As Integer
                Get
                    Return _Rotate_Velocity
                End Get
                Set
                    _Rotate_Velocity = Value
                    SetRotateVelocity()
                End Set
            End Property

            Private Sub ReadRotateVelocity() Handles MyBase.OnCmdChange
                _Rotate_Velocity = CInt(Cmd(2) - &H40)
            End Sub
            Private Sub SetRotateVelocity()
                If _Rotate_Velocity >= 0 Then
                    _Rotate_Direction = Direction.Right
                Else
                    _Rotate_Direction = Direction.Left
                End If
                Cmd(2) = CByte((Cmd(2) And Not CByte(&HC0)) Or (_Rotate_Direction And &HC0))
                Cmd(2) = CByte(Math.Abs(_Rotate_Velocity)) + &H40
                SendCmd()
            End Sub
        End Class
        Public Class Vpot_Cmd
            Inherits Track_Rotary_Cmd
            Shared _Mask_Id As Byte() = {&HFF, &H1F, &H0}

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property
            Sub New(Track_Nb As Integer)

                'set defaut cmd
                Cmd = ByteManager.ConcatBytes(VariableActuator_Header, {&H0}, {&H0})
                Track_Number = Track_Nb
                'set Id             


                SetCmdId(Mask_Id)
            End Sub


        End Class
        Public Class JogWheel_Cmd
            Inherits Track_Rotary_Cmd
            Shared _Mask_Id As Byte() = {&HFF, &H1F, &H0}

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property
            Sub New()

                'set defaut cmd
                Cmd = ByteManager.ConcatBytes(VariableActuator_Header, {&H40}, {&H0})
                Track_Number = 29
                'set Id             


                SetCmdId(Mask_Id)
            End Sub
        End Class
        Public Class Fader_Cmd
            Inherits Track_VariableActuator_Cmd

            Private _FaderValue As Integer
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &H0, &HFF, &H0}

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property
            Sub New(Track_Nb As Integer)

                'set defaut cmd
                Cmd = ByteManager.ConcatBytes(VariableActuator_Header, {&H0}, {&H0}, {&H0}, {&H0})
                Track_Number = Track_Nb
                'set Id             


                SetCmdId(Mask_Id)
            End Sub

            Public Overrides Sub ReadTrackNumber()
                MyBase.ReadTrackNumber()
            End Sub
            Public Overrides Sub SetTrackNumber()
                MyBase.SetTrackNumber()
                Cmd(3) = &H20 + Cmd(1)
            End Sub
            ''' <summary>
            ''' Fader value from 0 to 1023
            ''' </summary>
            ''' <returns></returns>
            Public Property FaderValue As Integer
                Get
                    Return _FaderValue
                End Get
                Set
                    _FaderValue = Value
                    SetFaderValue()
                    'Console.WriteLine("Set:" & Value)
                End Set
            End Property

            Private Sub ReadFaderValue() Handles MyBase.OnCmdChange
                _FaderValue = (CInt(Cmd(2)) << 3) Or (CInt(Cmd(4)) >> 4)
                ' Console.WriteLine("Get:" & _FaderValue)
            End Sub
            Private Sub SetFaderValue()
                Cmd(2) = _FaderValue >> 3
                Cmd(4) = (_FaderValue And 7) << 4
                SendCmd()
            End Sub

        End Class

        Public Module CmdParser

            ' ✅ HashSet : lookup O(1) au lieu de O(n) avec List
            Private ReadOnly CmdTypeSet As New HashSet(Of Byte) From {
        CByte(CmdType.ActuatorType),
        CByte(CmdType.ButtonType),
        CByte(CmdType.DisplayType)
    }

            Private ReadOnly DisplaySizes As New Dictionary(Of Byte, (Slice As Integer, Advance As Integer)) From {
    {CByte(DisplayCommands.VpotLed), (8, 9)},    ' ✅ Cmd=8 bytes
    {CByte(DisplayCommands.VueMeter), (8, 9)},    ' ✅ Cmd=8 bytes
    {CByte(DisplayCommands.AutoModeLed), (7, 8)},    ' ✅ Cmd=7 bytes
    {CByte(DisplayCommands.Clock), (15, 16)},  ' ✅ Cmd=15 bytes
    {CByte(DisplayCommands.ScribbleStrip), (11, 12)}   ' ✅ Cmd=11 bytes
}

            ' ✅ Display Masks (Mask à utiliser) par sous-commande Display
            Private ReadOnly DisplayMasks As New Dictionary(Of Byte, Byte()) From {
        {CByte(DisplayCommands.VpotLed), VpotLed_Cmd.Mask_Id},
        {CByte(DisplayCommands.VueMeter), VueMeter_Cmd.Mask_Id},
        {CByte(DisplayCommands.AutoModeLed), AutoModeLed_Cmd.Mask_Id},
        {CByte(DisplayCommands.Clock), Clock_Cmd.Mask_Id},
        {CByte(DisplayCommands.ScribbleStrip), ScribbleStrip_Cmd.Mask_Id}
    }

            Public CmdTypeList As New List(Of Byte) From {
        CmdType.ActuatorType, CmdType.ButtonType, CmdType.DisplayType
    }
            ''' <summary>Convertit les bytes masqués en clé string hex.</summary>
            Public Function MaskedKey(cmd As Byte(), mask As Byte()) As String
                Return BitConverter.ToString(ByteManager.AndBytes(cmd, mask))
            End Function
            Public Function Parse(CmdPacket As Byte()) As List(Of (Key As String, Cmd As Byte()))
                Dim result As New List(Of (Key As String, Cmd As Byte()))
                If CmdPacket Is Nothing OrElse CmdPacket.Length = 0 Then Return result

                Dim i As Integer = 0
                While i < CmdPacket.Length

                    Select Case CmdPacket(i)

                        Case CByte(CmdType.ButtonType)
                            If i + 3 > CmdPacket.Length Then Exit While
                            Dim Cmd = CmdPacket.AsSpan(i, 3).ToArray()
                            result.Add((MaskedKey(Cmd, Commands.Track_Button_Cmd.Mask_Id), Cmd))
                            i += 4

                        Case CByte(CmdType.DisplayType)
                            If i + 4 > CmdPacket.Length Then Exit While
                            Dim sz As (Slice As Integer, Advance As Integer)
                            If Not DisplaySizes.TryGetValue(CmdPacket(i + 3), sz) Then
                                i += 1 : Continue While
                            End If
                            If i + sz.Slice > CmdPacket.Length Then Exit While
                            Dim Cmd = CmdPacket.AsSpan(i, sz.Slice).ToArray()
                            result.Add((MaskedKey(Cmd, DisplayMasks(CmdPacket(i + 3))), Cmd))
                            i += sz.Advance

                        Case CByte(CmdType.ActuatorType)
                            If i + 3 > CmdPacket.Length Then Exit While
                            If CmdTypeSet.Contains(CmdPacket(i + 3)) OrElse CmdPacket(i + 3) = &H0 OrElse CmdPacket(i + 3) = CmdPacket.Length Then
                                Dim Cmd = CmdPacket.AsSpan(i, 3).ToArray()
                                result.Add((MaskedKey(Cmd, Commands.Vpot_Cmd.Mask_Id), Cmd))
                                i += 4
                            ElseIf i + 5 <= CmdPacket.Length AndAlso (CmdTypeSet.Contains(CmdPacket(i + 4)) OrElse CmdPacket(i + 4) = &H0 OrElse CmdPacket(i + 4) = CmdPacket.Length) Then
                                Dim Cmd = CmdPacket.AsSpan(i, 5).ToArray()
                                result.Add((MaskedKey(Cmd, Commands.Fader_Cmd.Mask_Id), Cmd))
                                i += 6
                            Else
                                i += 1
                            End If

                        Case Else
                            i += 1

                    End Select
                End While

                Return result
            End Function

        End Module



    End Namespace




End Namespace

