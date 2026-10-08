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
        private const float ChipRadius = 0.022f;
        private const float ChipThickness = 0.007f;
        private const int MaxChipsShown = 9;

        private static readonly Color[] _chipColors =
        {
            new Color(0.9f, 0.9f, 0.85f),
            new Color(0.75f, 0.1f, 0.1f),
            new Color(0.1f, 0.25f, 0.7f),
            new Color(0.8f, 0.65f, 0.15f),
        };

        public RouletteTable Table;
        public SpotDefinition Definition;
        public Transform ChipStack;

        public override ActionsReturnClass InteractionStates(HideoutPlayerOwner owner)
        {
            return Table.PlayPrompt(owner);
        }

        /// <summary>
        ///     Show a stack of chips for the amount bet here, none for zero
        /// </summary>
        public void ShowChips(long amount, long smallestChip)
        {
            ChipStack.gameObject.SetActive(amount > 0);
            if (amount <= 0)
            {
                return;
            }

            // Use the largest chip denomination that fits so stacks stay short
            var tier = 0;
            var chipValue = smallestChip;
            while (tier < _chipColors.Length - 1 && amount >= chipValue * 10)
            {
                chipValue *= 10;
                tier++;
            }

            var height = Mathf.Min(MaxChipsShown, (int)(amount / chipValue)) * ChipThickness;

            // Unity cylinders are 2 units tall and 1 unit wide
            ChipStack.localScale = new Vector3(ChipRadius * 2f, height / 2f, ChipRadius * 2f);
            ChipStack.localPosition = new Vector3(0f, height / 2f, 0f);
            ChipStack.GetComponent<Renderer>().material.color = _chipColors[tier];
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
