''' <summary>
''' Control24 common functions and default settings
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' 
''' This program is free software: you can redistribute it and/or modify
''' it under the terms of the GNU General Public License as published by
''' the Free Software Foundation, either version 3 of the License, or
''' (at your option) any later version.
''' </summary>

Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Text
Imports Microsoft.Win32

Namespace Reacontrol24

    ''' <summary>
    ''' Default settings and constants
    ''' </summary>
    Public Class Defaults
        Public Shared ReadOnly IP As String = "0.0.0.0"
        Public Shared ReadOnly Daemon As Integer = 9123
        Public Shared ReadOnly Control24Osc As Integer = 9124
        Public Shared ReadOnly OscDaw As Integer = 9125
        Public Shared ReadOnly Auth As String = "be_in-control"
        Public Shared ReadOnly LogLevel As Integer = 2 ' INFO level
        Public Shared ReadOnly InterfaceName As String = "en0"
        Public Shared ReadOnly Scribble As String = "/track/c24scribstrip/name"
        Public Shared ReadOnly LogDir As String = ".\logs"
        Public Shared ReadOnly LogFormat As String = "{0:yyyy-MM-dd HH:mm:ss}" & vbTab & "{1}" & vbTab & "{2}" & vbTab & "{3}" & vbTab & "{4}" & vbTab & "{5}" & vbTab & "{6}"
    End Class

    ''' <summary>
    ''' Command bytes
    ''' </summary>
    Public Class Commands
        Public Const Ack As Byte = &HA0
        Public Const Online As Byte = &HE2
        Public Const BroadcastInit As Byte = &HE0      ' Initial broadcast from Control24
        Public Const BroadcastActive As Byte = &HE1    ' Keep-alive broadcast from Control24
    End Class

    ''' <summary>
    ''' Channel and fader constants
    ''' </summary>
    Public Class Constants
        Public Const Channels As Integer = 24
        Public Const FaderRange As Integer = 1024 ' 2^10
        Public Shared ReadOnly FaderStep As Double = 1.0 / FaderRange
    End Class

    ''' <summary>
    ''' Utility functions
    ''' </summary>
    Public Class Utils
        ''' <summary>
        ''' Get current tick time in seconds
        ''' </summary>
        Public Shared Function Tick() As Double
            Return DateTime.Now.Subtract(New DateTime(1970, 1, 1)).TotalSeconds
        End Function

        ''' <summary>
        ''' Convert byte array to hex string with spaces
        ''' </summary>
        Public Shared Function HexL(data As Byte()) As String
            If data Is Nothing OrElse data.Length = 0 Then
                Return String.Empty
            End If

            Dim sb As New StringBuilder()
            For i As Integer = 0 To data.Length - 1
                If i > 0 Then sb.Append(" ")
                sb.Append(data(i).ToString("X2"))
            Next
            Return sb.ToString()
        End Function

        ''' <summary>
        ''' Fix file ownership (placeholder for Windows)
        ''' </summary>
        Public Shared Sub FixOwnership(path As String)
            ' Windows doesn't need this functionality like Linux does
        End Sub
    End Class

    ''' <summary>
    ''' Logging helper
    ''' </summary>
    Public Class LogHelper
        Private Shared loggers As New Dictionary(Of String, Logger)

        Public Shared Function StartLogging(name As String, logDir As String, Optional debug As Boolean = False) As Logger
            If Not IO.Directory.Exists(logDir) Then
                IO.Directory.CreateDirectory(logDir)
            End If

            Dim logger As New Logger(name, logDir, debug)
            loggers(name) = logger
            Return logger
        End Function
    End Class

    ''' <summary>
    ''' Simple logger class
    ''' </summary>
    Public Class Logger
        Private _name As String
        Private _logPath As String
        Private _debug As Boolean
        Private _writer As IO.StreamWriter

        ''' <summary>
        ''' Event raised when a log message is written
        ''' </summary>
        Public Event OnLogMessage(LogMessage As String)

        Public Sub New(name As String, logDir As String, debug As Boolean)
            _name = name
            _debug = debug
            _logPath = IO.Path.Combine(logDir, $"{name}.log.{DateTime.Now:dd_MM.HH_mm}.csv")

            _writer = New IO.StreamWriter(_logPath, True)
            Info("Logger started")
        End Sub

        Public Sub Debug(message As String, ParamArray args As Object())
            If _debug Then
                Log("DEBUG", String.Format(message, args))
            End If
        End Sub

        Public Sub Info(message As String, ParamArray args As Object())
            Log("INFO", String.Format(message, args))
        End Sub

        Public Sub Warn(message As String, ParamArray args As Object())
            Log("WARN", String.Format(message, args))
        End Sub

        Public Sub [Error](message As String, ParamArray args As Object())
            Log("ERROR", String.Format(message, args))
        End Sub

        Private Sub Log(level As String, message As String)
            Dim logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}" & vbTab & _name & vbTab & level & vbTab & Threading.Thread.CurrentThread.Name & vbTab & message
            _writer.WriteLine(logLine)
            _writer.Flush()
            'Console.WriteLine(logLine)

            ' Raise event for UI
            RaiseEvent OnLogMessage(logLine)
        End Sub

        Public Sub Close()
            _writer?.Close()
        End Sub
    End Class

    ''' <summary>
    ''' Network helper class
    ''' </summary>
    Public Class NetworkHelper
        Private _networks As Dictionary(Of String, NetworkInterfaceInfo)

        Public Class NetworkInterfaceInfo
            Public Property Name As String
            Public Property PcapName As String
            Public Property Mac As String
            Public Property IpAddresses As List(Of IPAddressInfo)
            Public Property IsDefault As Boolean

            Public Sub New()
                IpAddresses = New List(Of IPAddressInfo)
            End Sub
        End Class

        Public Class IPAddressInfo
            Public Property Address As String
            Public Property Mask As String
        End Class

        Public Sub New()
            _networks = ListNetworks()
        End Sub

        Public Overrides Function ToString() As String
            Dim sb As New StringBuilder()
            For Each kvp In _networks
                sb.AppendLine($"{kvp.Key} {If(kvp.Value.Name, "")}")
            Next
            Return sb.ToString()
        End Function

        Public Function GetDefault() As Tuple(Of String, String)
            For Each kvp In _networks
                If kvp.Value.IsDefault Then
                    Dim firstIp = kvp.Value.IpAddresses.FirstOrDefault()
                    If firstIp IsNot Nothing Then
                        Return Tuple.Create(kvp.Key, firstIp.Address)
                    End If
                End If
            Next
            Return Nothing
        End Function

        Public Function [Get](name As String) As NetworkInterfaceInfo
            If _networks.ContainsKey(name) Then
                Return _networks(name)
            End If

            ' Search by friendly name
            For Each kvp In _networks
                If kvp.Value.Name = name Then
                    Return kvp.Value
                End If
            Next

            Return Nothing
        End Function

        Public Function VerifyIp(ipStr As String) As String
            For Each kvp In _networks
                For Each ipInfo In kvp.Value.IpAddresses
                    If ipInfo.Address = ipStr Then
                        Return kvp.Key
                    End If
                Next
            Next
            Return Nothing
        End Function

        Private Shared Function ListNetworks() As Dictionary(Of String, NetworkInterfaceInfo)
            Dim results As New Dictionary(Of String, NetworkInterfaceInfo)
            Dim defaultNotFound As Boolean = True

            For Each nic In NetworkInterface.GetAllNetworkInterfaces()
                If nic.OperationalStatus = OperationalStatus.Up Then
                    Dim info As New NetworkInterfaceInfo With {
                        .Name = nic.Name,
                        .PcapName = nic.Id,
                        .Mac = BitConverter.ToString(nic.GetPhysicalAddress().GetAddressBytes()).Replace("-", ":")
                    }

                    Dim ipProps = nic.GetIPProperties()
                    For Each unicast In ipProps.UnicastAddresses
                        If unicast.Address.AddressFamily = AddressFamily.InterNetwork Then
                            info.IpAddresses.Add(New IPAddressInfo With {
                                .Address = unicast.Address.ToString(),
                                .Mask = unicast.IPv4Mask?.ToString()
                            })

                            If defaultNotFound Then
                                info.IsDefault = True
                                defaultNotFound = False
                            End If
                        End If
                    Next

                    results(nic.Id) = info
                End If
            Next

            Return results
        End Function

        Public Shared Function IpStrToTuple(ipStr As String) As Tuple(Of String, Integer)
            Dim parts = ipStr.Split(":"c)
            If parts.Length = 2 Then
                Return Tuple.Create(parts(0), Integer.Parse(parts(1)))
            End If
            Return Nothing
        End Function

        Public Shared Function IpStrFromTuple(ipAddr As String, ipPort As Integer) As String
            Return $"{ipAddr}:{ipPort}"
        End Function
    End Class

End Namespace
