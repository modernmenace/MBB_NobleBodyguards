using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Spawns escorts for the player, companions, and lords in settlement missions, and engages nearby enemies.
    /// </summary>
    public class BodyguardFollowMissionBehavior : MissionBehavior
    {
        /// <summary>Distance behind the leader at which escorts are positioned.</summary>
        private const float BehindDistance = 2f;

        /// <summary>Horizontal spacing used to position escorts on either side of the leader.</summary>
        private const float SideDistance = 1f;

        /// <summary>Seconds between scans for new or departed leaders.</summary>
        private const float LeaderScanInterval = 1f;

        private readonly List<EscortGroup> _groups = new List<EscortGroup>();
        private readonly Dictionary<Agent, Agent.AIStateFlag> _savedAlarmFlags =
            new Dictionary<Agent, Agent.AIStateFlag>();
        private readonly Dictionary<MobileParty, Dictionary<CharacterObject, int>> _takenUnits =
            new Dictionary<MobileParty, Dictionary<CharacterObject, int>>();

        private bool _playerGroupSpawned;
        private float _leaderScanTimer;

        /// <summary>A leader and the escorts assigned to them.</summary>
        private sealed class EscortGroup
        {
            public Agent Leader;
            public PartyBase Party;
            public bool Mounted;
            public readonly List<Agent> Escorts = new List<Agent>();
        }

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
        /// Spawns the player's escorts once, adds escorts for eligible companions and lords,
        /// respawns escorts when their leader changes mount state, and updates their positions.
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

            // Spawn the player's selected guards once when the player is ready.
            if (!_playerGroupSpawned)
            {
                _playerGroupSpawned = true;
                bool playerMounted = IsMountedVillageEscort(player);
                BodyguardSelectionBehavior.SelectBodyguards(MobileParty.MainParty, playerMounted);
                SpawnGroup(player, PartyBase.MainParty, playerMounted, BodyguardSelectionBehavior.SelectedBodyguards);
            }

            _leaderScanTimer -= dt;
            if (_leaderScanTimer <= 0f)
            {
                _leaderScanTimer = LeaderScanInterval;
                RemoveInactiveGroups();

                // Companion and lord escorts are skipped when the player-only option is on.
                bool playerOnly = NobleBodyguardsSettings.Instance?.PlayerOnly ?? false;
                if (!playerOnly)
                {
                    AddNewLeaders(player);
                }
            }

            foreach (EscortGroup group in _groups)
            {
                if (group.Leader == null || !group.Leader.IsActive())
                {
                    continue;
                }

                // Swap each group's escorts to match its leader's current mount state.
                bool mountedWanted = IsMountedVillageEscort(group.Leader);
                if (mountedWanted != group.Mounted)
                {
                    RespawnEscorts(group, mountedWanted);
                }

                UpdateEscorts(group);
            }
        }

        /// <summary>
        /// Determines whether escorts should be mounted: the leader must be riding in a village.
        /// </summary>
        /// <param name="leader">The leader agent.</param>
        /// <returns><see langword="true"/> if the leader is mounted in a village; otherwise <see langword="false"/>.</returns>
        private static bool IsMountedVillageEscort(Agent leader)
        {
            return leader.MountAgent != null
                && Settlement.CurrentSettlement != null
                && Settlement.CurrentSettlement.IsVillage;
        }

        /// <summary>
        /// Gets the party a mission agent belongs to, if it has one.
        /// </summary>
        /// <param name="agent">The agent to inspect.</param>
        /// <returns>The agent's party, or <see langword="null"/> if it has none.</returns>
        private static PartyBase GetLeaderParty(Agent agent)
        {
            return (agent.Origin as PartyAgentOrigin)?.Party;
        }

        /// <summary>
        /// Determines whether an agent should receive escorts: player companions who are not in the player's party,
        /// or lords whose party is part of an army. Notables and the player are excluded.
        /// </summary>
        /// <param name="agent">The agent to test.</param>
        /// <returns><see langword="true"/> if the agent is an eligible leader; otherwise <see langword="false"/>.</returns>
        private static bool IsEligibleLeader(Agent agent)
        {
            Hero hero = (agent.Character as CharacterObject)?.HeroObject;
            PartyBase party = GetLeaderParty(agent);

            if (hero == null || hero == Hero.MainHero || party == null || party.MobileParty == null)
            {
                return false;
            }

            if (hero.IsPlayerCompanion)
            {
                // Companions in the player's party already follow the player, so they are not escorted.
                return party.MobileParty != MobileParty.MainParty;
            }

            return party.MobileParty.Army != null;
        }

        /// <summary>
        /// Gets the set of troops already assigned from a party, creating it if needed.
        /// </summary>
        /// <param name="party">The party whose assignments are requested.</param>
        /// <returns>The assigned counts, keyed by character.</returns>
        private Dictionary<CharacterObject, int> GetTakenUnits(PartyBase party)
        {
            MobileParty mobileParty = party.MobileParty;

            if (!_takenUnits.TryGetValue(mobileParty, out Dictionary<CharacterObject, int> taken))
            {
                taken = new Dictionary<CharacterObject, int>();
                _takenUnits.Add(mobileParty, taken);
            }

            return taken;
        }

        /// <summary>
        /// Scans the mission for eligible leaders that do not yet have an escort group and spawns one for each.
        /// </summary>
        /// <param name="player">The player agent, which is never treated as a leader here.</param>
        private void AddNewLeaders(Agent player)
        {
            if (Mission.Current == null)
            {
                return;
            }

            // Collect candidates first so spawning does not change the collection being read.
            List<Agent> candidates = new List<Agent>();
            foreach (Agent agent in Mission.Current.Agents)
            {
                if (agent != player
                    && agent.IsActive()
                    && IsEligibleLeader(agent)
                    && !_groups.Exists(group => group.Leader == agent))
                {
                    candidates.Add(agent);
                }
            }

            foreach (Agent leader in candidates)
            {
                PartyBase party = GetLeaderParty(leader);
                bool mounted = IsMountedVillageEscort(leader);
                List<CharacterObject> picks = BodyguardSelectionBehavior.ChooseEscorts(
                    party.MobileParty,
                    mounted,
                    GetTakenUnits(party));

                if (picks.Count > 0)
                {
                    SpawnGroup(leader, party, mounted, picks);
                }
            }
        }

        /// <summary>
        /// Removes groups whose leader is no longer active and fades out their escorts.
        /// </summary>
        private void RemoveInactiveGroups()
        {
            for (int i = _groups.Count - 1; i >= 0; i--)
            {
                EscortGroup group = _groups[i];
                if (group.Leader != null && group.Leader.IsActive())
                {
                    continue;
                }

                foreach (Agent escort in group.Escorts)
                {
                    if (escort == null)
                    {
                        continue;
                    }

                    _savedAlarmFlags.Remove(escort);
                    if (escort.IsActive())
                    {
                        escort.FadeOut(false, true);
                    }
                }

                _groups.RemoveAt(i);
            }
        }

        /// <summary>
        /// Spawns escorts for a leader and registers the group when at least one escort spawns.
        /// </summary>
        /// <param name="leader">The agent the escorts follow.</param>
        /// <param name="party">The party the escorts are drawn from.</param>
        /// <param name="mounted">Whether the escorts should be mounted.</param>
        /// <param name="picks">The troops to spawn, in side order.</param>
        private void SpawnGroup(Agent leader, PartyBase party, bool mounted, List<CharacterObject> picks)
        {
            EscortGroup group = new EscortGroup
            {
                Leader = leader,
                Party = party,
                Mounted = mounted
            };

            Dictionary<CharacterObject, int> taken = GetTakenUnits(party);

            for (int index = 0; index < picks.Count; index++)
            {
                CharacterObject character = picks[index];
                Vec3 spawnPosition = GetFollowPosition(leader, index, mounted);
                Vec2 spawnDirection = leader.Frame.rotation.f.AsVec2;

                Agent agent = SpawnAgent(group, character, spawnPosition, spawnDirection);
                if (agent == null)
                {
                    continue;
                }

                group.Escorts.Add(agent);

                taken.TryGetValue(character, out int takenCount);
                taken[character] = takenCount + 1;
            }

            if (group.Escorts.Count > 0)
            {
                _groups.Add(group);
            }
        }

        /// <summary>
        /// Replaces every escort in a group with a new agent in the matching mount state. Each replacement keeps
        /// the old escort's position, facing, watch state, and combat state.
        /// </summary>
        /// <param name="group">The group to respawn.</param>
        /// <param name="mounted">Whether the replacement escorts should be mounted.</param>
        private void RespawnEscorts(EscortGroup group, bool mounted)
        {
            group.Mounted = mounted;

            for (int i = 0; i < group.Escorts.Count; i++)
            {
                Agent oldAgent = group.Escorts[i];
                if (oldAgent == null || !oldAgent.IsActive())
                {
                    continue;
                }

                EscortSnapshot snapshot = CaptureEscort(oldAgent);
                if (snapshot.Character == null)
                {
                    continue;
                }

                Agent newAgent = SpawnAgent(group, snapshot.Character, snapshot.Position, snapshot.Direction);
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
                group.Escorts[i] = newAgent;
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
        /// Creates a mission agent for a group at the given position and facing, matching the group's mount state.
        /// </summary>
        /// <param name="group">The group the agent belongs to.</param>
        /// <param name="character">The character to spawn.</param>
        /// <param name="position">The world-space spawn position.</param>
        /// <param name="direction">The horizontal facing direction.</param>
        /// <returns>The spawned agent, or <see langword="null"/> if the leader or the spawn is unavailable.</returns>
        private Agent SpawnAgent(EscortGroup group, CharacterObject character, Vec3 position, Vec2 direction)
        {
            Agent leader = group.Leader;
            if (leader == null || leader.Team == null)
            {
                return null;
            }

            // Use the leader's party origin; mounted escorts use their equipped horse.
            AgentBuildData buildData = new AgentBuildData(character)
                .InitialPosition(position)
                .InitialDirection(direction)
                .Team(leader.Team)
                .Banner(group.Party.Banner)
                .ClothingColor1(leader.Team.Color)
                .ClothingColor2(leader.Team.Color2)
                .TroopOrigin(new PartyAgentOrigin(group.Party, character))
                .IsFemale(character.IsFemale)
                .NoHorses(!group.Mounted);

            Agent agent = Mission.Current.SpawnAgent(buildData);

            if (agent != null)
            {
                agent.Controller = AgentControllerType.AI;
                agent.IsPaused = false;
                agent.SetAutomaticTargetSelection(true);
            }

            return agent;
        }

        /// <summary>Updates every escort in a group: fights nearby enemies, otherwise holds its slot.</summary>
        /// <param name="group">The group to update.</param>
        private void UpdateEscorts(EscortGroup group)
        {
            Agent leader = group.Leader;

            for (int i = 0; i < group.Escorts.Count; i++)
            {
                Agent escort = group.Escorts[i];
                if (escort == null || !escort.IsActive())
                {
                    continue;
                }

                Agent immediateEnemy = escort.ImmediateEnemy;
                bool hasActiveHostile =
                    immediateEnemy != null
                    && immediateEnemy.IsActive()
                    && immediateEnemy.Team != null
                    && escort.Team != null
                    && escort.Team.IsEnemyOf(immediateEnemy.Team);

                if (hasActiveHostile)
                {
                    SetCombatWatchState(escort);

                    if (escort.GetTargetAgent() != immediateEnemy)
                    {
                        escort.SetTargetAgent(immediateEnemy);
                    }

                    continue;
                }

                RestoreWatchState(escort);
                MoveAgentNearLeader(escort, leader, i, group.Mounted);
            }
        }

        /// <summary>Walks an escort to its slot, then matches the leader's facing once it arrives.</summary>
        /// <param name="escort">The agent to move.</param>
        /// <param name="leader">The leader agent used as the follow reference.</param>
        /// <param name="index">The escort's position index in the group.</param>
        /// <param name="mounted">Whether the group is mounted, which changes the spacing.</param>
        private void MoveAgentNearLeader(Agent escort, Agent leader, int index, bool mounted)
        {
            Vec3 targetPosition = GetFollowPosition(leader, index, mounted);
            WorldPosition worldPosition = new WorldPosition(
                Mission.Current.Scene,
                UIntPtr.Zero,
                targetPosition,
                true);

            float offsetX = escort.Position.x - targetPosition.x;
            float offsetY = escort.Position.y - targetPosition.y;

            // While walking, let the escort face its path instead of snapping sideways.
            if (offsetX * offsetX + offsetY * offsetY > 0.25f)
            {
                escort.SetScriptedPosition(ref worldPosition, false);
                return;
            }

            // Once in position, hold the leader's heading so the escort stands behind them.
            float direction = (float)Math.Atan2(
                -leader.Frame.rotation.f.x,
                leader.Frame.rotation.f.y);

            escort.SetScriptedPositionAndDirection(ref worldPosition, direction, false);
        }

        /// <summary>Calculates a position behind and to one side of a leader.</summary>
        /// <param name="leader">The leader agent used as the position reference.</param>
        /// <param name="index">The escort's position index, which determines its side.</param>
        /// <param name="mounted">Whether the group is mounted, which changes the spacing.</param>
        /// <returns>The world-space position where the escort should stand.</returns>
        private static Vec3 GetFollowPosition(Agent leader, int index, bool mounted)
        {
            float sideOffset = index == 0 ? -SideDistance : SideDistance;

            // Mounted escorts sit farther back to leave room for their horses.
            float behindDistance = mounted ? (BehindDistance + 1.0f) : BehindDistance;

            return leader.Position
                - leader.Frame.rotation.f * behindDistance
                + leader.Frame.rotation.s * sideOffset;
        }

        /// <summary>Saves the escort's alarm state and switches it to alarmed during combat.</summary>
        /// <param name="escort">The escort entering combat.</param>
        private void SetCombatWatchState(Agent escort)
        {
            if (_savedAlarmFlags.ContainsKey(escort))
            {
                return;
            }

            _savedAlarmFlags.Add(
                escort,
                escort.AIStateFlags & Agent.AIStateFlag.AlarmStateMask);

            escort.AIStateFlags |= Agent.AIStateFlag.Alarmed;
            escort.SetWatchState(Agent.WatchState.Alarmed);
        }

        /// <summary>Restores the escort's saved alarm state after combat ends.</summary>
        /// <param name="escort">The escort leaving combat.</param>
        private void RestoreWatchState(Agent escort)
        {
            if (!_savedAlarmFlags.TryGetValue(escort, out Agent.AIStateFlag savedAlarmFlags))
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

            escort.SetTargetAgent(null);
            escort.SetWatchState(previousWatchState);
            escort.AIStateFlags =
                (escort.AIStateFlags & ~Agent.AIStateFlag.AlarmStateMask)
                | previousAlarmState;

            _savedAlarmFlags.Remove(escort);
        }
    }
}