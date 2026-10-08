using System.Collections.Generic;

namespace HideoutRoulette.Client.Wheel
{
    /// <summary>
    ///     One place a bet can go on the felt, positioned in grid cells from the corner nearest the players left hand
    /// </summary>
    public class SpotDefinition
    {
        public string Type;
        public int Value;

        // Shown in the play mode panel
        public string Label;

        public int Column;
        public int Row;
        public int ColumnSpan = 1;
        public int RowSpan = 1;
    }

    /// <summary>
    ///     Betting grid of a European table. The printed layout in the model (Art/roulette_table.py) follows the same grid.
    /// </summary>
    public static class RouletteLayout
    {
        public const int PocketCount = 37;
        public const int GridColumns = 14;
        public const int GridRows = 5;

        private static readonly HashSet<int> _redNumbers = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

        public static string GetColorName(int number)
        {
            if (number == 0)
            {
                return "green";
            }

            return _redNumbers.Contains(number) ? "red" : "black";
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
                        Column = 1 + i * 4,
                        Row = 1,
                        ColumnSpan = 4,
                    }
                );
            }

            var evenMoney = new[] { ("low", "1 to 18"), ("even", "Even"), ("red", "Red"), ("black", "Black"), ("odd", "Odd"), ("high", "19 to 36") };
            for (var i = 0; i < evenMoney.Length; i++)
            {
                spots.Add(
                    new SpotDefinition
                    {
                        Type = evenMoney[i].Item1,
                        Label = evenMoney[i].Item2,
                        Column = 1 + i * 2,
                        Row = 0,
                        ColumnSpan = 2,
                    }
                );
            }

            return spots;
        }
    }
}
