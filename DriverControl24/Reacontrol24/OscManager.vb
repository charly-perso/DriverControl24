''' <summary>
''' OSC Manager - Handles Open Sound Control communication with DAWs
''' Wraps Rug.Osc for bidirectional communication
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' </summary>

Imports System.Net
Imports System.Threading
Imports Rug.Osc

Namespace Reacontrol24

    ''' <summary>
    ''' OSC Message wrapper with address and arguments
    ''' </summary>
    Public Class OscMessageInfo
        Public Property Address As String
        Public Property Arguments As New List(Of Object)
        Public Property Timestamp As DateTime

        Public Sub New(address As String, ParamArray args As Object())
            Me.Address = address
            Me.Timestamp = DateTime.Now
            If args IsNot Nothing Then
                Arguments.AddRange(args)
            End If
        End Sub

        Public Overrides Function ToString() As String
            Dim argsStr = String.Join(", ", Arguments.Select(Function(a) If(a IsNot Nothing, a.ToString(), "null")))
            Return $"{Address} [{argsStr}]"
        End Function
    End Class

    ''' <summary>
    ''' OSC Server - Receives messages from DAW
    ''' </summary>
    Public Class OscServerManager
        Implements IDisposable

        Private _server As OscReceiver
        Private _port As Integer
        Private _isRunning As Boolean
        Private _log As Logger
        Private _receiveThread As Thread

        ''' <summary>
        ''' Event raised when an OSC message is received
        ''' </summary>
        Public Event MessageReceived As EventHandler(Of OscMessageReceivedEventArgs)

        Public Sub New(port As Integer, logger As Logger)
            _port = port
            _log = logger
        End Sub

        ''' <summary>
        ''' Start the OSC server
        ''' </summary>
        Public Sub Start()
            If _isRunning Then
                Throw New InvalidOperationException("Server already running")
            End If

            Try
                _server = New OscReceiver(_port)
                _server.Connect()

                _isRunning = True
                _log.Info("OSC Server started on port {0}", _port)

                ' Start receive thread
                _receiveThread = New Thread(AddressOf ReceiveLoop) With {
                    .Name = "OscServerThread",
                    .IsBackground = True
                }
                _receiveThread.Start()

            Catch ex As Exception
                _log.Error("Failed to start OSC Server: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Stop the OSC server
        ''' </summary>
        Public Sub [Stop]()
            If Not _isRunning Then
                Return
            End If

            _isRunning = False

            Try
                If _server IsNot Nothing Then
                    _server.Close()
                End If

                _receiveThread?.Join(2000)

                _log.Info("OSC Server stopped")
            Catch ex As Exception
                _log.Error("Error stopping OSC Server: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Receive loop (runs on background thread)
        ''' </summary>
        Private Sub ReceiveLoop()
            While _isRunning
                Try
                    If _server.State = OscSocketState.Connected Then
                        ' Receive packet with timeout
                        Dim packet = _server.Receive()

                        If packet IsNot Nothing Then
                            ProcessPacket(packet)
                        End If
                    Else
                        Thread.Sleep(100)
                    End If
                Catch ex As ThreadInterruptedException
                    _log.Debug("OSC Server thread interrupted")
                    Exit While
                Catch ex As Exception
                    If _isRunning Then
                        _log.Error("OSC Server receive error: {0}", ex.Message)
                        Thread.Sleep(1000) ' Prevent tight loop on errors
                    End If
                End Try
            End While

            _log.Debug("OSC Server receive loop ended")
        End Sub

        ''' <summary>
        ''' Process received OSC packet
        ''' </summary>
        Private Sub ProcessPacket(packet As OscPacket)
            Try
                Select Case packet.GetType()
                    Case GetType(OscMessage)
                        Dim msg = CType(packet, OscMessage)
                        ProcessMessage(msg)

                    Case GetType(OscBundle)
                        Dim bundle = CType(packet, OscBundle)
                        For Each item In bundle
                            ProcessPacket(item)
                        Next

                    Case Else
                        _log.Warn("Unknown OSC packet type: {0}", packet.GetType().Name)
                End Select

            Catch ex As Exception
                _log.Error("Error processing OSC packet: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Process individual OSC message
        ''' </summary>
        Private Sub ProcessMessage(msg As OscMessage)
            Try
                Dim msgInfo As New OscMessageInfo(msg.Address)

                ' Extract arguments
                For i = 0 To msg.Count - 1
                    msgInfo.Arguments.Add(msg(i))
                Next

                _log.Debug("OSC Received: {0}", msgInfo.ToString())

                ' Raise event
                RaiseEvent MessageReceived(Me, New OscMessageReceivedEventArgs(msgInfo))

            Catch ex As Exception
                _log.Error("Error processing OSC message: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            [Stop]()
            GC.SuppressFinalize(Me)
        End Sub
    End Class

    ''' <summary>
    ''' OSC Client - Sends messages to DAW
    ''' </summary>
    Public Class OscClientManager
        Implements IDisposable

        Private _sender As OscSender
        Private _endpoint As IPEndPoint
        Private _isConnected As Boolean
        Private _log As Logger
        Private _sendLock As New Object()

        Public Sub New(ipAddress As String, port As Integer, logger As Logger)
            _endpoint = New IPEndPoint(Net.IPAddress.Parse(ipAddress), port)
            _log = logger
        End Sub

        ''' <summary>
        ''' Connect to the DAW
        ''' </summary>
        Public Sub Connect()
            If _isConnected Then
                Return
            End If

            Try
                _sender = New OscSender(_endpoint.Address, _endpoint.Port)
                _sender.Connect()

                _isConnected = True
                _log.Info("OSC Client connected to {0}:{1}", _endpoint.Address, _endpoint.Port)

            Catch ex As Exception
                _log.Error("Failed to connect OSC Client: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Disconnect from the DAW
        ''' </summary>
        Public Sub Disconnect()
            If Not _isConnected Then
                Return
            End If

            Try
                If _sender IsNot Nothing Then
                    _sender.Close()
                End If

                _isConnected = False
                _log.Info("OSC Client disconnected")

            Catch ex As Exception
                _log.Error("Error disconnecting OSC Client: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Send an OSC message
        ''' </summary>
        Public Sub SendMessage(address As String, ParamArray args As Object())
            If Not _isConnected Then
                _log.Warn("Cannot send OSC message - not connected")
                Return
            End If

            SyncLock _sendLock
                Try
                    Dim msg As New OscMessage(address)

                    ' Add arguments
                    If args IsNot Nothing Then
                        For Each arg In args
                            msg.Append(arg)
                        Next
                    End If

                    _sender.Send(msg)

                    Dim msgInfo As New OscMessageInfo(address, args)
                    _log.Debug("OSC Sent: {0}", msgInfo.ToString())

                Catch ex As Exception
                    _log.Error("Error sending OSC message to {0}: {1}", address, ex.Message)
                End Try
            End SyncLock
        End Sub

        ''' <summary>
        ''' Send a raw OSC message object
        ''' </summary>
        Public Sub SendMessage(msg As OscMessage)
            If Not _isConnected Then
                _log.Warn("Cannot send OSC message - not connected")
                Return
            End If

            SyncLock _sendLock
                Try
                    _sender.Send(msg)
                    _log.Debug("OSC Sent: {0}", msg.Address)

                Catch ex As Exception
                    _log.Error("Error sending OSC message: {0}", ex.Message)
                End Try
            End SyncLock
        End Sub

        ''' <summary>
        ''' Check if connected
        ''' </summary>
        Public ReadOnly Property IsConnected As Boolean
            Get
                Return _isConnected AndAlso _sender IsNot Nothing AndAlso _sender.State = OscSocketState.Connected
            End Get
        End Property

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            Disconnect()
            GC.SuppressFinalize(Me)
        End Sub
    End Class

    ''' <summary>
    ''' Event args for OSC message received
    ''' </summary>
    Public Class OscMessageReceivedEventArgs
        Inherits EventArgs

        Public Property Message As OscMessageInfo

        Public Sub New(message As OscMessageInfo)
            Me.Message = message
        End Sub
    End Class

    ''' <summary>
    ''' Combined OSC Manager - Manages both server and client
    ''' </summary>
    Public Class OscManager
        Implements IDisposable

        Private _server As OscServerManager
        Private _client As OscClientManager
        Private _log As Logger
        Private _serverPort As Integer
        Private _clientAddress As String
        Private _clientPort As Integer

        ''' <summary>
        ''' Event raised when a message is received from DAW
        ''' </summary>
        Public Event MessageReceived As EventHandler(Of OscMessageReceivedEventArgs)

        Public Sub New(serverPort As Integer, clientAddress As String, clientPort As Integer, logger As Logger)
            _serverPort = serverPort
            _clientAddress = clientAddress
            _clientPort = clientPort
            _log = logger
        End Sub

        ''' <summary>
        ''' Start OSC server and connect client
        ''' </summary>
        Public Sub Start()
            Try
                ' Start server to receive from DAW
                _server = New OscServerManager(_serverPort, _log)
                AddHandler _server.MessageReceived, AddressOf OnServerMessageReceived
                _server.Start()

                ' Connect client to send to DAW
                _client = New OscClientManager(_clientAddress, _clientPort, _log)
                _client.Connect()

                _log.Info("OSC Manager started - Server:{0} Client:{1}:{2}", _serverPort, _clientAddress, _clientPort)

            Catch ex As Exception
                _log.Error("Failed to start OSC Manager: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Stop OSC manager
        ''' </summary>
        Public Sub [Stop]()
            Try
                _server?.Stop()
                _client?.Disconnect()

                _log.Info("OSC Manager stopped")

            Catch ex As Exception
                _log.Error("Error stopping OSC Manager: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Send message to DAW
        ''' </summary>
        Public Sub SendToDAW(address As String, ParamArray args As Object())
            _client?.SendMessage(address, args)
        End Sub

        ''' <summary>
        ''' Send raw message to DAW
        ''' </summary>
        Public Sub SendToDAW(msg As OscMessage)
            _client?.SendMessage(msg)
        End Sub

        ''' <summary>
        ''' Handle server message received
        ''' </summary>
        Private Sub OnServerMessageReceived(sender As Object, e As OscMessageReceivedEventArgs)
            ' Forward the event
            RaiseEvent MessageReceived(Me, e)
        End Sub

        ''' <summary>
        ''' Check if ready
        ''' </summary>
        Public ReadOnly Property IsReady As Boolean
            Get
                Return _client?.IsConnected AndAlso _server IsNot Nothing
            End Get
        End Property

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            [Stop]()
            _server?.Dispose()
            _client?.Dispose()
            GC.SuppressFinalize(Me)
        End Sub
    End Class

End Namespace
