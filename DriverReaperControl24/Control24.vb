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
        End Module




        Public Class C24_Components
            Private ReadOnly _IdsToC24Components As Concurrent.ConcurrentDictionary(Of String, C24_Component) = New Concurrent.ConcurrentDictionary(Of String, C24_Component)
            Private ReadOnly _NameToC24Components As Dictionary(Of String, C24_Component) = New Dictionary(Of String, C24_Component)
            Private ReadOnly _AutoRefresh_C24_Components As List(Of C24_Component) = New List(Of C24_Component)
            Private ReadOnly _CmdToSendQueue As ConcurrentQueue(Of Byte()) = New ConcurrentQueue(Of Byte())
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
            Public ReadOnly Property CmdToSendQueue As Concurrent.ConcurrentQueue(Of Byte())
                Get
                    Return _CmdToSendQueue
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
            Private Sub EnqueueCmd(Cmd As Commands.Cmd)
                Dim _bytes(Cmd.Cmd.Length - 1) As Byte
                Cmd.Cmd.CopyTo(_bytes, 0)
                CmdToSendQueue.Enqueue(_bytes)
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
                    _IsBlinking = Value
                End Set
            End Property

            ''' <summary>
            ''' État de la LED.
            ''' En lecture : reflète l'état courant du bouton.
            ''' En écriture : envoie la commande LED vers le C24.
            ''' </summary>
            Public Property Led As Boolean
                Get
                    Return _Led
                End Get
                Set
                    _Led = Value
                    ButtonCmd.Is_On = _Led
                    ButtonCmd.SendCmd()
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

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>
            ''' Mode d'automation affiché sur les LEDs.
            ''' Envoie immédiatement la commande vers le C24.
            ''' </summary>
            Public Property AutoMode As Commands.AutoMode
                Get
                    Return _AutoMode
                End Get
                Set
                    _AutoMode = Value
                    _AutoModeLedCmd.AutoModeLed = _AutoMode
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

        End Class

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
            Private _ClockMode As Commands.ClockModes = Commands.ClockModes.Time
            Private _Clock_Value As String = ""

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>
            ''' Mode d'affichage de l'horloge (Time / Frame / Sample / Beat).
            ''' Met à jour simultanément les LEDs de mode et les points des séparateurs.
            ''' </summary>
            Public Property ClockMode As Commands.ClockModes
                Get
                    Return _ClockMode
                End Get
                Set
                    _ClockMode = Value
                    _ClockLedCmd.ClockMode = _ClockMode
                    _ClockCmd.ClockMode = _ClockMode
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
                    _Clock_Value = Value
                    _ClockCmd.Clock_Value = _Clock_Value
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

            ''' <summary>
            ''' Crée le composant horloge du Control24.
            ''' </summary>
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
            ''' <param name="mode">Mode d'affichage (Time / Frame / Sample / Beat).</param>
            ''' <param name="value">Valeur à afficher sur les 8 digits.</param>
            Public Sub SetClock(mode As Commands.ClockModes, value As String)
                ClockMode = mode
                Clock_Value = value
            End Sub

        End Class
        Public Class C24_ScribbleStrip
            Inherits C24_Component

            Private _Text As String
            Private _ScribbleStripCmd As ScribbleStrip_Cmd
            Private _Value As Double

            Public Property Text As String
                Get
                    _Text = _ScribbleStripCmd.Text
                    Return _Text
                End Get
                Set
                    _Text = Value
                    _ScribbleStripCmd.Text = _Text

                End Set
            End Property
            Public Property Value As Double
                Get
                    _Value = _ScribbleStripCmd.Value
                    Return _Value
                End Get
                Set
                    _Value = Value
                    _ScribbleStripCmd.Value = _Value

                End Set
            End Property

            Public ReadOnly Property ScribbleStripCmd As ScribbleStrip_Cmd
                Get
                    Return _ScribbleStripCmd
                End Get
            End Property

            Sub New(Name As String, Track_Nb As Integer)
                _Name = Name
                _ScribbleStripCmd = New ScribbleStrip_Cmd(Track_Nb)
                C24_SendingCommands.Add(_ScribbleStripCmd)
                Me._Component_Id = _ScribbleStripCmd.Cmd_Id
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
            Private _Value As Double = 0
            Private _Velocity_sensible As Boolean = False
            Private _LedMode As LedModeType
            Private _Value2 As Double = 0
            Private _VpotMode As VpotModeType = VpotModeType.Value
            Private _Adjust_Value2 As Boolean = False
            Private _Value_TextList As List(Of (item As String, Checked As Boolean))
            Private _SelectedItem_Index As Integer = 0
            Private _TrackNb As Integer? = Nothing
            Private _Display_Value2 As Boolean = False

            ' ── Temporisation ScribbleStrip ───────────────────────────────────
            Private _OriginalScribbleText As String = ""
            Private _IsShowingValue As Boolean = False
            Private _RestoreTimer As System.Threading.Timer
            Private Const RestoreDelayMs As Integer = 2000

            Public Event OnVpotValueChange(TrackNb As Integer, Value As Double)
            Public Event OnVpotValue2Change(TrackNb As Integer, Value2 As Double)
            Public Event OnVpotSelectedItemChange(TrackNb As Integer, SelectedItem_Index As Integer)

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
                Value
                TextList
            End Enum
            Public Property Display_Value2 As Boolean
                Get
                    Return _Display_Value2
                End Get
                Set
                    _Display_Value2 = Value
                End Set
            End Property

            Public Property Adjust_Value2 As Boolean
                Get
                    Return _Adjust_Value2
                End Get
                Set
                    _Adjust_Value2 = Value
                End Set
            End Property

            Public Property VpotMode As VpotModeType
                Get
                    Return _VpotMode
                End Get
                Set
                    _VpotMode = Value
                    If Value = VpotModeType.Pan Then
                        LedMode = LedModeType.Central_Dot
                        _Min_Value = -1
                        _Max_Value = 1
                    Else
                        LedMode = LedModeType.Dot
                        _Min_Value = 0
                        _Max_Value = 1
                    End If
                End Set
            End Property

            Public Property Min_Value As Double
                Get
                    Return _Min_Value
                End Get
                Set

                    _Min_Value = Math.Round(Value, 1)

                End Set
            End Property

            Public Property Max_Value As Double
                Get
                    Return _Max_Value
                End Get
                Set

                    _Max_Value = Math.Round(Value, 1)

                End Set
            End Property

            Public ReadOnly Property Step_Value As Double
                Get
                    If Velocity_sensible Then
                        _Step_Value = 1 / Fine_Step
                    Else
                        _Step_Value = 1 / Large_Step
                    End If
                    Return _Step_Value
                End Get

            End Property
            Public Property Value As Double
                Get
                    Return _Value
                End Get
                Set
                    '_Value += Value
                    _Value = Math.Round(Math.Min(Math.Max(Value, Min_Value), Max_Value), 1)

                    RefreshLedStatus()
                End Set
            End Property
            Public Property Value2 As Double
                Get
                    Return _Value2
                End Get
                Set
                    _Value2 = Math.Round(Math.Min(Math.Max(Value, Min_Value), Max_Value), 1)

                    RefreshLedStatus()
                End Set
            End Property

            Public Property Velocity_sensible As Boolean
                Get
                    Return _Velocity_sensible
                End Get
                Set
                    _Velocity_sensible = Value
                End Set
            End Property

            Public Property Value_TextList As List(Of (item As String, Checked As Boolean))
                Get
                    Return _Value_TextList
                End Get
                Set
                    _Value_TextList = Value

                End Set
            End Property

            Public Enum LedModeType
                off
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
                    _LedMode = Value
                End Set
            End Property
            Public Property SelectedItem_Index As Integer
                Get
                    Return _SelectedItem_Index
                End Get
                Set
                    _SelectedItem_Index = Value
                    RefreshSelectedItem()
                End Set
            End Property
            Private Sub RefreshSelectedItem()
                'display SelectedItem on scribble
                If Value_TextList IsNot Nothing AndAlso SelectedItem_Index < Value_TextList.Count Then
                    Dim item_text = Value_TextList.Item(SelectedItem_Index).item
                    Dim item_checked = Value_TextList.Item(SelectedItem_Index).Checked
                    If item_checked Then
                        ScribbleStrip.Text = ">" & item_text.Substring(0, 3)
                    Else
                        ScribbleStrip.Text = item_text
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




            Private Sub UpdateValueFromCmd() Handles _VpotCmd.OnCmdChange

                If VpotMode = VpotModeType.Value Or VpotMode = VpotModeType.Pan Then

                    If Adjust_Value2 Then
                        If Math.Abs(_Value2) <> 0 AndAlso Math.Abs(_Value2) <= 0.05 Then
                            _Value2 = 0
                        Else
                            _Value2 += _VpotCmd.Rotate_Velocity * Step_Value
                            _Value2 = Math.Round(Math.Min(Math.Max(_Value2, Min_Value), Max_Value), 2)
                        End If
                        RefreshLedStatus()
                        RefreshScribbleStrip(_Value2 * 100)
                        RaiseEvent OnVpotValue2Change(_TrackNb, _Value2)
                    Else
                        If Math.Abs(_Value) <> 0 AndAlso Math.Abs(_Value) <= 0.05 Then
                            _Value = 0
                        Else
                            _Value += _VpotCmd.Rotate_Velocity * Step_Value
                            _Value = Math.Round(Math.Min(Math.Max(_Value, Min_Value), Max_Value), 2)
                        End If

                        RefreshLedStatus()
                        RefreshScribbleStrip(_Value * 100)
                        RaiseEvent OnVpotValueChange(_TrackNb, _Value)

                    End If

                ElseIf VpotMode = VpotModeType.TextList Then
                    LedMode = LedModeType.Dot
                    If Value_TextList IsNot Nothing AndAlso Value_TextList.Count > 0 Then
                        Min_Value = 0
                        Max_Value = Value_TextList.Count - 1

                        Value = _VpotCmd.Rotate_Velocity
                        SelectedItem_Index = Value
                        RaiseEvent OnVpotSelectedItemChange(_TrackNb, SelectedItem_Index)
                    End If
                End If


            End Sub
            ' ── Temporisation ScribbleStrip ───────────────────────────────────

            ''' <summary>
            ''' Définit le texte permanent du ScribbleStrip (le nom de la piste, etc.).
            ''' Ce texte est restauré automatiquement après <see cref="RestoreDelayMs"/> ms
            ''' d'inactivité du Vpot.
            ''' </summary>
            ''' <param name="text">Texte permanent à afficher au repos.</param>
            Public Sub SetScribbleText(text As String)
                _OriginalScribbleText = text
                If Not _IsShowingValue Then
                    _ScribbleStrip.Text = _OriginalScribbleText
                End If
            End Sub

            ''' <summary>Restaure le texte permanent après l'expiration du timer.</summary>
            Private Sub OnRestoreTimer(state As Object)
                _IsShowingValue = False
                _ScribbleStrip.Text = _OriginalScribbleText
            End Sub

            Private Sub RefreshScribbleStrip(Value As Double)
                ' ── Mémoriser le texte original avant le premier affichage ────
                If Not _IsShowingValue Then
                    _OriginalScribbleText = _ScribbleStrip.Text
                    _IsShowingValue = True
                End If

                If VpotMode = VpotModeType.Value Then
                    _ScribbleStrip.Value = Value
                ElseIf VpotMode = VpotModeType.Pan Then
                    LedMode = LedModeType.Central_Dot
                    Dim Value_int As Integer = CInt(Value)
                    If Value_int = 0 Then
                        _ScribbleStrip.Text = "<0>"
                    ElseIf Value_int > 0 Then
                        _ScribbleStrip.Text = Value_int & ">"
                    ElseIf Value_int < 0 Then
                        _ScribbleStrip.Text = "<" & Math.Abs(Value_int)
                    End If
                End If

                ' ── Réarmer le timer à chaque mouvement ───────────────────────
                _RestoreTimer.Change(RestoreDelayMs, System.Threading.Timeout.Infinite)
            End Sub

#Region "Led"
            Private Sub RefreshLedStatus()

                Dim ValueLedStatus As BitArray = GetLedsStatus(_Value)
                If Display_Value2 Then
                    Dim Value2LedStatus As BitArray = GetLedsStatus(_Value2)
                    ValueLedStatus = ValueLedStatus.Or(Value2LedStatus)
                End If
                VpotLedCmd.Leds_Status = ValueLedStatus


            End Sub

            Public Sub SetLedsOff()
                LedMode = LedModeType.off
                RefreshLedStatus()
            End Sub

            Private Function GetLedsStatus(Value As Double) As BitArray
                Dim LedsStatus As New BitArray(LedNumber)

                If LedMode = LedModeType.Off Then
                    Return LedsStatus
                ElseIf LedMode = LedModeType.Dot Then

                    LedsStatus.Item(DriverReaperControl24.Utility.GetLedIndex(Value, Min_Value, Max_Value, LedNumber)) = True

                ElseIf LedMode = LedModeType.Gauge Then
                    Dim LedIndex As Integer = DriverReaperControl24.Utility.GetLedIndex(Value, Min_Value, Max_Value, LedNumber)
                    For i = 0 To LedIndex - 1
                        LedsStatus.Item(i) = True
                    Next

                ElseIf LedMode = LedModeType.Central_Dot Then
                    Dim roundedValue As Double = Math.Round(Value, 2)
                    Dim Middle As Double = Math.Round((Min_Value + Max_Value) / 2.0, 2)
                    Dim LedIndex As Integer = 0

                    If roundedValue <= Math.Round(Min_Value, 2) Then
                        ' ── Valeur minimum → LED 0 uniquement ────────────────────
                        LedIndex = 0

                    ElseIf roundedValue = Middle Then
                        ' ── Valeur centrale → LED 7 uniquement ───────────────────
                        LedIndex = 7

                    ElseIf roundedValue >= Math.Round(Max_Value, 2) Then
                        ' ── Valeur maximum → LED 14 uniquement ───────────────────
                        LedIndex = 14

                    ElseIf roundedValue < Middle Then
                        ' ── [Min..Middle[ → LEDs 1..6 linéaires ──────────────────
                        Dim span As Double = Middle - Min_Value
                        If span = 0.0 Then
                            LedIndex = 1
                        Else
                            Dim norm As Double = (roundedValue - Min_Value) / span
                            LedIndex = Math.Min(1 + CInt(Math.Floor(norm * 6)), 6)
                        End If

                    Else
                        ' ── ]Middle..Max] → LEDs 8..13 linéaires ─────────────────
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
                        LedIndex = DriverReaperControl24.Utility.GetLedIndex(Value, Min_Value, Middle, 8)
                    Else
                        LedIndex = DriverReaperControl24.Utility.GetLedIndex(Value, Middle, Max_Value, 8)
                    End If

                    For i = 0 To LedIndex - 1
                        If i >= LedIndex AndAlso i <= 7 Then
                            LedsStatus.Item(i) = True
                        ElseIf i >= 7 AndAlso i <= LedIndex Then
                            LedsStatus.Item(i) = True
                        End If
                    Next

                ElseIf LedMode = LedModeType.Q Then
                    Dim LedIndex As Integer = DriverReaperControl24.Utility.GetLedIndex(Value, Min_Value, Max_Value, 8)
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
            Private _VueMeterCmdLeft As VueMeter_Cmd    ' ✅ cmd dédiée canal gauche
            Private _VueMeterCmdRight As VueMeter_Cmd   ' ✅ cmd dédiée canal droit
            Private _Value_Left As Double
            Private _Min_Value As Double = 0
            Private _Max_Value As Double = 1

            Private _Reverse As Boolean = False
            Private _Value_Right As Double
            Private _Peak_Left As Boolean = False
            Private _Peak_Right As Boolean = False



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

            Sub New(Name As String, Track_Nb As Integer)
                _Name = Name
                'C24
                ' ✅ Canal gauche — cmd indépendante
                _VueMeterCmdLeft = New VueMeter_Cmd(Track_Nb)
                _VueMeterCmdLeft.Channel = StereoChannel.Left
                C24_SendingCommands.Add(_VueMeterCmdLeft)

                ' ✅ Canal droit — cmd indépendante
                _VueMeterCmdRight = New VueMeter_Cmd(Track_Nb)
                _VueMeterCmdRight.Channel = StereoChannel.Right
                C24_SendingCommands.Add(_VueMeterCmdRight)

                Me._Component_Id = _VueMeterCmdLeft.Cmd_Id




            End Sub


            Public Property Value_Left As Double
                Get
                    Return _Value_Left
                End Get
                Set
                    _Value_Left = Value
                    RefreshLedStatus(_Value_Left, StereoChannel.Left)
                    If _Value_Left = Max_Value Then
                        _Peak_Left = True
                    End If
                End Set
            End Property
            Public Property Value_Right As Double
                Get
                    Return _Value_Right
                End Get
                Set
                    _Value_Right = Value
                    RefreshLedStatus(_Value_Right, StereoChannel.Right)
                    If _Value_Left = Max_Value Then
                        _Peak_Right = True
                    End If
                End Set
            End Property
            Public Property Peak_Left As Boolean
                Get
                    Return _Peak_Left
                End Get
                Set
                    _Peak_Left = Value

                End Set
            End Property
            Public Property Peak_Right As Boolean
                Get
                    Return _Peak_Right
                End Get
                Set
                    _Peak_Right = Value
                End Set
            End Property

            Public Property Min_Value As Double
                Get
                    Return _Min_Value
                End Get
                Set
                    _Min_Value = Value
                End Set
            End Property

            Public Property Max_Value As Double
                Get
                    Return _Max_Value
                End Get
                Set
                    _Max_Value = Value
                End Set
            End Property

            Public Property Reverse As Boolean
                Get
                    Return _Reverse
                End Get
                Set
                    _Reverse = Value
                End Set
            End Property

            Public Sub ResetPeaks()
                Peak_Left = False
                Peak_Right = False
                Value_Left = _Value_Left
                Value_Right = _Value_Right
            End Sub
            Private Sub RefreshLedStatus(Value As Double, Channel As StereoChannel)
                Dim Leds_Status = GetLedsStatus(Value)
                If Reverse Then
                    Leds_Status = DriverReaperControl24.Utility.Bit.ReverseBitArray(Leds_Status)
                End If
                If Peak_Left OrElse Peak_Right Then
                    Leds_Status.Item(LedNumber - 1) = True
                End If

                ' ✅ Chaque canal met à jour sa propre cmd — aucune interférence
                If Channel = StereoChannel.Left Then
                    _VueMeterCmdLeft.Leds_Status = Leds_Status
                Else
                    _VueMeterCmdRight.Leds_Status = Leds_Status
                End If
            End Sub

            Private Function GetLedsStatus(Value As Double) As BitArray
                Dim LedsStatus As New BitArray(LedNumber)

                Dim LedIndex As Integer = DriverReaperControl24.Utility.GetLedIndex(Value, Min_Value, Max_Value, LedNumber)
                For i = 0 To LedIndex - 1
                    LedsStatus.Item(i) = True
                Next
                Return LedsStatus

            End Function


        End Class
        ''' <summary>
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
            Private Sub OnJogWheelCmdChange() Handles _JogWheelCmd.OnCmdChange
                RaiseEvent OnJogWheelChange(_JogWheelCmd.Rotate_Direction, _JogWheelCmd.Rotate_Velocity)
            End Sub

        End Class
        ''' <summary>
        ''' Représente un écran virtuel composé de plusieurs <see cref="C24_ScribbleStrip"/> alignés.
        ''' Permet d'afficher un texte long réparti sur l'ensemble des strips dans l'ordre de la liste.
        ''' Chaque strip affiche 4 caractères — la capacité totale est donc <c>Count × 4</c> caractères.
        ''' </summary>
        Public Class C24_ScribbleScreen

            Private ReadOnly _Strips As List(Of C24_ScribbleStrip)

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

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Crée un écran virtuel à partir d'une liste de strips déjà instanciés.
            ''' L'ordre de la liste détermine l'ordre d'affichage de gauche à droite.
            ''' </summary>
            ''' <param name="strips">Liste ordonnée de <see cref="C24_ScribbleStrip"/> composant l'écran.</param>
            Public Sub New(strips As List(Of C24_ScribbleStrip))
                _Strips = strips
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
            Private _Shift As C24_LedButton
            Private _Opt As C24_LedButton
            Private _Ctl_Clutch As C24_LedButton
            Private _Ctrl As C24_LedButton

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
            Private _Cut As C24_LedButton
            Private _Copy As C24_LedButton
            Private _Paste As C24_LedButton
            Private _Edit_Delete As C24_LedButton
            Private _Separate As C24_LedButton
            Private _Capture As C24_LedButton
            Private _Duplicate As C24_LedButton
            Private _Repeat As C24_LedButton
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
            Private _Up As C24_LedButton
            Private _Next As C24_LedButton
            Private _Down As C24_LedButton
            Private _Previous As C24_LedButton
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
            Private _Bank_Left As C24_LedButton
            Private _Nudge As C24_LedButton
            Private _Bank_Right As C24_LedButton
            ' ── ScribbleScreen ───────────────────────────────────────────────
            Private _ScreenLine As C24_ScribbleScreen
            Private _ScreenChannelBarScroll As C24_ScribbleScreen


            ' ── ScribbleScreen ───────────────────────────────────────────────
            ''' <summary>
            ''' Écran virtuel composé des 24 ScribbleStrips des channel strips dans l'ordre.
            ''' Capacité totale : 96 caractères (24 × 4).
            ''' </summary>
            Public ReadOnly Property ScreenChannelBarScroll As C24_ScribbleScreen
                Get
                    Return _ScreenChannelBarScroll
                End Get
            End Property

            ' ── ScribbleScreen ───────────────────────────────────────────────
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
            Public ReadOnly Property Bank_Left As C24_LedButton
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
            Public ReadOnly Property Bank_Right As C24_LedButton
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
            Public ReadOnly Property Up As C24_LedButton
                Get
                    Return _Up
                End Get
            End Property

            ''' <summary>Bouton Next.</summary>
            Public ReadOnly Property [Next] As C24_LedButton
                Get
                    Return _Next
                End Get
            End Property

            ''' <summary>Bouton Down.</summary>
            Public ReadOnly Property Down As C24_LedButton
                Get
                    Return _Down
                End Get
            End Property

            ''' <summary>Bouton Previous.</summary>
            Public ReadOnly Property Previous As C24_LedButton
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
            Public ReadOnly Property Cut As C24_LedButton
                Get
                    Return _Cut
                End Get
            End Property

            ''' <summary>Bouton Copy.</summary>
            Public ReadOnly Property [Copy] As C24_LedButton
                Get
                    Return _Copy
                End Get
            End Property

            ''' <summary>Bouton Paste.</summary>
            Public ReadOnly Property Paste As C24_LedButton
                Get
                    Return _Paste
                End Get
            End Property

            ''' <summary>Bouton Delete (Edit).</summary>
            Public ReadOnly Property Edit_Delete As C24_LedButton
                Get
                    Return _Edit_Delete
                End Get
            End Property

            ''' <summary>Bouton Separate.</summary>
            Public ReadOnly Property Separate As C24_LedButton
                Get
                    Return _Separate
                End Get
            End Property

            ''' <summary>Bouton Capture.</summary>
            Public ReadOnly Property Capture As C24_LedButton
                Get
                    Return _Capture
                End Get
            End Property

            ''' <summary>Bouton Duplicate.</summary>
            Public ReadOnly Property Duplicate As C24_LedButton
                Get
                    Return _Duplicate
                End Get
            End Property

            ''' <summary>Bouton Repeat.</summary>
            Public ReadOnly Property Repeat As C24_LedButton
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
            Public ReadOnly Property Shift As C24_LedButton
                Get
                    Return _Shift
                End Get
            End Property

            ''' <summary>Bouton Opt.</summary>
            Public ReadOnly Property Opt As C24_LedButton
                Get
                    Return _Opt
                End Get
            End Property

            ''' <summary>Bouton Ctl Clutch.</summary>
            Public ReadOnly Property Ctl_Clutch As C24_LedButton
                Get
                    Return _Ctl_Clutch
                End Get
            End Property

            ''' <summary>Bouton Ctrl.</summary>
            Public ReadOnly Property Ctrl As C24_LedButton
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
            Public Property BlinkInterval As Integer
                Get
                    Return _BlinkInterval
                End Get
                Set
                    _BlinkInterval = Value
                    _BlinkTimer?.Change(0, _BlinkInterval)   ' ✅ appliqué à chaud
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
                    _TimingQueueTimeout = Value
                End Set
            End Property

            Public Property InstantQueueTimeout As Integer
                Get
                    Return _InstantQueueTimeout
                End Get
                Set
                    _InstantQueueTimeout = Value
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

                'Dim vuemeter1 = New Hardware.C24_VueMeter("VueMeter_1", 1)
                'Me.Add_AutoRefresh_C24_Component(vuemeter1, "VueMeters")

                '' vuemeter1.Value_Left = 0.5
                ''vuemeter1.Value_Right = 0.8

                'Me.Add_C24_Component(New Hardware.C24_LedButton("F01", Commands.Btn_Region.Left_Strip, &H1), "Function")
                'Me.Add_C24_Component(New Hardware.C24_ScribbleStrip("ScribbleStrip_1", 1), "Scribbles")
                'Me.Add_C24_Component(New Hardware.C24_Vpot("Vpot_1", 1, Me.NameToC24Components("ScribbleStrip_1")), "Vpots")

                'Dim Vpot1 As C24_Vpot = Me.NameToC24Components("Vpot_1")
                'Dim itemlist As New List(Of (item As String, checked As Boolean)) From {
                '("ded", False),
                '("frfr", False),
                '("sza", True)
                '}

                'Vpot1.Value_TextList = itemlist
                'Vpot1.VpotMode = C24_Vpot.VpotModeType.TextList

                'Dim cs1 As New C24_ChannelStrip(1)
                'cs1.RegisterTo(Me)


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

                _Auto_Write = New C24_LedButton("Auto_Write", Btn_Region.Middle_Strip, &H17)
                Me.Add_C24_Component(_Auto_Write, "Automation")
                _Auto_Fader = New C24_LedButton("Auto_Fader", Btn_Region.Middle_Strip, &H18)
                Me.Add_C24_Component(_Auto_Fader, "Automation")
                _Auto_Touch = New C24_LedButton("Auto_Touch", Btn_Region.Middle_Strip, &H19)
                Me.Add_C24_Component(_Auto_Touch, "Automation")
                _Auto_Pan = New C24_LedButton("Auto_Pan", Btn_Region.Middle_Strip, &H1A)
                Me.Add_C24_Component(_Auto_Pan, "Automation")
                _Auto_Latch = New C24_LedButton("Auto_Latch", Btn_Region.Middle_Strip, &H1B)
                Me.Add_C24_Component(_Auto_Latch, "Automation")
                _Auto_Mute = New C24_LedButton("Auto_Mute", Btn_Region.Middle_Strip, &H1C)
                Me.Add_C24_Component(_Auto_Mute, "Automation")
                _Auto_Trim = New C24_LedButton("Auto_Trim", Btn_Region.Middle_Strip, &H1D)
                Me.Add_C24_Component(_Auto_Trim, "Automation")
                _Auto_Send_Level = New C24_LedButton("Auto_Send_Level", Btn_Region.Middle_Strip, &H1E)
                Me.Add_C24_Component(_Auto_Send_Level, "Automation")
                _Auto_Read = New C24_LedButton("Auto_Read", Btn_Region.Middle_Strip, &H1F)
                Me.Add_C24_Component(_Auto_Read, "Automation")
                _Auto_Send_Mute = New C24_LedButton("Auto_Send_Mute", Btn_Region.Middle_Strip, &H20)
                Me.Add_C24_Component(_Auto_Send_Mute, "Automation")
                _Auto_Off = New C24_LedButton("Auto_Off", Btn_Region.Middle_Strip, &H21)
                Me.Add_C24_Component(_Auto_Off, "Automation")
                _Auto_Plug_In = New C24_LedButton("Auto_Plug_In", Btn_Region.Middle_Strip, &H22)
                Me.Add_C24_Component(_Auto_Plug_In, "Automation")
                _Auto_Suspend = New C24_LedButton("Auto_Suspend", Btn_Region.Middle_Strip, &H15)
                Me.Add_C24_Component(_Auto_Suspend, "Automation")
                _Write_Auto_To_End = New C24_LedButton("Write_Auto_To_End", Btn_Region.Middle_Strip, &H31)
                Me.Add_C24_Component(_Write_Auto_To_End, "Automation")
                _Write_Auto_To_Start = New C24_LedButton("Write_Auto_To_Start", Btn_Region.Middle_Strip, &H2F)
                Me.Add_C24_Component(_Write_Auto_To_Start, "Automation")
                _Write_Auto_To_All = New C24_LedButton("Write_Auto_To_All", Btn_Region.Middle_Strip, &H30)
                Me.Add_C24_Component(_Write_Auto_To_All, "Automation")

                '*----------------------------------*
                '*      init Modifiers              *
                '*----------------------------------*

                _Shift = New C24_LedButton("Shift", Btn_Region.Left_Strip, &H23)
                Me.Add_C24_Component(_Shift, "Modifiers")
                _Opt = New C24_LedButton("Opt", Btn_Region.Left_Strip, &H24)
                Me.Add_C24_Component(_Opt, "Modifiers")
                _Ctl_Clutch = New C24_LedButton("Ctl_Clutch", Btn_Region.Left_Strip, &H25)
                Me.Add_C24_Component(_Ctl_Clutch, "Modifiers")
                _Ctrl = New C24_LedButton("Ctrl", Btn_Region.Left_Strip, &H26)
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
                _Input = New C24_LedButton("Input", Btn_Region.Middle_Strip, &H6)
                Me.Add_C24_Component(_Input, "Assignment")
                _Esc_Cancel = New C24_LedButton("Esc_Cancel", Btn_Region.Middle_Strip, &H8)
                Me.Add_C24_Component(_Esc_Cancel, "Assignment")
                _Output = New C24_LedButton("Output", Btn_Region.Middle_Strip, &H5)
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

                _Cut = New C24_LedButton("Cut", Btn_Region.Edit, &H4)
                Me.Add_C24_Component(_Cut, "EditFonction")
                _Copy = New C24_LedButton("Copy", Btn_Region.Edit, &H5)
                Me.Add_C24_Component(_Copy, "EditFonction")
                _Paste = New C24_LedButton("Paste", Btn_Region.Edit, &H6)
                Me.Add_C24_Component(_Paste, "EditFonction")
                _Edit_Delete = New C24_LedButton("Edit_Delete", Btn_Region.Edit, &H7)
                Me.Add_C24_Component(_Edit_Delete, "EditFonction")
                _Separate = New C24_LedButton("Separate", Btn_Region.Edit, &H8)
                Me.Add_C24_Component(_Separate, "EditFonction")
                _Capture = New C24_LedButton("Capture", Btn_Region.Edit, &H9)
                Me.Add_C24_Component(_Capture, "EditFonction")
                _Duplicate = New C24_LedButton("Duplicate", Btn_Region.Edit, &H1C)
                Me.Add_C24_Component(_Duplicate, "EditFonction")
                _Repeat = New C24_LedButton("Repeat", Btn_Region.Edit, &H1D)
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

                _Up = New C24_LedButton("Up", Btn_Region.Navigation, &HF)
                Me.Add_C24_Component(_Up, "Navigation")
                _Previous = New C24_LedButton("Previous", Btn_Region.Navigation, &H10)
                Me.Add_C24_Component(_Previous, "Navigation")
                _Next = New C24_LedButton("Next", Btn_Region.Navigation, &H11)
                Me.Add_C24_Component(_Next, "Navigation")
                _Down = New C24_LedButton("Down", Btn_Region.Navigation, &H12)
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


                _Save = New C24_LedButton("Save", Btn_Region.Edit, &H14)
                Me.Add_C24_Component(_Save, "Save")
                _Undo = New C24_LedButton("Undo", Btn_Region.Edit, &H15)
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

                '*----------------------------------*
                '*      init JogWheel               *
                '*----------------------------------*

                _JogWheel = New C24_JogWheel("JogWheel")
                Me.Add_C24_Component(_JogWheel, "JogWheel")


                '*----------------------------------*
                '*      init Bank                   *
                '*----------------------------------*


                _Bank_Left = New C24_LedButton("Bank_Left", Btn_Region.Edit, &HA)
                Me.Add_C24_Component(_Bank_Left, "Bank")
                _Nudge = New C24_LedButton("Nudge", Btn_Region.Edit, &HB)
                Me.Add_C24_Component(_Nudge, "Bank")
                _Bank_Right = New C24_LedButton("Bank_Right", Btn_Region.Edit, &HC)
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



                ' ── Signal : initialisation terminée ──────────────────────────
                _initEvent.Set()

            End Sub



            Private Sub NewChannelStrip(Tack_Number As Integer)




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


                ' Initialize packet capture
                ' ✅ Native Pcap P/Invoke - Direct wpcap.dll access (NO NuGet dependencies!)
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
                    _ReceivedC24PacketQueue = New Concurrent.ConcurrentQueue(Of Packet.C24_Packet)
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
                    _AllCmdBytesQueue = New ConcurrentQueue(Of List(Of Byte()))
                    SendingAllCmdBytesQueueThread = New Thread(AddressOf SendingAllCmdBytesQueueLoop) With {
                        .Name = "SendingAllCmdBytesQueue",
                .IsBackground = True,
                .Priority = ThreadPriority.Normal
                    }
                    SendingAllCmdBytesQueueThread.Start()



                Catch ex As Exception
                    '_log.Error("❌ Native Pcap initialization failed: {0}", ex.Message)
                    '_log.Error("💡 Solution: Install Npcap from https://npcap.com/")
                    '_log.Error("💡 Make sure to enable 'WinPcap API-compatible Mode' during installation")
                    Throw New Exception("Cannot initialize packet capture. Npcap is required for Control24.", ex)
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
                    Dim SendCmdBytes As New List(Of Byte())
                    Dim BytesToSend As Byte() = Nothing

                    ' ✅ TryDequeue en boucle : thread-safe, évite le TOCTOU de .Count
                    While CmdToSendQueue.TryDequeue(BytesToSend)
                        SendCmdBytes.Add(BytesToSend)
                    End While

                    If SendCmdBytes.Count > 0 Then
                        _AllCmdBytesQueue.Enqueue(SendCmdBytes)
                        ' Pas de Sleep → reboucle immédiatement si la queue se remplit vite
                    Else
                        Thread.Sleep(InstantQueueTimeout)   ' ✅ attente uniquement si la queue est vide
                    End If
                End While
            End Sub

            Private Sub SendingAllCmdBytesQueueLoop()
                While IsRunning
                    Dim cmdBatch As List(Of Byte()) = Nothing
                    Dim hasSent As Boolean = False

                    ' ✅ TryDequeue en boucle : vide toute la queue accumulée en un seul passage
                    While _AllCmdBytesQueue.TryDequeue(cmdBatch)
                        If cmdBatch IsNot Nothing AndAlso cmdBatch.Count > 0 Then
                            _PacketManager.SendCmdBytes(cmdBatch)
                            hasSent = True
                        End If
                    End While

                    If Not hasSent Then
                        Thread.Sleep(1)   ' ✅ cède le CPU uniquement si rien à envoyer
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




        End Class

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

            Private _DbVolume As Double = 0

            ' ── Restauration ScribbleStrip ─────────────────────────────────────
            Private _OriginalScribbleText As String = ""
            Private _IsShowingDb As Boolean = False
            Private _RestoreTimer As System.Threading.Timer
            Private Const RestoreDelayMs As Integer = 2000

            ' ── Vérification de position fader ────────────────────────────────
            Private _TargetFaderValue As Integer = 0
            Private _VerifyRetryCount As Integer = 0
            Private _VerifyTimer As System.Threading.Timer
            Private Const VerifyDelayMs As Integer = 150
            Private Const MaxVerifyRetry As Integer = 5

            ' ── Constantes ────────────────────────────────────────────────────
            Private Const FaderMin As Integer = 0
            Private Const FaderMax As Integer = 1023
            Private Const BtnFaderTouch As Byte = &H9

            ' ── Événements ────────────────────────────────────────────────────

            ''' <summary>Déclenché quand un humain pose la main sur le fader.</summary>
            Public Event On_FaderTouched(Component_Id As String)

            ''' <summary>Déclenché quand la main quitte le fader.</summary>
            Public Event On_FaderReleased(Component_Id As String)

            ''' <summary>
            ''' Déclenché quand le hardware envoie une nouvelle position de fader
            ''' pendant que le fader est touché par un humain.
            ''' </summary>
            Public Event On_FaderHumanChangeDbValue(TrackNb As Integer, DbValue As Double)

            ' ── Propriétés ────────────────────────────────────────────────────

            ''' <summary>Commande fader sous-jacente (accès direct aux bytes).</summary>
            Public ReadOnly Property FaderCmd As Commands.Fader_Cmd
                Get
                    Return _FaderCmd
                End Get
            End Property

            ''' <summary>Bouton de détection de contact humain sur le fader.</summary>
            Public ReadOnly Property FaderTouch As C24_Button
                Get
                    Return _FaderTouch
                End Get
            End Property

            ''' <summary>ScribbleStrip associé à ce fader.</summary>
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
                    _FaderCmd.FaderValue = Math.Min(Math.Max(Value, FaderMin), FaderMax)
                End Set
            End Property

            ''' <summary>
            ''' Volume en dB.
            ''' Affiche la valeur sur le <see cref="ScribbleStrip"/> uniquement si elle change.
            ''' Le texte original est restauré automatiquement <see cref="RestoreDelayMs"/> ms
            ''' après la dernière modification.
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

                    If _IsShowingDb = False Then
                        _OriginalScribbleText = _ScribbleStrip.Text
                        _IsShowingDb = True
                    End If

                    _ScribbleStrip.Value = _DbVolume
                    _RestoreTimer.Change(RestoreDelayMs, System.Threading.Timeout.Infinite)
                End Set
            End Property

            ' ── Restauration ScribbleStrip ─────────────────────────────────────

            ''' <summary>Restaure le texte original du ScribbleStrip après 2 s d'inactivité.</summary>
            Private Sub OnRestoreTimer(state As Object)
                _IsShowingDb = False
                _ScribbleStrip.Text = _OriginalScribbleText
            End Sub

            ''' <summary>
            ''' Envoie la commande fader vers <paramref name="rawValue"/>
            ''' et arme le timer de vérification.
            ''' Une nouvelle valeur demandée remplace automatiquement la cible
            ''' et redémarre le cycle de vérification.
            ''' </summary>
            ''' <param name="rawValue">Valeur brute cible [0..1023].</param>
            Private Sub SendAndArmVerify(rawValue As Integer)
                _TargetFaderValue = Math.Min(Math.Max(rawValue, FaderMin), FaderMax)
                FaderValue = _TargetFaderValue
                _VerifyTimer.Change(VerifyDelayMs, System.Threading.Timeout.Infinite)
            End Sub

            ''' <summary>
            ''' Vérifie que le fader a atteint la position cible.
            ''' Si la cible n'est pas atteinte et que le fader n'est pas touché,
            ''' renvoie la commande et se réarme indéfiniment jusqu'à succès
            ''' ou jusqu'à ce qu'une nouvelle valeur soit demandée via
            ''' <see cref="SendAndArmVerify"/>.
            ''' </summary>
            Private Sub OnVerifyTimer(state As Object)
                If IsTouched Then
                    Return
                End If

                If _FaderCmd.FaderValue = _TargetFaderValue Then
                    Return
                End If

                FaderValue = _TargetFaderValue
                _VerifyTimer.Change(VerifyDelayMs, System.Threading.Timeout.Infinite)
            End Sub

            ' ── API publique ──────────────────────────────────────────────────

            ''' <summary>
            ''' Pilote le fader depuis Reaper.
            ''' 
            ''' Lance la vérification automatique de position.
            ''' </summary>
            ''' <param name="Db_Volume">Volume cible en dB.</param>
            Public Sub SetFaderDbVolume(Db_Volume As Double)

                DbVolume = Db_Volume
                SendAndArmVerify(DbToRawFaderCmdValue(Db_Volume))
            End Sub

            ''' <summary>
            ''' Initialise la position du fader sans vérification d'affichage.
            ''' Lance quand même la vérification de position hardware.
            ''' </summary>
            ''' <param name="Db_Volume">Volume initial en dB.</param>
            Public Sub SetInitFaderDbVolume(Db_Volume As Double)
                _DbVolume = Db_Volume
                SendAndArmVerify(DbToRawFaderCmdValue(Db_Volume))
            End Sub

            ' ── Synchronisation depuis le hardware ────────────────────────────

            ''' <summary>
            ''' Appelé quand le hardware envoie un mouvement fader (fader touché uniquement).
            ''' </summary>
            Private Sub OnFaderCmdChange() Handles _FaderCmd.OnCmdChange
                If IsTouched Then
                    DbVolume = RawFaderCmdValueToDb(_FaderCmd.FaderValue)
                    RaiseEvent On_FaderHumanChangeDbValue(FaderCmd.Track_Number, DbVolume)
                End If
            End Sub

            Private Sub OnFaderTouchPressed(componentId As String)
                _VerifyTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite)
                RaiseEvent On_FaderTouched(_Component_Id)
            End Sub

            Private Sub OnFaderTouchReleased(componentId As String)
                RaiseEvent On_FaderReleased(_Component_Id)
            End Sub

            ' ── Constructeur ──────────────────────────────────────────────────

            ''' <summary>
            ''' Crée un fader motorisé avec détection de contact et affichage dB
            ''' pour la piste <paramref name="trackNumber"/>.
            ''' </summary>
            ''' <param name="name">Nom unique du composant.</param>
            ''' <param name="trackNumber">Numéro de piste 1-based.</param>
            ''' <param name="scribbleStrip">ScribbleStrip pour l'affichage du volume en dB.</param>
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

                _VerifyTimer = New System.Threading.Timer(
                    AddressOf OnVerifyTimer,
                    Nothing,
                    System.Threading.Timeout.Infinite,
                    System.Threading.Timeout.Infinite)
            End Sub

            ' ── Conversion dB ↔ raw ───────────────────────────────────────────

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

            ''' <summary>
            ''' Convertit une valeur brute de commande fader (0–1023) en volume en dB.
            ''' Fonction inverse de <see cref="DbToRawFaderCmdValue"/>.
            ''' </summary>
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
            Private Sub Pending_C24_Packets_QueueLoop()
                While Desk.IsRunning

                    If AckReceived AndAlso _Pending_C24_Packets.TryDequeue(Pending_C24_Packet) Then
                        Retry_Counter = 0
                        _PendingPacketCounter = Pending_C24_Packet.C24Header.SendCounterInt   ' ✅ sauvegarde AVANT envoi
                        _AckEvent.Reset()   ' ✅ reset AVANT envoi → élimine la race condition
                        AckReceived = False
                        Desk.Send_Cmd_C24Packet(Pending_C24_Packet.C24Packet)

                    ElseIf Pending_C24_Packet IsNot Nothing AndAlso Retry_Counter <= Max_Retry Then
                        Dim ackInTime As Boolean = _AckEvent.WaitOne(AckTimeoutBeforeRetry)

                        If ackInTime Then
                            Pending_C24_Packet = Nothing
                            Retry_Counter = 0
                        Else
                            If Pending_C24_Packet.C24Header.CmdType(0) <> CByte(Packet.CmdType.Init) Then
                                Interlocked.Increment(_Retry_Counter)
                                Pending_C24_Packet.C24Header.Retry = ByteManager.UInt16ToBytesLErev(Retry_Counter)
                                Pending_C24_Packet.RebuildPacket()
                            End If
                            Desk.Send_Cmd_C24Packet(Pending_C24_Packet.C24Packet)
                        End If

                    ElseIf Retry_Counter > Max_Retry Then
                        Pending_C24_Packet = Nothing
                        AckReceived = True
                        Retry_Counter = 0
                    End If

                    Thread.Sleep(0)
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
            Time
            Frame
            Sample
            Beat
            None
        End Enum
        Public Class ClockLed_Cmd
            Inherits Track_Display_Cmd
            Private ClockLedBytes = New Dictionary(Of ClockModes, Byte) From
                    {{ClockModes.Time, &H40},
                    {ClockModes.Frame, &H20},
                    {ClockModes.Sample, &H10},
                    {ClockModes.Beat, &H8},
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
                Track_Number = 25
                ClockMode = ClockModes.Time
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
            Private ClockModesDots = New Dictionary(Of ClockModes, Byte) From
                    {{ClockModes.Time, &H15},
                    {ClockModes.Frame, &H2A},
                    {ClockModes.Sample, &H0},
                    {ClockModes.Beat, &H14}}
            Private ClockModesDotsRev As Dictionary(Of Byte, ClockModes) = Utility.ReverseDictionary(Of ClockModes, Byte)(ClockModesDots)
            Private ReadOnly SevenSeg As New Dictionary(Of Char, Byte) From {
    {"0"c, &H7E},  ' 0b1111110
    {"1"c, &H30},  ' 0b0110000
    {"2"c, &H6D},  ' 0b1101101
    {"3"c, &H79},  ' 0b1111001
    {"4"c, &H33},  ' 0b0110011
    {"5"c, &H5B},  ' 0b1011011
    {"6"c, &H5F},  ' 0b1011111
    {"7"c, &H70},  ' 0b1110000
    {"8"c, &H7F},  ' 0b1111111
    {"9"c, &H7B},  ' 0b1111011
    {"-"c, &H1},  ' 0b0000001
    {" "c, &H0},
    {"L"c, &HE},
    {"h"c, &H17},
    {"o"c, &H1D},
    {"b"c, &H1F},
    {"H"c, &H37},
    {"J"c, &H38},
    {"Y"c, &H3B},
    {"d"c, &H3D},
    {"U"c, &H3E},
    {"R"c, &H46},
    {"F"c, &H47},
    {"C"c, &H4E},
    {"E"c, &H4F},
    {"S"c, &H5B},  ' 0b1011011 (= '5')
    {"P"c, &H67},
    {"Z"c, &H6D},  ' 0b1101101 (= '2')
    {"A"c, &H77}
}
            Private ReadOnly SevenSegRev As Dictionary(Of Byte, Char) = Utility.ReverseDictionary(Of Char, Byte)(SevenSeg)
            Private _ClockMode As ClockModes
            Private _Clock_Value As String
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &HFF, &HFF, &HFF, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &HFF}

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property
            Sub New()

                'set defaut cmd
                Cmd = ByteManager.ConcatBytes(Display_Header, {&H0, &H0}, {&H0}, {&H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0}, {Display_Ender})
                Track_Number = 25
                ClockMode = ClockModes.Time
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
                ClockMode = ClockModesDotsRev(Cmd(5))

            End Sub
            Private Sub SetClockMode()
                Cmd(5) = ClockModesDots(ClockMode)
                SendCmd()

            End Sub

            Public Property Clock_Value As String
                Get
                    Return _Clock_Value
                End Get
                Set
                    _Clock_Value = Value
                    SetClockValue()
                End Set
            End Property

            Private Sub ReadClockValue() Handles MyBase.OnCmdChange
                If Cmd Is Nothing OrElse Cmd.Length < 14 Then Return

                ' Cmd(6..13) = 8 bytes seven-segment → reconstitue la string
                Dim chars(7) As Char
                For i = 0 To 7
                    Dim b As Byte = Cmd(6 + i)
                    If SevenSegRev.ContainsKey(b) Then
                        chars(i) = SevenSegRev(b)
                    Else
                        chars(i) = "?"c   ' byte inconnu → espace
                    End If
                Next

                _Clock_Value = New String(chars)
            End Sub
            Private Sub SetClockValue()
                _Clock_Value = _Clock_Value.PadLeft(8)
                Dim Char_Values = Clock_Value.ToArray
                Cmd(6) = SevenSeg(Char_Values(0))
                Cmd(7) = SevenSeg(Char_Values(1))
                Cmd(8) = SevenSeg(Char_Values(2))
                Cmd(9) = SevenSeg(Char_Values(3))
                Cmd(10) = SevenSeg(Char_Values(4))
                Cmd(11) = SevenSeg(Char_Values(5))
                Cmd(12) = SevenSeg(Char_Values(6))
                Cmd(13) = SevenSeg(Char_Values(7))
                SendCmd()

            End Sub
        End Class
        Public Class ScribbleStrip_Cmd
            Inherits Track_Display_Cmd

            Private _Text As String
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &HFF, &HFF, &HFF, &HFF, &H0, &H0, &H0, &H0, &HFF}
            Private _Value As Double

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
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
                    SetTextValue()
                End Set
            End Property
            Public Property Value As Double
                Get
                    Return _Value
                End Get
                Set
                    _Value = Value
                    SetDoubleValue()
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
            Private Sub SetDoubleValue()
                Dim ic = CultureInfo.InvariantCulture
                Dim ValueAbs = Math.Abs(Value)

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
            End Sub


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
            Right = &H0
            Left = &H1
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
                Cmd(2) = CByte(_Rotate_Velocity) + &H40
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
                End Set
            End Property

            Private Sub ReadFaderValue() Handles MyBase.OnCmdChange
                _FaderValue = (CInt(Cmd(2)) << 3) Or (CInt(Cmd(4)) >> 4)
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

