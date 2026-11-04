namespace DriverConfig.Web.Services;

/// <summary>
/// Affichage « Long » d'un paramètre : le nom long (8 car.) est réparti sur deux strips.
/// Une ligne de texte placée juste avant le paramètre porte les 4 premiers caractères,
/// et le nom court du paramètre porte les 4 derniers.
/// Le mode n'est pas stocké : un paramètre est « lié » (Long) quand la ligne de texte qui le précède
/// et son nom court, mis bout à bout, redonnent exactement son nom long.
/// </summary>
public static class LongNameMode
{
    /// <summary>Un nom long de 4 caractères ou moins ne peut pas être réparti sur deux strips.</summary>
    public static bool CanBeLong(ParamEdit p) => !p.IsText && p.LongName.TrimEnd().Length > 4;

    public static string FirstHalf(string longName) => longName.PadRight(8)[..4];

    public static string SecondHalf(string longName) => longName.PadRight(8)[4..8];

    /// <summary>Vrai si la ligne précédant le paramètre est un texte qui reprend les 4 premiers caractères de son nom long.</summary>
    public static bool HasMatchingSeparator(IReadOnlyList<ParamEdit> rows, int index)
    {
        if (index <= 0 || index >= rows.Count) return false;
        var p = rows[index];
        var prev = rows[index - 1];
        return !p.IsText && prev.IsText
               && prev.Text.TrimEnd() == FirstHalf(p.LongName).TrimEnd();
    }

    /// <summary>
    /// Vrai si séparateur + nom court (4 + 4 caractères) redonnent exactement le nom long :
    /// le paramètre et son séparateur forment alors un bloc lié.
    /// </summary>
    public static bool IsLong(IReadOnlyList<ParamEdit> rows, int index)
    {
        if (!HasMatchingSeparator(rows, index)) return false;
        var p = rows[index];
        var text = rows[index - 1].Text;
        return text.Length <= 4 && p.ShortName.Length <= 4
               && text.PadRight(4) + p.ShortName.PadRight(4) == p.LongName.PadRight(8);
    }

    /// <summary>Vrai si la ligne est le séparateur d'un bloc lié (le paramètre suivant est lié à elle).</summary>
    public static bool IsLinkedSeparator(IReadOnlyList<ParamEdit> rows, int index)
        => index >= 0 && index + 1 < rows.Count && rows[index].IsText && IsLong(rows, index + 1);

    /// <summary>
    /// Bloc déplaçable contenant la ligne : séparateur + paramètre s'ils forment une paire « Long »
    /// (que l'on désigne l'un ou l'autre), sinon la ligne seule.
    /// </summary>
    public static (int Start, int Count) BlockRange(IReadOnlyList<ParamEdit> rows, int index)
    {
        if (rows[index].IsText)
            return index + 1 < rows.Count && IsLong(rows, index + 1) ? (index, 2) : (index, 1);
        return IsLong(rows, index) ? (index - 1, 2) : (index, 1);
    }

    /// <summary>Monte (-1) ou descend (+1) le bloc de la ligne d'un cran, séparateur compris.</summary>
    public static void MoveBlock(List<ParamEdit> rows, int index, int delta)
    {
        var (start, count) = BlockRange(rows, index);
        int neighbour = delta < 0 ? start - 1 : start + count;
        if (neighbour < 0 || neighbour >= rows.Count) return;
        var nb = BlockRange(rows, neighbour);

        var block = rows.GetRange(start, count);
        rows.RemoveRange(start, count);
        rows.InsertRange(delta < 0 ? nb.Start : start + nb.Count, block);
    }

    /// <summary>
    /// Dépose le bloc de la ligne <paramref name="from"/> à la place de la ligne <paramref name="target"/>
    /// (avant le bloc cible en remontant, après en descendant).
    /// </summary>
    public static void DropBlock(List<ParamEdit> rows, int from, int target)
    {
        var (start, count) = BlockRange(rows, from);
        if (target >= start && target < start + count) return;
        var nb = BlockRange(rows, target);

        var block = rows.GetRange(start, count);
        bool down = target > start;
        rows.RemoveRange(start, count);
        rows.InsertRange(down ? nb.Start + nb.Count - count : nb.Start, block);
    }

    /// <summary>Passe en Long : insère le séparateur et garde les 4 derniers caractères en nom court.</summary>
    public static void SetLong(List<ParamEdit> rows, int index)
    {
        var p = rows[index];
        if (IsLong(rows, index) || !CanBeLong(p)) return;
        if (!HasMatchingSeparator(rows, index))
            rows.Insert(index, new ParamEdit { ParamIndex = -1, Visible = false, Text = FirstHalf(p.LongName).TrimEnd() });
        p.ShortName = SecondHalf(p.LongName).TrimEnd();
    }

    /// <summary>
    /// Repasse en Court : supprime le séparateur précédent seulement s'il correspond aux
    /// 4 premiers caractères du nom long, puis le nom court reprend ces 4 caractères.
    /// </summary>
    public static void SetShort(List<ParamEdit> rows, int index)
    {
        var p = rows[index];
        if (p.IsText) return;
        if (HasMatchingSeparator(rows, index)) rows.RemoveAt(index - 1);
        p.ShortName = FirstHalf(p.LongName).TrimEnd();
    }

    /// <summary>
    /// Modifie le texte d'une ligne séparateur. Si elle est liée à son paramètre, le nom long
    /// devient la concaténation séparateur + nom court et le bloc reste lié.
    /// </summary>
    public static void EditSeparatorText(List<ParamEdit> rows, int index, string newText)
    {
        bool linked = IsLinkedSeparator(rows, index);
        rows[index].Text = newText;
        if (linked) rows[index + 1].LongName = Concat(newText, rows[index + 1].ShortName);
    }

    /// <summary>
    /// Modifie le nom court d'un paramètre. En mode Long, le nom long devient la concaténation
    /// séparateur + nom court et le bloc reste lié.
    /// </summary>
    public static void EditShortName(List<ParamEdit> rows, int index, string newShort)
    {
        bool linked = IsLong(rows, index);
        rows[index].ShortName = newShort;
        if (linked) rows[index].LongName = Concat(rows[index - 1].Text, newShort);
    }

    private static string Concat(string text, string shortName) => (text.PadRight(4) + shortName.PadRight(4)).TrimEnd();

    /// <summary>Modifie le nom long ; en mode Long, séparateur et nom court suivent.</summary>
    public static void EditLongName(List<ParamEdit> rows, int index, string newLongName)
    {
        var p = rows[index];
        bool wasLong = IsLong(rows, index);
        p.LongName = newLongName;
        if (!wasLong) return;
        rows[index - 1].Text = FirstHalf(newLongName).TrimEnd();
        p.ShortName = SecondHalf(newLongName).TrimEnd();
    }
}
