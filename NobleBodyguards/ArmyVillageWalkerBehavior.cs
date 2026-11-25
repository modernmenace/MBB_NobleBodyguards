using System;
using System.Collections.Generic;
using System.Linq;
using SandBox;
using SandBox.CampaignBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.CampaignSystem.Settlements.Locations.LocationCharacter;

namespace NobleBodyguards
{
    /// <summary>
    /// Adds non-hero troops from lord armies present in a village as walkers alongside the villagers.
    /// </summary>
    public class ArmyVillageWalkerBehavior : CampaignBehaviorBase
    {
        /// <summary>The share of the village spawn points given to army walkers when few troops are available.</summary>
        private const float MinWalkerShare = 0.1f;

        /// <summary>The share of the village spawn points given to army walkers when the troop count reaches the full strength value.</summary>
        private const float MaxWalkerShare = 0.75f;

        /// <summary>The troop count at which the walker share reaches its maximum.</summary>
        private const int FullStrengthTroopCount = 1000;

        /// <summary>
        /// Registers the campaign event used to add walkers when a village center is about to spawn its characters.
        /// </summary>
        public override void RegisterEvents()
        {
            CampaignEvents.LocationCharactersAreReadyToSpawnEvent.AddNonSerializedListener(this, OnLocationCharactersAreReadyToSpawn);
        }

        /// <summary>
        /// Synchronizes behavior state with the campaign data store.
        /// </summary>
        /// <param name="dataStore">The campaign data store.</param>
        public override void SyncData(IDataStore dataStore)
        {
        }

        /// <summary>
        /// Adds army troop walkers to the village center when the MCM option is enabled.
        /// </summary>
        /// <param name="usablePointCount">The usable spawn point counts per location type.</param>
        private void OnLocationCharactersAreReadyToSpawn(Dictionary<string, int> usablePointCount)
        {
            NobleBodyguardsSettings settings = NobleBodyguardsSettings.Instance;
            if (!settings.Enabled || !settings.ArmyTroopsInVillages || Campaign.Current == null || CampaignMission.Current == null)
            {
                Log("Skipped: settings disabled or no mission.");
                return;
            }

            Settlement settlement = PlayerEncounter.LocationEncounter?.Settlement;
            if (settlement == null || !settlement.IsVillage)
            {
                Log("Skipped: no village encounter (settlement null: " + (settlement == null) + ").");
                return;
            }

            Location villageCenter = settlement.LocationComplex.GetLocationWithId("village_center");
            if (villageCenter == null || CampaignMission.Current.Location != villageCenter)
            {
                Log("Skipped: mission is not village_center (villageCenter null: " + (villageCenter == null) + ").");
                return;
            }

            if (!usablePointCount.TryGetValue("npc_common", out int points) || points <= 0)
            {
                Log("Skipped: no npc_common points.");
                return;
            }

            List<WalkerTroop> pool = GetArmyTroopPool(settlement);
            if (pool.Count == 0)
            {
                Log("Skipped: no lord army troops in " + settlement.Name + ".");
                return;
            }

            int walkerCount = GetWalkerCount(points, pool.Count);
            if (walkerCount <= 0)
            {
                Log("Skipped: " + points + " npc_common points and " + pool.Count + " troops give 0 walkers.");
                return;
            }

            List<WalkerTroop> selected = pool
                .OrderBy(walker => MBRandom.RandomFloat)
                .Take(walkerCount)
                .ToList();

            Log("Spawning " + selected.Count + " walkers from " + pool.Count + " troops (" + points + " npc_common points).");

            CultureObject culture = settlement.Culture;
            foreach (IGrouping<object, WalkerTroop> group in selected.GroupBy(walker => new { walker.Character, walker.Owner }))
            {
                WalkerTroop walker = group.First();
                villageCenter.AddLocationCharacters(
                    (CreateLocationCharacterDelegate)((cultureObject, relation) => CreateWalker(walker, relation)),
                    culture,
                    default(CharacterRelations),
                    group.Count());
            }
        }

        /// <summary>
        /// Calculates how many walkers to spawn, scaling the share of spawn points with the number of available troops.
        /// </summary>
        /// <param name="points">The usable npc_common spawn points in the village.</param>
        /// <param name="troopCount">The number of eligible troops in the village.</param>
        /// <returns>The number of walkers to spawn, never more than the troop count.</returns>
        private static int GetWalkerCount(int points, int troopCount)
        {
            float strength = Math.Min(troopCount, FullStrengthTroopCount) / (float)FullStrengthTroopCount;
            float share = MinWalkerShare + (MaxWalkerShare - MinWalkerShare) * strength;
            int count = (int)(points * share);

            return Math.Min(count, troopCount);
        }

        /// <summary>
        /// Shows a diagnostic message in the game UI when the debug messages option is enabled.
        /// </summary>
        /// <param name="text">The message to display.</param>
        private static void Log(string text)
        {
            if (!NobleBodyguardsSettings.Instance.DebugMessages)
            {
                return;
            }

            InformationManager.DisplayMessage(new InformationMessage("[NobleBodyguards] " + text));
        }

        /// <summary>
        /// Collects healthy, non-hero troops from the player's party and from lord parties in the settlement.
        /// </summary>
        /// <param name="settlement">The village being populated.</param>
        /// <returns>One entry per available troop, with its owning clan.</returns>
        private static List<WalkerTroop> GetArmyTroopPool(Settlement settlement)
        {
            List<WalkerTroop> pool = new List<WalkerTroop>();

            foreach (MobileParty party in settlement.Parties)
            {
                bool isPlayerParty = party == MobileParty.MainParty;
                bool isLordParty = party.LeaderHero != null && party.LeaderHero.IsLord;

                if (!isPlayerParty && !isLordParty)
                {
                    continue;
                }

                foreach (TroopRosterElement element in party.MemberRoster.GetTroopRoster())
                {
                    if (element.Character.IsHero)
                    {
                        continue;
                    }

                    int available = Math.Max(0, element.Number - element.WoundedNumber);
                    for (int i = 0; i < available; i++)
                    {
                        pool.Add(new WalkerTroop(element.Character, party.ActualClan));
                    }
                }
            }

            return pool;
        }

        /// <summary>
        /// Creates a walker agent for an army troop, using the owning clan's banner and colors.
        /// </summary>
        /// <param name="walker">The troop and its owning clan.</param>
        /// <param name="relation">The relation of the walker to the player.</param>
        /// <returns>The location character to add to the village center.</returns>
        private static LocationCharacter CreateWalker(WalkerTroop walker, CharacterRelations relation)
        {
            CharacterObject character = walker.Character;
            Clan owner = walker.Owner;

            Tuple<string, Monster> actionSetAndMonster = character.IsFemale
                ? CommonTownsfolkCampaignBehavior.GetRandomTownsWomanActionSetAndMonster(character.Race)
                : CommonTownsfolkCampaignBehavior.GetRandomTownsManActionSetAndMonster(character.Race);

            Campaign.Current.Models.AgeModel.GetAgeLimitForLocation(character, out int minAge, out int maxAge, "");

            AgentData agentData = new AgentData((IAgentOriginBase)new SimpleAgentOrigin(character, -1, owner?.Banner, new UniqueTroopDescriptor()))
                .Monster(actionSetAndMonster.Item2)
                .Equipment(character.FirstBattleEquipment)
                .NoHorses(true)
                .ClothingColor1(owner != null ? owner.Color : 0xFFFFFFFF)
                .ClothingColor2(owner != null ? owner.Color2 : 0xFFFFFFFF)
                .Age(MBRandom.RandomInt(minAge, maxAge));

            IAgentBehaviorManager agentBehaviorManager = SandBoxManager.Instance.AgentBehaviorManager;
            return new LocationCharacter(
                agentData,
                (AddBehaviorsDelegate)agentBehaviorManager.AddOutdoorWandererBehaviors,
                "npc_common",
                false,
                relation,
                actionSetAndMonster.Item1,
                false,
                false,
                (ItemObject)null,
                false,
                false,
                true,
                (AfterAgentCreatedDelegate)null,
                false);
        }

        /// <summary>
        /// A troop available to walk in the village, along with the clan that owns it.
        /// </summary>
        private sealed class WalkerTroop
        {
            public WalkerTroop(CharacterObject character, Clan owner)
            {
                Character = character;
                Owner = owner;
            }

            public CharacterObject Character { get; }

            public Clan Owner { get; }
        }
    }
}