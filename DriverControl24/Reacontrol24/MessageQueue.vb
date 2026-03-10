''' <summary>
''' Message Queue Manager - Thread-safe message queuing system
''' Handles bidirectional message flow between Control24 and DAW clients
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' </summary>

Imports System.Collections.Concurrent
Imports System.Threading

Namespace Reacontrol24

    ''' <summary>
    ''' Message types for routing
    ''' </summary>
    Public Enum MessageType
        C24Command      ' Command from Control24 hardware
        OscCommand      ' OSC command from DAW
        TcpCommand      ' TCP command from client
        SystemCommand   ' Internal system command
    End Enum

    ''' <summary>
    ''' Message priority for queue processing
    ''' </summary>
    Public Enum MessagePriority
        Low = 0
        Normal = 1
        High = 2
        Critical = 3
    End Enum

    ''' <summary>
    ''' Queued message wrapper
    ''' </summary>
    Public Class QueuedMessage
        Public Property MessageType As MessageType
        Public Property Priority As MessagePriority
        Public Property Timestamp As DateTime
        Public Property Data As Byte()
        Public Property StringData As String
        Public Property SourceId As String
        Public Property TargetId As String
        Public Property Metadata As Dictionary(Of String, Object)

        Public Sub New(msgType As MessageType, data As Byte(), Optional priority As MessagePriority = MessagePriority.Normal)
            Me.MessageType = msgType
            Me.Data = data
            Me.Priority = priority
            Me.Timestamp = DateTime.Now
            Me.Metadata = New Dictionary(Of String, Object)
        End Sub

        Public Sub New(msgType As MessageType, stringData As String, Optional priority As MessagePriority = MessagePriority.Normal)
            Me.MessageType = msgType
            Me.StringData = stringData
            Me.Priority = priority
            Me.Timestamp = DateTime.Now
            Me.Metadata = New Dictionary(Of String, Object)
        End Sub

        Public Overrides Function ToString() As String
            Return $"[{Priority}] {MessageType} from {SourceId} @ {Timestamp:HH:mm:ss.fff}"
        End Function
    End Class

    ''' <summary>
    ''' Thread-safe message queue with priority support
    ''' </summary>
    Public Class MessageQueue
        Implements IDisposable

        Private _queue As ConcurrentQueue(Of QueuedMessage)
        Private _priorityQueues As Dictionary(Of MessagePriority, ConcurrentQueue(Of QueuedMessage))
        Private _messageAvailable As AutoResetEvent
        Private _log As Logger
        Private _maxQueueSize As Integer
        Private _droppedMessages As Long

        Public Sub New(logger As Logger, Optional maxQueueSize As Integer = 10000)
            _log = logger
            _maxQueueSize = maxQueueSize
            _queue = New ConcurrentQueue(Of QueuedMessage)
            _messageAvailable = New AutoResetEvent(False)
            _droppedMessages = 0

            ' Initialize priority queues
            _priorityQueues = New Dictionary(Of MessagePriority, ConcurrentQueue(Of QueuedMessage))
            For Each priority As MessagePriority In [Enum].GetValues(GetType(MessagePriority))
                _priorityQueues(priority) = New ConcurrentQueue(Of QueuedMessage)
            Next
        End Sub

        ''' <summary>
        ''' Enqueue a message
        ''' </summary>
        Public Function Enqueue(message As QueuedMessage) As Boolean
            If Count >= _maxQueueSize Then
                _droppedMessages += 1
                _log.Warn("Message queue full ({0}), dropping message: {1}", _maxQueueSize, message.ToString())
                Return False
            End If

            Try
                ' Add to appropriate priority queue
                _priorityQueues(message.Priority).Enqueue(message)

                ' Signal that a message is available
                _messageAvailable.Set()

                _log.Debug("Message enqueued: {0}", message.ToString())
                Return True

            Catch ex As Exception
                _log.Error("Error enqueueing message: {0}", ex.Message)
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Dequeue a message (blocks until available or timeout)
        ''' </summary>
        Public Function Dequeue(Optional timeoutMs As Integer = Timeout.Infinite) As QueuedMessage
            Try
                ' Wait for message to be available
                If Not _messageAvailable.WaitOne(timeoutMs) Then
                    Return Nothing
                End If

                ' Try to dequeue from priority queues (highest first)
                For priority = MessagePriority.Critical To MessagePriority.Low Step -1
                    Dim msg As QueuedMessage = Nothing
                    If _priorityQueues(priority).TryDequeue(msg) Then
                        ' Check if more messages available
                        If Count > 0 Then
                            _messageAvailable.Set()
                        End If
                        Return msg
                    End If
                Next

                Return Nothing

            Catch ex As Exception
                _log.Error("Error dequeuing message: {0}", ex.Message)
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Try to dequeue without blocking
        ''' </summary>
        Public Function TryDequeue(ByRef message As QueuedMessage) As Boolean
            ' Try to dequeue from priority queues (highest first)
            For priority = MessagePriority.Critical To MessagePriority.Low Step -1
                If _priorityQueues(priority).TryDequeue(message) Then
                    Return True
                End If
            Next

            message = Nothing
            Return False
        End Function

        ''' <summary>
        ''' Current queue size
        ''' </summary>
        Public ReadOnly Property Count As Integer
            Get
                Dim total = 0
                For Each kvp In _priorityQueues
                    total += kvp.Value.Count
                Next
                Return total
            End Get
        End Property

        ''' <summary>
        ''' Number of dropped messages
        ''' </summary>
        Public ReadOnly Property DroppedMessages As Long
            Get
                Return _droppedMessages
            End Get
        End Property

        ''' <summary>
        ''' Clear all messages
        ''' </summary>
        Public Sub Clear()
            For Each kvp In _priorityQueues
                While kvp.Value.TryDequeue(Nothing)
                End While
            Next
            _log.Info("Message queue cleared")
        End Sub

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            _messageAvailable?.Dispose()
            GC.SuppressFinalize(Me)
        End Sub
    End Class

    ''' <summary>
    ''' Message router - Routes messages between different components
    ''' </summary>
    Public Class MessageRouter
        Implements IDisposable

        Private _incomingQueue As MessageQueue
        Private _outgoingQueue As MessageQueue
        Private _processingThread As Thread
        Private _isRunning As Boolean
        Private _log As Logger

        ''' <summary>
        ''' Event raised when a message needs to be sent to Control24
        ''' </summary>
        Public Event SendToControl24 As EventHandler(Of QueuedMessage)

        ''' <summary>
        ''' Event raised when a message needs to be sent to OSC
        ''' </summary>
        Public Event SendToOSC As EventHandler(Of QueuedMessage)

        ''' <summary>
        ''' Event raised when a message needs to be sent to TCP clients
        ''' </summary>
        Public Event SendToTcpClients As EventHandler(Of QueuedMessage)

        Public Sub New(logger As Logger)
            _log = logger
            _incomingQueue = New MessageQueue(_log)
            _outgoingQueue = New MessageQueue(_log)
        End Sub

        ''' <summary>
        ''' Start message routing
        ''' </summary>
        Public Sub Start()
            If _isRunning Then
                Return
            End If

            _isRunning = True

            _processingThread = New Thread(AddressOf ProcessingLoop) With {
                .Name = "MessageRouter",
                .IsBackground = True
            }
            _processingThread.Start()

            _log.Info("Message router started")
        End Sub

        ''' <summary>
        ''' Stop message routing
        ''' </summary>
        Public Sub [Stop]()
            If Not _isRunning Then
                Return
            End If

            _isRunning = False
            _processingThread?.Join(2000)

            _log.Info("Message router stopped")
        End Sub

        ''' <summary>
        ''' Queue incoming message
        ''' </summary>
        Public Sub QueueIncoming(message As QueuedMessage)
            _incomingQueue.Enqueue(message)
        End Sub

        ''' <summary>
        ''' Queue outgoing message
        ''' </summary>
        Public Sub QueueOutgoing(message As QueuedMessage)
            _outgoingQueue.Enqueue(message)
        End Sub

        ''' <summary>
        ''' Processing loop
        ''' </summary>
        Private Sub ProcessingLoop()
            While _isRunning
                Try
                    ' Process incoming messages
                    Dim incomingMsg = _incomingQueue.Dequeue(100)
                    If incomingMsg IsNot Nothing Then
                        RouteIncomingMessage(incomingMsg)
                    End If

                    ' Process outgoing messages
                    Dim outgoingMsg = _outgoingQueue.Dequeue(100)
                    If outgoingMsg IsNot Nothing Then
                        RouteOutgoingMessage(outgoingMsg)
                    End If

                Catch ex As Exception
                    _log.Error("Error in message router processing loop: {0}", ex.Message)
                    Thread.Sleep(1000)
                End Try
            End While

            _log.Debug("Message router processing loop ended")
        End Sub

        ''' <summary>
        ''' Route incoming message to appropriate handler
        ''' </summary>
        Private Sub RouteIncomingMessage(message As QueuedMessage)
            Try
                _log.Debug("Routing incoming message: {0}", message.ToString())

                Select Case message.MessageType
                    Case MessageType.C24Command
                        ' From Control24 ? Send to OSC and TCP clients
                        RaiseEvent SendToOSC(Me, message)
                        RaiseEvent SendToTcpClients(Me, message)

                    Case MessageType.OscCommand
                        ' From OSC ? Send to Control24
                        RaiseEvent SendToControl24(Me, message)

                    Case MessageType.TcpCommand
                        ' From TCP client ? Send to Control24
                        RaiseEvent SendToControl24(Me, message)

                    Case MessageType.SystemCommand
                        ' System command - handle internally
                        HandleSystemCommand(message)

                    Case Else
                        _log.Warn("Unknown message type: {0}", message.MessageType)
                End Select

            Catch ex As Exception
                _log.Error("Error routing incoming message: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Route outgoing message
        ''' </summary>
        Private Sub RouteOutgoingMessage(message As QueuedMessage)
            Try
                _log.Debug("Routing outgoing message: {0}", message.ToString())

                ' Outgoing messages are responses from the system
                Select Case message.MessageType
                    Case MessageType.C24Command
                        RaiseEvent SendToControl24(Me, message)

                    Case MessageType.OscCommand
                        RaiseEvent SendToOSC(Me, message)

                    Case MessageType.TcpCommand
                        RaiseEvent SendToTcpClients(Me, message)

                    Case Else
                        _log.Warn("Unknown outgoing message type: {0}", message.MessageType)
                End Select

            Catch ex As Exception
                _log.Error("Error routing outgoing message: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Handle system command
        ''' </summary>
        Private Sub HandleSystemCommand(message As QueuedMessage)
            _log.Info("System command: {0}", message.StringData)
            ' TODO: Implement system commands (e.g., save state, load profile, etc.)
        End Sub

        ''' <summary>
        ''' Get queue statistics
        ''' </summary>
        Public Function GetStatistics() As String
            Return $"Incoming: {_incomingQueue.Count} (dropped: {_incomingQueue.DroppedMessages}), " &
                   $"Outgoing: {_outgoingQueue.Count} (dropped: {_outgoingQueue.DroppedMessages})"
        End Function

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            [Stop]()
            _incomingQueue?.Dispose()
            _outgoingQueue?.Dispose()
            GC.SuppressFinalize(Me)
        End Sub
    End Class

End Namespace
