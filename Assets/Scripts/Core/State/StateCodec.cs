using System;
using System.Globalization;
using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// Conversions between live values and their saved form. Every number that is written goes
    /// through here in the invariant culture: a device set to Russian writes "0,5" otherwise, and
    /// the same file then fails to read on a device set to English.
    /// </summary>
    public static class StateCodec
    {
        public static string FormatDecimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        public static bool TryParseDecimal(string text, out decimal value)
        {
            return decimal.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static string FormatColor(Color32 color) =>
            "#" + color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2");

        public static bool TryParseColor(string text, out Color32 color)
        {
            color = new Color32(0, 0, 0, 255);
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var hex = text[0] == '#' ? text.Substring(1) : text;
            if (hex.Length != 6 ||
                !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                return false;
            }

            color = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
            return true;
        }

        public static ColoringDto ToDto(in ColoringSettings settings)
        {
            return new ColoringDto
            {
                smooth = settings.Smooth,
                mode = (int)settings.Mode,
                cycleLength = settings.CycleLength,
                offset = settings.Offset,
                interior = FormatColor(settings.InteriorColor)
            };
        }

        public static ColoringSettings FromDto(ColoringDto dto)
        {
            var result = ColoringSettings.Default;
            if (dto == null)
            {
                return result;
            }

            result.Smooth = dto.smooth;
            result.Mode = Enum.IsDefined(typeof(ColoringMode), dto.mode) ? (ColoringMode)dto.mode : result.Mode;
            result.CycleLength = dto.cycleLength > 0f ? dto.cycleLength : result.CycleLength;
            result.Offset = dto.offset;
            if (TryParseColor(dto.interior, out var interior))
            {
                result.InteriorColor = interior;
            }

            return result.Sanitized();
        }

        public static PaletteDto ToDto(PaletteData palette)
        {
            var stops = palette.Stops;
            var dto = new PaletteDto
            {
                id = palette.Id,
                name = palette.DisplayName,
                stops = new PaletteStopDto[stops.Count]
            };

            for (var i = 0; i < stops.Count; i++)
            {
                dto.stops[i] = new PaletteStopDto { position = stops[i].Position, color = FormatColor(stops[i].Color) };
            }

            return dto;
        }

        /// <summary>Null when the DTO has no usable stops.</summary>
        public static PaletteData FromDto(PaletteDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.id) || dto.stops == null || dto.stops.Length == 0)
            {
                return null;
            }

            var stops = new PaletteData.ColorStop[dto.stops.Length];
            for (var i = 0; i < stops.Length; i++)
            {
                var source = dto.stops[i];
                TryParseColor(source?.color, out var color);
                stops[i] = new PaletteData.ColorStop(source?.position ?? 0f, color);
            }

            return PaletteData.FromStops(dto.id, string.IsNullOrEmpty(dto.name) ? dto.id : dto.name, stops);
        }

        /// <summary>
        /// Bring an older saved state up to <see cref="FractalStateDto.CurrentVersion"/>, in place.
        /// Null if unreadable. A file from a newer build is read as far as it goes.
        /// </summary>
        public static FractalStateDto Upgrade(FractalStateDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.fractal))
            {
                return null;
            }

            // Version 0 is a file written before the field existed, which is version 1's shape.
            if (dto.version < 2)
            {
                // Version 2 turned the Burning Ship the way the WPF engine draws it, masts up - the
                // same set mirrored top to bottom. A view saved of the old one shows the same place
                // once it is mirrored too: y and the rotation change sign.
                if (dto.fractal == MirroredInVersion2)
                {
                    MirrorVertically(dto);
                }

                dto.version = 2;
            }

            return dto;
        }

        /// <summary>The fractal whose picture version 2 mirrored. A historical fact, not a list to extend.</summary>
        private const string MirroredInVersion2 = "burning-ship";

        private static void MirrorVertically(FractalStateDto dto)
        {
            if (TryParseDecimal(dto.centerY, out var y))
            {
                dto.centerY = FormatDecimal(-y);
            }

            dto.rotation = double.IsNaN(dto.rotation) ? 0d : -dto.rotation;
        }

        public static string ToJson(object value) => JsonUtility.ToJson(value);

        public static bool TryFromJson<T>(string json, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                value = JsonUtility.FromJson<T>(json);
                return value != null;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
