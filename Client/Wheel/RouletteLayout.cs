using System.Collections.Generic;
using UnityEngine;

namespace HideoutRoulette.Client.Wheel
{
    /// <summary>
    ///     One place a bet can go on the felt, positioned in grid cells from the corner nearest the players left hand
    /// </summary>
    public class SpotDefinition
    {
        public string Type;
        public int Value;

        // Shown in the interaction prompt
        public string Label;

        // Drawn on the felt with the seven segment font, null for none
        public string FeltText;
        public Color32 FeltColor;

        public int Column;
        public int Row;
        public int ColumnSpan = 1;
        public int RowSpan = 1;
    }

    /// <summary>
    ///     Shape of a European wheel and betting layout, shared by the textures and the table builder
    /// </summary>
    public static class RouletteLayout
    {
        public const int PocketCount = 37;
        public const float PocketAngle = 360f / PocketCount;

        public const int GridColumns = 14;
        public const int GridRows = 5;

        public static readonly Color32 Red = new Color32(170, 20, 20, 255);
        public static readonly Color32 Black = new Color32(20, 20, 20, 255);
        public static readonly Color32 Green = new Color32(15, 110, 50, 255);
        public static readonly Color32 Felt = new Color32(10, 75, 40, 255);

        // Clockwise order of the numbers around a European wheel
        public static readonly int[] WheelOrder =
        {
            0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26,
        };

        private static readonly HashSet<int> _redNumbers = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

        public static Color32 GetColor(int number)
        {
            if (number == 0)
            {
                return Green;
            }

            return _redNumbers.Contains(number) ? Red : Black;
        }

        public static string GetColorName(int number)
        {
            if (number == 0)
            {
                return "green";
            }

            return _redNumbers.Contains(number) ? "red" : "black";
        }

        /// <summary>
        ///     Angle of a numbers pocket on the wheel disc, in degrees clockwise from the discs forward axis
        /// </summary>
        public static float GetPocketAngle(int number)
        {
            return System.Array.IndexOf(WheelOrder, number) * PocketAngle;
        }

        public static List<SpotDefinition> CreateSpots()
        {
            var spots = new List<SpotDefinition>
            {
                new SpotDefinition
                {
                    Type = "straight",
                    Value = 0,
                    Label = "0 green",
                    FeltText = "0",
                    FeltColor = Green,
                    Column = 0,
                    Row = 2,
                    RowSpan = 3,
                },
            };

            // Numbers run in 12 columns of 3, lowest number nearest the player
            for (var number = 1; number <= 36; number++)
            {
                spots.Add(
                    new SpotDefinition
                    {
                        Type = "straight",
                        Value = number,
                        Label = $"{number} {GetColorName(number)}",
                        FeltText = number.ToString(),
                        FeltColor = GetColor(number),
                        Column = 1 + (number - 1) / 3,
                        Row = 2 + (number - 1) % 3,
                    }
                );
            }

            for (var i = 0; i < 3; i++)
            {
                spots.Add(
                    new SpotDefinition
                    {
                        Type = "column",
                        Value = i + 1,
                        Label = $"Column {i + 1}",
                        FeltText = "2-1",
                        FeltColor = Felt,
                        Column = 13,
                        Row = 2 + i,
                    }
                );
                spots.Add(
                    new SpotDefinition
                    {
                        Type = "dozen",
                        Value = i + 1,
                        Label = $"{i * 12 + 1} to {i * 12 + 12}",
                        FeltText = $"{i * 12 + 1}-{i * 12 + 12}",
                        FeltColor = Felt,
                        Column = 1 + i * 4,
                        Row = 1,
                        ColumnSpan = 4,
                    }
                );
            }

            AddEvenMoneySpot(spots, 0, "low", "1 to 18", "1-18", Felt);
            AddEvenMoneySpot(spots, 1, "even", "Even", "EUEn", Felt);
            AddEvenMoneySpot(spots, 2, "red", "Red", null, Red);
            AddEvenMoneySpot(spots, 3, "black", "Black", null, Black);
            AddEvenMoneySpot(spots, 4, "odd", "Odd", "odd", Felt);
            AddEvenMoneySpot(spots, 5, "high", "19 to 36", "19-36", Felt);

            return spots;
        }

        private static void AddEvenMoneySpot(List<SpotDefinition> spots, int index, string type, string label, string feltText, Color32 color)
        {
            spots.Add(
                new SpotDefinition
                {
                    Type = type,
                    Label = label,
                    FeltText = feltText,
                    FeltColor = color,
                    Column = 1 + index * 2,
                    Row = 0,
                    ColumnSpan = 2,
                }
            );
        }
    }
}
