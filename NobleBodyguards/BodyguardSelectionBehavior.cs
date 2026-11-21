using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace NobleBodyguards
{
    /// <summary>
    /// Selects eligible party members to escort the player in settlement missions.
    /// </summary>
    public class BodyguardSelectionBehavior : CampaignBehaviorBase
    {
        /// <summary>
        /// Gets the currently selected bodyguards, ordered by troop tier and power.
        /// </summary>
        public static List<CharacterObject> SelectedBodyguards { get; private set; } = new List<CharacterObject>();

        /// <summary>
        /// Registers the campaign event used to refresh the selection upon settlement entry.
        /// </summary>
        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
        }

        /// <summary>
        /// Synchronizes behavior state with the campaign data store.
        /// </summary>
        /// <param name="dataStore">The campaign data store.</param>
        public override void SyncData(IDataStore dataStore)
        {
        }

        /// <summary>
        /// Selects up to two healthy, non-hero infantry members from the specified party.
        /// </summary>
        /// <param name="party">The party whose roster is used for selection. If <see langword="null"/>, the current selection is unchanged.</param>
        public static void SelectBodyguards(MobileParty party)
        {
            if (party == null)
            {
                return;
            }

            // Expand healthy, non-hero infantry stacks into individual candidates, then rank them.
            SelectedBodyguards = party.MemberRoster.GetTroopRoster()
                .Where(element => !element.Character.IsHero
                                   && element.Character.IsInfantry
                                   && element.Number > element.WoundedNumber)
                .SelectMany(element => Enumerable.Repeat(
                    element.Character,
                    element.Number - element.WoundedNumber))
                .OrderByDescending(character => character.Tier)
                .ThenByDescending(character => character.GetPower())
                .Take(2)
                .ToList();
        }

        /// <summary>
        /// Refreshes the main party's selection when it enters a settlement and notifies the player.
        /// </summary>
        /// <param name="party">The party that entered the settlement.</param>
        /// <param name="settlement">The settlement entered.</param>
        /// <param name="hero">The hero associated with the settlement-entry event.</param>
        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            // Only the player's party drives the selection and its settlement-entry notification.
            if (party != MobileParty.MainParty)
            {
                return;
            }

            SelectBodyguards(party);

            // Keep the notification quiet when the party has no eligible infantry.
            if (SelectedBodyguards.Count > 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{string.Join(" and ", SelectedBodyguards.Select(c => c.Name))} will escort you through {settlement.Name}."));
            }
        }
    }
}