using UnityEngine;

namespace HideoutRoulette.Client.Wheel
{
    /// <summary>
    ///     Sounds of the table. Clips come from the asset bundle, a missing clip is simply silent.
    /// </summary>
    public class TableAudio : MonoBehaviour
    {
        private const float ChipVolume = 0.6f;
        private const float SweepVolume = 0.7f;
        private const float RollVolume = 0.8f;
        private const float OutcomeVolume = 0.8f;

        // Different takes of the ball rolling, one is picked per spin
        public AudioClip[] BallRolls;
        public AudioClip BallClack;
        public AudioClip ChipPlace;
        public AudioClip ChipsSweep;
        public AudioClip Win;
        public AudioClip Lose;

        private AudioSource _loop;
        private AudioSource _oneShots;

        private void Awake()
        {
            _loop = CreateSource(true);
            _oneShots = CreateSource(false);
        }

        private AudioSource CreateSource(bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.dopplerLevel = 0f;

            // Heard from the table, fading out as the player walks away
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2.5f;
            source.maxDistance = 14f;

            // Through the games mixer so its volume settings apply
            if (MonoBehaviourSingleton<BetterAudio>.Instantiated)
            {
                source.outputAudioMixerGroup = MonoBehaviourSingleton<BetterAudio>.Instance.MasterMixerGroup;
            }

            return source;
        }

        public void PlayChip()
        {
            PlayOnce(ChipPlace, ChipVolume, true);
        }

        public void PlaySweep()
        {
            PlayOnce(ChipsSweep, SweepVolume, true);
        }

        public void PlayClack(float volume)
        {
            PlayOnce(BallClack, volume, true);
        }

        public void PlayWin()
        {
            PlayOnce(Win, OutcomeVolume, false);
        }

        public void PlayLose()
        {
            PlayOnce(Lose, OutcomeVolume, false);
        }

        /// <summary>
        ///     Choose one of the rolling takes for the coming spin, starting somewhere different in it each time
        /// </summary>
        public void PickRoll()
        {
            if (_loop == null || BallRolls == null || BallRolls.Length == 0)
            {
                return;
            }

            _loop.Stop();
            _loop.clip = BallRolls[Random.Range(0, BallRolls.Length)];
            if (_loop.clip != null)
            {
                _loop.time = Random.Range(0f, _loop.clip.length * 0.9f);
            }
        }

        /// <summary>
        ///     Rolling sound of the ball on the track, quieter and lower as it slows. Zero stops it.
        /// </summary>
        public void SetRoll(float intensity)
        {
            if (_loop == null || _loop.clip == null)
            {
                return;
            }

            if (intensity <= 0.01f)
            {
                _loop.Stop();

                return;
            }

            _loop.volume = Mathf.Clamp01(intensity) * RollVolume * Plugin.Volume.Value;
            _loop.pitch = 0.7f + 0.5f * Mathf.Clamp01(intensity);
            if (!_loop.isPlaying)
            {
                _loop.Play();
            }
        }

        private void PlayOnce(AudioClip clip, float volume, bool varyPitch)
        {
            if (clip == null || _oneShots == null)
            {
                return;
            }

            // A little variation stops repeated chips and bounces sounding identical
            _oneShots.pitch = varyPitch ? Random.Range(0.92f, 1.08f) : 1f;
            _oneShots.PlayOneShot(clip, Mathf.Clamp01(volume) * Plugin.Volume.Value);
        }
    }
}
