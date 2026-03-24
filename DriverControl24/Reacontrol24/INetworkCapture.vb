''' <summary>
''' Generic Network Capture Interface
''' Allows for multiple capture implementations (SharpPcap, Raw Sockets, WinDivert, etc.)
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' </summary>

Namespace Reacontrol24

    ''' <summary>
    ''' Network device information (generic)
    ''' </summary>
    Public Class GenericNetworkDevice
        Public Property Name As String
        Public Property FriendlyName As String
        Public Property Description As String
        Public Property MacAddress As String
        Public Property IpAddresses As List(Of String)
    End Class

    ''' <summary>
    ''' Generic interface for network packet capture
    ''' </summary>
    Public Interface INetworkCapture
        Inherits IDisposable

        ''' <summary>
        ''' Get available network devices
        ''' </summary>
        Function GetDevices() As List(Of GenericNetworkDevice)

        ''' <summary>
        ''' Open a network device for capture
        ''' </summary>
        Sub OpenDevice(deviceName As String, macAddress As String)

        ''' <summary>
        ''' Set packet filter
        ''' </summary>
        Sub SetFilter(macAddress As String)

        ''' <summary>
        ''' Start capturing packets
        ''' </summary>
        Sub StartCapture(handler As Action(Of Byte()))

        ''' <summary>
        ''' Stop capturing packets
        ''' </summary>
        Sub StopCapture()

        ''' <summary>
        ''' Send a packet (normal priority - CMD channel)
        ''' </summary>
        Function SendPacket(data As Byte()) As Integer

        ''' <summary>
        ''' Send an ACK packet (urgent - dedicated handle, zero contention with CMD sends)
        ''' </summary>
        Function SendAckPacket(data As Byte()) As Integer

        ''' <summary>
        ''' Event raised when packet received
        ''' </summary>
        Event PacketReceived As EventHandler(Of GenericPacketEventArgs)

        ''' <summary>
        ''' Is currently capturing
        ''' </summary>
        ReadOnly Property IsCapturing As Boolean

    End Interface

    ''' <summary>
    ''' Generic packet received event args
    ''' </summary>
    Public Class GenericPacketEventArgs
        Inherits EventArgs

        Public Property PacketTimestamp As Double
        Public Property PacketData As Byte()

        Public Sub New(timestamp As Double, data As Byte())
            Me.PacketTimestamp = timestamp
            Me.PacketData = data
        End Sub
    End Class

End Namespace
