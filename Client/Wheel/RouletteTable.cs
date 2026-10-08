using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Communications;
using HideoutRoulette.Client.Net;
using UnityEngine;

namespace HideoutRoulette.Client.Wheel
{
    /// <summary>
    ///     Bets, spinning and animation for one table. Only depends on the parts assigned below, TableFactory fills
    ///     them in from the model.
    /// </summary>
    public class RouletteTable : MonoBehaviour
    {
        private const float SpinSeconds = 9f;

        // When the ball first lands in the pocket ring, and how long before that it leaves the outer track
        private const float BallSeconds = 6.5f;
        private const float BallDropSeconds = 1.5f;
        private const float WheelSpinDegrees = 1080f;

        // Whole turns only: the landing sums rely on the extra laps not changing where the ball ends up
        private const int BallSpinTurns = 5;

        // The ball lands this many pockets short of the winning one and hops the rest of the way
        private const int SettleHops = 2;
        private const float ChipSweepSeconds = 0.55f;

        // (start, end, height). While dropping from the track, as a fraction of the drop.
        private static readonly (float Start, float End, float Height)[] _dropBounces = { (0.2f, 0.45f, 0.018f), (0.45f, 0.8f, 0.022f), (0.8f, 1f, 0.01f) };

        // Once in the pocket ring, in seconds after landing. The first two carry the ball one pocket onwards each.
        private static readonly (float Start, float End, float Height)[] _settleBounces = { (0f, 0.38f, 0.022f), (0.38f, 0.7f, 0.013f), (0.7f, 0.9f, 0.005f) };

        public Transform Disc;
        public Transform Ball;

        // Win marker, its origin is at its base
        public Transform Dolly;
        public TablePlayMode PlayMode;
        public TableAudio Audio;

        // One hidden chip per denomination tier, lowest first, copied into stacks on the betting spots
        public GameObject[] ChipTemplates;
        public float ChipThickness;
        public List<BetSpot> Spots = new List<BetSpot>();

        // Edges of the playing surface chips are swept to, in the tables local space: towards the player and away from them
        public float NearEdgeZ;
        public float FarEdgeZ;

        // Ball path, in the tables local space
        public Vector3 WheelCentre;
        public float TrackRadius;
        public float TrackHeight;
        public float PocketRadius;
        public float PocketHeight;

        // Where each numbers pocket sits on the wheel at rest, in degrees clockwise from the tables +Z, indexed by number
        public float[] PocketAngles;

        private readonly Dictionary<BetSpot, long> _bets = new Dictionary<BetSpot, long>();
        private Dictionary<BetSpot, long> _lastBets;
        private RouletteConfig _config = new RouletteConfig();

        // Chips sliding off the table, no longer owned by a betting spot
        private readonly List<GameObject> _looseChips = new List<GameObject>();

        // Degrees clockwise seen from above, the ball angle is relative to the table not the disc
        private float _wheelAngle;
        private float _ballAngle;

        public bool Spinning { get; private set; }

        /// <summary>
        ///     Outcome of the most recent spin as text, null before the first one
        /// </summary>
        public string LastResult { get; private set; }

        public long TotalBet => _bets.Values.Sum();

        public bool HasLastBets => _lastBets != null && _lastBets.Count > 0;

        public long[] ChipValues => new[] { _config.MinBet, _config.MinBet * 10, _config.MinBet * 100 };

        private async void Start()
        {
            // The ball starts in the zero pocket
            _ballAngle = PocketAngles[0];
            SetBall(_ballAngle, PocketRadius, PocketHeight);

            var config = await RouletteApi.GetConfig();
            if (config != null)
            {
                _config = config;
            }
        }

        private void OnEnable()
        {
            // Coroutines die when the hideout is hidden, the server has already settled the spin by then
            Spinning = false;
        }

        private void OnDisable()
        {
            Audio.SetRoll(0f);

            // Their sweep coroutine has just died with everything else
            foreach (var chip in _looseChips)
            {
                Destroy(chip);
            }

            _looseChips.Clear();
        }

        /// <summary>
        ///     Prompt shown when the player looks at any part of the table
        /// </summary>
        public ActionsReturnClass PlayPrompt(HideoutPlayerOwner owner)
        {
            // The game asks again whenever the players view drifts onto another part of the table, which would bring
            // the prompt back over the play view
            if (TablePlayMode.Current != null)
            {
                return null;
            }

            return new ActionsReturnClass
            {
                Actions = new List<ActionsTypesClass> { new ActionsTypesClass { Name = "Play roulette", Action = () => PlayMode.Enter(owner) } },
            };
        }

        public long GetBet(BetSpot spot)
        {
            return _bets.TryGetValue(spot, out var amount) ? amount : 0;
        }

        public void AddBet(BetSpot spot, long amount)
        {
            if (TotalBet + amount > _config.MaxTotalBet)
            {
                NotificationManagerClass.DisplayWarningNotification($"Roulette: table maximum is {_config.MaxTotalBet:N0} roubles");

                return;
            }

            if (!_bets.ContainsKey(spot) && _bets.Count >= _config.MaxBetsPerSpin)
            {
                NotificationManagerClass.DisplayWarningNotification($"Roulette: no more than {_config.MaxBetsPerSpin} bets per spin");

                return;
            }

            SetBet(spot, GetBet(spot) + amount);
            Audio.PlayChip();
        }

        public void RemoveFromBet(BetSpot spot, long amount)
        {
            if (GetBet(spot) <= 0)
            {
                return;
            }

            SetBet(spot, Math.Max(0, GetBet(spot) - amount));
            Audio.PlayChip();
        }

        /// <summary>
        ///     Hand every bet back, the chips slide to the players side of the table
        /// </summary>
        public void ClearAllBets()
        {
            if (_bets.Count == 0)
            {
                return;
            }

            SweepChips(_bets.Keys, NearEdgeZ);
            _bets.Clear();
        }

        public void RepeatLastBets()
        {
            foreach (var bet in _lastBets)
            {
                SetBet(bet.Key, bet.Value);
            }

            Audio.PlayChip();
        }

        private void SetBet(BetSpot spot, long amount)
        {
            if (amount > 0)
            {
                _bets[spot] = amount;

                // Chips may land on the number the marker is standing on
                Dolly.gameObject.SetActive(false);
            }
            else
            {
                _bets.Remove(spot);
            }

            spot.ShowChips(amount, _config.MinBet);
        }

        public void Spin()
        {
            if (Spinning || _bets.Count == 0)
            {
                return;
            }

            Spinning = true;
            Dolly.gameObject.SetActive(false);

            var bets = _bets
                .Select(bet => new RouletteBet
                {
                    Type = bet.Key.Definition.Type,
                    Value = bet.Key.Definition.Value,
                    Amount = bet.Value,
                })
                .ToArray();
            RouletteApi.Spin(bets, OnSpinResolved);
        }

        private void OnSpinResolved(RouletteSpinResult result, string error)
        {
            // Unity null check, the table is destroyed if the hideout unloaded while waiting
            if (this == null)
            {
                return;
            }

            if (result == null)
            {
                Spinning = false;
                NotificationManagerClass.DisplayWarningNotification($"Roulette: {error}");

                return;
            }

            if (isActiveAndEnabled)
            {
                StartCoroutine(Animate(result));
            }
            else
            {
                Finish(result);
            }
        }

        /// <summary>
        ///     Wheel turns one way and slows. The ball circles the other way, bounces down off the track, lands a couple
        ///     of pockets short and hops into the winning one, then is carried round in it.
        /// </summary>
        private IEnumerator Animate(RouletteSpinResult result)
        {
            var pocketAngle = PocketAngles[result.Pocket];
            var pocketStep = 360f / RouletteLayout.PocketCount;
            var wheelStart = _wheelAngle;
            var ballStart = _ballAngle;

            // Seen from the wheel the ball is travelling towards lower angles, so it lands on the high side of its pocket.
            // Its travel round the track is picked to bring it down exactly there.
            var landingAngle = pocketAngle + SettleHops * pocketStep;
            var wheelAtLanding = wheelStart + WheelSpinDegrees * EaseOut(BallSeconds / SpinSeconds);
            var ballTravel = BallSpinTurns * 360f + Mathf.Repeat(ballStart - wheelAtLanding - landingAngle, 360f);

            // Every time the ball comes down on something: (seconds into the spin, loudness)
            var dropStart = BallSeconds - BallDropSeconds;
            var impacts = new List<(float Time, float Volume)>();
            foreach (var bounce in _dropBounces)
            {
                impacts.Add((dropStart + bounce.End * BallDropSeconds, 0.4f + bounce.Height * 25f));
            }

            foreach (var bounce in _settleBounces)
            {
                impacts.Add((BallSeconds + bounce.End, 0.25f + bounce.Height * 25f));
            }

            Audio.PickRoll();

            var nextImpact = 0;
            var elapsed = 0f;
            while (elapsed < SpinSeconds)
            {
                elapsed += Time.deltaTime;

                _wheelAngle = wheelStart + WheelSpinDegrees * EaseOut(elapsed / SpinSeconds);
                Disc.localRotation = Quaternion.Euler(0f, _wheelAngle, 0f);

                if (elapsed < BallSeconds)
                {
                    _ballAngle = ballStart - ballTravel * EaseOut(elapsed / BallSeconds);

                    var dropProgress = Mathf.Clamp01((elapsed - dropStart) / BallDropSeconds);
                    var drop = Mathf.SmoothStep(0f, 1f, dropProgress);
                    SetBall(
                        _ballAngle,
                        Mathf.Lerp(TrackRadius, PocketRadius, drop),
                        Mathf.Lerp(TrackHeight, PocketHeight, drop) + BounceHeight(_dropBounces, dropProgress)
                    );

                    // The ball slows steadily, and stops rolling on the track as it bounces off it
                    Audio.SetRoll((1f - elapsed / BallSeconds) * (1f - drop));
                }
                else
                {
                    var settle = elapsed - BallSeconds;

                    // One pocket further on with each of the first hops
                    var pocketsToGo = 0f;
                    for (var hop = 0; hop < SettleHops; hop++)
                    {
                        pocketsToGo += 1f - Mathf.Clamp01((settle - _settleBounces[hop].Start) / (_settleBounces[hop].End - _settleBounces[hop].Start));
                    }

                    _ballAngle = _wheelAngle + pocketAngle + pocketsToGo * pocketStep;
                    SetBall(_ballAngle, PocketRadius, PocketHeight + BounceHeight(_settleBounces, settle));
                    Audio.SetRoll(0f);
                }

                while (nextImpact < impacts.Count && elapsed >= impacts[nextImpact].Time)
                {
                    Audio.PlayClack(impacts[nextImpact].Volume);
                    nextImpact++;
                }

                yield return null;
            }

            Finish(result);
        }

        /// <summary>
        ///     Height above the surface for a run of bounces, each a simple arc
        /// </summary>
        private static float BounceHeight((float Start, float End, float Height)[] bounces, float position)
        {
            foreach (var bounce in bounces)
            {
                if (position >= bounce.Start && position < bounce.End)
                {
                    var progress = (position - bounce.Start) / (bounce.End - bounce.Start);

                    return bounce.Height * 4f * progress * (1f - progress);
                }
            }

            return 0f;
        }

        private void Finish(RouletteSpinResult result)
        {
            Audio.SetRoll(0f);
            _ballAngle = _wheelAngle + PocketAngles[result.Pocket];
            SetBall(_ballAngle, PocketRadius, PocketHeight);

            var winningSpot = Spots.FirstOrDefault(spot => spot.Definition.Type == "straight" && spot.Definition.Value == result.Pocket);
            if (winningSpot != null)
            {
                Dolly.position = winningSpot.transform.position;
                Dolly.gameObject.SetActive(true);
            }

            // Winning bets come back to the player, the house takes the rest
            _lastBets = new Dictionary<BetSpot, long>(_bets);
            var winners = _bets.Keys.Where(spot => RouletteLayout.IsWinningBet(spot.Definition.Type, spot.Definition.Value, result.Pocket)).ToList();
            SweepChips(winners, NearEdgeZ);
            SweepChips(_bets.Keys.Except(winners).ToList(), FarEdgeZ);
            _bets.Clear();
            Spinning = false;

            if (result.Net > 0)
            {
                Audio.PlayWin();
            }
            else if (result.Net < 0)
            {
                Audio.PlayLose();
            }

            var outcome = result.Net >= 0 ? $"won {result.Net:N0}" : $"lost {Math.Abs(result.Net):N0}";
            var mail = result.PaidByMail ? " (stash full, winnings sent by mail)" : string.Empty;
            LastResult = $"{result.Pocket} {result.Color}, you {outcome} roubles{mail}";
            NotificationManagerClass.DisplayMessageNotification($"Roulette: {LastResult}", ENotificationDurationType.Long);
        }

        /// <summary>
        ///     Take the chips off these spots and slide them to one edge of the table
        /// </summary>
        private void SweepChips(IEnumerable<BetSpot> spots, float edgeZ)
        {
            var chips = new List<GameObject>();
            foreach (var spot in spots)
            {
                chips.AddRange(spot.ReleaseChips());
            }

            if (chips.Count == 0)
            {
                return;
            }

            // Nothing would move them while the hideout is hidden
            if (!isActiveAndEnabled)
            {
                foreach (var chip in chips)
                {
                    Destroy(chip);
                }

                return;
            }

            Audio.PlaySweep();
            StartCoroutine(SlideChips(chips, edgeZ));
        }

        private IEnumerator SlideChips(List<GameObject> chips, float edgeZ)
        {
            _looseChips.AddRange(chips);

            var starts = new Vector3[chips.Count];
            for (var i = 0; i < chips.Count; i++)
            {
                chips[i].transform.SetParent(transform, true);
                starts[i] = chips[i].transform.localPosition;
            }

            var elapsed = 0f;
            while (elapsed < ChipSweepSeconds)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.Clamp01(elapsed / ChipSweepSeconds);

                // Gather speed across the felt, then shrink away at the rail
                var travelled = progress * progress;
                var scale = 1f - Mathf.SmoothStep(0f, 1f, (progress - 0.7f) / 0.3f);
                for (var i = 0; i < chips.Count; i++)
                {
                    var position = starts[i];
                    position.z = Mathf.Lerp(starts[i].z, edgeZ, travelled);
                    chips[i].transform.localPosition = position;
                    chips[i].transform.localScale = Vector3.one * scale;
                }

                yield return null;
            }

            foreach (var chip in chips)
            {
                _looseChips.Remove(chip);
                Destroy(chip);
            }
        }

        private void SetBall(float angle, float radius, float height)
        {
            var radians = angle * Mathf.Deg2Rad;
            Ball.localPosition = WheelCentre + new Vector3(Mathf.Sin(radians) * radius, height, Mathf.Cos(radians) * radius);
        }

        private static float EaseOut(float progress)
        {
            var remaining = 1f - Mathf.Clamp01(progress);

            return 1f - remaining * remaining;
        }
    }
}
