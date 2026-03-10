' Ajouter un flag de test dans la classe
Private _testModeActive As Boolean = False

' Méthode publique pour activer/désactiver le mode test
Public Sub SetTestMode(enabled As Boolean)
    _testModeActive = enabled
    If enabled Then
        _logger?.Info("Test mode ACTIVE - Broadcasts will be ignored during tests")
    Else
        _logger?.Info("Test mode INACTIVE - Normal broadcast handling resumed")
    End If
End Sub

' Modifier HandleBroadcastPacket pour ignorer les broadcasts en mode test
Private Sub HandleBroadcastPacket(data As Byte())
    Try
        Dim dataLen = data.Length
        _logger?.Debug($"HandleBroadcastPacket: MAC={BitConverter.ToString(data, 6, 6)} IsVendor={IsAvidVendorMAC(data, 6)} DataLen={dataLen}")

        ' ✅ NOUVEAU: Ignorer les broadcasts en mode test
        If _testModeActive Then
            _logger?.Debug("Test mode active - Broadcast ignored")
            Return
        End If

        If Not IsAvidVendorMAC(data, 6) Then
            Return
        End If

        ' ... reste du code inchangé ...
    Catch ex As Exception
        _logger?.Error($"HandleBroadcastPacket error: {ex.Message}")
    End Try
End Sub