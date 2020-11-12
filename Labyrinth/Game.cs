﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Labyrinth
{
    public class Game
    {
        private const int MAZE_SIZE = 200;
        private const float CHANCE_FOR_ENEMY_SPAWN = 0.5f;
        private const float CHANCE_FOR_ENEMY_MOVE = 0.7f;
        private const float CHANCE_FOR_MERCHANT = 0.1f;
        private const float CHANCE_TO_FLEE = 0.9f;

        public static bool MinotaurIsAlive { get; set; } = true;
        public static bool DragonIsAlive { get; set; } = true;

        private Maze Maze;
        private Player Player;
        private readonly int StartingDifficulty;
        private int Difficulty;
        private int MovesTaken;
        private BattleResult? BattleResult;
        private readonly Dictionary<char, string> DirectionActions = new Dictionary<char, string>
        {
            { 'N', Direction.North.ToString() },
            { 'E', Direction.East.ToString() },
            { 'S', Direction.South.ToString() },
            { 'W', Direction.West.ToString() }
        };
        private readonly Dictionary<char, string> ChestActions = new Dictionary<char, string>
        {
            { (char)ChestAction.Open, "Open the chest" },
            { (char)ChestAction.Leave, "Don't open the chest" },
            { (char)ChestAction.Examine, "Examine the chest" }
        };
        public readonly static Dictionary<char, string> BattleActions = new Dictionary<char, string>
        {
            { (char)BattleAction.Attack, "Normal attack" },
            { (char)BattleAction.Bow, "Bow attack" },
            { (char)BattleAction.Potion, "Use potion" },
            { (char)BattleAction.Flee, "Attempt to flee" }
        };
        public readonly static Dictionary<char, string> YesNoActions = new Dictionary<char, string>
        {
            { (char)YesNoAction.Yes, "Yes" },
            { (char)YesNoAction.No, "No" }
        };

        /// <summary>
        /// Constructs a <see cref="Game"/> object
        /// </summary>
        /// <param name="startingDifficulty">The starting difficulty of the game</param>
        public Game(int startingDifficulty)
        {
            StartingDifficulty = startingDifficulty;
            Reset();
        }

        /// <summary>
        /// Begins the game
        /// </summary>
        public void Start()
        {
            // Place the player at a random starting location
            Player.Location = Utils.GetRandomFromList<Location>(Maze.Network.SelectMany(l => l).ToList().Where(l => l != null));

            DisplayMessage("You enter the Labyrinth.");

            // Main game loop
            while (true)
            {
                Dictionary<char, string> possibleDirections = DirectionActions
                    .Where(a => Player.Location.Neighbors[(int)Enum.Parse(typeof(Direction), a.Value)] != null)
                    .ToDictionary(a => a.Key, a => a.Value);

                // Player movement
                DisplayPrompt("Which direction?", possibleDirections);
                char dirChar = GetInput(possibleDirections.Keys);
                Direction dir = (Direction)Enum.Parse(typeof(Direction), DirectionActions[dirChar]);
                Location newLocation = Player.Move(dir);

                #region Initialize player's new location
                if (Player.Location.Enemy == null && Utils.Roll(CHANCE_FOR_ENEMY_SPAWN))
                {
                    Player.Location.Enemy = Enemy.RandomEnemy(Difficulty);
                }
                else if (Utils.Roll(CHANCE_FOR_MERCHANT))
                {
                    Player.Location.Merchant = new Merchant();
                }
                #endregion

                #region Room is trapped
                if (Player.Location.IsTrapped)
                {
                    DisplayMessage("You feel a plate sink under your foot.");
                    DisplayMessage("Flames erupt from the floor!");
                    int trapDamage = Player.Location.Trap.Trigger();
                    Player.Damage(trapDamage);
                    DisplayMessage($"You take {trapDamage} points of damage.");
                }
                #endregion

                #region Found an enemy
                if (Player.Location.Enemy != null)
                {
                    BattleResult = Battle(Player, Player.Location.Enemy);

                    switch (BattleResult.Value)
                    {
                        case Labyrinth.BattleResult.Won:
                            DisplayMessage($"You defeat the {Player.Location.Enemy.EnemyType}!");
                            Player.GiveXP(Player.Location.Enemy.XP);

                            if (Player.Location.Enemy.EnemyType == EnemyType.Minotaur)
                                MinotaurIsAlive = false;

                            Player.Location.Enemy = null;
                            break;
                        case Labyrinth.BattleResult.Fled:
                            DisplayMessage("You ran away.");
                            break;
                        case Labyrinth.BattleResult.Lost:
                            GameOver();
                            return;
                    }
                }
                else
                {
                    BattleResult = null;
                }
                #endregion

                #region Found a merchant
                if (Player.Location.Merchant != null)
                {
                    DisplayMessage("A cloaked figure appears before you.");
                    DisplayMessage(Player.Location.Merchant.Dialogue.Greeting);

                    if (Player.JunkValue > 0)
                    {
                        Player.SellJunk();
                        DisplayMessage("You sell your unused items.");
                    }

                    DisplayMessage(Player.Location.Merchant.Dialogue.IntroQuestion);

                    bool doneShopping = false;
                    do
                    {
                        Dictionary<char, string> purchaseActions = Utils.CreateActions(Player.Location.Merchant.Items.ToList(), i => string.Format($"{$"{i.Name} ({i.Count})",-24}{i.Value}"));
                        foreach (MerchantAction action in Utils.GetEnumValues<MerchantAction>())
                            purchaseActions.Add((char)action, action.ToString());

                        DisplayPrompt("", purchaseActions, "\t") ;
                        DisplayMessage($"Gold: {Player.Items.CountOf(ItemType.Gold)}");

                        char inputChar = GetInput(purchaseActions.Keys);
                        int inputInt = int.Parse(inputChar.ToString());

                        if (inputInt > 0 && inputInt <= Player.Location.Merchant.Items.Count)
                        {
                            Item itemToBuy = Player.Location.Merchant.Items.ElementAt(inputInt - 1);
                            if (Player.Items.CountOf(ItemType.Gold) >= itemToBuy.Value)
                            {
                                Player.SpendGold(itemToBuy.Value);
                                Player.Items.Add(itemToBuy);
                                Player.Location.Merchant.Sell(itemToBuy);

                                DisplayMessage(Player.Location.Merchant.Dialogue.RepeatQuestion);
                            }
                            else
                            {
                                DisplayMessage("You don't have enough gold for that.");
                            }
                        }
                        else
                        {
                            switch ((MerchantAction)inputChar)
                            {
                                case MerchantAction.Nothing:
                                    doneShopping = true;
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }
                        }
                    }
                    while (!doneShopping);

                    DisplayMessage(Player.Location.Merchant.Dialogue.PartingMessage);
                }
                #endregion

                #region Found a chest
                if (Player.Location.Chest != null)
                {
                    DisplayPrompt("You find a chest.", ChestActions);

                    char action;

                    do
                    {
                        action = GetInput(ChestActions.Keys);

                        switch (action)
                        {
                            case (char)ChestAction.Examine:
                                if (Player.Location.Chest.StatusVisible)
                                {
                                    if (Player.Location.Chest.IsTrapped)
                                        DisplayMessage("The chest looks booby-trapped.");
                                    else
                                        DisplayMessage("It appears safe to open");
                                }
                                else
                                {
                                    DisplayMessage("You can't tell if the chest is safe to open.");
                                }
                                break;
                            case (char)ChestAction.Open:
                                if (Player.Location.Chest.IsTrapped)
                                {
                                    DisplayMessage("The chest was booby-trapped!");
                                    int trapDamage = Player.Location.Chest.Trap.Trigger();
                                    Player.Damage(trapDamage);
                                    DisplayMessage($"You take {trapDamage} points of damage.");
                                }

                                DisplayMessage("You open the chest.");
                                Player.GiveLoot(Player.Location.Chest.Items);
                                break;
                            default:
                                break;
                        }
                    }
                    while (action == (char)ChestAction.Examine);
                }
                #endregion

                #region Found an item
                if (Player.Location.Items.Any())
                {
                    DisplayMessage("There's something on the floor.");
                    Player.GiveLoot(Player.Location.Items);
                }
                #endregion

                MovesTaken++;

                UpdateDifficulty();
                
                // Move enemies
                if (!(BattleResult != null && BattleResult.Value == Labyrinth.BattleResult.Fled)) // If the player fled, don't move the enemy
                {                                                                                   // TODO: This should probably only be the case for the enemy that was battled, rather than all enemies
                    foreach (Enemy enemy in Maze.Network.SelectMany(r => r.Select(l => l?.Enemy)).Where(e => e != null))
                    {
                        if (Utils.Roll(CHANCE_FOR_ENEMY_MOVE))
                        {
                            enemy.Move(Utils.GetRandomFromList(enemy.Location.GetValidDirections()));
                        }
                    }
                }

                Player.Location.Merchant = null;
            }

        }

        #region Battle Methods
        /// <summary>
        /// Begins a battle between the player and an enemy
        /// </summary>
        /// <param name="player">The player</param>
        /// <param name="enemy">The enemy to battle</param>
        /// <returns>The result of the battle</returns>
        public BattleResult Battle(Player player, Enemy enemy)
        {
            while (enemy.CurrentHP > 0 && player.CurrentHP > 0)
            {
                Dictionary<char, string> actions = GetValidBattleActions();

                int uiOffset = DisplayBattleUI(player, enemy);
                uiOffset += DisplayPrompt("", actions);

                char action;
                bool validAction = true;
                int tempOffset = 0;
                do
                {
                    action = GetInput(actions.Keys);

                    if (!validAction)
                    {
                        //ClearLines(tempOffset);
                        uiOffset -= tempOffset;
                    }

                    if (action == (char)BattleAction.Bow && !player.Items.Contains(ItemType.Arrows))
                    {
                        tempOffset += DisplayMessage("You don't have any arrows!", false);
                        validAction = false;
                    }
                    else if (action == (char)BattleAction.Potion && player.CurrentHP == player.MaxHP)
                    {
                        tempOffset += DisplayMessage("You're already at full HP!", false);
                        validAction = false;
                    }

                    uiOffset += tempOffset;
                } while (!validAction);

                switch (action)
                {
                    case (char)BattleAction.Attack:
                        uiOffset += ProcessPlayerAttack(player, enemy, false);
                        break;
                    case (char)BattleAction.Bow:
                        uiOffset += ProcessPlayerAttack(player, enemy, true);
                        break;
                    case (char)BattleAction.Potion:
                        uiOffset += DisplayMessage("You drink a potion.");
                        int healthRestored = player.UsePotion();
                        uiOffset += DisplayMessage($"Restored {healthRestored} HP.");
                        break;
                    case (char)BattleAction.Flee:
                        if (Utils.Roll(CHANCE_TO_FLEE))
                        {
                            //ClearLines(uiOffset);
                            return Labyrinth.BattleResult.Fled;
                        }
                        else
                        {
                            uiOffset += DisplayMessage("You couldn't get away!");
                        }
                        break;
                    default:
                        throw new Exception("Invalid battle action.");
                }
                
                if (enemy.CurrentHP > 0)
                {
                    uiOffset += ProcessEnemyAttack(enemy, player);
                }

                //ClearLines(uiOffset);
            }

            if (player.CurrentHP <= 0)
            {
                return Labyrinth.BattleResult.Lost;
            }
            else
            {
                return Labyrinth.BattleResult.Won;
            }
        }

        private void Reset()
        {
            Maze = new Maze(MAZE_SIZE);
            Player = new Player();
            Difficulty = StartingDifficulty;
            MovesTaken = 0;

            Player.ItemGained += new EventHandler<ItemGainedEventArgs>(OnItemGained);
            Player.StatsIncreased += new EventHandler<StatsIncreasedEventArgs>(OnStatsIncreased);
        }

        /// <summary>
        /// Displays the battle interface
        /// </summary>
        /// <param name="player">The player</param>
        /// <param name="enemy">The enemy that the player is battling</param>
        /// <param name="displayPositonAdjustment"></param>
        /// <returns>The number of lines written to the console</returns>
        private int DisplayBattleUI(Player player, Enemy enemy, int displayPositonAdjustment = -1)
        {
            //if (displayPositonAdjustment >= 0)
            //    ClearLines(displayPositonAdjustment);

            int linesWritten = 0;

            Console.WriteLine($"You {enemy.EnemyType,26}");
            linesWritten++;
            Console.WriteLine($"HP: {player.CurrentHP,2}/{player.MaxHP,-13} HP: {enemy.CurrentHP,2}/{enemy.MaxHP,-2}");
            linesWritten++;

            return linesWritten;
        }

        /// <summary>
        /// Returns the battle actions available to the player based on the items they have
        /// </summary>
        /// <returns>The valid actions available to the player</returns>
        private Dictionary<char, string> GetValidBattleActions()
        {
            Dictionary<char, string> actions = new Dictionary<char, string>();

            foreach (var action in BattleActions)
            {
                switch (action.Key)
                {
                    case (char)BattleAction.Bow:
                        if (Player.Items.Contains(ItemType.Bow))
                            actions.Add(action.Key, action.Value);
                        break;
                    case (char)BattleAction.Potion:
                        if (Player.Items.Contains(ItemType.Potions))
                            actions.Add(action.Key, action.Value);
                        break;
                    default:
                        actions.Add(action.Key, action.Value);
                        break;
                }
            }

            return actions;
        }

        /// <summary>
        /// Processes the player's attack and displays the results
        /// </summary>
        /// <param name="player">The player</param>
        /// <param name="enemy">The enemy being attacked</param>
        /// <param name="attackWithBow">Whether the player is attacking with a bow</param>
        /// <returns>The number of lines written to the console</returns>
        protected int ProcessPlayerAttack(Player player, Enemy enemy, bool attackWithBow)
        {
            string weaponName = attackWithBow ? "Bow" : player.Items.Contains(ItemType.Weapon) ? player.Items[ItemType.Weapon].Name : "Fists";
            int offset = DisplayMessage($"You attack the {enemy.EnemyType} with your {weaponName}.");

            (AttackResult Result, int Damage) = attackWithBow ? player.AttackWithBow(enemy) : player.Attack(enemy);
            if (Result == AttackResult.Miss)
            {
                offset += DisplayMessage($"The {enemy.EnemyType} dodged your attack.");
            }
            else
            {
                if (Result == AttackResult.Crit)
                    offset += DisplayMessage("Critical hit!");

                offset += DisplayMessage($"Dealt {Damage} damage.");
            }

            return offset;
        }

        /// <summary>
        /// Processes the enemy's attack and displays the results
        /// </summary>
        /// <param name="enemy">The enemy attacking the player</param>
        /// <param name="player">The player</param>
        /// <returns>The number of lines writen to the console</returns>
        protected int ProcessEnemyAttack(Enemy enemy, Player player)
        {
            int offset = DisplayMessage($"The {enemy.EnemyType} attacks!");
            (AttackResult Result, int Damage) = enemy.Attack(player);
            
            if (Result == AttackResult.Miss)
            {
                offset += DisplayMessage($"You dodge its attack.");
            }
            else
            {
                if (Result == AttackResult.Crit)
                    offset += DisplayMessage("Critical hit!");

                offset += DisplayMessage($"You take {Damage} damage.");
            }

            return offset;
        }
        #endregion

        #region IO Methods
        /// <summary>
        /// Retrieves an input character based on a list of valid inputs
        /// </summary>
        /// <param name="validInputs">Uppercase characters reperesenting valid inputs</param>
        /// <returns>The uppercase character that the player entered</returns>
        public static char GetInput(params char[] validInputs)
        {
            char input;

            if (validInputs == null || validInputs.Length == 0)
            {
                input = Console.ReadKey().KeyChar;
            }
            else
            {
                do
                {
                    input = char.ToUpper(Console.ReadKey(true).KeyChar);
                }
                while (!validInputs.Contains(input));
            }

            return input;
        }

        public static char GetInput(IEnumerable<char> validInputs)
        {
            return GetInput(validInputs.ToArray());
        }

        /// <summary>
        /// Retrieves an input character based on a list of valid inputs
        /// </summary>
        /// <param name="validInputs">Case-insensitive strings reperesenting valid inputs</param>
        /// <returns>The uppercase character that the player entered</returns>
        public static string GetInput(params string[] validInputs)
        {
            string input;

            if (validInputs == null || validInputs.Length == 0)
            {
                input = Console.ReadLine();
            }
            else
            {
                do
                {
                    input = Console.ReadLine().ToLower();
                }
                while (!validInputs.Select(i => i.ToLower()).Contains(input));
            }

            return validInputs.First(i => i.ToLower() == input);
        }

        public static string GetInput(IEnumerable<string> validInputs)
        {
            return GetInput(validInputs.ToArray());
        }

        /// <summary>
        /// Prints a message to the console
        /// </summary>
        /// <param name="message">The message to display</param>
        /// <param name="extraLine">Whether an extra line should be printed after the message</param>
        /// <returns>The number of lines written to the console</returns>
        protected static int DisplayMessage(string message, bool extraLine = true)
        {
            Console.WriteLine(message);
            int linesWritten = 1;

            if (extraLine)
            {
                Console.WriteLine();
                linesWritten++;
            }

            return linesWritten;
        }

        /// <summary>
        /// Displays a message along with input options for the player.
        /// </summary>
        /// <param name="prompt">The message to display.</param>
        /// <param name="actions">The actions that the player can take.</param>
        /// <param name="padding">Text that will appear before each option.</param>
        /// <returns>The number of lines written to the console.</returns>
        protected static int DisplayPrompt(string prompt, Dictionary<char, string> actions, string padding = "")
        {
            int linesWritten = 0;

            if (prompt != null)
            {
                Console.WriteLine(prompt);
                linesWritten++;
            }

            foreach (KeyValuePair<char, string> action in actions)
            {
                Console.WriteLine($"{padding}{action.Key}: {action.Value}");
                linesWritten++;
            }

            Console.WriteLine();
            return ++linesWritten;
        }

        //private void ClearLines(int numLines)
        //{
        //    Console.SetCursorPosition(0, Console.CursorTop);
        //    Console.Write(new string(' ', Console.BufferWidth));

        //    for (int i = 0; i < numLines; i++)
        //    {
        //        Console.SetCursorPosition(0, Console.CursorTop - 2);
        //        Console.Write(new string(' ', Console.BufferWidth));
        //    }

        //    Console.SetCursorPosition(0, Console.CursorTop - 1);
        //}
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles the <see cref="Player.ItemGained"/> event
        /// </summary>
        /// <param name="sender">The object that invoked the event</param>
        /// <param name="e">The arguments sent with the event</param>
        private void OnItemGained(object sender, ItemGainedEventArgs e)
        {
            string message = e.FirstItem ? "Found " : "Also found ";
            message += e.Item.Stackable ? e.Item.Count.ToString() : Utils.AnOrA(e.Item.Name, false);
            message += " " + e.Item.Name;
            message += e.ItemKept ? '!' : '.';
            DisplayMessage(message);
        }

        /// <summary>
        /// Handles the <see cref="Player.StatsIncreased"/> event
        /// </summary>
        /// <param name="sender">The object that invoked the event</param>
        /// <param name="e">The arguments sent with the event</param>
        private void OnStatsIncreased (object sender, StatsIncreasedEventArgs e)
        {
            string message = "";

            foreach (StatIncrease stat in e.StatsIncreased)
            {
                message += stat.Stat switch
                {
                    Stats.XP    => $"Earned {stat.IncreaseAmount} XP.",
                    Stats.Level => $"Leveled up!\nReached level {stat.NewAmount}.",
                    _           => $"{stat.Stat} increased by {stat.IncreaseAmount}."
                };

                message += '\n';
            }

            DisplayMessage(message);
        }
        #endregion

        /// <summary>
        /// Updates the game difficulty based on the player's stats and the length of the game
        /// </summary>
        private void UpdateDifficulty()
        {
            Difficulty = StartingDifficulty + ((Player.Power + Player.Defense) / 2) + (MovesTaken / 10);
        }

        /// <summary>
        /// Displays a game over message
        /// </summary>
        private void GameOver()
        {
            DisplayMessage("Game over.");
            DisplayPrompt("Play again?", YesNoActions);
            switch ((YesNoAction)GetInput(YesNoActions.Select(a => a.Key)))
            {
                case YesNoAction.Yes:
                    Reset();
                    Start();
                    break;
                default:
                    return;
            }
            
        }
    }
}
