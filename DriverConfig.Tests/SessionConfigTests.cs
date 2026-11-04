using DriverConfig;

namespace DriverConfig.Tests;

[TestClass]
public class SessionConfigTests
{
    private const string SampleIni = """
        [Driver]
        AUTOSTART=True
        NETWORKINTERFACE=TestWithoutC24
        VSDEBUG=False

        [Plugins]
        Plugin0=VST: ReaComp (Cockos)|1
        Plugin1=VST: ReaEQ (Cockos)|1
        Plugin2=JS: Gain|0

        [Pluginshortnames]
        PluginShortName0=VST: ReaComp (Cockos)|ReaComp
        PluginShortName1=VST: ReaEQ (Cockos)|ReaEQ
        PluginShortName2=JS: Gain|Gain

        [Eq]
        EQ0=VST: ReaEQ (Cockos)

        [Dyn]
        DYN0=VST: ReaComp (Cockos)

        ; commentaire de section
        [FxParams:ReaComp]
        p0_i=0
        p0_n=Threshold
        p0_s=Thrs
        p0_l=Thresh
        p0_v=1
        text="Sep"
        p1_i=1
        p1_n=Ratio
        p1_s=Rati
        p1_l=Ratio
        p1_v=0

        [FxParams:Ghost]
        p0_i=0
        p0_n=Unknown
        p0_s=Unkn
        p0_l=Unknown
        p0_v=1
        """;

    private string _dir = "";
    private string _ini = "";

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "c24cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _ini = Path.Combine(_dir, "ReaperDriverControl24.ini");
        File.WriteAllText(_ini, SampleIni);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [TestMethod]
    public void Load_ReadsAllSections()
    {
        var cfg = new ReaperSessionConfigBase(_ini);
        Assert.AreEqual(3, cfg.Plugins.Count);
        Assert.IsFalse(cfg.IsPluginVisible("JS: Gain"));
        Assert.IsTrue(cfg.IsEQPlugin("VST: ReaEQ (Cockos)"));
        Assert.IsTrue(cfg.IsDynPlugin("VST: ReaComp (Cockos)"));
        var p = cfg.GetFxParams("VST: ReaComp (Cockos)");
        Assert.AreEqual(3, p.Count);
        Assert.AreEqual("Sep", p[1].TextOnly);
        Assert.AreEqual(-1, p[1].ParamIndex);
        Assert.IsFalse(p[2].Visible);
    }

    [TestMethod]
    public void SaveWithoutChange_IsStable()
    {
        new ReaperSessionConfigBase(_ini).Save();
        string first = File.ReadAllText(_ini);
        new ReaperSessionConfigBase(_ini).Save();
        Assert.AreEqual(first, File.ReadAllText(_ini));
    }

    [TestMethod]
    public void ModifyingExistingFxParam_IsPersisted()
    {
        const string fx = "VST: ReaComp (Cockos)";
        var cfg = new ReaperSessionConfigBase(_ini);
        cfg.SetFxParamShortName(fx, 0, "Thr");
        cfg.SetFxParamVisible(fx, 1, true);

        var reloaded = new ReaperSessionConfigBase(_ini);
        Assert.AreEqual("Thr ", reloaded.GetFxParamShortName(fx, 0));
        Assert.IsTrue(reloaded.IsFxParamVisible(fx, 1));
        Assert.AreEqual("Sep", reloaded.GetFxParams(fx)[1].TextOnly);
    }

    [TestMethod]
    public void SetFxParams_PersistsOrderAndTextSlots()
    {
        const string fx = "VST: ReaComp (Cockos)";
        var cfg = new ReaperSessionConfigBase(_ini);
        var list = cfg.GetFxParams(fx);
        list.Reverse();
        list.Add(new FxParamConfig { ParamIndex = -1, TextOnly = "Fin" });
        cfg.SetFxParams(fx, list);

        var after = new ReaperSessionConfigBase(_ini).GetFxParams(fx);
        Assert.AreEqual(4, after.Count);
        Assert.AreEqual(1, after[0].ParamIndex);
        Assert.AreEqual("Sep", after[1].TextOnly);
        Assert.AreEqual(0, after[2].ParamIndex);
        Assert.AreEqual("Fin", after[3].TextOnly);
    }

    [TestMethod]
    public void RenamingPluginShortName_MovesItsFxParamsSection()
    {
        const string fx = "VST: ReaComp (Cockos)";
        var cfg = new ReaperSessionConfigBase(_ini);
        cfg.SetPluginShortName(fx, "Comp");

        Assert.Contains("[FxParams:Comp]", File.ReadAllText(_ini));
        var reloaded = new ReaperSessionConfigBase(_ini);
        Assert.AreEqual(3, reloaded.GetFxParams(fx).Count);
    }

    [TestMethod]
    public void ShortName_Validation()
    {
        var cfg = new ReaperSessionConfigBase(_ini);
        Assert.IsNotNull(cfg.ValidatePluginShortName("JS: Gain", ""));
        Assert.IsNotNull(cfg.ValidatePluginShortName("JS: Gain", "123456789"));
        Assert.IsNotNull(cfg.ValidatePluginShortName("JS: Gain", "reacomp"));   // doublon (insensible à la casse)
        Assert.IsNotNull(cfg.ValidatePluginShortName("JS: Gain", "a|b"));
        Assert.IsNull(cfg.ValidatePluginShortName("JS: Gain", "Gain2"));
        Assert.IsNull(cfg.ValidatePluginShortName("VST: ReaComp (Cockos)", "ReaComp")); // son propre nom
    }

    [TestMethod]
    public void SetPluginShortNames_AllowsSwap_AndRejectsDuplicates()
    {
        var cfg = new ReaperSessionConfigBase(_ini);
        cfg.SetPluginShortNames(new Dictionary<string, string>
        {
            ["VST: ReaComp (Cockos)"] = "ReaEQ",
            ["VST: ReaEQ (Cockos)"] = "ReaComp",
        });
        var reloaded = new ReaperSessionConfigBase(_ini);
        Assert.AreEqual("ReaEQ", reloaded.GetPluginShortName("VST: ReaComp (Cockos)").Trim());
        Assert.AreEqual(3, reloaded.GetFxParams("VST: ReaComp (Cockos)").Count);

        Assert.Throws<ArgumentException>(() => reloaded.SetPluginShortNames(
            new Dictionary<string, string> { ["JS: Gain"] = "reaeq" }));
        Assert.AreEqual("Gain", reloaded.GetPluginShortName("JS: Gain").Trim());
    }

    [TestMethod]
    public void UnknownFxParamsSection_IsPreserved()
    {
        var cfg = new ReaperSessionConfigBase(_ini);
        cfg.Autostart = false;
        string text = File.ReadAllText(_ini);
        Assert.Contains("[FxParams:Ghost]", text);
        Assert.Contains("p0_n=Unknown", text);
    }

    [TestMethod]
    public void Batch_WritesOnlyOnce_AtTheEnd()
    {
        var cfg = new ReaperSessionConfigBase(_ini);
        string before = File.ReadAllText(_ini);
        using (cfg.BeginBatch())
        {
            cfg.Autostart = false;
            cfg.VSDebug = true;
            Assert.AreEqual(before, File.ReadAllText(_ini));
        }
        var reloaded = new ReaperSessionConfigBase(_ini);
        Assert.IsFalse(reloaded.Autostart);
        Assert.IsTrue(reloaded.VSDebug);
    }

    [TestMethod]
    public void EqDyn_RemoveIsNotReinjectedFromFile()
    {
        var cfg = new ReaperSessionConfigBase(_ini);
        cfg.RemoveEqPlugin("VST: ReaEQ (Cockos)");
        cfg.AddEqPlugin("JS: Gain");
        var reloaded = new ReaperSessionConfigBase(_ini);
        CollectionAssert.AreEqual(new[] { "JS: Gain" }, reloaded.EQPlugins.ToArray());
    }

    [TestMethod]
    public void Lists_AreReadInNaturalOrder()
    {
        var names = Enumerable.Range(0, 12).Select(i => $"Plug{i}").ToList();
        File.WriteAllText(_ini, "[Plugins]\n" + string.Join("\n", names.Select((n, i) => $"Plugin{i}={n}|1")) + "\n");
        var cfg = new ReaperSessionConfigBase(_ini);
        CollectionAssert.AreEqual(names, cfg.Plugins.ToList());
    }

    [TestMethod]
    public void Reload_DiscardsInMemoryChanges()
    {
        var cfg = new ReaperSessionConfigBase(_ini);
        using (cfg.BeginBatch()) { cfg.Autostart = false; cfg.Reload(); }
        Assert.IsTrue(cfg.Autostart);
    }
}
