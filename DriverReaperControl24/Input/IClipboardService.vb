Namespace DriverReaperControl24.Input
    ''' <summary>
    ''' Définit un service d'accès au presse-papiers multi-plateforme.
    ''' </summary>
    Public Interface IClipboardService
        ''' <summary>
        ''' Lit le texte courant du presse-papiers.
        ''' </summary>
        ''' <returns>Le texte courant, ou une chaîne vide si indisponible.</returns>
        Function GetText() As String

        ''' <summary>
        ''' Écrit un texte dans le presse-papiers.
        ''' </summary>
        ''' <param name="text">Texte à copier dans le presse-papiers.</param>
        ''' <returns>True si l'écriture a réussi, sinon False.</returns>
        Function SetText(text As String) As Boolean
    End Interface
End Namespace