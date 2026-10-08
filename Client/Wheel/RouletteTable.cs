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
    ///     Bets, spinning and animation for one table. Only depends on the transforms below,
    ///     so the visuals can come from code or from a prefab with the same parts.
    /// </summary>
    public class RouletteTable : MonoBehaviour
    {
        private const float SpinSeconds = 7f;
        private const float BallSeconds = 5.5f;
        private const float BallDropSeconds = 1.5f;
        private const float WheelSpinDegrees = 900f;
        private const float BallSpinDegrees = 1440f;

        public Transform Disc;
        public Transform Ball;
        public Transform Dolly;
        public TablePlayMode PlayMode;
        public List<BetSpot> Spots = new List<BetSpot>();

        // Ball path, in the tables local space
        public Vector3 WheelCentre;
        public float TrackRadius;
        public float TrackHeight;
        public float PocketRadius;
        public float PocketHeight;

        private readonly Dictionary<BetSpot, long> _bets = new Dictionary<BetSpot, long>();
        private Dictionary<BetSpot, long> _lastBets;
        private RouletteConfig _config = new RouletteConfig();

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

        /// <summary>
        ///     Prompt shown when the player looks at any part of the table
        /// </summary>
        public ActionsReturnClass PlayPrompt(HideoutPlayerOwner owner)
        {
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
        }

        public void RemoveFromBet(BetSpot spot, long amount)
        {
            SetBet(spot, Math.Max(0, GetBet(spot) - amount));
        }

        public void ClearAllBets()
        {
            foreach (var spot in _bets.Keys.ToList())
            {
                SetBet(spot, 0);
            }
        }

        public void RepeatLastBets()
        {
            foreach (var bet in _lastBets)
            {
                SetBet(bet.Key, bet.Value);
            }
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
        ///     Wheel turns one way and slows, ball circles the other way, drops in and is carried round in the winning pocket
        /// </summary>
        private IEnumerator Animate(RouletteSpinResult result)
        {
            var pocketAngle = RouletteLayout.GetPocketAngle(result.Pocket);
            var wheelStart = _wheelAngle;
            var ballStart = _ballAngle;

            // Pick the balls travel so it comes to rest exactly over the winning pocket
            var wheelAtBallStop = wheelStart + WheelSpinDegrees * EaseOut(BallSeconds / SpinSeconds);
            var ballTravel = BallSpinDegrees + Mathf.Repeat(ballStart - wheelAtBallStop - pocketAngle, 360f);

            var elapsed = 0f;
            while (elapsed < SpinSeconds)
            {
                elapsed += Time.deltaTime;

                _wheelAngle = wheelStart + WheelSpinDegrees * EaseOut(elapsed / SpinSeconds);
                Disc.localRotation = Quaternion.Euler(0f, _wheelAngle, 0f);

                if (elapsed < BallSeconds)
                {
                    _ballAngle = ballStart - ballTravel * EaseOut(elapsed / BallSeconds);
                    var drop = Mathf.SmoothStep(0f, 1f, (elapsed - (BallSeconds - BallDropSeconds)) / BallDropSeconds);
                    SetBall(_ballAngle, Mathf.Lerp(TrackRadius, PocketRadius, drop), Mathf.Lerp(TrackHeight, PocketHeight, drop));
                }
                else
                {
                    _ballAngle = _wheelAngle + pocketAngle;
                    SetBall(_ballAngle, PocketRadius, PocketHeight);
                }

                yield return null;
            }

            Finish(result);
        }

        private void Finish(RouletteSpinResult result)
        {
            _ballAngle = _wheelAngle + RouletteLayout.GetPocketAngle(result.Pocket);
            SetBall(_ballAngle, PocketRadius, PocketHeight);

            var winningSpot = Spots.FirstOrDefault(spot => spot.Definition.Type == "straight" && spot.Definition.Value == result.Pocket);
            if (winningSpot != null)
            {
                Dolly.position = winningSpot.transform.position + transform.up * (Dolly.localScale.y);
                Dolly.gameObject.SetActive(true);
            }

            _lastBets = new Dictionary<BetSpot, long>(_bets);
            ClearAllBets();
            Spinning = false;

            var outcome = result.Net >= 0 ? $"won {result.Net:N0}" : $"lost {Math.Abs(result.Net):N0}";
            var mail = result.PaidByMail ? " (stash full, winnings sent by mail)" : string.Empty;
            LastResult = $"{result.Pocket} {result.Color}, you {outcome} roubles{mail}";
            NotificationManagerClass.DisplayMessageNotification($"Roulette: {LastResult}", ENotificationDurationType.Long);
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
