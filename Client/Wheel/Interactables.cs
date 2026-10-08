using System.Collections.Generic;
using EFT;
using EFT.Hideout.ShootingRange;
using UnityEngine;

namespace HideoutRoulette.Client.Wheel
{
    // Both interactables derive from InteractiveShootingRange because the hideout asks that
    // type for its own list of actions, so no patch is needed to show a prompt.
    // Walking up to any part of the table offers to play, betting itself happens in TablePlayMode.

    /// <summary>
    ///     A betting spot on the felt and the chips stacked on it
    /// </summary>
    public class BetSpot : InteractiveShootingRange
    {
        private const int MaxChipsPerStack = 20;

        // Half the distance between neighbouring stacks, a little over a chips radius
        private const float StackSpacing = 0.0225f;

        // Where each stack stands on the spot (x, z from its centre), by how many stacks there are
        private static readonly Vector2[][] _stackOffsets =
        {
            new[] { Vector2.zero },
            new[] { new Vector2(-StackSpacing, 0f), new Vector2(StackSpacing, 0f) },
            new[] { new Vector2(-StackSpacing, StackSpacing), new Vector2(StackSpacing, StackSpacing), new Vector2(0f, -StackSpacing) },
            new[]
            {
                new Vector2(-StackSpacing, StackSpacing),
                new Vector2(StackSpacing, StackSpacing),
                new Vector2(-StackSpacing, -StackSpacing),
                new Vector2(StackSpacing, -StackSpacing),
            },
        };

        // Each chip is turned a little from the one below so the edge inlays do not line up
        private const float ChipTwistDegrees = 37f;

        public RouletteTable Table;
        public SpotDefinition Definition;

        // Width and depth of the printed cell on the felt
        public Vector2 Size;

        private readonly List<GameObject> _chips = new List<GameObject>();

        public override ActionsReturnClass InteractionStates(HideoutPlayerOwner owner)
        {
            return Table.PlayPrompt(owner);
        }

        /// <summary>
        ///     Show the chips that make up the amount bet here, one stack per denomination, none for zero
        /// </summary>
        public void ShowChips(long amount, long smallestChip)
        {
            foreach (var chip in _chips)
            {
                Destroy(chip);
            }

            _chips.Clear();
            if (amount <= 0)
            {
                return;
            }

            // Break the amount into chips the way a croupier would, largest denomination first. Each tier is worth ten of
            // the one below, so only the top tier can need more than nine chips.
            var stacks = new List<(int Tier, int Count)>();
            var remaining = amount;
            for (var tier = Table.ChipTemplates.Length - 1; tier >= 0; tier--)
            {
                var chipValue = smallestChip * (long)Mathf.Pow(10f, tier);
                var count = (int)Mathf.Min(MaxChipsPerStack, remaining / chipValue);
                remaining %= chipValue;
                if (count > 0)
                {
                    stacks.Add((tier, count));
                }
            }

            var offsets = _stackOffsets[stacks.Count - 1];
            for (var stack = 0; stack < stacks.Count; stack++)
            {
                for (var i = 0; i < stacks[stack].Count; i++)
                {
                    var chip = Instantiate(Table.ChipTemplates[stacks[stack].Tier], transform);
                    chip.transform.localPosition = new Vector3(offsets[stack].x, i * Table.ChipThickness, offsets[stack].y);
                    chip.transform.localRotation = Quaternion.Euler(0f, i * ChipTwistDegrees, 0f);
                    chip.SetActive(true);
                    _chips.Add(chip);
                }
            }
        }
    }

    /// <summary>
    ///     The wheel itself
    /// </summary>
    public class WheelSpot : InteractiveShootingRange
    {
        public RouletteTable Table;

        public override ActionsReturnClass InteractionStates(HideoutPlayerOwner owner)
        {
            return Table.PlayPrompt(owner);
        }
    }
}
