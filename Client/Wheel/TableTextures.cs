using UnityEngine;

namespace HideoutRoulette.Client.Wheel
{
    /// <summary>
    ///     Seven segment style lettering, drawn without needing a font asset from the game
    /// </summary>
    public static class SevenSegment
    {
        private const float CharWidth = 0.5f;
        private const float CharGap = 0.2f;
        private const float Thickness = 0.14f;

        /// <summary>
        ///     Is a point inside the strokes of the text. Coordinates are relative to the centre of the text.
        /// </summary>
        public static bool Hit(string text, float x, float y, float height)
        {
            var charWidth = CharWidth * height;
            var pitch = charWidth + CharGap * height;
            var totalWidth = text.Length * pitch - CharGap * height;

            var fromLeft = x + totalWidth / 2f;
            var fromBottom = y + height / 2f;
            if (fromLeft < 0f || fromLeft >= totalWidth || fromBottom < 0f || fromBottom > height)
            {
                return false;
            }

            var index = Mathf.Min((int)(fromLeft / pitch), text.Length - 1);
            var charX = fromLeft - index * pitch;
            if (charX > charWidth)
            {
                return false;
            }

            var segments = GetSegments(text[index]);
            var thickness = Thickness * height;
            var half = height / 2f;

            var left = charX <= thickness;
            var right = charX >= charWidth - thickness;
            var upper = fromBottom >= half;

            return (segments & 1) != 0 && fromBottom >= height - thickness // top
                || (segments & 2) != 0 && right && upper
                || (segments & 4) != 0 && right && !upper
                || (segments & 8) != 0 && fromBottom <= thickness // bottom
                || (segments & 16) != 0 && left && !upper
                || (segments & 32) != 0 && left && upper
                || (segments & 64) != 0 && Mathf.Abs(fromBottom - half) <= thickness / 2f; // middle
        }

        private static int GetSegments(char character)
        {
            switch (character)
            {
                case '0': return 63;
                case '1': return 6;
                case '2': return 91;
                case '3': return 79;
                case '4': return 102;
                case '5': return 109;
                case '6': return 125;
                case '7': return 7;
                case '8': return 127;
                case '9': return 111;
                case '-': return 64;
                case 'E': return 121;
                case 'U': return 62;
                case 'n': return 84;
                case 'o': return 92;
                case 'd': return 94;
                default: return 0;
            }
        }
    }

    public static class TableTextures
    {
        private const int FeltCellPixels = 64;
        private const int FeltBorderPixels = 2;
        private const float FeltTextHeight = 26f;

        private const int WheelPixels = 512;

        // Wheel rings as a fraction of the disc radius
        public const float PocketRingInner = 0.62f;
        public const float NumberRingInner = 0.8f;
        private const float NumberRingCentre = 0.9f;
        private const float WheelTextHeight = 0.085f;
        private const float SeparatorDegrees = 0.5f;

        private static readonly Color32 _line = new Color32(215, 200, 150, 255);
        private static readonly Color32 _text = new Color32(235, 235, 225, 255);
        private static readonly Color32 _wood = new Color32(70, 40, 20, 255);
        private static readonly Color32 _hub = new Color32(180, 150, 70, 255);

        /// <summary>
        ///     Betting layout as seen from above with the player at the bottom edge
        /// </summary>
        public static Texture2D CreateFelt(System.Collections.Generic.List<SpotDefinition> spots)
        {
            var width = RouletteLayout.GridColumns * FeltCellPixels;
            var height = RouletteLayout.GridRows * FeltCellPixels;
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = RouletteLayout.Felt;
            }

            foreach (var spot in spots)
            {
                var x0 = spot.Column * FeltCellPixels;
                var y0 = spot.Row * FeltCellPixels;
                var x1 = x0 + spot.ColumnSpan * FeltCellPixels;
                var y1 = y0 + spot.RowSpan * FeltCellPixels;
                var centreX = (x0 + x1) / 2f;
                var centreY = (y0 + y1) / 2f;

                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var onBorder =
                            x - x0 < FeltBorderPixels || x1 - 1 - x < FeltBorderPixels || y - y0 < FeltBorderPixels || y1 - 1 - y < FeltBorderPixels;

                        Color32 color;
                        if (onBorder)
                        {
                            color = _line;
                        }
                        else if (spot.FeltText != null && SevenSegment.Hit(spot.FeltText, x + 0.5f - centreX, y + 0.5f - centreY, FeltTextHeight))
                        {
                            color = _text;
                        }
                        else
                        {
                            color = spot.FeltColor;
                        }

                        pixels[y * width + x] = color;
                    }
                }
            }

            return CreateTexture(width, height, pixels);
        }

        /// <summary>
        ///     Top of the wheel disc. Pocket 0 of the wheel order sits on the textures up axis, the rest follow clockwise.
        /// </summary>
        public static Texture2D CreateWheel()
        {
            var pixels = new Color32[WheelPixels * WheelPixels];
            var half = WheelPixels / 2f;

            for (var py = 0; py < WheelPixels; py++)
            {
                for (var px = 0; px < WheelPixels; px++)
                {
                    var x = (px + 0.5f - half) / half;
                    var y = (py + 0.5f - half) / half;
                    pixels[py * WheelPixels + px] = GetWheelColor(x, y);
                }
            }

            return CreateTexture(WheelPixels, WheelPixels, pixels);
        }

        private static Color32 GetWheelColor(float x, float y)
        {
            var radius = Mathf.Sqrt(x * x + y * y);
            if (radius < 0.16f)
            {
                return _hub;
            }

            if (radius < PocketRingInner)
            {
                return _wood;
            }

            // Clockwise from the up axis, matching how the disc mesh is unwrapped
            var angle = Mathf.Repeat(Mathf.Atan2(x, y) * Mathf.Rad2Deg + RouletteLayout.PocketAngle / 2f, 360f);
            var slot = Mathf.Min((int)(angle / RouletteLayout.PocketAngle), RouletteLayout.PocketCount - 1);
            var offsetDegrees = angle - (slot + 0.5f) * RouletteLayout.PocketAngle;

            if (RouletteLayout.PocketAngle / 2f - Mathf.Abs(offsetDegrees) < SeparatorDegrees || Mathf.Abs(radius - NumberRingInner) < 0.008f)
            {
                return _line;
            }

            var number = RouletteLayout.WheelOrder[slot];
            var color = RouletteLayout.GetColor(number);
            if (radius < NumberRingInner)
            {
                // Pockets the ball lands in, darker than the number band above them
                return new Color32((byte)(color.r * 0.6f), (byte)(color.g * 0.6f), (byte)(color.b * 0.6f), 255);
            }

            // Lettering runs along the rim with its top edge outwards
            var along = offsetDegrees * Mathf.Deg2Rad * radius;
            var outward = radius - NumberRingCentre;

            return SevenSegment.Hit(number.ToString(), along, outward, WheelTextHeight) ? _text : color;
        }

        private static Texture2D CreateTexture(int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);

            return texture;
        }
    }
}
