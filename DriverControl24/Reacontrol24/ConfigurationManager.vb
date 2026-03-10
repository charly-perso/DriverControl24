''' <summary>
''' Configuration Manager - Handles persistent configuration (XML/JSON)
''' Saves and loads user settings, profiles, and network configurations
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' </summary>

Imports System.IO
Imports System.Xml.Serialization

Namespace Reacontrol24

    ''' <summary>
    ''' Main configuration class - serializable
    ''' </summary>
    <Serializable>
    Public Class Control24Configuration
        Public Property NetworkInterfaceName As String
        Public Property NetworkMacAddress As String
        Public Property NetworkIpAddress As String

        Public Property OscServerPort As Integer = 9124
        Public Property OscDawAddress As String = "127.0.0.1"
        Public Property OscDawPort As Integer = 9125

        Public Property TcpServerPort As Integer = 9123
        Public Property TcpServerEnabled As Boolean = True

        Public Property DebugLogging As Boolean = True
        Public Property LogDirectory As String = ".\logs"

        Public Property AutoStartMonitoring As Boolean = False
        Public Property AutoConnectOSC As Boolean = True

        Public Property LastUsedProfile As String = "Default"
        Public Property Profiles As New List(Of String)

        Public Property CustomSettings As New Dictionary(Of String, String)

        Public Sub New()
            ' Default constructor for serialization
        End Sub
    End Class

    ''' <summary>
    ''' Statistics data - serializable
    ''' </summary>
    <Serializable>
    Public Class Control24Statistics
        Public Property TotalPacketsReceived As Long
        Public Property TotalPacketsSent As Long
        Public Property TotalOscMessagesSent As Long
        Public Property TotalOscMessagesReceived As Long
        Public Property TotalTcpClientsConnected As Long
        Public Property TotalErrors As Long
        Public Property UptimeSeconds As Long
        Public Property LastStartTime As DateTime
        Public Property LastStopTime As DateTime

        Public Sub New()
            ' Default constructor
        End Sub
    End Class

    ''' <summary>
    ''' Configuration Manager
    ''' </summary>
    Public Class ConfigurationManager
        Private Shared _instance As ConfigurationManager
        Private _configPath As String
        Private _statsPath As String
        Private _config As Control24Configuration
        Private _stats As Control24Statistics
        Private _log As Logger

        Private Sub New()
            _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Control24", "config.xml")
            _statsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Control24", "stats.xml")

            ' Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath))
        End Sub

        ''' <summary>
        ''' Singleton instance
        ''' </summary>
        Public Shared ReadOnly Property Instance As ConfigurationManager
            Get
                If _instance Is Nothing Then
                    _instance = New ConfigurationManager()
                End If
                Return _instance
            End Get
        End Property

        ''' <summary>
        ''' Set logger
        ''' </summary>
        Public Sub SetLogger(logger As Logger)
            _log = logger
        End Sub

        ''' <summary>
        ''' Load configuration
        ''' </summary>
        Public Function LoadConfiguration() As Control24Configuration
            Try
                If File.Exists(_configPath) Then
                    Dim serializer As New XmlSerializer(GetType(Control24Configuration))
                    Using reader As New StreamReader(_configPath)
                        _config = CType(serializer.Deserialize(reader), Control24Configuration)
                    End Using
                    _log?.Info("Configuration loaded from {0}", _configPath)
                Else
                    _config = New Control24Configuration()
                    _log?.Info("Using default configuration")
                End If

                Return _config

            Catch ex As Exception
                _log?.Error("Failed to load configuration: {0}", ex.Message)
                _config = New Control24Configuration()
                Return _config
            End Try
        End Function

        ''' <summary>
        ''' Save configuration
        ''' </summary>
        Public Sub SaveConfiguration(config As Control24Configuration)
            Try
                _config = config

                Dim serializer As New XmlSerializer(GetType(Control24Configuration))
                Using writer As New StreamWriter(_configPath)
                    serializer.Serialize(writer, _config)
                End Using

                _log?.Info("Configuration saved to {0}", _configPath)

            Catch ex As Exception
                _log?.Error("Failed to save configuration: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Load statistics
        ''' </summary>
        Public Function LoadStatistics() As Control24Statistics
            Try
                If File.Exists(_statsPath) Then
                    Dim serializer As New XmlSerializer(GetType(Control24Statistics))
                    Using reader As New StreamReader(_statsPath)
                        _stats = CType(serializer.Deserialize(reader), Control24Statistics)
                    End Using
                    _log?.Debug("Statistics loaded")
                Else
                    _stats = New Control24Statistics()
                End If

                Return _stats

            Catch ex As Exception
                _log?.Error("Failed to load statistics: {0}", ex.Message)
                _stats = New Control24Statistics()
                Return _stats
            End Try
        End Function

        ''' <summary>
        ''' Save statistics
        ''' </summary>
        Public Sub SaveStatistics(stats As Control24Statistics)
            Try
                _stats = stats

                Dim serializer As New XmlSerializer(GetType(Control24Statistics))
                Using writer As New StreamWriter(_statsPath)
                    serializer.Serialize(writer, _stats)
                End Using

                _log?.Debug("Statistics saved")

            Catch ex As Exception
                _log?.Error("Failed to save statistics: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Get current configuration
        ''' </summary>
        Public ReadOnly Property CurrentConfiguration As Control24Configuration
            Get
                If _config Is Nothing Then
                    LoadConfiguration()
                End If
                Return _config
            End Get
        End Property

        ''' <summary>
        ''' Get current statistics
        ''' </summary>
        Public ReadOnly Property CurrentStatistics As Control24Statistics
            Get
                If _stats Is Nothing Then
                    LoadStatistics()
                End If
                Return _stats
            End Get
        End Property

        ''' <summary>
        ''' Export configuration to file
        ''' </summary>
        Public Sub ExportConfiguration(filePath As String)
            Try
                If _config Is Nothing Then
                    LoadConfiguration()
                End If

                Dim serializer As New XmlSerializer(GetType(Control24Configuration))
                Using writer As New StreamWriter(filePath)
                    serializer.Serialize(writer, _config)
                End Using

                _log?.Info("Configuration exported to {0}", filePath)

            Catch ex As Exception
                _log?.Error("Failed to export configuration: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Import configuration from file
        ''' </summary>
        Public Function ImportConfiguration(filePath As String) As Control24Configuration
            Try
                Dim serializer As New XmlSerializer(GetType(Control24Configuration))
                Using reader As New StreamReader(filePath)
                    _config = CType(serializer.Deserialize(reader), Control24Configuration)
                End Using

                SaveConfiguration(_config)
                _log?.Info("Configuration imported from {0}", filePath)

                Return _config

            Catch ex As Exception
                _log?.Error("Failed to import configuration: {0}", ex.Message)
                Throw
            End Try
        End Function

        ''' <summary>
        ''' Reset to defaults
        ''' </summary>
        Public Sub ResetToDefaults()
            _config = New Control24Configuration()
            SaveConfiguration(_config)
            _log?.Info("Configuration reset to defaults")
        End Sub

    End Class

End Namespace
