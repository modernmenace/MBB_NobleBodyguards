using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace NobleBodyguards
{
    public class BodyguardFollowMissionBehavior : MissionBehavior
    {
        private const float BehindDistance = 2f;
        private const float SideDistance = 1f;

        private readonly List<Agent> _bodyguardAgents = new List<Agent>();

        private bool _bodyguardsSpawnAttempted;

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            Agent player = Agent.Main;
            if (player == null || !player.IsActive())
            {
                return;
            }

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

        public override void AfterStart()
        {
            base.AfterStart();

            if (MobileParty.MainParty != null)
            {
                BodyguardSelectionBehavior.SelectBodyguards(MobileParty.MainParty);
            }
        }

        private void SpawnBodyguards()
        {
            int index = 0;

            foreach (CharacterObject character in BodyguardSelectionBehavior.SelectedBodyguards)
            {
                Agent agent = SpawnAgentNearPlayer(character, index);
                if (agent != null)
                {
                    _bodyguardAgents.Add(agent);
                }

                index++;
            }
        }

        private Agent SpawnAgentNearPlayer(CharacterObject character, int index)
        {
            Agent player = Agent.Main;
            if (player == null)
            {
                return null;
            }

            Vec3 spawnPosition = GetFollowPosition(player, index);

            AgentBuildData buildData = new AgentBuildData(character)
                .InitialPosition(spawnPosition)
                .InitialDirection(player.Frame.rotation.f.AsVec2)
                .TroopOrigin(new PartyAgentOrigin(PartyBase.MainParty, character))
                .IsFemale(character.IsFemale)
                .NoHorses(true);

            return Mission.Current.SpawnAgent(buildData);
        }

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

            bodyguard.SetScriptedPosition(ref worldPosition, false);
            bodyguard.SetScriptedPositionAndDirection(ref worldPosition, direction, false);
        }

        private Vec3 GetFollowPosition(Agent player, int index)
        {
            float sideOffset = index == 0 ? -SideDistance : SideDistance;

            return player.Position
                - player.Frame.rotation.f * BehindDistance
                + player.Frame.rotation.s * sideOffset;
        }
    }
}