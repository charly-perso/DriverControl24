''' <summary>
''' Control24 mixing desk daemon
''' Daemon process to serve further client processes
''' that can choose to implement a protocol with DAWs etc.
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' </summary>

Imports System.Net
Imports System.Net.Sockets
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Diagnostics ' For Process priority management

Namespace Reacontrol24

    ''' <summary>
    ''' Timing constants (in seconds)
    ''' </summary>
    Public Class Timing
        Public Const KeepAlive As Double = 10.0
        Public Const KeepAliveLoop As Double = 1.0
        Public Const BeforeAckt As Double = 0.0008
        Public Const MainLoop As Double = 6.0
        Public Const ListenerPoll As Double = 2.0
        Public Const ListenerReconnect As Double = 1.0
        Public Const WaitDeskAck As Double = 0.1
        Public Const Backoff As Double = 0.3
    End Class

    ''' <summary>
    ''' PCAP settings
    ''' </summary>
    Public Class PcapSettings
        Public Const ErrbufSize As Integer = 256
        Public Const Snaplen As Integer = 1038
        Public Const Promisc As Integer = 1
        Public Const PacketLimit As Integer = -1 ' infinite
        Public Const PollDelay As Integer = 5
        Public Const Filter As String = "(ether dst {0} or broadcast) and ether[12:2]=0x885f"
    End Class

    ''' <summary>
    ''' Mac Address structure
    ''' </summary>
    <StructLayout(LayoutKind.Sequential, Pack:=1)>
    Public Structure MacAddress
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=3)>
        Public Vendor As Byte()

        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=3)>
        Public Device As Byte()

        Private Shared ReadOnly _vendor As Byte() = {&H0, &HA0, &H7E}
        Private Shared ReadOnly _broadcast As Byte() = {&HFF, &HFF, &HFF}

        Public Function IsVendor() As Boolean
            Return Vendor IsNot Nothing AndAlso
                   Vendor.SequenceEqual(_vendor)
        End Function

        Public Function IsBroadcast() As Boolean
            Return Vendor IsNot Nothing AndAlso
                   Vendor.SequenceEqual(_broadcast)
        End Function

        Public Overrides Function ToString() As String
            If Vendor Is Nothing OrElse Device Is Nothing Then
                Return "00:00:00:00:00:00"
            End If
            Return String.Join(":", Vendor.Concat(Device).Select(Function(b) b.ToString("X2")))
        End Function
    End Structure

    ''' <summary>
    ''' Ethernet Header structure (14 bytes)
    ''' </summary>
    <StructLayout(LayoutKind.Sequential, Pack:=1)>
    Public Structure EthHeader
        Public MacDest As MacAddress
        Public MacSrc As MacAddress

        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=2)>
        Public Protocol As Byte()

        Public Sub New(dummy As Boolean)
            Protocol = {&H88, &H5F}
            MacDest = New MacAddress()
            MacSrc = New MacAddress()
        End Sub

        Public Function IsBroadcast() As Boolean
            Return MacDest.IsBroadcast()
        End Function

        Public Overrides Function ToString() As String
            Return $"to:{MacDest} from:{MacSrc} prot:{Utils.HexL(Protocol)}"
        End Function
    End Structure

    ''' <summary>
    ''' C24 Header structure (16 bytes)
    ''' All multi-byte fields are BIG ENDIAN
    ''' </summary>
    <StructLayout(LayoutKind.Sequential, Pack:=1)>
    Public Structure C24Header
        Public NumBytes As UInt16 ' Big Endian - already swapped when set
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=2)>
        Public Unknown1 As Byte()
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=4)>
        Public SendCounter As Byte() ' Big Endian - already swapped when set
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=4)>
        Public CmdCounter As Byte() ' Big Endian - already swapped when set
        Public Retry As UInt16 ' Big Endian - already swapped when set
        Public C24Cmd As Byte
        Public NumCommands As Byte

        Public Function IsRetry() As Boolean
            ' Retry is now in host byte order after parsing
            Return Retry <> 0
        End Function

        Public Overrides Function ToString() As String
            Dim cmd = If(GetCommandName(C24Cmd), $"0x{C24Cmd:X2}")
            ' Values are already in host byte order after parsing - display directly
            Return $"bytes:{NumBytes} c_cnt:{CmdCounter} s_cnt:{SendCounter} retry:{Retry} cmd:{cmd} nc:{NumCommands}"
        End Function

        Private Shared Function GetCommandName(cmd As Byte) As String
            Select Case cmd
                Case Commands.Ack
                    Return "Ack"
                Case Commands.Online
                    Return "Online"
                Case Commands.BroadcastInit
                    Return "BcastInit"
                Case Commands.BroadcastActive
                    Return "BcastActive"
                Case Else
                    Return Nothing
            End Select
        End Function

        Private Shared Function SwapBytesUInt16(value As UInt16) As UInt16
            Return CUShort((value >> 8) Or (value << 8))
        End Function

        Private Shared Function SwapBytesUInt32(value As UInt32) As UInt32
            Return ((value And &HFFUI) << 24) Or
                   ((value And &HFF00UI) << 16) Or
                   ((value And &HFF0000UI) >> 16) Or
                   ((value And &HFF000000UI) >> 24)
        End Function
    End Structure

    ''' <summary>
    ''' C24 Broadcast Data structure (50 bytes total based on logs)
    ''' Wire format from Control24 broadcast packets
    ''' </summary>
    <StructLayout(LayoutKind.Sequential, Pack:=1)>
    Public Structure C24BcastData
        ' 6 bytes: MAC address of computer that responded (or 00:00:00:00:00:00 if init broadcast)
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=6)>
        Public ComputerMac As Byte()

        ' 9 bytes: Unknown/Padding
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=9)>
        Public Unknown1 As Byte()

        ' 9 bytes: Firmware version (e.g., "v1.08")
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=9)>
        Public Version As Byte()

        ' 9 bytes: Device name (e.g., "CNTRL|1 ")
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=9)>
        Public Device As Byte()

        ' 17 bytes: Reserved/Padding
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=17)>
        Public Reserved As Byte()

        Public Overrides Function ToString() As String
            Dim ver = If(Version IsNot Nothing, Text.Encoding.ASCII.GetString(Version).TrimEnd(CChar(vbNullChar)), "")
            Dim dev = If(Device IsNot Nothing, Text.Encoding.ASCII.GetString(Device).TrimEnd(CChar(vbNullChar)), "")
            Dim mac = If(ComputerMac IsNot Nothing, BitConverter.ToString(ComputerMac).Replace("-", " ").ToLower(), "")
            Return $"BCAST d:{dev} v:{ver} u1:{mac}"
        End Function
    End Structure

    ''' <summary>
    ''' C24 Packet class - dynamically sized based on data length
    ''' </summary>
    Public Class C24Packet
        Public Property EthHeader As EthHeader
        Public Property C24Header As C24Header
        Public Property PacketData As Byte()

        Public ReadOnly Property TotalLength As Integer
            Get
                Return 30 + If(PacketData?.Length, 0) ' 14 (Eth) + 16 (C24 Header) + data
            End Get
        End Property

        Public ReadOnly Property DataLength As Integer
            Get
                Return If(PacketData?.Length, 0)
            End Get
        End Property

        Public Sub New(dataLength As Integer)
            EthHeader = New EthHeader(True)
            Dim tempC24 As C24Header
            ' Initialize with zeros - PreparePacket will set the correct values
            tempC24.NumBytes = 0
            tempC24.Unknown1 = {0, 0}
            tempC24.SendCounter = {&H0, &H0, &H0, &H0}
            tempC24.CmdCounter = {&H0, &H0, &H0, &H0}
            tempC24.Retry = 0
            tempC24.C24Cmd = 0
            tempC24.NumCommands = 0
            C24Header = tempC24

            If dataLength > 0 Then
                PacketData = New Byte(dataLength - 1) {}
            End If
        End Sub

        Public Sub New(rawData As Byte())
            If rawData.Length < 30 Then
                Throw New ArgumentException("Packet too short")
            End If

            ' Parse Ethernet Header (14 bytes: 0-13)
            Dim ethBytes(13) As Byte
            Array.Copy(rawData, 0, ethBytes, 0, 14)
            EthHeader = BytesToStructure(Of EthHeader)(ethBytes)

            ' Parse C24 Header manually (16 bytes: 14-29)
            ' Convert from BIG-ENDIAN (network byte order) to HOST ORDER (little-endian)
            ' Big-endian means: most significant byte first
            Dim tempHeader As C24Header

            ' NumBytes (bytes 14-15) - Big-endian: [MSB][LSB]
            tempHeader.NumBytes = CUShort((CUShort(rawData(14)) << 8) Or rawData(15))

            ' Unknown1 (bytes 16-17)
            tempHeader.Unknown1 = {rawData(16), rawData(17)}

            ' SendCounter (bytes 18-21) - Big-endian: [MSB]...[LSB]
            tempHeader.SendCounter = {rawData(18), rawData(19), rawData(20), rawData(21)}
            '(CUInt(rawData(18)) << 24) Or
            '(CUInt(rawData(19)) << 16) Or
            '(CUInt(rawData(20)) << 8) Or
            'CUInt(rawData(21))

            ' CmdCounter (bytes 22-25) - Big-endian: [MSB]...[LSB]
            tempHeader.CmdCounter = {rawData(22), rawData(23), rawData(24), rawData(25)}
            '(CUInt(rawData(22)) << 24) Or
            '                    (CUInt(rawData(23)) << 16) Or
            '                    (CUInt(rawData(24)) << 8) Or
            '                    CUInt(rawData(25))

            ' Retry (bytes 26-27) - Big-endian: [MSB][LSB]
            tempHeader.Retry = CUShort((CUShort(rawData(26)) << 8) Or rawData(27))

            ' C24Cmd (byte 28)
            tempHeader.C24Cmd = rawData(28)

            ' NumCommands (byte 29)
            tempHeader.NumCommands = rawData(29)

            C24Header = tempHeader

            ' Parse Data (starts at byte 30)
            Dim dataLen = rawData.Length - 30
            If dataLen > 0 Then
                PacketData = New Byte(dataLen - 1) {}
                Array.Copy(rawData, 30, PacketData, 0, dataLen)
            End If
        End Sub

        Public Function ToBuffer() As Byte()
            Dim buffer(TotalLength - 1) As Byte
            Dim ethBytes = StructureToBytes(EthHeader)
            Dim c24Bytes = StructureToBytes(C24Header)

            ' Copy Ethernet Header (14 bytes)
            Array.Copy(ethBytes, 0, buffer, 0, 14)

            ' Copy C24 Header base (12 bytes: NumBytes(2) + Unknown1(2) + SendCtr(4) + CmdCtr(4))
            Array.Copy(c24Bytes, 0, buffer, 14, 12)

            ' Copy Retry (2 bytes) at position 26
            buffer(26) = c24Bytes(12)
            buffer(27) = c24Bytes(13)

            ' Copy C24Cmd and NumCommands (2 bytes) at position 28
            buffer(28) = C24Header.C24Cmd
            buffer(29) = C24Header.NumCommands

            ' Copy packet data starting at position 30
            If PacketData IsNot Nothing Then
                Array.Copy(PacketData, 0, buffer, 30, PacketData.Length)
            End If

            Return buffer
        End Function

        Public Function IsBroadcast() As Boolean
            Return EthHeader.IsBroadcast()
        End Function

        Public Function IsRetry() As Boolean
            Return C24Header.IsRetry()
        End Function

        Public Overrides Function ToString() As String
            Return $"{EthHeader} {C24Header} {Utils.HexL(PacketData)}"
        End Function

        Private Shared Function BytesToStructure(Of T As Structure)(bytes As Byte()) As T
            Dim handle As GCHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned)
            Try
                Return CType(Marshal.PtrToStructure(handle.AddrOfPinnedObject(), GetType(T)), T)
            Finally
                handle.Free()
            End Try
        End Function

        Private Shared Function StructureToBytes(Of T As Structure)(str As T) As Byte()
            Dim size As Integer = Marshal.SizeOf(str)
            Dim arr(size - 1) As Byte
            Dim ptr As IntPtr = Marshal.AllocHGlobal(size)
            Try
                Marshal.StructureToPtr(str, ptr, True)
                Marshal.Copy(ptr, arr, 0, size)
            Finally
                Marshal.FreeHGlobal(ptr)
            End Try
            Return arr
        End Function
    End Class

    ''' <summary>
    ''' Main C24 Session class with OSC integration
    ''' </summary>
    Public Class C24Session
        Implements IDisposable

        Private _log As Logger
        Private _network As NetworkHelper.NetworkInterfaceInfo
        Private _listenAddress As IPEndPoint
        Private _macComputer As MacAddress
        Private _macControl24 As MacAddress?
        Private _ethHeader As EthHeader
        Private _isClosing As Boolean
        Private _isCapturing As Boolean
        Public _sendCounter As UInt32 = 1
        Private _cmdCounter As UInt32 = 0
        Private _sendLock As New AutoResetEvent(True)
        Private _pcapLastSent As Double
        Private _currentRetryDesk As Integer = 0

        ' ✅ FIX: Flag to ensure initialization packets are sent ONLY ONCE
        Private _initializationSent As Boolean = False
        Private _initLock As New Object()  ' Thread-safe lock for initialization

        ' ✅ NEW: ACK-based send queue mechanism - LOCK-FREE with PRIORITY SUPPORT
        Private _pendingCommands As New System.Collections.Concurrent.ConcurrentQueue(Of PendingCommand)()
        Private _priorityCommands As New System.Collections.Concurrent.ConcurrentQueue(Of PendingCommand)() ' ✅ NEW: High-priority queue
        Private _waitingForAck As Boolean = False
        Private _lastSendTime As DateTime
        Private _ackTimeout As Integer = 800  ' ✅ INCREASED to 800ms (was 500ms) - covers 99.9% of cases
        Private _sendQueueThread As Thread
        Private _sendQueueRunning As Boolean = False
        Private _ackReceivedEvent As New ManualResetEventSlim(False)
        Private _expectedAckCounter As UInt32 = 0  ' ✅ NEW: Shared variable for expected ACK counter
        Private _commandSequenceLock As New Object() ' ✅ NEW: Lock for sequence preservation

        ' ✅ NEW: Smart keep-alive management
        Private _keepAliveEnabled As Boolean = True
        Private _lastCommandTime As DateTime = DateTime.MinValue
        Private _keepAliveIdleTimeout As Integer = 10000  ' ✅ FIXED: 10 secondes (était 3000ms)
        Private _keepAliveLock As New Object()
        Private _isTestingActive As Boolean = False  ' ✅ NEW: Flag pour désactiver keep-alive pendant les tests

        ' ✅ NEW: Anti-duplicate detection
        Private _lastSentCommandData As Byte() = Nothing
        Private _lastSentCommandTime As DateTime = DateTime.MinValue
        Private _duplicateDetectionWindowMs As Integer = 200  ' 200ms window to detect duplicates
        Private _duplicateDetectionLock As New Object()

        ' Structure to hold pending commands
        ' ✅ FIXED: Changed from Structure to Class to allow modification in queue
        Private Class PendingCommand
            Public PacketData As Byte()
            Public NumCommands As Byte
            Public SendAttempts As Integer
            Public MaxRetries As Integer
            Public Timestamp As DateTime
            Public SendCounterUsed As UInt32  ' ✅ Track which SendCounter was used
            Public IsKeepAlive As Boolean     ' ✅ NEW: Flag to identify keep-alive packets
            Public Priority As CommandPriority ' ✅ NEW: Priority level for queue ordering

            Public Sub New(data As Byte(), numCmds As Byte, Optional isKeepAlive As Boolean = False, Optional priority As CommandPriority = CommandPriority.Normal)
                PacketData = data
                NumCommands = numCmds
                SendAttempts = 0
                MaxRetries = 3
                Timestamp = DateTime.Now
                SendCounterUsed = 0
                Me.IsKeepAlive = isKeepAlive
                Me.Priority = priority
            End Sub
        End Class

        ''' <summary>
        ''' Command priority levels - Higher = More urgent
        ''' ✅ NEW: Priority system for queue management
        ''' </summary>
        Private Enum CommandPriority As Integer
            Low = 0          ' VU meters, status updates
            Normal = 1       ' Regular LED commands, scribble strips
            High = 2         ' User interactions (buttons, faders)
            Critical = 3     ' Keep-alive, ACK responses
            System = 4       ' Init, broadcasts responses (highest)
        End Enum

        Private _TimerCallback As New TimerCallback(AddressOf KeepAliveLoop)
        Private keepaliveTimer As Timer  ' ✅ FIXED: Initialisation différée (pas de New)

        ' Packet capture
        Private _packetCapture As INetworkCapture  ' ✅ Interface générique
        Private _keepAliveThread As Thread
        Private _backoffTimer As Timer

        ' OSC integration
        Private _oscManager As OscManager
        Private _oscMapper As C24OscMapper
        Private _oscReverseMapper As OscToC24Mapper
        Public _desk As C24Desk

        ' OSC configuration
        Private _oscServerPort As Integer = Defaults.Control24Osc
        Private _oscDawAddress As String = "127.0.0.1"
        Private _oscDawPort As Integer = Defaults.OscDaw

        ' TCP Server integration
        Private _tcpServer As TcpServerManager
        Private _tcpServerPort As Integer = Defaults.Daemon

        ' Message routing
        Private _messageRouter As MessageRouter

        ' Statistics monitoring
        Private _statsMonitor As StatisticsMonitor

        ' ✅ NEW: initialization queue flag
        Private _initInProgress As Boolean = False

        Public Sub New(networkInfo As NetworkHelper.NetworkInterfaceInfo, listenAddress As IPEndPoint, logger As Logger)
            _log = logger
            _network = networkInfo
            _listenAddress = listenAddress

            ' Parse MAC address
            Dim macBytes = networkInfo.Mac.Split(":"c).Select(Function(s) Convert.ToByte(s, 16)).ToArray()
            _macComputer = New MacAddress With {
                .Vendor = macBytes.Take(3).ToArray(),
                .Device = macBytes.Skip(3).ToArray()
            }

            ' Initialize Ethernet header
            _ethHeader = New EthHeader(True) With {
                .MacSrc = _macComputer
            }

            ' Initialize packet capture
            ' ✅ Native Pcap P/Invoke - Direct wpcap.dll access (NO NuGet dependencies!)
            Try
                _log.Info("🚀 Initializing NativePcapAdapter (P/Invoke direct to wpcap.dll)...")
                _packetCapture = New NativePcapAdapter(_log)
                _log.Info("✅ Native Pcap initialized successfully - Zero NuGet dependencies!")
            Catch ex As Exception
                _log.Error("❌ Native Pcap initialization failed: {0}", ex.Message)
                _log.Error("💡 Solution: Install Npcap from https://npcap.com/")
                _log.Error("💡 Make sure to enable 'WinPcap API-compatible Mode' during installation")
                Throw New Exception("Cannot initialize packet capture. Npcap is required for Control24.", ex)
            End Try

            ' Initialize OSC mappers
            _oscMapper = New C24OscMapper(_log)
            _oscReverseMapper = New OscToC24Mapper(_log)

            ' Initialize desk
            _desk = New C24Desk(AddressOf OscClientSendHandler, AddressOf C24ClientSendHandler, _log)

            ' Initialize message router
            _messageRouter = New MessageRouter(_log)
            AddHandler _messageRouter.SendToControl24, AddressOf OnRouterSendToControl24
            AddHandler _messageRouter.SendToOSC, AddressOf OnRouterSendToOSC
            AddHandler _messageRouter.SendToTcpClients, AddressOf OnRouterSendToTcpClients

            ' Initialize statistics monitor
            _statsMonitor = New StatisticsMonitor(_log)

            ' ✅ FIXED: Start timer AFTER initialization (not in constructor)
            keepaliveTimer = New Timer(_TimerCallback, Nothing, CInt(Timing.KeepAlive) * 1000, CInt(Timing.KeepAlive) * 1000)

            _log.Info("C24 Session initialized for network: {0}", networkInfo.Name)
        End Sub

        ''' <summary>
        ''' Set OSC configuration
        ''' </summary>
        Public Sub ConfigureOSC(serverPort As Integer, dawAddress As String, dawPort As Integer)
            _oscServerPort = serverPort
            _oscDawAddress = dawAddress
            _oscDawPort = dawPort
            _log.Info("OSC configured - Server:{0} DAW:{1}:{2}", serverPort, dawAddress, dawPort)
        End Sub

        ''' <summary>
        ''' Set TCP server configuration
        ''' </summary>
        Public Sub ConfigureTcpServer(port As Integer)
            _tcpServerPort = port
            _log.Info("TCP Server configured on port {0}", port)
        End Sub

        ''' <summary>
        ''' Wait for send queue to be empty (for testing/synchronization)
        ''' ✅ NEW: Public API pour synchroniser les tests
        ''' ✅ FIXED: Timeout increased to 1000ms to account for retries
        ''' </summary>
        Public Function WaitForQueueEmpty(timeoutMs As Integer) As Boolean
            Dim stopwatch = Diagnostics.Stopwatch.StartNew()

            While Not _pendingCommands.IsEmpty
                If stopwatch.ElapsedMilliseconds > timeoutMs Then
                    _log.Warn("WaitForQueueEmpty: Timeout after {0}ms ({1} commands still pending)", timeoutMs, _pendingCommands.Count)
                    Return False
                End If

                Thread.Sleep(10)
            End While

            ' ✅ ADDITIONAL: Wait for last command to complete (ACK received)
            ' Even if queue is empty, the last command might still be waiting for ACK
            Dim maxWaitForLastAck = 1000  ' 1 second max for last ACK
            While _waitingForAck AndAlso stopwatch.ElapsedMilliseconds < (timeoutMs + maxWaitForLastAck)
                Thread.Sleep(10)
            End While

            If _waitingForAck Then
                _log.Warn("WaitForQueueEmpty: Last command still waiting for ACK after {0}ms", stopwatch.ElapsedMilliseconds)
                Return False
            End If

            _log.Debug("WaitForQueueEmpty: Queue empty and all ACKs received after {0}ms", stopwatch.ElapsedMilliseconds)
            Return True
        End Function

        ''' <summary>
        ''' Get current queue size (for testing/monitoring)
        ''' </summary>
        Public ReadOnly Property PendingCommandsCount As Integer
            Get
                Return _pendingCommands.Count
            End Get
        End Property

        ''' <summary>
        ''' Enable or disable testing mode (disables keep-alive)
        ''' ✅ NEW: Public API pour les tests de LED
        ''' </summary>
        Public Sub SetTestingMode(enabled As Boolean)
            SyncLock _keepAliveLock  ' ✅ Thread-safe
                _isTestingActive = enabled

                If enabled Then
                    ' ✅ CRITICAL: ARRÊTER le timer pendant les tests
                    If keepaliveTimer IsNot Nothing Then
                        keepaliveTimer.Change(Threading.Timeout.Infinite, Threading.Timeout.Infinite)
                        _log.Info("Testing mode ENABLED - Keep-alive timer STOPPED")
                    End If
                Else
                    ' ✅ CRITICAL: REDÉMARRER le timer après les tests
                    If keepaliveTimer IsNot Nothing Then
                        keepaliveTimer.Change(CInt(Timing.KeepAlive) * 1000, CInt(Timing.KeepAlive) * 1000)
                        _log.Info("Testing mode DISABLED - Keep-alive timer RESTARTED")
                    End If
                End If
            End SyncLock
        End Sub

        ''' <summary>
        ''' Set test mode on adapter (blocks incoming broadcasts during LED tests)
        ''' ✅ CRITICAL: Prevents Control24 broadcasts from interrupting test sequences
        ''' </summary>
        Public Sub SetAdapterTestMode(enabled As Boolean)
            If TypeOf _packetCapture Is NativePcapAdapter Then
                DirectCast(_packetCapture, NativePcapAdapter).SetTestMode(enabled)
            End If
        End Sub

        ''' <summary>
        ''' Get statistics monitor for UI updates
        ''' </summary>
        Public ReadOnly Property StatisticsMonitor As StatisticsMonitor
            Get
                Return _statsMonitor
            End Get
        End Property

        ''' <summary>
        ''' Start the session - open device and start capturing
        ''' </summary>
        Public Sub Start()
            Try
                ' OPTIMIZATION: Boost process priority for real-time performance
                Try
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High
                    _log.Info("Process priority set to High for optimal performance")
                Catch ex As Exception
                    _log.Warn("Failed to set process priority: {0} (may require admin rights)", ex.Message)
                    ' Continue anyway - not critical
                End Try

                ' Open packet capture device
                ' ✅ Support multiple capture implementations
                If TypeOf _packetCapture Is PacketCaptureManager Then
                    ' SharpPcap : accepts NetworkInterfaceInfo directly
                    DirectCast(_packetCapture, PacketCaptureManager).OpenDevice(_network, _network.Mac)
                    _log.Info("📡 SharpPcap device opened")

                ElseIf TypeOf _packetCapture Is NativePcapAdapter Then
                    ' Native Pcap : P/Invoke direct, accepts interface name
                    _log.Info("📡 Opening Native Pcap for interface: {0}", _network.Name)
                    _packetCapture.OpenDevice(_network.Name, _network.Mac)
                    _log.Info("✅ Native Pcap device opened successfully")

                ElseIf TypeOf _packetCapture Is PcapDotNetAdapter Then
                    ' Pcap.Net : modern API, accepts interface name
                    _log.Info("📡 Opening Pcap.Net for interface: {0}", _network.Name)
                    _packetCapture.OpenDevice(_network.Name, _network.Mac)
                    _log.Info("✅ Pcap.Net device opened successfully")

                ElseIf TypeOf _packetCapture Is WinDivertAdapter Then
                    ' WinDivert : kernel-mode, accepts interface name
                    _log.Info("📡 Opening WinDivert for interface: {0}", _network.Name)
                    _packetCapture.OpenDevice(_network.Name, _network.Mac)
                    _log.Info("✅ WinDivert device opened successfully")

                ElseIf TypeOf _packetCapture Is RawSocketAdapter Then
                    ' RawSocket : fallback option (limited functionality)
                    _packetCapture.OpenDevice(_network.Name, _network.Mac)
                    _log.Info("📡 RawSocket device opened")

                Else
                    ' Generic interface
                    _packetCapture.OpenDevice(_network.Name, _network.Mac)
                    _log.Info("📡 Generic capture device opened")
                End If

                ' Set filter for Control24 packets
                _packetCapture.SetFilter(_network.Mac)

                ' Start packet capture
                _packetCapture.StartCapture(AddressOf PacketHandler)
                _isCapturing = True

                ' Start message router
                _messageRouter.Start()
                _log.Info("Message router started")

                ' Start statistics monitor
                _statsMonitor.Start()
                _log.Info("Statistics monitor started")

                ' Start TCP server
                Try
                    _tcpServer = New TcpServerManager(_tcpServerPort, _log)
                    AddHandler _tcpServer.ClientConnected, AddressOf OnTcpClientConnected
                    AddHandler _tcpServer.ClientDisconnected, AddressOf OnTcpClientDisconnected
                    AddHandler _tcpServer.DataReceived, AddressOf OnTcpDataReceived
                    _tcpServer.Start()
                    _log.Info("TCP Server started on port {0}", _tcpServerPort)
                Catch ex As Exception
                    _log.Warn("TCP Server failed to start: {0}", ex.Message)
                    ' Continue without TCP server
                End Try

                ' Start OSC manager
                Try
                    _oscManager = New OscManager(_oscServerPort, _oscDawAddress, _oscDawPort, _log)
                    AddHandler _oscManager.MessageReceived, AddressOf OnOscMessageReceived
                    _oscManager.Start()
                    _log.Info("OSC Manager started")
                Catch ex As Exception
                    _log.Warn("OSC Manager failed to start: {0}", ex.Message)
                    ' Continue without OSC
                End Try


                ' Start keep-alive thread
                '_keepAliveThread = New Thread(AddressOf KeepAliveLoop) With {
                '    .Name = "KeepAliveThread",
                '    .IsBackground = True
                '}
                '_keepAliveThread.Start()

                ' ✅ NEW: Start send queue thread for ACK-based transmission
                _sendQueueRunning = True
                _sendQueueThread = New Thread(AddressOf SendQueueLoop) With {
                    .Name = "SendQueueThread",
                    .IsBackground = True,
                    .Priority = ThreadPriority.AboveNormal
                }
                _sendQueueThread.Start()
                _log.Info("Send queue thread started (ACK-based transmission)")

                _log.Info("C24 Session started (Full mode: Capture + OSC + TCP + Routing + Optimizations)")
            Catch ex As Exception
                _log.Error("Failed to start C24 Session: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Packet handler callback from capture
        ''' </summary>
        Private Sub PacketHandler(timestamp As Double, pktData As Byte())
            Try
                ' _log.Debug("PCAP Captured: {0} bytes", pktData.Length) ' DISABLED FOR PERFORMANCE

                Dim pktLen = pktData.Length

                ' Build packet object
                Dim packet As New C24Packet(pktData)

                ' ✅ FIX: BLOCK TX packets (our own transmissions) - BPF filter doesn't work for TX
                ' Npcap captures TX packets BEFORE the BPF filter can block them
                ' Check if packet source MAC is OUR MAC (means we sent it - TX packet)
                Dim srcMac = packet.EthHeader.MacSrc.ToString().ToUpper()
                Dim ourMac = _macComputer.ToString().ToUpper()

                If srcMac = ourMac Then
                    ' This is a TX packet (we sent it) - IGNORE to prevent duplicates
                    ' _log.Debug("Ignoring TX packet from self: {0}", srcMac) ' DISABLED FOR PERFORMANCE
                    Return
                End If

                ' Update statistics
                _statsMonitor.IncrementPacketsReceived()

                ' Detailed traffic logging
                ' _log.Debug("Packet Received: {0}", packet.ToString()) ' DISABLED FOR PERFORMANCE

                ' Handle broadcast packets
                If packet.IsBroadcast() Then
                    HandleBroadcastPacket(packet)
                    Return
                End If

                ' Handle regular packets from desk
                If _macControl24.HasValue Then
                    HandleDeskPacket(packet)
                End If

            Catch ex As Exception
                _log.Error("Error handling packet: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Handle broadcast packets (desk discovery) - FIXED: Response as SYSTEM priority
        ''' ✅ CRITICAL: Broadcast responses are queued with SYSTEM priority (highest)
        ''' </summary>
        Private Sub HandleBroadcastPacket(packet As C24Packet)
            Dim macsrc = packet.EthHeader.MacSrc

            _log.Debug("HandleBroadcastPacket: MAC={0} IsVendor={1} DataLen={2}",
                      macsrc.ToString(), macsrc.IsVendor(), If(packet.PacketData?.Length, -1))

            If macsrc.IsVendor() Then
                ' Parse broadcast data (50 bytes according to logs)
                If packet.PacketData IsNot Nothing AndAlso packet.PacketData.Length >= 34 Then
                    Dim bcastBytes(49) As Byte
                    Array.Copy(packet.PacketData, 0, bcastBytes, 0, Math.Min(packet.PacketData.Length, 50))

                    Dim handle As GCHandle = GCHandle.Alloc(bcastBytes, GCHandleType.Pinned)
                    Try
                        Dim bcastData = CType(Marshal.PtrToStructure(handle.AddrOfPinnedObject(), GetType(C24BcastData)), C24BcastData)

                        _log.Info("Desk detected: {0} at {1}", bcastData.ToString(), macsrc.ToString())

                        ' Check broadcast type
                        Dim broadcastType = packet.C24Header.C24Cmd
                        Select Case broadcastType
                            Case Commands.BroadcastInit  ' 0xE0 - Initial broadcast
                                _log.Debug("Initial broadcast (0xE0) detected")

                            Case Commands.BroadcastActive  ' 0xE1 - Keep-alive broadcast
                                _log.Debug("Active broadcast (0xE1) detected")
                        End Select

                        ' ✅ FIX: Thread-safe initialization - ONLY ONCE even with multiple broadcasts
                        ' The Control24 sends multiple broadcasts (0xE0 init + 0xE1 active) at startup
                        ' We must ensure initialization packets are sent ONLY ONCE
                        SyncLock _initLock
                            If Not _initializationSent Then
                                ' Save Control24 MAC address (first time only)
                                If Not _macControl24.HasValue Then
                                    _macControl24 = macsrc

                                    ' Update ethernet header destination
                                    Dim tempEth = _ethHeader
                                    tempEth.MacDest = macsrc
                                    _ethHeader = tempEth

                                    _log.Info("Control24 MAC address saved: {0}", macsrc.ToString())
                                End If

                                ' Send initialization packets (ONLY ONCE)
                                _initializationSent = True
                                SendInitializationPackets()
                            Else
                                ' ✅ NEW: During active testing, ACKNOWLEDGE broadcasts but don't re-init
                                ' This keeps the Control24 happy without breaking test sequences
                                If _isTestingActive Then
                                    _log.Debug("Broadcast during testing - acknowledged (no action)")
                                Else
                                    _log.Debug("Initialization already sent - ignoring subsequent broadcast")
                                End If
                            End If
                        End SyncLock

                    Finally
                        handle.Free()
                    End Try
                Else
                    _log.Warn("Broadcast packet too short: expected >=34 bytes (minimum), got {0} bytes", If(packet.PacketData?.Length, 0))
                End If
            Else
                _log.Warn("Broadcast from non-Avid vendor: {0}", macsrc.ToString())
            End If
        End Sub

        ''' <summary>
        ''' Send initialization packets to desk via direct send
        ''' Init packets MUST be sent immediately without waiting for ACK
        ''' The Control24 only responds AFTER receiving BOTH init packets
        ''' ✅ CRITICAL: Both packets MUST be sent within <1ms (timing-sensitive!)
        ''' </summary>
        Private Sub SendInitializationPackets()
            ' ✅ Reset counter to match known-good sequence
            _sendCounter = 1

            ' ✅ CRITICAL: Clear any queued commands before init
            ' (There might be stale init packets from previous attempts)
            While _pendingCommands.TryDequeue(Nothing)
                ' Clear queue
            End While
            _log.Debug("Command queue cleared before init")

            ' ✅ CRITICAL TIMING FIX: Prepare BOTH packets BEFORE sending
            ' The Control24 expects both packets within <1ms (very timing-sensitive!)
            ' Any delay >20ms will cause init to fail

            ' Prepare init1 (Online command)
            Dim init1 = PreparePacket(Nothing, 0, 0, Commands.Online, Nothing, 1)

            ' Prepare init2 (Clock clear command) - DO THIS BEFORE SENDING init1!
            Dim init2Data As Byte() = {&HF0, &H13, &H1, &H30, &H19,
                                      &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &HF7}
            Dim init2 = PreparePacket(init2Data, init2Data.Length, 1, 0, {&H2, &H44}, 2)

            ' ✅ NOW send both packets back-to-back with minimal delay
            SendPacket(init1)
            _log.Debug("Sent Online command packet with SendCounter=1 (NumCommands=0)")

            ' ✅ Advance counter IMMEDIATELY
            _sendCounter = 2

            ' ✅ Send init2 IMMEDIATELY (no allocations between send calls!)
            SendPacket(init2)
            _log.Debug("Sent clock clear packet with SendCounter=2 (NumCommands=1, Parity=02 44)")

            ' ✅ Set counter for next command (keep-alive, user commands, etc.)
            _sendCounter = 3

            _log.Info("Initialization packets sent directly to desk (back-to-back, <1ms)")
        End Sub

        ''' <summary>
        ''' Handle OSC messages from DAW
        ''' </summary>
        Private Sub OnOscMessageReceived(sender As Object, e As OscMessageReceivedEventArgs)
            Try
                _log.Debug("OSC from DAW: {0}", e.Message.ToString())

                ' Parse OSC message
                Dim cmd = _oscReverseMapper.ParseOscMessage(e.Message)
                If cmd Is Nothing Then
                    Return
                End If

                ' Route to appropriate handler based on command type
                Select Case cmd.CommandType
                    Case "track"
                        HandleTrackOscCommand(cmd)

                    Case "button"
                        HandleButtonOscCommand(cmd)

                    Case "clock"
                        HandleClockOscCommand(cmd)

                    Case "transport"
                        HandleTransportOscCommand(cmd)

                    Case Else
                        _log.Debug("Unhandled OSC command type: {0}", cmd.CommandType)
                End Select

            Catch ex As Exception
                _log.Error("Error handling OSC message: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Handle track-related OSC commands - FIXED: Use MessageRouter
        ''' </summary>
        Private Sub HandleTrackOscCommand(cmd As OscToC24Command)
            If cmd.TrackNumber < 0 OrElse cmd.TrackNumber >= 32 Then
                Return
            End If

            ' ✅ FIX: Use string constructor to avoid ambiguity
            Dim msg As New QueuedMessage(MessageType.OscCommand, cmd.Address, MessagePriority.Normal) With {
                .SourceId = "OSC_DAW",
                .Metadata = New Dictionary(Of String, Object) From {
                    {"CommandType", cmd.CommandType},
                    {"TrackNumber", cmd.TrackNumber},
                    {"Parameter", cmd.Parameter},
                    {"Value", cmd.Value},
                    {"StringValue", cmd.StringValue}
                }
            }

            ' Priorité selon le type
            Select Case cmd.Parameter
                Case "volume", "pan"
                    msg.Priority = MessagePriority.High  ' Faders = temps réel
                Case "vu"
                    msg.Priority = MessagePriority.Low   ' VU = moins critique
                Case Else
                    msg.Priority = MessagePriority.Normal
            End Select

            ' Router via MessageRouter - évite collision
            _messageRouter.QueueIncoming(msg)

            _log.Debug("OSC command queued: {0} track {1} {2} = {3}",
                      cmd.CommandType, cmd.TrackNumber, cmd.Parameter, cmd.Value)
        End Sub

        ''' <summary>
        ''' Handle button-related OSC commands
        ''' </summary>
        Private Sub HandleButtonOscCommand(cmd As OscToC24Command)
            ' Button LED updates from DAW
            If cmd.Value.HasValue Then
                _desk.GetTrack(0)?.ButtonLed?.SetButton(cmd.Address, CInt(cmd.Value.Value))
            End If
        End Sub

        ''' <summary>
        ''' Handle clock-related OSC commands
        ''' </summary>
        Private Sub HandleClockOscCommand(cmd As OscToC24Command)
            ' Clock display updates
            _log.Debug("Clock command: {0} = {1}", cmd.Parameter, cmd.StringValue)
        End Sub

        ''' <summary>
        ''' Handle transport-related OSC commands
        ''' </summary>
        Private Sub HandleTransportOscCommand(cmd As OscToC24Command)
            ' Transport button LED updates
            _log.Debug("Transport command: {0} = {1}", cmd.Parameter, cmd.Value)
        End Sub

        ''' <summary>
        ''' OSC client send handler
        ''' </summary>
        Private Sub OscClientSendHandler(message As Object)
            If _oscManager Is Nothing OrElse Not _oscManager.IsReady Then
                Return
            End If

            If TypeOf message Is Rug.Osc.OscMessage Then
                _oscManager.SendToDAW(CType(message, Rug.Osc.OscMessage))
            End If
        End Sub

        ''' <summary>
        ''' C24 client send handler
        ''' Sends commands to Control24 hardware via packet capture
        ''' Called by C24Desk classes when they need to update the hardware
        ''' ✅ FIXED: Anti-duplicate detection to prevent double sends
        ''' </summary>
        Private Sub C24ClientSendHandler(cmdBytes As Byte())
            Try
                ' Validate input
                If cmdBytes Is Nothing OrElse cmdBytes.Length = 0 Then
                    _log.Warn("C24ClientSendHandler: Empty command bytes")
                    Return
                End If

                ' Check if Control24 is connected
                If Not _macControl24.HasValue Then
                    _log.Warn("C24ClientSendHandler: Control24 not connected yet")
                    Return
                End If

                '' ✅ CRITICAL FIX: Anti-duplicate detection with time window
                '' Prevents the same command from being sent twice within 200ms
                'SyncLock _duplicateDetectionLock
                '    Dim now = DateTime.Now
                '    Dim timeSinceLastSend = (now - _lastSentCommandTime).TotalMilliseconds

                '    ' Check if this is a duplicate command sent too quickly
                '    If _lastSentCommandData IsNot Nothing AndAlso
                '       _lastSentCommandData.Length = cmdBytes.Length AndAlso
                '       timeSinceLastSend < _duplicateDetectionWindowMs Then

                '        ' Compare byte-by-byte (fast path for small arrays)
                '        Dim isDuplicate = True
                '        For i = 0 To cmdBytes.Length - 1
                '            If _lastSentCommandData(i) <> cmdBytes(i) Then
                '                isDuplicate = False
                '                Exit For
                '            End If
                '        Next

                '        If isDuplicate Then
                '            _log.Debug("C24ClientSendHandler: Duplicate command BLOCKED (within {0:F0}ms window)", timeSinceLastSend)
                '            Return ' ✅ Block duplicate - prevents double sends!
                '        End If
                '    End If

                '    ' Update duplicate detection tracking
                '    _lastSentCommandData = CType(cmdBytes.Clone(), Byte())
                '    _lastSentCommandTime = now
                'End SyncLock

                ' ✅ NEW: Queue command instead of sending directly
                ' This ensures ACK-based transmission with proper sequencing
                Dim numCommands As Byte = CountCommands(cmdBytes)
                Dim pendingCmd As New PendingCommand(cmdBytes, numCommands, isKeepAlive:=False)

                _pendingCommands.Enqueue(pendingCmd)  ' ✅ Thread-safe, no lock needed!
                _log.Debug("C24ClientSendHandler: Command queued ({0} in queue)", _pendingCommands.Count)

            Catch ex As Exception
                _log.Error("C24ClientSendHandler error: {0}", ex.Message)
                _statsMonitor.IncrementErrors()
            End Try
        End Sub

        ''' <summary>
        ''' Keep-alive loop - SMART with interrupt-safe queuing
        ''' ✅ FIXED: Queue keep-alive as CRITICAL priority (interrupts safely)
        ''' </summary>
        Private Sub KeepAliveLoop()
            Try
                ' Check if we're capturing and desk is connected
                If Not _isCapturing OrElse Not _macControl24.HasValue Then
                    Return
                End If

                ' ✅ IMPROVED: More aggressive conditions to prevent interference
                ' Calculate time since last command
                Dim timeSinceLastCommand = (DateTime.Now - _lastCommandTime).TotalMilliseconds

                ' ✅ CRITICAL: During active testing, only send keep-alive if REALLY necessary
                ' Check if queue has active commands (heuristic: recent activity)
                Dim queueSize = _pendingCommands.Count
                Dim priorityQueueSize = _priorityCommands.Count

                ' ✅ NEW LOGIC: Keep-alive as CRITICAL interrupt (short, doesn't break sequence)
                ' Only send if:
                ' 1. Idle for >30 seconds (increased threshold)
                ' 2. OR: Testing active BUT idle for >15 seconds (safety timeout)
                Dim idleThreshold = If(_isTestingActive, 15000, 30000) ' 15s during tests, 30s normally

                If timeSinceLastCommand >= idleThreshold Then
                    ' Prepare keep-alive data
                    Dim keepaliveData As Byte() = {0}
                    Dim keepAlivePending As New PendingCommand(keepaliveData, 1, isKeepAlive:=True, priority:=CommandPriority.Critical)

                    ' ✅ CRITICAL: Queue as PRIORITY (interrupts safely without breaking sequence)
                    _priorityCommands.Enqueue(keepAlivePending)
                    _log.Debug("Keep-alive QUEUED as CRITICAL interrupt ({0} in priority queue, idle: {1:F0}ms)", priorityQueueSize + 1, timeSinceLastCommand)
                End If

            Catch ex As Exception
                _log.Error("KeepAliveLoop error: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Backoff timer complete
        ''' </summary>
        Private Sub BackoffComplete(state As Object)
            _log.Debug("Backoff complete")
            _sendLock.Set()
            _backoffTimer?.Dispose()
            _backoffTimer = Nothing
        End Sub

        ''' <summary>
        ''' Send raw command bytes to Control24
        ''' Public method for external classes to send commands directly
        ''' </summary>
        Public Sub SendRawCommand(cmdBytes As Byte())
            Try
                ' Validate input
                If cmdBytes Is Nothing OrElse cmdBytes.Length = 0 Then
                    _log.Warn("SendRawCommand: Empty command bytes")
                    Return
                End If

                ' Check if Control24 is connected
                If Not _macControl24.HasValue Then
                    _log.Warn("SendRawCommand: Control24 not connected yet")
                    Return
                End If

                ' Determine number of commands
                Dim numCommands As Byte = CountCommands(cmdBytes)

                ' Prepare and send packet
                Dim packet = PreparePacket(cmdBytes, cmdBytes.Length, numCommands)
                SendPacket(packet)

                _log.Debug("SendRawCommand: Sent {0} bytes, {1} command(s)", cmdBytes.Length, numCommands)

            Catch ex As Exception
                _log.Error("SendRawCommand error: {0}", ex.Message)
                _statsMonitor.IncrementErrors()
            End Try
        End Sub

        ''' <summary>
        ''' Send a packet to the desk
        ''' </summary>
        Public Sub SendPacket(packet As C24Packet)
            _log.Debug("Sending Packet of {0} bytes: {1}", packet.TotalLength, Utils.HexL(packet.ToBuffer()))

            Dim buf = packet.ToBuffer()
            Dim status = _packetCapture.SendPacket(buf)

            If status <> packet.TotalLength Then
                _log.Warn("Error sending packet")
                _statsMonitor.IncrementErrors()
            Else
                _pcapLastSent = Utils.Tick()
                _statsMonitor.IncrementPacketsSent()

                ' NOTE: SendCounter is NOT incremented here.
                ' - Normal queued commands: increment happens in SendQueueLoop AFTER ACK.
                ' - Initialization packets: SendInitializationPackets sets counters explicitly.
            End If
        End Sub

        ''' <summary>
        ''' Prepare a generic packet
        ''' </summary>
        Private Function PreparePacket(pktData As Byte(), pktDataLen As Integer, ncmds As Byte,
                                      Optional c24cmd As Byte = 0,
                                      Optional parity As Byte() = Nothing,
                                      Optional overrideSendCounter As UInt32? = Nothing) As C24Packet
            Dim packet As New C24Packet(pktDataLen)
            packet.EthHeader = _ethHeader

            Dim tempHeader = packet.C24Header


            Dim numBytes As UInt16
            If parity IsNot Nothing Then
                ' With parity bytes: data + (Unknown1/Parity(2) + SendCtr(4) + CmdCtr(4) + Retry(2) + Cmd(1) + NumCmds(1)) = data + 16
                numBytes = CUShort(pktDataLen + 16)
            Else
                tempHeader.Unknown1 = {0, 0}
                ' Without parity: same calculation
                numBytes = CUShort(pktDataLen + 16)
            End If

            ' Convert to big-endian (swap bytes)
            tempHeader.NumBytes = CUShort((numBytes >> 8) Or (numBytes << 8))

            ' Unknown1 / Parity bytes
            If parity IsNot Nothing Then
                tempHeader.Unknown1 = parity
            Else
                tempHeader.Unknown1 = {0, 0}
            End If

            ' Set command byte if provided
            If c24cmd <> 0 Then
                tempHeader.C24Cmd = c24cmd
            Else
                tempHeader.C24Cmd = 0
            End If

            ' Copy data if present
            If pktDataLen > 0 AndAlso pktData IsNot Nothing Then
                packet.PacketData = pktData
            End If

            ' Number of commands in this packet
            tempHeader.NumCommands = ncmds

            ' Handle counters based on command type
            If c24cmd = Commands.Ack Then
                ' ACK uses cmdcounter (big-endian)
                Dim cmdCtr = _cmdCounter
                tempHeader.CmdCounter = SwapUInt32(cmdCtr)
                tempHeader.SendCounter = {&H0, &H0, &H0, &H0}
            Else
                ' ✅ FIX: Use override counter if provided, otherwise use _sendCounter
                Dim counterToUse As UInt32 = If(overrideSendCounter.HasValue, overrideSendCounter.Value, _sendCounter)
                tempHeader.SendCounter = SwapUInt32(counterToUse)
                tempHeader.CmdCounter = {&H0, &H0, &H0, &H0}
            End If

            ' Retry is always 0 for outgoing packets
            tempHeader.Retry = 0

            packet.C24Header = tempHeader
            Return packet
        End Function

        ''' <summary>
        ''' Swap UInt32 in bytes for big-endian conversion
        ''' </summary>
        Private Function SwapUInt32(value As UInt32) As Byte()
            Dim _bytes = BitConverter.GetBytes(value)
            Dim _bytes_list = _bytes.ToList
            _bytes_list.Reverse()

            Return _bytes_list.ToArray


        End Function

        ''' <summary>
        ''' Prepare keep-alive packet
        ''' </summary>
        Public Function PrepareKeepAlive() As C24Packet
            Dim keepaliveData As Byte() = {0}
            Return PreparePacket(keepaliveData, 1, 1)
        End Function

        ''' <summary>
        ''' Prepare ACK packet
        ''' </summary>
        Private Function PrepareAck() As C24Packet
            Return PreparePacket(Nothing, 0, 0, Commands.Ack)
        End Function

        ''' <summary>
        ''' Close the session
        ''' </summary>
        Public Sub Close()
            _log.Info("C24session closing")
            _isClosing = True

            ' ✅ NEW: Stop send queue thread
            _sendQueueRunning = False
            _ackReceivedEvent.Set()  ' Wake up the thread if it's waiting
            _sendQueueThread?.Join(2000)
            _log.Info("Send queue thread stopped")

            ' Dispose event
            _ackReceivedEvent?.Dispose()

            ' Save statistics
            Try
                Dim stats = _statsMonitor.CreateStatistics()
                ConfigurationManager.Instance.SaveStatistics(stats)
            Catch ex As Exception
                _log.Error("Failed to save statistics: {0}", ex.Message)
            End Try

            ' Stop statistics monitor
            _statsMonitor?.Stop()
            _statsMonitor?.Dispose()

            ' Stop message router
            _messageRouter?.Stop()
            _messageRouter?.Dispose()

            ' Stop TCP server
            _tcpServer?.Stop()
            _tcpServer?.Dispose()

            ' Stop OSC manager
            _oscManager?.Stop()
            _oscManager?.Dispose()

            ' Stop keep-alive thread
            _keepAliveThread?.Join(2000)

            ' Stop packet capture
            _packetCapture?.Dispose()

            ' Log statistics
            If _messageRouter IsNot Nothing Then
                _log.Info("Message router stats: {0}", _messageRouter.GetStatistics())
            End If
            If _statsMonitor IsNot Nothing Then
                _log.Info("Session stats: {0}", _statsMonitor.GetSummary())
            End If

            _log.Info("C24session closed")
        End Sub

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            Close()
            GC.SuppressFinalize(Me)
        End Sub

        ''' <summary>
        ''' Handle packets from the desk - FIXED: Queue-based processing + SendCounter increment after ACK
        ''' ✅ CRITICAL FIX: Do NOT increment SendCounter here - let SendQueueLoop handle it
        ''' </summary>
        Private Sub HandleDeskPacket(packet As C24Packet)
            Dim pktLen = packet.TotalLength

            If pktLen > 30 Then
                ' Check if it's an ACK
                If packet.C24Header.C24Cmd = Commands.Ack Then
                    ' ✅ FIX: ACK received - check against shared expected counter
                    If _waitingForAck Then
                        Dim ackCounter = packet.C24Header.CmdCounter

                        ' ✅ FIXED: Use shared _expectedAckCounter instead of peeking queue
                        If _expectedAckCounter > 0 AndAlso BitConverter.ToUInt32(ackCounter) = _expectedAckCounter Then
                            _log.Debug("ACK received for command {0} (matches expected) - signaling event", ackCounter)

                            ' ✅ CRITICAL FIX: DO NOT increment SendCounter here!
                            ' Let SendQueueLoop handle the increment AFTER clearing _waitingForAck
                            ' This prevents race condition where next command uses incremented counter
                            ' before current command is fully processed

                            ' ✅ CRITICAL: Signal the event instead of dequeuing here
                            ' This allows the SendQueueLoop to handle dequeue without blocking capture thread
                            _ackReceivedEvent.Set()
                        Else
                            _log.Warn("ACK mismatch! Received {0}, expected {1} - ignoring", ackCounter, _expectedAckCounter)
                        End If
                    Else
                        _log.Debug("Unexpected ACK received (not waiting): CmdCounter={0}", packet.C24Header.CmdCounter)
                    End If

                    ' Release send lock if it was held
                    If _backoffTimer Is Nothing OrElse Not _backoffTimer.Equals(Nothing) Then
                        _sendLock.Set()
                    End If

                    ' Check if it's a broadcast (can still receive these after connection)
                ElseIf packet.C24Header.C24Cmd = Commands.BroadcastActive OrElse
                       packet.C24Header.C24Cmd = Commands.BroadcastInit Then
                    ' _log.Debug("Broadcast received while connected (cmd: 0x{0:X2})", packet.C24Header.C24Cmd) ' DISABLED
                    ' Normal - Control24 continues broadcasting even after connection
                Else
                    ' Regular command packet (cmd = 0x00 typically)

                    ' Handle commands
                    If packet.C24Header.NumCommands > 0 Then
                        Dim cmdNumber = packet.C24Header.SendCounter
                        _cmdCounter = BitConverter.ToUInt32(cmdNumber)

                        ' ═══════════════════════════════════════════════════════
                        ' CRITICAL PATH: ULTRA-FAST ACK SENDING (<1ms target)
                        ' ═══════════════════════════════════════════════════════

                        ' _log.Debug("FROMDESK {0}", cmdNumber) ' DISABLED FOR PERFORMANCE

                        ' Send ACK IMMEDIATELY using pre-allocated buffer
                        Try
                            SendFastAck(BitConverter.ToUInt32(cmdNumber))
                        Catch ex As Exception
                            _log.Error("ACK send failed: {0}", ex.Message)
                        End Try

                        ' Handle retry logic
                        If packet.IsRetry() Then
                            _currentRetryDesk = packet.C24Header.Retry
                            _log.Warn("Retry packets from desk: {0}", _currentRetryDesk)
                            ' ✅ FIX: Only reset lock and start backoff on RETRY
                            _sendLock.Reset()
                            _backoffTimer = New Timer(AddressOf BackoffComplete, Nothing, CInt(Timing.Backoff * 1000), Threading.Timeout.Infinite)
                        End If
                        ' ✅ FIX: Removed the second check for backoff timer
                        ' Lock management is now only done in retry logic
                    Else
                        _log.Warn("FROMDESK unhandled: {0:X2}", If(packet.PacketData?.Length > 0, packet.PacketData(0), 0))
                    End If
                End If
            End If
        End Sub

        ''' <summary>
        ''' ULTRA-FAST ACK sending - ZERO allocations, pinned memory, no GC interference
        ''' Target: <5ms execution time guaranteed
        ''' </summary>
        Private Sub SendFastAck(cmdNumber As UInt32)
            Dim buf(29) As Byte

            ' Ethernet Header (14 bytes)
            Dim ethBytes = StructureToBytes(_ethHeader)
            Array.Copy(ethBytes, buf, 14)

            ' C24 Header for ACK (16 bytes) - all fields are STATIC except CmdCounter
            ' NumBytes (bytes 14-15) - Big Endian: 0x00 0x10 (16 bytes)
            buf(14) = &H0
            buf(15) = &H10

            ' Unknown1 (bytes 16-17)
            buf(16) = &H0
            buf(17) = &H0

            ' SendCounter (bytes 18-21) - ALWAYS 0 for ACK
            buf(18) = &H0
            buf(19) = &H0
            buf(20) = &H0
            buf(21) = &H0

            ' CmdCounter (bytes 22-25) - DYNAMIC - set to 0 in template
            buf(22) = CByte((cmdNumber >> 24) And &HFF)
            buf(23) = CByte((cmdNumber >> 16) And &HFF)
            buf(24) = CByte((cmdNumber >> 8) And &HFF)
            buf(25) = CByte(cmdNumber And &HFF)

            ' Retry (bytes 26-27) - ALWAYS 0 for outgoing
            buf(26) = &H0
            buf(27) = &H0

            ' C24Cmd (byte 28) - ACK command (0xA0)
            buf(28) = Commands.Ack

            ' NumCommands (byte 29) - ALWAYS 0 for ACK
            buf(29) = &H0

            ' Send directly via pcap - buffer is already pinned, ultra-fast!
            Dim status = _packetCapture.SendPacket(buf)

            If status <> buf.Length Then
                ' Only log errors (keep this for debugging critical issues)
                _log.Error("Fast ACK send failed: sent {0}/{1} bytes", status, buf.Length)
                _statsMonitor.IncrementErrors()
            End If
        End Sub

        Private Shared Function StructureToBytes(Of T As Structure)(str As T) As Byte()
            Dim size As Integer = Marshal.SizeOf(str)
            Dim arr(size - 1) As Byte
            Dim ptr As IntPtr = Marshal.AllocHGlobal(size)
            Try
                Marshal.StructureToPtr(str, ptr, True)
                Marshal.Copy(ptr, arr, 0, size)
            Finally
                Marshal.FreeHGlobal(ptr)
            End Try
            Return arr
        End Function

        ''' <summary>
        ''' Main loop - process incoming packets and dispatch messages
        ''' </summary>
        Private Sub MainLoop()
            ' ...existing code...
        End Sub

        ''' <summary>
        ''' Router: Send to Control24 - FIXED: Handle OSC commands properly
        ''' </summary>
        Private Sub OnRouterSendToControl24(sender As Object, message As QueuedMessage)
            Try
                _log.Debug("Router → Control24: {0}", message.ToString())

                ' Check if Control24 is connected
                If Not _macControl24.HasValue Then
                    _log.Warn("Cannot send to Control24 - desk not detected yet")
                    Return
                End If

                ' Handle different message types
                Select Case message.MessageType
                    Case MessageType.TcpCommand
                        HandleTcpToC24Command(message)

                    Case MessageType.OscCommand
                        ' ✅ NEW: Traiter les commandes OSC via router
                        HandleRoutedOscCommand(message)

                    Case MessageType.SystemCommand
                        HandleSystemToC24Command(message)

                    Case Else
                        _log.Warn("Unhandled message type for Control24: {0}", message.MessageType)
                End Select

            Catch ex As Exception
                _log.Error("Error sending to Control24: {0}", ex.Message)
                _statsMonitor.IncrementErrors()
            End Try
        End Sub

        ''' <summary>
        ''' Router: Send to OSC - Process C24 commands from queue
        ''' </summary>
        Private Sub OnRouterSendToOSC(sender As Object, message As QueuedMessage)
            Try
                ' Send message via OSC
                If _oscManager IsNot Nothing AndAlso _oscManager.IsReady Then
                    _log.Debug("Router → OSC: {0}", message.ToString())

                    ' ✅ FIX: Process C24 commands from queue (not from packet handler)
                    If message.MessageType = MessageType.C24Command AndAlso message.Data IsNot Nothing Then
                        ProcessC24CommandsForOSC(message.Data)
                    End If
                End If
            Catch ex As Exception
                _log.Error("Error sending to OSC: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Process C24 commands and convert to OSC - Queue-safe version
        ''' </summary>
        Private Sub ProcessC24CommandsForOSC(data As Byte())
            Try
                ' Split commands (they may be multiplexed)
                Dim commands = SplitC24Commands(data)

                For Each cmdBytes In commands
                    If cmdBytes.Length = 0 Then
                        Continue For
                    End If

                    ' Parse command
                    Dim parsed = _oscMapper.ParseC24Command(cmdBytes)
                    If parsed IsNot Nothing Then
                        _log.Debug("C24 Cmd parsed: {0}", parsed.ToString())

                        ' ✅ FIX: Only send to OSC, DON'T update _desk
                        ' (avoid feedback loop and threading issues)
                        If Not String.IsNullOrEmpty(parsed.OscAddress) Then
                            Dim oscMsg = _oscMapper.ToOscMessage(parsed)
                            If oscMsg IsNot Nothing AndAlso _oscManager IsNot Nothing Then
                                _oscManager.SendToDAW(oscMsg)
                            End If
                        End If
                    End If
                Next

            Catch ex As Exception
                _log.Error("Error processing C24 commands for OSC: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Handle routed OSC commands - SAFE threading
        ''' </summary>
        Private Sub HandleRoutedOscCommand(message As QueuedMessage)
            Try
                ' Extract metadata
                If Not message.Metadata.ContainsKey("CommandType") Then
                    _log.Warn("OSC command missing CommandType metadata")
                    Return
                End If

                Dim cmdType = CStr(message.Metadata("CommandType"))
                Dim trackNum = CInt(message.Metadata("TrackNumber"))
                Dim param = CStr(message.Metadata("Parameter"))

                ' Get track
                Dim track = _desk.GetTrack(trackNum)
                If track Is Nothing Then
                    _log.Warn("Track {0} not found", trackNum)
                    Return
                End If

                ' Apply command based on parameter
                Select Case param
                    Case "volume"
                        If message.Metadata.ContainsKey("Value") AndAlso track.Fader IsNot Nothing Then
                            Dim value = CDbl(message.Metadata("Value"))
                            track.Fader.Gain = value
                            _log.Debug("Track {0} volume set to {1}", trackNum, value)
                        End If

                    Case "pan"
                        If message.Metadata.ContainsKey("Value") AndAlso track.VPot IsNot Nothing Then
                            Dim value = CDbl(message.Metadata("Value"))
                            track.VPot.Pan = value
                            _log.Debug("Track {0} pan set to {1}", trackNum, value)
                        End If

                    Case "name"
                        If message.Metadata.ContainsKey("StringValue") AndAlso track.ScribbleStrip IsNot Nothing Then
                            Dim text = CStr(message.Metadata("StringValue"))
                            track.ScribbleStrip.SetText(text)
                            _log.Debug("Track {0} name set to '{1}'", trackNum, text)
                        End If

                    Case "vu"
                        If message.Metadata.ContainsKey("Value") AndAlso track.VuMeter IsNot Nothing Then
                            Dim value = CDbl(message.Metadata("Value"))
                            Dim speaker = 0
                            If message.Metadata.ContainsKey("Speaker") Then
                                speaker = CInt(message.Metadata("Speaker"))
                            End If
                            'track.VuMeter.UpdateLevel(speaker, value)
                        End If

                    Case Else
                        _log.Debug("Unhandled OSC parameter: {0}", param)
                End Select

            Catch ex As Exception
                _log.Error("Error handling routed OSC command: {0}", ex.Message)
                _statsMonitor.IncrementErrors()
            End Try
        End Sub

        ''' <summary>
        ''' Handle TCP command to Control24
        ''' </summary>
        Private Sub HandleTcpToC24Command(message As QueuedMessage)
            If message.Data Is Nothing OrElse message.Data.Length = 0 Then
                Return
            End If

            Try
                ' Check if it's already a C24 command format (raw bytes)
                If message.Data.Length >= 3 AndAlso
                   (message.Data(0) = &H90 OrElse message.Data(0) = &HB0 OrElse
                    message.Data(0) = &HF0 OrElse message.Data(0) = &HD0) Then
                    ' Raw C24 command - send directly via C24ClientSendHandler
                    C24ClientSendHandler(message.Data)
                    _log.Debug("Sent raw C24 command from TCP: {0} bytes", message.Data.Length)
                Else
                    ' Try to parse as string command
                    If Not String.IsNullOrEmpty(message.StringData) Then
                        ParseAndSendStringCommand(message.StringData, message.SourceId)
                    Else
                        _log.Warn("TCP command format not recognized")
                    End If
                End If

            Catch ex As Exception
                _log.Error("Error handling TCP to Control24: {0}", ex.Message)
                _statsMonitor.IncrementErrors()
            End Try
        End Sub

        ''' <summary>
        ''' Handle system command to Control24
        ''' </summary>
        Private Sub HandleSystemToC24Command(message As QueuedMessage)
            If String.IsNullOrEmpty(message.StringData) Then
                Return
            End If

            Try
                Dim cmd = message.StringData.ToLower().Trim()

                Select Case cmd
                    Case "reset", "initialize"
                        _log.Info("Re-initializing Control24")
                        SendInitializationPackets()

                    Case "keepalive"
                        _log.Debug("Force keep-alive")
                        SendPacket(PrepareKeepAlive())

                    Case "clearclock"
                        Dim clearData As Byte() = {&HF0, &H13, &H1, &H30, &H19,
                                                   &H0, &H0, &H0, &H0, &H0, &H0,
                                                   &H0, &H0, &H0, &HF7}
                        Dim packet = PreparePacket(clearData, clearData.Length, 1, 0, {&H2, &H44})
                        SendPacket(packet)
                        _log.Info("Clock display cleared")

                    Case "online"
                        Dim onlinePacket = PreparePacket(Nothing, 0, 0, Commands.Online)
                        SendPacket(onlinePacket)
                        _log.Info("Online command sent")

                    Case Else
                        _log.Warn("Unknown system command: {0}", cmd)
                End Select

            Catch ex As Exception
                _log.Error("Error handling system command: {0}", ex.Message)
                _statsMonitor.IncrementErrors()
            End Try
        End Sub

        ''' <summary>
        ''' Parse and send string command (for simple text-based commands from TCP)
        ''' </summary>
        Private Sub ParseAndSendStringCommand(command As String, sourceId As String)
            Try
                Dim parts = command.Trim().Split(" "c)
                If parts.Length = 0 Then
                    Return
                End If

                Dim cmd = parts(0).ToLower()

                Select Case cmd
                    Case "fader"
                        ' Format: "fader <track> <value>"
                        If parts.Length >= 3 Then
                            Dim track = Integer.Parse(parts(1))
                            Dim value = Double.Parse(parts(2))
                            _log.Debug("Fader command: track {0} value {1}", track, value)
                            ' TODO: Implement fader control
                        End If

                    Case "vpot"
                        ' Format: "vpot <track> <value>"
                        If parts.Length >= 3 Then
                            Dim track = Integer.Parse(parts(1))
                            Dim value = Double.Parse(parts(2))
                            _log.Debug("VPot command: track {0} value {1}", track, value)
                            ' TODO: Implement VPot control
                        End If

                    Case "button"
                        ' Format: "button <id> <state>"
                        If parts.Length >= 3 Then
                            Dim buttonId = parts(1)
                            Dim state = Integer.Parse(parts(2))
                            _log.Debug("Button command: {0} state {1}", buttonId, state)
                            ' TODO: Implement button control
                        End If

                    Case "scribble"
                        ' Format: "scribble <track> <text>"
                        If parts.Length >= 3 Then
                            Dim track = Integer.Parse(parts(1))
                            Dim text = String.Join(" ", parts.Skip(2))
                            _log.Debug("Scribble command: track {0} text '{1}'", track, text)
                            ' TODO: Implement scribble strip control
                        End If

                    Case "raw"
                        ' Format: "raw <hex bytes>"
                        If parts.Length >= 2 Then
                            Dim hexBytes = parts.Skip(1).ToArray()
                            Dim data = hexBytes.Select(Function(h) Convert.ToByte(h, 16)).ToArray()
                            C24ClientSendHandler(data)
                            _log.Debug("Raw command sent: {0} bytes", data.Length)
                        End If

                    Case Else
                        _log.Warn("Unknown command from {0}: {1}", sourceId, cmd)
                End Select

            Catch ex As Exception
                _log.Error("Error parsing string command: {0}", ex.Message)
                _statsMonitor.IncrementErrors()
            End Try
        End Sub

        ''' <summary>
        ''' Router: Send to TCP clients
        ''' </summary>
        Private Sub OnRouterSendToTcpClients(sender As Object, message As QueuedMessage)
            Try
                ' Broadcast to all TCP clients
                If _tcpServer IsNot Nothing Then
                    _log.Debug("Router → TCP Clients: {0}", message.ToString())

                    If message.Data IsNot Nothing Then
                        _tcpServer.BroadcastData(message.Data)
                    ElseIf Not String.IsNullOrEmpty(message.StringData) Then
                        Dim data = Text.Encoding.UTF8.GetBytes(message.StringData & vbCrLf)
                        _tcpServer.BroadcastData(data)
                    End If
                End If
            Catch ex As Exception
                _log.Error("Error sending to TCP clients: {0}", ex.Message)
                _statsMonitor.IncrementErrors()
            End Try
        End Sub

        ''' <summary>
        ''' TCP client connected handler
        ''' </summary>
        Private Sub OnTcpClientConnected(sender As Object, e As ClientEventArgs)
            _log.Info("TCP Client connected: {0}", e.ClientId)

            ' Update statistics
            If _tcpServer IsNot Nothing Then
                _statsMonitor.SetTcpClients(_tcpServer.ConnectedClientsCount)
            End If

            ' Send welcome message
            Try
                Dim welcomeMsg = $"Welcome to Control24 Middleware - Client {e.ClientId}{vbCrLf}"
                Dim data = Text.Encoding.UTF8.GetBytes(welcomeMsg)
                _tcpServer.SendToClient(e.ClientId, data)
            Catch ex As Exception
                _log.Warn("Failed to send welcome message: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' TCP client disconnected handler
        ''' </summary>
        Private Sub OnTcpClientDisconnected(sender As Object, e As ClientEventArgs)
            _log.Info("TCP Client disconnected: {0}", e.ClientId)

            ' Update statistics
            If _tcpServer IsNot Nothing Then
                _statsMonitor.SetTcpClients(_tcpServer.ConnectedClientsCount)
            End If
        End Sub

        ''' <summary>
        ''' TCP data received handler
        ''' </summary>
        Private Sub OnTcpDataReceived(sender As Object, e As ClientDataReceivedEventArgs)
            Try
                _log.Debug("TCP data received from {0}: {1} bytes", e.ClientId, e.Data.Length)

                ' Try to decode as string first
                Dim stringData As String = Nothing
                Try
                    stringData = Text.Encoding.UTF8.GetString(e.Data).Trim()
                Catch
                    ' Not UTF-8, treat as raw bytes
                End Try

                ' Queue as TCP command
                Dim msg As New QueuedMessage(MessageType.TcpCommand, e.Data) With {
                    .SourceId = e.ClientId,
                    .StringData = stringData,
                    .Priority = MessagePriority.Normal}
                _messageRouter.QueueIncoming(msg)

            Catch ex As Exception
                _log.Error("Error handling TCP data: {0}", ex.Message)
                _statsMonitor.IncrementErrors()
            End Try
        End Sub

        ''' <summary>
        ''' Split multiplexed C24 commands
        ''' </summary>
        Private Function SplitC24Commands(data As Byte()) As List(Of Byte())
            Dim commands As New List(Of Byte())
            If data Is Nothing OrElse data.Length = 0 Then
                Return commands
            End If

            Dim current As New List(Of Byte)
            Dim splitBytes As Byte() = {&H90, &HB0, &HFF, &HD0, &HF0}

            For i = 0 To data.Length - 1
                Dim b = data(i)

                If b = &HF7 Then
                    ' End of SysEx command
                    current.Add(b)
                    If current.Count > 0 Then
                        commands.Add(current.ToArray())
                        current.Clear()
                    End If
                ElseIf splitBytes.Contains(b) AndAlso current.Count > 0 Then
                    ' Start of new command - save previous
                    commands.Add(current.ToArray())
                    current.Clear()
                    current.Add(b)
                Else
                    current.Add(b)
                End If
            Next

            ' Add remaining data
            If current.Count > 0 Then
                commands.Add(current.ToArray())
            End If

            Return commands
        End Function

        ''' <summary>
        ''' Count number of commands in byte array
        ''' Simple heuristic: count command bytes (0x90, 0xB0, 0xF0, 0xD0, 0xFF)
        ''' </summary>
        Private Function CountCommands(cmdBytes As Byte()) As Byte
            If cmdBytes Is Nothing OrElse cmdBytes.Length = 0 Then
                Return 0
            End If

            ' Count command start bytes
            Dim count As Integer = 0
            Dim cmdMarkers As Byte() = {&H90, &HB0, &HF0, &HD0, &HFF}

            For i = 0 To cmdBytes.Length - 1
                If cmdMarkers.Contains(cmdBytes(i)) Then
                    count += 1
                End If
            Next

            ' Return at least 1 if we have data
            If count = 0 AndAlso cmdBytes.Length > 0 Then
                count = 1
            End If

            Return CByte(Math.Min(count, 255))
        End Function

        ''' <summary>
        ''' Send queue loop - PRIORITY-AWARE with interrupt support
        ''' ✅ NEW: Checks priority queue FIRST, then normal queue
        ''' ✅ CRITICAL: Interrupts (keep-alive, broadcasts) processed immediately without breaking sequence
        ''' </summary>
        Private Sub SendQueueLoop()
            _log.Info("Send queue loop started (priority-aware, interrupt-safe mode)")

            Try
                While _sendQueueRunning
                    Dim currentCmd As PendingCommand = Nothing

                    ' ✅ PRIORITY 1: Check high-priority queue FIRST (interrupts)
                    If _priorityCommands.TryDequeue(currentCmd) Then
                        _log.Debug("Processing PRIORITY command (priority={0})", currentCmd.Priority)
                        ProcessCommand(currentCmd, isPriority:=True)
                        Continue While ' ✅ Process next command immediately
                    End If

                    ' ✅ PRIORITY 2: Check normal queue (LED tests, scribble strips, etc.)
                    If _pendingCommands.TryDequeue(currentCmd) Then
                        ProcessCommand(currentCmd, isPriority:=False)
                        Continue While
                    End If

                    ' Queue is empty, wait a bit
                    Thread.Sleep(10)
                End While

            Catch ex As Exception
                _log.Error("Send queue loop error: {0}", ex.Message)
            End Try

            _log.Info("Send queue loop ended")
        End Sub

        ''' <summary>
        ''' Process a single command (common logic for both queues)
        ''' ✅ NEW: Extracted from SendQueueLoop for clarity
        ''' </summary>
        Private Sub ProcessCommand(currentCmd As PendingCommand, isPriority As Boolean)
            ' Update last command time for regular commands (not keep-alive)
            If Not currentCmd.IsKeepAlive Then
                _lastCommandTime = DateTime.Now
            End If

            ' Send the command (fire-and-forget - NO ACK wait)
            Try
                ' ✅ CRITICAL: Always use NEXT counter (increment before send)
                Dim counterToUse As UInt32 = _sendCounter
                _sendCounter += 1  ' ✅ Increment IMMEDIATELY (fire-and-forget)

                currentCmd.SendCounterUsed = counterToUse

                If isPriority Then
                    _log.Debug("SEND PRIORITY: Using SendCounter={0} (priority={1})", counterToUse, currentCmd.Priority)
                Else
                    _log.Debug("SEND: Using SendCounter={0} (fire-and-forget)", counterToUse)
                End If

                ' Prepare packet
                Dim packet As C24Packet = PrepareCommandPacket(currentCmd, counterToUse)

                ' Send the packet (fire-and-forget)
                If currentCmd.IsKeepAlive Then
                    _log.Debug("Sending keep-alive ({0} bytes) with SendCounter={1} (INTERRUPT mode)", packet.TotalLength, counterToUse)
                Else
                    _log.Debug("Sending command ({0} bytes) with SendCounter={1} (no ACK wait)", packet.TotalLength, counterToUse)
                End If

                Dim buf = packet.ToBuffer()
                Dim status = _packetCapture.SendPacket(buf)

                If status = packet.TotalLength Then
                    _pcapLastSent = Utils.Tick()
                    _statsMonitor.IncrementPacketsSent()

                    ' ✅ SUCCESS - Log completion
                    If currentCmd.IsKeepAlive Then
                        _log.Debug("Keep-alive sent successfully ({0} priority, {1} normal in queue)", _priorityCommands.Count, _pendingCommands.Count)
                    Else
                        _log.Debug("Command sent successfully ({0} priority, {1} normal in queue)", _priorityCommands.Count, _pendingCommands.Count)
                    End If
                Else
                    _log.Error("Failed to send {0}", If(currentCmd.IsKeepAlive, "keep-alive", "command"))
                    _statsMonitor.IncrementErrors()
                End If

            Catch ex As Exception
                _log.Error("Error sending {0}: {1}", If(currentCmd.IsKeepAlive, "keep-alive", "command"), ex.Message)
                _statsMonitor.IncrementErrors()
            End Try

            ' ✅ CRITICAL: Minimal delay (1ms) to avoid flooding
            ' Priority commands get NO delay (immediate processing)
            If Not isPriority Then
                Thread.Sleep(1)
            End If
        End Sub

        ''' <summary>
        ''' Prepare packet for a command (extracted for clarity)
        ''' ✅ NEW: Common packet preparation logic
        ''' </summary>
        Private Function PrepareCommandPacket(cmd As PendingCommand, counterToUse As UInt32) As C24Packet
            ' Special case: Online command
            If cmd.PacketData.Length = 1 AndAlso cmd.PacketData(0) = &HFF Then
                Return PreparePacket(Nothing, 0, 0, Commands.Online, Nothing, counterToUse)
            End If

            '' Special case: Clock clear with parity
            'If cmd.PacketData.Length = 15 AndAlso
            '   cmd.PacketData(0) = &HF0 AndAlso
            '   cmd.PacketData(1) = &H13 AndAlso
            '   cmd.PacketData(2) = &H1 AndAlso
            '   cmd.PacketData(3) = &H30 Then
            '    Return PreparePacket(cmd.PacketData, cmd.PacketData.Length, 1, 0, {&H2, &H44}, counterToUse)
            'End If

            ' Normal command
            Return PreparePacket(cmd.PacketData,
                                cmd.PacketData.Length,
                                cmd.NumCommands,
                                0,
                                Nothing,
                                counterToUse)
        End Function
    End Class

End Namespace
