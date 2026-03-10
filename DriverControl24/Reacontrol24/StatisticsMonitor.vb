''' <summary>
''' Real-time Statistics Monitor
''' Tracks performance metrics and provides real-time monitoring
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' </summary>

Imports System.Threading
Imports System.Diagnostics

Namespace Reacontrol24

    ''' <summary>
    ''' Performance counter data
    ''' </summary>
    Public Class PerformanceData
        Public Property Timestamp As DateTime = DateTime.Now
        Public Property PacketsPerSecond As Integer
        Public Property OscMessagesPerSecond As Integer
        Public Property AverageLatencyMs As Double
        Public Property MemoryUsageMB As Double
        Public Property CpuUsagePercent As Double
        Public Property ActiveTcpClients As Integer
        Public Property QueueSize As Integer
        Public Property DroppedMessages As Long

        Public Overrides Function ToString() As String
            Return $"PPS:{PacketsPerSecond} OSC:{OscMessagesPerSecond} Lat:{AverageLatencyMs:F1}ms Mem:{MemoryUsageMB:F0}MB CPU:{CpuUsagePercent:F1}%"
        End Function
    End Class

    ''' <summary>
    ''' Real-time statistics monitor
    ''' </summary>
    Public Class StatisticsMonitor
        Implements IDisposable

        Private _log As Logger
        Private _monitorThread As Thread
        Private _isRunning As Boolean

        ' Counters
        Private _packetsReceived As Long
        Private _packetsSent As Long
        Private _oscMessagesSent As Long
        Private _oscMessagesReceived As Long
        Private _tcpClients As Integer
        Private _errors As Long
        Private _queueSize As Integer
        Private _droppedMessages As Long

        ' Performance tracking
        Private _lastPacketCount As Long
        Private _lastOscCount As Long
        Private _latencies As New List(Of Double)
        Private _startTime As DateTime

        ' Event for UI updates
        Public Event PerformanceUpdate As EventHandler(Of PerformanceData)

        Public Sub New(logger As Logger)
            _log = logger
            _startTime = DateTime.Now
        End Sub

        ''' <summary>
        ''' Start monitoring
        ''' </summary>
        Public Sub Start()
            If _isRunning Then
                Return
            End If

            _isRunning = True
            _startTime = DateTime.Now

            _monitorThread = New Thread(AddressOf MonitorLoop) With {
                .Name = "StatisticsMonitor",
                .IsBackground = True
            }
            _monitorThread.Start()

            _log.Info("Statistics monitor started")
        End Sub

        ''' <summary>
        ''' Stop monitoring
        ''' </summary>
        Public Sub [Stop]()
            If Not _isRunning Then
                Return
            End If

            _isRunning = False
            _monitorThread?.Join(2000)

            _log.Info("Statistics monitor stopped")
        End Sub

        ''' <summary>
        ''' Monitor loop - runs every second
        ''' </summary>
        Private Sub MonitorLoop()
            While _isRunning
                Try
                    Thread.Sleep(1000)

                    ' Calculate performance metrics
                    Dim currentPackets = Interlocked.Read(_packetsReceived) + Interlocked.Read(_packetsSent)
                    Dim currentOsc = Interlocked.Read(_oscMessagesSent) + Interlocked.Read(_oscMessagesReceived)

                    Dim perfData As New PerformanceData With {
                        .PacketsPerSecond = CInt(currentPackets - _lastPacketCount),
                        .OscMessagesPerSecond = CInt(currentOsc - _lastOscCount),
                        .AverageLatencyMs = CalculateAverageLatency(),
                        .MemoryUsageMB = GetMemoryUsageMB(),
                        .CpuUsagePercent = 0, ' TODO: Implement CPU monitoring
                        .ActiveTcpClients = _tcpClients,
                        .QueueSize = _queueSize,
                        .DroppedMessages = Interlocked.Read(_droppedMessages)
                    }

                    _lastPacketCount = currentPackets
                    _lastOscCount = currentOsc

                    ' Clear latency buffer
                    SyncLock _latencies
                        _latencies.Clear()
                    End SyncLock

                    ' Raise event
                    RaiseEvent PerformanceUpdate(Me, perfData)

                    _log.Debug("Performance: {0}", perfData.ToString())

                Catch ex As Exception
                    _log.Error("Error in monitor loop: {0}", ex.Message)
                End Try
            End While
        End Sub

        ''' <summary>
        ''' Increment packet received
        ''' </summary>
        Public Sub IncrementPacketsReceived()
            Interlocked.Increment(_packetsReceived)
        End Sub

        ''' <summary>
        ''' Increment packet sent
        ''' </summary>
        Public Sub IncrementPacketsSent()
            Interlocked.Increment(_packetsSent)
        End Sub

        ''' <summary>
        ''' Increment OSC sent
        ''' </summary>
        Public Sub IncrementOscSent()
            Interlocked.Increment(_oscMessagesSent)
        End Sub

        ''' <summary>
        ''' Increment OSC received
        ''' </summary>
        Public Sub IncrementOscReceived()
            Interlocked.Increment(_oscMessagesReceived)
        End Sub

        ''' <summary>
        ''' Set TCP clients count
        ''' </summary>
        Public Sub SetTcpClients(count As Integer)
            _tcpClients = count
        End Sub

        ''' <summary>
        ''' Increment errors
        ''' </summary>
        Public Sub IncrementErrors()
            Interlocked.Increment(_errors)
        End Sub

        ''' <summary>
        ''' Set queue size
        ''' </summary>
        Public Sub SetQueueSize(size As Integer)
            _queueSize = size
        End Sub

        ''' <summary>
        ''' Set dropped messages
        ''' </summary>
        Public Sub SetDroppedMessages(count As Long)
            Interlocked.Exchange(_droppedMessages, count)
        End Sub

        ''' <summary>
        ''' Record latency
        ''' </summary>
        Public Sub RecordLatency(latencyMs As Double)
            SyncLock _latencies
                _latencies.Add(latencyMs)
            End SyncLock
        End Sub

        ''' <summary>
        ''' Calculate average latency
        ''' </summary>
        Private Function CalculateAverageLatency() As Double
            SyncLock _latencies
                If _latencies.Count = 0 Then
                    Return 0.0
                End If
                Return _latencies.Average()
            End SyncLock
        End Function

        ''' <summary>
        ''' Get memory usage
        ''' </summary>
        Private Function GetMemoryUsageMB() As Double
            Try
                Dim currentProcess As Process = Process.GetCurrentProcess()
                Return currentProcess.WorkingSet64 / 1024.0 / 1024.0
            Catch
                Return 0.0
            End Try
        End Function

        ''' <summary>
        ''' Get uptime
        ''' </summary>
        Public ReadOnly Property Uptime As TimeSpan
            Get
                Return DateTime.Now - _startTime
            End Get
        End Property

        ''' <summary>
        ''' Get total packets
        ''' </summary>
        Public ReadOnly Property TotalPackets As Long
            Get
                Return Interlocked.Read(_packetsReceived) + Interlocked.Read(_packetsSent)
            End Get
        End Property

        ''' <summary>
        ''' Get total OSC messages
        ''' </summary>
        Public ReadOnly Property TotalOscMessages As Long
            Get
                Return Interlocked.Read(_oscMessagesSent) + Interlocked.Read(_oscMessagesReceived)
            End Get
        End Property

        ''' <summary>
        ''' Get total errors
        ''' </summary>
        Public ReadOnly Property TotalErrors As Long
            Get
                Return Interlocked.Read(_errors)
            End Get
        End Property

        ''' <summary>
        ''' Get statistics summary
        ''' </summary>
        Public Function GetSummary() As String
            Return $"Uptime: {Uptime:hh\:mm\:ss}, Packets: {TotalPackets}, OSC: {TotalOscMessages}, Errors: {TotalErrors}, Clients: {_tcpClients}"
        End Function

        ''' <summary>
        ''' Create statistics object for saving
        ''' </summary>
        Public Function CreateStatistics() As Control24Statistics
            Return New Control24Statistics With {
                .TotalPacketsReceived = Interlocked.Read(_packetsReceived),
                .TotalPacketsSent = Interlocked.Read(_packetsSent),
                .TotalOscMessagesSent = Interlocked.Read(_oscMessagesSent),
                .TotalOscMessagesReceived = Interlocked.Read(_oscMessagesReceived),
                .TotalTcpClientsConnected = _tcpClients,
                .TotalErrors = Interlocked.Read(_errors),
                .UptimeSeconds = CLng(Uptime.TotalSeconds),
                .LastStartTime = _startTime,
                .LastStopTime = DateTime.Now
            }
        End Function

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            [Stop]()
            GC.SuppressFinalize(Me)
        End Sub

    End Class

End Namespace
