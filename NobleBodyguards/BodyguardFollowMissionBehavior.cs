using System;
using System.Collections.Generic;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace NobleBodyguards
{
    /// <summary>
    /// Spawns the selected bodyguards in a settlement mission and keeps them near the player.
    /// </summary>
    public class BodyguardFollowMissionBehavior : MissionBehavior
    {
        /// <summary>Distance behind the player at which bodyguards are positioned.</summary>
        private const float BehindDistance = 2f;
        /// <summary>Horizontal spacing used to position bodyguards on either side of the player.</summary>
        private const float SideDistance = 1f;

        private readonly List<Agent> _bodyguardAgents = new List<Agent>();

        private bool _bodyguardsSpawnAttempted;

        /// <summary>Gets the mission behavior category for this behavior.</summary>
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        /// <summary>
        /// Spawns the selected bodyguards once the player is active and updates their follow positions.
        /// </summary>
        /// <param name="dt">Elapsed time since the previous mission tick.</param>
        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            Agent player = Agent.Main;
            if (player == null || !player.IsActive())
            {
                return;
            }

            // Spawn once, then continually refresh each active agent's follow position.
            if (!_bodyguardsSpawnAttempted)
            {
                SpawnBodyguards();
                _bodyguardsSpawnAttempted = true;
            }

            for (int i = 0; i < _bodyguardAgents.Count; i++)
            {
                Agent bodyguard = _bodyguardAgents[i];
                if (bodyguard == null || !bodyguard.IsActive())
                {
                    continue;
                }

                MoveAgentNearPlayer(bodyguard, player, i);
            }
        }

        /// <summary>
        /// Refreshes the bodyguard selection from the main party after the mission starts.
        /// </summary>
        public override void AfterStart()
        {
            base.AfterStart();

            if (MobileParty.MainParty != null)
            {
                BodyguardSelectionBehavior.SelectBodyguards(MobileParty.MainParty);
            }
        }

        /// <summary>Attempts to spawn each selected bodyguard near the player.</summary>
        private void SpawnBodyguards()
        {
            int index = 0;

            // Preserve selection order so each guard keeps a consistent side of the player.
            foreach (CharacterObject character in BodyguardSelectionBehavior.SelectedBodyguards)
            {
                Agent agent = SpawnAgentNearPlayer(character, index);
                if (agent != null)
                {
                    _bodyguardAgents.Add(agent);
                    Thread.Sleep(100); // Without this, colors dont get assigned properly to the second for some reason
                }

                index++;
            }
        }

        /// <summary>Creates a mission agent at the follow position corresponding to its index.</summary>
        /// <param name="character">The character to spawn.</param>
        /// <param name="index">The body's position index in the formation.</param>
        /// <returns>The spawned agent, or <see langword="null"/> if there is no player agent.</returns>
        private Agent SpawnAgentNearPlayer(CharacterObject character, int index)
        {
            Agent player = Agent.Main;
            if (player == null)
            {
                return null;
            }

            Vec3 spawnPosition = GetFollowPosition(player, index);

            // Use the troop's campaign origin while spawning it without a mount.
            AgentBuildData buildData = new AgentBuildData(character)
                .InitialPosition(spawnPosition)
                .InitialDirection(player.Frame.rotation.f.AsVec2)
                .Team(player.Team)
                .Banner(PartyBase.MainParty.Banner)
                .ClothingColor1(player.Team.Color)
                .ClothingColor2(player.Team.Color2)
                .TroopOrigin(new PartyAgentOrigin(PartyBase.MainParty, character))
                .IsFemale(character.IsFemale)
                .NoHorses(true);

            return Mission.Current.SpawnAgent(buildData);
        }

        /// <summary>Updates a bodyguard's scripted position and facing relative to the player.</summary>
        /// <param name="bodyguard">The agent to move.</param>
        /// <param name="player">The player agent used as the follow reference.</param>
        /// <param name="index">The body's position index in the formation.</param>
        private void MoveAgentNearPlayer(Agent bodyguard, Agent player, int index)
        {
            Vec3 targetPosition = GetFollowPosition(player, index);
            WorldPosition worldPosition = new WorldPosition(
                Mission.Current.Scene,
                UIntPtr.Zero,
                targetPosition,
                true);

            float direction = (float)System.Math.Atan2(
                player.Frame.rotation.f.y,
                player.Frame.rotation.f.x);

            float offsetX = bodyguard.Position.x - targetPosition.x;
            float offsetY = bodyguard.Position.y - targetPosition.y;

            if (offsetX * offsetX + offsetY * offsetY > 2.25f)
            {
                bodyguard.SetScriptedPosition(ref worldPosition, false);
                return;
            }

            bodyguard.SetScriptedPositionAndDirection(ref worldPosition, direction, false);
        }

        /// <summary>Calculates a position behind and to one side of the player.</summary>
        /// <param name="player">The player agent used as the position reference.</param>
        /// <param name="index">The body's position index, which determines its side.</param>
        /// <returns>The world-space position where the bodyguard should stand.</returns>
        private Vec3 GetFollowPosition(Agent player, int index)
        {
            float sideOffset = index == 0 ? -SideDistance : SideDistance;

            return player.Position
                - player.Frame.rotation.f * BehindDistance
                + player.Frame.rotation.s * sideOffset;
        }
    }
}