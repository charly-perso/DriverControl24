using DriverConfig.Web.Services;

namespace DriverConfig.Tests;

[TestClass]
public class LongNameModeTests
{
    private static ParamEdit P(int i, string name, string shortName, string longName) =>
        new() { ParamIndex = i, FullName = name, ShortName = shortName, LongName = longName };

    private static ParamEdit T(string text) => new() { ParamIndex = -1, Text = text, Visible = false };

    /// <summary>Thre+shol(Long) – Ratio(Court) – Atta+ck(Long) – Mix(Court)</summary>
    private static List<ParamEdit> Sample() =>
    [
        T("Thre"), P(0, "Threshold", "shol", "Threshol"),
        P(1, "Ratio", "Rati", "Ratio"),
        T("Atta"), P(2, "Attack", "ck", "Attack"),
        P(3, "Mix", "Mix", "Mix"),
    ];

    private static string Order(List<ParamEdit> rows) =>
        string.Join(",", rows.Select(r => r.IsText ? "[" + r.Text + "]" : r.FullName));

    [TestMethod]
    public void IsLong_RequiresMatchingSeparator()
    {
        var rows = Sample();
        Assert.IsTrue(LongNameMode.IsLong(rows, 1));
        Assert.IsFalse(LongNameMode.IsLong(rows, 2));
        rows[0].Text = "Xxxx";
        Assert.IsFalse(LongNameMode.IsLong(rows, 1));
    }

    [TestMethod]
    public void IsLong_RequiresConcatenationToEqualLongName()
    {
        var rows = Sample();
        Assert.IsTrue(LongNameMode.IsLong(rows, 1));          // "Thre" + "shol" = "Threshol"

        rows[1].ShortName = "hold";                           // "Thre" + "hold" ≠ "Threshol"
        Assert.IsFalse(LongNameMode.IsLong(rows, 1));
        Assert.IsTrue(LongNameMode.HasMatchingSeparator(rows, 1));
        Assert.IsFalse(LongNameMode.IsLinkedSeparator(rows, 0));
    }

    [TestMethod]
    public void UnlinkedPair_MovesAsTwoSeparateRows()
    {
        var rows = Sample();
        rows[1].ShortName = "hold";
        LongNameMode.MoveBlock(rows, 1, +1);                  // le paramètre seul descend
        Assert.AreEqual("[Thre],Ratio,Threshold,[Atta],Attack,Mix", Order(rows));
    }

    [TestMethod]
    public void SetLong_ReusesExistingSeparator_InsteadOfAddingASecondOne()
    {
        var rows = Sample();
        rows[1].ShortName = "hold";                           // séparateur correct, nom court faux
        LongNameMode.SetLong(rows, 1);
        Assert.AreEqual("[Thre],Threshold,Ratio,[Atta],Attack,Mix", Order(rows));
        Assert.AreEqual("shol", rows[1].ShortName);
        Assert.IsTrue(LongNameMode.IsLong(rows, 1));
    }

    [TestMethod]
    public void SetLong_InsertsSeparator_AndSplitsName()
    {
        var rows = Sample();
        LongNameMode.SetLong(rows, 2);
        Assert.AreEqual("[Thre],Threshold,[Rati],Ratio,[Atta],Attack,Mix", Order(rows));
        Assert.AreEqual("o", rows[3].ShortName);
    }

    [TestMethod]
    public void SetShort_RemovesOnlyMatchingSeparator()
    {
        var rows = Sample();
        LongNameMode.SetShort(rows, 1);
        Assert.AreEqual("Threshold,Ratio,[Atta],Attack,Mix", Order(rows));
        Assert.AreEqual("Thre", rows[0].ShortName);

        // Séparateur qui ne correspond pas : conservé.
        rows = Sample();
        rows[0].Text = "Autre";
        LongNameMode.SetShort(rows, 1);
        Assert.AreEqual("[Autre],Threshold,Ratio,[Atta],Attack,Mix", Order(rows));
    }

    [TestMethod]
    public void EditLongName_InLongMode_KeepsSeparatorAndShortInSync()
    {
        var rows = Sample();
        LongNameMode.EditLongName(rows, 1, "Limitxyz");
        Assert.AreEqual("Limi", rows[0].Text);
        Assert.AreEqual("txyz", rows[1].ShortName);
        Assert.IsTrue(LongNameMode.IsLong(rows, 1));
    }

    [TestMethod]
    public void EditSeparatorText_InLinkedBlock_UpdatesLongNameAndStaysLinked()
    {
        var rows = Sample();
        LongNameMode.EditSeparatorText(rows, 0, "Seui");
        Assert.AreEqual("Seuishol", rows[1].LongName);
        Assert.IsTrue(LongNameMode.IsLong(rows, 1));

        LongNameMode.EditSeparatorText(rows, 0, "Se");
        Assert.AreEqual("Se  shol", rows[1].LongName);
        Assert.IsTrue(LongNameMode.IsLong(rows, 1));
    }

    [TestMethod]
    public void EditShortName_InLinkedBlock_UpdatesLongNameAndStaysLinked()
    {
        var rows = Sample();
        LongNameMode.EditShortName(rows, 1, "old");
        Assert.AreEqual("Threold", rows[1].LongName);
        Assert.IsTrue(LongNameMode.IsLong(rows, 1));
    }

    [TestMethod]
    public void EditShortName_OutsideLinkedBlock_LeavesLongNameAlone()
    {
        var rows = Sample();
        LongNameMode.EditShortName(rows, 2, "Rat");
        Assert.AreEqual("Ratio", rows[2].LongName);
        rows[4].LongName = "Attacks";                        // plus de lien : "Atta"+"ck" ≠ "Attacks"
        LongNameMode.EditSeparatorText(rows, 3, "Xx");
        Assert.AreEqual("Attacks", rows[4].LongName);
    }

    [TestMethod]
    public void MoveBlock_MovesParamWithItsSeparator()
    {
        var rows = Sample();
        LongNameMode.MoveBlock(rows, 1, +1);                       // Threshold (Long) descend d'un cran
        Assert.AreEqual("Ratio,[Thre],Threshold,[Atta],Attack,Mix", Order(rows));

        LongNameMode.MoveBlock(rows, 4, -1);                       // Attack (Long) remonte au-dessus de Threshold
        Assert.AreEqual("Ratio,[Atta],Attack,[Thre],Threshold,Mix", Order(rows));

        LongNameMode.MoveBlock(rows, 0, -1);                       // déjà en tête : rien
        Assert.AreEqual("Ratio,[Atta],Attack,[Thre],Threshold,Mix", Order(rows));

        LongNameMode.MoveBlock(rows, 4, +1);                       // en désignant le séparateur, le bloc bouge aussi
        Assert.AreEqual("Ratio,[Atta],Attack,Mix,[Thre],Threshold", Order(rows));
        LongNameMode.MoveBlock(rows, 5, +1);                       // déjà en fin : rien
        Assert.AreEqual("Ratio,[Atta],Attack,Mix,[Thre],Threshold", Order(rows));
    }

    [TestMethod]
    public void MoveBlock_ShortParamStepsOverWholeNeighbourBlock()
    {
        var rows = Sample();
        LongNameMode.MoveBlock(rows, 2, -1);                       // Ratio remonte au-dessus du bloc Thre+Threshold
        Assert.AreEqual("Ratio,[Thre],Threshold,[Atta],Attack,Mix", Order(rows));
    }

    [TestMethod]
    public void DropBlock_WorksInBothDirections_AndIgnoresDropOnItself()
    {
        var rows = Sample();
        LongNameMode.DropBlock(rows, 1, 5);                        // bloc Threshold déposé sur Mix
        Assert.AreEqual("Ratio,[Atta],Attack,Mix,[Thre],Threshold", Order(rows));

        LongNameMode.DropBlock(rows, 5, 0);                        // Threshold (Long) remonté sur Ratio
        Assert.AreEqual("[Thre],Threshold,Ratio,[Atta],Attack,Mix", Order(rows));

        LongNameMode.DropBlock(rows, 0, 1);                        // déposé sur son propre paramètre
        Assert.AreEqual("[Thre],Threshold,Ratio,[Atta],Attack,Mix", Order(rows));

        LongNameMode.DropBlock(rows, 2, 4);                        // Ratio (simple) déposé sur le param d'un bloc Long
        Assert.AreEqual("[Thre],Threshold,[Atta],Attack,Ratio,Mix", Order(rows));
    }
}
