using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace NobleBodyguards
{
    public class NobleBodyguardsSubModule : MBSubModuleBase
    {
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);

            // if this is a campaign game, we want to add our behavior to the game starter
            if (game.GameType is Campaign)
            {
                // get the campaign starter ( cast the generic IGameStarter to a CampaignGameStarter )
                CampaignGameStarter campaignGameStarter = (CampaignGameStarter)gameStarterObject;

                // add our behavior to the campaign starter
                campaignGameStarter.AddBehavior(new BodyguardSelectionBehavior());
            }
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);

            Settlement settlement = Settlement.CurrentSettlement;
            string sceneName = mission.SceneName ?? string.Empty;
            bool isExcludedScene =
                sceneName.IndexOf("arena", StringComparison.OrdinalIgnoreCase) >= 0
                || sceneName.IndexOf("keep", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!mission.IsSiegeBattle
                && mission.Mode != MissionMode.Stealth
                && !isExcludedScene
                && settlement != null
                && (settlement.IsTown || settlement.IsVillage))
                        {
                            mission.AddMissionBehavior(new BodyguardFollowMissionBehavior());
                        }
        }
    }
}
