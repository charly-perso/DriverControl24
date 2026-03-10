Imports System.Collections.Concurrent
Imports System.Diagnostics.CodeAnalysis
Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Threading
Imports System.Windows.Forms.VisualStyles.VisualStyleElement.TrackBar
Imports DriverControl24.Control24.Commands
Imports DriverControl24.Reacontrol24
Imports DriverControl24.Reacontrol24.C24Clock
Imports DriverControl24.Utility.Bit
Imports Microsoft.VisualBasic.Devices
Imports Microsoft.VisualBasic.Logging
Imports SharpPcap

Namespace Control24

    Namespace Hardware
        Public Class C24_Components
            Private ReadOnly _IdsToC24Components As Concurrent.ConcurrentDictionary(Of String, C24_Component) = New Concurrent.ConcurrentDictionary(Of String, C24_Component)
            Private ReadOnly _NameToC24Components As Dictionary(Of String, C24_Component) = New Dictionary(Of String, C24_Component)
            Private ReadOnly _AutoRefresh_C24_Components As List(Of C24_Component) = New List(Of C24_Component)
            Private ReadOnly _CmdToSendQueue As ConcurrentQueue(Of Byte()) = New ConcurrentQueue(Of Byte())
            Private ReadOnly _GroupComponents As Dictionary(Of String, List(Of C24_Component)) = New Dictionary(Of String, List(Of C24_Component))

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
                AddHandler C24_Component.Cmd.NeedToSendCmd, AddressOf EnqueueCmd
                AddTogroupName(GroupName, C24_Component)
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

            Private Sub AddTogroupName(GroupName As String, C24_Component As C24_Component)
                If GroupName IsNot Nothing AndAlso GroupName <> "" Then
                    If GroupComponents.ContainsKey(GroupName) = False Then
                        GroupComponents.Add(GroupName, New List(Of C24_Component))
                    End If
                    GroupComponents(GroupName).Add(C24_Component)
                End If
            End Sub

        End Class
        Public MustInherit Class C24_Component
            Public _Component_Id As String
            Public _Cmd As Commands.Cmd
            Public _Name As String


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

            Public ReadOnly Property Cmd As Commands.Cmd
                Get
                    Return _Cmd
                End Get
            End Property
        End Class

        Public Class C24_LedButton
            Inherits C24_Component

            Private _ButtonCmd As Commands.Button_Cmd = Nothing
            Private _Status As Boolean

            ReadOnly Property ButtonCmd As Commands.Button_Cmd
                Get
                    Return _ButtonCmd
                End Get
            End Property

            Sub New(Name As String, Button_Nb As Byte, Track_Nb As Integer)
                _Name = Name
                _ButtonCmd = New Commands.Track_Button_Cmd(Button_Nb, Track_Nb)
                _Cmd = _ButtonCmd
                Me._Component_Id = _ButtonCmd.Cmd_Id
            End Sub

            Sub New(Name As String, Region As Commands.Btn_Region, Button_Nb As Byte)
                _Name = Name
                _ButtonCmd = New Commands.Region_Button_Cmd(Region, Button_Nb)
                _Cmd = _ButtonCmd
                Me._Component_Id = _ButtonCmd.Cmd_Id
            End Sub

            Public Property Status As Boolean
                Get
                    _Status = _ButtonCmd.Is_On
                    Return _Status
                End Get
                Set
                    _Status = Value
                    _ButtonCmd.Is_On = _Status
                    _ButtonCmd.SendCmd()
                End Set
            End Property
        End Class

        Public Class C24_ScribbleStrip
            Inherits C24_Component

            Private _Text As String
            Private _ScribbleStripCmd As ScribbleStrip_Cmd

            Public Property Text As String
                Get
                    _Text = _ScribbleStripCmd.ScribbleValue
                    Return _Text
                End Get
                Set
                    _Text = Value
                    _ScribbleStripCmd.ScribbleValue = _Text
                    _ScribbleStripCmd.SendCmd()
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
                _Cmd = _ScribbleStripCmd
                Me._Component_Id = _ScribbleStripCmd.Cmd_Id
            End Sub
        End Class

        Public Class C24_Vpot
            Inherits C24_Component

            Const LedNumber As Integer = 15
            Private WithEvents _VpotCmd As Commands.Vpot_Cmd
            Private WithEvents _VpotLedCmd As Commands.VpotLed_Cmd
            Private _ScribbleStrip As C24_ScribbleStrip
            Private _Min_Value As Double? = Nothing
            Private _Max_Value As Double? = Nothing
            Private _Step_Value As Double? = Nothing
            Private _Value As Double? = Nothing
            Private _Velocity_sensible As Boolean = False
            Private _LedMode As LedModeType
            Private _Value2 As Double? = Nothing

            ReadOnly Property VpotCmd As Commands.Vpot_Cmd
                Get
                    Return _VpotCmd
                End Get
            End Property

            ReadOnly Property VpotLedCmd As Commands.VpotLed_Cmd
                Get
                    Return _VpotLedCmd
                End Get
            End Property
            ReadOnly Property ScribbleStrip As C24_ScribbleStrip
                Get
                    Return _ScribbleStrip
                End Get
            End Property

            Sub New(Name As String, Track_Nb As Integer, ScribbleStrip As C24_ScribbleStrip)
                _Name = Name
                _VpotCmd = New Vpot_Cmd(Track_Nb)
                _VpotLedCmd = New VpotLed_Cmd(Track_Nb)
                _ScribbleStrip = ScribbleStrip
                _Cmd = _VpotCmd
                Me._Component_Id = _VpotCmd.Cmd_Id
            End Sub

            Property Min_Value As Double?
                Get
                    Return _Min_Value
                End Get
                Set
                    If Value IsNot Nothing Then
                        _Min_Value = Math.Round(CDbl(Value), 1)
                    End If
                End Set
            End Property

            Property Max_Value As Double?
                Get
                    Return _Max_Value
                End Get
                Set
                    If Value IsNot Nothing Then
                        _Max_Value = Math.Round(CDbl(Value), 1)
                    End If
                End Set
            End Property

            Property Step_Value As Double?
                Get
                    Return _Step_Value
                End Get
                Set
                    If Value IsNot Nothing Then
                        _Step_Value = Math.Round(CDbl(Value), 1)
                    End If
                End Set
            End Property
            Property Value As Double?
                Get
                    Return _Value
                End Get
                Set
                    If Value IsNot Nothing Then
                        _Value = Math.Round(CDbl(Value), 1)
                    End If
                    RefreshLedStatus()
                End Set
            End Property
            Property Value2 As Double?
                Get
                    Return _Value2
                End Get
                Set
                    If Value IsNot Nothing Then
                        _Value2 = Math.Round(CDbl(Value), 1)
                    End If
                    RefreshLedStatus()
                End Set
            End Property

            Property Velocity_sensible As Boolean
                Get
                    Return _Velocity_sensible
                End Get
                Set
                    _Velocity_sensible = Value
                End Set
            End Property

            Public Enum LedModeType
                Dot
                Central_Dot
                Gauge
                Central_Gauge
                Q
            End Enum

            Property LedMode As LedModeType
                Get
                    Return _LedMode
                End Get
                Set
                    _LedMode = Value
                End Set
            End Property

#Region "Led"
            Private Sub RefreshLedStatus()
                If Value IsNot Nothing AndAlso Min_Value IsNot Nothing AndAlso Max_Value IsNot Nothing Then
                    Dim ValueLedStatus As BitArray = GetLedsStatus(Value)
                    If Value2 IsNot Nothing Then
                        Dim Value2LedStatus As BitArray = GetLedsStatus(Value2)
                        ValueLedStatus = ValueLedStatus.Or(Value2LedStatus)
                    End If
                    VpotLedCmd.Leds_Status = ValueLedStatus
                End If

            End Sub

            ''' <summary>
            ''' Mappe une valeur dans [Min, Max] vers un index LED dans [0, Led_Number - 1].
            ''' </summary>
            Private Function GetLedIndex(Value As Double, Min As Double, Max As Double, Led_Number As Integer) As Integer
                If Led_Number < 2 OrElse Max <= Min Then Return 0          ' ✅ garde-fous

                Value = Math.Max(Min, Math.Min(Max, Value))                ' ✅ clamp → élimine <Min et >Max

                If Value <= Min Then Return 0                              ' ✅ borne basse
                If Value >= Max Then Return Led_Number - 1                 ' ✅ borne haute

                ' ✅ O(1) : index direct sans boucle
                Dim ratio As Double = (Value - Min) / (Max - Min)          ' 0.0 < ratio < 1.0
                Return CInt(Math.Floor(ratio * (Led_Number - 1)))          ' ✅ [1, Led_Number-2]
            End Function
            Private Function GetLedsStatus(Value As Double) As BitArray
                Dim LedsStatus As New BitArray(LedNumber)

                If LedMode = LedModeType.Dot Then

                    LedsStatus.Item(GetLedIndex(Value, Min_Value, Max_Value, LedNumber)) = True

                ElseIf LedMode = LedModeType.Gauge Then
                    Dim LedIndex As Integer = GetLedIndex(Value, Min_Value, Max_Value, LedNumber)
                    For i = 0 To LedIndex - 1
                        LedsStatus.Item(i) = True
                    Next

                ElseIf LedMode = LedModeType.Central_Dot Then
                    Dim LedIndex As Integer = 0
                    Dim Middle = (Min_Value + Max_Value) / 2
                    If Value <= Middle Then
                        LedIndex = GetLedIndex(Value, Min_Value, Middle, 8)
                    Else
                        LedIndex = GetLedIndex(Value, Middle, Max_Value, 8)
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

        Public Class C24_Desk
            Inherits C24_Components

            Const C24_port As Integer = 9123
            Private _NetworkInterface As NetworkInterface


            Private SendPacketsQueues As Dictionary(Of String, Concurrent.ConcurrentQueue(Of Byte()))

            Private ReceivedCmdQueue As Concurrent.ConcurrentQueue(Of Byte())
            Private _isCapturing As Boolean = False
            Private _IsRunning As Boolean
            Private SendingThread As Threading.Thread
            Dim _packetCapture As NativePcapAdapter
            Private _TimingQueue As Integer = 50
            Private SendingTimingQueueThread As Threading.Thread
            Private _ComputerMacAddr As Byte()

            Private _ReceivedPacketsQueue As ConcurrentQueue(Of Byte())
            Private _PacketManager As Packet.PacketManager

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

            Public Property TimingQueue As Integer
                Get
                    Return _TimingQueue
                End Get
                Set
                    _TimingQueue = Value
                End Set
            End Property

            Public Property ReceivedPacketsQueue As Concurrent.ConcurrentQueue(Of Byte())
                Get
                    Return _ReceivedPacketsQueue
                End Get
                Set
                    _ReceivedPacketsQueue = Value
                End Set
            End Property

            Sub New(NetworkInterface As Net.NetworkInformation.NetworkInterface)
                _NetworkInterface = NetworkInterface
                'initialize C24desk Components
                InitializeComponents()
            End Sub


            Sub InitializeComponents()

                Me.Add_C24_Component(New Hardware.C24_LedButton("F01", Commands.Btn_Region.Left_Strip, &H1), "Function")


            End Sub
            Sub StartNetworkCommunication()
                'init packet handler
                ' Get IP address
                Dim firstIp As IPAddress = If(NetworkInterface.GetIPProperties.UnicastAddresses?.Count > 0, NetworkInterface.GetIPProperties.UnicastAddresses(0).Address, New IPAddress({&H7F, &H0, &H0, &H1}))
                Dim listenAddress As New IPEndPoint(firstIp, C24_port)
                _ComputerMacAddr = NetworkInterface.GetPhysicalAddress.GetAddressBytes
                Dim mac_addr_str As String = BitConverter.ToString(_ComputerMacAddr).Replace("-", ":")
                Dim Interface_Name As String = NetworkInterface.Name

                'Set is running
                _IsRunning = True

                'init queues 
                SendPacketsQueues = New Dictionary(Of String, Concurrent.ConcurrentQueue(Of Byte()))
                SendPacketsQueues.Add("ACK", New Concurrent.ConcurrentQueue(Of Byte()))
                SendPacketsQueues.Add("CMD", New Concurrent.ConcurrentQueue(Of Byte()))

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
                    ReceivedPacketsQueue = New Concurrent.ConcurrentQueue(Of Byte())
                    _packetCapture.StartCapture(AddressOf PacketHandler)
                    _isCapturing = True

                    'Start C24packet Sending process thread

                    SendingThread = New Thread(AddressOf SendingLoop) With {
                        .Name = "NativePcapSend",
                        .IsBackground = True,
                        .Priority = ThreadPriority.Highest
                    }
                    SendingThread.Start()


                    'Start Sending Timing Queue Thread

                    SendingTimingQueueThread = New Thread(AddressOf SendingTimingQueueLoop) With {
                        .Name = "SendingTimingQueue",
                .IsBackground = True,
                .Priority = ThreadPriority.Normal
                    }
                    SendingTimingQueueThread.Start()



                Catch ex As Exception
                    '_log.Error("❌ Native Pcap initialization failed: {0}", ex.Message)
                    '_log.Error("💡 Solution: Install Npcap from https://npcap.com/")
                    '_log.Error("💡 Make sure to enable 'WinPcap API-compatible Mode' during installation")
                    Throw New Exception("Cannot initialize packet capture. Npcap is required for Control24.", ex)
                End Try



            End Sub


            Private Sub PacketHandler(timestamp As Double, pktData As Byte())
                Try
                    _PacketManager.ReceiveBytes(pktData)
                Catch ex As Exception

                End Try
            End Sub

            Private Sub SendingTimingQueueLoop()
                While IsRunning
                    Dim SendCmdBytes As New List(Of Byte())
                    For Each _AutoRefresh_C24_Component In Me.AutoRefresh_C24_Components
                        SendCmdBytes.Add(DirectCast(_AutoRefresh_C24_Component.Cmd.Cmd.Clone(), Byte()))
                    Next
                    Dim CmdToSendQueue_size As Integer = CmdToSendQueue.Count
                    For i = 0 To CmdToSendQueue_size - 1
                        Dim BytesToSend As Byte() = Nothing
                        If CmdToSendQueue.TryDequeue(BytesToSend) Then
                            SendCmdBytes.Add(DirectCast(BytesToSend.Clone(), Byte()))
                        End If
                    Next

                    _PacketManager.SendCmdBytes(SendCmdBytes)


                    Thread.Sleep(_TimingQueue)
                End While
            End Sub


            Private Sub SendingLoop()

                While IsRunning
                    Dim hasSent As Boolean = False

                    While Not SendPacketsQueues("ACK").IsEmpty
                        Dim packet As Byte() = Nothing
                        If SendPacketsQueues("ACK").TryDequeue(packet) Then
                            _packetCapture.SendPacket(packet)
                            hasSent = True   ' ✅
                        End If
                    End While

                    While Not SendPacketsQueues("CMD").IsEmpty
                        Dim packet As Byte() = Nothing
                        If SendPacketsQueues("CMD").TryDequeue(packet) Then
                            _packetCapture.SendPacket(packet)
                            hasSent = True   ' ✅
                        End If
                    End While
                    If Not hasSent Then Thread.Sleep(1)   ' ✅ évite le spin à 100% CPU
                End While
            End Sub

            Public Sub Send_Cmd_C24Packet(CmdC24Packet As Byte())
                SendPacketsQueues("CMD").Enqueue(CmdC24Packet)
            End Sub
            Public Sub Send_Ack_C24Packet(AckC24Packet As Byte())
                SendPacketsQueues("ACK").Enqueue(AckC24Packet)
            End Sub



        End Class



        Public Class test
            Sub creat_desk()

            End Sub
        End Class
    End Namespace

    Namespace Packet

        Public Class Eth_Header
            Private _Source_MacAddress As Byte()
            Private _Destination_MacAddress As Byte()
            Dim Protocol As Byte() = {&H88, &HEF}
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
                    _C24Header.NbBytes = {CByte(16)}
                    _C24Header.CmdNb = {&H0}
                Else

                    _Cmd_Packet = ByteManager.ConcatBytes(Cmds.ToArray)
                    _C24Header.NbBytes = ByteManager.UInt16ToBytesLErev(16 + _Cmd_Packet.Count)
                    _C24Header.CmdNb = {CByte(Cmds.Count)}   ' ✅ 1 byte
                    _C24Header.CheckSum = ByteManager.CalcChecksum(_Cmd_Packet)

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
            Private _EthHeader As Byte()
            Private _C24Header As Byte() = {&H0, &H10, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &HA0, &H0}

            ' ✅ Initialiser l'header Ethernet au moment de l'init du bureau
            Public Sub New(EthHeader As Byte())
                _EthHeader = EthHeader
            End Sub
            Public Function Get_Ack(ReceivedCounter As Byte()) As Byte()
                _C24Header(8) = ReceivedCounter(0)
                _C24Header(9) = ReceivedCounter(1)
                _C24Header(10) = ReceivedCounter(2)
                _C24Header(11) = ReceivedCounter(3)
                Return ByteManager.ConcatBytes(_EthHeader, _C24Header)
            End Function

        End Class
        Public Class PacketManager
            Private _Sending_Eth_Header As Byte() = Nothing
            Private _Received_C24Packet As C24_Packet
            Private _Desk As Control24.Hardware.C24_Desk
            Private _IsInit As Boolean = False
            Private _AckReceived As Boolean = True
            Private _C24Header_SendCounter As Integer = 0
            Private _AckTimeoutBeforeRetry As Integer = 10
            Private _Retry_Counter As Integer = 0
            Private _Max_Retry As Integer = 10
            Private _Send_Cmd_Bytes As List(Of Byte())
            Private _Max_PacketData_Length As Integer = 300
            Private _Pending_C24_Packets As Concurrent.ConcurrentQueue(Of C24_Packet) = New ConcurrentQueue(Of C24_Packet)
            Private Pending_C24_PacketsThread As Thread
            Private Pending_C24_Packet As C24_Packet = Nothing
            Private _AckC24_Packet As AckC24_Packet = Nothing


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

            ReadOnly Property IsInit As Boolean
                Get
                    Return _IsInit
                End Get
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


            Private Sub Pending_C24_Packets_QueueLoop()
                While Desk.IsRunning

                    If AckReceived AndAlso _Pending_C24_Packets.TryDequeue(Pending_C24_Packet) Then
                        Retry_Counter = 0
                        Desk.Send_Cmd_C24Packet(Pending_C24_Packet.C24Packet)
                        AckReceived = False
                    ElseIf Pending_C24_Packet IsNot Nothing AndAlso Retry_Counter <= Max_Retry Then
                        Thread.Sleep(AckTimeoutBeforeRetry)
                        If Not AckReceived Then   ' ✅ re-vérifier après le sleep
                            Interlocked.Increment(_Retry_Counter)
                            Pending_C24_Packet.C24Header.Retry = ByteManager.UInt16ToBytesLErev(Retry_Counter)
                            Pending_C24_Packet.RebuildPacket()
                            Desk.Send_Cmd_C24Packet(Pending_C24_Packet.C24Packet)
                        Else
                            ' ✅ ACK reçu pendant le sleep → libérer le paquet
                            Pending_C24_Packet = Nothing
                            Retry_Counter = 0
                        End If

                    ElseIf Retry_Counter > Max_Retry Then
                            ' ✅ Abandon du paquet → déblocage
                            Pending_C24_Packet = Nothing
                        AckReceived = True
                        Retry_Counter = 0
                    End If

                    Thread.Sleep(1)

                End While
            End Sub


            ''' <summary>
            ''' Entering point for all received C24 Packets
            ''' </summary>
            ''' <param name="Received_C24Packet"></param>
            Public Sub ReceiveBytes(ReceivedBytes As Byte())
                _Received_C24Packet = New C24_Packet(ReceivedBytes)
                'Cmd received from C24 --> ultra fast ACK send then send cmds to CmdsPacketqueue
                If _Received_C24Packet.C24Header.CmdType(0) = CByte(CmdType.Command) AndAlso _AckC24_Packet IsNot Nothing Then
                    Send_Ack_C24Packet(_AckC24_Packet.Get_Ack(_Received_C24Packet.C24Header.SendCounter))
                    'manage Commandes received ****** POINT d'entrée de tous les messages reçus *********
                    ReceivedC24Packet(_Received_C24Packet)
                    ' Init sequence
                ElseIf _Received_C24Packet.EthHeader.IsBroadcast AndAlso _IsInit = False Then
                    Dim _Eth_Header = New Eth_Header(_Desk.ComputerMacAddr, _Received_C24Packet.EthHeader.Source_MacAddress)
                    _Sending_Eth_Header = _Eth_Header.Get_Header_Copy()
                    _AckC24_Packet = New AckC24_Packet(_Sending_Eth_Header)
                    Dim Cmds As New List(Of Byte())
                    Cmds.Add({&H0})
                    Dim init_C24Packet As New C24_Packet(Sending_Eth_Header, C24Header_SendCounter, CmdType.Init, Cmds)
                    Send_Cmd_C24Packet(init_C24Packet)

                    ' ACK reçu
                ElseIf _Received_C24Packet.C24Header.CmdType(0) = CByte(CmdType.Ack) AndAlso ByteManager.BytesLEToUInt32(_Received_C24Packet.C24Header.ReceivedCounter) = C24Header_SendCounter Then
                    If C24Header_SendCounter = 1 Then
                        _IsInit = True
                    End If
                    AckReceived = True
                    Retry_Counter = 0

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

            Private Sub Send_Ack_C24Packet(Ack_Packet As Byte())
                _Desk.Send_Ack_C24Packet(Ack_Packet)
            End Sub
            ''' <summary>
            ''' Entering Point for receiving packets
            ''' </summary>
            ''' <param name="_C24_Packet"></param>
            Private Sub ReceivedC24Packet(_C24_Packet As C24_Packet)
                'parse Packet into commands
                Dim ParsedCmds = Control24.Commands.CmdParser.Parse(_C24_Packet.Cmd_Packet)
                'give and set commands to components
                For Each Kv In ParsedCmds
                    Dim component As Hardware.C24_Component = Nothing
                    If Desk.IdsToC24Components.TryGetValue(Kv.Key, component) Then  ' ✅ 1 seul lookup
                        component.Cmd.Cmd = Kv.Value
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
            End Sub
            Private Sub SetButtonStatus()
                If Is_On Then
                    Cmd(2) = Cmd(2) Or &H40
                Else
                    Cmd(2) = Cmd(2) Or &H0
                End If
            End Sub
            Private Sub SetButtonNb()
                Cmd(1) = ButtonNb
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
                MyBase.Cmd(2) = Cmd(2) Or (CByte(_Track_Number - 1) And TrackMask(2))
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
                Cmd(2) = Cmd(2) Or (Region And RegionMask(2))
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
            Private _DisplayDatas As Byte()
            Private _Track_Number As Integer

            Public Property DisplayDatas As Byte()
                Get
                    Return _DisplayDatas
                End Get
                Set
                    _DisplayDatas = Value
                    SetDisplayDatas()
                End Set
            End Property

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
                MyBase.Cmd(4) = Cmd(4) Or (CByte(_Track_Number - 1) And &H1F)
            End Sub

            Private Sub ReadDisplayCommand() Handles MyBase.OnCmdChange
                _DisplayCommand = Cmd(3)
            End Sub
            Private Sub SetDisplayCommand()
                Cmd(3) = _DisplayCommand
            End Sub
            Private Sub ReadDisplayDatas() Handles MyBase.OnCmdChange
                _DisplayDatas = ByteManager.SliceBytes(Cmd, 5, Cmd.Length - 6)
            End Sub
            Private Sub SetDisplayDatas()
                Cmd = ByteManager.ConcatBytes(Display_Header, {Cmd(3), Cmd(4)}, DisplayDatas, {Display_Ender})
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
                Dim d0 As Byte = Cmd(4)   ' Conserve les bits du numéro de piste
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

                DisplayDatas = {d0, d1, d2}
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

                DisplayDatas = {d0, d1, d2}
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
                Cmd(4) = Cmd(4) Or Channel
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
            End Sub
        End Class
        Public Enum ClockModes
            Time
            Frame
            Sample
            Beat
        End Enum
        Public Class ClockLed_Cmd
            Inherits Track_Display_Cmd
            Private ClockLedBytes = New Dictionary(Of ClockModes, Byte) From
                    {{ClockModes.Time, &H40},
                    {ClockModes.Frame, &H20},
                    {ClockModes.Sample, &H10},
                    {ClockModes.Beat, &H8}}
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
                Cmd = ByteManager.ConcatBytes(Display_Header, {&H0, &H0}, {&H0}, {Display_Ender})
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
            Shared _Mask_Id As Byte()

            Shared ReadOnly Property Mask_Id As Byte()
                Get
                    Return _Mask_Id
                End Get
            End Property
            Sub New()

                'set defaut cmd
                Cmd = ByteManager.ConcatBytes(Display_Header, {&H0, &H0}, {&H0}, {&H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0}, {Display_Ender})
                DisplayCommand = DisplayCommands.Clock
                Track_Number = 25
                ClockMode = ClockModes.Time
                'set Id                          
                _Mask_Id = {&HFF, &HFF, &HFF, &HFF, &HFF, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &HFF}
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

            End Sub
        End Class
        Public Class ScribbleStrip_Cmd
            Inherits Track_Display_Cmd

            Private _ScribbleValue As String
            Shared _Mask_Id As Byte() = {&HFF, &HFF, &HFF, &HFF, &HFF, &HFF, &H0, &H0, &H0, &H0, &HFF}

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

            Public Property ScribbleValue As String
                Get
                    Return _ScribbleValue
                End Get
                Set
                    _ScribbleValue = Value
                    SetScribbleValue()
                End Set
            End Property

            Private Sub ReadScribbleValue() Handles MyBase.OnCmdChange
                _ScribbleValue = New String(System.Text.ASCIIEncoding.ASCII.GetChars({Cmd(6), Cmd(7), Cmd(8), Cmd(9)}))
            End Sub

            Private Sub SetScribbleValue()


                If ScribbleValue.Contains(".") Then
                    Try
                        Dim sing = CSng(ScribbleValue)
                        _ScribbleValue = SingleToString(_ScribbleValue)
                    Catch ex As Exception

                    End Try
                Else
                    _ScribbleValue = _ScribbleValue.PadRight(4).Substring(0, 4)
                End If


                Dim _Scribble_Bytes = System.Text.ASCIIEncoding.ASCII.GetBytes(_ScribbleValue)
                Cmd(6) = _Scribble_Bytes(0)
                Cmd(7) = _Scribble_Bytes(1)
                Cmd(8) = _Scribble_Bytes(2)
                Cmd(9) = _Scribble_Bytes(3)

            End Sub

            ''' <summary>
            ''' Transforme le texte du scribble strip en 4 caractères.
            ''' Le bureau C24 possède des caractères combinés "point + chiffre" en un seul glyphe
            ''' (ex: "12.3" → "12" + chr spécial), sauf pour '0' (ASCII 48) qui reste un point.
            ''' </summary>
            Private Function SingleToString(Number As Single) As String

                Number = Math.Round(Number, 1)
                Dim Left = Number.ToString.Split(".")(0).PadLeft(3).Substring(0, 3)
                Dim Right = Number.ToString.Split(".")(1)

                Dim nco As Integer = AscW(Right)
                If nco <> 48 Then   ' Pas '0' → remplace le point par le glyphe combiné
                    Right = ChrW(nco - 26).ToString()
                Else
                    Right = "."
                End If

                Return Left & Right

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
                Cmd(1) = Cmd(1) Or (CByte(_Track_Number - 1) And &H1F)
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
                Cmd(2) = Cmd(2) Or (_Rotate_Direction And &HC0)
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
            Public Function Parse(CmdPacket As Byte()) As Dictionary(Of String, Byte())
                Dim result As New Dictionary(Of String, Byte())
                If CmdPacket Is Nothing OrElse CmdPacket.Length = 0 Then Return result

                Dim i As Integer = 0
                While i < CmdPacket.Length   ' ✅ While explicite : évite la confusion avec Next +1

                    Select Case CmdPacket(i)   ' ✅ Select Case : plus rapide que If/ElseIf

                        Case CByte(CmdType.ButtonType)
                            If i + 3 > CmdPacket.Length Then Exit While   ' ✅ garde-fou
                            Dim Cmd = CmdPacket.AsSpan(i, 3).ToArray()
                            result.Add(MaskedKey(Cmd, Commands.Track_Button_Cmd.Mask_Id), Cmd)  ' ✅ AsSpan : pas d'allocation LINQ
                            i += 4

                        Case CByte(CmdType.DisplayType)
                            If i + 4 > CmdPacket.Length Then Exit While
                            Dim sz As (Slice As Integer, Advance As Integer)
                            If Not DisplaySizes.TryGetValue(CmdPacket(i + 3), sz) Then  ' ✅ O(1)
                                i += 1 : Continue While
                            End If
                            If i + sz.Slice > CmdPacket.Length Then Exit While
                            Dim Cmd = CmdPacket.AsSpan(i, sz.Slice).ToArray()
                            result.Add(MaskedKey(Cmd, DisplayMasks(CmdPacket(i + 3))), Cmd)
                            i += sz.Advance

                        Case CByte(CmdType.ActuatorType)
                            If i + 3 > CmdPacket.Length Then Exit While
                            If CmdTypeSet.Contains(CmdPacket(i + 2)) Then
                                Dim Cmd = CmdPacket.AsSpan(i, 3).ToArray()   ' ✅ 3 bytes = longueur réelle de Vpot_Cmd
                                result.Add(MaskedKey(Cmd, Commands.Vpot_Cmd.Mask_Id), Cmd)
                                i += 4
                            ElseIf i + 5 <= CmdPacket.Length AndAlso CmdTypeSet.Contains(CmdPacket(i + 4)) Then
                                Dim Cmd = CmdPacket.AsSpan(i, 5).ToArray()   ' ✅ 5 bytes = longueur réelle de Fader_Cmd
                                result.Add(MaskedKey(Cmd, Commands.Fader_Cmd.Mask_Id), Cmd)
                                i += 6
                            Else
                                i += 1
                            End If

                        Case Else
                            i += 1   ' ✅ récupération sur octet inconnu

                    End Select
                End While

                Return result
            End Function

        End Module



    End Namespace

End Namespace

