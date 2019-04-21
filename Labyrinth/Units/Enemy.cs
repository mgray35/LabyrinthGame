﻿using System;
using System.Data;

namespace Labyrinth
{
    class Enemy : Unit
    {
        public const float CHANCE_TO_DODGE = 0.1f;
        private const float CHANCE_FOR_LOOT = 0.2f;

        private Location location;

        public EnemyType EnemyType { get; private set; }
        public string Description { get; private set; }
        public int Difficulty { get; private set; }
        public override Location Location
        {
            get { return location; }
            set
            {
                location = value;

                // Ensure the enemy's location is set to this
                if (location.Enemy != this)
                    location.Enemy = this;
            }
        }

        /// <summary>
        /// Constructs an <see cref="Enemy"/> of the given type
        /// </summary>
        /// <param name="type">The type of <see cref="Enemy"/> to construct</param>
        public Enemy(EnemyType type)
        {
            DataRow entry = EnemyDao.GetTable().Select($"{nameof(EnemyType)} = '{type}'")[0];

            EnemyType = type;
            Description = (string)entry[nameof(Description)];
            Power = (int)entry[nameof(Power)];
            Defense = 0;
            MaxHP = (int)entry[nameof(MaxHP)];
            CurrentHP = MaxHP;
            XP = (int)entry[nameof(XP)];
            Difficulty = (int)entry[nameof(Difficulty)];
            
            if (Utils.Roll(CHANCE_FOR_LOOT))
            {
                Item loot = Item.RandomItem(Items);
                Items[loot.ItemType] = loot;
            }
        }

        /// <summary>
        /// Constructs a random enemy based on the given difficulty level
        /// </summary>
        /// <param name="difficulty">The current game difficulty</param>
        /// <returns>A random enemy</returns>
        public static Enemy RandomEnemy(int difficulty)
        {
            Enemy enemy = null;
            int encounterValue = Utils.Random.Next(difficulty);

            foreach (DataRow row in EnemyDao.GetTable().Rows)
            {
                if (encounterValue <= (int)row[nameof(Difficulty)])
                {
                    enemy = new Enemy((EnemyType)Enum.Parse(typeof(EnemyType), row[nameof(EnemyType)].ToString()));
                    break;
                }
            }

            if (enemy == null)
            {
                if (Game.MinotaurIsAlive)
                {
                    enemy = new Enemy(EnemyType.Minotaur);
                }
                else
                {
                    enemy = new Enemy(EnemyType.Dragon);
                }
            }

            return enemy;
        }
    }
}
