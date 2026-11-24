using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace NobleBodyguards
{
    /// <summary>Provides the MCM options for Noble Bodyguards.</summary>
    public class NobleBodyguardsSettings : AttributeGlobalSettings<NobleBodyguardsSettings>
    {
        public override string Id => "NobleBodyguards";

        public override string DisplayName => "Noble Bodyguards";

        public override string FolderName => "NobleBodyguards";

        public override string FormatType => "json2";

        /// <summary>Gets or sets whether bodyguards are added to eligible settlement missions.</summary>
        [SettingPropertyBool(
            "Enable bodyguards",
            Order = 0,
            RequireRestart = false,
            HintText = "Spawn bodyguards in eligible settlement missions.")]
        public bool Enabled { get; set; } = true;

        /// <summary>Gets or sets whether only the player receives bodyguards, disabling companion and lord escorts.</summary>
        [SettingPropertyBool(
            "Player bodyguards only",
            Order = 1,
            RequireRestart = false,
            HintText = "When enabled, only the player receives bodyguards. Companions and lords get none.")]
        public bool PlayerOnly { get; set; } = false;
    }
}