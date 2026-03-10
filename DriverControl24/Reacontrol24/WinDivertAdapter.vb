''' <summary>
''' WinDivert adapter for Control24 - NO Npcap/WinPcap required!
''' High-performance kernel-mode packet capture and injection
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' 
''' Requires: WinDivert driver (https://reqrypt.org/windivert.html)
''' </summary>

Imports System.Net.NetworkInformation
Imports System.Runtime.InteropServices
Imports System.Threading

Namespace Reacontrol24

    ''' <summary>
    ''' WinDivert adapter implementing INetworkCapture
    ''' Uses Windows kernel-mode driver (no Npcap required)
    ''' </summary>
    Public Class WinDivertAdapter
        Implements INetworkCapture

        Private _handle As IntPtr = IntPtr.Zero
        Private _log As Logger
        Private _isCapturing As Boolean
        Private _captureThread As Thread
        Private _captureHandler As Action(Of Double, Byte())
        Private _selectedInterface As String

        ' WinDivert constants
        Private Const WINDIVERT_FLAG_SNIFF As UInt64 = 1
        Private Const WINDIVERT_FLAG_DROP As UInt64 = 2
        Private Const WINDIVERT_LAYER_NETWORK As Integer = 0
        Private Const WINDIVERT_LAYER_NETWORK_FORWARD As Integer = 1

        ' WinDivert P/Invoke declarations
        <DllImport("WinDivert.dll", CallingConvention:=CallingConvention.Cdecl, SetLastError:=True)>
        Private Shared Function WinDivertOpen(
            <MarshalAs(UnmanagedType.LPStr)> filter As String,
            layer As Integer,
            priority As Short,
            flags As UInt64) As IntPtr
        End Function

        <DllImport("WinDivert.dll", CallingConvention:=CallingConvention.Cdecl, SetLastError:=True)>
        Private Shared Function WinDivertRecv(
            handle As IntPtr,
            pPacket As Byte(),
            packetLen As UInteger,
            pAddr As IntPtr,
            ByRef readLen As UInteger) As Boolean
        End Function

        <DllImport("WinDivert.dll", CallingConvention:=CallingConvention.Cdecl, SetLastError:=True)>
        Private Shared Function WinDivertSend(
            handle As IntPtr,
            pPacket As Byte(),
            packetLen As UInteger,
            pAddr As IntPtr,
            ByRef writeLen As UInteger) As Boolean
        End Function

        <DllImport("WinDivert.dll", CallingConvention:=CallingConvention.Cdecl, SetLastError:=True)>
        Private Shared Function WinDivertClose(handle As IntPtr) As Boolean
        End Function

        <DllImport("WinDivert.dll", CallingConvention:=CallingConvention.Cdecl, SetLastError:=True)>
        Private Shared Function WinDivertHelperParsePacket(
            pPacket As Byte(),
            packetLen As UInteger,
            ByRef pIpHdr As IntPtr,
            ByRef pIpv6Hdr As IntPtr,
            ByRef pIcmpHdr As IntPtr,
            ByRef pIcmpv6Hdr As IntPtr,
            ByRef pTcpHdr As IntPtr,
            ByRef pUdpHdr As IntPtr,
            ByRef pData As IntPtr,
            ByRef dataLen As UInteger) As Boolean
        End Function

        ''' <summary>
        ''' Event raised when packet received
        ''' </summary>
        Public Event PacketReceived As EventHandler(Of GenericPacketEventArgs) Implements INetworkCapture.PacketReceived

        Public Sub New(logger As Logger)
            _log = logger
        End Sub

        ''' <summary>
        ''' Get available network devices
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

                    Dim dev As New GenericNetworkDevice With {
                        .Name = iface.Name,
                        .FriendlyName = iface.Description,
                        .Description = iface.Description,
                        .MacAddress = BitConverter.ToString(iface.GetPhysicalAddress().GetAddressBytes()).Replace("-", ":"),
                        .IpAddresses = New List(Of String)
                    }

                    ' Get IP addresses
                    For Each addr In iface.GetIPProperties().UnicastAddresses
                        If addr.Address.AddressFamily = Net.Sockets.AddressFamily.InterNetwork Then
                            dev.IpAddresses.Add(addr.Address.ToString())
                        End If
                    Next

                    devices.Add(dev)
                Next

                _log?.Info("WinDivert: Found {0} network devices", devices.Count)

            Catch ex As Exception
                _log?.Error("WinDivert: Failed to enumerate devices: {0}", ex.Message)
            End Try

            Return devices
        End Function

        ''' <summary>
        ''' Open device for capture
        ''' </summary>
        Public Sub OpenDevice(deviceName As String, macAddress As String) Implements INetworkCapture.OpenDevice
            Try
                _selectedInterface = deviceName
                
                ' Clean MAC address (remove separators)
                Dim macClean = macAddress.Replace(":", "").Replace("-", "").ToUpper()
                
                ' ?? IMPORTANT LIMITATION:
                ' WinDivert operates at IP layer and CANNOT capture non-IP Ethernet protocols like 0x885F
                ' Control24 uses custom Ethernet protocol (0x885F) which bypasses IP stack
                ' This means WinDivert CANNOT capture Control24 packets!
                
                _log?.Warn("?? WinDivert LIMITATION: Cannot capture Ethernet protocol 0x885F (Control24)")
                _log?.Warn("?? WinDivert only works with IP-based protocols")
                _log?.Warn("?? This adapter will NOT receive Control24 packets!")
                _log?.Info("?? Recommendation: Use SharpPcap (with Npcap) for Control24")
                
                ' Get interface index
                Dim ifIndex = GetInterfaceIndex(deviceName)
                
                ' Filter: Capture all IP traffic on specific interface (for diagnostic only)
                Dim filter = $"ifIdx == {ifIndex}"
                
                _log?.Info("WinDivert: Opening with filter: {0}", filter)
                _log?.Info("WinDivert: Target MAC: {0}", macClean)
                _log?.Info("WinDivert: Interface index: {0}", ifIndex)

                ' Open WinDivert handle
                ' SNIFF flag = don't drop packets, just copy them
                ' Priority 0 = default priority
                _handle = WinDivertOpen(filter, WINDIVERT_LAYER_NETWORK_FORWARD, 0, WINDIVERT_FLAG_SNIFF)

                If _handle = IntPtr.Zero Then
                    Dim err = Marshal.GetLastWin32Error()
                    Throw New Exception($"WinDivert failed to open (error {err}). " &
                                      "Make sure WinDivert driver is installed and you have administrator rights!")
                End If

                _log?.Info("? WinDivert opened successfully (handle: 0x{0:X})", _handle.ToInt64())
                _log?.Warn("?? REMINDER: WinDivert will NOT capture Control24 packets (0x885F)")

            Catch ex As Exception
                _log?.Error("WinDivert: Failed to open device: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Get interface index by name
        ''' </summary>
        Private Function GetInterfaceIndex(deviceName As String) As Integer
            Try
                For Each iface In NetworkInterface.GetAllNetworkInterfaces()
                    If iface.Name = deviceName OrElse iface.Description = deviceName Then
                        ' Get interface index from IPv4 properties
                        Dim ipProps = iface.GetIPProperties()
                        If ipProps IsNot Nothing Then
                            Dim ipv4Props = ipProps.GetIPv4Properties()
                            If ipv4Props IsNot Nothing Then
                                Return ipv4Props.Index
                            End If
                        End If
                    End If
                Next
            Catch ex As Exception
                _log?.Error("Failed to get interface index: {0}", ex.Message)
            End Try
            
            ' Default to 0 (all interfaces)
            Return 0
        End Function

        ''' <summary>
        ''' Set filter (already set in OpenDevice)
        ''' </summary>
        Public Sub SetFilter(macAddress As String) Implements INetworkCapture.SetFilter
            ' Filter already configured in OpenDevice
            _log?.Debug("WinDivert: Filter already set in OpenDevice")
        End Sub

        ''' <summary>
        ''' Start packet capture
        ''' </summary>
        Public Sub StartCapture(handler As Action(Of Double, Byte())) Implements INetworkCapture.StartCapture
            If _handle = IntPtr.Zero Then
                Throw New InvalidOperationException("Device not opened")
            End If

            _captureHandler = handler
            _isCapturing = True

            ' Start capture thread with high priority
            _captureThread = New Thread(AddressOf CaptureLoop) With {
                .Name = "WinDivertCapture",
                .IsBackground = True,
                .Priority = ThreadPriority.Highest
            }
            _captureThread.Start()

            _log?.Info("WinDivert: Capture started (High Priority)")
        End Sub

        ''' <summary>
        ''' Capture loop - runs on background thread
        ''' </summary>
        Private Sub CaptureLoop()
            Dim buffer(65535) As Byte
            Dim addr As IntPtr = Marshal.AllocHGlobal(64) ' WINDIVERT_ADDRESS structure (64 bytes)
            Dim readLen As UInteger
            Dim packetsReceived As Integer = 0

            Try
                _log?.Debug("WinDivert: Capture loop started")

                While _isCapturing
                    ' Receive packet from WinDivert
                    If WinDivertRecv(_handle, buffer, CUInt(buffer.Length), addr, readLen) Then
                        If readLen > 0 Then
                            packetsReceived += 1

                            ' Copy packet data
                            Dim packet(CInt(readLen) - 1) As Byte
                            Array.Copy(buffer, packet, readLen)

                            ' Check if this is an Ethernet frame with protocol 0x885F
                            ' Ethernet header: [6 bytes dest MAC][6 bytes src MAC][2 bytes protocol]
                            If readLen >= 14 Then
                                Dim protocol = (CUShort(packet(12)) << 8) Or packet(13)
                                
                                ' Only process Control24 packets (0x885F)
                                If protocol = &H885F Then
                                    Dim timestamp = Utils.Tick()

                                    ' Call handler
                                    _captureHandler?.Invoke(timestamp, packet)

                                    ' Raise event
                                    RaiseEvent PacketReceived(Me, New GenericPacketEventArgs(timestamp, packet))
                                End If
                            End If
                        End If
                    Else
                        ' Error occurred
                        Dim err = Marshal.GetLastWin32Error()
                        If err <> 0 AndAlso _isCapturing Then
                            _log?.Warn("WinDivert: Recv error {0}", err)
                            Thread.Sleep(10)
                        End If
                    End If
                End While

                _log?.Info("WinDivert: Capture loop ended. Total packets: {0}", packetsReceived)

            Catch ex As ThreadAbortException
                _log?.Debug("WinDivert: Capture thread aborted")
            Catch ex As Exception
                _log?.Error("WinDivert: Capture loop error: {0}", ex.Message)
            Finally
                If addr <> IntPtr.Zero Then
                    Marshal.FreeHGlobal(addr)
                End If
            End Try
        End Sub

        ''' <summary>
        ''' Stop packet capture
        ''' </summary>
        Public Sub StopCapture() Implements INetworkCapture.StopCapture
            If Not _isCapturing Then
                Return
            End If

            Try
                _isCapturing = False
                _captureThread?.Join(2000)
                _log?.Info("WinDivert: Capture stopped")

            Catch ex As Exception
                _log?.Error("WinDivert: Error stopping capture: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Send packet via WinDivert
        ''' </summary>
        Public Function SendPacket(data As Byte()) As Integer Implements INetworkCapture.SendPacket
            If _handle = IntPtr.Zero Then
                Throw New InvalidOperationException("Device not opened")
            End If

            Try
                ' Send packet
                ' addr = NULL means send via default routing
                Dim addr As IntPtr = IntPtr.Zero
                Dim writeLen As UInteger

                If WinDivertSend(_handle, data, CUInt(data.Length), addr, writeLen) Then
                    Return CInt(writeLen)
                Else
                    Dim err = Marshal.GetLastWin32Error()
                    _log?.Error("WinDivert: Send failed (error {0})", err)
                    Return 0
                End If

            Catch ex As Exception
                _log?.Error("WinDivert: SendPacket error: {0}", ex.Message)
                Return 0
            End Try
        End Function

        ''' <summary>
        ''' Check if currently capturing
        ''' </summary>
        Public ReadOnly Property IsCapturing As Boolean Implements INetworkCapture.IsCapturing
            Get
                Return _isCapturing
            End Get
        End Property

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                StopCapture()

                If _handle <> IntPtr.Zero Then
                    WinDivertClose(_handle)
                    _handle = IntPtr.Zero
                    _log?.Info("WinDivert: Handle closed")
                End If

            Catch ex As Exception
                _log?.Error("WinDivert: Dispose error: {0}", ex.Message)
            End Try

            GC.SuppressFinalize(Me)
        End Sub

    End Class

End Namespace
