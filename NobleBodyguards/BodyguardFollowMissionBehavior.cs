using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace NobleBodyguards
{
    /// <summary>
    /// Spawns selected party members in settlement missions, escorts the player, and engages nearby enemies.
    /// </summary>
    public class BodyguardFollowMissionBehavior : MissionBehavior
    {
        /// <summary>Distance behind the player at which bodyguards are positioned.</summary>
        private const float BehindDistance = 2f;

        /// <summary>Horizontal spacing used to position bodyguards on either side of the player.</summary>
        private const float SideDistance = 1f;

        private readonly List<Agent> _bodyguardAgents = new List<Agent>();
        private readonly Dictionary<Agent, Agent.AIStateFlag> _savedAlarmFlags =
            new Dictionary<Agent, Agent.AIStateFlag>();

        private bool _bodyguardsSpawnAttempted;
        private bool _mountedEscort;

        /// <summary>Snapshot of an escort's state, used to rebuild it after a mount change.</summary>
        private struct EscortSnapshot
        {
            public CharacterObject Character;
            public Vec3 Position;
            public Vec2 Direction;
            public Agent.WatchState WatchState;
        }

        /// <summary>Gets the mission behavior category for this behavior.</summary>
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        /// <summary>
        /// Spawns the selected bodyguards once the player is active, respawns them when the player
        /// mounts or dismounts, and updates their follow positions.
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

            // Spawn the selected guards once when the player is ready.
            if (!_bodyguardsSpawnAttempted)
            {
                _mountedEscort = IsMountedVillageEscort(player);
                BodyguardSelectionBehavior.SelectBodyguards(MobileParty.MainParty, _mountedEscort);
                SpawnBodyguards();
                _bodyguardsSpawnAttempted = true;
            }
            else
            {
                // Swap escorts to match the player's current mount state.
                bool mountedEscortWanted = IsMountedVillageEscort(player);
                if (mountedEscortWanted != _mountedEscort)
                {
                    RespawnEscorts(mountedEscortWanted);
                }
            }

            // Prioritize combat; otherwise keep the guards in escort positions.
            for (int i = 0; i < _bodyguardAgents.Count; i++)
            {
                Agent bodyguard = _bodyguardAgents[i];
                if (bodyguard == null || !bodyguard.IsActive())
                {
                    continue;
                }

                Agent immediateEnemy = bodyguard.ImmediateEnemy;
                bool hasActiveHostile =
                    immediateEnemy != null
                    && immediateEnemy.IsActive()
                    && immediateEnemy.Team != null
                    && bodyguard.Team != null
                    && bodyguard.Team.IsEnemyOf(immediateEnemy.Team);

                if (hasActiveHostile)
                {
                    SetCombatWatchState(bodyguard);

                    if (bodyguard.GetTargetAgent() != immediateEnemy)
                    {
                        bodyguard.SetTargetAgent(immediateEnemy);
                    }

                    continue;
                }

                RestoreWatchState(bodyguard);
                MoveAgentNearPlayer(bodyguard, player, i);
            }
        }

        /// <summary>
        /// Determines whether escorts should be mounted: the player must be riding in a village.
        /// </summary>
        /// <param name="player">The player agent.</param>
        /// <returns><see langword="true"/> if the player is mounted in a village; otherwise <see langword="false"/>.</returns>
        private static bool IsMountedVillageEscort(Agent player)
        {
            return player.MountAgent != null
                && Settlement.CurrentSettlement != null
                && Settlement.CurrentSettlement.IsVillage;
        }

        /// <summary>Attempts to spawn each selected bodyguard near the player.</summary>
        private void SpawnBodyguards()
        {
            Agent player = Agent.Main;
            if (player == null)
            {
                return;
            }

            int index = 0;

            // Preserve selection order so each guard keeps a consistent side of the player.
            foreach (CharacterObject character in BodyguardSelectionBehavior.SelectedBodyguards)
            {
                Vec3 spawnPosition = GetFollowPosition(player, index);
                Vec2 spawnDirection = player.Frame.rotation.f.AsVec2;

                Agent agent = SpawnAgent(character, spawnPosition, spawnDirection);
                if (agent != null)
                {
                    _bodyguardAgents.Add(agent);
                }

                index++;
            }
        }

        /// <summary>
        /// Replaces every escort with a new agent in the matching mount state. Each replacement keeps
        /// the old escort's position, facing, watch state, and combat state.
        /// </summary>
        /// <param name="mounted">Whether the replacement escorts should be mounted.</param>
        private void RespawnEscorts(bool mounted)
        {
            _mountedEscort = mounted;

            for (int i = 0; i < _bodyguardAgents.Count; i++)
            {
                Agent oldAgent = _bodyguardAgents[i];
                if (oldAgent == null || !oldAgent.IsActive())
                {
                    continue;
                }

                EscortSnapshot snapshot = CaptureEscort(oldAgent);
                if (snapshot.Character == null)
                {
                    continue;
                }

                Agent newAgent = SpawnAgent(snapshot.Character, snapshot.Position, snapshot.Direction);
                if (newAgent == null)
                {
                    continue;
                }

                newAgent.SetWatchState(snapshot.WatchState);

                // Carry combat state over to the replacement so it keeps fighting.
                if (_savedAlarmFlags.TryGetValue(oldAgent, out Agent.AIStateFlag savedAlarmFlags))
                {
                    _savedAlarmFlags.Remove(oldAgent);
                    _savedAlarmFlags.Add(newAgent, savedAlarmFlags);
                    newAgent.AIStateFlags |= Agent.AIStateFlag.Alarmed;

                    Agent target = oldAgent.GetTargetAgent();
                    if (target != null)
                    {
                        newAgent.SetTargetAgent(target);
                    }
                }

                oldAgent.FadeOut(false, true);
                _bodyguardAgents[i] = newAgent;
            }
        }

        /// <summary>Captures an escort's position, facing, character, and watch state.</summary>
        /// <param name="escort">The escort to capture.</param>
        /// <returns>A snapshot of the escort's current state.</returns>
        private static EscortSnapshot CaptureEscort(Agent escort)
        {
            return new EscortSnapshot
            {
                Character = escort.Character as CharacterObject,
                Position = escort.Position,
                Direction = escort.Frame.rotation.f.AsVec2,
                WatchState = escort.CurrentWatchState
            };
        }

        /// <summary>
        /// Creates a mission agent at the given position and facing, matching the current mount state.
        /// </summary>
        /// <param name="character">The character to spawn.</param>
        /// <param name="position">The world-space spawn position.</param>
        /// <param name="direction">The horizontal facing direction.</param>
        /// <returns>The spawned agent, or <see langword="null"/> if there is no player agent or the spawn failed.</returns>
        private Agent SpawnAgent(CharacterObject character, Vec3 position, Vec2 direction)
        {
            Agent player = Agent.Main;
            if (player == null)
            {
                return null;
            }

            // Use the troop's campaign origin; mounted escorts use their equipped horse.
            AgentBuildData buildData = new AgentBuildData(character)
                .InitialPosition(position)
                .InitialDirection(direction)
                .Team(player.Team)
                .Banner(PartyBase.MainParty.Banner)
                .ClothingColor1(player.Team.Color)
                .ClothingColor2(player.Team.Color2)
                .TroopOrigin(new PartyAgentOrigin(PartyBase.MainParty, character))
                .IsFemale(character.IsFemale)
                .NoHorses(!_mountedEscort);

            Agent agent = Mission.Current.SpawnAgent(buildData);

            if (agent != null)
            {
                agent.Controller = AgentControllerType.AI;
                agent.IsPaused = false;
                agent.SetAutomaticTargetSelection(true);
            }

            return agent;
        }

        /// <summary>Walks a bodyguard to its slot, then matches the player's facing once it arrives.</summary>
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

            float offsetX = bodyguard.Position.x - targetPosition.x;
            float offsetY = bodyguard.Position.y - targetPosition.y;

            // While walking, let the guard face its path instead of snapping sideways.
            if (offsetX * offsetX + offsetY * offsetY > 0.25f)
            {
                bodyguard.SetScriptedPosition(ref worldPosition, false);
                return;
            }

            // Once in position, hold the player's heading so the guard stands behind them.
            float direction = (float)System.Math.Atan2(
                -player.Frame.rotation.f.x,
                player.Frame.rotation.f.y);

            bodyguard.SetScriptedPositionAndDirection(ref worldPosition, direction, false);
        }

        /// <summary>Calculates a position behind and to one side of the player.</summary>
        /// <param name="player">The player agent used as the position reference.</param>
        /// <param name="index">The body's position index, which determines its side.</param>
        /// <returns>The world-space position where the bodyguard should stand.</returns>
        private Vec3 GetFollowPosition(Agent player, int index)
        {
            float sideOffset = index == 0 ? -SideDistance : SideDistance;

            // Mounted escorts sit farther back to leave room for their horses.
            float behindDistance = _mountedEscort ? (BehindDistance + 1.0f) : BehindDistance;

            return player.Position
                - player.Frame.rotation.f * behindDistance
                + player.Frame.rotation.s * sideOffset;
        }

        /// <summary>Saves the bodyguard's alarm state and switches it to alarmed during combat.</summary>
        /// <param name="bodyguard">The bodyguard entering combat.</param>
        private void SetCombatWatchState(Agent bodyguard)
        {
            if (_savedAlarmFlags.ContainsKey(bodyguard))
            {
                return;
            }

            _savedAlarmFlags.Add(
                bodyguard,
                bodyguard.AIStateFlags & Agent.AIStateFlag.AlarmStateMask);

            bodyguard.AIStateFlags |= Agent.AIStateFlag.Alarmed;
            bodyguard.SetWatchState(Agent.WatchState.Alarmed);
        }

        /// <summary>Restores the bodyguard's saved alarm state after combat ends.</summary>
        /// <param name="bodyguard">The bodyguard leaving combat.</param>
        private void RestoreWatchState(Agent bodyguard)
        {
            if (!_savedAlarmFlags.TryGetValue(bodyguard, out Agent.AIStateFlag savedAlarmFlags))
            {
                return;
            }

            Agent.WatchState previousWatchState = Agent.WatchState.Patrolling;
            Agent.AIStateFlag previousAlarmState =
                savedAlarmFlags & Agent.AIStateFlag.AlarmStateMask;

            if (previousAlarmState == Agent.AIStateFlag.Alarmed)
            {
                previousWatchState = Agent.WatchState.Alarmed;
            }
            else if (previousAlarmState == Agent.AIStateFlag.Cautious)
            {
                previousWatchState = Agent.WatchState.Cautious;
            }

            bodyguard.SetTargetAgent(null);
            bodyguard.SetWatchState(previousWatchState);
            bodyguard.AIStateFlags =
                (bodyguard.AIStateFlags & ~Agent.AIStateFlag.AlarmStateMask)
                | previousAlarmState;

            _savedAlarmFlags.Remove(bodyguard);
        }
    }
}