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
    public class BodyguardSelectionBehavior : CampaignBehaviorBase
    {
        // exposed so the mission behavior can read the current selection
        public static List<CharacterObject> SelectedBodyguards { get; private set; } = new List<CharacterObject>();

        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // nothing to persist, the selection is recalculated every time we enter a settlement
        }

        public static void SelectBodyguards(MobileParty party)
        {
            if (party == null)
            {
                return;
            }

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

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party != MobileParty.MainParty)
            {
                return;
            }

            SelectBodyguards(party);

            if (SelectedBodyguards.Count > 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{string.Join(" and ", SelectedBodyguards.Select(c => c.Name))} will escort you through {settlement.Name}."));
            }
        }
    }
}