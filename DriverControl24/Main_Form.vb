Public Class Main_Form
    Dim allowVisible As Boolean = False
    Dim _Network_settings_Form As Network_settings_Form
    Sub New()

        ' Cet appel est requis par le concepteur.
        InitializeComponent()

        ' Ajoutez une initialisation quelconque après l'appel InitializeComponent().
        If _Network_settings_Form Is Nothing Then
            _Network_settings_Form = New Network_settings_Form
        End If
        _Network_settings_Form.Show()
    End Sub
    Private Sub Main_Load(sender As Object, e As EventArgs) Handles MyBase.Load



    End Sub

    Sub NotifyIcon1_Click() Handles NotifyIcon1.Click
        ContextMenuStrip1.Show(MousePosition)

    End Sub

    Private Sub Control24SettingsToolStripMenuItem_Click(sender As Object, e As EventArgs) Handles Control24SettingsToolStripMenuItem.Click
        Dim _Control24_Settings_Form As New Control24_Settings_Form
        _Control24_Settings_Form.Show()

    End Sub

    Private Sub QuitToolStripMenuItem_Click(sender As Object, e As EventArgs) Handles QuitToolStripMenuItem.Click
        Me.Dispose()
    End Sub



    Protected Overrides Sub SetVisibleCore(value As Boolean)
        MyBase.SetVisibleCore(allowVisible)
    End Sub
    'Network settings
    Private Sub SettingsToolStripMenuItem_Click(sender As Object, e As EventArgs) Handles SettingsToolStripMenuItem.Click
        If _Network_settings_Form Is Nothing Then
            _Network_settings_Form = New Network_settings_Form
        End If
        _Network_settings_Form.Show()
    End Sub
End Class