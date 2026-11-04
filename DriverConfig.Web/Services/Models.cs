namespace DriverConfig.Web.Services;

/// <summary>Ligne éditable de la liste des plugins.</summary>
public sealed class PluginEdit
{
    public required string Name { get; init; }
    public required string OriginalShortName { get; init; }
    public required bool OriginalVisible { get; init; }
    public required bool OriginalEq { get; init; }
    public required bool OriginalDyn { get; init; }
    public bool HasParams { get; init; }

    public string ShortName { get; set; } = "";
    public bool Visible { get; set; }
    public bool Eq { get; set; }
    public bool Dyn { get; set; }

    public bool IsDirty => ShortName.Trim() != OriginalShortName.Trim()
                           || Visible != OriginalVisible || Eq != OriginalEq || Dyn != OriginalDyn;

    /// <summary>Type du plugin d'après son préfixe (VST3, VST, JS, CLAP…), « Autre » sinon.</summary>
    public string Kind
    {
        get
        {
            int colon = Name.IndexOf(':');
            return colon is > 0 and <= 6 ? Name[..colon].ToUpperInvariant() : "Autre";
        }
    }
}

/// <summary>Ligne éditable d'un paramètre FX (ou d'un séparateur texte).</summary>
public sealed class ParamEdit
{
    public Guid Id { get; } = Guid.NewGuid();
    /// <summary>Index Reaper du paramètre, -1 pour une ligne de texte.</summary>
    public int ParamIndex { get; set; }
    public string FullName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string LongName { get; set; } = "";
    public bool Visible { get; set; } = true;
    public string Text { get; set; } = "";

    public bool IsText => ParamIndex < 0;

    public ParamEdit Clone() => new()
    {
        ParamIndex = ParamIndex, FullName = FullName, ShortName = ShortName,
        LongName = LongName, Visible = Visible, Text = Text
    };

    public bool SameContent(ParamEdit o) =>
        ParamIndex == o.ParamIndex && FullName == o.FullName && ShortName.TrimEnd() == o.ShortName.TrimEnd()
        && LongName.TrimEnd() == o.LongName.TrimEnd() && Visible == o.Visible && Text == o.Text;
}

public sealed record DriverSettings(bool Autostart, bool VsDebug, string NetworkInterface,
                                    IReadOnlyList<string> Interfaces, bool InterfaceValid);

public sealed record BackupInfo(string FileName, DateTime TimestampUtc, long Size);
