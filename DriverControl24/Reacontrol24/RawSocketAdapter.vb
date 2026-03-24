''' <summary>
''' Raw Socket implementation of INetworkCapture
''' Alternative to SharpPcap - uses .NET raw sockets (Windows only)
''' Does not require external drivers (Npcap/WinPcap)
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' </summary>

Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Runtime.InteropServices
Imports System.Threading

Namespace Reacontrol24

    ''' <summary>
    ''' Raw Socket adapter implementing INetworkCapture
    ''' Uses Windows raw sockets (requires admin privileges)
    ''' </summary>
    Public Class RawSocketAdapter
        Implements INetworkCapture

        Private _socket As Socket
        Private _log As Logger
        Private _isCapturing As Boolean
        Private _captureHandler As Action(Of Byte())
        Private _captureThread As Thread
        Private _selectedInterface As NetworkInterface
        Private _localAddress As IPAddress

        Private Const IOC_VENDOR As Long = &H18000000
        Private Const IOC_IN As Long = &H80000000
        Private Const SIO_RCVALL As Integer = IOC_IN Or IOC_VENDOR Or 1

        Public Event PacketReceived As EventHandler(Of GenericPacketEventArgs) Implements INetworkCapture.PacketReceived

        Public Sub New(logger As Logger)
            _log = logger
        End Sub

        ''' <summary>
        ''' Get available devices
        ''' </summary>
        Public Function GetDevices() As List(Of GenericNetworkDevice) Implements INetworkCapture.GetDevices
            Dim devices As New List(Of GenericNetworkDevice)

            Try
                For Each iface In NetworkInterface.GetAllNetworkInterfaces()
                    ' Skip loopback and down interfaces
                    If iface.NetworkInterfaceType = NetworkInterfaceType.Loopback OrElse
                       iface.OperationalStatus <> OperationalStatus.Up Then
                        Continue For
                    End If

                    Dim genericDev As New GenericNetworkDevice With {
                        .Name = iface.Name,
                        .FriendlyName = iface.Description,
                        .Description = iface.Description,
                        .MacAddress = iface.GetPhysicalAddress().ToString(),
                        .IpAddresses = New List(Of String)
                    }

                    ' Get IP addresses
                    For Each addr In iface.GetIPProperties().UnicastAddresses
                        If addr.Address.AddressFamily = AddressFamily.InterNetwork Then
                            genericDev.IpAddresses.Add(addr.Address.ToString())
                        End If
                    Next

                    devices.Add(genericDev)
                Next

                _log?.Info("Found {0} network devices (RawSocket)", devices.Count)

            Catch ex As Exception
                _log?.Error("Failed to enumerate devices (RawSocket): {0}", ex.Message)
            End Try

            Return devices
        End Function

        ''' <summary>
        ''' Open device
        ''' </summary>
        Public Sub OpenDevice(deviceName As String, macAddress As String) Implements INetworkCapture.OpenDevice
            Try
                ' Find the interface
                _selectedInterface = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(
                    Function(i) i.Name = deviceName OrElse i.Description = deviceName)

                If _selectedInterface Is Nothing Then
                    Throw New Exception($"Interface not found: {deviceName}")
                End If

                ' Get first IPv4 address
                _localAddress = _selectedInterface.GetIPProperties().UnicastAddresses.
                    FirstOrDefault(Function(a) a.Address.AddressFamily = AddressFamily.InterNetwork)?.Address

                If _localAddress Is Nothing Then
                    Throw New Exception("No IPv4 address found on interface")
                End If

                ' Create raw socket
                _socket = New Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP)
                _socket.Bind(New IPEndPoint(_localAddress, 0))

                ' Set socket to capture all packets (promiscuous mode)
                _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, True)

                Dim byteIn(3) As Byte
                byteIn(0) = 1  ' Enable promiscuous mode
                byteIn(1) = 0
                byteIn(2) = 0
                byteIn(3) = 0

                _socket.IOControl(SIO_RCVALL, byteIn, Nothing)

                _log?.Info("Device opened: {0} at {1} (RawSocket)", deviceName, _localAddress.ToString())

            Catch ex As Exception
                _log?.Error("Failed to open device (RawSocket): {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Set filter (software filter - not hardware like BPF)
        ''' </summary>
        Public Sub SetFilter(macAddress As String) Implements INetworkCapture.SetFilter
            ' Raw sockets don't support hardware filters
            ' Filtering will be done in software during packet reception
            _log?.Debug("Filter set (RawSocket - software): {0}", macAddress)
        End Sub

        ''' <summary>
        ''' Start capture
        ''' </summary>
        Public Sub StartCapture(handler As Action(Of Byte())) Implements INetworkCapture.StartCapture
            _captureHandler = handler
            _isCapturing = True

            _captureThread = New Thread(AddressOf CaptureLoop) With {
                .Name = "RawSocketCapture",
                .IsBackground = True
            }
            _captureThread.Start()

            _log?.Info("Capture started (RawSocket)")
        End Sub

        ''' <summary>
        ''' Capture loop
        ''' </summary>
        Private Sub CaptureLoop()
            Dim buffer(65535) As Byte

            While _isCapturing
                Try
                    Dim bytesRead = _socket.Receive(buffer)

                    If bytesRead > 0 Then
                        Dim packet(bytesRead - 1) As Byte
                        Array.Copy(buffer, packet, bytesRead)

                        Dim timestamp = Utils.Tick()

                        ' Call handler
                        _captureHandler?.Invoke(packet)

                        ' Raise event
                        RaiseEvent PacketReceived(Me, New GenericPacketEventArgs(timestamp, packet))
                    End If

                Catch ex As SocketException When _isCapturing
                    ' Only log if we're still supposed to be capturing
                    _log?.Error("Socket error in capture loop (RawSocket): {0}", ex.Message)
                    Thread.Sleep(100)

                Catch ex As Exception When _isCapturing
                    _log?.Error("Error in capture loop (RawSocket): {0}", ex.Message)
                    Thread.Sleep(100)
                End Try
            End While

            _log?.Debug("Capture loop ended (RawSocket)")
        End Sub

        ''' <summary>
        ''' Stop capture
        ''' </summary>
        Public Sub StopCapture() Implements INetworkCapture.StopCapture
            If Not _isCapturing Then
                Return
            End If

            Try
                _isCapturing = False
                _captureThread?.Join(2000)
                _log?.Info("Capture stopped (RawSocket)")

            Catch ex As Exception
                _log?.Error("Error stopping capture (RawSocket): {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Send packet (via raw socket)
        ''' </summary>
        Public Function SendPacket(data As Byte()) As Integer Implements INetworkCapture.SendPacket
            Try
                ' Note: Raw sockets in Windows can't send Ethernet frames directly
                ' This would need to be IP layer packets
                ' For Ethernet layer, you still need WinPcap/Npcap or a kernel driver
                _log?.Warn("SendPacket not fully supported with RawSocket (needs Ethernet layer access)")
                Return 0

            Catch ex As Exception
                _log?.Error("Failed to send packet (RawSocket): {0}", ex.Message)
                Return 0
            End Try
        End Function

        ''' <summary>
        ''' Is capturing
        ''' </summary>
        Public ReadOnly Property IsCapturing As Boolean Implements INetworkCapture.IsCapturing
            Get
                Return _isCapturing
            End Get
        End Property

        ''' <summary>
        ''' Dispose
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            StopCapture()
            _socket?.Close()
            _socket?.Dispose()
            GC.SuppressFinalize(Me)
        End Sub

        Public Function SendAckPacket(data() As Byte) As Integer Implements INetworkCapture.SendAckPacket
            Throw New NotImplementedException()
        End Function
    End Class

End Namespace
