# Game Rules & Strategy Knowledge

Each record starts with a `## <id>` heading and is one retrievable chunk. IDs are unique.
All values are original to this project (not taken from any commercial game).

## rule_win_condition_v1
- type: rule
- tags: win, lose, citadel
- text: A player loses immediately when their Citadel is destroyed (Citadel health 2000). The opponent wins. Losing all other buildings and units does not end the match while the Citadel stands. Only Rams deal efficient damage to buildings, so a push on the Citadel needs Age II.

## rule_population_v1
- type: rule
- tags: population, house, cap
- text: Each unit uses population (Worker, Spearman, Archer = 1; Raider, Guardian = 2; Ram = 3). The Citadel provides 10 population and each House (wood 30) adds 5. The hard cap is 40 in Age I and 60 in Age II. Training is rejected if the new unit would exceed the current cap.

## rule_building_prerequisites_v1
- type: rule
- tags: buildings, training, prerequisites
- text: Units are trained only at their building: Worker at Citadel; Spearman, Guardian, Ram at Barracks; Archer at Archery Yard; Raider at Stable. Barracks costs wood 150, Archery Yard wood 175, Stable wood 175 and gold 50. Buildings must be placed by a Worker in explored territory the player owns or can see.

## rule_resources_v1
- type: rule
- tags: resources, wood, food, gold, economy
- text: Three resources exist: Wood (buildings, Archers, Rams), Food (Workers, infantry, Raiders), Gold (advanced units and technologies). Workers must carry gathered resources back to the Citadel to deposit them. Gold is scarce; Guardians, Raiders and Age research all compete for it, so avoid over-spending gold on a single unit type.

## rule_counter_triangle_v1
- type: rule
- tags: combat, counters, spearman, archer, raider, guardian
- text: Spearman beats Raider (bonus damage). Raider beats Archer and Worker. Archer beats Spearman and Worker from range. Guardian beats Spearman and Archer (high armor) but loses to Raider kiting and flanking, and is slow. Ram is nearly immune to arrows but dies to any melee unit. Mixed armies with a melee screen and Archers behind perform best.

## rule_defend_cavalry_v1
- type: rule
- tags: strategy, defense, raider, cavalry
- text: When visible enemies are mostly Raiders, train Spearmen at the Barracks (cost food 50, wood 25 each) and move the group to the threatened gate. Keep Archers behind the Spearmen. Do not spend gold on Age research until the threat is handled if gold is below 150 after training.

## rule_fog_of_war_v1
- type: rule
- tags: fog, visibility, security, ai
- text: A player only knows enemies currently inside vision range of their own units or buildings. Hidden enemy units, unexplored areas and the opponent's resources are never part of game state exposed to the AI. If no enemies are visible, the AI must say it has no information instead of guessing.

## rule_age_progression_v1
- type: rule
- tags: strategy, age, tech, timing
- text: Advance to Iron Age (food 300, gold 150) once the economy has at least 10 Workers and a Barracks plus one other military building. Age II unlocks Guardian and Ram and raises the population cap. Advancing too early with a weak army leaves the player exposed to Raider harassment.
