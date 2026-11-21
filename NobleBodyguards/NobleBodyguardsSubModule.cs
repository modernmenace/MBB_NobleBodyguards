using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace NobleBodyguards
{
    /// <summary>
    /// Initializes the Noble Bodyguards campaign behavior and eligible mission behavior.
    /// </summary>
    public class NobleBodyguardsSubModule : MBSubModuleBase
    {
        /// <summary>
        /// Adds bodyguard selection behavior when a campaign game starts.
        /// </summary>
        /// <param name="game">The game being started.</param>
        /// <param name="gameStarterObject">The game starter used to register campaign behaviors.</param>
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

        /// <summary>
        /// Adds bodyguard following to eligible town and village missions.
        /// </summary>
        /// <param name="mission">The mission being initialized.</param>
        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);

            Settlement settlement = Settlement.CurrentSettlement;
            string sceneName = mission.SceneName ?? string.Empty;
            // Arena and keep scenes are excluded from the settlement escort behavior.
            bool isExcludedScene =
                sceneName.IndexOf("arena", StringComparison.OrdinalIgnoreCase) >= 0
                || sceneName.IndexOf("keep", StringComparison.OrdinalIgnoreCase) >= 0;

            // Attach escorts only to non-siege, non-stealth missions in towns or villages.
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
