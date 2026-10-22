Namespace DriverReaperControl24.Input
    ''' <summary>
    ''' Définit un injecteur clavier abstrait multi-plateforme.
    ''' </summary>
    Public Interface IKeyboardInjector
        ''' <summary>
        ''' Envoie un événement "touche enfoncée".
        ''' </summary>
        ''' <param name="key">Touche à enfoncer.</param>
        Sub KeyDown(key As KeyboardKey)

        ''' <summary>
        ''' Envoie un événement "touche relâchée".
        ''' </summary>
        ''' <param name="key">Touche à relâcher.</param>
        Sub KeyUp(key As KeyboardKey)

        ''' <summary>
        ''' Simule un appui complet (down puis up).
        ''' </summary>
        ''' <param name="key">Touche à simuler.</param>
        ''' <param name="holdMilliseconds">Durée de maintien en millisecondes.</param>
        Sub PressKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20)

        ''' <summary>
        ''' Simule le raccourci de Ctrl de la plateforme courante.
        ''' Windows : Ctrl+Key. macOS : Cmd+Key.
        ''' </summary>
        ''' <param name="holdMilliseconds">Durée de maintien en millisecondes.</param>
        Sub PressCtrlAndKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20)

        ''' <summary>
        ''' Simule le raccourci de Shift Key de la plateforme courante.
        ''' Windows : Shift+Key. macOS : Maj+Key.
        ''' </summary>
        ''' <param name="holdMilliseconds">Durée de maintien en millisecondes.</param>
        Sub PressShiftAndKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20)
    End Interface
End Namespace