"""Generates the tables sounds and the felt textures. Everything is synthesised, so there is nothing to license,
and any file can be replaced by a recording or painted texture of the same name.

Run with Blender 3.x (used for its bundled numpy and image writer):
    blender -b --python Art/media.py -- Unity/Assets/Roulette
"""

import os
import sys
import wave

import bpy
import numpy as np

RATE = 44100
rng = np.random.default_rng(7)


# ---------------------------------------------------------------------------------------------------------------------
# Sound helpers


def seconds(duration):
    return np.arange(int(RATE * duration)) / RATE


def band_noise(duration, low, high):
    """Noise limited to a frequency band. Built in the frequency domain, so it also loops without a click."""
    count = int(RATE * duration)
    spectrum = np.fft.rfft(rng.standard_normal(count))
    freqs = np.fft.rfftfreq(count, 1 / RATE)
    centre, width = (low + high) / 2, (high - low) / 2
    spectrum *= np.exp(-(((freqs - centre) / width) ** 2))
    noise = np.fft.irfft(spectrum, count)
    return noise / np.max(np.abs(noise))


def ping(duration, partials, start=0.0):
    """Sum of decaying sines, partials are (frequency, level, decay time)."""
    t = seconds(duration)
    out = np.zeros_like(t)
    live = t >= start
    local = t[live] - start
    for frequency, level, decay in partials:
        out[live] += level * np.sin(2 * np.pi * frequency * local) * np.exp(-local / decay)
    return out


def click(duration, decay, low, high):
    """Short burst of band limited noise, the hard part of an impact."""
    return band_noise(duration, low, high) * np.exp(-seconds(duration) / decay)


def fade_edges(samples, fade=0.004):
    count = int(RATE * fade)
    samples[:count] *= np.linspace(0, 1, count)
    samples[-count:] *= np.linspace(1, 0, count)
    return samples


def save_wav(folder, name, samples, peak=0.8):
    samples = samples / np.max(np.abs(samples)) * peak
    with wave.open(os.path.join(folder, name + ".wav"), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(RATE)
        handle.writeframes((samples * 32767).astype(np.int16).tobytes())
    print(f"MEDIA sound {name}: {len(samples) / RATE:.2f}s")


# ---------------------------------------------------------------------------------------------------------------------
# Sounds


def ball_roll(seed):
    """Loop of a hard ball running round a wooden track. Each seed gives a different take.

    Three layers, all built so the end runs into the start: the wooden bowl ringing under the ball, a stream of tiny
    irregular knocks as the ball crosses the grain, and a faint hiss of the ball on lacquer. The loop is long and
    nothing in it repeats at a fixed rate, so it does not sound like a loop.
    """
    local = np.random.default_rng(seed)
    duration = 6.0
    count = int(RATE * duration)
    freqs = np.fft.rfftfreq(count, 1 / RATE)

    # The bowl: noise shaped by a handful of resonances, which is what makes it read as wood rather than hiss
    modes = [(170, 55, 1.0), (290, 70, 0.9), (470, 90, 0.8), (760, 120, 0.5), (1180, 180, 0.32), (1900, 300, 0.14)]
    shape = 0.04 * np.exp(-freqs / 3000)
    for frequency, width, level in modes:
        frequency *= local.uniform(0.9, 1.1)
        shape += level / (1 + ((freqs - frequency) / width) ** 2)
    shape *= 1 - np.exp(-((freqs / 70) ** 2))
    body = np.fft.irfft(np.fft.rfft(local.standard_normal(count)) * shape, count)

    # Knocks: random moments and strengths, each one a short woody tick
    knock_times = local.integers(0, count, int(duration * 30))
    impulses = np.zeros(count)
    np.add.at(impulses, knock_times, local.lognormal(0, 0.7, len(knock_times)))
    tick_length = int(RATE * 0.06)
    tick_t = np.arange(tick_length) / RATE
    tick = np.zeros(count)
    for frequency, level in ((390, 1.0), (650, 0.7), (1050, 0.45)):
        tick[:tick_length] += level * np.sin(2 * np.pi * frequency * local.uniform(0.9, 1.1) * tick_t) * np.exp(-tick_t / 0.011)
    knocks = np.fft.irfft(np.fft.rfft(impulses) * np.fft.rfft(tick), count)

    hiss = np.fft.irfft(np.fft.rfft(local.standard_normal(count)) * np.exp(-(((freqs - 4200) / 1700) ** 2)), count)

    # Loudness drifts up and down without any regular beat
    drift = np.fft.irfft(np.fft.rfft(local.standard_normal(count)) * np.exp(-((freqs / 4.0) ** 2)), count)
    drift = 0.8 + 0.2 * drift / np.max(np.abs(drift))

    return (body / np.std(body) + 0.5 * knocks / np.std(knocks) + 0.07 * hiss / np.std(hiss)) * drift


def ball_clack():
    """Ball striking a deflector or a pocket wall."""
    duration = 0.14
    return click(duration, 0.006, 1500, 9000) + ping(duration, [(2650, 0.7, 0.018), (4300, 0.45, 0.012), (6900, 0.2, 0.008)])


def chip_place():
    """One clay chip set down on felt or on another chip."""
    duration = 0.11
    return click(duration, 0.004, 2500, 11000) * 0.8 + ping(duration, [(3300, 0.6, 0.01), (5200, 0.4, 0.008), (7900, 0.2, 0.005)])


def chips_sweep():
    """Chips raked across the felt: a slide with chips knocking together in it."""
    duration = 0.6
    t = seconds(duration)
    slide = band_noise(duration, 1500, 6000) * np.sin(np.pi * t / duration) ** 1.5 * 0.5
    knocks = np.zeros_like(t)
    single = chip_place()
    for start in (0.05, 0.13, 0.19, 0.27, 0.33, 0.42, 0.47):
        offset = int(start * RATE)
        knocks[offset : offset + len(single)] += single[: len(knocks) - offset] * rng.uniform(0.3, 0.6)
    return fade_edges(slide + knocks)


def win():
    """Rising bell arpeggio."""
    duration = 1.3
    out = np.zeros(int(RATE * duration))
    for index, frequency in enumerate((1046.5, 1318.5, 1568.0, 2093.0)):
        out += ping(duration, [(frequency, 0.6, 0.35), (frequency * 2.76, 0.15, 0.12), (frequency * 2, 0.2, 0.2)], start=index * 0.09)
    return fade_edges(out)


def lose():
    """Two soft falling notes."""
    duration = 0.9
    out = ping(duration, [(233.1, 0.7, 0.22), (466.2, 0.2, 0.12)]) + ping(duration, [(185.0, 0.7, 0.3), (370.0, 0.2, 0.15)], start=0.22)
    return fade_edges(out)


# ---------------------------------------------------------------------------------------------------------------------
# Felt textures


def tile_noise(size, falloff):
    """Tileable noise, falloff sets how quickly fine detail dies away."""
    spectrum = np.fft.fft2(rng.standard_normal((size, size)))
    fx = np.fft.fftfreq(size)[:, None]
    fy = np.fft.fftfreq(size)[None, :]
    spectrum *= np.exp(-((fx**2 + fy**2) / falloff**2))
    noise = np.real(np.fft.ifft2(spectrum))
    return (noise - noise.min()) / (noise.max() - noise.min())


def save_png(folder, name, rgb):
    size = rgb.shape[0]
    rgba = np.concatenate([rgb, np.ones((size, size, 1))], axis=2).astype(np.float32)
    image = bpy.data.images.new(name, size, size, alpha=False)
    image.pixels = rgba.ravel()
    image.filepath_raw = os.path.join(folder, name + ".png")
    image.file_format = "PNG"
    image.save()
    print(f"MEDIA texture {name}: {size}px")


def blur(image, radius):
    """Gaussian blur that wraps round the edges, so the result still tiles."""
    fx = np.fft.fftfreq(image.shape[0])[:, None]
    fy = np.fft.fftfreq(image.shape[1])[None, :]
    return np.real(np.fft.ifft2(np.fft.fft2(image) * np.exp(-2 * (np.pi * radius) ** 2 * (fx**2 + fy**2))))


def scatter_fibres(size, count, min_length, max_length):
    """Short straight strands lying in every direction, the matted look of pressed wool."""
    canvas = np.zeros((size, size))
    x = rng.uniform(0, size, count)
    y = rng.uniform(0, size, count)
    angle = rng.uniform(0, np.pi, count)
    length = rng.integers(min_length, max_length, count)
    level = rng.uniform(0.3, 1.0, count)
    for step in range(max_length):
        alive = length > step
        np.add.at(canvas, ((y[alive] + np.sin(angle[alive]) * step).astype(int) % size, (x[alive] + np.cos(angle[alive]) * step).astype(int) % size), level[alive])
    return canvas


def normalise(image):
    return (image - image.min()) / (image.max() - image.min())


def felt_textures(folder):
    size = 1024

    # Fibres at two sizes, softened so individual strands blend into a fuzz
    fine = normalise(blur(scatter_fibres(size, 90000, 5, 16), 0.8))
    coarse = normalise(blur(scatter_fibres(size, 9000, 14, 40), 1.6))
    fibres = normalise(fine * 0.65 + coarse * 0.35)

    # Broad soft patches where the nap has been brushed different ways
    nap = normalise(tile_noise(size, 0.006) * 0.7 + tile_noise(size, 0.02) * 0.3)

    # Albedo is multiplied by the material colour, so it stays near white and only carries the variation.
    # The fibre shading is strong enough to read as cloth even where the surface grain map is not available.
    shade = np.clip(0.8 + 0.4 * (fibres - fibres.mean()) + 0.07 * (nap - 0.5), 0.55, 1.0)
    save_png(folder, "FeltAlbedo", np.repeat(shade[:, :, None], 3, axis=2))

    save_normal_map(folder, "FeltNormal", fibres, 5.0)


def save_normal_map(folder, name, height, strength):
    """Surface grain map from a height image, wrapping at the edges so it tiles."""
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * strength
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * strength
    normal = np.stack([-dx, -dy, np.ones_like(height)], axis=2)
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    save_png(folder, name, normal * 0.5 + 0.5)


def streaks(size, across, along):
    """Tileable noise stretched up the image: detail dies away at `across` side to side and at `along` top to bottom."""
    spectrum = np.fft.fft2(rng.standard_normal((size, size)))
    f_along = np.fft.fftfreq(size)[:, None]
    f_across = np.fft.fftfreq(size)[None, :]
    spectrum *= np.exp(-((f_across / across) ** 2 + (f_along / along) ** 2))
    return normalise(np.real(np.fft.ifft2(spectrum)))


def wood_textures(folder):
    """Straight grained hardwood, the grain running up the image. Tinted by each wood materials own colour."""
    size = 1024

    # Broad bands of lighter and darker growth, fine lines within them, and short dark pores
    bands = streaks(size, 0.02, 0.0015)
    lines = streaks(size, 0.2, 0.004)
    pores = np.zeros((size, size))
    count = 5000
    x = rng.integers(0, size, count)
    y = rng.integers(0, size, count)
    length = rng.integers(8, 45, count)
    for step in range(45):
        alive = length > step
        pores[(y[alive] + step) % size, x[alive]] = 1.0
    pores = blur(pores, 0.6)

    shade = np.clip(0.84 + 0.3 * (bands - 0.5) + 0.12 * (lines - 0.5) - 0.12 * pores, 0.5, 1.0)
    save_png(folder, "WoodAlbedo", np.repeat(shade[:, :, None], 3, axis=2))
    save_normal_map(folder, "WoodNormal", lines * 0.5 - pores * 0.5, 1.4)


def leather_textures(folder):
    """Pebbled leather for the padded rail."""
    size = 512

    # Rounded bumps of a fairly even size, with creases between them
    bumps = tile_noise(size, 0.07) - tile_noise(size, 0.02) * 0.6
    bumps = normalise(bumps)
    creases = np.clip((0.42 - bumps) * 4, 0.0, 1.0)

    shade = np.clip(0.9 + 0.12 * (bumps - 0.5) - 0.07 * creases, 0.5, 1.0)
    save_png(folder, "LeatherAlbedo", np.repeat(shade[:, :, None], 3, axis=2))
    save_normal_map(folder, "LeatherNormal", bumps, 2.5)


def main():
    root = sys.argv[sys.argv.index("--") + 1]
    audio = os.path.join(root, "Audio")
    textures = os.path.join(root, "Textures")
    os.makedirs(audio, exist_ok=True)
    os.makedirs(textures, exist_ok=True)

    for take in (1, 2, 3):
        save_wav(audio, f"BallRoll{take}", ball_roll(seed=100 + take), peak=0.6)
    save_wav(audio, "BallClack", ball_clack())
    save_wav(audio, "ChipPlace", chip_place(), peak=0.7)
    save_wav(audio, "ChipsSweep", chips_sweep(), peak=0.7)
    save_wav(audio, "Win", win(), peak=0.7)
    save_wav(audio, "Lose", lose(), peak=0.6)
    felt_textures(textures)
    wood_textures(textures)
    leather_textures(textures)


main()
