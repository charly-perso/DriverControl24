''' <summary>
''' TCP Server Manager - Handles multiple client connections
''' Allows DAW software to connect and communicate with Control24
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' </summary>

Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Collections.Concurrent
Imports System.IO

Namespace Reacontrol24

    ''' <summary>
    ''' TCP Client handler - Manages individual client connection
    ''' </summary>
    Public Class TcpClientHandler
        Implements IDisposable

        Private _client As TcpClient
        Private _stream As NetworkStream
        Private _receiveThread As Thread
        Private _isConnected As Boolean
        Private _log As Logger
        Private _clientId As String
        Private _sendLock As New Object()

        ''' <summary>
        ''' Event raised when data is received from client
        ''' </summary>
        Public Event DataReceived As EventHandler(Of ClientDataReceivedEventArgs)

        ''' <summary>
        ''' Event raised when client disconnects
        ''' </summary>
        Public Event Disconnected As EventHandler(Of ClientEventArgs)

        Public Sub New(client As TcpClient, logger As Logger)
            _client = client
            _log = logger
            _clientId = Guid.NewGuid().ToString().Substring(0, 8)

            Try
                _stream = client.GetStream()
                _isConnected = True

                ' Start receive thread
                _receiveThread = New Thread(AddressOf ReceiveLoop) With {
                    .Name = $"TcpClientHandler-{_clientId}",
                    .IsBackground = True
                }
                _receiveThread.Start()

                _log.Info("TCP Client connected: {0} from {1}", _clientId, RemoteEndPoint)

            Catch ex As Exception
                _log.Error("Failed to initialize TCP client handler: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Client ID
        ''' </summary>
        Public ReadOnly Property ClientId As String
            Get
                Return _clientId
            End Get
        End Property

        ''' <summary>
        ''' Remote endpoint
        ''' </summary>
        Public ReadOnly Property RemoteEndPoint As String
            Get
                Try
                    Return _client?.Client?.RemoteEndPoint?.ToString()
                Catch
                    Return "Unknown"
                End Try
            End Get
        End Property

        ''' <summary>
        ''' Is connected
        ''' </summary>
        Public ReadOnly Property IsConnected As Boolean
            Get
                Return _isConnected AndAlso _client?.Connected
            End Get
        End Property

        ''' <summary>
        ''' Send data to client
        ''' </summary>
        Public Sub SendData(data As Byte())
            If Not IsConnected Then
                _log.Warn("Cannot send data - client {0} not connected", _clientId)
                Return
            End If

            SyncLock _sendLock
                Try
                    _stream.Write(data, 0, data.Length)
                    _stream.Flush()
                    _log.Debug("Sent {0} bytes to client {1}", data.Length, _clientId)

                Catch ex As Exception
                    _log.Error("Error sending data to client {0}: {1}", _clientId, ex.Message)
                    Disconnect()
                End Try
            End SyncLock
        End Sub

        ''' <summary>
        ''' Send string to client
        ''' </summary>
        Public Sub SendString(message As String)
            Dim data = Encoding.UTF8.GetBytes(message)
            SendData(data)
        End Sub

        ''' <summary>
        ''' Receive loop
        ''' </summary>
        Private Sub ReceiveLoop()
            Dim buffer(4095) As Byte

            While _isConnected
                Try
                    If Not _stream.DataAvailable Then
                        Thread.Sleep(10)
                        Continue While
                    End If

                    Dim bytesRead = _stream.Read(buffer, 0, buffer.Length)

                    If bytesRead = 0 Then
                        ' Client disconnected
                        _log.Info("Client {0} disconnected (read 0 bytes)", _clientId)
                        Disconnect()
                        Exit While
                    End If

                    ' Copy received data
                    Dim receivedData(bytesRead - 1) As Byte
                    Array.Copy(buffer, receivedData, bytesRead)

                    ' Raise event
                    RaiseEvent DataReceived(Me, New ClientDataReceivedEventArgs(_clientId, receivedData))

                Catch ex As IOException
                    _log.Debug("Client {0} connection closed", _clientId)
                    Disconnect()
                    Exit While

                Catch ex As Exception
                    _log.Error("Error in receive loop for client {0}: {1}", _clientId, ex.Message)
                    Disconnect()
                    Exit While
                End Try
            End While

            _log.Debug("Receive loop ended for client {0}", _clientId)
        End Sub

        ''' <summary>
        ''' Disconnect client
        ''' </summary>
        Public Sub Disconnect()
            If Not _isConnected Then
                Return
            End If

            _isConnected = False

            Try
                _stream?.Close()
                _client?.Close()
                RaiseEvent Disconnected(Me, New ClientEventArgs(_clientId))
                _log.Info("Client {0} disconnected", _clientId)

            Catch ex As Exception
                _log.Error("Error disconnecting client {0}: {1}", _clientId, ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            Disconnect()
            _receiveThread?.Join(1000)
            _stream?.Dispose()
            _client?.Dispose()
            GC.SuppressFinalize(Me)
        End Sub
    End Class

    ''' <summary>
    ''' TCP Server Manager - Handles multiple client connections
    ''' </summary>
    Public Class TcpServerManager
        Implements IDisposable

        Private _listener As TcpListener
        Private _acceptThread As Thread
        Private _isRunning As Boolean
        Private _log As Logger
        Private _port As Integer
        Private _clients As ConcurrentDictionary(Of String, TcpClientHandler)

        ''' <summary>
        ''' Event raised when a client connects
        ''' </summary>
        Public Event ClientConnected As EventHandler(Of ClientEventArgs)

        ''' <summary>
        ''' Event raised when a client disconnects
        ''' </summary>
        Public Event ClientDisconnected As EventHandler(Of ClientEventArgs)

        ''' <summary>
        ''' Event raised when data is received from a client
        ''' </summary>
        Public Event DataReceived As EventHandler(Of ClientDataReceivedEventArgs)

        Public Sub New(port As Integer, logger As Logger)
            _port = port
            _log = logger
            _clients = New ConcurrentDictionary(Of String, TcpClientHandler)
        End Sub

        ''' <summary>
        ''' Start the TCP server
        ''' </summary>
        Public Sub Start()
            If _isRunning Then
                Throw New InvalidOperationException("Server already running")
            End If

            Try
                _listener = New TcpListener(IPAddress.Any, _port)
                _listener.Start()

                _isRunning = True
                _log.Info("TCP Server started on port {0}", _port)

                ' Start accept thread
                _acceptThread = New Thread(AddressOf AcceptLoop) With {
                    .Name = "TcpServerAccept",
                    .IsBackground = True
                }
                _acceptThread.Start()

            Catch ex As Exception
                _log.Error("Failed to start TCP Server: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Stop the TCP server
        ''' </summary>
        Public Sub [Stop]()
            If Not _isRunning Then
                Return
            End If

            _isRunning = False

            Try
                ' Disconnect all clients
                For Each kvp In _clients
                    kvp.Value.Disconnect()
                    kvp.Value.Dispose()
                Next
                _clients.Clear()

                _listener?.Stop()
                _acceptThread?.Join(2000)

                _log.Info("TCP Server stopped")

            Catch ex As Exception
                _log.Error("Error stopping TCP Server: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Accept loop
        ''' </summary>
        Private Sub AcceptLoop()
            While _isRunning
                Try
                    If Not _listener.Pending() Then
                        Thread.Sleep(100)
                        Continue While
                    End If

                    Dim client = _listener.AcceptTcpClient()
                    HandleNewClient(client)

                Catch ex As SocketException When Not _isRunning
                    ' Normal when stopping
                    Exit While

                Catch ex As Exception
                    If _isRunning Then
                        _log.Error("Error accepting client: {0}", ex.Message)
                        Thread.Sleep(1000)
                    End If
                End Try
            End While

            _log.Debug("Accept loop ended")
        End Sub

        ''' <summary>
        ''' Handle new client connection
        ''' </summary>
        Private Sub HandleNewClient(client As TcpClient)
            Try
                Dim handler As New TcpClientHandler(client, _log)

                ' Add to collection
                _clients.TryAdd(handler.ClientId, handler)

                ' Wire up events
                AddHandler handler.DataReceived, AddressOf OnClientDataReceived
                AddHandler handler.Disconnected, AddressOf OnClientDisconnected

                ' Raise event
                RaiseEvent ClientConnected(Me, New ClientEventArgs(handler.ClientId))

            Catch ex As Exception
                _log.Error("Error handling new client: {0}", ex.Message)
                client?.Close()
            End Try
        End Sub

        ''' <summary>
        ''' Handle client data received
        ''' </summary>
        Private Sub OnClientDataReceived(sender As Object, e As ClientDataReceivedEventArgs)
            ' Forward event
            RaiseEvent DataReceived(Me, e)
        End Sub

        ''' <summary>
        ''' Handle client disconnected
        ''' </summary>
        Private Sub OnClientDisconnected(sender As Object, e As ClientEventArgs)
            ' Remove from collection
            Dim handler As TcpClientHandler = Nothing
            _clients.TryRemove(e.ClientId, handler)
            handler?.Dispose()

            ' Forward event
            RaiseEvent ClientDisconnected(Me, e)
        End Sub

        ''' <summary>
        ''' Send data to all clients
        ''' </summary>
        Public Sub BroadcastData(data As Byte())
            For Each kvp In _clients
                If kvp.Value.IsConnected Then
                    kvp.Value.SendData(data)
                End If
            Next
        End Sub

        ''' <summary>
        ''' Send data to specific client
        ''' </summary>
        Public Sub SendToClient(clientId As String, data As Byte())
            Dim handler As TcpClientHandler = Nothing
            If _clients.TryGetValue(clientId, handler) Then
                handler.SendData(data)
            Else
                _log.Warn("Client {0} not found", clientId)
            End If
        End Sub

        ''' <summary>
        ''' Get connected clients count
        ''' </summary>
        Public ReadOnly Property ConnectedClientsCount As Integer
            Get
                Return _clients.Count
            End Get
        End Property

        ''' <summary>
        ''' Get client IDs
        ''' </summary>
        Public ReadOnly Property ClientIds As IEnumerable(Of String)
            Get
                Return _clients.Keys
            End Get
        End Property

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            [Stop]()
            GC.SuppressFinalize(Me)
        End Sub
    End Class

    ''' <summary>
    ''' Event args for client events
    ''' </summary>
    Public Class ClientEventArgs
        Inherits EventArgs

        Public Property ClientId As String

        Public Sub New(clientId As String)
            Me.ClientId = clientId
        End Sub
    End Class

    ''' <summary>
    ''' Event args for client data received
    ''' </summary>
    Public Class ClientDataReceivedEventArgs
        Inherits EventArgs

        Public Property ClientId As String
        Public Property Data As Byte()

        Public Sub New(clientId As String, data As Byte())
            Me.ClientId = clientId
            Me.Data = data
        End Sub
    End Class

End Namespace
